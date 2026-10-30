import assert from 'node:assert/strict'
import { readFileSync, existsSync, writeFileSync } from 'node:fs'
import vm from 'node:vm'
import { createHash } from 'node:crypto'
import ts from 'typescript'
import { parse, compileScript } from '@vue/compiler-sfc'
import * as Vue from 'vue'
globalThis.Document ??= class Document {}
globalThis.ShadowRoot ??= class ShadowRoot {}

const results = []
const startedAt = new Date().toISOString()
const rootUrl = new URL('../src/l12/site/', import.meta.url)
const flush = async () => { for (let i = 0; i < 12; i++) { await Promise.resolve(); await Vue.nextTick() } }
const renderer = Vue.createRenderer({
  createElement: tag => ({ tag, tagName: tag.toUpperCase(), children: [], props: {}, style: {}, addEventListener() {}, removeEventListener() {}, getRootNode: () => ({ activeElement: null }) }), createText: text => ({ text }), createComment: () => ({}),
  insert(node, parent, anchor) { if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1); node.parent = parent; const i = anchor ? parent.children.indexOf(anchor) : -1; parent.children.splice(i < 0 ? parent.children.length : i, 0, node) },
  remove(node) { if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1) },
  setText: (node, text) => { node.text = text }, setElementText: (node, text) => { node.text = text; node.children = [] },
  parentNode: node => node.parent, nextSibling(node) { return node.parent?.children[node.parent.children.indexOf(node) + 1] },
  patchProp: (node, key, old, value) => { node.props[key] = value },
})
const text = node => [node.text ?? '', ...(node.children ?? []).map(text)].join(' ')
const all = node => [node, ...(node.children ?? []).flatMap(all)]
const noop = () => {}
const pending = () => new Promise(() => {})
function environment() {
  const route = Vue.reactive({ fullPath: '/me', params: { code: 'A', accountId: 'target' }, query: {} })
  const platformState = Vue.reactive({ token: 'token-a', account: { id: 'actor', username: '玩家', permissions: ['admin.accounts.read'] } })
  const authState = Vue.reactive({ verified: true })
  const calls = [], entries = [], profileReads = [], previews = [], revocations = []
  const listeners = new Map()
  const teleportTarget = { tag: 'teleport-host', children: [] }
  const permission = name => platformState.account?.permissions?.includes(name) ?? false
  const read = (mode, target, query) => new Promise((resolve, reject) => calls.push({ mode, target, query, resolve, reject }))
  const generic = new Proxy({}, { get: (_, key) => key === 'then' ? undefined : pending })
  const api = new Proxy({ platformState, authState, hasPermission: permission, canAccessAdmin: true,
    playerApi: { statistics: () => { profileReads.push('statistics'); return pending() }, tournamentReputation: query => read('self', null, query) },
    rankedApi: { overview: () => { profileReads.push('ranked'); return pending() } }, alternateArtApi: generic, sessionApi: generic, usernameChangeApi: generic,
    adminApi: { account: async id => ({ id, username: '目标', roles: [], permissions: [], tournamentScopes: [] }), accountTournamentReputation: (id, query) => read('admin', id, query),
      previewAccountTournamentReputationRevoke: (...args) => new Promise((resolve, reject) => previews.push({ args, resolve, reject })),
      revokeAccountTournamentReputation: (...args) => new Promise((resolve, reject) => revocations.push({ args, resolve, reject })),
    },
    tournamentApi: { getByCode: async () => ({ id: 't', name: '比赛', code: 'A', organizerAccountId: 'actor', participants: [], referees: [], rounds: [], postponements: [], finalSwissStandings: [], status: 'registration', format: 'swiss', settings: {}, rules: {}, visibility: 'public' }) },
  }, { get: (target, key) => key in target ? target[key] : pending })
  const storage = { getItem: () => null, setItem: noop, removeItem: noop }
  const stub = { render: () => null }
  let actualPanel = false
  const cache = new Map()
  function component(name) {
    if (cache.has(name)) return cache.get(name)
    const filename = new URL(name, rootUrl)
    const parsed = parse(readFileSync(filename, 'utf8'), { filename: name })
    assert.deepEqual(parsed.errors, [])
    const compiled = compileScript(parsed.descriptor, { id: name, inlineTemplate: true })
    const module = { exports: {} }
    const imports = id => {
      if (id === 'vue') return Vue
      if (id === 'vue-router') return { useRoute: () => route, useRouter: () => ({ push: noop, replace: noop }) }
      if (id === '@/l12/platform') return api
      if (id.endsWith('visibleResourceRead')) {
        const module = { exports: {} }
        vm.runInNewContext(ts.transpileModule(readFileSync(new URL('../visibleResourceRead.ts', rootUrl), 'utf8'), {
          compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
        }).outputText, { module, exports: module.exports, Promise, Set })
        return module.exports
      }
      if (id.endsWith('mobileViewport')) return { landscapeTeleportTarget: () => teleportTarget }
      if (id.endsWith('TournamentReputationPanel.vue')) return { __esModule: true, default: actualPanel ? component('TournamentReputationPanel.vue') : { props: ['mode', 'accountId'], setup(props) { const entry = { mode: props.mode ?? 'self', target: props.accountId, active: true }; entries.push(entry); Vue.onBeforeUnmount(() => { entry.active = false }); return () => null } } }
      if (id.endsWith('AdminRiskActionDialog.vue')) return { __esModule: true, default: { props: ['title', 'impact', 'busy', 'error'], emits: ['confirm', 'cancel'], setup(props, { emit }) { return () => Vue.h('div', { role: 'dialog' }, [props.title, props.impact, Vue.h('button', { onClick: () => emit('confirm') }, '确认撤销'), Vue.h('button', { onClick: () => emit('cancel') }, '取消')]) } } }
      if (id.endsWith('.vue')) return { __esModule: true, default: stub }
      if (id.endsWith('/net')) return { l12State: Vue.reactive({ status: 'offline' }), connect: noop }
      if (id.endsWith('browserStorage')) return { optionalLocalStorage: storage, optionalSessionStorage: storage, BrowserLoginStorageError: Error }
      if (id.endsWith('useSectionScroll')) return { useSectionScroll: noop }
      if (id.endsWith('useAdminRiskAction')) { const module = { exports: {} }; vm.runInNewContext(ts.transpileModule(readFileSync(new URL('useAdminRiskAction.ts', rootUrl), 'utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, { module, exports: module.exports, require: () => Vue, Error }); return module.exports }
      if (id.endsWith('/decks')) return { loadDeckCatalog: async () => [] }
      return new Proxy({}, { get: (_, key) => key === 'vHorizontalScrollArrows' ? {} : () => '' })
    }
    const javascript = ts.transpileModule(compiled.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
    vm.runInNewContext(javascript, { module, exports: module.exports, require: imports, console, Date, Error, URLSearchParams,
      window: { innerWidth: 1000, addEventListener: noop, removeEventListener: noop, setTimeout: () => 1, clearTimeout: noop, matchMedia: () => ({ matches: false, addEventListener: noop, removeEventListener: noop }) },
      document: { visibilityState: 'visible', querySelector: () => null, addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) }, setInterval: () => 1, clearInterval: noop }, { filename: name })
    cache.set(name, module.exports.default); return module.exports.default
  }
  function mount(name, initialProps = {}, real = false) {
    actualPanel = real; const props = Vue.reactive(initialProps); const root = { children: [] }
    const app = renderer.createApp({ render: () => Vue.h(component(name), props) }); app.config.warnHandler = noop; app.mount(root)
    return { root, props, close: () => app.unmount(), click(label) { const button = all(root).find(n => n.tag === 'button' && text(n).includes(label)); assert(button, label); button.props.onClick({ preventDefault: noop, stopPropagation: noop }) } }
  }
  return { route, platformState, authState, calls, entries, profileReads, previews, revocations, mount, api, visibility: () => listeners.get('visibilitychange')?.() }
}
function page(overrides = {}) { return { ruleVersion: 1, score: 100, qualification: 'normal', asOf: '2026-10-29T00:00:00Z', revision: 0, nextRecoveryAt: null, page: 1, pageSize: 20, total: 0, items: [], ...overrides } }
async function test(name, fn) { try { await fn(); results.push({ name, passed: true }); console.log('PASS ' + name) } catch (error) { results.push({ name, passed: false, error: String(error) }); console.error('FAIL ' + name + ': ' + error.message) } }

await test('actual My overview mounts one private self consumer within existing three-read budget', async () => { const e = environment(); const f = e.mount('ProfilePage.vue'); try { await flush(); assert.equal(e.entries.length, 1); assert.equal(e.entries[0].mode, 'self'); assert.equal(e.profileReads.length + e.entries.length, 3); e.route.query.section = 'security'; await flush(); assert.equal(e.entries[0].active, false) } finally { f.close() } })
await test('actual authenticated tournament overview mounts only self consumer', async () => { const e = environment(); e.route.fullPath = '/battle/tournaments/A'; const f = e.mount('TournamentAccountDetail.vue'); try { await flush(); assert.equal(e.entries.length, 1); assert.equal(e.entries[0].mode, 'self'); assert.equal(e.entries[0].target, undefined) } finally { f.close() } })
await test('actual account management mounts matching global-admin target only', async () => { const e = environment(); e.route.fullPath = '/admin/accounts/target'; const f = e.mount('AdminAccountDetailPage.vue'); try { await flush(); assert.equal(e.entries.length, 1); assert.equal(e.entries[0].mode, 'admin'); assert.equal(e.entries[0].target, 'target'); e.platformState.account.permissions = []; await flush(); e.route.params.accountId = 'other'; await flush(); assert.equal(e.entries.length, 1) } finally { f.close() } })

if (!process.env.L12_REPUTATION_UI_ENTRY_BASELINE && existsSync(new URL('TournamentReputationPanel.vue', rootUrl))) {
  await test('admin existing-fact revoke requires reason, server preview and explicit risk confirmation', async () => {
    const e = environment(); e.platformState.account.permissions.push('admin.accounts.status.write')
    const f = e.mount('TournamentReputationPanel.vue', { mode: 'admin', accountId: 'target' }, true)
    try {
      e.calls[0].resolve(page({ score: 80, revision: 1, total: 1, items: [{ id: 'original', sequence: 1, tournamentId: 't', tournamentName: '比赛', kind: 'no-show', effectiveAt: '2026-10-01T00:00:00Z', recordedAt: '2026-10-01T00:00:00Z', assessment: 'deducted', effectiveScoreChange: -20 }] })); await flush()
      f.click('撤销此处置'); await flush(); f.click('预演撤销'); await flush(); assert.equal(e.previews.length, 0)
      all(f.root).find(n => n.tag === 'textarea').props['onUpdate:modelValue']('核实平台故障'); await flush()
      f.click('预演撤销'); await flush(); assert.equal(e.previews.length, 1); assert.equal(e.revocations.length, 0)
      assert.deepEqual([...e.previews[0].args], ['target', 'original', '核实平台故障', 1])
      e.previews[0].resolve({ accountId: 'target', eventId: 'original', tournamentId: 't', tournamentVersion: 3, platformVersion: 9, reputationRevision: 1, alreadyRevoked: false, before: { score: 80, nextRecoveryAt: null }, after: { score: 100, nextRecoveryAt: null } }); await flush()
      assert.match(text(f.root), /重新核算/); f.click('确认撤销'); await flush(); assert.equal(e.revocations.length, 1)
      const [target, event, command] = e.revocations[0].args; assert.equal(target, 'target'); assert.equal(event, 'original')
      assert.equal(command.expectedVersion, 9); assert.equal(command.expectedTournamentVersion, 3); assert.equal(command.expectedReputationRevision, 1)
      assert.equal(command.reason, '核实平台故障'); assert(command.idempotencyKey); assert(!('score' in command)); assert(!('kind' in command)); assert(!('effectiveAt' in command))
      e.revocations[0].resolve({ applied: true }); await flush(); assert.equal(e.calls.length, 2)
    } finally { f.close() }
  })
  await test('capped zero delta remains correctable but self and read-only admin have no write controls', async () => {
    const item = { id: 'cap', sequence: 1, tournamentId: 't', tournamentName: '比赛', kind: 'no-show', effectiveAt: '2026-10-01T00:00:00Z', recordedAt: '2026-10-01T00:00:00Z', assessment: 'no-additional-deduction', effectiveScoreChange: 0 }
    for (const mode of ['self', 'admin']) { const e = environment(); const f = e.mount('TournamentReputationPanel.vue', { mode, accountId: 'target' }, true); try { e.calls[0].resolve(page({ score: 80, revision: 1, total: 1, items: [item] })); await flush(); assert(!text(f.root).includes('撤销此处置')) } finally { f.close() } }
    const e = environment(); e.platformState.account.permissions.push('admin.accounts.status.write'); const f = e.mount('TournamentReputationPanel.vue', { mode: 'admin', accountId: 'target' }, true)
    try { e.calls[0].resolve(page({ score: 80, revision: 1, total: 1, items: [item] })); await flush(); assert.match(text(f.root), /撤销此处置/) } finally { f.close() }
  })
  await test('late preview after route target change cannot show confirmation or submit revoke', async () => {
    const e = environment(); e.platformState.account.permissions.push('admin.accounts.status.write'); const f = e.mount('TournamentReputationPanel.vue', { mode: 'admin', accountId: 'target' }, true)
    try {
      e.calls[0].resolve(page({ score: 80, revision: 1, total: 1, items: [{ id: 'original', sequence: 1, tournamentId: 't', tournamentName: '旧赛事', kind: 'no-show', effectiveAt: '2026-10-01T00:00:00Z', recordedAt: '2026-10-01T00:00:00Z', assessment: 'deducted', effectiveScoreChange: -20 }] })); await flush()
      f.click('撤销此处置'); await flush(); all(f.root).find(n => n.tag === 'textarea').props['onUpdate:modelValue']('核实故障'); f.click('预演撤销'); await flush()
      f.props.accountId = 'other'; e.route.params.accountId = 'other'; e.route.fullPath = '/admin/accounts/other'; await flush()
      e.previews[0].resolve({ accountId: 'target', eventId: 'original', tournamentVersion: 3, platformVersion: 9, reputationRevision: 1, alreadyRevoked: false, before: { score: 80 }, after: { score: 100 } }); await flush()
      assert(!all(f.root).some(n => n.props?.role === 'dialog')); assert.equal(e.revocations.length, 0); assert(!text(f.root).includes('旧赛事'))
    } finally { f.close() }
  })
  await test('actual dedicated API methods use existing request, self has no target and admin id is encoded', async () => {
    const source = readFileSync(new URL('../src/l12/platform.ts', import.meta.url), 'utf8')
    const parsed = ts.createSourceFile('platform.ts', source, ts.ScriptTarget.Latest, true)
    const calls = []
    function method(api, name) {
      let arrow
      function visit(node) { if (ts.isVariableDeclaration(node) && node.name.getText(parsed) === api && node.initializer && ts.isObjectLiteralExpression(node.initializer)) arrow = node.initializer.properties.find(item => ts.isPropertyAssignment(item) && item.name.getText(parsed) === name)?.initializer; ts.forEachChild(node, visit) }
      visit(parsed); assert(arrow)
      const module = { exports: {} }
      vm.runInNewContext(ts.transpileModule('module.exports = ' + arrow.getText(parsed), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText, { module, URLSearchParams, encodeURIComponent, platformRequest: (...args) => { calls.push(args); return Promise.resolve(null) } })
      return module.exports
    }
    await method('playerApi', 'tournamentReputation')({ page: 2, pageSize: 20, expectedRevision: 7, accountId: 'not-allowed' })
    assert.equal(calls[0][0], '/api/me/tournament-reputation?page=2&pageSize=20&expectedRevision=7'); assert.equal(calls[0].length, 1)
    await method('adminApi', 'accountTournamentReputation')('a/b')
    assert.equal(calls[1][0], '/api/admin/accounts/a%2Fb/tournament-reputation?page=1&pageSize=20'); assert.equal(calls[1].length, 1)
    assert(!readFileSync(new URL('TournamentPublicDetail.vue', rootUrl), 'utf8').includes('TournamentReputationPanel'))
  })
  await test('real My composition makes exactly two original reads plus one self read', async () => { const e = environment(); const f = e.mount('ProfilePage.vue', {}, true); try { await flush(); assert.equal(e.calls.length, 1); assert.equal(e.calls[0].mode, 'self'); assert.equal(e.profileReads.length + e.calls.length, 3); e.route.query.section = 'collection'; await flush(); e.calls[0].resolve(page({ score: 81 })); await flush(); assert(!text(f.root).includes('81')) } finally { f.close() } })
  await test('existing tournament visibility reload does not remount or refresh private score', async () => { const e = environment(); const f = e.mount('TournamentAccountDetail.vue', {}, true); try { await flush(); assert.equal(e.calls.length, 1); e.calls[0].resolve(page({ score: 94 })); await flush(); let finish; e.api.tournamentApi.getByCode = () => new Promise(resolve => { finish = resolve }); e.visibility(); await flush(); assert.match(text(f.root), /94/); finish({ id: 't', name: '比赛', code: 'A', organizerAccountId: 'actor', participants: [], referees: [], rounds: [], postponements: [], finalSwissStandings: [], status: 'registration', format: 'swiss', rules: {} }); await flush(); assert.equal(e.calls.length, 1) } finally { f.close() } })
  await test('unverified and ordinary organizer cannot request target reputation', async () => { const e = environment(); e.authState.verified = false; const f = e.mount('TournamentReputationPanel.vue', {}, true); try { assert.equal(e.calls.length, 0); e.authState.verified = true; await flush(); assert.equal(e.calls.length, 1); e.platformState.account.mustChangePassword = true; await flush(); e.calls[0].resolve(page({ score: 92 })); await flush(); assert(!text(f.root).includes('92')) } finally { f.close() } const admin = environment(); admin.platformState.account.permissions = ['tournaments.manage']; const target = admin.mount('TournamentReputationPanel.vue', { mode: 'admin', accountId: 'target' }, true); try { assert.equal(admin.calls.length, 0) } finally { target.close() } })
  await test('server 100 displays; history collapsed and toggle causes no fetch', async () => { const e = environment(); const f = e.mount('TournamentReputationPanel.vue', {}, true); try { await flush(); assert.equal(e.calls.length, 1); e.calls[0].resolve(page()); await flush(); assert.match(text(f.root), /100/); const details = all(f.root).find(n => n.tag === 'details'); assert(details); assert(!details.props.open); details.props.onToggle({ currentTarget: { open: true } }); await flush(); assert.equal(e.calls.length, 1) } finally { f.close() } })
  await test('failure and malformed response remain unknown, never synthetic 100', async () => { const e = environment(); const f = e.mount('TournamentReputationPanel.vue', {}, true); try { e.calls[0].reject(new Error('503')); await flush(); assert.match(text(f.root), /未知/); assert(!text(f.root).includes('100')); f.click('重试'); await flush(); e.calls[1].resolve(page({ score: null })); await flush(); assert.match(text(f.root), /未知/) } finally { f.close() } })
  await test('logout, token ABA and unmount reject late responses', async () => { const e = environment(); const f = e.mount('TournamentReputationPanel.vue', {}, true); const old = e.calls[0]; e.platformState.token = 'token-b'; await flush(); e.platformState.token = 'token-a'; await flush(); old.resolve(page({ score: 77, qualification: 'warning' })); await flush(); assert(!text(f.root).includes('77')); e.calls.at(-1).resolve(page({ score: 88 })); await flush(); assert.match(text(f.root), /88/); e.authState.verified = false; await flush(); assert(!text(f.root).includes('88')); f.close(); e.calls[1].resolve(page({ score: 66 })); await flush(); assert.equal(f.root.children.length, 0) })
  await test('account and route ABA reject old self projection without cross-account cache', async () => { const e = environment(); const f = e.mount('TournamentReputationPanel.vue', { accountId: 'never-a-self-target' }, true); try { const first = e.calls[0]; assert.equal(first.target, null); e.platformState.account.id = 'other'; await flush(); e.platformState.account.id = 'actor'; e.route.fullPath = '/battle/tournaments/A'; await flush(); e.route.fullPath = '/me'; await flush(); first.resolve(page({ score: 76, qualification: 'warning' })); await flush(); assert(!text(f.root).includes('76')); assert(e.calls.length >= 4); e.calls.at(-1).resolve(page({ score: 0, qualification: 'blocked' })); await flush(); assert.match(text(f.root), /新报名/); assert(all(f.root).some(n => n.tag === 'strong' && /^0\s/.test(text(n).trim()))) } finally { f.close() } })
  await test('admin denial and target route changes clear and isolate data', async () => { const e = environment(); const f = e.mount('TournamentReputationPanel.vue', { mode: 'admin', accountId: 'target' }, true); try { const old = e.calls[0]; f.props.accountId = 'other'; e.route.params.accountId = 'other'; e.route.fullPath = '/admin/accounts/other'; await flush(); old.resolve(page({ score: 73, qualification: 'warning' })); await flush(); assert(!text(f.root).includes('73')); assert.equal(e.calls.at(-1).target, 'other'); e.platformState.account.permissions = []; await flush(); e.calls.at(-1).resolve(page({ score: 89 })); await flush(); assert(!text(f.root).includes('89')) } finally { f.close() } })
  await test('bounded next page carries revision and uses authoritative effective changes', async () => { const e = environment(); const f = e.mount('TournamentReputationPanel.vue', {}, true); try { e.calls[0].resolve(page({ revision: 7, total: 21, items: [
    { id: '1', sequence: 1, tournamentId: 't', tournamentName: '比赛', kind: 'no-show', effectiveAt: '2026-10-01T00:00:00Z', recordedAt: '2026-10-01T00:00:00Z', assessment: 'no-additional-deduction', effectiveScoreChange: 0 },
    { id: '2', sequence: 2, tournamentId: 't', tournamentName: '比赛', kind: 'revoke', effectiveAt: '2026-10-01T00:00:00Z', recordedAt: '2026-10-01T00:00:00Z', assessment: 'correction', effectiveScoreChange: null },
  ] })); await flush(); assert.match(text(f.root), /不重复扣分/); assert.match(text(f.root), /重新核算/); f.click('下一页'); await flush(); assert.equal(e.calls[1].query.page, 2); assert.equal(e.calls[1].query.pageSize, 20); assert.equal(e.calls[1].query.expectedRevision, 7); e.calls[1].reject(new Error('409')); await flush(); assert.match(text(f.root), /未知/); assert(!text(f.root).includes('100')) } finally { f.close() } })
  await test('source has no timer, visibility refresh, cross-account storage or public DTO extension', () => { const source = readFileSync(new URL('TournamentReputationPanel.vue', rootUrl), 'utf8'); assert(!/setInterval|setTimeout|visibilitychange|localStorage|sessionStorage/.test(source)); const platform = readFileSync(new URL('../src/l12/platform.ts', import.meta.url), 'utf8'); assert.match(platform, /\/api\/me\/tournament-reputation/); assert.match(platform, /\/tournament-reputation/); })
}
if (process.env.L12_REPUTATION_UI_RESULT) writeFileSync(process.env.L12_REPUTATION_UI_RESULT, JSON.stringify({ startedAt, endedAt: new Date().toISOString(), results, passed: results.filter(x => x.passed).length, failed: results.filter(x => !x.passed).length,
  pins: Object.fromEntries(['ProfilePage.vue', 'TournamentAccountDetail.vue', 'AdminAccountDetailPage.vue', 'TournamentReputationPanel.vue'].filter(name => existsSync(new URL(name, rootUrl))).map(name => [name, createHash('sha256').update(readFileSync(new URL(name, rootUrl))).digest('hex')])) }, null, 2))
if (results.some(x => !x.passed)) process.exitCode = 1
