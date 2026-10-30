import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'

const require = createRequire(import.meta.url)
const ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const { baseParse, parserOptions } = require('@vue/compiler-dom')
const paths = {
  workbench: new URL('../src/l12/site/AdminTournamentWorkbench.vue', import.meta.url),
  detail: new URL('../src/l12/site/TournamentAccountDetail.vue', import.meta.url),
  policy: new URL('../src/l12/tournamentRulingPolicy.ts', import.meta.url),
}
const sources = Object.fromEntries(Object.entries(paths).map(([key, path]) => [key, fs.existsSync(path) ? fs.readFileSync(path, 'utf8') : null]))
const descriptors = Object.fromEntries(['workbench', 'detail'].map(key => {
  const parsed = parse(sources[key]); assert.deepEqual(parsed.errors, [])
  return [key, parsed.descriptor]
}))
function transpile(source) { return ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText }
let policy = { canDirectlyRuleTournamentMatch: () => true, canManageParticipantRemovalWithoutSelfRuling: () => true, tournamentSelfRulingMessage: 'missing-policy' }
if (sources.policy) {
  const module = { exports: {} }
  vm.runInNewContext(transpile(sources.policy), { module, exports: module.exports, require: id => { throw new Error(`unexpected policy import: ${id}`) } })
  policy = module.exports
}
function functions(key, names) {
  const source = descriptors[key].scriptSetup.content
  const ast = ts.createSourceFile('actual-component.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const nodes = names.map(name => {
    const node = ast.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
    assert.ok(node, `actual ${key}.${name}`); return node.getText(ast)
  })
  const derived = key === 'detail' ? ast.statements.filter(ts.isVariableStatement)
    .flatMap(node => node.declarationList.declarations)
    .filter(node => ts.isIdentifier(node.name) && ['me', 'activeRegistration'].includes(node.name.text))
    .map(node => 'const ' + node.getText(ast)) : []
  if (key === 'detail') assert.equal(derived.length, 2, 'actual participant-state guards must be present')
  return transpile([...derived, ...nodes].join('\n') + `\nglobalThis.handlers = {${names.join(',')}}`)
}
const names = {
  workbench: ['errorText', 'actorIsCurrent', 'runAction', 'ruleMatch', 'noShowLoss', 'removeParticipant', 'addTime', 'requestRematch', 'dropRegistration', 'join', 'unbanParticipant'],
  detail: ['mutate', 'remove', 'register', 'drop', 'pauseMatch', 'roundCheckIn', 'unban'],
}
const methodCode = Object.fromEntries(Object.keys(names).map(key => [key, functions(key, names[key])]))
const match = (overrides = {}) => ({ id: 'synthetic-match', playerAAccountId: 'actor-A', playerBAccountId: 'actor-B', status: 'running', readyA: false, readyB: true, paused: false, ...overrides })
const tournament = (matches = [match()], overrides = {}) => ({ id: 'synthetic-tournament', version: 19, organizerAccountId: 'actor-A', participants: ['actor-A', 'actor-B'].map(accountId => ({ accountId, dropped: false, removed: false })), referees: [], rounds: [{ number: 1, status: 'running', matches }], ...overrides })
function fixture(key, actor = 'actor-A', item = tournament()) {
  const calls = [], pending = [], notice = { value: '' }, busy = { value: false }
  const platformState = { account: actor ? { id: actor } : null, token: 'synthetic-token' }
  const api = new Proxy({}, { get: (_, name) => async (...args) => { calls.push({ name, args }); return item } })
  const scope = { ...policy, Error, console, computed: work => ({ get value() { return work() } }), componentAlive: true, disposed: false, actorEpoch: 0, platformState,
    route: { fullPath: '/synthetic/tournament' }, tournament: { value: item }, busy, notice, dropArmed: { value: true },
    readModelCurrent: { value: true }, tournamentLoadEpoch: 0,
    accountId: { get value() { return platformState.account?.id ?? '' } },
    rulingReasons: { 'synthetic-match': ' synthetic reason ' }, participantReasons: { 'actor-A': ' synthetic reason ', 'actor-B': ' synthetic reason ', 'actor-C': ' synthetic reason ' },
    matchReasons: { 'synthetic-match': ' synthetic reason ' }, tournamentApi: api,
    tournamentActionKey: value => `tournament:${value.id}`, syncAfterWrite: async () => {},
    runGatedAction: (_key, work) => { const promise = work(); pending.push(promise); return promise },
    fetch: () => { throw new Error('network forbidden') },
  }
  if (key === 'detail') {
    // Execute the actual mutate wrapper too; only collect its promise for void event handlers.
    vm.runInNewContext(methodCode[key], scope)
    const original = scope.handlers.mutate
    scope.mutate = (...args) => { const promise = original(...args); pending.push(promise); return promise }
    // Function declarations share their global binding, so the handler resolves this tracked wrapper.
  } else vm.runInNewContext(methodCode[key], scope)
  return { scope, calls, pending, notice, busy, handlers: scope.handlers,
    async invoke(name, ...args) { scope.handlers[name](...args); await Promise.all(pending); for (let i = 0; i < 4; i++) await Promise.resolve() } }
}
function templateButtons(key, callees) {
  const ast = baseParse(descriptors[key].template.content, parserOptions), result = []
  function walk(node) {
    if (node.type === 1 && node.tag === 'button') {
      const click = node.props.find(prop => prop.type === 7 && prop.name === 'on' && prop.arg?.content === 'click')?.exp?.content
      if (click && callees.some(name => click.startsWith(name + '('))) {
        result.push({ click, disabled: node.props.find(prop => prop.type === 7 && prop.name === 'bind' && prop.arg?.content === 'disabled')?.exp?.content })
      }
    }
    for (const child of node.children ?? []) walk(child)
  }
  walk(ast); return result
}
function disabled(button, context) { return !!(button.disabled && vm.runInNewContext(button.disabled, { ...policy, ...context })) }
const results = []
async function test(name, run) { try { await run(); results.push({ name, passed: true }) } catch (error) { results.push({ name, passed: false, error: error.message.split('\n').slice(0, 3).join(' ') }) } }

