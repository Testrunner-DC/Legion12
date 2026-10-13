import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

const frontendRoot = fileURLToPath(new URL('..', import.meta.url))
const tickerFilename = join(frontendRoot, 'src', 'l12', 'site', 'RankedBroadcastTicker.vue')
let moduleSerial = 0

class FakeTimers {
  nextId = 1
  tasks = new Map()

  setTimeout = (callback, delay = 0) => {
    const id = this.nextId++
    this.tasks.set(id, { callback, delay: Number(delay) || 0 })
    return id
  }

  clearTimeout = id => { this.tasks.delete(id) }

  runNext() {
    const next = this.tasks.entries().next().value
    assert.ok(next, 'expected a pending ticker timer')
    this.tasks.delete(next[0])
    next[1].callback()
    return next[1].delay
  }
}

class VisibilityDocument extends EventTarget {
  hidden = false

  setHidden(hidden) {
    this.hidden = hidden
    this.dispatchEvent(new Event('visibilitychange'))
  }
}

function deferred() {
  let resolve
  const promise = new Promise(done => { resolve = done })
  return { promise, resolve }
}

async function settle(turns = 16) {
  for (let index = 0; index < turns; index += 1) await Promise.resolve()
}

function compileTickerScript() {
  const source = readFileSync(tickerFilename, 'utf8')
  const match = source.match(/<script setup[^>]*>([\s\S]*?)<\/script>/)
  assert.ok(match, 'RankedBroadcastTicker must keep a script setup block')
  const parsed = ts.createSourceFile(tickerFilename, match[1], ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const body = parsed.statements
    .filter(statement => !ts.isImportDeclaration(statement))
    .map(statement => statement.getFullText(parsed))
    .join('')
  const result = ts.transpileModule(`
    const {
      computed, onBeforeUnmount, onMounted, watch, useRoute,
      authState, platformState, rankedApi,
      claimNextRankedBroadcast, completeCurrentRankedBroadcast,
      configureRankedBroadcastPlayback, rankedBroadcastAnimationStyle,
      rankedBroadcastIntervalMs, rankedBroadcastLobbyDelayMs, rankedBroadcastPlayback,
    } = globalThis.__rankedBroadcastTickerHarness
    ${body}
  `, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, isolatedModules: true },
    fileName: tickerFilename,
    reportDiagnostics: true,
  })
  const errors = (result.diagnostics || []).filter(item => item.category === ts.DiagnosticCategory.Error)
  assert.deepEqual(errors, [], 'RankedBroadcastTicker script must transpile in the lifecycle harness')
  return result.outputText
}

const compiledTicker = compileTickerScript()

async function mountTicker({ hidden = false, settings, claim, routePath = '/game', lobbyDelayMs = 0 } = {}) {
  const timers = new FakeTimers()
  const document = new VisibilityDocument()
  document.hidden = hidden
  const hooks = { mounted: null, unmounted: null, watchers: [] }
  const calls = { settings: 0, claims: 0, configured: 0 }
  const harness = {
    computed: getter => ({ get value() { return getter() } }),
    onMounted: callback => { hooks.mounted = callback },
    onBeforeUnmount: callback => { hooks.unmounted = callback },
    watch: (source, callback) => { hooks.watchers.push({ source, callback }) },
    useRoute: () => ({ path: routePath }),
    authState: { verified: true },
    platformState: { account: { id: 'ticker-account' } },
    rankedApi: {
      broadcastSettings: () => {
        calls.settings += 1
        return settings ? settings(calls.settings) : Promise.resolve({ intervalSeconds: 15 })
      },
    },
    claimNextRankedBroadcast: () => {
      calls.claims += 1
      return claim ? claim(calls.claims) : Promise.resolve(null)
    },
    completeCurrentRankedBroadcast: () => false,
    configureRankedBroadcastPlayback: () => { calls.configured += 1 },
    rankedBroadcastAnimationStyle: () => ({}),
    rankedBroadcastIntervalMs: () => 15_000,
    rankedBroadcastLobbyDelayMs: () => lobbyDelayMs,
    rankedBroadcastPlayback: { claim: null },
  }
  globalThis.document = document
  globalThis.window = { setTimeout: timers.setTimeout, clearTimeout: timers.clearTimeout }
  globalThis.__rankedBroadcastTickerHarness = harness
  moduleSerial += 1
  const encoded = Buffer.from(`${compiledTicker}\n//# sourceURL=ranked-broadcast-ticker-lifecycle-${moduleSerial}.mjs`).toString('base64')
  await import(`data:text/javascript;base64,${encoded}#${moduleSerial}`)
  assert.equal(typeof hooks.mounted, 'function')
  assert.equal(typeof hooks.unmounted, 'function')
  return { calls, document, hooks, timers }
}

