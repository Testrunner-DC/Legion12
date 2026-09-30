import assert from 'node:assert/strict'
import './test-library-placement-recovery.mjs'
import './test-prompt-player-copy.mjs'
import {readFileSync} from 'node:fs'
const source=readFileSync(new URL('../src/l12/game/PromptOverlay.vue',import.meta.url),'utf8')
const playerCopy=readFileSync(new URL('../src/l12/game/promptPlayerCopy.ts',import.meta.url),'utf8')
const board=readFileSync(new URL('../src/l12/game/GameBoard.vue',import.meta.url),'utf8')
const serverRoot=new URL('../../服务端WebSocket/TwelveLegions/',import.meta.url)
// Stage 3B-2 regression scope. The future-card, repository-wide gate is deferred
// until the whole player-information programme has passed Stage 6 acceptance.
const serverSources=[
 'L12RuleKernelIntegration.cs','L12MoralePayments.cs',
 'L12PromptsAndSetup.cs','L12ResponsePresentation.cs',
].map(file=>({file,text:readFileSync(new URL(file,serverRoot),'utf8')}))
const forbiddenPlayerPromptTemplates=[
 /(?:实际结果|实际支付|后续结果).{0,30}权威(?:处理|结算|确定)/,
 /(?:继续|结束|提交|处理)本次声明|本次声明到此结束|后续声明按步骤继续/,
 /本次效果尚未结算|取消本次发动声明|不执行本次可选段/,
 /效果原文中的我方／对方以发动者为准/,
 /未结算效果|尚未结算的效果/,
]
for(const {file,text} of serverSources)
 for(const phrase of forbiddenPlayerPromptTemplates)
  assert(!phrase.test(text),`${file} still exposes a system-process prompt template: ${phrase}`)
assert(!source.includes('waiting.playerName')&&!source.includes('props.game.players[playerIndex]?.name')
 && !source.includes('<strong>{{ player.name }}</strong>'),
 'Prompt and waiting dialogs must use recipient-relative roles, never account names')
assert(!board.includes('props.game.players[prompt.playerIndex]?.name'),
 'Inline prompt dialogs must not show account names')
assert(!source.includes('naturalChoiceLabel(prompt.value?.presentation?.choiceConsequences?.[id], id)\n    ?? cardFor(id)?.name')
 && !board.includes('|| prompt.presentation?.choiceConsequences?.[choice]?.trim()'),
 'A choice consequence must never become its button label')
assert(source.includes('justify-content:safe center'),'Scrollable candidates must keep their first card accessible')
assert(source.includes('overflow-x:hidden;overflow-y:auto'),'Full effect text must not trap footer actions below a clipped panel')
assert(source.includes('.prompt-choices.effect-option-list{display:grid;width:100%;grid-template-columns:repeat(auto-fit,minmax(min(100%,180px),1fr));grid-auto-rows:1fr'))
assert(source.includes('height:auto;min-height:82px!important;max-height:none;overflow:visible'), 'Equal options must grow together without clipping consequences')
assert(source.includes('displayedChoices.value.filter(id => !isDeclineChoice(id))'))
assert(source.includes("if (p.data?.choiceMode === 'instant' || isPureEffectDecision.value) { resolveChoice(id); return }"),'Pure two-choice effect decisions must submit in one click')
assert(source.includes('<footer v-if="!isPureEffectDecision" class="prompt-action-footer" data-ui-contract="equal-action-group">'),'Pure effect decisions must omit the redundant footer')
const footerStart=source.indexOf('<footer v-if="!isPureEffectDecision" class="prompt-action-footer" data-ui-contract="equal-action-group">')
const footer=source.slice(footerStart,source.indexOf('</footer>',footerStart))
assert(footer.indexOf('v-for="choice in supplementalChoices"')<footer.indexOf('prompt-confirm-choice'),'Decline must precede confirm in the same footer')
assert(footer.includes(':disabled="l12State.pendingAction"'))
assert(!source.includes('class="prompt-supplemental-choices"'),'Do not restore the extra decline-only row')
assert(source.includes('.prompt-action-footer>.prompt-footer-choice,.prompt-action-footer>.prompt-confirm-choice'))
assert(source.includes('.prompt-action-footer>button{box-sizing:border-box;width:132px;min-width:132px;max-width:132px;height:48px;min-height:48px;max-height:48px'))
for(const field of ['promptId','activationId','sourceInstanceId','sourceCardId','step','createdRevision','controller'])
 assert(source.includes(`${field}: p.${field}`),'Preserve authoritative binding '+field)
assert(playerCopy.includes('prompt.data?.effectText?.trim()') && source.includes('promptSituationCopy(prompt.value)'), 'Effect decision must display the authoritative full effect text through shared copy')
assert(source.includes('prompt.value?.presentation?.title'),'New snapshots must prefer the authoritative narrative title')
assert(playerCopy.includes('prompt.presentation?.situation'),'New snapshots must prefer the authoritative situation')
assert(playerCopy.includes('prompt.presentation?.instruction') && source.includes('promptInstructionCopy(prompt.value'), 'New snapshots must prefer the authoritative instruction')
assert(playerCopy.includes('prompt.presentation?.choiceConsequences') && source.includes('promptConsequenceCopy(prompt.value'), 'Choice consequences must come from the server contract')
assert(source.includes('waitingPrompt.value?.waitingSummary'),'Waiting recipients must render the public-safe server summary')
assert(source.includes('legacyPromptTitle'),'Old snapshots must retain a natural-language title fallback')
assert(source.includes('legacyWaitingSummary'),'Old snapshots must retain a public-safe waiting fallback')
assert(!source.includes("return isDeclineChoice(id) ? '不发动' : '发动'"),'Client must not overwrite authoritative option text')
assert(!source.includes("return prompt.value?.kind ?? ''"),'Client must never display an internal prompt kind')
console.log('Prompt presentation guards passed: centered safe overflow, one equal-size footer, pending lock and full authoritative binding.')
