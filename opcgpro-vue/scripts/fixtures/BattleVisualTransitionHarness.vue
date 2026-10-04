<script setup lang="ts">
import { reactive, ref } from 'vue'
import type { ActionEvent, Card, GameState, PlayerView, Prompt } from '../../src/l12/types'
import GameBoard from '../../src/l12/game/GameBoard.vue'
import { l12State } from '../../src/l12/net'
import { useLandscapeViewport } from '../../src/l12/mobileViewport'
import { authoritativeCardVisibilityLeaseCount } from '../../src/l12/game/authoritativeCardVisibility'

const faceUrl = (cardId: string) => `/api/site/media/visual-transition/${cardId}.png`
const harnessParams = new URLSearchParams(window.location.search)
const initialViewer = Number(harnessParams.get('viewer') || 0)
const landscapeEnabled = ref(harnessParams.get('landscape') === '1')
useLandscapeViewport(landscapeEnabled)
const card = (instanceId: string, name: string, cardId: string, tapped = false, imageUrl?: string): Card => ({
  instanceId, name, cardId, cardType: 'legion', faction: 'otherworld', cost: 1,
  baseTroops: 2000, troops: 2000, disasterLevel: 0, tapped, summonRound: 0, imageUrl,
})
const mover = card('mover-1', '侍从骑士', 'S02-0609')
const swapper = card('mover-2', '罗宾汉', 'S02-0617')
const host = card('host-1', '狮心王理查一世', 'S02-0608')
const handSquire = card('hand-squire', '侍从骑士', 'S02-0609')
const robinSquire = card('robin-squire', '侍从骑士', 'S02-0609')
const topSingleCard = card('top-single', '顶部单张', 'S01-01M1', false, faceUrl('S01-01M1'))
const topManyCards = [
  card('top-card-1', '顶部一', 'S01-01M2', false, faceUrl('S01-01M2')),
  card('top-card-2', '顶部二', 'S01-02M1', false, faceUrl('S01-02M1')),
  card('top-card-3', '顶部三', 'S01-02M3', false, faceUrl('S01-02M3')),
]
const millCards = [
  card('mill-card-1', '磨牌一', 'S01-03M1', false, faceUrl('S01-03M1')),
  card('mill-card-2', '磨牌二', 'S01-03M2', false, faceUrl('S01-03M2')),
  card('mill-card-3', '磨牌三', 'S01-04M1', false, faceUrl('S01-04M1')),
]
const duplicateCard = card('duplicate-discard', '佣兵部队', 'S01-04M2', false, faceUrl('S01-04M2'))
const pharaohFestival = { ...card('pharaoh-festival', '法老王的庆典', 'S01-0222', false, faceUrl('S01-0222')), cardType: 'tactic' }
const festivalHandCard = card('festival-hand-card', '陵墓守卫', 'S01-0212', false, faceUrl('S01-0212'))
const festivalGraveCard = card('festival-grave-card', '卡诺匹斯罐 一', 'S01-0208', false, faceUrl('S01-0208'))
const finn = card('finn-optional-ready', '芬恩', 'S02-0610', true)
const nuada = { ...card('nuada-source', '银臂努阿达', 'ST06-M1'), cardType: 'master' }
const restedDefender = card('rested-defender', '休整守军', 'S01-01M1', true, faceUrl('S01-01M1'))
const activeDefender = card('active-defender', '活跃守军', 'S01-01M2', false, faceUrl('S01-01M2'))
const plainEntrant = card('plain-entrant', '普通登场军团', 'S01-02M1', false, faceUrl('S01-02M1'))
// Mirrors the server's ordinary public serialization defaults. identityKnown
// is only meaningful for covered cards and remains false on public event cards.
const triggeredEntrant = { ...card('triggered-entrant', '加拉哈德', 'S02-0604', false, faceUrl('S02-0604')), hidden:false, identityKnown:false }
const angus = { ...card('angus-source', '安格斯', 'S02-06M2', false, faceUrl('S02-06M2')), cardType: 'master' }
const negatedEntrant = card('negated-entrant', '效果被无效军团', 'S01-03M1', false, faceUrl('S01-03M1'))
const freeEntrant = card('free-entrant', '免费登场军团', 'S01-03M2', false, faceUrl('S01-03M2'))
const rapidEntrantA = card('rapid-entrant-a', '连续登场甲', 'S01-04M1', false, faceUrl('S01-04M1'))
const rapidEntrantB = card('rapid-entrant-b', '连续登场乙', 'S01-04M2', false, faceUrl('S01-04M2'))
const interleavedEntrant = card('interleaved-entrant', '交错登场军团', 'S01-01M2', false, faceUrl('S01-01M2'))
const entryArtifact = { ...card('entry-artifact', '登场圣物', 'S01-01M1', false, faceUrl('S01-01M1')), cardType:'artifact' }
const graveyardEntrant = card('graveyard-entrant', '墓地置入军团', 'S01-01M2', false, faceUrl('S01-01M2'))
const libraryEntrant = card('library-entrant', '牌库置入军团', 'S01-02M1', false, faceUrl('S01-02M1'))
const missingTypeSource = { ...card('missing-type-source', '缺类型来源', 'S01-02M3', false, faceUrl('S01-02M3')), cardType:'' }
const masterPlaySource = { ...card('master-play-source', '主宰来源', 'S01-03M1', false, faceUrl('S01-03M1')), cardType:'master' }
const unknownOriginEntrant = card('unknown-origin-entrant', '未知来源置入军团', 'S01-03M2', false, faceUrl('S01-03M2'))

