import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const requireDependency = createRequire(path.join(root, 'package.json'))
const ts = requireDependency('typescript')
const vue = requireDependency('vue')
const { compileScript, compileStyle, compileTemplate, parse } = requireDependency('@vue/compiler-sfc')
const { computed, createRenderer, nextTick, reactive, ref, watch } = vue
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const sources = Object.fromEntries([
  'src/l12/site/TournamentAccountDetail.vue',
  'src/l12/site/AdminTournamentWorkbench.vue',
  'src/l12/site/ProfilePage.vue',
  'src/l12/site/HomeRevisitActions.vue',
].map(relative => [relative, read(relative)]))

class MemoryStorage {
  values = new Map()
  get length() { return this.values.size }
  clear() { this.values.clear() }
  getItem(key) { return this.values.has(key) ? this.values.get(key) : null }
  key(index) { return [...this.values.keys()][index] ?? null }
  removeItem(key) { this.values.delete(key) }
  setItem(key, value) { this.values.set(key, String(value)) }
}
class MemoryEvents {
  listeners = new Map()
  addEventListener(name, listener) {
    const values = this.listeners.get(name) ?? new Set()
    values.add(listener); this.listeners.set(name, values)
  }
  removeEventListener(name, listener) { this.listeners.get(name)?.delete(listener) }
  dispatch(name) { for (const listener of this.listeners.get(name) ?? []) listener({ type: name }) }
}

const localStorage = new MemoryStorage()
const sessionStorage = new MemoryStorage()
const windowEvents = new MemoryEvents()
const documentEvents = new MemoryEvents()
Object.assign(globalThis, {
  localStorage,
  sessionStorage,
  window: Object.assign(windowEvents, {
    setTimeout, clearTimeout,
    matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
  }),
  document: Object.assign(documentEvents, {
    hidden: false,
    visibilityState: 'visible',
    title: '',
    createElement: () => ({ click() {}, style: {} }),
  }),
  location: { origin: 'http://synthetic.invalid', href: 'http://synthetic.invalid/' },
})
Object.defineProperty(globalThis, 'navigator', {
  configurable: true,
  value: { clipboard: { writeText: async () => {} } },
})

const platformState = reactive({ account: null, token: '' })
const authState = reactive({ initialized: true, verified: false, refreshing: false })
let requestHandler = async pathValue => { throw new Error(`Unexpected platform request: ${pathValue}`) }
globalThis.__deckAuthorityTestPlatform = {
  platformState,
  watch,
  getEffectiveOperationsPolicy: async () => ({ defaultPresetDeckIds: [] }),
  platformRequest: (...args) => requestHandler(...args),
}

const productionModuleUrl = source => `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText).toString('base64')}`
const deckSource = read('src/l12/decks.ts').replaceAll('\r\n', '\n')
const openingHandImport = `import {
  NORMAL_OPENING_HAND_CARD_TYPES,
  bypassesNormalDrawDeck,
  isDerivedDeckSpecialCard,
  isNormalOpeningHandCard,
  normalOpeningHandCopies,
} from './openingHandEligibility'`
const binaryUrl = productionModuleUrl(read('src/l12/deckCodeCodec.ts'))
const codecUrl = productionModuleUrl(read('src/l12/deckCacheCodec.ts')
  .replace("from './deckCodeCodec'", `from '${binaryUrl}'`))
const storageUrl = productionModuleUrl(read('src/l12/deckCacheStorage.ts')
  .replace("from './deckCodeCodec'", `from '${binaryUrl}'`)
  .replace("from './deckCacheCodec'", `from '${codecUrl}'`))
const executableDeckSource = deckSource
  .replace("import { watch } from 'vue'", 'const { watch } = globalThis.__deckAuthorityTestPlatform')
  .replace("from './deckCacheStorage'", `from '${storageUrl}'`)
  .replace("from './deckCacheCodec'", `from '${codecUrl}'`)
  .replace("import { normalizeLookupCardType } from './cardPresentation'", 'const normalizeLookupCardType = value => value')
  .replace(
    "import { getEffectiveOperationsPolicy, platformRequest, platformState, type OperationsCardRestriction } from './platform'",
    'const { getEffectiveOperationsPolicy, platformRequest, platformState } = globalThis.__deckAuthorityTestPlatform',
  )
  .replace("import { deploymentPath } from './deploymentBase'", 'const deploymentPath = value => value')
  .replace("import moraleIdentityData from '../../../服务端WebSocket/TwelveLegions/Data/morale-identities.json'", 'const moraleIdentityData = []')
  .replace("import cardProductInclusionsData from '../../../服务端WebSocket/TwelveLegions/Data/card-product-inclusions.json'", 'const cardProductInclusionsData = { products: [], cards: [] }')
  .replace("import cardArchiveAssetsData from '../../../服务端WebSocket/TwelveLegions/Data/card-archive-assets.json'", 'const cardArchiveAssetsData = { cards: [] }')
  .replace("import seasonTwoRulesData from '../../../服务端WebSocket/TwelveLegions/Data/cards.s2.json'", 'const seasonTwoRulesData = []')
  .replace(openingHandImport, `const NORMAL_OPENING_HAND_CARD_TYPES = new Set(['legion', 'tactic', 'artifact'])
const isDerivedDeckSpecialCard = card => card?.cardType === 'token' || card?.id === 'S02-01S1' || card?.id === 'S02-06S2'
const bypassesNormalDrawDeck = card => isDerivedDeckSpecialCard(card) || Boolean(card?.effect?.includes('构筑时不计入卡组数量'))
const isNormalOpeningHandCard = card => Boolean(card && NORMAL_OPENING_HAND_CARD_TYPES.has(card.cardType) && !bypassesNormalDrawDeck(card))
const normalOpeningHandCopies = (copies, cardForCopy) => copies.filter(copy => isNormalOpeningHandCard(cardForCopy(copy)))`)
const decks = await import(productionModuleUrl(executableDeckSource))

