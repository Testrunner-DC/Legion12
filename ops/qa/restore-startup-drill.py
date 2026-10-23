"""One private, loopback-only application restore; never restore a live path."""
import base64
import ctypes
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import signal
import socket
import sqlite3
import subprocess
import tarfile
import time
import types
import urllib.request

ROOT = Path('/tmp/l12-restore-start-20261024-ba083305-r3')
COMMIT = '3a376f17e5a6f9431303b35775977fabd2083b6a'
DLL_SHA = 'BAEFF29E79B3DE4AF855151D166ABD89EB415D3426D40762E1860A8DA5FFF171'
ARCHIVE_SHA = 'D12D8C9B51FC28FD9D21A43CFB101051880CE8929242FEF349AAFFD4D5981956'
OWNER = {'schema': 1, 'purpose': 'isolated-startup-restore', 'application': COMMIT, 'archive': ARCHIVE_SHA}


def sha(file):
    with Path(file).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def available():
    return int(dict((row.split(':', 1)[0], row.split(':', 1)[1].strip().split()[0])
                    for row in Path('/proc/meminfo').read_text().splitlines())['MemAvailable']) * 1024


def collection_hash(key, value):
    if key == 'Accounts':
        # AccountRow.FontSize was added after this fixed Oct19 archive. The
        # actual accepted application assigns "standard" when it is absent.
        # Do not erase or normalize any explicit preference or other field.
        value = [dict(row) for row in value]
        for row in value:
            row.setdefault('FontSize', 'standard')
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def facts(runtime):
    result = {}
    for name in ('platform.db', 'matches.db'):
        db = sqlite3.connect((runtime / name).as_uri() + '?mode=ro', uri=True, timeout=1)
        try:
            db.execute('PRAGMA query_only=ON')
            assert db.execute('PRAGMA integrity_check').fetchall() == [('ok',)], 'RESTORED_INTEGRITY'
            tables = {row[0] for row in db.execute("SELECT name FROM sqlite_schema WHERE type='table'")}
            counts, digests = {}, {}
            if name == 'matches.db':
                for table in ('matches', 'match_events'):
                    assert table in tables
                    rows = db.execute('SELECT * FROM ' + table).fetchall()
                    counts[table] = len(rows)
                    # Original rows are synthetic test records. Emit only counts
                    # and hashes, never room IDs, commands, hands or player text.
                    encoded = sorted(json.dumps(row, separators=(',', ':'), default=lambda x: x.hex()) for row in rows)
                    digests[table] = hashlib.sha256('\n'.join(encoded).encode()).hexdigest()
            else:
                row = db.execute('SELECT snapshot_json FROM platform_state WHERE singleton_id=1').fetchone()
                state = json.loads(row[0])
                # DataFile persistence uses its declared PascalCase names,
                # not the camelCase HTTP projection. F2 decks live in tables.
                for key in ('Accounts', 'Friends', 'BugReports', 'Articles', 'SiteMedia'):
                    value = state.get(key)
                    assert isinstance(value, (dict, list)), 'PLATFORM_BUSINESS_COLLECTION_MISSING'
                    if isinstance(value, (dict, list)):
                        counts[key] = len(value)
                        digests[key] = collection_hash(key, value)
                for table in ('account_decks', 'deck_payloads', 'published_decks'):
                    assert table in tables
                    hashes = []
                    for item in db.execute('SELECT * FROM ' + table):
                        encoded = json.dumps(item, separators=(',', ':'), default=lambda x: x.hex()).encode()
                        hashes.append(hashlib.sha256(encoded).hexdigest())
                    counts[table] = len(hashes)
                    digests[table] = hashlib.sha256('\n'.join(sorted(hashes)).encode()).hexdigest()
            result[name] = {'integrity': 'ok', 'counts': counts, 'businessHashes': digests}
        finally:
            db.close()
    return result


