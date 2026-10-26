"""Bounded external HTTP driver. Private readiness/credentials stay in pipe memory."""
import argparse
import concurrent.futures
import hashlib
import json
import math
import os
import pathlib
import shutil
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

FAILURE_PHASE = None


class RejectRedirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None


def private_opener():
    return urllib.request.build_opener(urllib.request.ProxyHandler({}), RejectRedirects())


def validate_payload(group, wrong, status, payload, raw, ready, retry):
    if group == 'reader':
        require(status == 200 and payload.get('id') == ready['readerId'])
    elif wrong:
        require(status in (401, 429, 503))
    else:
        require(status in (200, 503))
    if status == 200 and group != 'reader':
        require(isinstance(payload.get('token'), str) and bool(payload['token']))
        require(payload.get('account', {}).get('id') == ready['ownerId'])
        require(not any(key in raw.decode().lower() for key in ('passwordhash', 'passworditerations', 'salt')))
    code = payload.get('code') if status != 200 else None
    require(code in (None, 'authentication_failed', 'authentication_busy', 'login_rate_limited', 'rate_limited'))
    if status == 503:
        require(code == 'authentication_busy' and retry == 1)
    if status == 401:
        require(wrong and code == 'authentication_failed')
    if status == 429:
        require(retry is not None and retry > 0)
    require(status in (200, 401, 429, 503))
    return code


def require(value):
    if not value:
        raise RuntimeError('qualification-boundary')


