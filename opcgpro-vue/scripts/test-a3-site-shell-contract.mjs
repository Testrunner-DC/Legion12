import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import { parse as parseSfc } from '@vue/compiler-sfc'
import ts from 'typescript'
import { createMemoryHistory, createRouter } from 'vue-router'
import { hasTemplateClass, hasInvitationTemplate } from './vue-semantic-contract.mjs'

const fixture = classes => `<template><main><div class="${classes}" /></main></template>`
assert(hasTemplateClass(fixture('invitation-stack'), ['invitation-stack']))
assert(hasTemplateClass(fixture('ui-state-scope invitation-stack extra'), ['invitation-stack']))
assert(!hasTemplateClass(fixture('invitation-stack-old'), ['invitation-stack']))
assert(!hasTemplateClass('<!-- invitation-stack --><template><div /></template>', ['invitation-stack']))
assert(!hasTemplateClass('<template><div /></template><style>.invitation-stack{}</style>', ['invitation-stack']))
assert(!hasTemplateClass(fixture('invitation-gate site-modal-mask'), ['invitation-gate'], ['site-modal-mask']))
assert(hasTemplateClass(fixture('invitation-gate ui-state-scope'), ['invitation-gate'], ['site-modal-mask']))
const tree = '<template><div class="ui-state-scope invitation-stack"><div class="invitation-gate"/><div class="outgoing-invitation-gate"/></div></template>'
assert(hasInvitationTemplate(tree))
assert(!hasInvitationTemplate('<template><i class="invitation-stack invitation-gate outgoing-invitation-gate"/></template>'))
assert(!hasInvitationTemplate('<template><div class="invitation-stack"/><div class="invitation-gate"/><div class="outgoing-invitation-gate"/></template>'))
assert(!hasInvitationTemplate(tree.replace('<div class="ui-state-scope invitation-stack">','<div class="site-modal-mask"><div class="ui-state-scope invitation-stack">').replace('</template>','</div></template>')))
assert(!hasInvitationTemplate(tree.replace('class="invitation-gate"','class="site-modal-mask invitation-gate"')))
assert(!hasInvitationTemplate(tree.replace('<div class="invitation-gate"/>','<i class="invitation-gate"/>')))
assert(!hasInvitationTemplate(tree.replace('<div class="outgoing-invitation-gate"/>','')))
assert(!hasInvitationTemplate('<template><div class="invitation-stack"/><i class="invitation-stack"><div class="invitation-gate"/><div class="outgoing-invitation-gate"/></i></template>'))
assert(!hasInvitationTemplate('<template><div class="invitation-stack"><div class="invitation-gate"/></div><div class="invitation-stack"><div class="outgoing-invitation-gate"/></div></template>'))
assert(!hasInvitationTemplate('<template><div class="invitation-stack"><div class="invitation-gate outgoing-invitation-gate"/></div></template>'))
console.log('A3 template class contract: 17/17 equivalence and structural-boundary checks')

const shellSource = readFileSync(new URL('../src/l12/site/SiteShell.vue', import.meta.url), 'utf8')
const shellScript = parseSfc(shellSource, { filename: 'SiteShell.vue' }).descriptor.scriptSetup?.content
assert(shellScript, 'SiteShell.vue must keep a script-setup navigation owner')
const shellFile = ts.createSourceFile('SiteShell.vue.ts', shellScript, ts.ScriptTarget.ESNext, true, ts.ScriptKind.TS)
function navigationDeclaration(name) {
  const statement = shellFile.statements.find(candidate => ts.isVariableStatement(candidate)
    && candidate.declarationList.declarations.some(declaration => ts.isIdentifier(declaration.name) && declaration.name.text === name))
  assert(statement, `SiteShell.vue must declare ${name}`)
  return statement.getText(shellFile)
}

const routes = [
  { path: '/', name: 'home', component: {} },
  { path: '/battle', name: 'battle', component: {}, meta: { section: 'battle' } },
  { path: '/battle/rankings', name: 'rankings', component: {}, meta: { section: 'battle' } },
  { path: '/decks', name: 'decks', component: {} },
  { path: '/decks/:deckId', name: 'public-deck-detail', component: {} },
]
const router = createRouter({ history: createMemoryHistory(), routes })
const route = new Proxy({}, { get: (_target, key) => router.currentRoute.value[key] })
const context = { route, computed: getter => ({ get value() { return getter() } }) }
vm.runInNewContext([
  navigationDeclaration('mainNav'),
  navigationDeclaration('battleNav'),
  navigationDeclaration('nav'),
  'globalThis.__siteNavigation = nav',
].join('\n'), context)

const mainLabels = ['主页', '资讯', '对战', '牌库', '图鉴', '规则', '我的']
const battleLabels = ['主页', '大厅', '赛事', '牌库', '排行', '好友', '对局']
async function expectNavigation(target, expectedLabels, label) {
  await router.push(target)
  assert.deepEqual(Array.from(context.__siteNavigation.value, item => item.label), expectedLabels, label)
}

await expectNavigation('/battle', battleLabels, 'battle root keeps battle navigation')
await expectNavigation('/battle/rankings', battleLabels, 'existing battle child keeps battle navigation')
await expectNavigation('/decks?from=%2Fbattle', battleLabels, 'battle deck-management entry keeps battle navigation')
assert.equal(context.__siteNavigation.value.find(item => item.label === '牌库')?.to, '/decks?from=%2Fbattle')
await expectNavigation('/decks', mainLabels, 'direct deck-library entry keeps main navigation')
await expectNavigation('/decks?from=%2F', mainLabels, 'home origin does not opt into battle navigation')
await expectNavigation('/decks?from=unknown', mainLabels, 'unknown origin does not opt into battle navigation')
await expectNavigation('/decks?from=https%3A%2F%2Fexample.invalid%2Fbattle', mainLabels, 'external origin does not opt into battle navigation')
await expectNavigation('/decks?from=%2Fbattle%2F', mainLabels, 'near-match origin does not opt into battle navigation')
await expectNavigation({ name: 'decks', query: { from: ['/battle', '/'] } }, mainLabels, 'array origin does not opt into battle navigation')
await expectNavigation('/decks/example?from=%2Fbattle', mainLabels, 'public deck detail does not inherit deck-management navigation')
console.log('A3 site navigation contract: 10/10 route cases plus battle deck-link continuity')
