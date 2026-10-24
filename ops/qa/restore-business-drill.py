"""Private synthetic backup -> restore -> actual application startup/readback.

Never selects a running database, credentials, maintenance or systemd operation.
"""
import base64
import ctypes
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import resource
import shutil
import signal
import socket
import subprocess
import tarfile
import time
import types
import urllib.request

COMMIT = "3a376f17e5a6f9431303b35775977fabd2083b6a"
DLL_SHA = "BAEFF29E79B3DE4AF855151D166ABD89EB415D3426D40762E1860A8DA5FFF171"
PROGRAM_ROOT = "/opt/legion12-testrun-releases/" + COMMIT + "-20261023T205850Z/publish"
VERIFIER_SHA = "FC37461942E342477EBC2708A0C6EA92F06FDE13063366847D2563FDBC52F6F0"


def available():
    return int(next(line.split()[1] for line in Path("/proc/meminfo").read_text().splitlines()
        if line.startswith("MemAvailable:"))) * 1024


def child_policy():
    os.nice(19)
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    if ctypes.CDLL(None).prctl(1, signal.SIGKILL) != 0: raise RuntimeError("PARENT_DEATH_GUARD_FAILED")


def durable_proof(file, value):
    raw = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    with file.open("xb") as stream:
        stream.write(raw); stream.flush(); os.fsync(stream.fileno())
    descriptor = os.open(file.parent, os.O_RDONLY | os.O_DIRECTORY)
    try: os.fsync(descriptor)
    finally: os.close(descriptor)
    return hashlib.sha256(raw).hexdigest()


def program_manifest(source, facts):
    """Ignore only the known live runtime link; never enumerate or copy it."""
    result, total = {}, 0
    for directory, directories, files in os.walk(source, followlinks=False):
        directory = Path(directory)
        if directory == source and "runtime" in directories:
            runtime = directory / "runtime"
            facts.require(runtime.is_symlink() and runtime.resolve() == Path("/opt/legion12-testrun-runtime"), "LIVE_RUNTIME_LINK_IDENTITY")
            directories.remove("runtime")
        for name in directories: facts.plain(directory / name, True)
        for name in files:
            file = directory / name
            info = facts.plain(file)
            total += info.st_size
            facts.require(len(result) < 1000 and total <= 128 * 1024**2, "PROGRAM_COPY_BUDGET")
            result[file.relative_to(source).as_posix()] = {"bytes": info.st_size, "sha256": hashlib.sha256(file.read_bytes()).hexdigest()}
    return result


