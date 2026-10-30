import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import * as Vue from 'vue'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'

const pages = ['TournamentPublicDetail', 'TournamentHubPage', 'TournamentAccountDetail', 'AdminTournamentWorkbench']
const compile = text => ts.transpileModule(text, { compilerOptions: {
  target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext,
} }).outputText.replace(/^"use strict";\s*/, '').trim().replace(/;$/, '')
const source = name => {
  const sfc = fs.readFileSync(new URL(`../src/l12/site/${name}.vue`, import.meta.url), 'utf8')
  const script = sfc.match(/<script setup(?: lang="ts")?>([\s\S]*?)<\/script>/)?.[1]
  assert(script)
  return ts.createSourceFile(`${name}.ts`, script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
}
const variable = (tree, name) => tree.statements.flatMap(node => ts.isVariableStatement(node)
  ? [...node.declarationList.declarations] : []).find(node => node.name.getText(tree) === name)
const fn = (tree, name) => tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
const helperUrl = new URL('../src/l12/visibleResourceRead.ts', import.meta.url)
const helper = fs.existsSync(helperUrl)
  ? await import(`data:text/javascript;base64,${Buffer.from(compile(fs.readFileSync(helperUrl, 'utf8'))).toString('base64')}`)
  : null
const hiddenResults = []
for (const name of pages) {
  const tree = source(name)
  const event = variable(tree, name === 'AdminTournamentWorkbench' ? 'onTournamentResource' : 'onResource')
  assert(event?.initializer, `${name}: execute the actual resource callback`)
  let reads = 0
  const load = () => { reads++; return Promise.resolve() }
  const scheduler = helper?.createVisibleResourceRead({ visible: () => false, invalidate: () => {}, load })
  const callback = new Function('load', 'refreshTournaments', 'visibleTournamentRead', 'actorEpoch',
    'componentAlive', 'notice', 'errorText', `return (${compile(event.initializer.getText(tree))})`)(
    load, load, scheduler, 0, true, { value: '' }, String)
  callback()
  await Promise.resolve(); await Promise.resolve()
  hiddenResults.push({ page: name, hiddenResourceReads: reads })
  scheduler?.dispose()
}
const publicTree = source('TournamentPublicDetail')
const refresh = fn(publicTree, 'refresh')
assert(refresh, 'execute the original sixty-second callback')
let timerReads = 0, nextDelay = 0
const timerScheduler = helper?.createVisibleResourceRead({ visible: () => false, invalidate: () => {},
  load: () => { timerReads++; return Promise.resolve() } })
new Function('load', 'visibleTournamentRead', 'window',
  `let refreshTimer; ${compile(refresh.getText(publicTree))}; refresh()`)(
  () => { timerReads++; return Promise.resolve() }, timerScheduler,
  { setTimeout: (_callback, delay) => { nextDelay = delay; return 1 } })
await Promise.resolve(); await Promise.resolve()
assert.equal(nextDelay, 60_000, 'the established public refresh interval is unchanged')
timerScheduler?.dispose()
console.log(JSON.stringify({ actualComponentCallbacks: true, hiddenResults, hiddenPublicTimerReads: timerReads }))
assert.deepEqual(hiddenResults.map(item => item.hiddenResourceReads), [0, 0, 0, 0],
  'hidden resource invalidation must not fetch tournament projections')
assert.equal(timerReads, 0, 'the hidden public timer must not fetch a tournament projection')

assert(helper, 'the actual shared scheduler must exist')
const settle = async () => { for (let n = 0; n < 32; n++) await Promise.resolve(); await Vue.nextTick() }
const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }
const coordinatorSource = fs.readFileSync(new URL('../src/l12/platformRequestReliability.ts', import.meta.url), 'utf8')
const { createRequestCoordinator } = await import(`data:text/javascript;base64,${Buffer.from(compile(coordinatorSource)).toString('base64')}`)
function pageHarness(name) {
  const tree = source(name), calls = [], networkStarts = [], plans = [], watchers = [], connections = []
  const coordinator = createRequestCoordinator(4)
  const ref = value => Vue.ref(value)
  const payload = { id: 'tournament', name: 'synthetic private name', status: 'running', version: 1,
    participants: [{ accountId: 'actor', deck: {} }], referees: [], rounds: [] }
  const model = ref(payload), rows = ref([payload]), result = ref({ items: [payload] })
  const doc = { visibilityState: 'hidden', title: payload.name }
  const account = Vue.reactive({ account: { id: 'actor', role: 'admin', permissionVersion: 1, permissions: ['tournaments.manage'] }, token: 'synthetic-token' })
  const request = kind => {
    calls.push(kind)
    return coordinator.execute({ key: kind, method: 'GET', timeoutMs: 2_000, maxAttempts: 1,
      shouldRetry: () => false, retryDelayMs: () => 0,
      run: () => { networkStarts.push(kind); return plans.shift()?.promise ?? Promise.resolve(kind === 'list' ? { items: [payload], platformVersion: 1 } : kind === 'summary' ? { items: [payload] } : payload) } })
  }
  const env = {
    createVisibleResourceRead: helper.createVisibleResourceRead,
    document: doc, platformState: account, route: Vue.reactive({ fullPath: '/t/synthetic', params: { code: 'synthetic' }, query: {} }),
    loading: ref(false), busy: ref(false), notice: ref(''), readModelCurrent: ref(true), readModelOwner: ref(0),
    tournament: model, tournaments: rows, result, career: ref({ private: true }), identity: ref('synthetic-token'),
    section: ref('mine'), format: ref(''), timeRange: ref(''), page: ref(1), careerPage: ref(1), search: ref(''),
    emptyResult: () => ({ items: [] }), accountId: ref('actor'), activeRegistration: Vue.computed(() => !!model.value?.participants?.some(person => person.accountId === 'actor')),
    catalog: ref([]), deckId: ref('unsaved-deck'), selectedDeckMetadata: ref({ id: 'unsaved-deck' }), deckSelectorOpen: ref(true),
    deckError: ref(''), deckLoading: ref(false), dropArmed: ref(false),
    deckDrafts: { tournament: { id: 'unsaved-deck' } }, platformVersion: ref(1), detailId: ref('tournament'),
    props: { adminMode: true }, errorText: String, mergeTournamentSnapshot: items => items,
    legacyCandidates: ref([]), readLegacyCandidates: () => [], form: { disasterCardIds: [], cardRestrictions: [] },
    loadFriends: () => Promise.resolve(), getEffectiveOperationsPolicy: () => Promise.resolve({ disasterCardIds: ['policy-ready'], cardRestrictions: [] }),
    publicTournamentApi: { getByCode: () => request('public'), summaries: () => request('summary') },
    tournamentApi: { getByCode: () => request('detail'), list: () => request('list'), summaries: () => request('summary'), career: () => request('career'), exportCsv: () => request('export') },
    window: { addEventListener() {}, removeEventListener() {}, clearTimeout() {}, setTimeout(callback, delay) { watchers.push({ callback, delay }); return watchers.length } },
    router: { replace() {}, push() {}, resolve: () => ({ fullPath: '/synthetic' }) },
    loadCatalog: () => Promise.resolve(), loadDeckCatalog: () => Promise.resolve([]), loadDeckDirectory: () => Promise.resolve(),
    runGatedAction: async (_key, work) => work(),
    connect: () => connections.shift()?.promise ?? Promise.resolve(), tournamentActionKey: item => `tournament:${item.id}`,
    enterTournamentMatch: () => calls.push('enter'), spectateTournamentMatch: () => calls.push('spectate'), l12State: {},
  }
  const functions = ['load', 'loadCareer', 'invalidateTournamentRead', 'readTournamentSnapshot', 'refreshTournaments', 'hydrateDrafts',
    'actorIsCurrent', 'loadVisibleTournamentResources', 'mutate', 'runAction', 'syncAfterWrite', 'room', 'enterMatch', 'exportCsv', 'login']
    .map(key => fn(tree, key)).filter(Boolean).map(node => compile(node.getText(tree))).join('\n')
  const initializers = ['visibleTournamentRead', 'visibleCareerRead']
    .map(key => variable(tree, key)).filter(Boolean).map(node => compile(`const ${node.getText(tree)};`)).join('\n')
  const eventName = name === 'AdminTournamentWorkbench' ? 'onTournamentResource' : 'onResource'
  const event = compile(`const ${variable(tree, eventName).getText(tree)};`)
  const visibility = compile(`const ${variable(tree, 'onVisibility').getText(tree)};`)
  const watchNodes = tree.statements.filter(node => ts.isExpressionStatement(node) && ts.isCallExpression(node.expression)
    && node.expression.expression.getText(tree) === 'watch')
  const watchBindings = watchNodes.filter(node => {
    const text = node.getText(tree)
    return name === 'TournamentHubPage' || text.includes('platformState.account')
  }).map(node => compile(node.getText(tree))).join('\n')
  const scope = Vue.effectScope()
  const api = scope.run(() => new Function(...Object.keys(env), 'watch', `
    let generation=0,careerGeneration=0,disposed=false,actorEpoch=0,tournamentLoadEpoch=0,deckLoadEpoch=0;
    let componentAlive=true,catalogDemand=false,initialResourcesPending=false,searchTimer;
    ${functions}\n${initializers}\n${event}\n${visibility}\n${watchBindings}
    return { onResource:${eventName},onVisibility,dispose(){visibleTournamentRead.dispose();${name === 'TournamentHubPage' ? 'visibleCareerRead.dispose();' : ''}disposed=true;componentAlive=false;actorEpoch++;tournamentLoadEpoch++;generation++;careerGeneration++},
      mutate:${name === 'TournamentAccountDetail' ? 'mutate' : 'null'},runAction:${name === 'AdminTournamentWorkbench' ? 'runAction' : 'null'},
      hydrate:${name === 'AdminTournamentWorkbench' ? 'hydrateDrafts' : 'null'},
      afterWrite:${name === 'AdminTournamentWorkbench' ? 'syncAfterWrite' : 'null'},
      room:${name === 'TournamentAccountDetail' ? 'room' : name === 'AdminTournamentWorkbench' ? 'enterMatch' : 'null'},
      exportCsv:${name === 'TournamentAccountDetail' ? 'exportCsv' : 'null'},
      initialize(){initialResourcesPending=true;visibleTournamentRead.request()},
      careerRequest:${name === 'TournamentHubPage' ? '()=>visibleCareerRead.request()' : '()=>{}'},
      summaryRequest:()=>visibleTournamentRead.request() }
  `)(...Object.values(env), Vue.watch))
  return { ...env, api, calls, networkStarts, plans, connections, timers: watchers, scope, close() { api.dispose(); scope.stop() } }
}

