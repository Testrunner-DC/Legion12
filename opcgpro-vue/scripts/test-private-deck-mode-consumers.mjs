import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const dependencyRoot = path.resolve(process.env.L12_MODE_CONSUMER_DEPENDENCY_ROOT || projectRoot)
const requireDependency = createRequire(path.join(dependencyRoot, 'package.json'))
const ts = requireDependency('typescript')
const { compileScript, compileStyle, compileTemplate, parse } = requireDependency('@vue/compiler-sfc')

const sources = {
  battle: 'src/l12/site/BattleHubPage.vue',
  sandbox: 'src/l12/site/SandboxPage.vue',
  selector: 'src/l12/SavedDeckSelector.vue',
  decks: 'src/l12/decks.ts',
  focused: 'scripts/test-private-deck-mode-consumers.mjs',
}
const baselines = {
  battle: { rawSha256: 'D98AB595F7B51889963CE380970049DB33486A793A831FB74C8E5B9363158686', lfSha256: '2D8A470800F7614B11DADB9865E122486AE524624AC34C47FDED3274CD36CFC2', bytes: 49515 },
  sandbox: { rawSha256: 'DCE5412AC47F6575FE31525B3D997BB90DAE924EDD551E0FD8401896C4E55AD5', lfSha256: '9943F22F523D353C46E18E579EE26DB5BC2B809CE28387D14880E687B71839C8', bytes: 10503 },
  selector: { rawSha256: '98EDCD0FB810815B2A465448745678EEB15D467A0AC6FD0F41B6E0916845A1EB', lfSha256: 'F7689F90677A3A84A2E5BCF229B77DC31203051B4613970157F4D6174900020D', bytes: 6745 },
  focusedRed: { rawSha256: '3D8CC4B56D9C8C656A63014128C99F3BA49C79244A9E7402EFC5EAEC90BC9FCD' },
}
const originalCu4Core = {
  rawSha256: 'E19608E2C343B177FABB18FA4126DF4513DEEB7E9761794CE08CFBFCEB63F9A3',
  lfSha256: 'C8DC5730A4F4B7E8D4DB6741D2D8FCC47EA1389681F144A4962287DC1CC5A4D3',
}
const readBytes = relative => fs.readFileSync(path.join(projectRoot, relative))
const normalizeLf = value => value.toString('utf8').replaceAll('\r\n', '\n')
const read = relative => normalizeLf(readBytes(relative))
const sha256 = value => crypto.createHash('sha256').update(value).digest('hex').toUpperCase()
const manifest = Object.fromEntries(Object.entries(sources).map(([name, relative]) => {
  const raw = readBytes(relative)
  const normalized = normalizeLf(raw)
  return [name, {
    path: relative,
    bytes: raw.length,
    lines: normalized.split('\n').length,
    rawSha256: sha256(raw),
    lfSha256: sha256(normalized),
  }]
}))

// Bind the producer tested now, rather than an expired child-task lease.
// Check it again after execution so concurrent source edits cannot reuse this result.
const expectedCore = { rawSha256: manifest.decks.rawSha256, lfSha256: manifest.decks.lfSha256 }
function requireUnchangedCore() {
  const raw = readBytes(sources.decks)
  assert.equal(sha256(raw), expectedCore.rawSha256, 'Core source changed while consumer regressions were executing')
  assert.equal(sha256(normalizeLf(raw)), expectedCore.lfSha256, 'Core normalized source changed during regressions')
}

class MemoryStorage {
  values = new Map()
  rejectWrite = null
  get length() { return this.values.size }
  clear() { this.values.clear(); this.rejectWrite = null }
  getItem(key) { return this.values.has(key) ? this.values.get(key) : null }
  key(index) { return [...this.values.keys()][index] ?? null }
  removeItem(key) { this.values.delete(key) }
  setItem(key, value) {
    if (this.rejectWrite?.(key, String(value))) {
      const error = new Error('QuotaExceededError: focused storage rejection')
      error.name = 'QuotaExceededError'
      throw error
    }
    this.values.set(key, String(value))
  }
}

globalThis.localStorage = new MemoryStorage()
const platformState = { account: { id: 'mode-consumer-account', username: '认证玩家' }, token: 'mode-consumer-token' }
const platformWatchers = []
let requestHandler = async requestPath => { throw new Error(`Unexpected platform request ${requestPath}`) }
function sameWatchValue(left, right) {
  return Array.isArray(left) && Array.isArray(right)
    ? left.length === right.length && left.every((value, index) => Object.is(value, right[index]))
    : Object.is(left, right)
}
function platformWatch(source, callback, options = {}) {
  const record = { source, callback, value: source() }
  platformWatchers.push(record)
  if (options.immediate) callback(record.value, undefined)
  return () => platformWatchers.splice(platformWatchers.indexOf(record), 1)
}
function notifyPlatformWatchers() {
  for (const record of platformWatchers) {
    const next = record.source()
    if (sameWatchValue(next, record.value)) continue
    const previous = record.value
    record.value = Array.isArray(next) ? [...next] : next
    record.callback(next, previous)
  }
}
function setIdentity(accountId, token) {
  platformState.account = accountId ? { id: accountId, username: `玩家-${accountId}` } : undefined
  platformState.token = token
  notifyPlatformWatchers()
}
globalThis.__modeConsumerPlatform = {
  platformState,
  watch: platformWatch,
  getEffectiveOperationsPolicy: async () => ({ defaultPresetDeckIds: [] }),
  platformRequest: (...args) => requestHandler(...args),
}

function moduleUrl(source) {
  const output = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  }).outputText
  return 'data:text/javascript;base64,' + Buffer.from(output).toString('base64')
}

async function loadProductionDeckModule() {
  const binaryUrl = moduleUrl(read('src/l12/deckCodeCodec.ts'))
  const codecUrl = moduleUrl(read('src/l12/deckCacheCodec.ts')
    .replace("from './deckCodeCodec'", "from '" + binaryUrl + "'"))
  const storageUrl = moduleUrl(read('src/l12/deckCacheStorage.ts')
    .replace("from './deckCodeCodec'", "from '" + binaryUrl + "'")
    .replace("from './deckCacheCodec'", "from '" + codecUrl + "'"))
  const openingHandImport = `import {
  NORMAL_OPENING_HAND_CARD_TYPES,
  bypassesNormalDrawDeck,
  isDerivedDeckSpecialCard,
  isNormalOpeningHandCard,
  normalOpeningHandCopies,
} from './openingHandEligibility'`
  const executable = read(sources.decks)
    .replace("import { watch } from 'vue'", 'const { watch } = globalThis.__modeConsumerPlatform')
    .replace("from './deckCacheStorage'", "from '" + storageUrl + "'")
    .replace("from './deckCacheCodec'", "from '" + codecUrl + "'")
    .replace("import { normalizeLookupCardType } from './cardPresentation'", 'const normalizeLookupCardType = value => value')
    .replace(
      "import { getEffectiveOperationsPolicy, platformRequest, platformState, type OperationsCardRestriction } from './platform'",
      'const { getEffectiveOperationsPolicy, platformRequest, platformState } = globalThis.__modeConsumerPlatform',
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
  return import(moduleUrl(executable))
}

function scriptFile(relative) {
  const source = read(relative)
  const script = source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1]
  assert.ok(script, `${relative} must contain script setup`)
  const file = ts.createSourceFile(relative, script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  assert.equal(file.parseDiagnostics.length, 0, `${relative} script must parse`)
  return file
}

function mountedCallback(relative) {
  const file = scriptFile(relative)
  const statement = file.statements.find(row => ts.isExpressionStatement(row)
    && ts.isCallExpression(row.expression)
    && row.expression.expression.getText(file) === 'onMounted')
  assert.ok(statement, `${relative} must register onMounted`)
  return statement.expression.arguments[0].getText(file)
}

function initializer(relative, name) {
  const file = scriptFile(relative)
  const declaration = file.statements.filter(ts.isVariableStatement)
    .flatMap(row => row.declarationList.declarations)
    .find(row => row.name.getText(file) === name)
  assert.ok(declaration?.initializer, `${relative} must declare ${name}`)
  return declaration.initializer.getText(file)
}

function functionText(relative, name) {
  const file = scriptFile(relative)
  const declaration = file.statements.find(row => ts.isFunctionDeclaration(row) && row.name?.text === name)
  assert.ok(declaration, `${relative} must declare function ${name}`)
  return declaration.getText(file)
}

function evaluateExpression(expression, dependencies) {
  const source = ts.transpileModule(`const result = ${expression};`, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
  }).outputText
  return new Function(...Object.keys(dependencies), source + '\nreturn result')(...Object.values(dependencies))
}

