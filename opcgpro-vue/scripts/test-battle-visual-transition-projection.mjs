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
const entryGroup = 'play:entry-source'
const entryPlay = { sequence:8, type:'play', playerIndex:0, text:'打出登场军团', playerLogGroupId:entryGroup, playerLogTiming:'enter', cards:[sourceCard] }
const entryResult = { ...event, sequence:10, type:'effect-result', playerLogGroupId:entryGroup, playerLogTiming:'enter', cards:[sourceCard] }
const entryKey = `${entryGroup}\u001fenter\u001f${sourceCard.instanceId}`
const ownsEntry = key => key === entryKey
assert.equal(model.entryPresentationTransactionKey(entryPlay), entryKey, 'stable group/timing/source identity owns an entry transaction')
assert.equal(model.entryPresentationTransactionKey({ ...entryPlay, cards:[{ ...sourceCard, hidden:false, identityKnown:false }] }), entryKey,
  'real serialized public cards keep entry identity even when identityKnown has its ordinary false default')
assert.equal(model.entryMovementTransactionKey(entryPlay, 'hand', 'field', 'cursor'), entryKey, 'a cursor-proven hand-to-field legion movement registers the entry transaction')
assert.equal(model.entryMovementTransactionKey({ ...entryPlay, type:'put' }, 'resolving', 'field', 'hint'), entryKey, 'a hint-proven resolving-to-field legion put registers the entry transaction')
assert.equal(model.entryMovementTransactionKey(entryPlay, 'hand', 'field', 'play-contract'), entryKey,
  'a public legion play contract survives an opponent projection without private hand instances')
assert.equal(model.entryMovementTransactionKey({ ...entryPlay, playerLogTiming:'promotion-enter' }, 'hand', 'field', 'play-contract'), null,
  'the private-hand play contract does not broaden to promotion entry')
assert.equal(model.entryMovementTransactionKey({ ...entryPlay, type:'put' }, 'resolving', 'field', 'fallback'), null,
  'an unknown put source may use resolving for layout but cannot register an entry continuation')
assert.equal(model.entryEffectContinuationTransaction(entryResult, ownsEntry), entryKey, 'matching entry result consumes the registered movement transaction without a second full card')
assert.equal(model.entryEffectContinuationTransaction(entryResult, () => false), null, 'matching log metadata alone cannot invent an unclaimed movement transaction')
assert.equal(model.entryEffectContinuationTransaction({ ...entryResult, playerLogGroupId:undefined }, ownsEntry), null, 'missing group never guesses an entry transaction')
assert.equal(model.entryEffectContinuationTransaction({ ...entryResult, playerLogTiming:undefined }, ownsEntry), null, 'missing timing never guesses an entry transaction')
assert.equal(model.entryEffectContinuationTransaction({ ...entryResult, cards:[targetCard] }, ownsEntry), null, 'a different instance cannot borrow the entry transaction')
assert.equal(model.entryEffectContinuationTransaction({ ...entryResult, playerLogGroupId:'play:later-entry' }, ownsEntry), null, 'a later real entry group is independent')
assert.equal(model.entryEffectContinuationTransaction({ ...entryResult, cards:[{ ...sourceCard, hidden:true }] }, ownsEntry), null, 'hidden sources retain their existing presentation boundary')
assert.equal(model.entryEffectContinuationTransaction({ ...entryResult, playerLogTiming:'active' }, ownsEntry), null, 'an already-field active effect remains a full-card presentation')
const factResult = { ...entryResult, playerPresentationFactSequences:[19] }
assert.deepEqual(model.effectResultPresentationFactContinuation(factResult, sequence => sequence === 19), [19],
  'a result becomes a text-only continuation only after its exact authority fact is visibly owned')
assert.equal(model.effectResultPresentationFactContinuation(factResult, () => false), null,
  'an unclaimed fact retains the full-card fallback')
