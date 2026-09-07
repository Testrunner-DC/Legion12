import assert from 'node:assert/strict'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const server = await createServer({ root, server: { middlewareMode: true }, appType: 'custom', optimizeDeps: { noDiscovery: true } })

const replayCard = (instanceId, cardId, name) => ({
  InstanceId: instanceId, CardId: cardId, Name: name, CardType: 'legion', Faction: 'otherworld',
  Cost: 2, BaseTroops: 3000, Troops: 3000, DisasterLevel: 0, Tapped: false, SummonRound: 1,
})
const player = (index) => ({
  PlayerIndex: index, Name: `玩家${index + 1}`, DeckName: '回放测试', Faction: index ? 'tianting' : 'otherworld',
  MasterId: index ? 'MASTER-1' : 'MASTER-0', MasterName: index ? '' : '旧录像主宰名', Hp: 8, MaxHp: 10,
  FactionEffect: index ? { CardId: 'RUNE-1', Name: '士气·天廷', EffectText: '权威阵营效果文本', Abilities: [] } : null,
  Library: [], Hand: [], MoraleDeck: [], Morale: [], Field: [[null, null, null], [null, null, null]],
  Graveyard: [], Resolving: [], ExtraRelics: [], SpecialZones: { Runes: 0, TrialLevel: 0, GodPower: [], Trials: [] },
  MulliganDone: true,
})
const state = (players, events = [], prompts = []) => ({
  MatchId: 'focus-test', RoomCode: 'FOCUS', ActivePlayer: 0, FirstPlayer: 0, DiceWinner: 0,
  InitiativeRolls: [6, 2], Phase: 'Main', Round: 2, DisasterMode: 'all', DisasterValue: 0,
  Players: players, Events: events, Prompts: prompts,
})
const command = (sequence, playerIndex, payload, snapshot) => ({
  sequence, receivedUtc: '', playerIndex, command: payload, accepted: true, revision: sequence,
  stateHash: `hash-${sequence}`, state: snapshot,
})
const match = { matchId: 'focus-test', roomCode: 'FOCUS', player0: '玩家1', player1: '玩家2', deck0: '', deck1: '', startedUtc: '', commandCount: 2 }
const catalog = [
  { id: 'MASTER-0', nameZh: '权威主宰零', cardType: 'master', faction: 'otherworld', effect: '权威主宰零效果' },
  { id: 'MASTER-1', nameZh: '权威主宰一', cardType: 'master', faction: 'tianting', effect: '权威主宰一效果' },
  { id: 'RUNE-1', nameZh: '士气·天廷', cardType: 'rune', faction: 'tianting', effect: '目录阵营效果' },
  { id: 'SOURCE', nameZh: '实际来源', cardType: 'legion', faction: 'otherworld', effect: '来源效果' },
]

try {
  const { replayFocusCardAt, replayGameAt } = await server.ssrLoadModule('/src/l12/replayModel.ts')
  const basePlayers = [player(0), player(1)]

  const masterDetail = { match, commands: [command(1, 0, { type: 'activateAbility', sourceInstanceId: 'master-0' }, state(basePlayers))] }
  const master = replayFocusCardAt(masterDetail, 0, catalog)
  assert.equal(master?.instanceId, 'master-0')
  assert.equal(master?.name, '旧录像主宰名')
  assert.equal(master?.effectText, '权威主宰零效果')

  const factionDetail = { match, commands: [command(1, 1, { type: 'activateAbility', sourceInstanceId: 'faction-1' }, state(basePlayers))] }
  const faction = replayFocusCardAt(factionDetail, 0, catalog)
  assert.equal(faction?.instanceId, 'faction-1')
  assert.equal(faction?.effectText, '权威阵营效果文本')
  assert.equal(replayGameAt(factionDetail, 0, new Map(catalog.map(card => [card.id, card])))?.players[1].factionEffect?.cardId, 'RUNE-1')

  const promptState = state(basePlayers, [], [{ PromptId: 'prompt-1', SourceInstanceId: 'master-1', SourceCardId: 'MASTER-1' }])
  const promptDetail = { match, commands: [
    command(1, 1, { type: 'passPriority' }, promptState),
    command(2, 1, { type: 'resolvePrompt', promptId: 'prompt-1' }, state(basePlayers)),
  ] }
  assert.equal(replayFocusCardAt(promptDetail, 1, catalog)?.instanceId, 'master-1')

  const source = replayCard('source-1', 'SOURCE', '实际来源')
  const previousPlayers = [player(0), player(1)]
  previousPlayers[0].Field[0][0] = source
  const noisyEvents = [
    { Sequence: 6, Type: 'attack', PlayerIndex: 0, Text: '实际来源进攻', Cards: [source] },
    { Sequence: 7, Type: 'effect-trigger', PlayerIndex: 1, Text: '对手触发', Cards: [replayCard('other-1', 'OTHER', '对手来源')] },
    { Sequence: 8, Type: 'effect-hand-add', PlayerIndex: 0, Text: '抽到目标', Cards: [replayCard('drawn-1', 'DRAWN', '被抽到的牌')] },
    { Sequence: 9, Type: 'destroy', PlayerIndex: 0, Text: '目标被击杀', Cards: [replayCard('defeated-1', 'DEFEATED', '被击杀目标')] },
  ]
  const ordinaryDetail = { match, commands: [
    command(1, 0, { type: 'passPriority' }, state(previousPlayers, [{ Sequence: 5, Type: 'turn-start', PlayerIndex: 0, Text: '开始', Cards: [] }])),
    command(2, 0, { type: 'attack', cardInstanceId: 'source-1' }, state(basePlayers, noisyEvents)),
  ] }
  assert.equal(replayFocusCardAt(ordinaryDetail, 1, catalog)?.instanceId, 'source-1')

  ordinaryDetail.commands[1].command = { type: 'legacyActionWithoutSource' }
  assert.equal(replayFocusCardAt(ordinaryDetail, 1, catalog)?.instanceId, 'source-1', 'event fallback must prefer the acting player source event')

  const systemOnly = { match, commands: [command(1, 0, { type: 'endTurn' }, state(basePlayers, [
    { Sequence: 1, Type: 'turn-end', PlayerIndex: 0, Text: '回合结束', Cards: [source] },
  ]))] }
  assert.equal(replayFocusCardAt(systemOnly, 0, catalog), null)
  console.log('Replay focus passed: exact master/faction sources, prompt source, prior-zone source, actor event fallback and system-event exclusion.')
} finally {
  await server.close()
}