function player(playerIndex: number): PlayerView {
  return {
    playerIndex, name: playerIndex ? '玩家B' : '玩家A', deckName: '', faction: 'otherworld',
    master: { masterId: playerIndex ? 'S02-06M1' : 'ST06-M1', masterName: playerIndex ? '莫瑞甘' : '银臂努阿达', hp: 8, maxHp: 8 },
    libraryCount: playerIndex ? 20 : 23, libraryTop: playerIndex ? libraryEntrant : null,
    hand: playerIndex ? [plainEntrant, triggeredEntrant, negatedEntrant, freeEntrant, rapidEntrantA, rapidEntrantB, entryArtifact, missingTypeSource, masterPlaySource] : [handSquire, robinSquire, duplicateCard, pharaohFestival],
    handCount: playerIndex ? 9 : 4,
    morale: [], field: playerIndex
      ? [[restedDefender, activeDefender, null], [null, null, null]]
      : [[mover, swapper, host], [finn, null, null]],
    graveyard: playerIndex ? [graveyardEntrant] : [], graveyardCount: playerIndex ? 1 : 0, resolving: [],
    specialZones: { runes: 2, trialLevel: 0, godPower: [], trials: [] }, mulliganDone: true,
  }
}

const game = reactive<GameState>({
  matchId: 'visual-transition-harness', roomCode: 'HARNESS', you: initialViewer, revision: 1,
  activePlayer: 0, firstPlayer: 0, diceWinner: 0, initiativeRolls: [4, 2], phase: 'Main', round: 2, turnSerial: 2,
  disasterMode: 'none', disasterValue: 0, prompts: [], effectStack: [], players: [player(0), player(1)],
  recentEvents: [], legalAttackTargets: {}, stateHash: 'visual-1',
})
if (harnessParams.has('private-hand')) {
  // Production opponent/spectator projection exposes only handCount. Keep the
  // count while removing every private instance before the board mounts.
  game.players[1].hand = []
}
const playbackSpeed = ref<number | null>(null)
let sequence = 0
const authorityEntryMoves = new Map<string, number>()
function publish(event: Omit<ActionEvent, 'sequence'>) {
  game.revision += 1
  game.stateHash = `visual-${game.revision}`
  game.recentEvents = [...(game.recentEvents ?? []), { ...event, sequence: ++sequence }]
}
function prompt(id: string, data: Record<string, string> = {}): Prompt {
  return { promptId: id, playerIndex: 0, kind: 'optional-card', text: '测试阻塞弹框', validChoices: ['yes'], minChoose: 1, maxChoose: 1, data, choiceLabels: { yes: '确认' } }
}
function replaceField(next: Array<Array<Card | null>>) { game.players[0].field = next; game.revision += 1 }
function mill(cards: Card[], text: string) {
  game.players[0].libraryCount = Math.max(0, game.players[0].libraryCount - cards.length)
  game.players[0].graveyard = [...(game.players[0].graveyard ?? []), ...cards]
  game.players[0].graveyardCount = game.players[0].graveyard.length
  publish({ type:'mill', playerIndex:0, text, cards })
}
function returnFromGrave(cards: Card[], placement: '顶部' | '底部') {
  const ids = new Set(cards.map(card => card.instanceId))
  game.players[0].graveyard = (game.players[0].graveyard ?? []).filter(card => !ids.has(card.instanceId))
  game.players[0].graveyardCount = game.players[0].graveyard.length
  game.players[0].libraryCount += cards.length
  const events = cards.map(card => ({
    sequence:++sequence, type:'return', playerIndex:0,
    text:`〈${card.name}〉从墓地返回牌库${placement}`, cards:[card],
  }))
  game.revision += 1
  game.stateHash = `visual-${game.revision}`
  game.recentEvents = [...(game.recentEvents ?? []), ...events]
}
function publishBatch(events: Array<Omit<ActionEvent, 'sequence'>>) {
  game.revision += 1
  game.stateHash = `visual-${game.revision}`
  game.recentEvents = [...(game.recentEvents ?? []), ...events.map(event => ({ ...event, sequence:++sequence }))]
}
function placeOpponent(card: Card, row: number, slot: number, events: Array<Omit<ActionEvent, 'sequence'>>) {
  const projectedPrivateHand = (game.players[1].hand?.length ?? 0) === 0 && (game.players[1].handCount ?? 0) > 0
  game.players[1].hand = game.players[1].hand?.filter(item => item.instanceId !== card.instanceId)
  game.players[1].handCount = projectedPrivateHand
    ? Math.max(0, (game.players[1].handCount ?? 0) - 1)
    : game.players[1].hand?.length
  game.players[1].field[row][slot] = card
  authorityEntryMoves.set(card.instanceId, (authorityEntryMoves.get(card.instanceId) ?? 0) + 1)
  publishBatch(events)
}