function createRuntime(body, dependencies) {
  const source = ts.transpileModule(`export function createFocusedRuntime() { ${body} }`, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
  }).outputText
  const module = { exports: {} }
  new Function('module', 'exports', ...Object.keys(dependencies), source)(module, module.exports, ...Object.values(dependencies))
  return module.exports.createFocusedRuntime()
}

const ref = value => ({ value })
const computed = callback => ({ get value() { return callback() } })
const deferred = () => {
  let resolve, reject
  const promise = new Promise((accept, decline) => { resolve = accept; reject = decline })
  return { promise, resolve, reject }
}
const tick = () => new Promise(resolve => queueMicrotask(resolve))

function makeSummary(overrides = {}) {
  const legal = overrides.legal ?? true
  return {
    id: overrides.id ?? 'private-summary-1',
    revision: overrides.revision ?? 7,
    name: overrides.name ?? '只含摘要',
    masterId: overrides.masterId ?? 'S01-01M1',
    updatedAt: overrides.updatedAt ?? '2026-10-07T00:00:00.000Z',
    publicationId: overrides.publicationId ?? null,
    publicationVersion: overrides.publicationVersion ?? null,
    counts: overrides.counts ?? { main: 40, uncountedMain: 0, morale: 8, special: 0, bench: 0 },
    legal,
    legalityReason: overrides.legalityReason ?? (legal ? null : '当前赛季禁限摘要不通过'),
  }
}

function makeBody(id = 'private-summary-1', revision = 7, name = '正文牌库', masterId = 'S01-01M1') {
  return {
    id, revision, publicationId: null, publicationVersion: null, name, masterId,
    cardIds: ['CARD-1'], moraleIds: ['MORALE-1'], specialIds: [], benchIds: [],
    updatedAt: '2026-10-07T00:00:00.000Z',
  }
}

function summaryPageForPath(requestPath) {
  const url = new URL(requestPath, 'https://focused.invalid')
  const legal = url.searchParams.has('legal') ? url.searchParams.get('legal') === 'true' : true
  const masterId = url.searchParams.get('masterId') || 'S01-01M1'
  const item = makeSummary({
    id: `directory-${url.searchParams.get('page')}`,
    revision: Number(url.searchParams.get('page')),
    name: `目录第${url.searchParams.get('page')}页`, masterId, legal,
  })
  return {
    items: [item], total: 1, page: Number(url.searchParams.get('page')),
    pageSize: Number(url.searchParams.get('pageSize')), generation: 12, permissionVersion: 4,
    catalogVersion: 'A'.repeat(64), policyVersion: 9,
    facets: { masters: [{ masterId, count: 1 }], legal: legal ? 1 : 0, illegal: legal ? 0 : 1 },
  }
}

function emptySelectionMap() {
  return { ranked: null, casual: null, friendly: null, 'sandbox-player': null, 'sandbox-opponent': null }
}
function emptyMessageMap() {
  return { ranked: '', casual: '', friendly: '', 'sandbox-player': '', 'sandbox-opponent': '' }
}

async function runAuthenticatedMount(relative) {
  let officialCalls = 0
  const ensureOfficialPrebuiltDecks = async () => { officialCalls++; throw new Error('authenticated mount requested full collection') }
  const cachedDecks = ref({})
  const catalog = ref([])
  const decks = ref({})
  const ranked = ref(null)
  const dependencies = {
    maintenanceClockTimer: 0,
    window: { setInterval: () => 1, addEventListener: () => {} },
    onOperationsResource: () => {},
    platformState,
    identityEpoch: 0,
    identityCurrent: (epoch, accountId, token) => epoch === 0 && platformState.account?.id === accountId && platformState.token === token,
    ensureOfficialPrebuiltDecks,
    loadDeckCatalog: async () => [],
    catalog, cachedDecks, decks, ranked,
    hydrateDeckSelections: () => {}, hydrateScope: () => {},
    deckErrorBelongsToCurrentAccount: () => true,
    l12State: { notice: '', status: 'online' },
    refreshOperationsPolicy: async () => {},
    normalizeRankedOverview: value => value,
    rankedApi: { overview: async () => ({}) },
    connect: async () => {},
  }
  const callback = evaluateExpression(mountedCallback(relative), dependencies)
  await callback()
  return { officialCalls, cachedDecks: cachedDecks.value, catalog: catalog.value }
}

async function runGuestMount(relative) {
  const original = { accountId: platformState.account?.id, token: platformState.token }
  setIdentity(undefined, '')
  const official = makeBody('guest-official', 1, '官方本地预组')
  let officialCalls = 0
  const cachedDecks = ref({})
  const catalog = ref([])
  const ranked = ref(null)
  const hydrate = () => {}
  const dependencies = {
    maintenanceClockTimer: 0,
    window: { setInterval: () => 1, addEventListener: () => {} },
    onOperationsResource: () => {}, platformState, identityEpoch: 0,
    identityCurrent: (epoch, accountId, token) => epoch === 0 && platformState.account?.id === accountId && platformState.token === token,
    ensureOfficialPrebuiltDecks: async () => { officialCalls++; return { [official.name]: official } },
    loadDeckCatalog: async () => [], catalog, cachedDecks, decks: ref({}), ranked,
    hydrateDeckSelections: hydrate, hydrateScope: hydrate, deckErrorBelongsToCurrentAccount: () => true,
    l12State: { notice: '', status: 'offline' }, refreshOperationsPolicy: async () => {},
    normalizeRankedOverview: value => value, rankedApi: { overview: async () => ({}) }, connect: async () => {},
  }
  try {
    await evaluateExpression(mountedCallback(relative), dependencies)()
    return { officialCalls, cachedDecks: cachedDecks.value, official }
  } finally { setIdentity(original.accountId, original.token) }
}

function createDirectoryRuntime(deckModule, props = { open: true, mode: 'casual' }) {
  const keyword = ref('')
  const masterId = ref('')
  const legal = ref('')
  const sort = ref('latest')
  const directoryPage = ref(null)
  const directoryLoading = ref(false)
  const directoryError = ref('')
  const authenticated = computed(() => Boolean(platformState.account?.id && platformState.token))
  return createRuntime(`
    const pageSize = 30
    let selectorEpoch = 0
    let requestEpoch = 0
    let selectorAlive = true
    ${functionText(sources.selector, 'selectorContextCurrent')}
    ${functionText(sources.selector, 'loadDirectoryPage')}
    return {
      loadDirectoryPage,
      bumpSelectorEpoch() { selectorEpoch++; requestEpoch++ },
      unmount() { selectorAlive = false; selectorEpoch++; requestEpoch++ },
      state() { return { page: directoryPage.value, loading: directoryLoading.value, error: directoryError.value } },
    }
  `, {
    props, platformState, authenticated, keyword, masterId, legal, sort,
    directoryPage, directoryLoading, directoryError,
    loadPrivateDeckSummaryPage: deckModule.loadPrivateDeckSummaryPage,
  })
}

