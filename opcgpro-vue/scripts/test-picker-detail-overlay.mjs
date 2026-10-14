import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import { parse, compileScript } from '@vue/compiler-sfc'
import { parse as parseTemplate } from '@vue/compiler-dom'
import postcss from 'postcss'
import ts from 'typescript'
import * as Vue from 'vue'

// Real component script/lifecycle plus parsed overlay bindings. Actual browser
// pointer hit testing is covered separately; a fake renderer is not DOM proof.
const read = file => readFileSync(new URL(`../src/l12/${file}`, import.meta.url), 'utf8')
const details = parse(read('CatalogCardDetails.vue')).descriptor
const picker = parse(read('SingleCardPicker.vue')).descriptor
const elements = source => {
  const found = []
  function walk(node) { if (node.type === 1) found.push(node); for (const child of node.children || []) walk(child) }
  walk(parseTemplate(source)); return found
}
const attribute = (node, name) => node.props.find(p => p.type === 6 && p.name === name)
const directive = (node, name, arg) => node.props.find(p => p.type === 7 && p.name === name && p.arg?.content === arg)
function layer(descriptor, selector) {
  const values = []
  for (const style of descriptor.styles) postcss.parse(style.content).walkRules(rule => {
    if (rule.selector === selector) rule.walkDecls('z-index', decl => values.push(Number(decl.value)))
  })
  assert.equal(values.length, 1, `one unambiguous layer for ${selector}`)
  assert.ok(Number.isInteger(values[0])); return values[0]
}
const pickerLayer = layer(picker, '.single-card-picker-mask')
assert.equal(layer(details, '.catalog-detail-mask'), 4500, 'ordinary details retain their existing layer')
assert.equal(pickerLayer, 5200, 'picker layer is not globally lowered')
assert.ok(layer(details, '.catalog-detail-mask.within-picker') > pickerLayer, 'nested detail owns clicks above picker')
const pickerNodes = elements(picker.template.content)
const nested = pickerNodes.filter(n => n.tag === 'CatalogCardDetails')
assert.equal(nested.length, 1)
assert.ok(attribute(nested[0], 'within-picker'), 'only the shared nested consumer opts in')
assert.equal(directive(nested[0], 'on', 'close')?.exp?.content, 'detailCard = null', 'detail close does not select/close the picker')
const detailNodes = elements(details.template.content)
const mask = detailNodes.find(n => attribute(n, 'class')?.value?.content === 'catalog-detail-mask')
const maskClass = directive(mask, 'bind', 'class')?.exp?.content
const enabled = vm.runInNewContext(`(${maskClass})`, { withinPicker: true })
const disabled = vm.runInNewContext(`(${maskClass})`, { withinPicker: false })
assert.equal(enabled['within-picker'], true); assert.equal(disabled['within-picker'], false)
assert.ok(directive(mask, 'on', 'click')?.modifiers.some(m => (m.content ?? m) === 'self'), 'inner content is not a backdrop click')
const closeNode = detailNodes.find(n => attribute(n, 'aria-label')?.value?.content === '关闭卡牌详情')
assert.equal(attribute(closeNode, 'ref')?.value?.content, 'closeButton')
assert.equal(directive(closeNode, 'on', 'click')?.exp?.content, "emit('close')")
for (const descriptor of [details, picker]) {
  const teleport = elements(descriptor.template.content).find(n => n.tag === 'Teleport')
  assert.equal(directive(teleport, 'bind', 'to')?.exp?.content, 'landscapeTeleportTarget()', 'body/logical host routing is unchanged')
}

const renderer = Vue.createRenderer({
  createElement: () => ({ children: [] }), createText: () => ({}), createComment: () => ({}),
  insert(child, parent) { parent.children.push(child) }, remove() {}, setText() {}, setElementText() {},
  parentNode: () => null, nextSibling: () => null, patchProp() {},
})
const script = compileScript(details, { id: 'picker-detail-regression' })
const code = ts.transpileModule(script.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
function mount(withinPicker, originKind = 'element') {
  const document = { activeElement: null }
  class Element {
    isConnected = true
    focusCalls = 0
    focus() { this.focusCalls++; document.activeElement = this }
  }
  // Real DOM nodes are non-reactive; keep the synthetic elements non-reactive too.
  const origin = originKind === 'element' ? Vue.markRaw(new Element()) : {}
  const close = Vue.markRaw(new Element())
  document.activeElement = origin
  const listeners = new Map(), closes = []
  let added = 0, removed = 0
  const window = {
    addEventListener(type, fn) { assert.equal(type, 'keydown'); added++; listeners.set(type, fn) },
    removeEventListener(type, fn) { assert.equal(listeners.get(type), fn); removed++; listeners.delete(type) },
  }
  const module = { exports: {} }
  const imports = { vue: Vue, './CardDetailContent.vue': {}, './mobileViewport': { landscapeTeleportTarget: () => 'body' } }
  vm.runInNewContext(code, { module, exports: module.exports, require: id => { assert.ok(id in imports, id); return imports[id] }, window, document, HTMLElement: Element }, { filename: 'CatalogCardDetails.vue' })
  const component = module.exports.default
  const setup = component.setup
  component.setup = (props, context) => {
    const state = setup(props, context)
    state.closeButton.value = close
    return state
  }
  component.render = () => null
  const props = { card: { id: 'SYNTHETIC', nameZh: '合成卡' }, onClose: () => closes.push('close') }
  if (withinPicker !== undefined) props.withinPicker = withinPicker
  const root = { children: [] }
  renderer.render(Vue.h(component, props), root)
  return { origin, close, document, closes, listeners, counts: () => ({ added, removed }), unmount: () => renderer.render(null, root) }
}
const cases = []
function test(name, run) { run(); cases.push(name) }
test('nested mount focuses close; Escape closes once; unmount returns exact origin and removes listener', () => {
  const f = mount(true)
  assert.equal(f.document.activeElement, f.close)
  let stopped = 0
  f.listeners.get('keydown')({ key: 'Enter', stopPropagation: () => stopped++ })
  assert.equal(f.closes.length, 0)
  f.listeners.get('keydown')({ key: 'Escape', stopPropagation: () => stopped++ })
  assert.equal(stopped, 1); assert.equal(f.closes.length, 1)
  f.unmount()
  assert.equal(f.document.activeElement, f.origin); assert.equal(f.origin.focusCalls, 1)
  assert.equal(f.listeners.size, 0); assert.deepEqual(f.counts(), { added: 1, removed: 1 })
})
test('detached origin is never focused during teardown', () => {
  const f = mount(true); f.origin.isConnected = false; f.unmount()
  assert.equal(f.origin.focusCalls, 0)
})
test('non-element origin is safe', () => {
  const f = mount(true, 'non-element'); f.unmount(); assert.equal(f.listeners.size, 0)
})
for (const value of [false, undefined]) test(`ordinary details ${value ?? 'default'} do not change script focus behavior`, () => {
  const f = mount(value)
  assert.equal(f.document.activeElement, f.origin); assert.equal(f.close.focusCalls, 0)
  f.unmount(); assert.equal(f.origin.focusCalls, 0); assert.equal(f.listeners.size, 0)
})
test('repeated mount/unmount has no retained listener or stale focus origin', () => {
  for (let i = 0; i < 4; i++) { const f = mount(true); f.unmount(); assert.equal(f.listeners.size, 0); assert.equal(f.origin.focusCalls, 1) }
})
console.log(`Shared picker/detail overlay: parsed layer/click/host guards and ${cases.length} real component lifecycle cases passed.`)