function importedBindings(compiled, modules, filename) {
  const file = ts.createSourceFile(filename, compiled, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  assert.equal(file.parseDiagnostics.length, 0, `${filename} compiled script must parse`)
  const bindings = {}
  for (const statement of file.statements.filter(ts.isImportDeclaration)) {
    const moduleName = statement.moduleSpecifier.text
    const value = modules[moduleName]
    assert.ok(value, `${filename} missing module fixture ${moduleName}`)
    const clause = statement.importClause
    if (!clause || clause.isTypeOnly) continue
    if (clause.name) bindings[clause.name.text] = value.default
    const named = clause.namedBindings
    if (named && ts.isNamespaceImport(named)) bindings[named.name.text] = value
    if (named && ts.isNamedImports(named)) for (const element of named.elements) {
      if (element.isTypeOnly) continue
      bindings[element.name.text] = value[element.propertyName?.text ?? element.name.text]
    }
  }
  const body = file.statements.filter(statement => !ts.isImportDeclaration(statement)).map(statement => statement.getText(file)).join('\n')
  return { bindings, body }
}

function compileComponent(relative, modules) {
  const parsed = parse(sources[relative], { filename: relative })
  assert.deepEqual(parsed.errors, [], `${relative} must parse`)
  const template = compileTemplate({ source: parsed.descriptor.template?.content ?? '', filename: relative,
    id: `light-${relative.replace(/\W/g, '-')}` })
  assert.deepEqual(template.errors, [], `${relative} template must compile`)
  for (const style of parsed.descriptor.styles) {
    const compiledStyle = compileStyle({ source: style.content, filename: relative,
      id: `data-v-light-${relative.replace(/\W/g, '-')}`, scoped: style.scoped })
    assert.deepEqual(compiledStyle.errors, [], `${relative} style must compile`)
  }
  const script = compileScript(parsed.descriptor, { id: `light-${relative.replace(/\W/g, '-')}` })
  const { bindings, body } = importedBindings(script.content, modules, relative)
  const output = ts.transpileModule(body, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText
  const exports = {}
  return new Function(...Object.keys(bindings), 'exports', `${output}\nreturn exports.default`)(...Object.values(bindings), exports)
}

function hostNode(type = 'root') { return { type, children: [], parent: null, text: '', props: {} } }
const renderer = createRenderer({
  patchProp(node, key, _previous, value) { node.props[key] = value },
  insert(child, parent, anchor) {
    child.parent = parent
    const index = anchor ? parent.children.indexOf(anchor) : -1
    if (index < 0) parent.children.push(child); else parent.children.splice(index, 0, child)
  },
  remove(child) { const index = child.parent?.children.indexOf(child) ?? -1; if (index >= 0) child.parent.children.splice(index, 1) },
  createElement: type => hostNode(type),
  createText: text => Object.assign(hostNode('text'), { text }),
  createComment: text => Object.assign(hostNode('comment'), { text }),
  setText(node, text) { node.text = text },
  setElementText(node, text) { node.text = text; node.children = [] },
  parentNode: node => node.parent,
  nextSibling(node) { const index = node.parent?.children.indexOf(node) ?? -1; return index >= 0 ? node.parent.children[index + 1] ?? null : null },
  querySelector: () => null,
  setScopeId() {},
  cloneNode: node => ({ ...node, children: [...node.children] }),
  insertStaticContent(content, parent, anchor) {
    const node = Object.assign(hostNode('static'), { text: content })
    this.insert?.(node, parent, anchor)
    return [node, node]
  },
})

function mount(component, props = {}) {
  const app = renderer.createApp(component, props)
  app.config.warnHandler = () => {}
  const rootNode = hostNode()
  app.mount(rootNode)
  return { app, rootNode, state: app._instance.setupState, unmount: () => app.unmount() }
}
async function flush(turns = 8) {
  for (let index = 0; index < turns; index += 1) { await Promise.resolve(); await nextTick() }
}
function deferred() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
function summary(id = 'deck-A', overrides = {}) {
  return {
    id, revision: 2, name: `牌库-${id}`, masterId: 'MASTER-A', updatedAt: '2026-10-07T12:00:00.000Z',
    publicationId: null, publicationVersion: null,
    counts: { main: 40, uncountedMain: 0, morale: 10, special: 0, bench: 0 },
    legal: true, legalityReason: null, ...overrides,
  }
}
function summaryPage(url, items = [summary()], total = items.length, facetOverrides = {}) {
  const parsed = new URL(url, 'http://synthetic.invalid')
  const page = Number(parsed.searchParams.get('page') ?? 1)
  const pageSize = Number(parsed.searchParams.get('pageSize') ?? 30)
  return {
    items, total, page, pageSize, generation: 9, permissionVersion: 3,
    catalogVersion: 'A'.repeat(64), policyVersion: 7,
    facets: { masters: total ? [{ masterId: items[0]?.masterId ?? 'MASTER-A', count: total }] : [],
      legal: total, illegal: 0, ...facetOverrides },
  }
}
function tournamentFixture(overrides = {}) {
  return {
    id: 'tournament-A', version: 5, code: 'EVENT-A', name: '合成赛事', status: 'registration', phase: 'registration-open',
    format: 'swiss', organizerAccountId: 'organizer', organizerName: 'Organizer', referees: [], rounds: [], participants: [],
    counts: { active: 0, waitlisted: 0, registered: 0, pendingCheckIn: 0, checkedIn: 0, removed: 0 },
    rules: { disasterMode: 'season', deckVisibility: 'after', disasterCardIds: [], cardRestrictions: [], ruleset: '现行', banList: '', hash: 'A'.repeat(64) },
    registrationVisibility: 'public', checkInMinutes: 5, lateGraceMinutes: 0, timeControl: {},
    eliminationBracket: [], finalSwissStandings: [], postponements: [], ...overrides,
  }
}
function participantFixture(overrides = {}) {
  return {
    accountId: 'account-A', username: 'Account A', waitlisted: false, waitlistPosition: null,
    deck: null, tournamentCheckedInAt: null, dropped: false, removed: false, eliminated: false,
    registrationBanned: false, seed: 1, ...overrides,
  }
}

const route = reactive({ params: { code: 'EVENT-A' }, query: {}, fullPath: '/battle/tournaments/EVENT-A' })
const routerCalls = []
const router = { push: async value => { routerCalls.push(['push', value]) }, replace: async value => { routerCalls.push(['replace', value]) } }
let permissions = new Set()
let tournamentList = []
let tournamentReads = []
let preCheckIns = []
let tournamentListBehavior = async () => ({ platformVersion: 1, items: tournamentList })
let preCheckInBehavior = async args => tournamentFixture({ id: args[0], version: args[1] + 1 })
const tournamentApi = {
  getByCode: async code => { tournamentReads.push(code); return tournamentList.find(item => item.code === code) ?? tournamentFixture({ code }) },
  list: (...args) => tournamentListBehavior(...args),
  preCheckIn: async (...args) => { preCheckIns.push(args); return preCheckInBehavior(args) },
}
const platformModule = {
  platformState,
  hasPermission: permission => permissions.has(permission),
  tournamentApi,
  friendApi: { friends: async () => [] },
  getEffectiveOperationsPolicy: async () => ({ disasterCardIds: [], cardRestrictions: [] }),
}
const noopLabels = new Proxy({}, { get: () => value => String(value ?? '') })
const netModule = { l12State: reactive({ notice: '', game: null, room: null }), connect: async () => {}, enterTournamentMatch() {}, spectateTournamentMatch() {} }
const routeModule = { useRoute: () => route, useRouter: () => router }
const componentModule = { default: {} }
const actionGateModule = { useActionGate: () => {
  const active = new Set()
  return {
    pending: computed(() => active.size > 0),
    isPending: key => active.has(key),
    run: async (key, work) => { if (active.has(key)) return; active.add(key); try { return await work() } finally { active.delete(key) } },
  }
} }
const timeModule = {
  tournamentTimeControlLabel: () => '计时',
  tournamentTimeControlMinutes: () => ({ total: 25, operation: 4, reconnect: 4, disaster: 1, mulligan: 1 }),
  tournamentTimeControlSeconds: value => value,
}
let deckCatalogReads = 0
const deckModule = {
  syncSavedDecksFromAccount: decks.syncSavedDecksFromAccount,
  loadSavedDecksState: decks.loadSavedDecksState,
  deckErrorBelongsToCurrentAccount: decks.deckErrorBelongsToCurrentAccount,
  loadPrivateDeckSummaryPage: decks.loadPrivateDeckSummaryPage,
  loadDeckCatalog: async () => { deckCatalogReads += 1; return [] },
}

let loginBehavior = async () => {
  platformState.account = { id: 'account-A', username: 'Account A', permissions: [] }
  platformState.token = 'token-A'
  authState.verified = true
}
let changeUsernameBehavior = async () => ({ message: '用户名已更新', account: platformState.account })
const profilePlatformModule = {
  alternateArtApi: { mine: async () => [] },
  canAccessAdmin: computed(() => false),
  changePassword: async () => ({ message: '密码已更新' }),
  changeUsername: (...args) => changeUsernameBehavior(...args),
  emailApi: { capability: async () => ({ enabled: false }), status: async () => null },
  login: (...args) => loginBehavior(...args),
  logout: async () => { platformState.account = null; platformState.token = ''; authState.verified = false },
  mfaCapability: async () => null,
  PlatformRequestError: class PlatformRequestError extends Error {},
  platformState,
  playerApi: { statistics: async () => ({ lines: [] }) },
  rankedApi: { overview: async () => ({ profile: { masterTitles: [], selectedMasterTitle: '' } }) },
  refreshCurrentAccount: async () => platformState.account,
  register: (...args) => loginBehavior(...args),
  sessionApi: { list: async () => [] },
  usernameChangeApi: { status: async () => ({ freeRenameAvailable: false }), useFreeRename: async () => ({}), request: async () => ({}) },
}
const profileDeckModule = { ensureOfficialPrebuiltDecks: decks.ensureOfficialPrebuiltDecks }

function tournamentDetailComponent() {
  return compileComponent('src/l12/site/TournamentAccountDetail.vue', {
    vue, 'vue-router': routeModule, '@/l12/decks': deckModule, '@/l12/net': netModule,
    '@/l12/platform': platformModule, '@/l12/tournamentLabels': noopLabels,
    '@/l12/tournamentTime': timeModule, './TournamentJudgeDesk.vue': componentModule,
    './TournamentManagementPanel.vue': componentModule,
  })
}
function tournamentWorkbenchComponent() {
  return compileComponent('src/l12/site/AdminTournamentWorkbench.vue', {
    vue, 'vue-router': routeModule, '@/l12/decks': deckModule, '@/l12/net': netModule,
    './ConstructionRuleEditor.vue': componentModule, './DisasterPoolPicker.vue': componentModule,
    '@/l12/useActionGate': actionGateModule, '@/l12/tournamentSnapshotMerge': { mergeTournamentSnapshot: items => items },
    '@/l12/tournamentTime': timeModule, '@/l12/platform': platformModule,
  })
}
function profileComponent() {
  return compileComponent('src/l12/site/ProfilePage.vue', {
    vue, 'vue-router': routeModule, '@/l12/net': netModule,
    '@/l12/platform': profilePlatformModule, '@/l12/decks': profileDeckModule,
    '@/l12/RankedIdentityBadge.vue': componentModule,
    './RankedPenaltyHistory.vue': componentModule,
    './RankedMasterTitleRulesModal.vue': componentModule, '@/l12/specialAssets': { masterProfileUrl: () => '' },
    '@/l12/CardImage.vue': componentModule, './useSectionScroll': { useSectionScroll() {} },
    './UiButton.vue': componentModule, './UiNotice.vue': componentModule,
  })
}
const homeRevisitModule = await import(productionModuleUrl(read('src/l12/site/homeRevisit.ts')))
function homeComponent() {
  return compileComponent('src/l12/site/HomeRevisitActions.vue', {
    vue, '@/l12/platform': { authState, platformState }, '@/l12/decks': deckModule,
    '@/l12/net': netModule,
    './homeRevisit': homeRevisitModule,
    'vue-router': routeModule,
  })
}

function resetIdentity(account = { id: 'account-A', username: 'Account A', permissions: [] }, token = 'token-A', verified = true) {
  platformState.account = account
  platformState.token = token
  authState.verified = verified
  localStorage.clear(); sessionStorage.clear(); permissions = new Set(); routerCalls.length = 0
  tournamentReads = []; preCheckIns = []; tournamentList = []
  tournamentListBehavior = async () => ({ platformVersion: 1, items: tournamentList })
  preCheckInBehavior = async args => tournamentFixture({ id: args[0], version: args[1] + 1 })
  deckCatalogReads = 0
  route.params = { code: 'EVENT-A' }; route.query = {}; route.fullPath = '/battle/tournaments/EVENT-A'
}
function captureRequests(handler) {
  const requests = []
  requestHandler = async (requestPath, init = {}) => {
    requests.push({ path: requestPath, init })
    return handler(requestPath, init, requests)
  }
  return requests
}
const summaryRequests = requests => requests.filter(request => request.path.startsWith('/api/decks/summaries?'))
const fullDeckRequests = requests => requests.filter(request => request.path === '/api/decks')

let passed = 0, failed = 0
const failures = []
async function check(label, run) {
  try {
    await run(); passed += 1; console.log(`PASS ${passed + failed}: ${label}`)
  } catch (error) {
    failed += 1; failures.push(label); console.error(`FAIL ${passed + failed}: ${label}\n${error?.stack || error}`)
  }
}

await check('authenticated TournamentAccountDetail mount reads one bounded summary page and never full deck bodies', async () => {
  resetIdentity(); tournamentList = [tournamentFixture()]
  const requests = captureRequests(requestPath => requestPath.startsWith('/api/decks/summaries?')
    ? summaryPage(requestPath) : requestPath === '/api/decks' ? [] : Promise.reject(new Error(`unexpected ${requestPath}`)))
  const mounted = mount(tournamentDetailComponent())
  await flush(); mounted.unmount()
  console.log('OBSERVE TournamentAccountDetail', requests.map(item => item.path))
  assert.equal(fullDeckRequests(requests).length, 0)
  assert.equal(summaryRequests(requests).length, 1)
  assert.equal(summaryRequests(requests)[0].init.cache, 'no-store')
  const url = new URL(summaryRequests(requests)[0].path, 'http://synthetic.invalid')
  assert.equal(url.searchParams.get('page'), '1'); assert.equal(url.searchParams.get('pageSize'), '30')
  assert.equal(url.searchParams.get('sort'), 'latest'); assert.equal(url.searchParams.has('legal'), false)
})

await check('authenticated non-admin TournamentWorkbench mount reads summaries while adminMode preserves its early return', async () => {
  resetIdentity(); tournamentList = []
  const requests = captureRequests(requestPath => requestPath.startsWith('/api/decks/summaries?')
    ? summaryPage(requestPath) : requestPath === '/api/decks' ? [] : Promise.reject(new Error(`unexpected ${requestPath}`)))
  const player = mount(tournamentWorkbenchComponent(), { adminMode: false })
  await flush(); player.unmount()
  console.log('OBSERVE TournamentWorkbench player', requests.map(item => item.path))
  assert.equal(fullDeckRequests(requests).length, 0)
  assert.equal(summaryRequests(requests).length, 1)
  assert.equal(summaryRequests(requests)[0].init.cache, 'no-store')
  const beforeAdmin = requests.length
  const catalogsBeforeAdmin = deckCatalogReads
  const admin = mount(tournamentWorkbenchComponent(), { adminMode: true })
  await flush(); admin.unmount()
  assert.equal(requests.length, beforeAdmin, 'adminMode must return before private deck or catalog reads')
  assert.equal(deckCatalogReads, catalogsBeforeAdmin, 'adminMode must return before catalog reads')
})

await check('guest tournament surfaces never read the private deck directory', async () => {
  resetIdentity(null, '', false); tournamentList = [tournamentFixture()]
  const requests = captureRequests(requestPath => Promise.reject(new Error(`unexpected ${requestPath}`)))
  const detail = mount(tournamentDetailComponent()); const workbench = mount(tournamentWorkbenchComponent(), { adminMode: false })
  await flush(); detail.unmount(); workbench.unmount()
  assert.equal(requests.length, 0)
})

await check('both tournament surfaces keep only the newest same-token A-B-A directory response', async () => {
  for (const [label, create] of [
    ['detail', () => mount(tournamentDetailComponent())],
    ['workbench', () => mount(tournamentWorkbenchComponent(), { adminMode: false })],
  ]) {
    resetIdentity(); tournamentList = [tournamentFixture({ participants: [participantFixture()] })]
    const pending = []
    captureRequests(requestPath => {
      const gate = deferred(); pending.push({ gate, requestPath }); return gate.promise
    })
    const mounted = create(); await flush(2)
    platformState.account = { id: 'account-B', username: 'Account B', permissions: [] }; await flush(2)
    platformState.account = { id: 'account-A', username: 'Account A', permissions: [] }; await flush(2)
    assert.equal(pending.length, 3, `${label} should issue one bounded read per actor generation`)
    pending[2].gate.resolve(summaryPage(pending[2].requestPath, [summary('newest')]))
    pending[0].gate.reject(Object.assign(new Error('old A 401'), { status: 401 }))
    pending[1].gate.reject(Object.assign(new Error('old B 403'), { status: 403 })); await flush()
    assert.equal(mounted.state.savedDecks[0]?.id, 'newest'); assert.equal(mounted.state.deckError, '')
    mounted.unmount()
  }
})

await check('TournamentAccountDetail reaches later summary pages, searches server-side, and preserves an off-page selection', async () => {
  resetIdentity()
  tournamentList = [tournamentFixture({ participants: [participantFixture()],
    counts: { active: 1, waitlisted: 0, registered: 1, pendingCheckIn: 1, checkedIn: 0, removed: 0 } })]
  const requests = captureRequests(requestPath => {
    if (!requestPath.startsWith('/api/decks/summaries?')) throw new Error(`unexpected ${requestPath}`)
    const url = new URL(requestPath, 'http://synthetic.invalid')
    if (url.searchParams.get('keyword')) return summaryPage(requestPath, [summary('search-result')], 1)
    if (url.searchParams.get('page') === '2') return summaryPage(requestPath,
      [summary('deck-illegal', { legal: false, legalityReason: '赛季规则不合法' })], 31, { legal: 30, illegal: 1 })
    return summaryPage(requestPath, [summary('deck-A')], 31, { legal: 30, illegal: 1 })
  })
  const mounted = mount(tournamentDetailComponent()); await flush()
  mounted.state.deckId = 'deck-A'; mounted.state.selectDeck()
  assert.equal(mounted.state.selectedDeck.id, 'deck-A')
  mounted.state.changeDeckPage(1); await flush()
  assert.equal(mounted.state.deckPage, 2); assert.equal(mounted.state.savedDecks[0].id, 'deck-illegal')
  assert.equal(mounted.state.deckId, 'deck-A'); assert.equal(mounted.state.selectedDeck.id, 'deck-A')
  mounted.state.deckId = 'deck-illegal'; mounted.state.selectDeck()
  assert.equal(mounted.state.selectedDeck.legal, false, 'season legality metadata must not hide a tournament choice')
  mounted.state.deckSearchInput = '关键字'; mounted.state.applyDeckSearch(); await flush()
  const searched = new URL(summaryRequests(requests).at(-1).path, 'http://synthetic.invalid')
  assert.equal(searched.searchParams.get('keyword'), '关键字'); assert.equal(searched.searchParams.get('page'), '1')
  assert.equal(searched.searchParams.has('legal'), false)
  assert.equal(mounted.state.deckId, 'deck-illegal'); assert.equal(mounted.state.selectedDeck.id, 'deck-illegal')
  mounted.unmount()
})

await check('TournamentAccountDetail submits only selected id and name once, including an illegal season summary', async () => {
  resetIdentity()
  tournamentList = [tournamentFixture({ participants: [participantFixture()] })]
  captureRequests(requestPath => requestPath.startsWith('/api/decks/summaries?')
    ? summaryPage(requestPath, [summary('deck-illegal', { name: '赛事自定牌库', legal: false, legalityReason: '赛季规则不合法' })], 1,
      { legal: 0, illegal: 1 }) : Promise.reject(new Error(`unexpected ${requestPath}`)))
  const gate = deferred(); preCheckInBehavior = () => gate.promise
  const mounted = mount(tournamentDetailComponent()); await flush()
  mounted.state.deckId = 'deck-illegal'; mounted.state.selectDeck()
  mounted.state.preCheckIn(); mounted.state.preCheckIn()
  assert.equal(preCheckIns.length, 1, 'busy guard must suppress a repeated write')
  assert.deepEqual(preCheckIns[0], ['tournament-A', 5, '赛事自定牌库', '', 'deck-illegal'])
  gate.resolve(tournamentFixture({ id: 'tournament-A', version: 6 })); await flush()
  assert.equal(mounted.state.notice, '牌库快照已锁定，赛前签到完成')
  mounted.unmount()
})

await check('TournamentAccountDetail ignores a late changed-selection response and keeps drafts through 403/404/409', async () => {
  resetIdentity(); tournamentList = [tournamentFixture({ participants: [participantFixture()] })]
  captureRequests(requestPath => requestPath.startsWith('/api/decks/summaries?')
    ? summaryPage(requestPath, [summary('deck-A'), summary('deck-B')], 2)
    : Promise.reject(new Error(`unexpected ${requestPath}`)))
  const gate = deferred(); preCheckInBehavior = () => gate.promise
  const mounted = mount(tournamentDetailComponent()); await flush()
  mounted.state.deckId = 'deck-A'; mounted.state.selectDeck(); mounted.state.preCheckIn()
  mounted.state.deckId = 'deck-B'; mounted.state.selectDeck()
  gate.resolve(tournamentFixture({ id: 'tournament-A', version: 6 })); await flush()
  assert.equal(mounted.state.tournament.version, 5); assert.equal(mounted.state.deckId, 'deck-B')
  const beforeErrors = preCheckIns.length
  for (const status of [403, 404, 409]) {
    preCheckInBehavior = async () => { throw Object.assign(new Error(`写入失败 ${status}`), { status }) }
    mounted.state.preCheckIn(); await flush()
    assert.equal(mounted.state.deckId, 'deck-B'); assert.equal(mounted.state.selectedDeck.id, 'deck-B')
    assert.match(mounted.state.notice, new RegExp(String(status)))
  }
  assert.equal(preCheckIns.length - beforeErrors, 3, 'failed writes must not retry silently')
  mounted.unmount()
})

await check('TournamentWorkbench paginates summaries without auto-selecting or clearing an off-page draft', async () => {
  resetIdentity(); tournamentList = [tournamentFixture({ participants: [participantFixture()] })]
  const requests = captureRequests(requestPath => {
    if (!requestPath.startsWith('/api/decks/summaries?')) throw new Error(`unexpected ${requestPath}`)
    const url = new URL(requestPath, 'http://synthetic.invalid')
    return url.searchParams.get('page') === '2'
      ? summaryPage(requestPath, [summary('deck-B')], 31)
      : summaryPage(requestPath, [summary('deck-A')], 31)
  })
  const mounted = mount(tournamentWorkbenchComponent(), { adminMode: false }); await flush()
  mounted.state.detailId = 'tournament-A'
  assert.equal(mounted.state.deckDrafts['tournament-A'].id, '', 'mount must not silently choose the first deck')
  mounted.state.deckDrafts['tournament-A'].id = 'deck-A'; mounted.state.selectDeckDraft('tournament-A')
  mounted.state.changeDeckPage(1); await flush()
  assert.equal(mounted.state.deckPage, 2); assert.equal(mounted.state.deckDrafts['tournament-A'].id, 'deck-A')
  assert.equal(mounted.state.selectedDeckFor('tournament-A').id, 'deck-A')
  const latest = new URL(summaryRequests(requests).at(-1).path, 'http://synthetic.invalid')
  assert.equal(latest.searchParams.get('pageSize'), '30'); assert.equal(latest.searchParams.has('legal'), false)
  mounted.unmount()
})

await check('TournamentWorkbench submits id and name once and ignores a response after explicit selection change', async () => {
  resetIdentity(); tournamentList = [tournamentFixture({ participants: [participantFixture()] })]
  captureRequests(requestPath => requestPath.startsWith('/api/decks/summaries?')
    ? summaryPage(requestPath, [summary('deck-A'), summary('deck-B')], 2)
    : Promise.reject(new Error(`unexpected ${requestPath}`)))
  const first = deferred(); preCheckInBehavior = () => first.promise
  const mounted = mount(tournamentWorkbenchComponent(), { adminMode: false }); await flush()
  mounted.state.detailId = 'tournament-A'
  mounted.state.deckDrafts['tournament-A'].id = 'deck-A'; mounted.state.selectDeckDraft('tournament-A')
  const item = mounted.state.tournaments[0], person = item.participants[0]
  mounted.state.saveDeck(item, person); mounted.state.saveDeck(item, person)
  assert.equal(preCheckIns.length, 1); assert.deepEqual(preCheckIns[0], ['tournament-A', 5, '牌库-deck-A', '', 'deck-A'])
  mounted.state.deckDrafts['tournament-A'].id = 'deck-B'; mounted.state.selectDeckDraft('tournament-A')
  first.resolve(tournamentFixture({ id: 'tournament-A', version: 6, participants: [participantFixture({ deck: { name: '牌库-deck-A' } })] }))
  await flush()
  assert.equal(mounted.state.tournaments[0].version, 5, 'late result must not replace the current view after selection changed')
  assert.equal(mounted.state.deckDrafts['tournament-A'].id, 'deck-B')
  preCheckInBehavior = async () => { throw Object.assign(new Error('冲突 409'), { status: 409 }) }
  mounted.state.saveDeck(mounted.state.tournaments[0], mounted.state.tournaments[0].participants[0]); await flush()
  assert.equal(mounted.state.deckDrafts['tournament-A'].id, 'deck-B'); assert.match(mounted.state.notice, /409/)
  assert.equal(preCheckIns.length, 2, '409 must not be retried')
  mounted.unmount()
})

await check('Profile login uses identity data only and does not issue a private deck read', async () => {
  resetIdentity(null, '', false)
  route.query = { redirect: '/decks?tab=mine' }
  const requests = captureRequests(requestPath => requestPath === '/api/decks' ? [] : Promise.reject(new Error(`unexpected ${requestPath}`)))
  loginBehavior = async () => {
    platformState.account = { id: 'account-A', username: 'Account A', permissions: [] }
    platformState.token = 'token-A'; authState.verified = true
  }
  const mounted = mount(profileComponent())
  await mounted.state.submitAuth(); await flush(); mounted.unmount()
  console.log('OBSERVE Profile login', requests.map(item => item.path))
  assert.equal(fullDeckRequests(requests).length, 0)
  assert.deepEqual(routerCalls.at(-1), ['replace', '/decks?tab=mine'])
})

await check('Profile mandatory username change uses identity data only and preserves redirect handling', async () => {
  resetIdentity({ id: 'account-A', username: 'Old', permissions: [], mustChangeUsername: true }, 'token-A', true)
  route.query = { redirect: '/battle' }
  const requests = captureRequests(requestPath => requestPath === '/api/decks' ? [] : Promise.reject(new Error(`unexpected ${requestPath}`)))
  changeUsernameBehavior = async () => {
    platformState.account = { id: 'account-A', username: 'New', permissions: [], mustChangeUsername: false }
    return { message: '用户名已更新', account: platformState.account }
  }
  const mounted = mount(profileComponent())
  await mounted.state.submitUsernameChange(); await flush(); mounted.unmount()
  console.log('OBSERVE Profile username', requests.map(item => item.path))
  assert.equal(fullDeckRequests(requests).length, 0)
  assert.deepEqual(routerCalls.at(-1), ['replace', '/battle'])
})

await check('Profile preserves must-change gates and form drafts without any private deck read', async () => {
  resetIdentity(null, '', false)
  const requests = captureRequests(requestPath => Promise.reject(new Error(`unexpected ${requestPath}`)))
  loginBehavior = async () => {
    platformState.account = { id: 'account-A', username: 'Legacy Name', permissions: [], mustChangeUsername: true }
    platformState.token = 'token-A'; authState.verified = true
  }
  const loginView = mount(profileComponent())
  loginView.state.auth.username = 'Legacy Name'; loginView.state.auth.password = 'temporary'
  await loginView.state.submitAuth(); await flush()
  assert.equal(loginView.state.auth.username, 'Legacy Name'); assert.equal(loginView.state.auth.password, '')
  assert.match(loginView.state.notice, /必须先修改用户名/); assert.equal(routerCalls.length, 0)
  loginView.unmount()

  resetIdentity({ id: 'account-A', username: 'Legacy Name', permissions: [], mustChangeUsername: true }, 'token-A', true)
  changeUsernameBehavior = async () => { throw Object.assign(new Error('修改被拒绝 403'), { status: 403 }) }
  const renameView = mount(profileComponent())
  renameView.state.usernameChange.username = '保留的新名字'; renameView.state.usernameChange.currentPassword = 'keep-secret'
  await renameView.state.submitUsernameChange(); await flush()
  assert.equal(renameView.state.usernameChange.username, '保留的新名字')
  assert.equal(renameView.state.usernameChange.currentPassword, 'keep-secret')
  assert.match(renameView.state.usernameChangeNotice, /403/); assert.equal(requests.length, 0)
  renameView.unmount()
})

// The user removed both Home shortcuts. Preserve the four lifecycle cases,
// now proving zero reads rather than testing the retired recent-deck consumer.
await check('HomeRevisitActions has no private read for a verified idle account', async () => {
  resetIdentity(); netModule.l12State.game = null
  const requests = captureRequests(requestPath => Promise.reject(new Error(`unexpected ${requestPath}`)))
  const mounted = mount(homeComponent()); await flush()
  assert.equal(requests.length, 0); assert.equal(mounted.state.canContinue, false)
  assert.equal(mounted.state.refreshRecent, undefined)
  mounted.unmount()
})

await check('HomeRevisitActions keeps own-connection authority across same-token A-B-A without reads', async () => {
  resetIdentity()
  Object.assign(netModule.l12State, { accountId: 'account-A', status: 'online',
    recoveryPhase: 'snapshot-acknowledged', leavingRoom: false, game: { matchId: 'synthetic-match', phase: 'Main' } })
  const requests = captureRequests(requestPath => Promise.reject(new Error(`unexpected ${requestPath}`)))
  const mounted = mount(homeComponent()); await flush(); assert.equal(mounted.state.canContinue, true)
  platformState.account = { id: 'account-B', username: 'Account B', permissions: [] }; await flush()
  assert.equal(mounted.state.canContinue, false)
  platformState.account = { id: 'account-A', username: 'Account A', permissions: [] }; await flush()
  assert.equal(mounted.state.canContinue, true); assert.equal(requests.length, 0)
  assert.equal(platformState.token, 'token-A')
  mounted.unmount(); netModule.l12State.game = null
})

await check('HomeRevisitActions adds no refresh listeners or reads on storage, visibility and route changes', async () => {
  resetIdentity(); netModule.l12State.game = null
  const count = events => [...events.listeners.values()].reduce((total, values) => total + values.size, 0)
  const listenersBefore = [count(windowEvents), count(documentEvents)]
  const requests = captureRequests(requestPath => Promise.reject(new Error(`unexpected ${requestPath}`)))
  const mounted = mount(homeComponent()); await flush()
  windowEvents.dispatch('storage'); document.hidden = true; documentEvents.dispatch('visibilitychange'); await flush()
  document.hidden = false; documentEvents.dispatch('visibilitychange'); route.fullPath = '/?home-revisit=2'; await flush()
  assert.equal(requests.length, 0); assert.deepEqual([count(windowEvents), count(documentEvents)], listenersBefore)
  mounted.unmount(); assert.deepEqual([count(windowEvents), count(documentEvents)], listenersBefore)
})

await check('HomeRevisitActions ended connection and unmount cannot revive removed shortcuts', async () => {
  resetIdentity()
  Object.assign(netModule.l12State, { accountId: 'account-A', status: 'online',
    recoveryPhase: 'snapshot-acknowledged', leavingRoom: false, game: { matchId: 'synthetic-match', phase: 'Main' } })
  const requests = captureRequests(requestPath => Promise.reject(new Error(`unexpected ${requestPath}`)))
  const mounted = mount(homeComponent()); await flush()
  netModule.l12State.game.phase = 'GameOver'; await flush(); assert.equal(mounted.state.canContinue, false)
  mounted.unmount(); windowEvents.dispatch('storage'); documentEvents.dispatch('visibilitychange'); await flush()
  assert.equal(requests.length, 0); netModule.l12State.game = null
})

for (const [label, identity] of [
  ['guest', [null, '', false]],
  ['unverified', [{ id: 'account-A' }, 'token-A', false]],
  ['disabled', [{ id: 'account-A', disabled: true }, 'token-A', true]],
  ['deleted', [{ id: 'account-A', deleted: true }, 'token-A', true]],
]) await check(`HomeRevisitActions ${label} identity performs no deck read`, async () => {
  resetIdentity(...identity)
  const requests = captureRequests(requestPath => Promise.reject(new Error(`unexpected ${requestPath}`)))
  const mounted = mount(homeComponent()); await flush(); mounted.unmount()
  assert.equal(requests.length, 0)
})

await check('consumer sources contain no legacy full-sync or cache-latest fallback', async () => {
  assert.equal(sources['src/l12/site/TournamentAccountDetail.vue'].includes('syncSavedDecksFromAccount'), false)
  assert.equal(sources['src/l12/site/AdminTournamentWorkbench.vue'].includes('syncSavedDecksFromAccount'), false)
  assert.equal(sources['src/l12/site/ProfilePage.vue'].includes('ensureOfficialPrebuiltDecks'), false)
  assert.equal(sources['src/l12/site/HomeRevisitActions.vue'].includes('loadSavedDecksState'), false)
  assert.equal(sources['src/l12/site/HomeRevisitActions.vue'].includes('loadPrivateDeck'), false)
  assert.equal(sources['src/l12/site/HomeRevisitActions.vue'].includes('我的牌库'), false)
  assert.equal(sources['src/l12/site/HomeRevisitActions.vue'].includes('我的赛事'), false)
  for (const name of ['src/l12/site/TournamentAccountDetail.vue', 'src/l12/site/AdminTournamentWorkbench.vue']) {
    assert.match(sources[name], /pageSize:\s*30/)
    assert.equal(sources[name].includes('loadPrivateDeckBody'), false)
    assert.equal(sources[name].includes('expectedRevision'), false)
  }
})

const report = {
  schema: 1,
  sources: Object.fromEntries(Object.entries(sources).map(([name, value]) => [name,
    crypto.createHash('sha256').update(value).digest('hex')])),
  realVueMounts: true,
  realProductionDeckReaders: true,
  compiledVueTemplates: true,
  compiledVueStyles: true,
  syntheticPlatformTransport: true,
  networkRequests: 0,
  productionWrites: 0,
  cases: passed + failed,
  passed,
  failed,
  failureLabels: failures,
}
console.log(JSON.stringify(report, null, 2))
if (failed) process.exitCode = 1