def run(p):
    facts = types.ModuleType("owned_restore_business_facts")
    facts_raw = base64.b64decode(p["factsSource"], validate=True)
    assert hashlib.sha256(facts_raw).hexdigest().upper() == p["factsSourceSha256"]
    exec(compile(facts_raw, "<owned-restore-facts>", "exec"), facts.__dict__)
    require = facts.require
    require(socket.gethostname() == "ser550468033481" and os.geteuid() == 0, "RESTORE_HOST_IDENTITY")
    require(re.fullmatch(r"[0-9a-f]{32}", p["runId"]), "RESTORE_RUN_ID")
    root = Path("/tmp") / ("l12-restore-business-" + p["runId"])
    require(root.parent.resolve() == Path("/tmp") and not root.parent.is_symlink()
        and not os.path.lexists(root), "FRESH_PRIVATE_ROOT_REQUIRED")
    require(available() >= 1024**3 and shutil.disk_usage(root.parent).free >= 8 * 1024**3 + 512 * 1024**2,
        "SHARED_HOST_RESOURCE_RESERVE")
    os.umask(0o077)
    root.mkdir(mode=0o700)
    owner = {"schema": 1, "purpose": "nonempty-business-restore", "runId": p["runId"], "commit": COMMIT, "dllSha256": DLL_SHA}
    (root / "owner.json").write_text(json.dumps(owner))
    started, deadline, child = time.monotonic(), time.monotonic() + 40, None
    outcome = {"schema": 1, "status": "NONEMPTY_RESTORE_NOT_ACCEPTED", "formalWrites": 0,
        "liveDatabaseRestores": 0, "serviceChanges": 0, "applicationCommit": COMMIT, "applicationDllSha256": DLL_SHA}
    try:
        source = Path("/opt/legion12-testrun/publish").resolve()
        require(str(source) == PROGRAM_ROOT, "ACCEPTED_PROGRAM_ROOT_CHANGED")
        actual = program_manifest(source, facts)
        require(actual == p["programManifest"], "PROGRAM_ARCHIVE_MANIFEST_MISMATCH")
        app = root / "app"
        app.mkdir()
        for name, value in actual.items():
            target = app / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source / name, target)
        require(facts.tree_manifest(app) == actual, "PROGRAM_COPY_BYTES")
        tools = root / "tools"
        tools.mkdir()
        require(set(p["tools"]) == {"RestoreBusinessFixture.dll", "RestoreBusinessFixture.deps.json", "RestoreBusinessFixture.runtimeconfig.json"}, "FIXTURE_TOOL_SET")
        for name, value in p["tools"].items():
            raw = base64.b64decode(value["base64"], validate=True)
            require(0 < len(raw) <= 256 * 1024 and hashlib.sha256(raw).hexdigest().upper() == value["sha256"], "FIXTURE_TOOL_BYTES")
            (tools / name).write_bytes(raw)
        for relative, name in (("GrandUMIServer.dll", "GrandUMIServer.dll"),
            ("runtimes/linux-x64/native/libe_sqlite3.so", "libe_sqlite3.so")):
            require(relative in actual, "FIXTURE_NATIVE_DEPENDENCY_MISSING")
            shutil.copyfile(app / relative, tools / name)
        image = base64.b64decode(p["image"], validate=True)
        require(0 < len(image) <= 256 * 1024 and hashlib.sha256(image).hexdigest() == p["imageSha256"], "PUBLIC_IMAGE_BYTES")
        (root / "fixture.webp").write_bytes(image)
        (root / "source").mkdir()
        (root / "restored").mkdir()
        secret = "Qa-restore-" + os.urandom(20).hex() + "!aA9"
        environment = {"PATH": "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
            "HOME": str(root), "TMPDIR": str(root), "DOTNET_CLI_HOME": str(root), "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
            "LD_LIBRARY_PATH": str(tools) + ":" + str(app / "runtimes/linux-x64/native") + ":" + str(app),
            "L12_ADMIN_PASSWORD": secret, "L12_LISTEN_HOST": "127.0.0.1", "L12_PRIVATE_DECK_OBJECT_PERSISTENCE": "true",
            "L12_DEPLOYMENT_DRAIN": "true", "L12_CHAT_ENABLED": "false"}
        require("L12_TESTRUN_MATCH_STORAGE" not in environment, "EPHEMERAL_DELETION_FORBIDDEN")
        tool = tools / "RestoreBusinessFixture.dll"
        def invoke(mode):
            log_file = root / (mode[2:] + ".private.log")
            require(time.monotonic() < deadline, "RESTORE_DEADLINE")
            with log_file.open("xb") as log:
                result = subprocess.run(["/usr/bin/dotnet", str(tool), mode, p["runId"], COMMIT, DLL_SHA, p["imageSha256"]],
                    env=environment, cwd=tools, stdout=log, stderr=log, preexec_fn=child_policy, timeout=min(12, deadline - time.monotonic()))
            require(result.returncode == 0 and log_file.stat().st_size <= 65536, "SYNTHETIC_TOOL_EXECUTION_FAILED")
            payload = json.loads(log_file.read_text().splitlines()[-1])
            require(payload["status"] == ("SYNTHETIC_BUSINESS_SEEDED" if mode == "--seed" else "RESTORED_BUSINESS_API_READBACK_VERIFIED"), "SYNTHETIC_TOOL_RESULT")
            return payload
        seeded = invoke("--seed")
        runtime = root / "source" / "runtime"
        files = facts.tree_manifest(runtime)
        before = facts.database_facts(runtime, deadline)
        coverage = facts.nonempty_coverage(before, files)
        archive = root / "synthetic-runtime.tar.gz"
        with tarfile.open(archive, "w:gz", format=tarfile.USTAR_FORMAT) as bundle:
            for name in sorted(files): bundle.add(runtime / name, arcname=name, recursive=False)
        archive_sha = hashlib.sha256(archive.read_bytes()).hexdigest()
        verifier_raw = base64.b64decode(p["verifier"], validate=True)
        require(hashlib.sha256(verifier_raw).hexdigest().upper() == VERIFIER_SHA, "EXISTING_VERIFIER_CHANGED")
        verifier = types.ModuleType("existing_stopped_runtime_verifier")
        exec(compile(verifier_raw, "<existing-stopped-runtime-verifier>", "exec"), verifier.__dict__)
        proof = verifier.verify(runtime, archive, archive_sha, test_only=True)
        require(proof["verified"] and proof["persistentFactMutations"] == 0, "STOPPED_RAW_PROOF_REJECTED")
        restored = root / "restored" / "runtime"
        restored.mkdir()
        seen = set()
        with tarfile.open(archive, "r:gz") as bundle:
            for member in bundle:
                name = str(PurePosixPath(member.name))
                require(member.isfile() and name in files and name not in seen and member.size == files[name]["bytes"], "RESTORE_ARCHIVE_ENTRY_REJECTED")
                seen.add(name)
                target = restored / name
                require(target.resolve().is_relative_to(restored), "RESTORE_ARCHIVE_PATH")
                target.parent.mkdir(parents=True, exist_ok=True)
                with bundle.extractfile(member) as incoming, target.open("xb") as outgoing: shutil.copyfileobj(incoming, outgoing)
        require(seen == set(files) and facts.tree_manifest(restored) == files, "RESTORED_FILES_NOT_EXACT")
        restored_before = facts.database_facts(restored, deadline)
        require(restored_before == before, "RESTORED_DATABASE_FACTS_CHANGED")
        readback = invoke("--verify")
        require(facts.database_facts(restored, deadline) == before, "STORE_READBACK_CHANGED_BUSINESS_FACTS")
        # This is the existing verifier's actual structured function return,
        # not fabricated historical stdout. Persist it before application start.
        checkpoint_sha = durable_proof(root / "prepared-proof.json", {"owner": owner, "coverage": coverage,
            "seedReadback": seeded, "restoredReadback": readback, "archiveSha256": archive_sha,
            "verifierSourceSha256": VERIFIER_SHA, "stoppedBackupVerification": proof,
            "databaseFacts": before, "snapshotFiles": files})
        outcome.update(coverage=coverage, sourceSeedReadback=seeded, restoredStoreReadback=readback,
            stoppedBackupVerification=proof, preparedProofSha256=checkpoint_sha, preparedProofDurableBeforeAppStart=True)
        # Use the same accepted application, not a replacement persistence reader.
        app_runtime = app / "runtime"
        require(not os.path.lexists(app_runtime), "APP_RUNTIME_ALREADY_EXISTS")
        restored.rename(app_runtime)
        with socket.socket() as reservation:
            reservation.bind(("127.0.0.1", 0)); port = reservation.getsockname()[1]
        require(port not in (8083, 8084), "LIVE_PORT_FORBIDDEN")
        with (root / "application.private.log").open("xb") as log:
            child = subprocess.Popen(["/usr/bin/dotnet", str(app / "GrandUMIServer.dll"), str(port)], cwd=app,
                env=environment, stdout=log, stderr=log, start_new_session=True, preexec_fn=child_policy)
            health, peak = None, 0
            while time.monotonic() < min(deadline - 5, started + 30):
                require(child.poll() is None and available() >= 768 * 1024**2, "ISOLATED_CHILD_EXIT_OR_RESOURCE_RESERVE")
                rss = int(next(row.split()[1] for row in Path("/proc/" + str(child.pid) + "/status").read_text().splitlines() if row.startswith("VmRSS:"))) * 1024
                peak = max(peak, rss)
                require(rss < 512 * 1024**2, "ISOLATED_CHILD_MEMORY")
                try:
                    with urllib.request.urlopen("http://127.0.0.1:" + str(port) + "/health", timeout=.5) as response: health = json.load(response)
                    break
                except OSError: time.sleep(.2)
            require(health and health.get("service") == "twelve-legions" and health.get("serverVersion") == COMMIT, "APPLICATION_HEALTH_IDENTITY")
            listeners = subprocess.check_output(["ss", "-H", "-ltnp", "sport = :" + str(port)], text=True, timeout=2)
            durable_proof(root / "listener-proof.json", {"port": port, "pid": child.pid, "actualSsOutput": listeners})
            facts.require_owned_loopback_listener(listeners, port, child.pid)
            during = facts.database_facts(app_runtime, deadline)
            startup_metadata = facts.require_started_business_preserved(before, during)
            child.send_signal(signal.SIGINT); child.wait(timeout=5)
            require(child.returncode == 0, "NORMAL_APPLICATION_SHUTDOWN_REQUIRED")
        after = facts.database_facts(app_runtime, deadline)
        media_after = {name: value for name, value in facts.tree_manifest(app_runtime).items() if name.startswith("site-media/")}
        shutdown_metadata = facts.require_started_business_preserved(before, after)
        require(after == during and media_after == {name: value for name, value in files.items() if name.startswith("site-media/")}
            and hashlib.sha256(archive.read_bytes()).hexdigest() == archive_sha, "APPLICATION_SHUTDOWN_OR_MEDIA_FACTS_CHANGED")
        outcome.update(status="NONEMPTY_SYNTHETIC_APPLICATION_RESTORE_VERIFIED", coverage=coverage,
            sourceSeedReadback=seeded, restoredStoreReadback=readback,
            allBusinessFactsPreservedBeforeDuringAfter=True, allPlatformSnapshotFieldsAndTablesExact=True,
            nonOperationalMatchTablesExact=True, startupSchemaMetadata=startup_metadata, shutdownSchemaMetadata=shutdown_metadata,
            allSnapshotFilesRestoredExactly=True, sourceDatabaseFacts=before, restoredDatabaseFacts=after,
            uploadedFilesBeforeDuringAfterEqual=True, originalSyntheticArchiveUnchanged=True,
            applicationNormalExitCode=child.returncode, loopbackOnly=True, peakIsolatedRssBytes=peak,
            applicationPrivateLogSha256=hashlib.sha256((root / "application.private.log").read_bytes()).hexdigest(), privateLogsEmitted=False)
    except Exception as error:
        message = str(error)
        outcome["reason"] = message if re.fullmatch(r"[A-Z][A-Z0-9_]{0,99}", message) else type(error).__name__
    finally:
        if child is not None and child.poll() is None: child.kill(); child.wait(timeout=3)
        outcome["isolatedChildExited"] = child is None or child.poll() is not None
        outcome["elapsedMs"] = round((time.monotonic() - started) * 1000, 3)
    if outcome["status"] == "NONEMPTY_SYNTHETIC_APPLICATION_RESTORE_VERIFIED":
        require(root.parent == Path("/tmp") and root.resolve() == root and json.loads((root / "owner.json").read_text()) == owner, "OWNED_CLEANUP_IDENTITY")
        shutil.rmtree(root)
        outcome["disposablePrivateCopyRemoved"] = not root.exists()
    else: outcome["disposablePrivateCopyRemoved"] = False
    return outcome