const tests = [
  ['authoritative settings establish lobby delay without a second settings read', async () => {
    const fixture = await mountTicker({ routePath: '/lobby', lobbyDelayMs: 3_000 })
    await fixture.hooks.mounted()
    await settle()
    assert.equal(fixture.calls.settings, 1)
    assert.equal(fixture.calls.claims, 0)
    assert.equal(fixture.timers.tasks.size, 1)
    assert.ok(fixture.timers.runNext() > 0)
    await settle()
    assert.equal(fixture.calls.settings, 1)
    assert.equal(fixture.calls.claims, 1)
    fixture.hooks.unmounted()
  }],
  ['mount performs one authoritative settings read before the claim', async () => {
    const fixture = await mountTicker()
    await fixture.hooks.mounted()
    await settle()
    assert.equal(fixture.calls.settings, 1)
    assert.equal(fixture.calls.claims, 1)
    assert.equal(fixture.timers.tasks.size, 1)
    fixture.hooks.unmounted()
  }],
  ['late settings cannot resurrect an unmounted ticker', async () => {
    const pendingSettings = deferred()
    const fixture = await mountTicker({
      settings: call => call === 1 ? pendingSettings.promise : Promise.resolve({ intervalSeconds: 15 }),
    })
    const mounting = fixture.hooks.mounted()
    fixture.hooks.unmounted()
    pendingSettings.resolve({ intervalSeconds: 15 })
    await mounting
    await settle()
    assert.equal(fixture.calls.settings, 1)
    assert.equal(fixture.calls.claims, 0)
    assert.equal(fixture.timers.tasks.size, 0)
    fixture.document.setHidden(true)
    fixture.document.setHidden(false)
    await settle()
    assert.equal(fixture.calls.settings, 1, 'an unmounted instance must release its visibility listener')
    assert.equal(fixture.calls.claims, 0)
  }],
  ['late empty claim cannot schedule work after unmount', async () => {
    const pendingClaim = deferred()
    const fixture = await mountTicker({ claim: () => pendingClaim.promise })
    await fixture.hooks.mounted()
    await settle()
    assert.equal(fixture.calls.claims, 1)
    fixture.hooks.unmounted()
    pendingClaim.resolve(null)
    await settle()
    assert.equal(fixture.timers.tasks.size, 0)
  }],
  ['hidden mount stays idle and visibility restoration starts exactly one cycle', async () => {
    const fixture = await mountTicker({ hidden: true })
    await fixture.hooks.mounted()
    await settle()
    assert.equal(fixture.calls.settings, 0)
    assert.equal(fixture.calls.claims, 0)
    assert.equal(fixture.timers.tasks.size, 0)
    fixture.document.setHidden(false)
    await settle()
    assert.equal(fixture.calls.settings, 1)
    assert.equal(fixture.calls.claims, 1)
    assert.equal(fixture.timers.tasks.size, 1)
    fixture.hooks.unmounted()
  }],
  ['hiding during settings invalidates the old cycle and foreground resumes once', async () => {
    const pendingSettings = deferred()
    const fixture = await mountTicker({
      settings: call => call === 1 ? pendingSettings.promise : Promise.resolve({ intervalSeconds: 15 }),
    })
    const mounting = fixture.hooks.mounted()
    await settle()
    assert.equal(fixture.calls.settings, 1)
    fixture.document.setHidden(true)
    pendingSettings.resolve({ intervalSeconds: 15 })
    await mounting
    await settle()
    assert.equal(fixture.calls.claims, 0)
    assert.equal(fixture.timers.tasks.size, 0)
    fixture.document.setHidden(false)
    await settle()
    assert.equal(fixture.calls.settings, 2)
    assert.equal(fixture.calls.claims, 1)
    assert.equal(fixture.timers.tasks.size, 1)
    fixture.hooks.unmounted()
  }],
]

let passed = 0
const failures = []
for (const [name, test] of tests) {
  try {
    await test()
    passed += 1
  } catch (error) {
    failures.push({ name, error })
    console.error(`FAIL: ${name}`)
    console.error(error?.stack || error)
  }
}

delete globalThis.__rankedBroadcastTickerHarness

if (failures.length) throw new AggregateError(failures.map(item => item.error),
  `Ranked broadcast ticker lifecycle: ${passed}/${tests.length} passed`)
console.log(`Ranked broadcast ticker lifecycle: ${passed}/${tests.length} passed`)