assert.equal(model.effectResultPresentationFactContinuation({ ...factResult, playerPresentationFactSequences:[] }, () => true), null,
  'an empty reference list cannot suppress a card')
assert.equal(model.effectResultPresentationFactContinuation({ ...factResult, playerPresentationFactSequences:[19, 20] }, sequence => sequence === 19), null,
  'partial ownership cannot suppress a multi-fact result')
assert.equal(model.effectResultPresentationFactContinuation({ ...factResult, playerPresentationFactSequences:[19, 19] }, () => true), null,
  'malformed duplicate references fail safe instead of changing presentation')
assert.equal(model.effectResultPresentationFactContinuation({ ...factResult, type:'effect-trigger' }, () => true), null,
  'non-terminal presentation events do not borrow result fact references')
assert.equal(model.entryMovementTransactionKey({ ...entryPlay, cards:[{ ...sourceCard, cardType:'artifact' }] }, 'hand', 'relic', 'cursor'), null, 'artifact hand-to-relic movement never owns a battlefield entry continuation')
assert.equal(model.entryMovementTransactionKey({ ...entryPlay, cards:[{ ...sourceCard, cardType:'master' }] }, 'hand', 'field', 'cursor'), null, 'master movement never owns a legion entry continuation')
assert.equal(model.entryMovementTransactionKey({ ...entryPlay, cards:[{ ...sourceCard, cardType:'' }] }, 'hand', 'field', 'cursor'), null, 'missing card type keeps the existing full-card effect presentation')
assert.equal(model.entryMovementTransactionKey({ ...entryPlay, type:'put' }, 'graveyard', 'field', 'cursor'), null, 'graveyard put retains a separate effect presentation')
assert.equal(model.entryMovementTransactionKey({ ...entryPlay, type:'put' }, 'library', 'field', 'cursor'), null, 'library put retains a separate effect presentation')
const reentryPlay = { ...entryPlay, sequence:20, playerLogGroupId:'play:reentry' }
const reentryResult = { ...entryResult, sequence:21, playerLogGroupId:'play:reentry' }
const reentryKey = model.entryMovementTransactionKey(reentryPlay, 'hand', 'field', 'cursor')
assert.equal(model.entryEffectContinuationTransaction(reentryResult, key => key === reentryKey),
  `play:reentry\u001fenter\u001f${sourceCard.instanceId}`, 'a true later re-entry owns a new visual transaction')
assert.deepEqual(model.movementCardsForEvent({ ...event, type: 'move', cards: [sourceCard, targetCard] }).map(item => item.instanceId), ['nuada', 'target'], 'every card in a swap/multi-move is retained')
assert.deepEqual(model.movementCardsForEvent({ ...event, type: 'attach', cards: [targetCard, sourceCard] }).map(item => item.instanceId), ['target', 'nuada'], 'attach host/source order is not guessed in the projection model')
assert.deepEqual(model.movementCardsForEvent({ ...event, type: 'mill', cards: [sourceCard, targetCard] }).map(item => item.instanceId), ['nuada', 'target'], 'every milled card keeps authoritative event order')