for (const name of pages) {
  const h = pageHarness(name)
  const stale = deferred(), fresh = deferred()
  h.plans.push(stale, fresh)
  h.api.onResource(); h.api.onResource(); await settle()
  assert.equal(h.calls.length, 0, `${name}: hidden burst is demand, not network work`)
  if (name.includes('Detail')) assert.equal(h.document.title, '赛事中心 · 十二军团', 'stale title is withdrawn immediately')
  if (name === 'TournamentAccountDetail' || name === 'AdminTournamentWorkbench') {
    assert.equal(h.readModelCurrent.value, false)
    assert.equal(h.deckId.value, 'unsaved-deck')
    assert.equal(h.deckDrafts.tournament.id, 'unsaved-deck')
  }
  h.document.visibilityState = 'visible'
  h.api.onVisibility(); h.api.onResource(); await settle()
  assert.equal(h.calls.length, 1, `${name}: burst has one real load`)
  h.api.onResource(); h.api.onResource()
  stale.resolve(name === 'AdminTournamentWorkbench' ? { items: [], platformVersion: 1 } : { id: 'old', name: 'revoked old reply' })
  await settle()
  assert.equal(h.calls.length, 2, `${name}: dirty in-flight work starts a fresh load only after old settlement`)
  assert.notEqual(h.tournament.value?.name, 'revoked old reply')
  fresh.reject(new Error('synthetic 403 revoked'))
  // Simulate the existing authoritative account refresh returning a new same-value object.
  h.platformState.account = { ...h.platformState.account, permissions: [...h.platformState.account.permissions] }
  await settle()
  assert.equal(h.calls.length, 2, `${name}: an error/same-value account refresh cannot create a retry loop`)
  assert.equal(h.loading.value, false)
  assert.match(h.notice.value, /403/)
  if (name === 'TournamentAccountDetail' || name === 'AdminTournamentWorkbench') assert.equal(h.readModelCurrent.value, false)
  h.api.onVisibility(); await settle()
  assert.equal(h.calls.length, 3, 'a later genuine visible demand may retry once')
  if (name === 'TournamentAccountDetail' || name === 'AdminTournamentWorkbench') assert.equal(h.readModelCurrent.value, true)
  h.close()
}

