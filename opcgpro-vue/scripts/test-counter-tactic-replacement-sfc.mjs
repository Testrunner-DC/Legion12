import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import { parse, compileScript } from '@vue/compiler-sfc'
import * as vue from 'vue'

const source = fs.readFileSync(new URL('../src/l12/game/PlayerMat.vue', import.meta.url), 'utf8')
const descriptor = parse(source, { filename: 'PlayerMat.vue' }).descriptor
const compiled = compileScript(descriptor, { id: 'counter-placement-runtime', inlineTemplate: true })
const code = ts.transpileModule(compiled.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText
function importTs(relative) {
  const module = { exports: {} }
  const source = fs.readFileSync(new URL('../src/l12/' + relative, import.meta.url), 'utf8')
  const output = ts.transpileModule(source, { compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022,
  } }).outputText
  new Function('module', 'exports', output)(module, module.exports)
  return module.exports
}
const element = (tag, text = '') => ({ tag, text, props: {}, children: [], parent: null })
const body = element('body')
const renderer = vue.createRenderer({
  createElement: tag => element(tag), createText: text => element('#text', text),
  createComment: text => element('#comment', text),
  setText: (node, text) => { node.text = text },
  setElementText: (node, text) => { node.text = text; node.children = [] },
  insert(child, parent, anchor) {
    if (child.parent) { const index = child.parent.children.indexOf(child); if (index >= 0) child.parent.children.splice(index, 1) }
    child.parent = parent
    const index = anchor ? parent.children.indexOf(anchor) : -1
    if (index < 0) parent.children.push(child); else parent.children.splice(index, 0, child)
  },
  remove(child) {
    if (child.parent) { const index = child.parent.children.indexOf(child); if (index >= 0) child.parent.children.splice(index, 1) }
    child.parent = null
  },
  patchProp: (node, key, _old, value) => { node.props[key] = value },
  parentNode: node => node.parent,
  nextSibling: node => node.parent?.children[node.parent.children.indexOf(node) + 1],
  querySelector: selector => selector === 'body' ? body : null,
  setScopeId() {},
  insertStaticContent(content, parent, anchor) {
    const node = element('#static', content)
    node.parent = parent
    const index = anchor ? parent.children.indexOf(anchor) : -1
    if (index < 0) parent.children.push(node); else parent.children.splice(index, 0, node)
    return [node, node]
  },
})
const leaf = vue.defineComponent({ render() { return vue.h('span') } })
const portal = vue.defineComponent({ props: { lane: String }, inheritAttrs: false, setup(_props, { slots }) { return () => slots.default?.() } })
const deps = {
  vue,
  './mobileBattleDock': { useMobileBattleDock: () => null },
  './battlefieldTargetPresentation': importTs('game/battlefieldTargetPresentation.ts'),
  './canopicRelicPresentation': importTs('game/canopicRelicPresentation.ts'),
  '../cardPresentation': importTs('cardPresentation.ts'),
  '../specialAssets': new Proxy({}, { get: () => '' }),
  '../mobileViewport': { landscapeTeleportTarget: () => 'body' },
}
const module = { exports: {} }
new Function('require', 'module', 'exports', code)(id => {
  if (id in deps) return deps[id]
  if (id.endsWith('BattleDockPortal.vue')) return { __esModule: true, default: portal }
  if (id.endsWith('.vue')) return { __esModule: true, default: leaf }
  throw new Error('Unexpected component dependency ' + id)
}, module, module.exports)
const walk = node => [node, ...node.children.flatMap(walk)]
const classes = node => String(node.props.class ?? '').split(/\s+/)
let scenarios = 0, assertions = 0
function equal(actual, expected, message) { assert.deepEqual(actual, expected, message); assertions++ }
const errors = [], warnings = []
for (const actor of [0, 1]) for (const side of ['my', 'opponent']) for (const mobile of [false, true]) {
  const target = { instanceId: 'counter-target', cardId: 'S01-0016', name: '绝对防御',
    cardType: 'tactic', isCounterTactic: true, hidden: true, ownerIndex: actor,
    cost: 2, troops: 0, baseTroops: 0, faction: 'universal', tapped: false, summonRound: 1, disasterLevel: 0 }
  const player = { playerIndex: actor, name: '合成玩家', faction: 'taiyangcheng', hand: [], handCount: 0,
    field: [[null, null, null], [null, target, null]], morale: [], temporaryMorale: 0,
    master: { masterId: 'S01-02M1', masterName: '合成主宰', hp: 8, maxHp: 8, abilities: [] },
    graveyard: [], graveyardCount: 0, libraryCount: 40, libraryTop: null, relic: null, extraRelics: [],
    specialZones: { trials: [], canopicTrack: [], canopicProgress: [] }, factionEffect: null }
  const events = []
  const props = vue.reactive({ player, side, mobileLayout: mobile, controllable: true, viewerPlayerIndex: actor,
    placementMode: true, placementRow: 1, placementCanReplaceCounter: true, actionsEnabled: false,
    onSlot: (...args) => events.push(args) })
  const root = element('root')
  const app = renderer.createApp({ render: () => vue.h(module.exports.default, props) })
  app.config.errorHandler = error => { errors.push(error.stack ?? error.message) }
  app.config.warnHandler = message => { warnings.push(message) }
  app.mount(root)
  await vue.nextTick()
  const slot = () => walk(root).find(node => node.props['data-tutorial-row'] === 1 && node.props['data-tutorial-slot'] === 1)
  assert.ok(slot(), 'real compiled PlayerMat renders the exact rear slot')
  equal(classes(slot()).includes('available'), true, 'own hidden counter has a real rendered available class')
  slot().props.onClick({})
  equal(events.length, 1, 'actual click binding emits one slot action')
  equal(events[0].slice(0, 2), [1, 1], 'actual click emits the exact selected row/slot')
  equal(events[0][2].instanceId, target.instanceId, 'actual click emits the real target card')
  slot().props.onKeyup({ key: 'Enter' })
  equal(events.length, 2, 'keyboard Enter emits one additional action')
  slot().props.onKeyup({ key: 'Escape' })
  equal(events.length, 2, 'unrelated key does not emit placement')

  props.player.field[1][1].hidden = false; await vue.nextTick()
  equal(classes(slot()).includes('available'), false, 'same mounted component removes availability after reveal')
  props.player.field[1][1].hidden = true; props.player.field[1][1].ownerIndex = 1 - actor; await vue.nextTick()
  equal(classes(slot()).includes('available'), false, 'foreign owner cannot gain rendered availability')
  props.player.field[1][1].ownerIndex = undefined; await vue.nextTick()
  equal(classes(slot()).includes('available'), true, 'old snapshots without owner keep existing controller fallback')
  props.controllable = false; await vue.nextTick()
  equal(classes(slot()).includes('available'), false, 'uncontrolled opponent covered card cannot gain availability')
  props.controllable = true; props.placementCanReplaceCounter = false; await vue.nextTick()
  equal(classes(slot()).includes('available'), false, 'active tactic cannot replace occupied slot')
  props.player.field[1][1] = null; await vue.nextTick()
  equal(classes(slot()).includes('available'), true, 'empty destination retains existing placement behavior')
  props.placementRow = 0; await vue.nextTick()
  equal(classes(slot()).includes('available'), false, 'row-limited placement does not highlight other row')
  props.placementMode = false; await vue.nextTick()
  equal(walk(root).filter(node => classes(node).includes('available')).length, 0, 'leaving placement clears all highlights')
  app.unmount()
  scenarios++
}
assert.deepEqual(errors, [], 'actual SFC runtime must have zero component errors')
assert.deepEqual(warnings, [], 'actual SFC runtime must have zero warnings')
console.log(JSON.stringify({ passed: true, scenarios, assertions,
  scope: 'actual complete PlayerMat SFC compiled/rendered in Vue memory host; real template click/Enter handlers; leaf art/portal stubs; not browser geometry/server' }))