const claims = model.createMovementClaimState()
assert.deepEqual(model.claimFreshMovementEvents([{ ...event, sequence: 8 }], claims), [], 'first snapshot is a reconnect/replay baseline, not a fresh animation')
const firstFresh = model.claimFreshMovementEvents([{ ...event, sequence: 8 }, { ...event, sequence: 10, type: 'discard', cards: [sourceCard] }], claims)
assert.deepEqual(firstFresh.map(item => item.sequence), [10], 'new movement event is claimed once')
assert.deepEqual(model.claimFreshMovementEvents([{ ...event, sequence: 8 }, { ...event, sequence: 10, type: 'discard', cards: [sourceCard] }], claims), [], 'reentrant watcher snapshot cannot reclaim a sequence while its animation is preparing')
assert.deepEqual(model.claimFreshMovementEvents([{ ...event, sequence: 8 }, { ...event, sequence: 10, type: 'discard', cards: [sourceCard] }, { ...event, sequence: 11, type: 'return', cards: [sourceCard] }], claims).map(item => item.sequence), [11], 'a later real movement of the same instance remains fresh')
const discardKey = model.movementFactKey({ ...event, sequence: 10 }, 0, sourceCard, 'hand', 'graveyard')
assert.equal(model.claimMovementFact(claims, discardKey), true, 'first authoritative movement fact is admitted')
assert.equal(model.claimMovementFact(claims, discardKey), false, 'same sequence/card-index/from/to fact is deduplicated')
assert.equal(model.claimMovementFact(claims, model.movementFactKey({ ...event, sequence: 11 }, 0, sourceCard, 'graveyard', 'library')), true, 'same instance at a later sequence and different direction is not swallowed')
assert.equal(model.claimMovementFact(claims, model.movementFactKey({ ...event, sequence: 11 }, 1, sourceCard, 'graveyard', 'library')), true, 'event-local card order is part of movement identity')
const resetClaims = model.createMovementClaimState()
model.resetMovementClaimState(resetClaims, 0)
assert.deepEqual(model.claimFreshMovementEvents([{ ...event, sequence: 1, type: 'mill', cards: [sourceCard] }], resetClaims).map(item => item.sequence), [1], 'first live event after an empty new-match baseline is animated')
assert.equal(model.isAuthoritativePublicFaceMovement({ ...event, type: 'mill' }, 'library', 'graveyard'), true, 'public mill owns a face-up authority fact across the hidden library boundary')
assert.equal(model.isMovementCardConcealed({ ...event, type: 'mill' }, { ...sourceCard, hidden: true, identityKnown: false }, 'library', 'graveyard'), false, 'public mill displays the authoritative event face while moving')
assert.equal(model.isMovementCardConcealed({ ...event, type: 'mill' }, { ...sourceCard, cardId: 'hidden-card' }, 'library', 'graveyard'), true, 'a viewer projection without public identity must keep the library card concealed')
assert.equal(model.isMovementCardConcealed({ ...event, type: 'put' }, sourceCard, 'library', 'field'), true, 'unclassified library-origin movement remains concealed')
assert.equal(model.isAuthoritativePublicFaceMovement({ ...event, type: 'return' }, 'graveyard', 'library'), true, 'graveyard return owns a face-up authority fact until it reaches the library')
assert.equal(model.isMovementCardConcealed({ ...event, type: 'return' }, sourceCard, 'graveyard', 'library'), false, 'public graveyard return keeps its known face during movement')
assert.equal(model.isMovementCardConcealed({ ...event, type: 'return' }, { ...sourceCard, hidden: true, identityKnown: false }, 'graveyard'), true, 'genuinely unknown identity remains concealed')
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
const relicStateBefore = model.collectVisualFieldState([{
  ...player([[null, null, null], [null, null, null]]),
  relic: card('primary-relic', 'S01-0215', false),
  extraRelics: [card('extra-relic', 'S01-0215', false)],
}])
const relicStateAfter = model.collectVisualFieldState([{
  ...player([[null, null, null], [null, null, null]]),
  relic: card('primary-relic', 'S01-0215', false),
  extraRelics: [card('extra-relic', 'S01-0215', true)],
}])
assert.deepEqual(model.changedTappedStates(relicStateBefore, relicStateAfter),
  [{ instanceId:'extra-relic', fromTapped:false, toTapped:true }],
  'extra relic state changes are collected independently from the primary relic')
const extraRelicClaims = model.createCardStateClaimState()
model.resetCardStateClaimState(extraRelicClaims, 70, relicStateBefore, 200)
assert.deepEqual(model.claimCardStateTransitions(extraRelicClaims, 71, relicStateAfter).map(change => change.transactionKey),
  ['71:extra-relic:active>rested'], 'a newer extra relic authority revision owns one state transaction')
assert.deepEqual(model.claimCardStateTransitions(extraRelicClaims, 71, relicStateBefore), [],
  'same-revision replacement cannot replay or reverse an extra relic transaction')
