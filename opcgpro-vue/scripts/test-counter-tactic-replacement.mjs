import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const require = createRequire(import.meta.url)
const ts = require('typescript')
const { parse } = require('@vue/compiler-sfc')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const read = name => fs.readFileSync(path.join(root, name), 'utf8')
const board = read('src/l12/game/GameBoard.vue')
const mat = read('src/l12/game/PlayerMat.vue')
const presentation = read('src/l12/cardPresentation.ts')
const compile = source => ts.transpileModule(source, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText
const module = { exports: {} }
new Function('module', 'exports', compile(presentation))(module, module.exports)
const { isCounterTacticCard, canReplaceOwnCoveredCounter } = module.exports
const ast = source => ts.createSourceFile('actual.ts', parse(source).descriptor.scriptSetup.content,
  ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const boardAst = ast(board), matAst = ast(mat)
function declaration(source, name) {
  const found = source.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name
    || ts.isVariableStatement(node) && node.declarationList.declarations.some(item => item.name.getText(source) === name))
  assert.ok(found, 'Missing actual product declaration ' + name)
  return found.getText(source)
}
const computed = getter => ({ get value() { return getter() } })
const template = parse(board).descriptor.template.content
const bindings = [...template.matchAll(/:placement-can-replace-counter="([^"]+)"/g)].map(match => match[1])
assert.equal(bindings.length, 2, 'both rendered battlefield halves retain replacement capability')
let assertions = 0, scenarios = 0
function equal(actual, expected, message) {
  const plain = value => value && typeof value === 'object' ? JSON.parse(JSON.stringify(value)) : value
  assert.deepEqual(plain(actual), plain(expected), message); assertions++
}
function fixture(hand, target, options = {}) {
  const actor = options.actor ?? 0
  const slots = [[null, null, null], [null, target, null]]
  const player = { playerIndex: actor, field: slots, hand: [hand], morale: [] }
  const commands = []
  const context = {
    computed, isCounterTacticCard, canReplaceOwnCoveredCounter,
    props: { readOnly: options.readOnly ?? false,
      game: { phase: 'Main', activePlayer: actor, you: actor, legionPlacementRows: options.rows ?? [0, 1], prompts: [], players: [] } },
    l12State: { pendingAction: options.pending ?? false, gmEnabled: options.sandbox ?? false },
    controlledPlayerIndex: { value: actor }, me: { value: player },
    enemy: { value: { playerIndex: 1 - actor, field: [[null, null, null], [null, null, null]] } },
    isMyMain: { value: true }, hasBlockingPrompt: { value: options.prompt ?? false },
    freeActionBusy: { value: options.busy ?? false }, activeMorale: { value: 99 },
    selectedId: { value: null }, focusCard: { value: null }, mode: { value: 'play' }, playArmed: { value: false },
    resolveBoardSlotPrompt: () => false, resourceSelectionPrompt: { value: null }, boardTargetPrompt: { value: null },
    defenseTargetType: { value: 'legion' }, promotionFoundationIdsFor: () => [], canPromote: () => false,
    isInfiltrator: () => false, defenseIds: { value: [] }, supportIds: { value: [] },
    gameAction: command => commands.push({ actor, command }),
    sandboxAction: (actor, command) => commands.push({ actor, command }),
  }
  vm.createContext(context)
  const names = ['isCounter', 'playableIds', 'selectedHandCard', 'allowedLegionPlacementRows',
    'isLegionPlacementRowAllowed', 'handPlacementRow', 'handPlacementCanReplaceCounter',
    'dispatchCommand', 'freeCommand', 'playFromHand', 'ownSlot']
  vm.runInContext(compile(names.map(name => declaration(boardAst, name)).join('\n')), context)
  const evaluate = expression => vm.runInContext(expression, context)
  const arm = () => { context.hand = hand; evaluate('playFromHand(hand)') }
  const click = (row = 1, slot = 1) => {
    context.target = player.field[row][slot]
    context.row = row; context.slot = slot
    evaluate('ownSlot(row, slot, target)')
  }
  const highlight = (branch, row = 1, slot = 1, controllable = true) => {
    const bindContext = Object.fromEntries(Object.entries(context).map(([key, value]) =>
      [key, value && typeof value === 'object' && 'value' in value ? value.value : value]))
    for (const name of ['selectedHandCard', 'handPlacementRow', 'handPlacementCanReplaceCounter'])
      bindContext[name] = evaluate(name + '.value')
    const capability = vm.runInNewContext(bindings[branch], bindContext)
    return vm.runInNewContext(compile(declaration(matAst, 'isCounterTactic') + '\n'
      + declaration(matAst, 'isPlacementDestination') + '\nisPlacementDestination(row, target)'), {
      isCounterTacticCard, canReplaceOwnCoveredCounter, row, target: player.field[row][slot],
      props: { placementMode: context.playArmed.value && !context.hasBlockingPrompt.value && !context.freeActionBusy.value,
        placementRow: evaluate('handPlacementRow.value'), placementCanReplaceCounter: capability,
        controllable, player, viewerPlayerIndex: actor, side: 'my' },
    })
  }
  return { context, commands, player, arm, click, highlight, evaluate }
}
const pool = ['s1', 's2', 'st'].flatMap(set => JSON.parse(fs.readFileSync(
  path.join(root, '../服务端WebSocket/TwelveLegions/Data/cards.' + set + '.json'), 'utf8')))
const counters = pool.filter(card => card.cardType === 'tactic' && card.isCounterTactic === true)
equal(counters.length, 18, 'audit the complete existing counter pool')
const covered = actor => ({ instanceId: 'covered-own', cardId: counters[0].id, name: '旧盖伏',
  cardType: 'tactic', isCounterTactic: true, hidden: true, ownerIndex: actor })
const handCard = definition => ({ ...definition, instanceId: 'new-hand', cardId: definition.id,
  name: definition.nameZh, cost: 0, playCost: 0 })
for (const actor of [0, 1]) {
  for (const counter of counters) {
    for (const slot of [0, 1, 2]) {
      const f = fixture(handCard(counter), null, { actor })
      f.player.field[1][slot] = covered(actor)
      f.arm()
      for (const branch of [0, 1])
        equal(f.highlight(branch, 1, slot), true, counter.id + ' replacement must highlight on both battlefield branches')
      f.click(1, slot)
      equal(f.commands, [{ actor, command: { type: 'playCard', cardInstanceId: 'new-hand', row: 1, slot, targetPlayerIndex: actor } }],
        counter.id + ' sends the original playCard with exact controlled owner and slot')
      f.click(1, slot)
      equal(f.commands.length, 1, 'repeated click after dispatch cannot submit again')
      scenarios++
    }
  }
}
const legion = { instanceId: 'new-hand', cardId: 'ST03-09', cardType: 'legion', cost: 0 }
for (const actor of [0, 1]) {
  for (const kind of ['counter', 'legion']) {
    const source = kind === 'counter' ? handCard(counters[0]) : legion
    for (const [name, target, row, allowed] of [
      ['own-covered', covered(actor), 1, true],
      ['owner-missing-legacy', { ...covered(actor), ownerIndex: undefined }, 1, true],
      ['face-up', { ...covered(actor), hidden: false }, 1, false],
      ['foreign-owner', { ...covered(actor), ownerIndex: 1 - actor }, 1, false],
      ['front-covered', covered(actor), 0, false],
      ['occupied-legion', { ...covered(actor), cardType: 'legion', isCounterTactic: false }, 1, false],
      ['hidden-identity-unknown', { instanceId: 'unknown', hidden: true, cardType: 'unknown' }, 1, false],
    ]) {
      const f = fixture(source, null, { actor })
      f.player.field[row][1] = target; f.arm()
      for (const branch of [0, 1]) equal(f.highlight(branch, row), allowed, kind + '/' + name + ' highlight')
      f.click(row)
      equal(f.commands.length, Number(allowed), kind + '/' + name + ' command qualification')
      scenarios++
    }
  }
  for (const [name, options] of Object.entries({
    readOnly: { readOnly: true }, pending: { pending: true }, busy: { busy: true }, prompt: { prompt: true },
  })) {
    const f = fixture(handCard(counters[0]), covered(actor), { actor, ...options })
    f.arm(); f.click()
    equal(f.commands.length, 0, name + ' still blocks dispatch')
    scenarios++
  }
  const sandbox = fixture(handCard(counters[0]), covered(actor), { actor, sandbox: true })
  sandbox.arm(); sandbox.click(); equal(sandbox.commands[0].actor, actor, 'sandbox controls the exact acting owner'); scenarios++
  const restricted = fixture(legion, covered(actor), { actor, rows: [0] })
  restricted.arm(); equal(restricted.highlight(1), false, 'restricted legion rear cannot highlight')
  restricted.click(); equal(restricted.commands.length, 0, 'restricted legion rear cannot submit'); scenarios++
  const counterInDisaster = fixture(handCard(counters[0]), covered(actor), { actor, rows: [0] })
  counterInDisaster.arm(); equal(counterInDisaster.highlight(1), true, 'legion row restrictions do not prevent counter placement')
  counterInDisaster.click(); equal(counterInDisaster.commands.length, 1, 'counter replacement remains available during restricted legion placement'); scenarios++
  const frontEmpty = fixture(handCard(counters[0]), null, { actor })
  frontEmpty.arm(); equal(frontEmpty.highlight(1, 0), false, 'counter front empty slot never highlights')
  frontEmpty.click(0); equal(frontEmpty.commands.length, 0, 'counter front empty slot never dispatches'); scenarios++
  const changed = fixture(handCard(counters[0]), covered(actor), { actor })
  changed.arm(); equal(changed.highlight(1), true); changed.player.field[1][1].hidden = false
  equal(changed.highlight(1), false, 'live target changes clear eligibility'); changed.click()
  equal(changed.commands.length, 0, 'a revealed target cannot be replaced using stale highlight'); scenarios++
  const full = fixture(legion, covered(actor), { actor })
  full.player.field = [0, 1].map(row => [0, 1, 2].map(slot => ({ ...covered(actor),
    instanceId: row + '-' + slot, hidden: false })))
  equal(full.evaluate('playableIds.value.length'), 0, 'all face-up occupied slots cannot enable legion play')
  full.player.field[1][1].hidden = true
  equal(full.evaluate('playableIds.value.length'), 1, 'one own covered counter keeps legion play enabled')
  full.player.field[1][1].ownerIndex = 1 - actor
  equal(full.evaluate('playableIds.value.length'), 0, 'foreign ownership cannot enable legion play'); scenarios++
}
console.log(JSON.stringify({ passed: true, scenarios, assertions, cardsScanned: pool.length,
  counterCards: counters.map(card => card.id), scope: 'actual product functions and both SFC template expressions; controlled dependencies; not browser/backend' }))
