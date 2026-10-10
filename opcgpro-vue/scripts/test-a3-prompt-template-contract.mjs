import assert from 'node:assert/strict'
import { promptFooterContract } from './prompt-template-contract.mjs'

const fixture = `<template><section v-else-if="prompt" class="prompt-panel prompt-choice-panel">
  <footer v-if="!isPureEffectDecision" class="prompt-action-footer" data-ui-contract="equal-action-group">
    <template v-if="showSupplemental"><button v-for="choice in supplementalChoices" class="prompt-footer-choice" :disabled="l12State.pendingAction">Decline</button></template>
    <template v-if="ordered"><button class="primary prompt-confirm-choice" :disabled="l12State.pendingAction || invalidOrder">Confirm order</button></template>
    <template v-else><button class="primary prompt-confirm-choice" :disabled="l12State.pendingAction || invalidSelection">Confirm</button></template>
  </footer>
</section></template>`
let checks = 0
function check(source, expected, label) {
  const actual = promptFooterContract(source)
  for (const [key, value] of Object.entries(expected)) assert.equal(actual[key], value, `${label}: ${key}`)
  checks++
}
const passes = { root: true, ordered: true, pending: true, noExtraDeclineRow: true }
check(fixture, passes, 'real footer shape')
check(fixture.replace('class="primary prompt-confirm-choice"', 'class="prompt-confirm-choice extra primary"')
  .replace('class="prompt-action-footer"', 'class="extra prompt-action-footer"')
  .replace('!isPureEffectDecision', '! ( isPureEffectDecision )')
  .replace('choice in supplementalChoices', 'choice   in   supplementalChoices')
  .replaceAll('l12State.pendingAction', '( l12State . pendingAction )'), passes, 'equivalent class order and expression whitespace')
check(fixture.replace('v-if="!isPureEffectDecision" class="prompt-action-footer"', 'class="prompt-action-footer" v-if="!isPureEffectDecision"'), passes, 'equivalent attribute order')
check(fixture.replace('<footer ', '<div ').replace('</footer>', '</div>'), { root: false }, 'wrong footer tag')
check(fixture.replace('v-else-if="prompt"', 'v-else-if="other"'), { root: false }, 'wrong panel binding')
check(fixture.replace('class="prompt-action-footer"', 'class="prompt-action-footer-old"'), { root: false }, 'similar footer class')
check(fixture.replace('v-if="!isPureEffectDecision"', ''), { root: false }, 'missing pure-decision condition')
check(fixture.replace('v-if="!isPureEffectDecision"', 'v-if="isPureEffectDecision"'), { root: false }, 'reversed pure-decision condition')
check(fixture.replace('v-if="!isPureEffectDecision"', 'v-if="\'!isPureEffectDecision\'"'), { root: false }, 'literal condition is not a binding')
check(fixture.replace('data-ui-contract="equal-action-group"', ''), { root: false }, 'missing equal-action marker')
check(fixture.replace('v-for="choice in supplementalChoices"', ''), { ordered: false }, 'missing supplemental loop')
check(fixture.replace('choice in supplementalChoices', 'choice in otherChoices'), { ordered: false }, 'wrong supplemental loop')
check(fixture.replaceAll('class="primary prompt-confirm-choice"', 'class="primary"'), { ordered: false }, 'missing confirmation nodes')
check(fixture.replace('class="prompt-footer-choice"', 'class="prompt-footer-choice prompt-confirm-choice"'), { ordered: false }, 'same-node supplemental and confirm')
const supplemental = fixture.match(/    <template v-if="showSupplemental">.*?<\/template>/)[0]
check(fixture.replace(supplemental, '').replace('  </footer>', `${supplemental}\n  </footer>`), { ordered: false }, 'supplemental after confirms')
check(fixture.replace(supplemental, '').replace('</section>', `${supplemental}</section>`), { ordered: false }, 'supplemental in a different owner')
check(fixture.replace(supplemental, `<div>${supplemental}</div>`), { ordered: false }, 'real wrapper changes button ownership')
check(fixture.replace('class="prompt-footer-choice" :disabled="l12State.pendingAction"', 'class="prompt-footer-choice"'), { pending: false }, 'missing supplemental pending binding')
check(fixture.replace(':disabled="l12State.pendingAction || invalidOrder"', ':disabled="invalidOrder"'), { pending: false }, 'one confirm misses pending binding')
check(fixture.replace(':disabled="l12State.pendingAction"', 'disabled="l12State.pendingAction"'), { pending: false }, 'literal disabled attribute is not the binding')
check(fixture.replace(':disabled="l12State.pendingAction"', ':disabled="!l12State.pendingAction"'), { pending: false }, 'negated pending binding')
check(fixture.replace(':disabled="l12State.pendingAction"', ':disabled="l12State.pendingAction && extra"'), { pending: false }, 'conditional AND is not a pending lock')
check(fixture.replace(':disabled="l12State.pendingAction"', ':disabled="\'l12State.pendingAction\'"'), { pending: false }, 'string literal is not pending state')
check(fixture.replace('</section>', '<div class="extra prompt-supplemental-choices"/></section>'), { noExtraDeclineRow: false }, 'extra decline row with added class')
check(`<template><!-- ${fixture.slice(10, -11)} --><section/></template>`, { root: false }, 'comment cannot impersonate footer')
check(`<script>const decoy = ${JSON.stringify(fixture)};</script><template><section/></template>`, { root: false }, 'script cannot impersonate footer')
check(fixture.replace('</section>', fixture.match(/  <footer[\s\S]*?  <\/footer>/)[0] + '</section>'), { root: false }, 'duplicate footer')
check(fixture.replace('<button v-for="choice in supplementalChoices"', '<i v-for="choice in supplementalChoices"').replace('>Decline</button>', '>Decline</i>'), { ordered: false }, 'wrong supplemental element')
check(fixture.replace('  </footer>', '<div><button>Nested action</button></div>  </footer>'),
  { root: true, ordered: true, pending: false }, 'nested footer action must not bypass pending lock')
check(fixture.replace('  </footer>', '<div><button :disabled="l12State.pendingAction || otherBusy">Nested action</button></div>  </footer>'),
  passes, 'nested footer action with actual pending lock')
console.log(`A3 Prompt footer template contract: ${checks}/${checks}`)