model.resetCardStateClaimState(extraRelicClaims, 80, relicStateAfter, 210)
assert.deepEqual(model.claimCardStateTransitions(extraRelicClaims, 80, relicStateAfter), [],
  'extra relic reconnect baseline cannot backfill historical state motion')
const knownZones = model.collectKnownCardZones([{
  ...player([[card('field-copy'), null, null], [null, null, null]]),
  hand: [card('hand-copy')], graveyard: [card('grave-copy')], resolving: [card('resolving-copy')],
}])
assert.equal(knownZones.get('hand-copy'), 'hand')
assert.equal(knownZones.get('grave-copy'), 'graveyard')
assert.equal(knownZones.get('field-copy'), 'field')
assert.equal(knownZones.get('resolving-copy'), 'resolving')

const stateClaims = model.createCardStateClaimState()
assert.deepEqual(model.claimCardStateTransitions(stateClaims, 20, before), [], 'first authoritative snapshot establishes a state baseline')
const firstRest = model.claimCardStateTransitions(stateClaims, 21, after)
assert.deepEqual(firstRest.map(change => change.transactionKey), ['21:same:active>rested'], 'a newer authoritative revision claims one active-to-rested transaction')
assert.deepEqual(model.claimCardStateTransitions(stateClaims, 21, before), [], 'same-revision object replacement cannot roll the accepted state backward')
assert.deepEqual(model.claimCardStateTransitions(stateClaims, 21, after), [], 'same-revision replay cannot claim the same rest transaction twice')
assert.deepEqual(model.claimCardStateTransitions(stateClaims, 22, before).map(change => change.transactionKey), ['22:same:rested>active'], 'a later real ready transaction remains visible')
model.resetCardStateClaimState(stateClaims, 30, after)
assert.deepEqual(model.claimCardStateTransitions(stateClaims, 30, after), [], 'reconnect baseline never backfills historical state motion')
assert.deepEqual(model.claimCardStateTransitions(stateClaims, 29, before), [], 'stale snapshot cannot rewind live authoritative state')

const attackStateClaims = model.createCardStateClaimState()
model.resetCardStateClaimState(attackStateClaims, 40, before, 100)
const attackEvent = { ...event, sequence:101, type:'attack', playerIndex:0, cards:[card('same'), targetCard] }
const [attackRest] = model.claimCardStateTransitions(attackStateClaims, 41, after, [attackEvent])
assert.equal(attackRest.transactionKey, '41:same:active>rested', 'attack rest keeps the authority revision transaction identity')
assert.equal(attackRest.attackSequence, 101, 'fresh attack event binds to the same active-to-rested transaction')
assert.equal(attackRest.attackTargetInstanceId, targetCard.instanceId, 'legion target is carried into the combined attack-rest motion')
assert.equal(attackRest.attackTargetPlayerIndex, 1, 'defending player is carried into the combined attack-rest motion')
assert.deepEqual(model.claimCardStateTransitions(attackStateClaims, 41, before, [attackEvent]), [], 'same revision cannot replay or reverse the combined attack-rest transaction')
model.resetCardStateClaimState(attackStateClaims, 50, after, 110)
assert.deepEqual(model.claimCardStateTransitions(attackStateClaims, 51, before, [{ ...attackEvent, sequence:110 }]).map(change => change.attackSequence),
  [undefined], 'a historical attack at the reconnect baseline cannot bind to a later real ready transaction')

const directAttackFactClaims = model.createCardStateClaimState()
model.resetCardStateClaimState(directAttackFactClaims, 52, before, 100)
const directAttackFact = { ...attackEvent, playerCardStateTransition:{ instanceId:'same', fromTapped:false, toTapped:true } }
const [directAttackRest] = model.claimCardStateTransitions(directAttackFactClaims, 53, after, [directAttackFact])
assert.equal(directAttackRest.transactionKey, '101:same:active>rested',
  'an attack-bound state fact uses the existing attack event identity')
