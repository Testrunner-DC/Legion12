// Run through verified SSH on the test host; never emit credentials or auth responses.
import fs from 'node:fs'
import assert from 'node:assert/strict'

assert.equal(process.getuid(), 0, 'root-only test environment verification')
const [commit] = process.argv.slice(2)
assert.match(commit || '', /^[a-f0-9]{40}$/)
const envPath = '/etc/legion12-testrun.env'
assert(!fs.lstatSync(envPath).isSymbolicLink(), 'test environment must not be a link')
assert.equal(fs.statSync(envPath).mode & 0o077, 0, 'test environment must be private')
const source = fs.readFileSync(envPath, 'utf8')
const value = key => {
  const lines = source.split(/\r?\n/).filter(line => line.startsWith(`${key}=`))
  assert.equal(lines.length, 1, `expected a unique ${key}`)
  return lines[0].slice(key.length + 1).trim().replace(/^(['"])(.*)\1$/, '$2')
}
assert.equal(value('L12_PUBLIC_BASE_URL'), 'https://legion-12.com/testrun')
const base = 'http://127.0.0.1:8084'
const healthResponse = await fetch(`${base}/health`, { redirect: 'error', signal: AbortSignal.timeout(15000) })
assert.equal(healthResponse.status, 200, 'loopback test health unavailable')
assert.equal((await healthResponse.json()).serverVersion, commit, 'loopback mode must bind to the public test candidate')
let token
try {
  const login = await fetch(`${base}/api/auth/login`, {
    method: 'POST', redirect: 'error', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'Admin', password: value('L12_ADMIN_PASSWORD') }),
    signal: AbortSignal.timeout(15000),
  })
  assert.equal(login.status, 200, 'test bootstrap account unavailable; do not reset credentials')
  token = (await login.json()).token
  assert.equal(typeof token, 'string')
  const response = await fetch(`${base}/api/admin/storage/private-deck-persistence`, {
    redirect: 'error', headers: { Authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(15000),
  })
  assert.equal(response.status, 200, 'protected test mode read failed')
  assert.match(response.headers.get('cache-control') || '', /no-store/)
  const status = await response.json()
  assert.deepEqual(status, { configuredEnabled: true, effectiveMode: 'object', storageMode: 'sqlite', writable: true, fallbackMirrorHealthy: true, restartRequiredForChange: true })
  const logout = await fetch(`${base}/api/auth/sessions/current`, { method: 'DELETE', redirect: 'error', headers: { Authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(15000) })
  assert(logout.ok, 'test verification session cleanup failed')
  token = undefined
  console.log(JSON.stringify({ status: 'passed', commit, ...status }))
} finally {
  if (token) {
    const logout = await fetch(`${base}/api/auth/sessions/current`, { method: 'DELETE', redirect: 'error', headers: { Authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(15000) })
    assert(logout.ok, 'test verification session cleanup failed')
  }
}