await test('invalidated read models cannot start a new ruling or removal', async () => {
  for (const key of ['workbench', 'detail']) {
    const f = fixture(key, 'actor-C')
    f.scope.readModelCurrent.value = false
    await f.invoke(key === 'workbench' ? 'removeParticipant' : 'remove', ...(key === 'workbench'
      ? [tournament(), { accountId: 'actor-B' }, false] : [{ accountId: 'actor-B' }, false]))
    assert.equal(f.calls.length, 0)
  }
})

await test('pure policy fails closed on unknown actor or match identity and is not authorization', () => {
  assert.ok(sources.policy, 'shared policy must exist')
  for (const actor of ['', null, undefined, ' ', ' actor-C ']) assert.equal(policy.canDirectlyRuleTournamentMatch(actor, match()), false)
  for (const value of [null, {}, match({ id: '' }), match({ playerAAccountId: '' }), match({ playerBAccountId: '' })]) assert.equal(policy.canDirectlyRuleTournamentMatch('actor-C', value), false)
  assert.equal(policy.canDirectlyRuleTournamentMatch('actor-C', match()), true)
  assert.equal(policy.canDirectlyRuleTournamentMatch('actor-C', match({ playerBAccountId: null })), true)
})
await test('actual result and penalty handlers refuse A and B, independently of global staff privilege', async () => {
  for (const actor of ['actor-A', 'actor-B']) for (const [kind, decision, target] of [['result', 'player-a'], ['result', 'player-b'], ['result', 'draw'], ['penalty', 'warning', 'actor-A'], ['penalty', 'warning', 'actor-B']]) {
    const f = fixture('workbench', actor); await f.invoke('ruleMatch', tournament(), match(), kind, decision, target)
    assert.equal(f.calls.length, 0, `${actor}/${kind}/${decision}`); assert.match(f.notice.value, /独立|利益冲突/)
  }
})
await test('actual no-show handler refuses both own sides even when opponent is the absent player', async () => {
  for (const actor of ['actor-A', 'actor-B']) for (const readyA of [false, true]) {
    const f = fixture('workbench', actor); await f.invoke('noShowLoss', tournament(), match({ readyA, readyB: !readyA }))
    assert.equal(f.calls.length, 0); assert.match(f.notice.value, /独立|利益冲突/)
  }
})
await test('actual direct handlers fail closed on missing actor and incomplete match', async () => {
  for (const [actor, value] of [['', match()], ['actor-C', match({ id: '' })], ['actor-C', match({ playerAAccountId: '' })]]) {
    for (const handler of ['ruleMatch', 'noShowLoss']) { const f = fixture('workbench', actor); await f.invoke(handler, tournament(), value, 'result', 'player-a'); assert.equal(f.calls.length, 0) }
  }
})
await test('independent direct rulings retain actual ID/version/reason/target and other-table access', async () => {
  for (const handler of ['ruleMatch', 'noShowLoss']) {
    const f = fixture('workbench', 'actor-C'); await f.invoke(handler, tournament(), match(), 'penalty', 'warning', 'actor-B')
    assert.equal(f.calls.length, 1); const call = f.calls[0]
    assert.equal(call.name, 'ruleMatch'); assert.deepEqual(call.args.slice(0, 3), ['synthetic-tournament', 'synthetic-match', 19])
    assert.equal(call.args[3].reason, 'synthetic reason'); assert.equal(call.args[3].targetAccountId, handler === 'ruleMatch' ? 'actor-B' : 'actor-A')
  }
})
await test('both actual removal consumers refuse implicit own-table result for either target and ban flag', async () => {
  for (const key of ['workbench', 'detail']) for (const actor of ['actor-A', 'actor-B']) for (const target of ['actor-A', 'actor-B']) for (const ban of [false, true]) {
    const f = fixture(key, actor), person = { accountId: target }
    await f.invoke(key === 'workbench' ? 'removeParticipant' : 'remove', ...(key === 'workbench' ? [tournament(), person, ban] : [person, ban]))
    assert.equal(f.calls.length, 0, `${key}/${actor}/${target}/${ban}`); assert.match(f.notice.value, /独立|利益冲突/)
  }
})
await test('removal excludes completed rounds/matches, byes and other tables but fails closed on unknown context', async () => {
  const allowed = [tournament([], { rounds: [] }), tournament([match({ status: 'completed' })]), tournament([match({ playerBAccountId: null })]),
    tournament([match({ playerAAccountId: 'actor-C', playerBAccountId: 'actor-D' })]), tournament([], { rounds: [{ status: 'completed', matches: [match()] }] })]
  for (const item of allowed) for (const key of ['workbench', 'detail']) {
    const f = fixture(key, 'actor-A', item), person = { accountId: 'actor-B' }
    await f.invoke(key === 'workbench' ? 'removeParticipant' : 'remove', ...(key === 'workbench' ? [item, person, true] : [person, true])); assert.equal(f.calls.length, 1)
    assert.deepEqual(f.calls[0].args, ['synthetic-tournament', 19, 'actor-B', true, 'synthetic reason'])
  }
  for (const item of [null, {}, tournament([], { id: '' }), tournament([], { rounds: null }), tournament([match({ id: '' })])]) assert.equal(policy.canManageParticipantRemovalWithoutSelfRuling(item, 'actor-A', 'actor-B'), false)
  assert.equal(policy.canManageParticipantRemovalWithoutSelfRuling(tournament(), '', 'actor-B'), false)
})
await test('detail removal checks actual mutate item rather than stale outer tournament', async () => {
  const f = fixture('detail', 'actor-A', tournament([], { rounds: [] }))
  f.scope.tournament.value = tournament()
  await f.invoke('remove', { accountId: 'actor-B' }, false); assert.equal(f.calls.length, 0)
})
await test('real template direct buttons are disabled for own/unknown, retain busy and B guards', () => {
  const buttons = templateButtons('workbench', ['ruleMatch', 'noShowLoss']); assert.equal(buttons.length, 6)
  for (const button of buttons) {
    for (const accountId of ['actor-A', 'actor-B', '']) assert.equal(disabled(button, { busy: false, accountId, match: match() }), true, button.click)
    assert.equal(disabled(button, { busy: true, accountId: 'actor-C', match: match() }), true)
    assert.equal(disabled(button, { busy: false, accountId: 'actor-C', match: match() }), false)
    assert.equal(disabled(button, { busy: false, accountId: 'actor-C', match: match({ id: '' }) }), true)
    if (/player-b|draw|warning',match.playerB/.test(button.click)) assert.equal(disabled(button, { busy: false, accountId: 'actor-C', match: match({ playerBAccountId: null }) }), true)
  }
})
await test('both real template removal buttons track current actionable self-table and busy state', () => {
  for (const key of ['workbench', 'detail']) {
    const buttons = templateButtons(key, key === 'workbench' ? ['removeParticipant'] : ['remove']); assert.equal(buttons.length, 2)
    for (const button of buttons) for (const target of ['actor-A', 'actor-B']) {
      const context = { busy: false, accountId: 'actor-A', person: { accountId: target }, detail: tournament(), tournament: tournament() }
      assert.equal(disabled(button, context), true); assert.equal(disabled(button, { ...context, accountId: 'actor-C' }), false)
      assert.equal(disabled(button, { ...context, accountId: 'actor-C', busy: true }), true)
    }
  }
})
await test('actual add-time/rematch/drop/pause/check-in/register/unban handlers remain available on own table', async () => {
  for (const [key, name, args, api] of [
    ['workbench', 'addTime', [tournament(), match(), 5], 'extendMatch'], ['workbench', 'requestRematch', [tournament(), match()], 'rematch'],
    ['workbench', 'dropRegistration', [tournament()], 'drop'], ['workbench', 'join', [tournament()], 'register'],
    ['workbench', 'unbanParticipant', [tournament(), { accountId: 'actor-B' }], 'setRegistrationBan'],
    ['detail', 'pauseMatch', [match()], 'pauseMatch'], ['detail', 'roundCheckIn', [1, true], 'checkIn'],
    ['detail', 'register', [], 'register'], ['detail', 'drop', [], 'drop'], ['detail', 'unban', [{ accountId: 'actor-B' }], 'setRegistrationBan'],
  ]) { const f = fixture(key); await f.invoke(name, ...args); assert.equal(f.calls.length, 1, `${key}.${name}`); assert.equal(f.calls[0].name, api) }
  for (const button of templateButtons('workbench', ['addTime', 'requestRematch'])) assert.equal(disabled(button, { busy: false, accountId: 'actor-A', match: match() }), false)
})
await test('actual participant-state guards reject withdrawn and removed check-in/drop without changing staff identity', async () => {
  for (const flags of [{ dropped: true }, { removed: true }]) {
    const item = tournament(undefined, { participants: [{ accountId: 'actor-A', ...flags }] })
    for (const [name, args] of [['drop', []], ['roundCheckIn', [1, true]]]) {
      const f = fixture('detail', 'actor-A', item)
      await f.invoke(name, ...args)
      assert.equal(f.calls.length, 0)
      assert.match(f.notice.value, /报名/)
      assert.equal(f.scope.tournament.value.organizerAccountId, 'actor-A')
    }
  }
})
await test('existing reason validation and version are retained without policy granting permission', async () => {
  for (const key of ['workbench', 'detail']) {
    const f = fixture(key, 'actor-C'); f.scope.rulingReasons = {}; f.scope.participantReasons = {}
    await f.invoke(key === 'workbench' ? 'removeParticipant' : 'remove', ...(key === 'workbench' ? [tournament(), { accountId: 'actor-B' }, false] : [{ accountId: 'actor-B' }, false]))
    assert.equal(f.calls.length, 0); assert.match(f.notice.value, /理由/)
  }
  const f = fixture('workbench', 'actor-C'); f.scope.rulingReasons = {}; await f.invoke('ruleMatch', tournament(), match(), 'result', 'player-a'); assert.equal(f.calls.length, 0); assert.match(f.notice.value, /理由/)
})
await test('actual SFC scripts/templates compile and shared guard imports are explicit', () => {
  for (const key of ['workbench', 'detail']) {
    const script = compileScript(descriptors[key], { id: 'self-ruling-regression' })
    const template = compileTemplate({ source: descriptors[key].template.content, filename: key + '.vue', id: 'self-ruling-regression', compilerOptions: { bindingMetadata: script.bindings } })
    assert.deepEqual(template.errors, [])
    const ast = ts.createSourceFile('actual.ts', descriptors[key].scriptSetup.content, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
    const imports = ast.statements.filter(ts.isImportDeclaration).filter(node => node.moduleSpecifier.text === '@/l12/tournamentRulingPolicy')
    assert.equal(imports.length, 1, `${key} uses shared policy`)
  }
})
await test('actual existing staff/global permission expressions are preserved; eligibility grants no privilege', () => {
  const names = ['canGloballyManage', 'canGloballyRule', 'isStaff', 'isOrganizer', 'canManageTournament', 'canRuleTournament']
  const ast = ts.createSourceFile('actual.ts', descriptors.workbench.scriptSetup.content, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const declarations = ast.statements.filter(ts.isVariableStatement).flatMap(node => node.declarationList.declarations)
    .filter(node => ts.isIdentifier(node.name) && names.includes(node.name.text)).map(node => 'const ' + node.getText(ast))
  assert.equal(declarations.length, names.length)
  for (const [actor, adminMode, permitted, expected] of [['actor-C', false, false, false], ['actor-A', false, false, true], ['actor-C', true, false, false], ['actor-C', true, true, true], ['actor-C', false, true, false]]) {
    const context = { accountId: { value: actor }, props: { adminMode }, hasPermission: () => permitted, computed: work => ({ get value() { return work() } }), item: tournament() }
    const result = vm.runInNewContext(transpile(declarations.join('\n') + '\nglobalThis.result = [canManageTournament(item),canRuleTournament(item)]'), context)
    assert.deepEqual([...context.result], [expected, expected])
  }
  assert.equal(policy.canDirectlyRuleTournamentMatch('actor-C', match()), true)
})
const failed = results.filter(result => !result.passed).length
console.log(JSON.stringify({ suite: 'tournament-ruling-policy', passed: results.length - failed, failed, networkCalls: 0,
  mode: 'actual-SFC-AST-methods-and-template-expressions;no-browser;no-mounted-lifecycle',
  sourceSha256: Object.fromEntries(Object.entries(sources).map(([key, source]) => [key, source === null ? null : createHash('sha256').update(source).digest('hex').toUpperCase()])), results }, null, 2))
if (failed) process.exitCode = 1
