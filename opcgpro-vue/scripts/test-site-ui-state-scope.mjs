import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { parse } from '@vue/compiler-dom'
import { parse as parseSfc } from '@vue/compiler-sfc'
import ts from 'typescript'
import { usesSiteUiStates } from '../src/l12/site/siteUiStateScope.ts'

const cases = [
  [{}, true], [{ section: 'battle' }, true], [{ requiresAdmin: true }, true],
  [{ requiresAccount: true }, true], [{ immersive: true }, false],
  [{ landscapeCanvas: true }, false],
  [{ immersive: true, landscapeCanvas: true, editorAdaptiveCanvas: true }, false],
  [{ immersive: false, landscapeCanvas: false }, true],
]
for (const [meta, expected] of cases) assert.equal(usesSiteUiStates(meta), expected)
console.log(`Site UI state route policy: ${cases.length}/${cases.length}`)

// Inspect semantic dialog roots, not exact serialized class attributes or CSS text.
const dialogs = ['AdminAccountsPage.vue', 'AdminAlternateArtsPanel.vue',
  'RankedMasterTitleRulesModal.vue', 'ArticleContentRenderer.vue', 'BattleHubPage.vue',
  'DeckSnapshotViewer.vue', 'AdminRiskActionDialog.vue', 'MobileFilterSheet.vue']
for (const file of dialogs) {
  const source = fs.readFileSync(path.join(import.meta.dirname, '../src/l12/site', file), 'utf8')
  const ast = parse(parseSfc(source).descriptor.template.content, {comments:false})
  let found = 0
  function visit(node, scoped = false, teleported = false) {
    const classes = node.props?.find(p=>p.type===6 && p.name==='class')?.value?.content?.split(/\s+/) || []
    const participates = scoped || classes.includes('ui-state-scope') || classes.includes('ui-dialog')
    const detached = teleported || node.tag==='Teleport'
    if (detached && (node.tag==='dialog' || node.props?.some(p=>p.type===6 && p.name==='role' && p.value?.content==='dialog'))) {
      found++
    }
    if (detached && ['button','input','select','textarea','UiButton'].includes(node.tag)) {
      assert(participates, `${file}: detached ${node.tag} must opt into site states`)
    }
    for (const child of node.children || []) visit(child, participates, detached)
  }
  visit(ast)
  assert(found>0, `${file}: fixture audit must find a real dialog`)
}
console.log(`Site Teleport dialog state coverage: ${dialogs.length}/${dialogs.length}`)

// App-level consumers are outside SiteShell. Their state opt-in must follow
// the same route policy rather than leaking ordinary control paint into canvas.
function hasRouteState(source, rootClass, stateClass, tag) {
  const sfc = parseSfc(source)
  const content = sfc.descriptor.template?.content
  if (sfc.errors.length) return false
  if (!content) return false
  let ast
  try { ast = parse(content, { comments: false }) } catch { return false }
  const roots = []
  function collect(node) {
    const classes = node.props?.find(p => p.type === 6 && p.name === 'class')?.value?.content?.split(/\s+/) || []
    if (classes.includes(rootClass)) roots.push(node)
    for (const child of node.children || []) collect(child)
  }
  collect(ast)
  if (roots.length !== 1 || roots[0].tag !== tag) return false
  const staticClasses = roots[0].props?.find(p => p.type === 6 && p.name === 'class')?.value?.content?.split(/\s+/) || []
  if (staticClasses.includes(stateClass) || roots[0].props?.some(p => p.type === 7 && p.name === 'bind' && (!p.arg || !p.arg.isStatic))) return false
  const binding = roots[0].props?.find(p => p.type === 7 && p.name === 'bind' && p.arg?.isStatic && p.arg.content === 'class')
  if (!binding?.exp?.content) return false
  const expressionSource = ts.createSourceFile('state.ts', `const state = (${binding.exp.content})`, ts.ScriptTarget.Latest, true)
  const unwrap = node => ts.isParenthesizedExpression(node) ? unwrap(node.expression) : node
  const expression = expressionSource.statements[0]?.declarationList?.declarations[0]?.initializer
  if (!expression || !ts.isObjectLiteralExpression(unwrap(expression))) return false
  const properties = unwrap(expression).properties
  if (properties.some(p => ts.isSpreadAssignment(p) || p.name && ts.isComputedPropertyName(p.name))) return false
  const matching = properties.filter(p => ts.isPropertyAssignment(p) && (ts.isStringLiteral(p.name) || ts.isIdentifier(p.name)) && p.name.text === stateClass)
  if (matching.length !== 1) return false
  const property = matching[0]
  const call = unwrap(property.initializer)
  return ts.isCallExpression(call) && ts.isIdentifier(call.expression) && call.expression.text === 'usesSiteUiStates'
    && call.arguments.length === 1 && ts.isPropertyAccessExpression(unwrap(call.arguments[0]))
    && ts.isIdentifier(unwrap(call.arguments[0]).expression) && unwrap(call.arguments[0]).expression.text === 'route'
    && unwrap(call.arguments[0]).name.text === 'meta'
}
let globalCases = 0
for (const [file, rootClass, stateClass, tag] of [
  ['GlobalBugFeedback.vue', 'bug-feedback-dialog', 'ui-state-scope', 'section'],
  ['GlobalBugFeedback.vue', 'bug-feedback-trigger', 'ui-state-control', 'button'],
  ['FriendRequestNotifications.vue', 'friend-request-dialog', 'ui-state-scope', 'section'],
  ['RankedIntegrityNotice.vue', 'integrity-notice', 'ui-state-scope', 'section'],
]) {
  const source = fs.readFileSync(path.join(import.meta.dirname, '../src/l12/site', file), 'utf8')
  const expect = (candidate, accepted) => { assert.equal(hasRouteState(candidate, rootClass, stateClass, tag), accepted, `${file}: ${rootClass} route guard`); globalCases++ }
  expect(source, true)
  expect(source.replace(`class="${rootClass}"`, `class="another-state ${rootClass}"`), true)
  expect(source.replaceAll('usesSiteUiStates(route.meta)', 'true'), false)
  expect(source.replaceAll('usesSiteUiStates(route.meta)', "'usesSiteUiStates(route.meta)'"), false)
  expect(source.replaceAll('usesSiteUiStates(route.meta)', 'usesSiteUiStates({})'), false)
  expect(source.replaceAll('usesSiteUiStates(route.meta)', 'usesSiteUiStates(route.meta) || true'), false)
  expect(source.replace(`class="${rootClass}"`, `class="${rootClass}-fake"`), false)
  expect(source.replace(`class="${rootClass}"`, `class="${rootClass} ${stateClass}"`), false)
  expect(source.replace(`'${stateClass}': usesSiteUiStates(route.meta)`, `'${stateClass}': usesSiteUiStates(route.meta), '${stateClass}': true`), false)
  expect(source.replace(`'${stateClass}': usesSiteUiStates(route.meta)`, `'${stateClass}': usesSiteUiStates(route.meta), ...uncontrolled`), false)
  expect(source.replace(`class="${rootClass}"`, `class="${rootClass}" v-bind="uncontrolled"`), false)
  expect(`<template><!-- <${tag} class="${rootClass}" :class="{ '${stateClass}': usesSiteUiStates(route.meta) }"/> --></template>`, false)
}
console.log(`Global site route-aware state contracts: ${globalCases}/${globalCases}`)