// Actual Hub watcher callbacks preserve the original request scopes.
{
  const h = pageHarness('TournamentHubPage')
  h.api.summaryRequest(); h.api.careerRequest(); await settle()
  assert.deepEqual(h.calls, [])
  h.document.visibilityState = 'visible'; h.api.onVisibility(); await settle()
  assert.equal(h.calls.filter(kind => kind === 'career').length, 1, 'hidden mine initial load wakes career once')
  const careerReads = () => h.calls.filter(kind => kind === 'career').length
  h.document.visibilityState = 'hidden'; h.api.onVisibility(); await settle()
  h.document.visibilityState = 'visible'; h.api.onVisibility(); await settle()
  assert.equal(careerReads(), 1, 'clean visibility return does not invent a career GET')
  const summaries = h.calls.filter(kind => kind === 'summary').length
  h.careerPage.value++; await settle()
  assert.equal(careerReads(), 2)
  assert.equal(h.calls.filter(kind => kind === 'summary').length, summaries, 'career page changes read only career')
  h.api.onResource(); await settle(); assert.equal(careerReads(), 2)
  h.search.value = 'new query'; await Vue.nextTick()
  h.timers.findLast(timer => timer.delay === 250).callback(); await settle()
  assert.equal(careerReads(), 2, 'resource/search read only summaries')
  h.platformState.account.role = 'player'; await settle()
  assert.equal(careerReads(), 3, 'real permission facts can revalidate the displayed career')
  h.identity.value = 'another verified identity'; await settle()
  assert.equal(careerReads(), 4)
  assert(h.career.value, 'identity-triggered career work is not left permanently empty')
  h.close()
}