assert.equal(directAttackRest.attackSequence, 101,
  'the attack event can own and decorate its own active-to-rested fact')
assert.equal(directAttackRest.presentationFactSequence, 101,
  'the direct attack fact exposes the same authority sequence to the presentation coordinator')
assert.deepEqual(model.claimCardStateTransitions(directAttackFactClaims, 53, after, [directAttackFact]), [],
  'retransmitting the attack-bound fact in the same revision cannot replay it')

const thunderAbortFactClaims = model.createCardStateClaimState()
model.resetCardStateClaimState(thunderAbortFactClaims, 54, before, 200)
const thunderAbortFact = { ...directAttackFact, sequence:201, type:'attack-ended' }
const [thunderAbortRest] = model.claimCardStateTransitions(thunderAbortFactClaims, 55, after, [thunderAbortFact])
assert.equal(thunderAbortRest.transactionKey, '201:same:active>rested',
  'a thunder abort owns one rest transaction on the actual attack-ended event')
assert.equal(thunderAbortRest.attackSequence, undefined,
  'attack-ended never invents a lunge transaction when no attack event was published')
assert.equal(thunderAbortRest.presentationFactSequence, 201,
  'the thunder abort exposes only the attack-ended authority identity')
assert.deepEqual(model.claimCardStateTransitions(thunderAbortFactClaims, 55, after, [thunderAbortFact]), [],
  'retransmitting the thunder abort fact cannot replay the rest animation')

const sameRevisionStateClaims = model.createCardStateClaimState()
model.resetCardStateClaimState(sameRevisionStateClaims, 60, before, 300)
const restFact = { ...event, sequence:301, type:'state', playerCardStateTransition:{ instanceId:'same', fromTapped:false, toTapped:true } }
const readyFact = { ...event, sequence:302, type:'state', playerCardStateTransition:{ instanceId:'same', fromTapped:true, toTapped:false } }
const sameRevisionStateChanges = model.claimCardStateTransitions(sameRevisionStateClaims, 61, before, [readyFact, restFact])
assert.deepEqual(sameRevisionStateChanges.map(change => change.transactionKey),
  ['301:same:active>rested', '302:same:rested>active'],
  'ordered explicit facts preserve two real state transitions even when the final snapshot equals the baseline')
assert.deepEqual(sameRevisionStateChanges.map(change => change.presentationFactSequence), [301, 302],
  'each state transition carries its own existing authority event identity')
assert.deepEqual(model.claimCardStateTransitions(sameRevisionStateClaims, 61, before, [restFact, readyFact]), [],
  'same-revision delivery cannot replay explicit state facts')
const orderedAttackStateClaims = model.createCardStateClaimState()
model.resetCardStateClaimState(orderedAttackStateClaims, 62, before, 300)
const laterAttack = { ...attackEvent, sequence:303 }
const laterAttackRest = { ...restFact, sequence:304 }
const orderedAttackChanges = model.claimCardStateTransitions(orderedAttackStateClaims, 63, after,
  [laterAttackRest, laterAttack, readyFact, restFact])
assert.deepEqual(orderedAttackChanges.map(change => change.transactionKey),
  ['301:same:active>rested', '302:same:rested>active', '304:same:active>rested'],
  'same-revision rest-ready-attack-rest facts preserve their exact authority order')
assert.deepEqual(orderedAttackChanges.map(change => change.attackSequence), [undefined, undefined, 303],
  'a later attack binds only its following rest fact and never retroactively turns an earlier rest into a lunge')
const staleExplicitStateClaims = model.createCardStateClaimState()
model.resetCardStateClaimState(staleExplicitStateClaims, 70, before, 400)
assert.deepEqual(model.claimCardStateTransitions(staleExplicitStateClaims, 71, after,
  [{ ...restFact, sequence:399 }]).map(change => change.transactionKey), ['71:same:active>rested'],
  'an event at the recovery watermark is ignored and the current snapshot uses the legacy fallback once')

