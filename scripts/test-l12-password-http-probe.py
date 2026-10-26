"""Executable negative boundaries for the private real-HTTP qualification host."""
import argparse
import importlib.util
import json
import os
import pathlib
import subprocess
import tempfile
import types
import threading
import urllib.error
import http.server

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', required=True)
parser.add_argument('--probe', required=True)
parser.add_argument('--temp-base', required=True)
parser.add_argument('--report')
args = parser.parse_args()
driver_path = pathlib.Path(__file__).parent / 'qualification/password-http/drive.py'
spec = importlib.util.spec_from_file_location('password_http_driver', driver_path)
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)
driver.ARGS = types.SimpleNamespace(dotnet=args.dotnet)
env = driver.child_environment(args.temp_base)
results = []

def check(name, command, extra=None, passed=False):
    child_env = dict(env)
    child_env.update(extra or {})
    process = subprocess.run([args.dotnet, args.probe] + command, cwd=args.temp_base,
                             env=child_env, capture_output=True, text=True, timeout=8,
                             creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    value = json.loads(process.stdout)
    assert (process.returncode == 0) == passed, name
    assert value['kind'] == ('self-test' if passed else 'failure'), name
    assert not process.stderr, name
    assert 'credential-sentinel' not in process.stdout, name
    results.append({'name': name, 'passed': True})

check('explicit whitelist and loopback accepted', ['--self-test'], passed=True)
for key in ('L12_ADMIN_PASSWORD', 'L12_TESTRUN_SHARED_GALLERY', 'ASPNETCORE_URLS', 'Kestrel__Endpoints__Http__Url', 'PRIVATE_SECRET'):
    check('reject inherited ' + key, ['--self-test'], {key: 'credential-sentinel'})
check('reject public listening address', ['--self-test'], {'L12_LISTEN_HOST': '0.0.0.0'})
check('reject unknown CLI', ['--arbitrary', '1'])
check('reject duplicate CLI', ['--iterations', '120000'] * 4)
check('reject unsupported cost', ['--iterations', '999999', '--server-root', args.temp_base,
      '--server-sha256', '0' * 64, '--sandbox-root', args.temp_base])

# Empty/path checks execute before assembly resolution, without constructing any Store.
with tempfile.TemporaryDirectory(prefix='l12-auth-boundary-', dir=args.temp_base) as wrong_name:
    check('reject unowned sandbox name', ['--iterations', '120000', '--server-root', args.temp_base,
          '--server-sha256', '0' * 64, '--sandbox-root', wrong_name])
with tempfile.TemporaryDirectory(prefix='l12-auth-http-', dir=args.temp_base) as root:
    marker = pathlib.Path(root) / 'preserve.txt'
    marker.write_text('unchanged synthetic guard', encoding='utf8')
    check('reject nonempty sandbox', ['--iterations', '120000', '--server-root', args.temp_base,
          '--server-sha256', '0' * 64, '--sandbox-root', root])
    assert marker.read_text(encoding='utf8') == 'unchanged synthetic guard'

assert set(env) <= {'PATH', 'SystemRoot', 'WINDIR', 'TEMP', 'TMP', 'TMPDIR',
                   'L12_LISTEN_HOST', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO'}
assert 'L12_ADMIN_PASSWORD' not in env
assert 'os.environ.copy' not in driver_path.read_text(encoding='utf8')
results.append({'name': 'driver builds fresh environment, never copies inherited secrets', 'passed': True})

ready = {'ownerId': 'synthetic-owner', 'readerId': 'synthetic-reader'}
valid = {'account': {'id': ready['ownerId']}, 'token': 'synthetic-token'}
def oracle(name, group, wrong, status, payload, retry=None, accepted=False):
    try:
        driver.validate_payload(group, wrong, status, payload, json.dumps(payload).encode(), ready, retry)
        actual = True
    except RuntimeError:
        actual = False
    assert actual == accepted, name
    results.append({'name': name, 'passed': True})
oracle('correct account and token accepted', 'burst', False, 200, valid, accepted=True)
oracle('wrong account success rejected', 'burst', False, 200, {'account': {'id': 'other'}, 'token': 'synthetic'})
oracle('missing token rejected', 'burst', False, 200, {'account': {'id': ready['ownerId']}})
oracle('correct password 401 rejected', 'burst', False, 401, {'code': 'authentication_failed'})
oracle('wrong password 200 rejected', 'wrong-password', True, 200, valid)
oracle('wrong reader account rejected', 'reader', False, 200, {'id': 'other'})
oracle('correct reader accepted', 'reader', False, 200, {'id': ready['readerId']}, accepted=True)
oracle('bounded busy accepted separately', 'burst', False, 503, {'code': 'authentication_busy'}, 1, True)
oracle('busy without retry rejected', 'burst', False, 503, {'code': 'authentication_busy'})
oracle('storage503 never classified as auth busy', 'burst', False, 503, {'code': 'storage_unavailable'}, 1)
oracle('rate limit without retry rejected', 'traffic-limit', True, 429, {'code': 'rate_limited'})
oracle('leaked credential fields rejected', 'burst', False, 200, {**valid, 'PasswordHash': 'synthetic'})

hits = []
class TransportStub(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        hits.append(self.path)
        if self.path == '/redirect':
            self.send_response(302)
            self.send_header('Location', '/must-not-follow')
            self.end_headers()
        else:
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b'{}')
    def log_message(self, *unused):
        pass
with http.server.ThreadingHTTPServer(('127.0.0.1', 0), TransportStub) as stub:
    worker = threading.Thread(target=stub.serve_forever, daemon=True)
    worker.start()
    old_proxy = {key: os.environ.get(key) for key in ('HTTP_PROXY', 'http_proxy', 'NO_PROXY', 'no_proxy')}
    try:
        os.environ.update(HTTP_PROXY='http://127.0.0.1:1', http_proxy='http://127.0.0.1:1', NO_PROXY='', no_proxy='')
        opener = driver.private_opener()
        with opener.open(f'http://127.0.0.1:{stub.server_port}/direct', timeout=3) as response:
            assert response.status == 200
        try:
            opener.open(f'http://127.0.0.1:{stub.server_port}/redirect', timeout=3)
            raise AssertionError('redirect was followed')
        except urllib.error.HTTPError as error:
            assert error.code == 302
        assert hits == ['/direct', '/redirect']
        results.append({'name': 'private HTTP ignores proxy environment and refuses redirects', 'passed': True})
    finally:
        for key, value in old_proxy.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value
        stub.shutdown()
        worker.join(timeout=3)
output = {'schema': 'l12-password-http-boundaries-v1', 'passed': True, 'count': len(results),
          'cases': results, 'applicationServerStarted': False, 'localTransportStubStarted': True, 'productionInputs': False}
if args.report:
    pathlib.Path(args.report).write_text(json.dumps(output, indent=2), encoding='utf8')
print(json.dumps(output))
