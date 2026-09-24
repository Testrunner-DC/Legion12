import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'

const source = fs.readFileSync(new URL('../src/l12/platformRequestReliability.ts', import.meta.url), 'utf8')
const javascript = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { createRequestCoordinator, RequestDeadlineError } = await import(
  `data:text/javascript;base64,${Buffer.from(javascript).toString('base64')}`
)
const tick = () => new Promise(resolve => setTimeout(resolve, 0))
let checks = 0

{
  const coordinator = createRequestCoordinator(4)
  const releases = []
  let maximumActive = 0
  let active = 0
  const requests = Array.from({ length: 8 }, (_, index) => coordinator.execute({
    key: `read-${index}`, method: 'GET', timeoutMs: 1_000, maxAttempts: 1,
    shouldRetry: () => false, retryDelayMs: () => 0,
    run: async () => {
      active += 1
      maximumActive = Math.max(maximumActive, active)
      await new Promise(resolve => releases.push(resolve))
      active -= 1
      return index
    },
  }))
  await tick()
  assert.equal(coordinator.snapshot().active, 4); checks += 1
  assert.equal(coordinator.snapshot().queued, 4); checks += 1
  releases.splice(0).forEach(resolve => resolve())
  await tick()
  releases.splice(0).forEach(resolve => resolve())
  assert.deepEqual(await Promise.all(requests), [0, 1, 2, 3, 4, 5, 6, 7]); checks += 1
  assert.equal(maximumActive, 4); checks += 1
}

{
  const coordinator = createRequestCoordinator(4)
  let reads = 0
  let finish
  const request = {
    key: 'account-a\nGET\n/api/profile', method: 'GET', timeoutMs: 1_000, maxAttempts: 1,
    shouldRetry: () => false, retryDelayMs: () => 0,
    run: async () => { reads += 1; await new Promise(resolve => { finish = resolve }); return { ok: true } },
  }
  const first = coordinator.execute(request)
  const second = coordinator.execute(request)
  assert.equal(first, second); checks += 1
  await tick()
  assert.equal(reads, 1); checks += 1
  finish()
  assert.deepEqual(await second, { ok: true }); checks += 1
}

{
  const coordinator = createRequestCoordinator(4)
  let writes = 0
  let finish
  const request = {
    key: 'account-a\nPOST\n/api/action\n{"value":1}', method: 'POST', timeoutMs: 1_000, maxAttempts: 1,
    shouldRetry: () => true, retryDelayMs: () => 0,
    run: async () => { writes += 1; await new Promise(resolve => { finish = resolve }); return 'done' },
  }
  const first = coordinator.execute(request)
  const second = coordinator.execute(request)
  await tick()
  assert.equal(writes, 1); checks += 1
  finish()
  assert.equal(await first, 'done'); checks += 1
  assert.equal(await second, 'done'); checks += 1
}

{
  const coordinator = createRequestCoordinator(2)
  let attempts = 0
  const result = await coordinator.execute({
    key: 'retry-read', method: 'GET', timeoutMs: 1_000, maxAttempts: 3,
    shouldRetry: error => error instanceof TypeError, retryDelayMs: () => 0,
    run: async () => { attempts += 1; if (attempts < 3) throw new TypeError('offline'); return 'fresh' },
  })
  assert.equal(result, 'fresh'); checks += 1
  assert.equal(attempts, 3); checks += 1

  let mutationAttempts = 0
  await assert.rejects(coordinator.execute({
    key: 'one-write', method: 'POST', timeoutMs: 1_000, maxAttempts: 1,
    shouldRetry: () => true, retryDelayMs: () => 0,
    run: async () => { mutationAttempts += 1; throw new TypeError('offline') },
  }), TypeError)
  assert.equal(mutationAttempts, 1); checks += 1
}

{
  const coordinator = createRequestCoordinator(1)
  let observedAbort = false
  await assert.rejects(coordinator.execute({
    key: 'deadline', method: 'GET', timeoutMs: 10, maxAttempts: 1,
    shouldRetry: () => false, retryDelayMs: () => 0,
    run: signal => new Promise((_, reject) => signal.addEventListener('abort', () => {
      observedAbort = true
      reject(new DOMException('aborted', 'AbortError'))
    }, { once: true })),
  }), RequestDeadlineError)
  assert.equal(observedAbort, true); checks += 1
}

{
  const coordinator = createRequestCoordinator(1)
  let releaseFirst
  let queuedRuns = 0
  const first = coordinator.execute({
    key: 'first', method: 'GET', timeoutMs: 1_000, maxAttempts: 1,
    shouldRetry: () => false, retryDelayMs: () => 0,
    run: () => new Promise(resolve => { releaseFirst = resolve }),
  })
  await tick()
  const controller = new AbortController()
  const queued = coordinator.execute({
    method: 'GET', signal: controller.signal, timeoutMs: 1_000, maxAttempts: 1,
    shouldRetry: () => false, retryDelayMs: () => 0,
    run: async () => { queuedRuns += 1; return 'unexpected' },
  })
  controller.abort()
  await assert.rejects(queued, error => error?.name === 'AbortError')
  assert.equal(queuedRuns, 0); checks += 1
  releaseFirst('released')
  assert.equal(await first, 'released'); checks += 1
}

{
  const coordinator = createRequestCoordinator(1)
  let releaseFirst
  let queuedRuns = 0
  const first = coordinator.execute({
    key: 'queue-holder', method: 'GET', timeoutMs: 1_000, maxAttempts: 1,
    shouldRetry: () => false, retryDelayMs: () => 0,
    run: () => new Promise(resolve => { releaseFirst = resolve }),
  })
  await tick()
  await assert.rejects(coordinator.execute({
    method: 'GET', timeoutMs: 10, maxAttempts: 1,
    shouldRetry: () => false, retryDelayMs: () => 0,
    run: async () => { queuedRuns += 1; return 'unexpected' },
  }), RequestDeadlineError)
  assert.equal(queuedRuns, 0); checks += 1
  releaseFirst('released')
  await first
}

{
  const coordinator = createRequestCoordinator(1)
  let calls = 0
  const request = {
    key: 'cleared-after-failure', method: 'GET', timeoutMs: 1_000, maxAttempts: 1,
    shouldRetry: () => false, retryDelayMs: () => 0,
    run: async () => { calls += 1; if (calls === 1) throw new Error('first failed'); return 'second succeeded' },
  }
  await assert.rejects(coordinator.execute(request), /first failed/)
  assert.equal(await coordinator.execute(request), 'second succeeded'); checks += 1
  assert.deepEqual(coordinator.snapshot(), { active: 0, queued: 0, reads: 0, mutations: 0 }); checks += 1
}

const platform = fs.readFileSync(new URL('../src/l12/platform.ts', import.meta.url), 'utf8')
assert(platform.includes('PLATFORM_MAX_CONCURRENT_REQUESTS = 4')); checks += 1
assert(platform.includes('Math.min(reliability.maxAttempts ?? PLATFORM_READ_MAX_ATTEMPTS, PLATFORM_READ_MAX_ATTEMPTS)')); checks += 1
assert(platform.includes('Math.min(reliability.timeoutMs ?? maximumTimeoutMs, maximumTimeoutMs)')); checks += 1
assert(!platform.includes('dedupe?: boolean')); checks += 1
assert(platform.includes("'stale_session'")); checks += 1
assert(platform.includes('retryAfterMilliseconds(response)')); checks += 1
assert(platform.includes('requestBodyKey(fetchInit.body)')); checks += 1

console.log(`Platform request reliability: ${checks}/${checks} checks passed`)