const transactionClaims = model.createMovementClaimState()
const handZones = new Map([['entrant', 'hand'], ['second-entrant', 'hand']])
model.resetMovementClaimState(transactionClaims, 9, 40, handZones)
const fieldZones = new Map([['entrant', 'field'], ['second-entrant', 'field']])
const playFact = { key:'10:0:entrant:hand>field', sequence:10, cardIndex:0, instanceId:'entrant', from:'hand', to:'field' }
const enterFact = { key:'11:0:entrant:hand>field', sequence:11, cardIndex:0, instanceId:'entrant', from:'hand', to:'field' }
const secondPlay = { key:'12:0:second-entrant:hand>field', sequence:12, cardIndex:0, instanceId:'second-entrant', from:'hand', to:'field' }
assert.equal(model.movementTransactionKey(41, playFact), '41:entrant:hand>field', 'movement transaction identity is revision, instance and normalized zones rather than event source')
assert.deepEqual(model.claimMovementTransactions(transactionClaims, 41, fieldZones, [playFact, enterFact, secondPlay]).map(fact => fact.key),
  [playFact.key, secondPlay.key], 'different event sources describing one authoritative entry are collapsed while different instances remain independent')
assert.deepEqual(model.claimMovementTransactions(transactionClaims, 42, fieldZones, [enterFact]), [], 'later snapshots cannot replay an already settled cross-zone migration')
const graveZones = new Map([['entrant', 'graveyard'], ['second-entrant', 'field']])
const leaveFact = { key:'13:0:entrant:field>graveyard', sequence:13, cardIndex:0, instanceId:'entrant', from:'field', to:'graveyard' }
assert.deepEqual(model.claimMovementTransactions(transactionClaims, 43, graveZones, [leaveFact]).map(fact => fact.key), [leaveFact.key], 'a real later departure of the same instance remains visible')
const reenterFact = { key:'14:0:entrant:graveyard>field', sequence:14, cardIndex:0, instanceId:'entrant', from:'graveyard', to:'field' }
assert.deepEqual(model.claimMovementTransactions(transactionClaims, 44, fieldZones, [reenterFact]).map(fact => fact.key), [reenterFact.key], 'the same instance may enter again after an authoritative departure')
const duplicateEnterAtDestination = { key:'15:0:entrant:field>field', sequence:15, cardIndex:0, instanceId:'entrant', from:'field', to:'field' }
assert.deepEqual(model.claimMovementTransactions(transactionClaims, 45, fieldZones, [duplicateEnterAtDestination]), [], 'a later enter description cannot masquerade as a same-zone move after the card has settled')
const firstMove = { key:'16:0:entrant:field>field', sequence:16, cardIndex:0, instanceId:'entrant', from:'field', to:'field', allowSameZone:true }
const secondMove = { key:'17:0:entrant:field>field', sequence:17, cardIndex:0, instanceId:'entrant', from:'field', to:'field', allowSameZone:true }
assert.deepEqual(model.claimMovementTransactions(transactionClaims, 46, fieldZones, [firstMove, secondMove]).map(fact => fact.key),
  [firstMove.key, secondMove.key], 'same-zone movements retain their distinct authoritative event identities')
model.resetMovementClaimState(transactionClaims, 17, 50, fieldZones)
assert.deepEqual(model.claimMovementTransactions(transactionClaims, 50, fieldZones, [playFact]), [], 'reconnect baseline does not backfill a historical entry')

