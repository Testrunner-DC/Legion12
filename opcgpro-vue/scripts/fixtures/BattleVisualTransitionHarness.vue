<script setup lang="ts">
import { reactive, ref } from 'vue'
import type { ActionEvent, Card, GameState, PlayerView, Prompt } from '../../src/l12/types'
import GameBoard from '../../src/l12/game/GameBoard.vue'

const card = (instanceId: string, name: string, cardId: string, tapped = false): Card => ({
  instanceId, name, cardId, cardType: 'legion', faction: 'otherworld', cost: 1,
  baseTroops: 2000, troops: 2000, disasterLevel: 0, tapped, summonRound: 0,
})
const mover = card('mover-1', '侍从骑士', 'S02-0609')
const swapper = card('mover-2', '罗宾汉', 'S02-0617')
const host = card('host-1', '狮心王理查一世', 'S02-0608')
const handSquire = card('hand-squire', '侍从骑士', 'S02-0609')
const robinSquire = card('robin-squire', '侍从骑士', 'S02-0609')
const millCards = [
  card('mill-card-1', '磨牌一', 'S01-0001'),
  card('mill-card-2', '磨牌二', 'S01-0003'),
  card('mill-card-3', '磨牌三', 'S01-0004'),
]
const duplicateCard = card('duplicate-discard', '佣兵部队', 'S01-0002')
const nuada = { ...card('nuada-source', '银臂努阿达', 'ST06-M1'), cardType: 'master' }

function player(playerIndex: number): PlayerView {
  return {
    playerIndex, name: playerIndex ? '玩家B' : '玩家A', deckName: '', faction: 'otherworld',
    master: { masterId: playerIndex ? 'S02-06M1' : 'ST06-M1', masterName: playerIndex ? '莫瑞甘' : '银臂努阿达', hp: 8, maxHp: 8 },
    libraryCount: playerIndex ? 20 : 21, libraryTop: null, hand: playerIndex ? [] : [handSquire, robinSquire, duplicateCard], handCount: playerIndex ? 0 : 3,
    morale: [], field: playerIndex
      ? [[null, null, null], [null, null, null]]
      : [[mover, swapper, host], [null, null, null]],
    graveyard: [], graveyardCount: 0, resolving: [],
    specialZones: { runes: 2, trialLevel: 0, godPower: [], trials: [] }, mulliganDone: true,
  }
}

const game = reactive<GameState>({
  matchId: 'visual-transition-harness', roomCode: 'HARNESS', you: 0, revision: 1,
  activePlayer: 0, firstPlayer: 0, diceWinner: 0, initiativeRolls: [4, 2], phase: 'Main', round: 2, turnSerial: 2,
  disasterMode: 'none', disasterValue: 0, prompts: [], effectStack: [], players: [player(0), player(1)],
  recentEvents: [], legalAttackTargets: {}, stateHash: 'visual-1',
})
const playbackSpeed = ref<number | null>(null)
let sequence = 0
function publish(event: Omit<ActionEvent, 'sequence'>) {
  game.revision += 1
  game.stateHash = `visual-${game.revision}`
  game.recentEvents = [...(game.recentEvents ?? []), { ...event, sequence: ++sequence }]
}
function prompt(id: string, data: Record<string, string> = {}): Prompt {
  return { promptId: id, playerIndex: 0, kind: 'optional-card', text: '测试阻塞弹框', validChoices: ['yes'], minChoose: 1, maxChoose: 1, data, choiceLabels: { yes: '确认' } }
}
function replaceField(next: Array<Array<Card | null>>) { game.players[0].field = next; game.revision += 1 }

const api = {
  tap() { const current = game.players[0].field.flat().find(item => item?.instanceId === mover.instanceId); if (current) current.tapped = true; game.revision += 1 },
  ready() { const current = game.players[0].field.flat().find(item => item?.instanceId === mover.instanceId); if (current) current.tapped = false; game.revision += 1 },
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
  millMany() {
    game.players[0].libraryCount = Math.max(0, game.players[0].libraryCount - millCards.length)
    game.players[0].graveyard = [...(game.players[0].graveyard ?? []), ...millCards]
    game.players[0].graveyardCount = game.players[0].graveyard.length
    publish({ type:'mill', playerIndex:0, text:'动画契约弃置牌库顶部3张牌', cards:millCards })
  },
  returnMilled() {
    const ids = new Set(millCards.map(card => card.instanceId))
    game.players[0].graveyard = (game.players[0].graveyard ?? []).filter(card => !ids.has(card.instanceId))
    game.players[0].graveyardCount = game.players[0].graveyard.length
    game.players[0].libraryCount += millCards.length
    const events = millCards.map(card => ({
      sequence:++sequence, type:'return', playerIndex:0,
      text:`〈${card.name}〉从墓地返回牌库底部`, cards:[card],
    }))
    game.revision += 1
    game.stateHash = `visual-${game.revision}`
    game.recentEvents = [...(game.recentEvents ?? []), ...events]
  },
  duplicateDiscardSnapshots() {
    game.players[0].hand = game.players[0].hand?.filter(card => card.instanceId !== duplicateCard.instanceId)
    game.players[0].handCount = game.players[0].hand?.length
    game.players[0].graveyard = [...(game.players[0].graveyard ?? []), duplicateCard]
    game.players[0].graveyardCount = game.players[0].graveyard.length
    const discard = { sequence:++sequence, type:'discard', playerIndex:0, text:'玩家A弃置佣兵部队', cards:[duplicateCard] }
    game.recentEvents = [...(game.recentEvents ?? []), discard]
    queueMicrotask(() => {
      game.recentEvents = [...(game.recentEvents ?? []),
        { sequence:++sequence, type:'response', playerIndex:0, text:'发动佣兵部队抵挡进攻', cards:[duplicateCard] },
        { sequence:++sequence, type:'effect-response', playerIndex:0, text:'佣兵部队响应结算', cards:[duplicateCard] },
      ]
    })
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
}
Object.assign(window, { __visualTransitionHarness: api })
</script>

<template>
  <GameBoard :game="game" read-only :replay-playback-speed="playbackSpeed" />
</template>
