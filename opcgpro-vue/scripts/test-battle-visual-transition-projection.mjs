import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/game/visualTransitionProjection.ts', import.meta.url), 'utf8')
const output = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const model = await import(`data:text/javascript;base64,${Buffer.from(output).toString('base64')}`)

const card = (instanceId, cardId = 'S02-0609', tapped = false) => ({
  instanceId, cardId, name: instanceId, cardType: 'legion', faction: 'otherworld', cost: 1,
  baseTroops: 1000, troops: 1000, disasterLevel: 0, tapped, summonRound: 1,
})
const sourceCard = card('nuada', 'ST06-M1')
const targetCard = card('target')
const event = {
  sequence: 10, type: 'effect-trigger', playerIndex: 0, text: '银臂努阿达触发', effectSceneId: 'nuada-rune-buff',
  effectResultStatus: 'resolved', cards: [sourceCard, targetCard],
}

assert.equal(model.isCardEffectPresentationEvent(event), true, 'structured trigger must enter the shared card-art queue')
assert.equal(model.isCardEffectPresentationEvent({ ...event, effectSceneId: undefined }), false, 'rule hints without a scene must not masquerade as card triggers')
assert.equal(model.isCardEffectPresentationEvent({ ...event, effectResultStatus: 'declared' }), false, 'declarations must wait for a real result')
assert.equal(model.isCardEffectPresentationEvent({ ...event, type: 'effect-announced' }), false, 'whole-effect announcements stay out of presentation')
assert.deepEqual(model.cardEffectPresentationCards(event).map(item => item.instanceId), ['nuada'], 'only the source card is presented')
assert.deepEqual(model.movementCardsForEvent({ ...event, type: 'move', cards: [sourceCard, targetCard] }).map(item => item.instanceId), ['nuada', 'target'], 'every card in a swap/multi-move is retained')
assert.deepEqual(model.movementCardsForEvent({ ...event, type: 'attach', cards: [targetCard, sourceCard] }).map(item => item.instanceId), ['target', 'nuada'], 'attach host/source order is not guessed in the projection model')
const costLeave = { ...event, type:'leave', text:'加拉哈德作为主动效果的费用被弃置', cards:[sourceCard] }
assert.equal(model.isCombatDefeatLeaveEvent(costLeave), false, 'ordinary field costs must not be swallowed by combat defeat motion')
assert.equal(model.leaveMovementDestination(costLeave), 'graveyard', 'ordinary field costs must animate toward graveyard')
assert.equal(model.leaveMovementDestination({ ...costLeave, text:'返回所有者手牌' }), 'hand')
assert.equal(model.leaveMovementDestination({ ...costLeave, text:'返回所有者牌库顶部' }), 'library')
assert.equal(model.leaveMovementDestination({ ...costLeave, cards:[{ ...sourceCard, isMasterLegion:true }] }), 'master', 'master legions return to the master anchor')
assert.equal(model.leaveMovementDestination({ ...costLeave, text:'被效果移出游戏' }), 'center', 'explicit removals must not falsely fly to the graveyard')
assert.equal(model.isCombatDefeatLeaveEvent({ ...costLeave, text:'因兵力不高于0阵亡' }), true)
assert.equal(model.isSupersededLeaveEvent(costLeave, [{ ...event, type:'return', sequence:11, cards:[sourceCard] }]), true, 'paired authoritative destination event must own the movement once')
assert.equal(model.isSupersededLeaveEvent(costLeave, [{ ...event, type:'grave', sequence:9, cards:[sourceCard] }]), true, 'a dedicated destination event emitted before leave must still own the movement')
assert.equal(model.isSupersededLeaveEvent(costLeave, [{ ...event, type:'derived-vanished', sequence:9, cards:[sourceCard] }]), true, 'derived cards vanish instead of flying to graveyard')
assert.equal(model.isSupersededLeaveEvent(costLeave, [{ ...event, type:'return', sequence:11, cards:[targetCard] }]), false, 'different card movement must not suppress the leave visual')

const hints = model.collectPromptSourceZoneHints([{
  promptId: 'p1', playerIndex: 0, kind: 'optional-card', text: '', validChoices: [], minChoose: 1, maxChoose: 1,
  data: { 'hand-copy:zone': '手牌', 'deck-copy:zone': '牌库', 'grave-copy:zone': '墓地', unrelated: 'x' }, choiceLabels: {},
}])
assert.equal(hints.get('hand-copy'), 'hand')
assert.equal(hints.get('deck-copy'), 'library')
assert.equal(hints.get('grave-copy'), 'graveyard')
assert.equal(hints.has('unrelated'), false)

const player = field => ({ playerIndex: 0, name: 'A', deckName: '', faction: 'otherworld', master: { masterId: 'M', masterName: 'M', hp: 8, maxHp: 8 }, libraryCount: 0, morale: [], field, mulliganDone: true })
const before = model.collectVisualFieldState([player([[card('same', 'X', false), null, null], [null, null, null]])])
const after = model.collectVisualFieldState([player([[card('same', 'X', true), null, null], [null, null, null]])])
assert.deepEqual(model.changedTappedStates(before, after), [{ instanceId: 'same', fromTapped: false, toTapped: true }])
assert.deepEqual(model.changedTappedStates(after, before), [{ instanceId: 'same', fromTapped: true, toTapped: false }])
const knownZones = model.collectKnownCardZones([{
  ...player([[card('field-copy'), null, null], [null, null, null]]),
  hand: [card('hand-copy')], graveyard: [card('grave-copy')], resolving: [card('resolving-copy')],
}])
assert.equal(knownZones.get('hand-copy'), 'hand')
assert.equal(knownZones.get('grave-copy'), 'graveyard')
assert.equal(knownZones.get('field-copy'), 'field')
assert.equal(knownZones.get('resolving-copy'), 'resolving')

console.log('Battle visual transition projection passed: 28/28 assertions')