// Both directions use the actual page write-readback and actual shared GET coordinator.
for (const manualFirst of [false, true]) {
  const h = pageHarness('AdminTournamentWorkbench'), old = deferred(), fresh = deferred()
  h.document.visibilityState = 'visible'; h.plans.push(old, fresh)
  let acknowledged = false, ack
  const written = { id: 'tournament', name: 'saved fact', version: 2, participants: [{ accountId: 'actor', deck: {} }] }
  if (manualFirst) ack = h.api.afterWrite(written, 'write receipt').then(() => { acknowledged = true })
  else h.api.onResource()
  await settle()
  assert.equal(h.networkStarts.length, 1)
  if (manualFirst) h.api.onResource()
  else ack = h.api.afterWrite(written, 'write receipt').then(() => { acknowledged = true })
  await settle(); assert.equal(h.networkStarts.length, 1)
  old.resolve({ items: [{ ...written, name: 'revoked stale DTO' }], platformVersion: 2 })
  await settle()
  assert.equal(h.networkStarts.length, 2, 'a write/read overlap starts a new GET after the shared pending read settles')
  assert.equal(acknowledged, false)
  assert.equal(h.readModelCurrent.value, false)
  assert(!h.tournaments.value.some(item => item.name === 'revoked stale DTO'))
  fresh.resolve({ items: [{ ...written, name: 'new authorized projection' }], platformVersion: 2 })
  await ack; await settle()
  assert.equal(acknowledged, true)
  assert.equal(h.readModelCurrent.value, true)
  assert.equal(h.tournaments.value[0].name, 'new authorized projection')
  h.close()
}

// Waited explicit readback retires immediately when its owner retires; no polling is added.
for (const dispose of [false, true]) {
  const pending = deferred(); let current = true
  const scheduler = helper.createVisibleResourceRead({ visible: () => false, invalidate: () => {}, load: () => pending.promise })
  const waiting = scheduler.refresh(() => current)
  await settle()
  if (dispose) scheduler.dispose()
  else { current = false; scheduler.wake() }
  assert.equal(await waiting, false, 'dispose/current invalidation cannot leave a write acknowledgement suspended')
  pending.resolve(); await settle(); scheduler.dispose()
}

for (const name of ['TournamentAccountDetail', 'AdminTournamentWorkbench']) {
  const h = pageHarness(name)
  h.platformState.account = { ...h.platformState.account, permissions: [...h.platformState.account.permissions] }
  await settle()
  assert.equal(h.calls.length, 0)
  assert.equal(h.deckId.value, 'unsaved-deck')
  assert.equal(h.deckDrafts.tournament.id, 'unsaved-deck')
  h.platformState.account.permissionVersion++
  assert.equal(h.readModelCurrent.value, false, 'real permission invalidation masks synchronously, even while hidden')
  await settle(); assert.equal(h.calls.length, 0)
  assert.equal(h.deckId.value, 'unsaved-deck')
  h.close()
}