function createBattleConfirmHarness(deckModule, scope, { friendlyRestrictions = false, ready = false, validator = null } = {}) {
  const rule = { cardId: 'BANNED-1', maxCopies: 0 }
  const room = scope === 'friendly'
    ? { roomCode: 'ROOM-1', options: { useCardRestrictions: friendlyRestrictions }, players: [{ playerIndex: 0, ready }], yourPlayerIndex: 0 }
    : null
  const l12State = { room, notice: '' }
  const me = computed(() => l12State.room?.players.find(player => player.playerIndex === l12State.room?.yourPlayerIndex))
  const deckSelectorConfirming = ref(false)
  const deckSelectorScope = ref(scope)
  const deckSelectorOpen = ref(true)
  const deckSelectorError = ref('')
  const catalog = ref([{ id: 'catalog-present' }])
  const operationsPolicy = ref({ cardRestrictions: [rule] })
  const roomOptions = ref({ useCardRestrictions: friendlyRestrictions })
  const cachedDecks = ref({})
  const selectedDecks = ref(emptySelectionMap())
  const selectedDeckNames = ref({ ranked: '', casual: '', friendly: '', 'sandbox-player': '', 'sandbox-opponent': '' })
  const selectionMessages = ref(emptyMessageMap())
  const validationCalls = []
  const validateDeck = (deck, cards, restrictions) => {
    validationCalls.push({ deck, cards, restrictions })
    return validator ? validator(deck, cards, restrictions) : ''
  }
  const runtime = createRuntime(`
    let componentAlive = true
    let identityEpoch = 0
    let selectorActionEpoch = 0
    ${functionText(sources.battle, 'friendlyUsesRestrictions')}
    ${functionText(sources.battle, 'scopeNeedsPolicy')}
    ${functionText(sources.battle, 'restrictionsForScope')}
    ${functionText(sources.battle, 'identityCurrent')}
    ${functionText(sources.battle, 'requestStatus')}
    ${functionText(sources.battle, 'bodyFailureMessage')}
    ${functionText(sources.battle, 'cachedSelection')}
    ${functionText(sources.battle, 'confirmDeckSelection')}
    ${functionText(sources.battle, 'cancelDeckSelection')}
    return {
      confirmDeckSelection, cancelDeckSelection,
      bumpIdentity() { identityEpoch++; selectorActionEpoch++ },
      unmount() { componentAlive = false; identityEpoch++; selectorActionEpoch++ },
    }
  `, {
    l12State, me, deckSelectorConfirming, deckSelectorScope, deckSelectorOpen, deckSelectorError,
    catalog, operationsPolicy, roomOptions, platformState, cachedDecks, selectedDecks, selectedDeckNames,
    selectionMessages, validateDeck, loadPrivateDeckBody: deckModule.loadPrivateDeckBody,
    saveSelectedDeckName: deckModule.saveSelectedDeckName,
    deckErrorBelongsToCurrentAccount: deckModule.deckErrorBelongsToCurrentAccount,
  })
  return { runtime, refs: { deckSelectorConfirming, deckSelectorOpen, deckSelectorError, cachedDecks,
    selectedDecks, selectedDeckNames, selectionMessages }, validationCalls, l12State, rule }
}

function createBattleMatchHarness(deckModule, scope = 'ranked') {
  const rule = { cardId: 'BANNED-1', maxCopies: 0 }
  const selection = { id: `match-${scope}`, revision: 3, name: `旧${scope}`, body: makeBody(`match-${scope}`, 3, `旧${scope}`) }
  const selectedDecks = ref({ ...emptySelectionMap(), [scope]: selection })
  const selectedDeckNames = ref({ ranked: '', casual: '', friendly: '', 'sandbox-player': '', 'sandbox-opponent': '', [scope]: selection.name })
  const selectionMessages = ref(emptyMessageMap())
  const cachedDecks = ref({ [selection.name]: selection.body })
  const catalog = ref([{ id: 'catalog-present' }])
  const operationsPolicy = ref({ cardRestrictions: [rule] })
  const roomOptions = ref({ useCardRestrictions: false })
  const selectedMatchMode = ref(scope)
  const tab = ref('match')
  const modeActionPending = ref(false)
  const ranked = ref({ profile: { faction: 'order' } })
  const l12State = { room: null, notice: '' }
  const activeDeckScope = ref(scope)
  const validationCalls = []
  const joins = []
  const runtime = createRuntime(`
    let componentAlive = true
    let identityEpoch = 0
    let modeActionEpoch = 0
    ${functionText(sources.battle, 'friendlyUsesRestrictions')}
    ${functionText(sources.battle, 'scopeNeedsPolicy')}
    ${functionText(sources.battle, 'restrictionsForScope')}
    ${functionText(sources.battle, 'identityCurrent')}
    ${functionText(sources.battle, 'selectionCurrent')}
    ${functionText(sources.battle, 'requestStatus')}
    ${functionText(sources.battle, 'bodyFailureMessage')}
    ${functionText(sources.battle, 'deckError')}
    ${functionText(sources.battle, 'loadSelectedBody')}
    ${functionText(sources.battle, 'onMatch')}
    return { onMatch, bumpIdentity() { identityEpoch++; modeActionEpoch++ }, unmount() { componentAlive = false; identityEpoch++; modeActionEpoch++ } }
  `, {
    platformState, selectedDecks, selectedDeckNames, selectionMessages, cachedDecks, catalog,
    operationsPolicy, roomOptions, selectedMatchMode, tab, modeActionPending, ranked, l12State, activeDeckScope,
    loadPrivateDeckBody: deckModule.loadPrivateDeckBody,
    deckErrorBelongsToCurrentAccount: deckModule.deckErrorBelongsToCurrentAccount,
    validateDeck: (deck, cards, restrictions) => { validationCalls.push({ deck, cards, restrictions }); return '' },
    operationsAllowed: () => true, ensureConnected: async () => true,
    joinMatchmaking: (mode, deck) => joins.push({ mode, deck }),
  })
  return { runtime, refs: { selectedDecks, selectionMessages, selectedMatchMode, modeActionPending }, selection, joins, validationCalls, l12State, rule }
}

