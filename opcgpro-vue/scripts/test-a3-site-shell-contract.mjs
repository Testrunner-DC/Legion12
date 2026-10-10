import assert from 'node:assert/strict'
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