def child_environment(temp):
    env = {'PATH': os.path.dirname(os.path.realpath(ARGS.dotnet)),
           'TEMP': str(temp), 'TMP': str(temp), 'TMPDIR': str(temp),
           'L12_LISTEN_HOST': '127.0.0.1', 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1'}
    # Explicit platform-only whitelist; never copy production bootstrap or hosting settings.
    if os.name == 'nt':
        for key in ('SystemRoot', 'WINDIR'):
            if key in os.environ:
                env[key] = os.environ[key]
    return env


def live_guard():
    if not ARGS.live_guard:
        return None
    result = []
    for path, unit in [('/health', 'legion12-test.service'), ('/testrun/health', 'legion12-testrun.service')]:
        start = time.monotonic()
        with urllib.request.urlopen('https://legion-12.com' + path, timeout=3) as response:
            data = json.loads(response.read(1024 * 1024))
        props = dict(line.split('=', 1) for line in subprocess.check_output(
            ['/usr/bin/systemctl', 'show', unit, '-p', 'MainPID', '-p', 'NRestarts', '-p', 'ActiveState'], text=True, timeout=3).splitlines())
        require(data.get('serverVersion') == '97a7a71b0f84ac83b4bbcd9a90e95c08d54d88bf')
        require(props.get('ActiveState') == 'active' and props.get('NRestarts') == '0')
        result.append({'unit': unit, 'version': data['serverVersion'], 'maintenance': data.get('maintenance'),
                       'process': props, 'elapsedMs': round((time.monotonic() - start) * 1000, 3)})
    return result


def guard_identity(rows):
    return [{key: value for key, value in row.items() if key != 'elapsedMs'} for row in rows] if rows else None


def run_case(iterations):
    global FAILURE_PHASE
    root = pathlib.Path(tempfile.mkdtemp(prefix='l12-auth-http-', dir=ARGS.temp_base)).resolve()
    require(root.parent == pathlib.Path(ARGS.temp_base).resolve() and not root.is_symlink())
    command = [ARGS.dotnet, ARGS.probe, '--server-root', ARGS.server_root,
               '--server-sha256', ARGS.server_sha256, '--sandbox-root', str(root), '--iterations', str(iterations)]
    if os.name != 'nt':
        command = ['/usr/bin/nice', '-n', '19'] + command
    process = subprocess.Popen(command, cwd=root, env=child_environment(ARGS.temp_base),
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                               creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    case_start = time.monotonic()
    succeeded = False
    try:
        with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pipe:
            first = pipe.submit(process.stdout.readline)
            try:
                ready = json.loads(first.result(timeout=15))
            except Exception:
                process.kill()
                raise RuntimeError('private-host-start-failed') from None
        if ready.get('kind') == 'failure':
            FAILURE_PHASE = {key: ready.get(key) for key in ('stage', 'errorType')}
        require(ready.get('kind') == 'ready' and ready.get('iterations') == iterations)
        require(len(ready['accounts']) == 8 and len({a['ownerId'] for a in ready['accounts']}) == 8)
        address = urllib.parse.urlparse(ready['address'])
        require(address.scheme == 'http' and address.hostname == '127.0.0.1' and address.port > 0)
        samples = []
        guards = []
        baseline_guard = live_guard()
        if baseline_guard:
            guards.append(baseline_guard)

        def request(group, wave=0, wrong=False, gate=None, slot=0):
            if gate:
                gate.wait(timeout=3)
            require(time.monotonic() - case_start < 35)
            start = time.monotonic()
            if group == 'reader':
                request = urllib.request.Request(ready['address'] + 'api/auth/me',
                    headers={'Authorization': 'Bearer ' + ready['readerToken'], 'Accept-Encoding': 'identity'})
            else:
                account = ready['accounts'][slot]
                data = json.dumps({'username': account['username'], 'password': ready['password'] + ('wrong' if wrong else '')}).encode()
                request = urllib.request.Request(ready['address'] + 'api/auth/login', data=data,
                    headers={'Content-Type': 'application/json', 'Accept-Encoding': 'identity'})
            try:
                response = private_opener().open(request, timeout=5)
            except urllib.error.HTTPError as error:
                response = error
            with response:
                raw = response.read(65537)
                require(len(raw) <= 65536)
                require(response.geturl() == request.full_url)
                payload = json.loads(raw)
                status = response.code
                retry = response.headers.get('Retry-After')
                retry = int(retry) if retry is not None else None
                identity = {'ownerId': ready['accounts'][slot]['ownerId'], 'readerId': ready['readerId']}
                code = validate_payload(group, wrong, status, payload, raw, identity, retry)
                row = {'group': group, 'wave': wave, 'status': status, 'code': code,
                       'retryAfter': retry, 'elapsedMs': round((time.monotonic() - start) * 1000, 3),
                       'bytes': len(raw), 'limit': response.headers.get('RateLimit-Limit')}
                if status == 200 and group != 'reader':
                    check_start = time.monotonic()
                    token_request = urllib.request.Request(ready['address'] + 'api/auth/me',
                        headers={'Authorization': 'Bearer ' + payload['token'], 'Accept-Encoding': 'identity'})
                    with private_opener().open(token_request, timeout=5) as check:
                        require(check.geturl() == token_request.full_url and check.status == 200)
                        require(json.loads(check.read(65536)).get('id') == identity['ownerId'])
                    row['tokenVerified'] = True
                    row['tokenCheckMs'] = round((time.monotonic() - check_start) * 1000, 3)
                return row

        samples.append(request('warmup'))
        require(samples[-1]['status'] == 200)
        for _ in range(4):
            samples.append(request('serial'))
            require(samples[-1]['status'] == 200)
            samples.append(request('reader'))
        for size in (1, 2, 4, 8):
            gate = threading.Event()
            with concurrent.futures.ThreadPoolExecutor(max_workers=18) as pool:
                futures = [pool.submit(request, 'burst', size, False, gate, slot) for slot in range(size)]
                futures += [pool.submit(request, 'reader', size, False, gate) for _ in range(10)]
                gate.set()
                samples.extend(f.result(timeout=7) for f in futures)
            guard = live_guard()
            require(guard_identity(guard) == guard_identity(baseline_guard))
            if guard:
                guards.append(guard)
        # Six real bad-password requests exercise the durable failure buckets.
        for _ in range(6):
            samples.append(request('wrong-password', wrong=True))
        for _ in range(6):
            samples.append(request('traffic-limit', wrong=True))
        require(any(r['status'] == 401 for r in samples))
        require(any(r['code'] == 'login_rate_limited' for r in samples))
        require(samples[-1]['status'] == 429 and samples[-1]['code'] == 'rate_limited')
        process.stdin.write('finish\n')
        process.stdin.flush()
        remaining, error_output = process.communicate(timeout=12)
        require(process.returncode == 0 and not error_output)
        finished = json.loads(remaining.strip())
        require(finished.get('kind') == 'finished' and finished.get('credentialsUnchanged') is True
                and finished.get('syntheticSessionsRevoked') is True and finished.get('serverStopped') is True)
        require(time.monotonic() - case_start < 50)
        result = {'iterations': iterations, 'samples': samples, 'liveGuards': guards,
                  'finished': finished, 'durationMs': round((time.monotonic() - case_start) * 1000, 3),
                  'clientSeparateProcess': True, 'privateLoopbackHost': True,
                  'totalHttpRequests': len(samples) + sum(r.get('tokenVerified', False) for r in samples)}
        succeeded = True
        return result
    finally:
        if process.poll() is None:
            process.kill()
            process.wait(timeout=5)
        if succeeded:
            # Only the exact newly created, exited, purely synthetic private tree is disposable.
            require(root.parent == pathlib.Path(ARGS.temp_base).resolve() and root.name.startswith('l12-auth-http-'))
            require(not root.is_symlink() and all(not p.is_symlink() for p in root.rglob('*')))
            shutil.rmtree(root)
        # Failure data stays private under the governed temporary base, never copied into evidence.


def main():
    global ARGS
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'probe', 'server-root', 'server-sha256', 'temp-base'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--live-guard', action='store_true')
    ARGS = parser.parse_args()
    ARGS.server_sha256 = ARGS.server_sha256.upper()
    for value in (ARGS.dotnet, ARGS.probe, ARGS.server_root, ARGS.temp_base):
        require(pathlib.Path(value).is_absolute())
    require(hashlib.sha256((pathlib.Path(ARGS.server_root) / 'GrandUMIServer.dll').read_bytes()).hexdigest().upper() == ARGS.server_sha256)
    start = time.monotonic()
    before = live_guard()
    cases = [run_case(cost) for cost in (120000, 600000)]
    after = live_guard()
    require(guard_identity(before) == guard_identity(after))
    require(time.monotonic() - start < 100)
    print(json.dumps({'schema': 'l12-password-http-v1', 'scope': 'real-http-original-binary-private-synthetic-loopback',
                      'serverSha256': ARGS.server_sha256, 'probeSha256': hashlib.sha256(pathlib.Path(ARGS.probe).read_bytes()).hexdigest(),
                      'cases': cases, 'before': before, 'after': after, 'existingAccountWrites': 0,
                      'productionBusinessOrStatisticsWrites': 0, 'higherCostWritesEnabled': False,
                      'persistentMigrationPerformed': False, 'syntheticTreesRemoved': True,
                      'publicTlsGatewayMeasured': False, 'full30Plus10Claimed': False,
                      'durationMs': round((time.monotonic() - start) * 1000, 3)}, ensure_ascii=False))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'schema': 'l12-password-http-v1', 'status': 'failed', 'errorType': type(error).__name__, 'privateHostFailure': FAILURE_PHASE}))
        raise SystemExit(1)