function createRoomHarness(deckModule, { useRestrictions = true } = {}) {
  const rule = { cardId: 'BANNED-ROOM', maxCopies: 0 }
  const body = makeBody('friendly-room', 4, '好友房正文')
  const selection = { id: body.id, revision: body.revision, name: '好友房旧缓存', body: { ...body, name: '好友房旧缓存' } }
  const selectedDecks = ref({ ...emptySelectionMap(), friendly: selection })
  const selectedDeckNames = ref({ ranked: '', casual: '', friendly: selection.name, 'sandbox-player': '', 'sandbox-opponent': '' })
  const selectionMessages = ref(emptyMessageMap())
  const cachedDecks = ref({ [selection.name]: selection.body })
  const catalog = ref([{ id: 'catalog-present' }])
  const operationsPolicy = ref({ version: 9, cardRestrictions: [rule] })
  const roomOptions = ref({ useCardRestrictions: useRestrictions })
  const l12State = { room: { roomCode: 'ROOM-READY', options: { useCardRestrictions: useRestrictions }, players: [{ playerIndex: 0, ready: false }], yourPlayerIndex: 0 }, notice: '' }
  const me = computed(() => l12State.room?.players.find(player => player.playerIndex === l12State.room?.yourPlayerIndex))
  const activeDeckScope = ref('friendly')
  const modeActionPending = ref(false)
  const submissions = []
  const readyCalls = []
  const validationCalls = []
  const runtime = createRuntime(`
    let componentAlive = true
    let identityEpoch = 0
    let roomDeckSubmitKey = ''
    let roomDeckSyncKey = ''
    let roomDeckSyncPromise = null
    ${functionText(sources.battle, 'friendlyUsesRestrictions')}
    ${functionText(sources.battle, 'scopeNeedsPolicy')}
    ${functionText(sources.battle, 'restrictionsForScope')}
    ${functionText(sources.battle, 'friendlyRulesKey')}
    ${functionText(sources.battle, 'identityCurrent')}
    ${functionText(sources.battle, 'selectionCurrent')}
    ${functionText(sources.battle, 'requestStatus')}
    ${functionText(sources.battle, 'bodyFailureMessage')}
    ${functionText(sources.battle, 'deckError')}
    ${functionText(sources.battle, 'loadSelectedBody')}
    ${functionText(sources.battle, 'runRoomDeckSync')}
    ${functionText(sources.battle, 'syncCurrentRoomDeck')}
    ${functionText(sources.battle, 'toggleReady')}
    return { syncCurrentRoomDeck, toggleReady, bumpIdentity() { identityEpoch++ }, unmount() { componentAlive = false; identityEpoch++ } }
  `, {
    platformState, selectedDecks, selectedDeckNames, selectionMessages, cachedDecks, catalog,
    operationsPolicy, roomOptions, l12State, me, activeDeckScope, modeActionPending,
    loadPrivateDeckBody: deckModule.loadPrivateDeckBody,
    deckErrorBelongsToCurrentAccount: deckModule.deckErrorBelongsToCurrentAccount,
    validateDeck: (deck, cards, restrictions) => { validationCalls.push({ deck, cards, restrictions }); return '' },
    selectCustomDeck: deck => submissions.push(deck), setReady: value => readyCalls.push(value),
  })
  return { runtime, body, refs: { selectedDecks, selectionMessages, modeActionPending, operationsPolicy }, l12State,
    submissions, readyCalls, validationCalls, rule }
}

function createSandboxHarness(deckModule, player, opponent) {
  const selectedDecks = ref({ 'sandbox-player': player, 'sandbox-opponent': opponent })
  const selectionMessages = ref({ 'sandbox-player': '', 'sandbox-opponent': '' })
  const playerDeckName = ref(player?.name ?? '')
  const opponentDeckName = ref(opponent?.name ?? '')
  const playerDeckError = ref('')
  const opponentDeckError = ref('')
  const cachedDecks = ref({})
  const catalog = ref([{ id: 'catalog-present' }])
  const disasterMode = ref('none')
  const creating = ref(false)
  const l12State = { status: 'online', notice: '' }
  const creations = []
  const validationCalls = []
  const runtime = createRuntime(`
    let componentAlive = true
    let identityEpoch = 0
    let createActionEpoch = 0
    ${functionText(sources.sandbox, 'identityCurrent')}
    ${functionText(sources.sandbox, 'selectionCurrent')}
    ${functionText(sources.sandbox, 'requestStatus')}
    ${functionText(sources.sandbox, 'bodyFailureMessage')}
    ${functionText(sources.sandbox, 'loadSelectionBody')}
    ${functionText(sources.sandbox, 'startSandbox')}
    return { startSandbox, bumpIdentity() { identityEpoch++; createActionEpoch++ }, unmount() { componentAlive = false; identityEpoch++; createActionEpoch++ } }
  `, {
    platformState, selectedDecks, selectionMessages, playerDeckName, opponentDeckName,
    playerDeckError, opponentDeckError, cachedDecks, catalog, disasterMode, creating, l12State,
    loadPrivateDeckBody: deckModule.loadPrivateDeckBody,
    deckErrorBelongsToCurrentAccount: deckModule.deckErrorBelongsToCurrentAccount,
    validateDeck: (...args) => { validationCalls.push(args); return '' },
    connect: async () => {}, createSandbox: (playerBody, opponentBody, mode) => creations.push({ playerBody, opponentBody, mode }),
  })
  return { runtime, refs: { selectedDecks, selectionMessages, disasterMode, creating }, l12State, creations, validationCalls }
}

function createSandboxConfirmHarness(deckModule, scope) {
  const selectorTarget = ref(scope)
  const selectorConfirming = ref(false)
  const creating = ref(false)
  const selectorError = ref('')
  const catalog = ref([{ id: 'catalog-present' }])
  const cachedDecks = ref({})
  const selectedDecks = ref({ 'sandbox-player': null, 'sandbox-opponent': null })
  const selectionMessages = ref({ 'sandbox-player': '', 'sandbox-opponent': '' })
  const playerDeckName = ref('')
  const opponentDeckName = ref('')
  const l12State = { notice: '' }
  const validationCalls = []
  const runtime = createRuntime(`
    let componentAlive = true
    let identityEpoch = 0
    let selectorActionEpoch = 0
    ${functionText(sources.sandbox, 'identityCurrent')}
    ${functionText(sources.sandbox, 'requestStatus')}
    ${functionText(sources.sandbox, 'bodyFailureMessage')}
    ${functionText(sources.sandbox, 'confirmDeckSelection')}
    ${functionText(sources.sandbox, 'cancelDeckSelection')}
    return { confirmDeckSelection, cancelDeckSelection, bumpIdentity() { identityEpoch++; selectorActionEpoch++ }, unmount() { componentAlive = false; identityEpoch++; selectorActionEpoch++ } }
  `, {
    platformState, selectorTarget, selectorConfirming, creating, selectorError, catalog, cachedDecks,
    selectedDecks, selectionMessages, playerDeckName, opponentDeckName, l12State,
    loadPrivateDeckBody: deckModule.loadPrivateDeckBody,
    saveSelectedDeckName: deckModule.saveSelectedDeckName,
    deckErrorBelongsToCurrentAccount: deckModule.deckErrorBelongsToCurrentAccount,
    validateDeck: (...args) => { validationCalls.push(args); return '' },
  })
  return { runtime, refs: { selectorTarget, selectorConfirming, selectorError, selectedDecks,
    selectionMessages, playerDeckName, opponentDeckName, cachedDecks }, validationCalls }
}