const sameRevisionClaims = model.createMovementClaimState()
model.resetMovementClaimState(sameRevisionClaims, 20, 60, new Map([['chain-unit', 'field']]))
const sameRevisionBatch = model.beginMovementTransactionBatch(sameRevisionClaims, 61, new Map([['chain-unit', 'field']]))
assert.ok(sameRevisionBatch, 'a newer snapshot opens one ordered movement transaction batch')
const sameRevisionLeave = { key:'21:0:chain-unit:field>graveyard', sequence:21, cardIndex:0, instanceId:'chain-unit', from:'field', to:'graveyard' }
assert.equal(model.claimMovementTransaction(sameRevisionClaims, sameRevisionBatch, sameRevisionLeave), true, 'same-revision chain claims field-to-grave first')
assert.equal(sameRevisionBatch.cursor.get('chain-unit'), 'graveyard', 'accepted departure advances the per-instance batch cursor')
const sameRevisionReenter = { key:'22:0:chain-unit:graveyard>field', sequence:22, cardIndex:0, instanceId:'chain-unit', from:sameRevisionBatch.cursor.get('chain-unit'), to:'field' }
assert.equal(model.claimMovementTransaction(sameRevisionClaims, sameRevisionBatch, sameRevisionReenter), true, 'same-revision re-entry reads the accepted departure cursor')
assert.equal(sameRevisionBatch.cursor.get('chain-unit'), 'field', 'accepted re-entry advances the cursor back to field')

const duplicateDescriptionClaims = model.createMovementClaimState()
model.resetMovementClaimState(duplicateDescriptionClaims, 30, 70, new Map([['described-unit', 'hand']]))
const duplicateDescriptionBatch = model.beginMovementTransactionBatch(duplicateDescriptionClaims, 71, new Map([['described-unit', 'field']]))
assert.ok(duplicateDescriptionBatch, 'play snapshot opens a movement batch')
const describedPlay = { key:'31:0:described-unit:hand>field', sequence:31, cardIndex:0, instanceId:'described-unit', from:'hand', to:'field' }
assert.equal(model.claimMovementTransaction(duplicateDescriptionClaims, duplicateDescriptionBatch, describedPlay), true, 'play owns the hand-to-field migration')
const describedEnter = { key:'32:0:described-unit:field>field', sequence:32, cardIndex:0, instanceId:'described-unit', from:duplicateDescriptionBatch.cursor.get('described-unit'), to:'field' }
assert.equal(model.claimMovementTransaction(duplicateDescriptionClaims, duplicateDescriptionBatch, describedEnter), false, 'enter description at the accepted destination does not advance the cursor twice')

const interleavedClaims = model.createMovementClaimState()
model.resetMovementClaimState(interleavedClaims, 40, 80, new Map([['returning-unit', 'field'], ['other-unit', 'hand']]))
const interleavedBatch = model.beginMovementTransactionBatch(interleavedClaims, 81, new Map([['returning-unit', 'field'], ['other-unit', 'field']]))
assert.ok(interleavedBatch, 'interleaved instances share a batch without sharing cursors')
assert.equal(model.claimMovementTransaction(interleavedClaims, interleavedBatch,
  { key:'41:0:returning-unit:field>graveyard', sequence:41, cardIndex:0, instanceId:'returning-unit', from:'field', to:'graveyard' }), true)
assert.equal(model.claimMovementTransaction(interleavedClaims, interleavedBatch,
  { key:'42:0:other-unit:hand>field', sequence:42, cardIndex:0, instanceId:'other-unit', from:'hand', to:'field' }), true)
assert.equal(model.claimMovementTransaction(interleavedClaims, interleavedBatch,
  { key:'43:0:returning-unit:graveyard>field', sequence:43, cardIndex:0, instanceId:'returning-unit', from:interleavedBatch.cursor.get('returning-unit'), to:'field' }), true)
assert.deepEqual([...interleavedBatch.cursor.entries()].sort(), [['other-unit', 'field'], ['returning-unit', 'field']], 'different instance cursors remain isolated while event order is preserved')
model.finalizeMovementTransactionBatch(interleavedClaims, interleavedBatch, new Map([['returning-unit', 'field'], ['other-unit', 'field']]))
assert.equal(interleavedClaims.zoneRevision, 81, 'finalization commits the batch revision only after ordered claims finish')

console.log('Battle visual transition projection passed: 92/92 assertions')