const api = {
  tap() { const current = game.players[0].field.flat().find(item => item?.instanceId === mover.instanceId); if (current) current.tapped = true; game.revision += 1 },
  ready() { const current = game.players[0].field.flat().find(item => item?.instanceId === mover.instanceId); if (current) current.tapped = false; game.revision += 1 },
  attackWithDuplicateRestSnapshots() {
    const current = game.players[0].field.flat().find(item => item?.instanceId === mover.instanceId)
    if (!current) return
    current.tapped = true
    game.revision += 1
    const attackRevision = game.revision
    game.recentEvents = [...(game.recentEvents ?? []), { sequence:++sequence, type:'attack', playerIndex:0, text:'侍从骑士发动进攻', cards:[current, restedDefender] }]
    queueMicrotask(() => {
      current.tapped = false
      game.revision = attackRevision
      queueMicrotask(() => {
        current.tapped = true
        game.revision = attackRevision
      })
    })
  },
  attackRestedTarget() { swapper.tapped = true; publishBatch([{ type:'attack', playerIndex:0, text:'攻击休整守军', cards:[swapper, restedDefender] }]) },
  attackActiveTarget() { swapper.tapped = true; publishBatch([{ type:'attack', playerIndex:0, text:'攻击活跃守军', cards:[swapper, activeDefender] }]) },
  beginAttackRestHandoff() {
    const current = game.players[0].field.flat().find(item => item?.instanceId === mover.instanceId)
    if (!current) return
    current.tapped = true
    publish({
      type:'attack', playerIndex:0, text:'侍从骑士进攻并立即休整', cards:[current, activeDefender],
      playerCardStateTransition:{ instanceId:current.instanceId, fromTapped:false, toTapped:true },
    })
  },
  returnDuringAttackRest() {
    const current = game.players[0].field.flat().find(item => item?.instanceId === mover.instanceId)
    if (!current) return
    replaceField(game.players[0].field.map(row => row.map(item => item?.instanceId === current.instanceId ? null : item)))
    game.players[0].hand = [...(game.players[0].hand ?? []), current]
    game.players[0].handCount = game.players[0].hand.length
    publish({ type:'return', playerIndex:0, text:'侍从骑士在进攻结算后返回手牌', cards:[current] })
  },
  reconnectWithHistoricalReady() {
    l12State.status = 'connecting'
    l12State.recoveryPhase = 'snapshot-received'
    const current = game.players[0].field.flat().find(item => item?.instanceId === mover.instanceId)
    if (current) current.tapped = false
    game.revision += 10
    queueMicrotask(() => {
      l12State.status = 'online'
      l12State.recoveryPhase = 'snapshot-acknowledged'
    })
  },
  reconnectWithHistoricalAttack() {
    l12State.status = 'connecting'
    l12State.recoveryPhase = 'snapshot-received'
    const current = game.players[0].field.flat().find(item => item?.instanceId === mover.instanceId)
    if (current) current.tapped = true
    game.revision += 10
    game.recentEvents = [...(game.recentEvents ?? []), { sequence:++sequence, type:'attack', playerIndex:0, text:'重连快照中的历史进攻', cards: current ? [current, activeDefender] : [] }]
    queueMicrotask(() => {
      l12State.status = 'online'
      l12State.recoveryPhase = 'snapshot-acknowledged'
    })
  },
  replaySeekBackwardWithHistoricalAttack() {
    const priorRevision = game.revision
    const current = game.players[0].field.flat().find(item => item?.instanceId === mover.instanceId)
    if (current) current.tapped = true
    game.revision = Math.max(1, priorRevision - 5)
    game.recentEvents = [...(game.recentEvents ?? []), { sequence:++sequence, type:'attack', playerIndex:0, text:'回放后退帧中的历史进攻', cards: current ? [current, activeDefender] : [] }]
    queueMicrotask(() => { game.revision = priorRevision + 1 })
  },
  playPlainEntrant() {
    placeOpponent(plainEntrant, 0, 2, [{ type:'play', playerIndex:1, text:'玩家B打出普通登场军团', cards:[plainEntrant] }])
  },
  sameRevisionLeaveAndReenter() {
    publishBatch([
      { type:'leave', playerIndex:1, text:'普通登场军团因效果临时离场', cards:[plainEntrant] },
      { type:'enter', playerIndex:1, text:'普通登场军团从墓地再次登场', cards:[plainEntrant] },
    ])
  },
  leavePlainEntrant() {
    game.players[1].field = game.players[1].field.map(row => row.map(item => item?.instanceId === plainEntrant.instanceId ? null : item))
    game.players[1].graveyard = [...(game.players[1].graveyard ?? []), plainEntrant]
    game.players[1].graveyardCount = game.players[1].graveyard.length
    publishBatch([{ type:'leave', playerIndex:1, text:'普通登场军团离场进入墓地', cards:[plainEntrant] }])
  },
  reenterPlainEntrant() {
    game.players[1].graveyard = (game.players[1].graveyard ?? []).filter(item => item.instanceId !== plainEntrant.instanceId)
    game.players[1].graveyardCount = game.players[1].graveyard.length
    game.players[1].field[0][2] = plainEntrant
    publishBatch([{ type:'enter', playerIndex:1, text:'普通登场军团从墓地再次登场', cards:[plainEntrant] }])
  },
  interleavedDifferentInstanceChain() {
    game.players[0].hand = [...(game.players[0].hand ?? []), interleavedEntrant]
    game.players[0].handCount = game.players[0].hand.length
    game.revision += 1
    queueMicrotask(() => {
      game.players[0].hand = game.players[0].hand?.filter(item => item.instanceId !== interleavedEntrant.instanceId)
      game.players[0].handCount = game.players[0].hand?.length
      game.players[0].field[1][1] = interleavedEntrant
      publishBatch([
        { type:'leave', playerIndex:1, text:'普通登场军团因效果临时离场', cards:[plainEntrant] },
        { type:'play', playerIndex:0, text:'玩家A打出交错登场军团', cards:[interleavedEntrant] },
        { type:'enter', playerIndex:1, text:'普通登场军团从墓地再次登场', cards:[plainEntrant] },
      ])
    })
  },
  playTriggeredEntrant() {
    const playerLogGroupId = 'play:triggered-entrant'
    placeOpponent(triggeredEntrant, 1, 0, [
      { type:'play', playerIndex:1, text:'玩家B打出加拉哈德', playerLogGroupId, playerLogTiming:'enter', cards:[triggeredEntrant] },
      { type:'enter', playerIndex:1, text:'加拉哈德登场并触发效果', playerLogGroupId, playerLogTiming:'enter', cards:[triggeredEntrant] },
      { type:'effect-trigger', playerIndex:1, text:'加拉哈德的登场时效果入栈', effectSceneId:'entry-trigger', effectResultStatus:'resolved', playerLogGroupId, playerLogTiming:'enter', cards:[triggeredEntrant] },
    ])
  },
  playGalahadTrialRuneChain() {
    const playerLogGroupId = 'play:galahad-trial-rune'
    placeOpponent(triggeredEntrant, 1, 0, [
      { type:'play', playerIndex:1, text:'玩家B打出加拉哈德', playerLogGroupId, playerLogTiming:'enter', cards:[triggeredEntrant] },
      { type:'effect-trigger', playerIndex:1, text:'加拉哈德的登场时效果入栈', effectSceneId:'galahad-entry-trial', effectResultStatus:'declared', playerLogGroupId, playerLogTiming:'enter', cards:[triggeredEntrant] },
    ])
    // The real chain prepays Galahad's rest while its hand-to-field flight is
    // still visible, then publishes the trial result and Angus rune receipt.
    setTimeout(() => {
      triggeredEntrant.tapped = true
      game.players[1].specialZones.runes += 1
      publishBatch([
        { type:'state', playerIndex:1, text:'加拉哈德转为休整', cards:[triggeredEntrant],
          playerCardStateTransition:{ instanceId:triggeredEntrant.instanceId, fromTapped:false, toTapped:true } },
        { type:'effect-result', playerIndex:1, text:'加拉哈德发动试炼并进入休整', effectText:'进行试炼。', effectSceneId:'galahad-entry-trial', effectResultStatus:'resolved', playerLogGroupId, playerLogTiming:'enter', cards:[triggeredEntrant] },
        { type:'effect-result', playerIndex:1, text:'安格斯因推进试炼获得1符文', effectText:'获得1符文。', effectSceneId:'angus-trial-rune', effectResultStatus:'resolved', playerLogGroupId:'effect:angus-trial-rune', playerLogTiming:'trial', cards:[angus] },
      ])
    }, 120)
  },
  repeatTriggeredEnterSnapshot() {
    publishBatch([
      { type:'enter', playerIndex:1, text:'带登场效果军团登场并触发效果', cards:[triggeredEntrant] },
      { type:'effect-response', playerIndex:1, text:'响应后重新发布登场对象', cards:[triggeredEntrant] },
    ])
  },
  playNegatedEntrant() {
    placeOpponent(negatedEntrant, 1, 1, [
      { type:'play', playerIndex:1, text:'玩家B打出效果被无效军团', cards:[negatedEntrant] },
      { type:'enter', playerIndex:1, text:'效果被无效军团登场', cards:[negatedEntrant] },
      { type:'effect-result', playerIndex:1, text:'登场时效果被无效', effectSceneId:'entry-negated', effectResultStatus:'negated', cards:[negatedEntrant] },
    ])
  },
  freeTriggeredEntrant() {
    const playerLogGroupId = 'play:free-triggered-entrant'
    game.prompts = [prompt('free-entry-source', { 'free-entrant:zone':'手牌' })]
    placeOpponent(freeEntrant, 1, 2, [
      { type:'put', playerIndex:1, text:'效果使免费登场军团从手牌免费登场', playerLogGroupId, playerLogTiming:'enter', cards:[freeEntrant] },
      { type:'enter', playerIndex:1, text:'免费登场军团登场并触发效果', playerLogGroupId, playerLogTiming:'enter', cards:[freeEntrant] },
      { type:'effect-result', playerIndex:1, text:'免费登场军团放弃发动可选登场效果', effectText:'不发动可选登场效果。', effectSceneId:'free-entry-declined', effectResultStatus:'declined', playerLogGroupId, playerLogTiming:'enter', cards:[freeEntrant] },
    ])
    game.prompts = []
  },
  rapidDifferentEntrants() {
    game.players[0].hand = [...(game.players[0].hand ?? []), rapidEntrantA, rapidEntrantB]
    game.players[0].handCount = game.players[0].hand.length
    game.revision += 1
    queueMicrotask(() => {
      game.players[0].hand = game.players[0].hand?.filter(item => item.instanceId !== rapidEntrantA.instanceId && item.instanceId !== rapidEntrantB.instanceId)
      game.players[0].handCount = game.players[0].hand?.length
      game.players[0].field[1][1] = rapidEntrantA
      game.players[0].field[1][2] = rapidEntrantB
      publishBatch([
        { type:'play', playerIndex:0, text:'玩家A连续打出军团甲', cards:[rapidEntrantA] },
        { type:'play', playerIndex:0, text:'玩家A连续打出军团乙', cards:[rapidEntrantB] },
      ])
    })
  },
  playEntryArtifact() {
    const playerLogGroupId = 'play:entry-artifact'
    game.players[1].hand = game.players[1].hand?.filter(item => item.instanceId !== entryArtifact.instanceId)
    game.players[1].handCount = game.players[1].hand?.length
    game.players[1].relic = entryArtifact
    publishBatch([
      { type:'play', playerIndex:1, text:'玩家B打出登场圣物', playerLogGroupId, playerLogTiming:'enter', cards:[entryArtifact] },
      { type:'effect-result', playerIndex:1, text:'登场圣物的登场效果结算', effectText:'圣物效果正常展示。', effectSceneId:'artifact-entry-effect', effectResultStatus:'resolved', playerLogGroupId, playerLogTiming:'enter', cards:[entryArtifact] },
    ])
  },
  putGraveyardEntrant() {
    const playerLogGroupId = 'play:graveyard-entrant'
    game.players[1].graveyard = (game.players[1].graveyard ?? []).filter(item => item.instanceId !== graveyardEntrant.instanceId)
    game.players[1].graveyardCount = game.players[1].graveyard.length
    game.players[1].field[0][2] = graveyardEntrant
    publishBatch([
      { type:'put', playerIndex:1, text:'墓地置入军团从墓地登场', playerLogGroupId, playerLogTiming:'enter', cards:[graveyardEntrant] },
      { type:'effect-result', playerIndex:1, text:'墓地置入军团的登场效果结算', effectText:'墓地来源效果正常展示。', effectSceneId:'graveyard-entry-effect', effectResultStatus:'resolved', playerLogGroupId, playerLogTiming:'enter', cards:[graveyardEntrant] },
    ])
  },
  putLibraryEntrant() {
    const playerLogGroupId = 'play:library-entrant'
    game.players[1].libraryTop = null
    game.players[1].libraryCount = Math.max(0, game.players[1].libraryCount - 1)
    game.players[1].field[1][0] = libraryEntrant
    publishBatch([
      { type:'put', playerIndex:1, text:'牌库置入军团从牌库登场', playerLogGroupId, playerLogTiming:'enter', cards:[libraryEntrant] },
      { type:'effect-result', playerIndex:1, text:'牌库置入军团的登场效果结算', effectText:'牌库来源效果正常展示。', effectSceneId:'library-entry-effect', effectResultStatus:'resolved', playerLogGroupId, playerLogTiming:'enter', cards:[libraryEntrant] },
    ])
  },
  putUnknownOriginEntrant() {
    const playerLogGroupId = 'play:unknown-origin-entrant'
    // This instance was absent from every prior authority snapshot and prompt.
    // The movement may use resolving as a layout fallback, but that fallback
    // must not authorize suppression of the effect's full-card presentation.
    game.players[1].field[1][1] = unknownOriginEntrant
    publishBatch([
      { type:'put', playerIndex:1, text:'未知来源置入军团登场', playerLogGroupId, playerLogTiming:'enter', cards:[unknownOriginEntrant] },
      { type:'effect-result', playerIndex:1, text:'未知来源置入军团的登场效果结算', effectText:'未知来源保留整卡展示。', effectSceneId:'unknown-origin-entry-effect', effectResultStatus:'resolved', playerLogGroupId, playerLogTiming:'enter', cards:[unknownOriginEntrant] },
    ])
  },
  playMissingTypeSource() {
    const playerLogGroupId = 'play:missing-type-source'
    game.players[1].hand = game.players[1].hand?.filter(item => item.instanceId !== missingTypeSource.instanceId)
    game.players[1].handCount = game.players[1].hand?.length
    game.players[1].resolving = [...(game.players[1].resolving ?? []), missingTypeSource]
    publishBatch([
      { type:'play', playerIndex:1, text:'玩家B打出缺类型来源', playerLogGroupId, playerLogTiming:'enter', cards:[missingTypeSource] },
      { type:'effect-result', playerIndex:1, text:'缺类型来源的效果结算', effectText:'缺类型来源保留整卡展示。', effectSceneId:'missing-type-entry-effect', effectResultStatus:'resolved', playerLogGroupId, playerLogTiming:'enter', cards:[missingTypeSource] },
    ])
  },
  playMasterSource() {
    const playerLogGroupId = 'play:master-source'
    game.players[1].hand = game.players[1].hand?.filter(item => item.instanceId !== masterPlaySource.instanceId)
    game.players[1].handCount = game.players[1].hand?.length
    game.players[1].resolving = [...(game.players[1].resolving ?? []), masterPlaySource]
    publishBatch([
      { type:'play', playerIndex:1, text:'玩家B打出主宰来源', playerLogGroupId, playerLogTiming:'enter', cards:[masterPlaySource] },
      { type:'effect-result', playerIndex:1, text:'主宰来源的效果结算', effectText:'主宰来源保留整卡展示。', effectSceneId:'master-entry-effect', effectResultStatus:'resolved', playerLogGroupId, playerLogTiming:'enter', cards:[masterPlaySource] },
    ])
  },
  move() {
    replaceField([[null, mover, host], [null, swapper, null]])
    publish({ type: 'move', playerIndex: 0, text: '侍从骑士移动至相邻阵地', cards: [mover] })
  },
  swap() {
    replaceField([[swapper, mover, host], [null, null, null]])
    publish({ type: 'move', playerIndex: 0, text: '罗宾汉与侍从骑士互换阵地', cards: [mover, swapper] })
  },
  attach() {
    game.players[0].hand = game.players[0].hand?.filter(item => item.instanceId !== handSquire.instanceId)
    game.players[0].handCount = game.players[0].hand?.length
    const currentHost = game.players[0].field.flat().find(item => item?.instanceId === host.instanceId)!
    currentHost.attachedCards = [...(currentHost.attachedCards ?? []), handSquire]
    publish({ type: 'attach', playerIndex: 0, text: '〈侍从骑士〉叠放至〈狮心王理查一世〉下方', cards: [currentHost, handSquire] })
  },
  prepareRobin() { game.prompts = [prompt('robin-prompt', { 'robin-squire:zone': '手牌' })] },
  robin() {
    game.prompts = []
    game.players[0].hand = game.players[0].hand?.filter(item => item.instanceId !== robinSquire.instanceId)
    game.players[0].handCount = game.players[0].hand?.length
    replaceField([[swapper, mover, host], [robinSquire, null, null]])
    publish({ type: 'put', playerIndex: 0, text: '侍从骑士活跃登场', cards: [robinSquire] })
  },
  triggerNuada() {
    publish({ type: 'effect-trigger', playerIndex: 0, text: '银臂努阿达因我方消耗符文触发', effectText: '选择我方1张【彼界】军团，本回合兵力+1000。', effectSceneId: 'nuada-rune-buff', effectResultStatus: 'resolved', cards: [nuada, mover] })
  },
  effectThenMove() {
    const source = game.players[0].field.flat().find(item => item?.instanceId === swapper.instanceId) ?? swapper
    const first = ++sequence
    const second = ++sequence
    replaceField([[source, null, host], [null, mover, null]])
    game.recentEvents = [...(game.recentEvents ?? []),
      { sequence:first, type:'effect-trigger', playerIndex:0, text:'银臂努阿达先触发', effectText:'先展示效果来源。', effectSceneId:'ordered-source', effectResultStatus:'resolved', cards:[nuada] },
      { sequence:second, type:'move', playerIndex:0, text:'罗宾汉随后移动', cards:[source] },
    ]
  },
  costLeave() {
    const leaving = game.players[0].field.flat().find(item => item?.instanceId === swapper.instanceId) ?? swapper
    replaceField(game.players[0].field.map(row => row.map(item => item?.instanceId === leaving.instanceId ? null : item)))
    game.players[0].graveyard = [...(game.players[0].graveyard ?? []), leaving]
    game.players[0].graveyardCount = game.players[0].graveyard.length
    publish({ type:'leave', playerIndex:0, text:'加拉哈德作为主动效果的费用被弃置', cards:[leaving] })
  },
  millOneForTop() { mill([topSingleCard], '动画契约弃置牌库顶部1张牌') },
  returnOneTop() { returnFromGrave([topSingleCard], '顶部') },
  millManyForTop() { mill(topManyCards, '动画契约弃置牌库顶部3张牌后逐张回顶') },
  returnManyTop() { returnFromGrave(topManyCards, '顶部') },
  millMany() { mill(millCards, '动画契约弃置牌库顶部3张牌后逐张回底') },
  returnMilled() { returnFromGrave(millCards, '底部') },
  duplicateDiscardSnapshots() {
    game.players[0].hand = game.players[0].hand?.filter(card => card.instanceId !== duplicateCard.instanceId)
    game.players[0].handCount = game.players[0].hand?.length
    game.players[0].graveyard = [...(game.players[0].graveyard ?? []), duplicateCard]
    game.players[0].graveyardCount = game.players[0].graveyard.length
    game.revision += 1
    game.stateHash = `visual-${game.revision}`
    const discard = { sequence:++sequence, type:'discard', playerIndex:0, text:'玩家A弃置佣兵部队', cards:[duplicateCard] }
    game.recentEvents = [...(game.recentEvents ?? []), discard]
    queueMicrotask(() => {
      game.recentEvents = [...(game.recentEvents ?? []),
        { sequence:++sequence, type:'response', playerIndex:0, text:'发动佣兵部队抵挡进攻', cards:[duplicateCard] },
        { sequence:++sequence, type:'effect-response', playerIndex:0, text:'佣兵部队响应结算', cards:[duplicateCard] },
      ]
    })
  },
  pharaohFestivalChain() {
    game.prompts = [prompt('festival-source-zones', {
      'pharaoh-festival:zone': '手牌',
      'festival-hand-card:zone': '牌库',
      'festival-grave-card:zone': '牌库',
    })]
    game.players[0].hand = game.players[0].hand?.filter(card => card.instanceId !== pharaohFestival.instanceId)
    game.players[0].handCount = game.players[0].hand?.length
    game.players[0].resolving = [...(game.players[0].resolving ?? []), pharaohFestival]
    publish({ type:'play', playerIndex:0, text:'玩家A 打出 法老王的庆典', cards:[pharaohFestival] })
    queueMicrotask(() => {
      game.prompts = []
      game.players[0].hand = [...(game.players[0].hand ?? []), festivalHandCard]
      game.players[0].handCount = game.players[0].hand.length
      publish({ type:'reveal', playerIndex:0, text:'法老王的庆典展示〈陵墓守卫〉并加入手牌', cards:[festivalHandCard] })
      queueMicrotask(() => {
        game.players[0].graveyard = [...(game.players[0].graveyard ?? []), festivalGraveCard]
        game.players[0].graveyardCount = game.players[0].graveyard.length
        game.players[0].libraryCount = Math.max(0, game.players[0].libraryCount - 2)
        publish({ type:'discard', playerIndex:0, text:'法老王的庆典将 卡诺匹斯罐 一 从牌库置入墓地', cards:[festivalGraveCard] })
        // Real command round-trips commonly append audit-only facts while the
        // previous image/DOM preparation is still awaiting. They must not make
        // any of the three already claimed movements enter the queue again.
        queueMicrotask(() => publish({ type:'effect-decision', playerIndex:0, text:'法老王的庆典完成选择', cards:[] }))
      })
    })
  },
  beginFinnOptionalReady() {
    finn.tapped = true
    game.players[0].field[1][2] = finn
    game.prompts = [prompt('finn-ready-choice')]
    game.revision += 1
  },
  declineFinnOptionalReady() {
    game.prompts = []
    publish({ type:'effect-declined', playerIndex:0, text:'芬恩选择不消耗1符文转为活跃', cards:[finn] })
  },
  defeatRestedDefender() {
    const defeated = game.players[1].field.flat().find(item => item?.instanceId === restedDefender.instanceId)
    if (!defeated) return
    game.players[1].field = game.players[1].field.map(row => row.map(item => item?.instanceId === defeated.instanceId ? null : item))
    game.players[1].graveyard = [...(game.players[1].graveyard ?? []), defeated]
    game.players[1].graveyardCount = game.players[1].graveyard.length
    publishBatch([
      { type:'combat', playerIndex:0, text:'侍从骑士造成2000点战斗伤害并击杀休整守军', cards:[mover, defeated] },
      { type:'leave', playerIndex:1, text:'休整守军阵亡进入墓地', cards:[defeated] },
    ])
  },
  returnDuplicateCard() {
    game.players[0].graveyard = (game.players[0].graveyard ?? []).filter(card => card.instanceId !== duplicateCard.instanceId)
    game.players[0].graveyardCount = game.players[0].graveyard.length
    game.players[0].libraryCount += 1
    publish({ type:'return', playerIndex:0, text:'〈佣兵部队〉从墓地返回牌库底部', cards:[duplicateCard] })
  },
  openPrompt() { game.prompts = [prompt(`blocking-${game.revision}`)]; game.revision += 1 },
  closePrompt() { game.prompts = []; game.revision += 1 },
  setReplay(speed: number | null) { playbackSpeed.value = speed },
  setViewer(playerIndex: number) { game.you = playerIndex; game.revision += 1 },
  authorityEntryMoveCount(instanceId: string) { return authorityEntryMoves.get(instanceId) ?? 0 },
  visibilityLeaseCount(instanceId: string) {
    const target = document.querySelector(`[data-card-instance-id="${CSS.escape(instanceId)}"]`)
    return target instanceof HTMLElement ? authoritativeCardVisibilityLeaseCount(target) : -1
  },
}
Object.assign(window, { __visualTransitionHarness: api })
</script>

<template>
  <div v-if="landscapeEnabled" id="l12-landscape-teleports" />
  <div :class="{ 'l12-landscape-surface': landscapeEnabled }" :data-l12-landscape-canvas="landscapeEnabled || undefined">
    <GameBoard :game="game" read-only :replay-playback-speed="playbackSpeed" />
  </div>
</template>