async function main() {
  requireUnchangedCore()
  const deckModule = await loadProductionDeckModule()
  const cases = []
  async function check(name, polarity, callback) {
    try {
      await callback()
      cases.push({ name, polarity, passed: true })
    } catch (error) {
      cases.push({ name, polarity, passed: false, error: error instanceof Error ? error.stack || error.message : String(error) })
    }
  }

  await check('three leased SFCs parse and compile script, template and scoped styles', 'positive', () => {
    for (const relative of [sources.battle, sources.sandbox, sources.selector]) {
      const filename = path.join(projectRoot, relative)
      const parsed = parse(read(relative), { filename })
      assert.deepEqual(parsed.errors, [], `${relative} SFC parse diagnostics`)
      const id = sha256(relative).slice(0, 8)
      const compiledScript = compileScript(parsed.descriptor, { id })
      assert.ok(compiledScript.content.length > 0)
      if (parsed.descriptor.template) {
        const template = compileTemplate({ source: parsed.descriptor.template.content, filename, id,
          compilerOptions: { bindingMetadata: compiledScript.bindings } })
        assert.deepEqual(template.errors, [], `${relative} template diagnostics`)
      }
      for (const style of parsed.descriptor.styles) {
        if (style.src) continue
        const result = compileStyle({ source: style.content, filename, id, scoped: style.scoped })
        assert.deepEqual(result.errors, [], `${relative} style diagnostics`)
      }
    }
  })

  await check('authenticated BattleHub and Sandbox mounts issue no legacy full collection read', 'negative', async () => {
    setIdentity('mode-consumer-account', 'mode-consumer-token')
    const battle = await runAuthenticatedMount(sources.battle)
    const sandbox = await runAuthenticatedMount(sources.sandbox)
    assert.equal(battle.officialCalls, 0)
    assert.equal(sandbox.officialCalls, 0)
  })

  await check('guest mounts retain the official local body seeding path', 'positive', async () => {
    const battle = await runGuestMount(sources.battle)
    const sandbox = await runGuestMount(sources.sandbox)
    assert.equal(battle.officialCalls, 1)
    assert.equal(sandbox.officialCalls, 1)
    assert.deepEqual(battle.cachedDecks['官方本地预组'], battle.official)
    assert.deepEqual(sandbox.cachedDecks['官方本地预组'], sandbox.official)
  })

  await check('authenticated selector executes one bounded real summary helper request', 'positive', async () => {
    setIdentity('mode-consumer-account', 'mode-consumer-token')
    const paths = []
    requestHandler = async requestPath => { paths.push(requestPath); return summaryPageForPath(requestPath) }
    const runtime = createDirectoryRuntime(deckModule)
    await runtime.loadDirectoryPage(2)
    assert.equal(paths.length, 1)
    assert.equal(paths[0], '/api/decks/summaries?page=2&pageSize=30&sort=latest')
    assert.equal(runtime.state().page.page, 2)
    assert.equal(paths.some(value => value === '/api/decks'), false)
    runtime.unmount()
  })

  await check('real summary helper validates keyword/master/legal/name query and selector never loops pages', 'positive', async () => {
    const paths = []
    requestHandler = async requestPath => { paths.push(requestPath); return summaryPageForPath(requestPath) }
    const props = { open: true, mode: 'friendly' }
    const keyword = ref('火焰')
    const masterId = ref('S02-02M1')
    const legal = ref('illegal')
    const sort = ref('name')
    const directoryPage = ref(null), directoryLoading = ref(false), directoryError = ref('')
    const authenticated = computed(() => true)
    const runtime = createRuntime(`
      const pageSize = 30
      let selectorEpoch = 0, requestEpoch = 0, selectorAlive = true
      ${functionText(sources.selector, 'selectorContextCurrent')}
      ${functionText(sources.selector, 'loadDirectoryPage')}
      return { loadDirectoryPage }
    `, { props, platformState, authenticated, keyword, masterId, legal, sort, directoryPage, directoryLoading, directoryError,
      loadPrivateDeckSummaryPage: deckModule.loadPrivateDeckSummaryPage })
    await runtime.loadDirectoryPage(3)
    assert.equal(paths.length, 1)
    assert.equal(paths[0], '/api/decks/summaries?page=3&pageSize=30&sort=name&keyword=%E7%81%AB%E7%84%B0&masterId=S02-02M1&legal=false')
  })

  await check('selector ignores late success, late failure and A-B-A directory results', 'negative', async () => {
    setIdentity('mode-consumer-account', 'mode-consumer-token')
    const runtime = createDirectoryRuntime(deckModule)
    const first = deferred(), second = deferred()
    let call = 0
    requestHandler = () => (++call === 1 ? first.promise : second.promise)
    const p1 = runtime.loadDirectoryPage(4)
    const p2 = runtime.loadDirectoryPage(5)
    second.resolve(summaryPageForPath('/api/decks/summaries?page=5&pageSize=30&sort=latest'))
    await p2
    first.resolve(summaryPageForPath('/api/decks/summaries?page=4&pageSize=30&sort=latest'))
    await p1
    assert.equal(runtime.state().page.page, 5)
    const lateFailure = deferred(), current = deferred()
    call = 0
    requestHandler = () => (++call === 1 ? lateFailure.promise : current.promise)
    const old = runtime.loadDirectoryPage(6)
    const fresh = runtime.loadDirectoryPage(7)
    current.resolve(summaryPageForPath('/api/decks/summaries?page=7&pageSize=30&sort=latest'))
    await fresh
    lateFailure.reject(new Error('迟到失败'))
    await old
    assert.equal(runtime.state().page.page, 7)
    assert.equal(runtime.state().error, '')
    const aba = deferred()
    requestHandler = () => aba.promise
    const stale = runtime.loadDirectoryPage(8)
    runtime.bumpSelectorEpoch(); setIdentity('mode-consumer-account-b', 'token-b')
    runtime.bumpSelectorEpoch(); setIdentity('mode-consumer-account', 'mode-consumer-token')
    aba.resolve(summaryPageForPath('/api/decks/summaries?page=8&pageSize=30&sort=latest'))
    await stale
    assert.equal(runtime.state().page.page, 7)
    assert.equal(runtime.state().error, '')
  })

  await check('selector renders authenticated summaries without fabricated arrays or body validation', 'negative', () => {
    const summary = makeSummary()
    const directoryPage = ref({ items: [summary], total: 1, facets: { masters: [], legal: 1, illegal: 0 } })
    const serverRows = evaluateExpression(initializer(sources.selector, 'serverRows'), { computed, directoryPage })
    const rows = evaluateExpression(initializer(sources.selector, 'rows'), {
      computed, authenticated: ref(true), serverRows, guestRows: ref([]),
    })
    assert.equal(rows.value.length, 1)
    assert.strictEqual(rows.value[0].deck, summary)
    assert.equal('cardIds' in rows.value[0].deck, false)
    const draftId = ref('selection-outside-page'), draftRevision = ref(99), draftName = ref('原选择')
    const selected = evaluateExpression(initializer(sources.selector, 'selected'), {
      computed, authenticated: ref(true), serverRows, draftId, draftRevision, guestRows: ref([]), draftName,
    })
    const selectedOutsidePage = evaluateExpression(initializer(sources.selector, 'selectedOutsidePage'), {
      computed, authenticated: ref(true), draftId, selected,
    })
    assert.equal(selected.value, undefined)
    assert.equal(selectedOutsidePage.value, true)
  })

  await check('guest selector validates only real local bodies and retains their arrays', 'positive', () => {
    const body = makeBody('guest-body', 1, '游客正文')
    let validated
    const props = { guestDecks: [body], loading: false, catalog: [{ id: 'catalog-present' }], restrictions: [] }
    const guestRows = evaluateExpression(initializer(sources.selector, 'guestRows'), {
      computed, props, validateDeck: deck => { validated = deck; return '' },
    })
    assert.strictEqual(validated, undefined)
    assert.equal(guestRows.value.length, 1)
    assert.strictEqual(validated, body)
    assert.deepEqual(guestRows.value[0].deck.cardIds, ['CARD-1'])
  })

  await check('current-season summary legality is informational outside restricted modes', 'negative', () => {
    const summary = makeSummary({ legal: false })
    const props = { usesSeasonRestrictions: false }
    const rowStatus = createRuntime(`${functionText(sources.selector, 'rowStatus')}; return { rowStatus }`, { props })
    assert.match(rowStatus.rowStatus(summary, ''), /不用于此模式/)
    props.usesSeasonRestrictions = true
    assert.match(rowStatus.rowStatus(summary, ''), /当前赛季/)
    assert.match(rowStatus.rowStatus(summary, ''), new RegExp(summary.legalityReason))
  })

  await check('selector confirm rejects repeated parent-confirming trigger', 'negative', () => {
    const emitted = []
    const selected = ref({ deck: makeSummary(), error: '' })
    const props = { disabled: false, loading: false, confirming: true }
    const runtime = createRuntime(`${functionText(sources.selector, 'confirm')}; return { confirm }`, {
      props, selected, emit: (...args) => emitted.push(args),
    })
    runtime.confirm()
    assert.equal(emitted.length, 0)
    props.confirming = false
    runtime.confirm()
    assert.equal(emitted.length, 1)
    assert.strictEqual(emitted[0][1], selected.value.deck)
    props.loading = true
    runtime.confirm()
    assert.equal(emitted.length, 1)
  })

  for (const scenario of [
    { scope: 'ranked', friendlyRestrictions: false, expectRestriction: true, legal: true },
    { scope: 'casual', friendlyRestrictions: false, expectRestriction: false, legal: false },
    { scope: 'friendly', friendlyRestrictions: false, expectRestriction: false, legal: false },
    { scope: 'friendly', friendlyRestrictions: true, expectRestriction: true, legal: true },
  ]) {
    await check(`${scenario.scope}${scenario.friendlyRestrictions ? '-restricted' : ''} confirm expands only selected stable body and applies original scope rules`, 'positive', async () => {
      localStorage.clear()
      setIdentity('mode-consumer-account', 'mode-consumer-token')
      const id = `confirm-${scenario.scope}-${scenario.friendlyRestrictions}`
      const summary = makeSummary({ id, revision: 2, name: `摘要-${id}`, legal: scenario.legal })
      const body = makeBody(id, 2, `正文-${id}`)
      const pending = deferred()
      const paths = []
      requestHandler = requestPath => { paths.push(requestPath); return pending.promise }
      const harness = createBattleConfirmHarness(deckModule, scenario.scope, scenario)
      const first = harness.runtime.confirmDeckSelection(summary)
      await tick()
      await harness.runtime.confirmDeckSelection(summary)
      assert.equal(paths.length, 1)
      assert.equal(paths[0], `/api/decks/by-id/${encodeURIComponent(id)}?expectedRevision=2`)
      assert.equal(harness.refs.selectedDecks.value[scenario.scope], null)
      pending.resolve(body)
      await first
      assert.strictEqual(harness.refs.selectedDecks.value[scenario.scope].body, harness.refs.cachedDecks.value[body.name])
      assert.equal(harness.refs.selectedDecks.value[scenario.scope].id, id)
      assert.equal(harness.validationCalls.length, 1)
      assert.deepEqual(harness.validationCalls[0].restrictions, scenario.expectRestriction ? [harness.rule] : [])
      assert.equal(localStorage.getItem(`l12-selected-custom-deck:mode-consumer-account:${scenario.scope}`), id)
      assert.equal(harness.refs.deckSelectorOpen.value, false)
    })
  }

  for (const scope of ['sandbox-player', 'sandbox-opponent']) {
    await check(`${scope} confirm ignores season summary and validates the real unrestricted body`, 'positive', async () => {
      localStorage.clear()
      const id = `confirm-${scope}`
      const summary = makeSummary({ id, revision: 5, legal: false })
      const body = makeBody(id, 5, `正文-${scope}`)
      const paths = []
      requestHandler = async requestPath => { paths.push(requestPath); return body }
      const harness = createSandboxConfirmHarness(deckModule, scope)
      await harness.runtime.confirmDeckSelection(summary)
      assert.deepEqual(paths, [`/api/decks/by-id/${encodeURIComponent(id)}?expectedRevision=5`])
      assert.strictEqual(harness.refs.selectedDecks.value[scope].body, harness.refs.cachedDecks.value[body.name])
      assert.equal(harness.validationCalls.length, 1)
      assert.equal(harness.validationCalls[0].length, 2)
      assert.equal(localStorage.getItem(`l12-selected-custom-deck:mode-consumer-account:${scope}`), id)
    })
  }

  await check('production validateDeck rejects an expanded invalid body before scope selection changes', 'negative', async () => {
    localStorage.clear()
    const summary = makeSummary({ id: 'invalid-expanded-body', revision: 2, legal: true })
    const body = makeBody(summary.id, summary.revision, '不完整正文')
    requestHandler = async () => body
    const harness = createBattleConfirmHarness(deckModule, 'casual', { validator: deckModule.validateDeck })
    await harness.runtime.confirmDeckSelection(summary)
    assert.equal(harness.validationCalls.length, 1)
    assert.strictEqual(harness.validationCalls[0].deck.id, body.id)
    assert.match(harness.refs.deckSelectorError.value, /主宰|主牌|士气|卡牌/)
    assert.equal(harness.refs.selectedDecks.value.casual, null)
    assert.equal(localStorage.getItem('l12-selected-custom-deck:mode-consumer-account:casual'), null)
  })

  await check('cancel invalidates a pending confirm without changing the prior selection', 'negative', async () => {
    localStorage.clear()
    const summary = makeSummary({ id: 'cancel-candidate', revision: 2 })
    const pending = deferred()
    requestHandler = () => pending.promise
    const harness = createBattleConfirmHarness(deckModule, 'casual')
    const old = { id: 'old-casual', revision: 1, name: '旧选择', body: makeBody('old-casual', 1, '旧选择') }
    harness.refs.selectedDecks.value.casual = old
    const task = harness.runtime.confirmDeckSelection(summary)
    await tick()
    harness.runtime.cancelDeckSelection()
    pending.resolve(makeBody(summary.id, summary.revision, '迟到正文'))
    await task
    assert.strictEqual(harness.refs.selectedDecks.value.casual, old)
    assert.equal(harness.refs.deckSelectorError.value, '')
    assert.equal(localStorage.getItem('l12-selected-custom-deck:mode-consumer-account:casual'), null)
  })

  await check('A-B-A identity switch ignores pending confirm success', 'negative', async () => {
    localStorage.clear()
    setIdentity('mode-consumer-account', 'mode-consumer-token')
    const summary = makeSummary({ id: 'aba-candidate', revision: 2 })
    const pending = deferred()
    requestHandler = () => pending.promise
    const harness = createBattleConfirmHarness(deckModule, 'casual')
    const task = harness.runtime.confirmDeckSelection(summary)
    await tick()
    harness.runtime.bumpIdentity(); setIdentity('mode-consumer-account-b', 'token-b')
    harness.runtime.bumpIdentity(); setIdentity('mode-consumer-account', 'mode-consumer-token')
    pending.resolve(makeBody(summary.id, summary.revision, 'ABA迟到正文'))
    await task
    assert.equal(harness.refs.selectedDecks.value.casual, null)
    assert.equal(harness.refs.deckSelectorError.value, '')
  })

  await check('component unmount ignores pending confirm success and failure', 'negative', async () => {
    localStorage.clear()
    const success = deferred()
    requestHandler = () => success.promise
    const firstHarness = createBattleConfirmHarness(deckModule, 'casual')
    const summary = makeSummary({ id: 'unmount-success', revision: 2 })
    const first = firstHarness.runtime.confirmDeckSelection(summary)
    await tick()
    firstHarness.runtime.unmount()
    success.resolve(makeBody(summary.id, summary.revision, '卸载迟到正文'))
    await first
    assert.equal(firstHarness.refs.selectedDecks.value.casual, null)
    assert.equal(firstHarness.refs.deckSelectorError.value, '')

    const failure = deferred()
    requestHandler = () => failure.promise
    const secondHarness = createBattleConfirmHarness(deckModule, 'casual')
    const second = secondHarness.runtime.confirmDeckSelection(makeSummary({ id: 'unmount-failure', revision: 2 }))
    await tick()
    secondHarness.runtime.unmount()
    failure.reject(Object.assign(new Error('卸载后的迟到失败'), { status: 404 }))
    await second
    assert.equal(secondHarness.refs.selectedDecks.value.casual, null)
    assert.equal(secondHarness.refs.deckSelectorError.value, '')
  })

  for (const status of [404, 409]) {
    await check(`${status} candidate confirm does not mark, delete or replace the current scope and never retries`, 'negative', async () => {
      localStorage.clear()
      const harness = createBattleConfirmHarness(deckModule, 'ranked')
      const old = { id: 'ranked-old', revision: 1, name: '原排位', body: makeBody('ranked-old', 1, '原排位') }
      harness.refs.selectedDecks.value.ranked = old
      let requests = 0
      requestHandler = async () => { requests++; throw Object.assign(new Error(`wire ${status}`), { status }) }
      await harness.runtime.confirmDeckSelection(makeSummary({ id: `candidate-${status}`, revision: 3 }))
      assert.equal(requests, 1)
      assert.strictEqual(harness.refs.selectedDecks.value.ranked, old)
      assert.equal(harness.refs.selectionMessages.value.ranked, '')
      assert.match(harness.refs.deckSelectorError.value, status === 404 ? /重新选择/ : /刷新目录.*未自动重试/)
    })
  }

  await check('storage rejection cannot half-commit a selected scope or claim server confirmation', 'negative', async () => {
    localStorage.clear()
    const body = makeBody('quota-body', 2, '配额失败正文')
    requestHandler = async () => body
    localStorage.rejectWrite = key => key.startsWith('l12-deck-cache-activity-v1:')
    let error
    try { await deckModule.loadPrivateDeckBody({ id: body.id, revision: body.revision }) }
    catch (caught) { error = caught }
    finally { localStorage.rejectWrite = null }
    assert.ok(error instanceof Error)
    assert.equal(error.serverConfirmed, false)
    assert.deepEqual(deckModule.loadSavedDecks(), {})

    localStorage.clear()
    const harness = createBattleConfirmHarness(deckModule, 'casual')
    const old = { id: 'quota-old', revision: 1, name: '配额前选择', body: makeBody('quota-old', 1, '配额前选择') }
    harness.refs.selectedDecks.value.casual = old
    requestHandler = async () => body
    localStorage.rejectWrite = key => key === 'l12-selected-custom-deck:mode-consumer-account:casual'
    try { await harness.runtime.confirmDeckSelection(makeSummary({ id: body.id, revision: body.revision })) }
    finally { localStorage.rejectWrite = null }
    assert.strictEqual(harness.refs.selectedDecks.value.casual, old)
    assert.equal(localStorage.getItem('l12-selected-custom-deck:mode-consumer-account:casual'), null)
    assert.match(harness.refs.deckSelectorError.value, /QuotaExceededError/)
  })

  await check('ranked matchmaking fetches exact revision once and submits the same validated real body', 'positive', async () => {
    localStorage.clear()
    const harness = createBattleMatchHarness(deckModule, 'ranked')
    const body = makeBody(harness.selection.id, harness.selection.revision, '排位最新正文')
    const pending = deferred()
    const paths = []
    requestHandler = requestPath => { paths.push(requestPath); return pending.promise }
    const first = harness.runtime.onMatch()
    await tick()
    await harness.runtime.onMatch()
    assert.equal(paths.length, 1)
    pending.resolve(body)
    await first
    assert.deepEqual(paths, [`/api/decks/by-id/${encodeURIComponent(body.id)}?expectedRevision=${body.revision}`])
    assert.equal(harness.joins.length, 1)
    assert.strictEqual(harness.joins[0].deck, harness.validationCalls[0].deck)
    assert.deepEqual(harness.validationCalls[0].restrictions, [harness.rule])
  })

  await check('mode change during pending matchmaking prevents late submission', 'negative', async () => {
    localStorage.clear()
    const harness = createBattleMatchHarness(deckModule, 'ranked')
    const pending = deferred()
    requestHandler = () => pending.promise
    const task = harness.runtime.onMatch()
    await tick()
    harness.refs.selectedMatchMode.value = 'casual'
    pending.resolve(makeBody(harness.selection.id, harness.selection.revision, '迟到排位正文'))
    await task
    assert.equal(harness.joins.length, 0)
  })

  await check('current matchmaking 404 marks only that scope and does not retry', 'negative', async () => {
    localStorage.clear()
    const harness = createBattleMatchHarness(deckModule, 'ranked')
    let requests = 0
    requestHandler = async () => { requests++; throw Object.assign(new Error('missing'), { status: 404 }) }
    await harness.runtime.onMatch()
    assert.equal(requests, 1)
    assert.match(harness.refs.selectionMessages.value.ranked, /重新选择/)
    assert.equal(harness.refs.selectionMessages.value.casual, '')
  })

  await check('room sync deduplicates exact body submission and ready waits for it', 'positive', async () => {
    localStorage.clear()
    const harness = createRoomHarness(deckModule, { useRestrictions: true })
    const pending = deferred()
    const paths = []
    requestHandler = requestPath => { paths.push(requestPath); return pending.promise }
    const first = harness.runtime.syncCurrentRoomDeck()
    const duplicate = harness.runtime.syncCurrentRoomDeck()
    await tick()
    assert.equal(paths.length, 1)
    pending.resolve(harness.body)
    assert.equal(await first, true)
    assert.equal(await duplicate, true)
    assert.equal(harness.submissions.length, 1)
    assert.strictEqual(harness.submissions[0], harness.validationCalls[0].deck)
    assert.deepEqual(harness.validationCalls[0].restrictions, [harness.rule])
    harness.l12State.room.options.useCardRestrictions = false
    requestHandler = async requestPath => { paths.push(requestPath); return harness.body }
    assert.equal(await harness.runtime.syncCurrentRoomDeck(), true)
    assert.equal(paths.length, 2)
    assert.equal(harness.submissions.length, 2)
    assert.deepEqual(harness.validationCalls[1].restrictions, [])
    harness.refs.operationsPolicy.value.version = 10
    assert.equal(await harness.runtime.syncCurrentRoomDeck(), true)
    assert.equal(paths.length, 2)
    await harness.runtime.toggleReady()
    assert.deepEqual(harness.readyCalls, [true])
    assert.equal(paths.length, 2)
    harness.l12State.room.players[0].ready = true
    await harness.runtime.toggleReady()
    assert.deepEqual(harness.readyCalls, [true, false])
  })

  await check('room change during body read prevents stale room submission', 'negative', async () => {
    localStorage.clear()
    const harness = createRoomHarness(deckModule)
    const pending = deferred()
    requestHandler = () => pending.promise
    const task = harness.runtime.syncCurrentRoomDeck()
    await tick()
    harness.l12State.room.roomCode = 'ROOM-CHANGED'
    pending.resolve(harness.body)
    assert.equal(await task, false)
    assert.equal(harness.submissions.length, 0)
  })

  await check('ready room locks deck confirmation before any body request', 'negative', async () => {
    localStorage.clear()
    const harness = createBattleConfirmHarness(deckModule, 'friendly', { ready: true })
    let requests = 0
    requestHandler = async () => { requests++; return makeBody('ready-lock', 2) }
    await harness.runtime.confirmDeckSelection(makeSummary({ id: 'ready-lock', revision: 2 }))
    assert.equal(requests, 0)
    assert.equal(harness.refs.selectedDecks.value.friendly, null)
  })

  await check('sandbox start expands both exact revisions and submits those same unrestricted bodies', 'positive', async () => {
    localStorage.clear()
    const playerBody = makeBody('sandbox-player-body', 2, '我方正文')
    const opponentBody = makeBody('sandbox-opponent-body', 6, '对手正文')
    const player = { id: playerBody.id, revision: playerBody.revision, name: '我方旧缓存', body: { ...playerBody, name: '我方旧缓存' } }
    const opponent = { id: opponentBody.id, revision: opponentBody.revision, name: '对手旧缓存', body: { ...opponentBody, name: '对手旧缓存' } }
    const harness = createSandboxHarness(deckModule, player, opponent)
    const paths = []
    requestHandler = async requestPath => {
      paths.push(requestPath)
      return requestPath.includes(encodeURIComponent(playerBody.id)) ? playerBody : opponentBody
    }
    const first = harness.runtime.startSandbox()
    const duplicate = harness.runtime.startSandbox()
    await Promise.all([first, duplicate])
    assert.deepEqual(paths, [
      `/api/decks/by-id/${encodeURIComponent(playerBody.id)}?expectedRevision=${playerBody.revision}`,
      `/api/decks/by-id/${encodeURIComponent(opponentBody.id)}?expectedRevision=${opponentBody.revision}`,
    ])
    assert.equal(harness.creations.length, 1)
    assert.strictEqual(harness.creations[0].playerBody, harness.validationCalls[0][0])
    assert.strictEqual(harness.creations[0].opponentBody, harness.validationCalls[1][0])
    assert.equal(harness.validationCalls[0].length, 2)
    assert.equal(harness.validationCalls[1].length, 2)
  })

  await check('sandbox reuses one exact body when both sides choose the same stable revision', 'positive', async () => {
    localStorage.clear()
    const body = makeBody('sandbox-shared', 8, '双方正文')
    const selection = { id: body.id, revision: body.revision, name: '双方缓存', body }
    const harness = createSandboxHarness(deckModule, selection, { ...selection })
    let requests = 0
    requestHandler = async () => { requests++; return body }
    await harness.runtime.startSandbox()
    assert.equal(requests, 1)
    assert.equal(harness.creations.length, 1)
    assert.strictEqual(harness.creations[0].playerBody, harness.creations[0].opponentBody)
  })

  await check('sandbox mode change during body read prevents late creation', 'negative', async () => {
    localStorage.clear()
    const body = makeBody('sandbox-mode-change', 2, '模式迟到正文')
    const selection = { id: body.id, revision: body.revision, name: body.name, body }
    const harness = createSandboxHarness(deckModule, selection, { ...selection })
    const pending = deferred()
    requestHandler = () => pending.promise
    const task = harness.runtime.startSandbox()
    await tick()
    harness.refs.disasterMode.value = 'all'
    pending.resolve(body)
    await task
    assert.equal(harness.creations.length, 0)
  })

  await check('sandbox opponent 409 marks only opponent, refreshes explicitly and never retries', 'negative', async () => {
    localStorage.clear()
    const playerBody = makeBody('sandbox-ok-player', 2, '我方正文')
    const opponentBody = makeBody('sandbox-stale-opponent', 3, '对手正文')
    const harness = createSandboxHarness(deckModule,
      { id: playerBody.id, revision: 2, name: playerBody.name, body: playerBody },
      { id: opponentBody.id, revision: 3, name: opponentBody.name, body: opponentBody })
    let requests = 0
    requestHandler = async requestPath => {
      requests++
      if (requestPath.includes(encodeURIComponent(playerBody.id))) return playerBody
      throw Object.assign(new Error('conflict'), { status: 409 })
    }
    await harness.runtime.startSandbox()
    assert.equal(requests, 2)
    assert.equal(harness.refs.selectionMessages.value['sandbox-player'], '')
    assert.match(harness.refs.selectionMessages.value['sandbox-opponent'], /刷新目录.*未自动重试/)
    assert.equal(harness.creations.length, 0)
  })

  await check('stored scope id without cached revision remains untouched and demands re-selection', 'negative', () => {
    localStorage.clear()
    localStorage.setItem('l12-selected-custom-deck:mode-consumer-account:ranked', 'server-only-id')
    const cachedDecks = ref({})
    const selectedDeckNames = ref({ ranked: '', casual: '', friendly: '', 'sandbox-player': '', 'sandbox-opponent': '' })
    const selectedDecks = ref(emptySelectionMap())
    const selectionMessages = ref(emptyMessageMap())
    const runtime = createRuntime(`
      ${functionText(sources.battle, 'selectionStorageBase')}
      ${functionText(sources.battle, 'hasStoredSelection')}
      ${functionText(sources.battle, 'hydrateDeckSelections')}
      return { hydrateDeckSelections }
    `, { platformState, SELECTED_DECK_KEY: deckModule.SELECTED_DECK_KEY,
      L12_DECK_SELECTION_SCOPES: deckModule.L12_DECK_SELECTION_SCOPES,
      loadSelectedDeckName: () => '', cachedDecks, selectedDeckNames, selectedDecks, selectionMessages })
    runtime.hydrateDeckSelections()
    assert.equal(selectedDecks.value.ranked, null)
    assert.match(selectionMessages.value.ranked, /缺少可验证的服务器修订.*重新选择/)
    assert.equal(localStorage.getItem('l12-selected-custom-deck:mode-consumer-account:ranked'), 'server-only-id')
  })

  const passed = cases.filter(row => row.passed).length
  const failed = cases.filter(row => !row.passed).length
  const positive = cases.filter(row => row.polarity === 'positive')
  const negative = cases.filter(row => row.polarity === 'negative')
  requireUnchangedCore()
  const receipt = {
    schema: 2,
    kind: 'private-deck-mode-consumers-focused',
    generatedAt: new Date().toISOString(),
    immutableInitialRed: {
      testRawSha256: baselines.focusedRed.rawSha256,
      receipt: 'D:/GPT/Legion12/artifacts/deck-private-modes-20261007/initial-authenticated-mount-red.json',
      archive: 'D:/GPT/Legion12/artifacts/deck-private-modes-20261007/initial-red-source-3D8CC4B5',
    },
    baselines,
    manifest,
    originalCu4Core,
    coreBinding: { ...expectedCore, actualRawSha256: manifest.decks.rawSha256, actualLfSha256: manifest.decks.lfSha256 },
    totals: { total: cases.length, passed, failed, positive: positive.length, negative: negative.length, skipped: 0 },
    cases,
    dependencyLimits: [
      'Shared platform session/wire regressions run separately in test-platform-session-boundaries.mjs; this consumer fixture does not substitute injected errors for that transport evidence',
      'Focused Node compiles SFC blocks and executes production functions/helpers; it does not claim the full browser, full typecheck, Vite build, npm suite, .NET suite, or real storage-engine family gate',
    ],
  }
  if (process.env.L12_MODE_CONSUMER_RECEIPT) {
    const target = path.resolve(process.env.L12_MODE_CONSUMER_RECEIPT)
    fs.mkdirSync(path.dirname(target), { recursive: true })
    fs.writeFileSync(target, JSON.stringify(receipt, null, 2) + '\n')
  }
  const summary = `Private deck mode consumer Focused result: ${passed}/${cases.length}, positive=${positive.filter(row => row.passed).length}/${positive.length}, negative=${negative.filter(row => row.passed).length}/${negative.length}, failed=${failed}, skipped=0`
  if (process.env.L12_MODE_CONSUMER_LOG) {
    const target = path.resolve(process.env.L12_MODE_CONSUMER_LOG)
    fs.mkdirSync(path.dirname(target), { recursive: true })
    fs.writeFileSync(target, summary + '\n' + cases.filter(row => !row.passed).map(row => `${row.name}\n${row.error}`).join('\n') + '\n')
  }
  console.log(summary)
  if (failed) {
    for (const row of cases.filter(item => !item.passed)) console.error(`FAIL ${row.name}\n${row.error}`)
    process.exitCode = 1
  }
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url)
  await main()
