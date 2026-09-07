import assert from 'node:assert/strict'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

class MemoryStorage {
  #rows = new Map()
  get length() { return this.#rows.size }
  key(index) { return [...this.#rows.keys()][index] ?? null }
  getItem(key) { return this.#rows.get(key) ?? null }
  setItem(key, value) { this.#rows.set(String(key), String(value)) }
  removeItem(key) { this.#rows.delete(key) }
  completionKeys() { return [...this.#rows.keys()].filter(key => key.startsWith('l12-ranked-broadcast-completion:')) }
}

class TestWindow extends EventTarget {
  #nextTimer = 1
  #timers = new Map()
  matchMedia = () => ({ matches: false })
  setTimeout = (callback, delay = 0) => {
    const id = this.#nextTimer++
    this.#timers.set(id, { callback, delay })
    return id
  }
  clearTimeout = id => { this.#timers.delete(id) }
  runTimer(delay) {
    const timer = [...this.#timers].find(([, value]) => value.delay === delay)
    assert.ok(timer, `expected a pending ${delay}ms timer`)
    this.#timers.delete(timer[0])
    timer[1].callback()
  }
}

function makeClaim(id) {
  return {
    broadcast: {
      id,
      matchId: `match-${id}`,
      eventType: 'top-rank',
      message: `message-${id}`,
      createdAt: new Date().toISOString(),
    },
    claimToken: `token-${id}`,
    leaseExpiresAt: new Date(Date.now() + 45_000).toISOString(),
  }
}

async function settle(rounds = 8) {
  for (let index = 0; index < rounds; index += 1) await Promise.resolve()
}

const storage = new MemoryStorage()
const testWindow = new TestWindow()
const testPlatform = {
  authState: { verified: true },
  platformState: { account: { id: 'viewer-account' } },
  rankedApi: {},
}
globalThis.localStorage = storage
globalThis.window = testWindow
globalThis.__rankedBroadcastTestPlatform = testPlatform

let now = Date.now()
const originalDateNow = Date.now
Date.now = () => now

const vite = await createServer({
  configFile: false,
  root,
  cacheDir: path.join(os.tmpdir(), 'l12-ranked-broadcast-vite-cache'),
  appType: 'custom',
  logLevel: 'silent',
  server: { middlewareMode: true },
  resolve: { alias: [
    { find: /^@\/l12\/platform$/, replacement: 'virtual:ranked-broadcast-test-platform' },
    { find: '@', replacement: path.resolve(root, 'src') },
  ] },
  plugins: [{
    name: 'ranked-broadcast-test-platform',
    enforce: 'pre',
    resolveId(id) {
      const normalized = id.replaceAll('\\', '/')
      return id === 'virtual:ranked-broadcast-test-platform' || id === '@/l12/platform'
        || normalized.endsWith('/src/l12/platform.ts')
        ? '\0ranked-broadcast-test-platform' : null
    },
    load(id) {
      if (id !== '\0ranked-broadcast-test-platform') return null
      return `
        import { reactive } from 'vue'
        const source = globalThis.__rankedBroadcastTestPlatform
        export const authState = reactive(source.authState)
        export const platformState = reactive(source.platformState)
        export const rankedApi = source.rankedApi
        export class PlatformRequestError extends Error {
          constructor(message, status, code = '', correlationId = '') {
            super(message); this.status = status; this.code = code; this.correlationId = correlationId
          }
        }
      `
    },
  }],
})

try {
  const playback = await vite.ssrLoadModule('/src/l12/site/rankedBroadcastPlayback.ts')
  const mockPlatform = await vite.ssrLoadModule('virtual:ranked-broadcast-test-platform')
  const { rankedApi } = testPlatform

  const claimStarts = []
  let releaseFirstClaim
  rankedApi.claimBroadcast = startedAt => {
    claimStarts.push(startedAt)
    return new Promise(resolve => { releaseFirstClaim = resolve })
  }
  let completionMode = 'fail'
  const completionCalls = []
  rankedApi.completeBroadcast = async (id, token) => {
    completionCalls.push([id, token])
    if (completionMode === 'fail') throw new Error('offline')
    return { completed: true }
  }

  const firstInstance = playback.claimNextRankedBroadcast()
  const secondInstance = playback.claimNextRankedBroadcast()
  await settle()
  assert.equal(claimStarts.length, 1, 'two mounted tickers must share one claim request')
  assert.equal(claimStarts[0], playback.rankedBroadcastPlayback.subscriptionStartedAt)
  assert.ok(Number.isFinite(Date.parse(claimStarts[0])), 'claim must carry the foreground subscription time')
  releaseFirstClaim(makeClaim('broadcast-one'))
  await Promise.all([firstInstance, secondInstance])

  const playbackStartedAt = playback.rankedBroadcastPlayback.playbackStartedAt
  now += 6_250
  assert.equal(playback.rankedBroadcastAnimationStyle().animationDelay, '-6250ms')
  assert.equal(playback.rankedBroadcastPlayback.playbackStartedAt, playbackStartedAt,
    'route remount must not restart the active playback clock')
  await playback.claimNextRankedBroadcast()
  assert.equal(claimStarts.length, 1, 'a remounted ticker must reuse the visible claim')

  assert.equal(playback.completeCurrentRankedBroadcast(), true)
  assert.equal(playback.rankedBroadcastPlayback.claim, null, 'completion must hide before awaiting the API')
  assert.equal(storage.completionKeys().length, 1, 'completion must be persisted before the API settles')
  await settle()
  assert.equal(completionCalls.length, 1)
  assert.equal(storage.completionKeys().length, 1, 'a failed acknowledgement must remain durable')

  completionMode = 'success'
  testWindow.runTimer(15_000)
  await settle(16)
  assert.equal(storage.completionKeys().length, 0, 'a background retry must delete a completed acknowledgement')

  completionMode = 'fail'
  rankedApi.claimBroadcast = async startedAt => {
    claimStarts.push(startedAt)
    return makeClaim('broadcast-two')
  }
  await playback.claimNextRankedBroadcast()
  testWindow.dispatchEvent(new Event('pagehide'))
  assert.equal(playback.rankedBroadcastPlayback.claim, null, 'pagehide must retire the visible claim synchronously')
  assert.equal(storage.completionKeys().length, 1)
  await settle()

  rankedApi.claimBroadcast = async startedAt => {
    claimStarts.push(startedAt)
    return makeClaim('broadcast-three')
  }
  await playback.claimNextRankedBroadcast()
  playback.completeCurrentRankedBroadcast()
  assert.equal(storage.completionKeys().length, 2,
    'per-broadcast storage keys must preserve concurrent-tab acknowledgement writes')
  await settle()
  completionMode = 'success'
  testWindow.runTimer(15_000)
  await settle(24)
  assert.equal(storage.completionKeys().length, 0)

  playback.rankedBroadcastPlayback.subscriptionStartedAt = 'stale-login-subscription'
  mockPlatform.authState.verified = false
  assert.equal(playback.rankedBroadcastPlayback.accountId, '', 'logout must retire the module subscription even when no ticker is mounted')
  mockPlatform.authState.verified = true
  rankedApi.claimBroadcast = async startedAt => { claimStarts.push(startedAt); return null }
  await playback.claimNextRankedBroadcast()
  assert.notEqual(claimStarts.at(-1), 'stale-login-subscription', 're-login must create a fresh subscription')

  console.log('Ranked broadcast playback behavior passed: subscription, re-login reset, shared claim, elapsed remount, durable retry, pagehide, and multi-key storage.')
} finally {
  Date.now = originalDateNow
  await vite.close()
}
