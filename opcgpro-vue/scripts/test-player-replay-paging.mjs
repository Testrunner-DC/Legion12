import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const root = process.env.L12_REPLAY_SOURCE_ROOT || path.resolve(here, '../src/l12')
const require = createRequire(process.env.L12_REPLAY_TOOLING_PACKAGE || path.resolve(here, '../package.json'))
const ts = require('typescript'), vue = require('vue')
const source = name => fs.readFileSync(path.join(root, name), 'utf8')
const uri = text => 'data:text/javascript;base64,' + Buffer.from(text).toString('base64')
const transpile = text => ts.transpileModule(text, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
const modelUri = uri(transpile(source('replayModel.ts')))
const loaderUri = uri(transpile(source('replayArchiveLoader.ts')))
const providerUri = uri('export const platformRequest = (...args) => globalThis.__playerReplayRequest(...args)')
const helperCode = transpile(source('playerReplayArchive.ts')).replaceAll("'./platform'", JSON.stringify(providerUri))
  .replaceAll("'./replayArchiveLoader'", JSON.stringify(loaderUri)).replaceAll("'./replayModel'", JSON.stringify(modelUri))
export const playerReplayTestModule = await import(uri(helperCode))

export async function runPlayerReplayPagingTests() {
  const results = []
  async function test(name, run) {
    try { await run(); results.push({ name: 'player/' + name, passed: true }) }
    catch (error) { results.push({ name: 'player/' + name, passed: false, error: error.stack }) }
  }
  const previousProvider = globalThis.__playerReplayRequest
  const { createPlayerReplayReader, exportPlayerReplayBlob } = playerReplayTestModule
  const model = await import(modelUri)
  const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b }); return { promise, resolve, reject } }
  const flush = async () => { for (let index = 0; index < 16; index++) await Promise.resolve(); await vue.nextTick() }
  const match = { matchId: 'synthetic-player', roomCode: 'SYNTHETIC', player0: 'A', player1: 'B', deck0: '', deck1: '',
    startedUtc: '2026-10-24T00:00:00Z', endedUtc: '2026-10-24T01:00:00Z', winner: 0, finalHash: 'public-hash', error: null, commandCount: 6 }
  const commands = Array.from({ length: 6 }, (_, index) => ({ sequence: index + 1, receivedUtc: match.startedUtc,
    playerIndex: index % 2, command: { type: 'authorityProgress' }, accepted: true, error: null,
    revision: index + 1, stateHash: 'hash-' + index, state: { MatchId: match.matchId, Players: [
      { Name: 'A', DeckName: 'SYNTHETIC_PRIVATE_DECK_A', Hand: [] }, { Name: 'B', DeckName: 'SYNTHETIC_PRIVATE_DECK_B', Hand: [] }], Events: [] } }))
  const page = (index, viewer = 0) => {
    const items = structuredClone(commands.slice(index * 2, index * 2 + 2))
    return { match: { ...match }, items, nextCursor: index < 2 ? 'p' + (index + 1) : null, limit: 50,
      pageBytes: new TextEncoder().encode(JSON.stringify(items)).length, totalCommands: 6, viewerPlayerIndex: viewer }
  }
  const number = cursor => cursor === null || cursor === undefined ? 0 : Number(cursor.slice(1))
  try {
    await test('provider only uses owned page endpoint with caller signal and one attempt', async () => {
      const calls = [], controller = new AbortController()
      globalThis.__playerReplayRequest = async (...args) => { calls.push(args); return page(0, 1) }
      const result = await createPlayerReplayReader(match.matchId)(undefined, controller.signal)
      assert.equal(result.viewerPlayerIndex, 1); assert.equal(calls.length, 1)
      assert.equal(calls[0][0], '/api/matches/synthetic-player/replay?limit=50')
      assert.equal(calls[0][1].signal, controller.signal); assert.equal(calls[0][1].reliability.maxAttempts, 1)
      assert.equal(calls[0][1].cache, 'no-store')
    })
    await test('pre-cancelled provider never requests', async () => {
      const controller = new AbortController(); controller.abort(); let calls = 0
      globalThis.__playerReplayRequest = async () => { calls++; return page(0) }
      await assert.rejects(createPlayerReplayReader(match.matchId)(undefined, controller.signal), { name: 'AbortError' })
      assert.equal(calls, 0)
    })
    await test('late successful provider response after abort is rejected', async () => {
      const wait = deferred(), controller = new AbortController()
      globalThis.__playerReplayRequest = () => wait.promise
      const pending = createPlayerReplayReader(match.matchId)(undefined, controller.signal)
      controller.abort(); wait.resolve(page(0))
      await assert.rejects(pending, { name: 'AbortError' })
    })
    for (const corrupt of ['admin-field', 'viewer', 'match', 'private-summary', 'total', 'limit', 'bytes', 'body', 'state', 'stalled-cursor']) {
      await test('rejects invalid owned page/' + corrupt, async () => {
        const bad = page(0)
        if (corrupt === 'admin-field') bad.totalBytes = 123
        if (corrupt === 'viewer') bad.viewerPlayerIndex = null
        if (corrupt === 'match') bad.match.matchId = 'not-owned'
        if (corrupt === 'private-summary') bad.match.deck0 = 'private'
        if (corrupt === 'total') bad.totalCommands++
        if (corrupt === 'limit') bad.limit = 101
        if (corrupt === 'bytes') bad.pageBytes = 4 * 1024 * 1024 + 1
        if (corrupt === 'body') bad.items[0].state.padding = 'x'.repeat(4 * 1024 * 1024)
        if (corrupt === 'state') bad.items[0].state.MatchId = 'not-owned'
        if (corrupt === 'stalled-cursor') bad.nextCursor = 'incoming'
        globalThis.__playerReplayRequest = async () => bad
        await assert.rejects(createPlayerReplayReader(match.matchId)(corrupt === 'stalled-cursor' ? 'incoming' : undefined,
          new AbortController().signal), error => error.code === 'invalid_replay_page')
      })
    }
    await test('changed viewer or summary cannot enter existing reader', async () => {
      const read = createPlayerReplayReader(match.matchId), controller = new AbortController()
      globalThis.__playerReplayRequest = async () => page(0, 0)
      await read(undefined, controller.signal)
      globalThis.__playerReplayRequest = async () => page(1, 1)
      await assert.rejects(read('p1', controller.signal), error => error.code === 'invalid_replay_page')
    })
    for (const viewer of [0, 1]) await test('paged JSON keeps version, perspective, order and privacy/' + viewer, async () => {
      const cursors = []
      globalThis.__playerReplayRequest = async url => {
        assert(!url.includes('/admin/')); assert(url.includes('/replay?'))
        const cursor = new URL(url, 'https://synthetic.invalid').searchParams.get('cursor'); cursors.push(cursor)
        return page(number(cursor), viewer)
      }
      const result = await exportPlayerReplayBlob(match.matchId, new AbortController().signal)
      const payload = JSON.parse(await result.blob.text()), parsed = model.parseReplayPayload(payload)
      assert.equal(payload.format, 'legion12-replay'); assert.equal(payload.version, 1); assert.equal(payload.compatibilityVersion, 1)
      assert.deepEqual(cursors, [null, 'p1', 'p2']); assert.equal(parsed.viewerPlayerIndex, viewer)
      assert.deepEqual(parsed.commands, model.exportReplayPayload({ match, commands, viewerPlayerIndex: viewer }).detail.commands)
      assert(!JSON.stringify(payload).includes('SYNTHETIC_PRIVATE_DECK'))
    })
    for (const code of ['rate_limited', 'replay_incompatible', 'replay_page_too_large', 'storage_unavailable']) await test('failed export has no partial Blob or auto retry/' + code, async () => {
      let calls = 0
      globalThis.__playerReplayRequest = async () => { if (++calls === 1) return page(0); throw Object.assign(new Error(code),
        { status: code === 'rate_limited' ? 429 : code === 'replay_incompatible' ? 409 : code === 'storage_unavailable' ? 503 : 413, code }) }
      await assert.rejects(exportPlayerReplayBlob(match.matchId, new AbortController().signal))
      assert.equal(calls, 2)
    })
    await test('export cancellation aborts in-flight page and prevents later page or Blob', async () => {
      const wait = deferred(), controller = new AbortController(); let calls = 0, pendingSignal
      globalThis.__playerReplayRequest = async (_url, init) => { if (++calls === 1) return page(0); pendingSignal = init.signal; return wait.promise }
      const pending = exportPlayerReplayBlob(match.matchId, controller.signal)
      await flush(); assert(pendingSignal); controller.abort(); assert(pendingSignal.aborted)
      wait.resolve(page(1)); await assert.rejects(pending, { name: 'AbortError' }); assert.equal(calls, 2)
    })

    const records = source('MatchRecords.vue'), script = records.match(/<script setup[^>]*>([\s\S]*?)<\/script>/)[1]
    const ast = ts.createSourceFile('MatchRecords.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
    function fixture(exportRead, options = {}) {
      const unmounted = [], route = vue.reactive({ fullPath: '/battle/records' })
      const platformState = vue.reactive({ token: 'synthetic-token', account: { id: 'owner', disabled: false } })
      const authState = vue.reactive({ verified: true }), scope = vue.effectScope(), downloads = []
      const environment = { ...model, computed: vue.computed, ref: vue.ref, watch: vue.watch, onMounted() {}, onBeforeUnmount: fn => unmounted.push(fn),
        useRoute: () => route, useRouter: () => ({ push() {} }), platformState, authState, exportPlayerReplayBlob: exportRead,
        platformRequest: options.listRead || (async () => []), isMobileDeviceExperience: () => false, recordedMatchOutcome: () => ({ kind: 'ongoing' }),
        document: { createElement: () => ({ click() { downloads.push(true) } }) }, URL: { createObjectURL: () => 'blob:synthetic', revokeObjectURL() {} },
        window: { setTimeout() {} }, AbortController, Blob, Date, Intl, Error, console }
      const stripped = ast.statements.filter(node => !ts.isImportDeclaration(node)).map(node => node.getText(ast)).join('\n')
      const names = ast.statements.flatMap(node => ts.isFunctionDeclaration(node) && node.name ? [node.name.text]
        : ts.isVariableStatement(node) ? node.declarationList.declarations.flatMap(declaration => ts.isIdentifier(declaration.name) ? [declaration.name.text] : []) : [])
      vm.createContext(environment)
      scope.run(() => vm.runInContext(transpile(stripped + '\nglobalThis.__state={' + names.join(',') + '}'), environment))
      return { state: environment.__state, route, platformState, authState, downloads, dispose() { unmounted.forEach(fn => fn()); scope.stop() } }
    }
    for (const change of ['selection', 'route-ABA', 'account-ABA', 'token-ABA', 'disabled', 'unverified', 'unmount']) for (const late of ['success', 'failure']) {
      await test('actual records export abort and late download protection/' + change + '/' + late, async () => {
        const wait = deferred(); let signal, disposed = false
        const f = fixture((_match, input) => { signal = input; return wait.promise })
        try {
          f.state.selectMatch(match); const pending = f.state.exportReplay(); await flush(); assert(signal)
          if (change === 'selection') f.state.selectMatch({ ...match, matchId: 'other' })
          if (change === 'route-ABA') { f.route.fullPath = '/other'; f.route.fullPath = '/battle/records' }
          if (change === 'account-ABA') { f.platformState.account.id = 'other'; f.platformState.account.id = 'owner' }
          if (change === 'token-ABA') { f.platformState.token = 'other'; f.platformState.token = 'synthetic-token' }
          if (change === 'disabled') f.platformState.account.disabled = true
          if (change === 'unverified') f.authState.verified = false
          if (change === 'unmount') { f.dispose(); disposed = true }
          assert(signal.aborted)
          if (late === 'success') wait.resolve({ blob: new Blob(['synthetic']), match }); else wait.reject(new Error('synthetic late failure'))
          await pending
          assert.equal(f.downloads.length, 0); assert.equal(f.state.exporting.value, false)
        } finally { if (!disposed) f.dispose() }
      })
    }
    await test('actual records successful export downloads once', async () => {
      const f = fixture(async () => ({ blob: new Blob(['synthetic']), match }))
      try { f.state.selectMatch(match); await f.state.exportReplay(); assert.equal(f.downloads.length, 1); assert.equal(f.state.exporting.value, false) }
      finally { f.dispose() }
    })
    for (const change of ['removed-by-refresh', 'same-id-refresh', 'selected-ABA']) await test('actual list refresh governs pending second export page/' + change, async () => {
      const second = deferred(), list = deferred(); let signal, calls = 0, pending
      globalThis.__playerReplayRequest = async (_url, init) => {
        calls++; if (calls === 1) return page(0)
        if (calls === 2) { signal = init.signal; return second.promise }
        return page(2)
      }
      const f = fixture(exportPlayerReplayBlob, { listRead: () => list.promise })
      try {
        f.state.selectMatch(match); pending = f.state.exportReplay(); await flush(); assert(signal)
        if (change === 'selected-ABA') {
          f.state.selected.value = { ...match, matchId: 'another' }; f.state.selected.value = { ...match }
        } else {
          const refresh = f.state.loadMatches(); await flush(); assert.equal(signal.aborted, false)
          list.resolve(change === 'same-id-refresh' ? [{ ...match }] : []); await refresh
        }
        assert.equal(signal.aborted, change !== 'same-id-refresh')
        second.resolve(page(1)); await pending
        assert.equal(calls, change === 'same-id-refresh' ? 3 : 2)
        assert.equal(f.downloads.length, change === 'same-id-refresh' ? 1 : 0)
      } finally {
        f.dispose(); second.resolve(page(1)); list.resolve([]); if (pending) await pending
      }
    })
  } finally { globalThis.__playerReplayRequest = previousProvider }
  return results
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const cases = await runPlayerReplayPagingTests()
  console.log(JSON.stringify({ suite: 'player replay paging actual source', total: cases.length,
    passed: cases.filter(test => test.passed).length, failed: cases.filter(test => !test.passed).length, cases }, null, 2))
  if (cases.some(test => !test.passed)) process.exitCode = 1
}
