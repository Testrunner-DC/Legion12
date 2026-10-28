import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'
import * as vue from 'vue'
import { parse, compileScript, compileTemplate, compileStyle } from '@vue/compiler-sfc'

const root = new URL('../src/l12/', import.meta.url)
const helperPath = new URL('friendListPresentation.ts', root)
function evaluate(source, dependencies) {
  const module = { exports: {} }
  vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
    { module, exports: module.exports, require: id => {
      if (id in dependencies) return dependencies[id]
      throw new Error(`Unexpected dependency ${id}`)
    }, Map, Set, Boolean })
  return module.exports
}
const helper = fs.existsSync(helperPath) ? evaluate(fs.readFileSync(helperPath, 'utf8'), {}) : {}
const failures = []
function test(label, action) { try { action(); console.log(`PASS ${label}`) } catch (error) { failures.push(label); console.error(`FAIL ${label}: ${error.message}`) } }
for (const file of ['FriendsPage.vue', 'SocialPopover.vue']) {
  const source = fs.readFileSync(new URL(`site/${file}`, root), 'utf8')
  test(`${file} actual SFC script/template/styles compile`, () => {
    const { descriptor, errors } = parse(source)
    assert.equal(errors.length, 0)
    const script = compileScript(descriptor, { id: 'friends-qa' })
    assert.equal(compileTemplate({ source: descriptor.template.content, filename: file, id: 'friends-qa', compilerOptions: { bindingMetadata: script.bindings } }).errors.length, 0)
    for (const style of descriptor.styles) assert.equal(compileStyle({ source: style.content, filename: file, id: 'friends-qa', scoped: style.scoped }).errors.length, 0)
  })
  const friends = vue.reactive([
    { accountId: 'offline', username: '离线', online: true },
    { accountId: 'fallback', username: '兜底在线', online: true },
    { accountId: 'online', username: '实时在线', online: false },
    { accountId: 'missing', username: '无状态', online: false },
  ])
  const presence = vue.reactive([{ accountId: 'offline', online: false }, { accountId: 'online', online: true }])
  const resource = { accountId: 'owner', friends, requests: [], blocked: [] }
  const dependencies = {
    vue: { ...vue, onMounted: () => {}, onBeforeUnmount: () => {}, watch: () => {} },
    '@/l12/net': { l12State: { presence } }, '../net': { l12State: { presence } },
    '@/l12/platform': { platformState: {} }, '../platform': {},
    '@/l12/friendResource': { friendResource: resource }, '../friendResource': { friendResource: resource },
    '@/l12/useActionGate': { useActionGate: () => ({}) },
    '../chatResource': { chatResource: { state: { accountId: 'owner', threads: {} } } },
    '@/l12/friendListPresentation': helper, '../friendListPresentation': helper,
    './ChatPanel.vue': {},
  }
  let script = source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)[1]
  script = script.replace('defineProps<', '__defineProps<').replace('defineEmits<', '__defineEmits<')
  // Compile the real setup script, retaining actual computed and selection behavior.
  const model = evaluate(`function __defineProps<T>():T{return {} as T};function __defineEmits<T>():any{return ()=>{}};\n${script}\nexport { selectedId, selected }; export const rows = typeof orderedFriends === 'undefined' ? ${file === 'FriendsPage.vue' ? 'friends' : 'computed(() => friendResource.friends)'} : orderedFriends`, dependencies)
  const ids = () => Array.from(model.rows.value, friend => friend.accountId)
  test(`${file} stable online-first, presence false overrides fallback true`, () => assert.deepEqual(ids(), ['fallback', 'online', 'offline', 'missing']))
  test(`${file} presence changes preserve selected conversation and same-group order`, () => {
    model.selectedId.value = 'offline'
    presence[0].online = true
    assert.deepEqual(ids(), ['offline', 'fallback', 'online', 'missing'])
    assert.equal(model.selectedId.value, 'offline')
    assert.equal(model.selected.value?.accountId, 'offline')
    assert.deepEqual(Array.from(friends, friend => friend.accountId), ['offline', 'fallback', 'online', 'missing'], 'source array must remain unchanged')
  })
}
if (failures.length) { console.error(`${failures.length} failed`); process.exitCode = 1 }