def run(p):
    assert socket.gethostname() == 'ser550468033481' and os.geteuid() == 0
    assert ROOT.parent == Path('/tmp') and ROOT.resolve() == ROOT and ROOT.is_dir() and not ROOT.is_symlink()
    assert ROOT.stat().st_uid == 0 and ROOT.stat().st_mode & 0o777 == 0o700, 'PRIVATE_OWNER_MODE_REQUIRED'
    assert json.loads((ROOT / 'owner.json').read_text()) == OWNER
    archive = ROOT / 'source.tar.gz'
    assert archive.is_file() and not archive.is_symlink() and archive.stat().st_size == 11221965
    assert sha(archive) == ARCHIVE_SHA
    assert available() >= 1024 ** 3 and shutil.disk_usage(ROOT).free >= 8 * 1024 ** 3 + 512 * 1024 ** 2
    source = Path('/opt/legion12-testrun/publish').resolve()
    assert str(source) == '/opt/legion12-testrun-releases/' + COMMIT + '-20261023T205850Z/publish'
    assert sha(source / 'GrandUMIServer.dll') == DLL_SHA
    app = ROOT / 'app'
    app.mkdir(mode=0o700)
    child = None
    started = time.monotonic()
    outcome = {'schema': 1, 'status': 'APPLICATION_RESTORE_NOT_ACCEPTED', 'formalWrites': 0,
               'liveDatabaseRestores': 0, 'serviceChanges': 0, 'originalArchiveSha256': ARCHIVE_SHA,
               'applicationCommit': COMMIT, 'applicationDllSha256': DLL_SHA}
    try:
        copied, copied_bytes = 0, 0
        for directory, dirs, files in os.walk(source, followlinks=False):
            relative = Path(directory).relative_to(source)
            if relative == Path('.'):
                dirs[:] = [name for name in dirs if name != 'runtime']
            for name in dirs:
                assert not (Path(directory) / name).is_symlink(), 'PROGRAM_LINK'
            target_dir = app / relative
            target_dir.mkdir(parents=True, exist_ok=True)
            for name in files:
                original = Path(directory) / name
                assert original.is_file() and not original.is_symlink(), 'PROGRAM_NOT_REGULAR'
                copied += 1
                copied_bytes += original.stat().st_size
                assert copied <= 1000 and copied_bytes <= 512 * 1024 ** 2, 'PROGRAM_COPY_BOUND'
                target = target_dir / name
                shutil.copyfile(original, target)
                assert target.is_file() and not target.is_symlink()
                assert sha(target) == sha(original), 'PROGRAM_COPY_SHA'
        runtime = app / 'runtime'
        runtime.mkdir(mode=0o700)
        seen, expanded = set(), 0
        with tarfile.open(archive, 'r:gz') as bundle:
            for member in bundle:
                posix = PurePosixPath(member.name)
                assert not posix.is_absolute() and '..' not in posix.parts
                assert member.isdir() or member.isfile(), 'ARCHIVE_LINK_OR_SPECIAL'
                if str(posix) == '.':
                    assert member.isdir()
                    continue
                key = str(posix)
                assert key not in seen
                seen.add(key)
                expanded += member.size
                assert len(seen) <= 10000 and 0 <= member.size <= 512 * 1024 ** 2 and expanded <= 1024 ** 3
                target = runtime.joinpath(*posix.parts)
                assert target.resolve().is_relative_to(runtime.resolve())
                if member.isdir():
                    target.mkdir(parents=True, exist_ok=True)
                    assert target.is_dir() and not target.is_symlink()
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with bundle.extractfile(member) as incoming, target.open('xb') as outgoing:
                        shutil.copyfileobj(incoming, outgoing)
        verifier_source = base64.b64decode(p['verifier'])
        assert hashlib.sha256(verifier_source).hexdigest().upper() == 'FC37461942E342477EBC2708A0C6EA92F06FDE13063366847D2563FDBC52F6F0'
        verifier = types.ModuleType('isolated_existing_verifier')
        exec(compile(verifier_source, '<accepted-backup-verifier>', 'exec'), verifier.__dict__)
        proof = verifier.verify(runtime, archive, ARCHIVE_SHA.lower(), test_only=True)
        assert proof['verified'] and proof['persistentFactMutations'] == 0
        before = facts(runtime)
        assert before['matches.db']['counts'] == {'matches': 3, 'match_events': 9}
        assert before['platform.db']['counts'], 'PLATFORM_BUSINESS_BASELINE_REQUIRED'
        with socket.socket() as reservation:
            reservation.bind(('127.0.0.1', 0))
            port = reservation.getsockname()[1]
        assert port not in (8083, 8084)
        environment = {'PATH': '/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin',
                       'HOME': str(ROOT), 'TMPDIR': str(ROOT), 'DOTNET_CLI_HOME': str(ROOT),
                       'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1', 'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
                       'L12_LISTEN_HOST': '127.0.0.1', 'L12_PRIVATE_DECK_OBJECT_PERSISTENCE': 'true',
                       'L12_DEPLOYMENT_DRAIN': 'true', 'L12_CHAT_ENABLED': 'false'}
        assert 'L12_TESTRUN_MATCH_STORAGE' not in environment
        def owned_child():
            os.nice(19)
            assert ctypes.CDLL(None).prctl(1, signal.SIGKILL) == 0
        with (ROOT / 'private-app.log').open('xb') as log:
            child = subprocess.Popen(['/usr/bin/dotnet', str(app / 'GrandUMIServer.dll'), str(port)],
                                     cwd=app, env=environment, stdout=log, stderr=log,
                                     start_new_session=True, preexec_fn=owned_child)
            health, peak_rss = None, 0
            health_deadline = min(started + 30, time.monotonic() + 15)
            while time.monotonic() < health_deadline:
                assert child.poll() is None, 'ISOLATED_APPLICATION_EXITED'
                status = dict(row.split(':', 1) for row in Path('/proc/' + str(child.pid) + '/status').read_text().splitlines() if ':' in row)
                rss = int(status.get('VmRSS', '0 kB').split()[0]) * 1024
                peak_rss = max(peak_rss, rss)
                assert rss < 512 * 1024 ** 2 and available() >= 768 * 1024 ** 2, 'SHARED_HOST_RESOURCE_RESERVE'
                try:
                    with urllib.request.urlopen('http://127.0.0.1:' + str(port) + '/health', timeout=.5) as response:
                        health = json.load(response)
                    break
                except OSError:
                    time.sleep(.2)
            assert health and health.get('service') == 'twelve-legions', 'ISOLATED_HEALTH_NOT_READY'
            assert health.get('serverVersion') == COMMIT and health.get('engineVersion') == 'l12-engine/' + COMMIT
            listeners = subprocess.check_output(['ss', '-H', '-ltnp', 'sport = :' + str(port)], text=True, timeout=2)
            assert '127.0.0.1:' + str(port) in listeners and 'pid=' + str(child.pid) + ',' in listeners
            assert '0.0.0.0:' + str(port) not in listeners and '[::]:' + str(port) not in listeners
            during = facts(runtime)
            assert during == before, 'STARTUP_CHANGED_BACKUP_BUSINESS_FACTS'
            child.send_signal(signal.SIGINT)
            child.wait(timeout=5)
            assert child.returncode == 0, 'ISOLATED_SHUTDOWN_REJECTED'
        after = facts(runtime)
        assert after == before and sha(archive) == ARCHIVE_SHA
        outcome.update({'status': 'ISOLATED_APPLICATION_STARTUP_RESTORE_VERIFIED', 'loopbackOnly': True,
                        'ephemeralMatchDeletionEnabled': False, 'healthIdentityMatched': True,
                        'businessBeforeDuringAfterEqual': True, 'restoredBusiness': before,
                        'legacyCompatibility': 'Only an absent AccountRow.FontSize is projected as the accepted default standard; explicit preferences and all other values remain exact.',
                        'programFilesCopied': copied, 'programBytesCopied': copied_bytes,
                        'expandedRuntimeBytes': expanded, 'peakIsolatedRssBytes': peak_rss,
                        'privateAppLogSha256': sha(ROOT / 'private-app.log'), 'privateAppLogEmitted': False,
                        'originalArchiveUnchanged': True, 'isolatedApplicationExitCode': child.returncode})
    except Exception as error:
        reason = str(error)
        outcome['reason'] = reason if reason.isupper() and reason.replace('_', '').isalnum() and len(reason) < 100 else type(error).__name__
    finally:
        if child is not None and child.poll() is None:
            child.kill()
            child.wait(timeout=3)
        outcome['isolatedChildExited'] = child is None or child.poll() is not None
        outcome['elapsedMs'] = round((time.monotonic() - started) * 1000, 3)
    # Failure keeps the private copy for bounded inspection. Success removes
    # only this newly-created, exact owned directory; E's source is untouched.
    if outcome['status'] == 'ISOLATED_APPLICATION_STARTUP_RESTORE_VERIFIED':
        assert ROOT.resolve() == ROOT and ROOT.parent == Path('/tmp') and json.loads((ROOT / 'owner.json').read_text()) == OWNER
        shutil.rmtree(ROOT)
        assert not ROOT.exists()
        outcome['disposableCopyRemoved'] = True
    else:
        outcome['disposableCopyRemoved'] = False
    return outcome
