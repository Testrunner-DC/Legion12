import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const dependencyRoot = path.resolve(process.env.L12_PLATFORM_DEPENDENCY_ROOT || root)
const requireDependency = createRequire(path.join(dependencyRoot, 'package.json'))
const ts = requireDependency('typescript')
const { reactive } = requireDependency('vue')
const platformFile = path.join(root, 'src/l12/platform.ts')
const reliabilityFile = path.join(root, 'src/l12/platformRequestReliability.ts')
const source = fs.readFileSync(platformFile, 'utf8')
const reliabilitySource = fs.readFileSync(reliabilityFile, 'utf8')
const tree = ts.createSourceFile(platformFile, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
assert.equal(tree.parseDiagnostics.length, 0, 'platform.ts must parse before the boundary tests run')

const declarationNames = new Set([
  'PlatformRequestError',
  'PLATFORM_MAX_CONCURRENT_REQUESTS', 'PLATFORM_READ_TIMEOUT_MS', 'PLATFORM_MUTATION_TIMEOUT_MS',
  'PLATFORM_UPLOAD_TIMEOUT_MS', 'PLATFORM_READ_MAX_ATTEMPTS', 'platformRequestCoordinator',
  'platformSessionVersion', 'platformState', 'authState', 'authRefreshPromise', 'authRefreshSessionVersion',
  'AUTH_REFRESH_RETRY_BASE_MS', 'AUTH_REFRESH_REQUEST_TIMEOUT_MS', 'AUTH_REFRESH_RETRY_MAX_MS',
  'authRefreshRetryTimer', 'authRefreshRetryAttempts', 'clearAuthRefreshRetry', 'isTemporaryAuthFailure',
  'platformSessionIsCurrent', 'scheduleAuthRefreshRetry', 'retryAfterMilliseconds', 'retryableReadFailure',
  'requestFingerprint', 'privateDeckConflictRevision', 'platformRequest', 'remember', 'refreshCurrentAccount',
  'forgetAccount', 'logout', 'changeUsername',
])
const requiredNames = new Set([
  'PlatformRequestError', 'PLATFORM_MAX_CONCURRENT_REQUESTS', 'PLATFORM_READ_TIMEOUT_MS',
  'PLATFORM_MUTATION_TIMEOUT_MS', 'PLATFORM_UPLOAD_TIMEOUT_MS', 'PLATFORM_READ_MAX_ATTEMPTS',
  'platformRequestCoordinator', 'platformSessionVersion', 'platformState', 'authState', 'authRefreshPromise',
  'AUTH_REFRESH_RETRY_BASE_MS', 'AUTH_REFRESH_REQUEST_TIMEOUT_MS', 'AUTH_REFRESH_RETRY_MAX_MS',
  'authRefreshRetryTimer', 'authRefreshRetryAttempts', 'clearAuthRefreshRetry', 'isTemporaryAuthFailure',
  'scheduleAuthRefreshRetry', 'retryAfterMilliseconds', 'retryableReadFailure', 'requestFingerprint',
  'platformRequest', 'remember', 'refreshCurrentAccount', 'forgetAccount', 'logout', 'changeUsername',
])
function declaredNames(node) {
  if ((ts.isFunctionDeclaration(node) || ts.isClassDeclaration(node)) && node.name) return [node.name.text]
  if (ts.isVariableStatement(node)) return node.declarationList.declarations.map(item => item.name.getText(tree))
  return []
}
const selected = tree.statements.filter(node => declaredNames(node).some(name => declarationNames.has(name)))
const selectedNames = new Set(selected.flatMap(declaredNames))
for (const name of requiredNames) assert.ok(selectedNames.has(name), `platform.ts must expose actual ${name} source`)
const compiled = ts.transpileModule(selected.map(node => node.getText(tree)).join('\n'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText
const reliabilityCompiled = ts.transpileModule(reliabilitySource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const reliability = await import(`data:text/javascript;base64,${Buffer.from(reliabilityCompiled).toString('base64')}`)

function deferred() {
  let resolve
  let reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const json = (status, body = {}, headers = {}) => new Response(JSON.stringify(body), {
  status,
  headers: { 'Content-Type': 'application/json', ...headers },
})
const account = (id, username = id) => ({ id, username })
const settle = promise => promise.then(value => ({ status: 'fulfilled', value }), reason => ({ status: 'rejected', reason }))
async function until(predicate, message) {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    if (predicate()) return
    await new Promise(resolve => setTimeout(resolve, 1))
  }
  assert.fail(message)
}

function actualRuntime(initialFetch = async () => json(200, {})) {
  const store = new Map()
  const localStorage = {
    getItem: key => store.get(key) ?? null,
    setItem: (key, value) => store.set(key, String(value)),
    removeItem: key => store.delete(key),
  }
  let fetcher = initialFetch
  let disconnectCalls = 0
  const fetchCalls = []
  const fetch = async (url, init) => {
    fetchCalls.push({ url: String(url), init })
    return fetcher(String(url), init)
  }
  const deps = {
    reactive,
    localStorage,
    loadAccount: () => null,
    createRequestCoordinator: reliability.createRequestCoordinator,
    RequestDeadlineError: reliability.RequestDeadlineError,
    fetch,
    apiBase: () => 'http://synthetic.invalid',
    disconnect: () => { disconnectCalls += 1 },
    l12State: { nickname: '' },
    window: { setTimeout, clearTimeout },
  }
  const runtime = new Function('deps', `const {${Object.keys(deps).join(',')}}=deps;const exports={};${compiled};return {
    PlatformRequestError,
    platformRequest,
    remember,
    refreshCurrentAccount,
    forgetAccount,
    logout,
    changeUsername,
    scheduleAuthRefreshRetry,
    platformState: exports.platformState,
    authState: exports.authState,
    sessionVersion: () => platformSessionVersion,
    authRefreshPromise: () => authRefreshPromise,
  }`)(deps)
  return {
    ...runtime,
    setFetch(value) { fetcher = value },
    fetchCalls,
    disconnectCalls: () => disconnectCalls,
    stored: key => store.get(key),
  }
}

let passed = 0
let failed = 0
const failures = []
async function check(label, run) {
  try {
    await run()
    passed += 1
    console.log(`PASS ${passed + failed}: ${label}`)
  } catch (error) {
    failed += 1
    failures.push({ label, error: error?.stack || String(error) })
    console.error(`FAIL ${passed + failed}: ${label}\n${error?.stack || error}`)
  }
}

for (const status of [401, 200, 403]) await check(`same-token A-B-A rejects a late authenticated ${status} response`, async () => {
  const response = deferred()
  const runtime = actualRuntime(() => response.promise)
  runtime.remember(account('A', 'old-A'), 'same-token')
  const pending = settle(runtime.platformRequest(`/api/private/late-${status}`, { reliability: { maxAttempts: 1 } }))
  await until(() => runtime.fetchCalls.length === 1, 'authenticated request did not start')
  runtime.remember(account('B'), 'token-B')
  runtime.remember(account('A', 'current-A'), 'same-token')
  response.resolve(status === 200 ? json(200, { value: 'old' }) : json(status, { code: `late_${status}` }))
  const result = await pending
  await new Promise(resolve => setTimeout(resolve, 0))
  assert.equal(result.status, 'rejected')
  assert.ok(result.reason instanceof runtime.PlatformRequestError)
  assert.equal(result.reason.code, 'stale_session')
  assert.equal(runtime.platformState.account?.username, 'current-A')
  assert.equal(runtime.platformState.token, 'same-token')
  assert.equal(runtime.authState.verified, true)
  assert.equal(runtime.disconnectCalls(), 0)
  assert.equal(runtime.fetchCalls.length, 1)
})

await check('a queued request rechecks the session generation before using a released slot', async () => {
  const blockers = Array.from({ length: 4 }, deferred)
  const runtime = actualRuntime((url) => {
    const index = runtime.fetchCalls.length - 1
    if (index < blockers.length) return blockers[index].promise
    return json(200, { url })
  })
  runtime.remember(account('A'), 'same-token')
  const active = blockers.map((_, index) => settle(runtime.platformRequest(`/api/private/queue-${index}`, { reliability: { maxAttempts: 1 } })))
  const queued = settle(runtime.platformRequest('/api/private/queue-last', { reliability: { maxAttempts: 1 } }))
  await until(() => runtime.fetchCalls.length === 4, 'coordinator did not fill its four bounded slots')
  runtime.remember(account('B'), 'token-B')
  runtime.remember(account('A'), 'same-token')
  blockers[0].resolve(json(200, { slot: 0 }))
  const queuedResult = await queued
  for (let index = 1; index < blockers.length; index += 1) blockers[index].resolve(json(200, { slot: index }))
  await Promise.all(active)
  assert.equal(queuedResult.status, 'rejected')
  assert.equal(queuedResult.reason?.code, 'stale_session')
  assert.equal(runtime.fetchCalls.length, 4)
})

await check('a delayed read retry rechecks the session generation before its next fetch', async () => {
  const first = deferred()
  const runtime = actualRuntime(() => runtime.fetchCalls.length === 1
    ? first.promise : json(200, { value: 'retried-old-session' }))
  runtime.remember(account('A'), 'same-token')
  const pending = settle(runtime.platformRequest('/api/private/retry-aba', { reliability: { maxAttempts: 2 } }))
  await until(() => runtime.fetchCalls.length === 1, 'first retryable read did not start')
  first.resolve(json(503, { code: 'temporary' }, { 'Retry-After': '0.05' }))
  await new Promise(resolve => setTimeout(resolve, 5))
  runtime.remember(account('B'), 'token-B')
  runtime.remember(account('A'), 'same-token')
  const result = await pending
  assert.equal(result.status, 'rejected')
  assert.equal(result.reason?.code, 'stale_session')
  assert.equal(runtime.fetchCalls.length, 1)
})

await check('same-session GET keeps the bounded retry behavior', async () => {
  const runtime = actualRuntime(() => runtime.fetchCalls.length === 1
    ? json(503, { code: 'temporary' }, { 'Retry-After': '0.001' })
    : json(200, { value: 'fresh' }))
  runtime.remember(account('A'), 'token-A')
  const result = await runtime.platformRequest('/api/private/retry-ok', { reliability: { maxAttempts: 2 } })
  assert.deepEqual(result, { value: 'fresh' })
  assert.equal(runtime.fetchCalls.length, 2)
})

await check('coordinator still limits distinct in-flight requests to four', async () => {
  const blockers = Array.from({ length: 4 }, deferred)
  const runtime = actualRuntime(() => {
    const index = runtime.fetchCalls.length - 1
    return index < 4 ? blockers[index].promise : json(200, { slot: index })
  })
  runtime.remember(account('A'), 'token-A')
  const requests = Array.from({ length: 5 }, (_, index) => settle(runtime.platformRequest(`/api/private/bounded-${index}`, {
    reliability: { maxAttempts: 1 },
  })))
  await until(() => runtime.fetchCalls.length === 4, 'four requests did not acquire the bounded slots')
  await new Promise(resolve => setTimeout(resolve, 5))
  assert.equal(runtime.fetchCalls.length, 4)
  blockers[0].resolve(json(200, { slot: 0 }))
  await until(() => runtime.fetchCalls.length === 5, 'queued request did not start after a slot was released')
  for (let index = 1; index < blockers.length; index += 1) blockers[index].resolve(json(200, { slot: index }))
  const results = await Promise.all(requests)
  assert.equal(results.filter(result => result.status === 'fulfilled').length, 5)
})

await check('mutations never retry even when a caller asks for two attempts', async () => {
  const runtime = actualRuntime(() => json(503, { code: 'temporary' }))
  runtime.remember(account('A'), 'token-A')
  const result = await settle(runtime.platformRequest('/api/private/mutation', {
    method: 'POST', body: '{}', reliability: { maxAttempts: 2 },
  }))
  assert.equal(result.status, 'rejected')
  assert.equal(result.reason?.status, 503)
  assert.equal(runtime.fetchCalls.length, 1)
  assert.equal(runtime.platformState.account?.id, 'A')
})

await check('a current authenticated 401 logs out exactly once without retry', async () => {
  const runtime = actualRuntime(() => json(401, {}))
  runtime.remember(account('A'), 'token-A')
  const result = await settle(runtime.platformRequest('/api/private/unauthorized', { reliability: { maxAttempts: 2 } }))
  assert.equal(result.status, 'rejected')
  assert.equal(result.reason?.status, 401)
  assert.equal(runtime.fetchCalls.length, 1)
  assert.equal(runtime.platformState.account, null)
  assert.equal(runtime.platformState.token, '')
  assert.equal(runtime.disconnectCalls(), 1)
})

await check('a current authenticated 403 fails permissions closed and refreshes the same session once', async () => {
  const runtime = actualRuntime((url) => url.endsWith('/api/auth/me')
    ? json(200, account('A', 'refreshed-A'))
    : json(403, { code: 'forbidden' }))
  runtime.remember(account('A', 'cached-A'), 'token-A')
  const result = await settle(runtime.platformRequest('/api/private/forbidden', { reliability: { maxAttempts: 2 } }))
  assert.equal(result.status, 'rejected')
  assert.equal(result.reason?.status, 403)
  await until(() => runtime.fetchCalls.length === 2 && runtime.authState.verified,
    '403 did not complete one authoritative account refresh')
  assert.equal(runtime.fetchCalls.filter(call => call.url.endsWith('/api/auth/me')).length, 1)
  assert.equal(runtime.platformState.account?.username, 'refreshed-A')
  assert.equal(runtime.platformState.token, 'token-A')
  assert.equal(runtime.disconnectCalls(), 0)
})

await check('a 404 is not retried and does not clear the current session', async () => {
  const runtime = actualRuntime(() => json(404, { code: 'not_found' }))
  runtime.remember(account('A'), 'token-A')
  const result = await settle(runtime.platformRequest('/api/private/missing', { reliability: { maxAttempts: 2 } }))
  assert.equal(result.status, 'rejected')
  assert.equal(result.reason?.status, 404)
  assert.equal(runtime.fetchCalls.length, 1)
  assert.equal(runtime.platformState.account?.id, 'A')
  assert.equal(runtime.platformState.token, 'token-A')
  assert.equal(runtime.disconnectCalls(), 0)
})

await check('public GET remains anonymous and its 401 cannot log out a current account', async () => {
  const runtime = actualRuntime(() => json(401, {}))
  runtime.remember(account('A'), 'token-A')
  const result = await settle(runtime.platformRequest('/api/public/example', { reliability: { maxAttempts: 1 } }))
  assert.equal(result.reason?.status, 401)
  assert.equal(runtime.platformState.account?.id, 'A')
  assert.equal(runtime.disconnectCalls(), 0)
  const request = runtime.fetchCalls[0].init
  assert.equal(request.headers.has('Authorization'), false)
  assert.equal(request.credentials, 'omit')
  assert.equal(request.cache, 'no-store')
})

await check('login remains an anonymous credential exchange and its 401 cannot log out a current account', async () => {
  const runtime = actualRuntime(() => json(401, {}))
  runtime.remember(account('A'), 'token-A')
  const result = await settle(runtime.platformRequest('/api/auth/login', {
    method: 'POST', body: '{}', reliability: { maxAttempts: 2 },
  }))
  assert.equal(result.reason?.status, 401)
  assert.equal(runtime.platformState.account?.id, 'A')
  assert.equal(runtime.disconnectCalls(), 0)
  assert.equal(runtime.fetchCalls.length, 1)
  assert.equal(runtime.fetchCalls[0].init.headers.has('Authorization'), false)
})

const conflictCases = [
  ['valid private read conflict', '/api/decks/by-id/deck-A?expectedRevision=1', 'GET', 409, 'deck_revision_conflict', 2, 2],
  ['valid private update conflict', '/api/decks/by-id/deck-A', 'PUT', 409, 'deck_revision_conflict', 3, 3],
  ['missing revision', '/api/decks/by-id/deck-A', 'DELETE', 409, 'deck_revision_conflict', undefined, undefined],
  ['string revision', '/api/decks/by-id/deck-A', 'PUT', 409, 'deck_revision_conflict', '4', undefined],
  ['zero revision', '/api/decks/by-id/deck-A', 'PUT', 409, 'deck_revision_conflict', 0, undefined],
  ['fractional revision', '/api/decks/by-id/deck-A', 'PUT', 409, 'deck_revision_conflict', 1.5, undefined],
  ['unsafe revision', '/api/decks/by-id/deck-A', 'PUT', 409, 'deck_revision_conflict', Number.MAX_SAFE_INTEGER + 1, undefined],
  ['wrong error code', '/api/decks/by-id/deck-A', 'PUT', 409, 'storage_conflict', 5, undefined],
  ['wrong endpoint', '/api/admin/content/deck-A', 'PUT', 409, 'deck_revision_conflict', 6, undefined],
  ['wrong method', '/api/decks/by-id/deck-A', 'POST', 409, 'deck_revision_conflict', 7, undefined],
  ['wrong status', '/api/decks/by-id/deck-A', 'PUT', 400, 'deck_revision_conflict', 8, undefined],
]
for (const [label, requestPath, method, status, code, currentRevision, expected] of conflictCases) {
  await check(`currentRevision boundary: ${label}`, async () => {
    const runtime = actualRuntime(() => json(status, {
      code, message: 'synthetic conflict', currentRevision, arbitrarySecret: 'must-not-copy',
    }))
    runtime.remember(account('A'), 'token-A')
    const result = await settle(runtime.platformRequest(requestPath, {
      method, ...(method === 'GET' ? {} : { body: '{}' }), reliability: { maxAttempts: 1 },
    }))
    assert.equal(result.status, 'rejected')
    assert.equal(result.reason?.currentRevision, expected)
    assert.equal(result.reason?.arbitrarySecret, undefined)
  })
}

await check('a stale forget operation cannot clear the same token in a newer generation', () => {
  const runtime = actualRuntime()
  runtime.remember(account('A', 'old-A'), 'same-token')
  const oldVersion = runtime.sessionVersion()
  runtime.remember(account('B'), 'token-B')
  runtime.remember(account('A', 'current-A'), 'same-token')
  runtime.forgetAccount('same-token', oldVersion)
  assert.equal(runtime.platformState.account?.username, 'current-A')
  assert.equal(runtime.platformState.token, 'same-token')
  assert.equal(runtime.disconnectCalls(), 0)
})

await check('a stale logout completion cannot clear the same token in a newer generation', async () => {
  const response = deferred()
  const runtime = actualRuntime(() => response.promise)
  runtime.remember(account('A', 'old-A'), 'same-token')
  const pending = settle(runtime.logout())
  await until(() => runtime.fetchCalls.length === 1, 'logout revocation did not start')
  runtime.remember(account('B'), 'token-B')
  runtime.remember(account('A', 'current-A'), 'same-token')
  response.resolve(new Response(null, { status: 204 }))
  const result = await pending
  assert.equal(result.status, 'rejected')
  assert.equal(result.reason?.code, 'stale_session')
  assert.equal(runtime.platformState.account?.username, 'current-A')
  assert.equal(runtime.platformState.token, 'same-token')
  assert.equal(runtime.disconnectCalls(), 0)
})

await check('refresh deduplication is generation-scoped and an old finally cannot clear the new pending refresh', async () => {
  const replies = []
  const runtime = actualRuntime(() => {
    const reply = deferred()
    replies.push(reply)
    return reply.promise
  })
  runtime.remember(account('A', 'old-A'), 'same-token')
  const first = runtime.refreshCurrentAccount({ force: true })
  await until(() => replies.length === 1, 'old refresh did not start')
  runtime.remember(account('B'), 'token-B')
  runtime.remember(account('A', 'current-A'), 'same-token')
  const second = runtime.refreshCurrentAccount({ force: true })
  await new Promise(resolve => setTimeout(resolve, 0))
  const callsWhenSecondRequested = replies.length
  replies[0].resolve(json(200, account('A', 'stale-refresh')))
  await settle(first)
  const third = runtime.refreshCurrentAccount({ force: true })
  await new Promise(resolve => setTimeout(resolve, 0))
  const callsAfterOldFinally = replies.length
  const currentReply = replies.at(-1)
  currentReply.resolve(json(200, account('A', 'fresh-refresh')))
  await Promise.all([settle(second), settle(third)])
  assert.equal(callsWhenSecondRequested, 2)
  assert.notEqual(first, second)
  assert.equal(third, second)
  assert.equal(callsAfterOldFinally, 2)
  assert.equal(runtime.platformState.account?.username, 'fresh-refresh')
  assert.equal(runtime.authState.verified, true)
  assert.equal(runtime.authState.refreshing, false)
})

const report = {
  schema: 1,
  source: platformFile.replaceAll('\\', '/'),
  sourceSha256: crypto.createHash('sha256').update(source).digest('hex'),
  coordinatorSha256: crypto.createHash('sha256').update(reliabilitySource).digest('hex'),
  actualSelectedSourceDeclarations: selected.length,
  realProductionFunctions: true,
  realRequestCoordinator: true,
  realVueReactive: true,
  syntheticFetchAndStorage: true,
  networkRequests: 0,
  productionWrites: 0,
  cases: passed + failed,
  passed,
  failed,
  failureLabels: failures.map(item => item.label),
}
console.log(JSON.stringify(report, null, 2))
if (failed) process.exitCode = 1