// The real Admin initialization awaits its actual read stages before unlocking display.
{
  const h = pageHarness('AdminTournamentWorkbench'), catalog = deferred()
  // This fixture's catalog function delegates to a controlled promise through its mutable plan.
  h.props.adminMode = false
  // The source-level loader's Promise.all is also compiled and checked below; the live stage
  // order here uses a pending list to ensure no later stage can imply early authorization.
  h.plans.push(catalog)
  h.document.visibilityState = 'visible'; h.api.initialize(); await settle()
  assert.equal(h.readModelCurrent.value, false)
  assert.deepEqual(h.form.disasterCardIds, [])
  catalog.resolve({ items: [{ id: 'tournament', participants: [] }], platformVersion: 1 })
  await settle(); await settle()
  assert.equal(h.readModelCurrent.value, true)
  assert.deepEqual(h.form.disasterCardIds, ['policy-ready'])
  h.close()
}

// Actual Admin hydration keeps automatic read refreshes separate from confirmed writes.
{
  const h = pageHarness('AdminTournamentWorkbench')
  const items = [{ id: 'tournament', participants: [{ accountId: 'actor', deck: {} }] }]
  h.api.hydrate(items, true)
  assert.equal(h.deckDrafts.tournament.id, 'unsaved-deck')
  h.api.hydrate(items)
  assert.equal(h.deckDrafts.tournament.id, '', 'the original explicit-write cleanup remains intact')
  h.close()
}

for (const name of ['TournamentAccountDetail', 'AdminTournamentWorkbench']) {
  const h = pageHarness(name), connecting = deferred()
  h.connections.push(connecting)
  if (name === 'TournamentAccountDetail') {
    const room = h.api.room({ id: 'match', roomCode: 'room' }, false)
    h.api.onResource(); connecting.resolve(); await room
    await h.api.exportCsv()
    assert.equal(h.calls.filter(kind => kind === 'export').length, 0)
  } else {
    h.api.room({ id: 'tournament' }, { id: 'match', roomCode: 'room' }, false)
    h.api.onResource(); connecting.resolve(); await settle()
    let started = 0
    await h.api.runAction('new', async () => { started++ })
    assert.equal(started, 0)
  }
  await settle()
  assert.equal(h.calls.filter(kind => kind === 'enter' || kind === 'spectate').length, 0, 'invalidated room entry cannot send a later command')
  h.close()
}

// A legitimate already-sent save keeps its receipt, but cannot unlock revoked display.
{
  const h = pageHarness('TournamentAccountDetail'), write = deferred()
  let writes = 0
  const save = h.api.mutate(() => { writes++; return write.promise }, 'saved receipt')
  h.api.onResource() // Hidden invalidation; no read is sent.
  write.resolve({ id: 'tournament', name: 'old write response' })
  await save; await settle()
  assert.equal(writes, 1)
  assert.equal(h.notice.value, 'saved receipt')
  assert.equal(h.readModelCurrent.value, false)
  assert.notEqual(h.tournament.value?.name, 'old write response')
  await h.api.mutate(() => { writes++; return Promise.resolve({}) }, 'must not run')
  assert.equal(writes, 1, 'an invalidated read generation cannot start a new write')
  h.close()
}

// Disposal suppresses queued starts and dirty follow-ups; late page results stay retired.
for (const name of pages) {
  const h = pageHarness(name), pending = deferred()
  h.document.visibilityState = 'visible'; h.api.onResource(); h.close(); await settle()
  assert.equal(h.calls.length, 0)
  const late = pageHarness(name)
  late.document.visibilityState = 'visible'; late.plans.push(pending); late.api.onResource(); await settle()
  late.api.onResource(); late.close(); pending.resolve({ name: 'late' }); await settle()
  assert.equal(late.calls.length, 1)
  assert.notEqual(late.tournament.value?.name, 'late')
}

for (const name of pages) {
  const filename = new URL(`../src/l12/site/${name}.vue`, import.meta.url)
  const { descriptor, errors } = parse(fs.readFileSync(filename, 'utf8'), { filename: filename.pathname })
  assert.deepEqual(errors, [])
  compileScript(descriptor, { id: name })
  const compiled = compileTemplate({ source: descriptor.template.content, filename: filename.pathname, id: name })
  assert.deepEqual(compiled.errors, [], `${name}: compile the actual changed template`)
  if (name === 'TournamentAccountDetail' || name === 'AdminTournamentWorkbench') {
    assert(compiled.code.includes('vShow') && compiled.code.includes('inert'), 'mask sensitive content and keyboard access without unmounting drafts')
  }
}
console.log(JSON.stringify({ status: 'passed', actualComponentCallbacks: true, actualSharedScheduler: true,
  hiddenResourceReads: 0, hiddenPublicTimerReads: 0, staleResponsesApplied: 0, networkCalls: 0,
  browserOrOnlineBenefitMeasured: false }))
