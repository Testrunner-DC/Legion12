import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const board = read('src/l12/game/GameBoard.vue')
const checks = [
  ['src/l12/game/BattleUtilityDock.vue', ':initial-tab=', ':spectators="l12State.game?.spectators || []"'],
  ['src/l12/game/GameBoard.vue', 'BattleSpectatorSummary', ':spectators="game.spectators"', 'data-ui-contract="selected-card-inspector-stack"'],
  ['src/l12/game/BattleSpectatorSummary.vue', 'overflowCount', '点击好友按钮查看完整名单', '@media(max-width:700px)'],
  ['src/l12/site/SocialPopover.vue', "tab === 'spectators'", '完整观战玩家列表', '本局对手'],
  ['src/l12/types.ts', 'spectators?: Array<{ accountId?: string | null; name: string }>', 'Current connected observers'],
]
for (const [file, ...needles] of checks) {
  const source = read(file)
  for (const needle of needles) {
    if (!source.includes(needle)) throw new Error(`${file} missing contract: ${needle}`)
  }
}
const inspectorIndex = board.indexOf('data-ui-contract="selected-card-inspector"')
const summaryIndex = board.indexOf('<BattleSpectatorSummary')
if (inspectorIndex < 0 || summaryIndex < 0 || inspectorIndex > summaryIndex) {
  throw new Error('观战摘要必须位于卡牌详情框之后，并锚定在同一详情栈的下沿')
}
const server = fs.readFileSync(path.resolve(root, '..', '服务端WebSocket/TwelveLegions/L12RoomManager.cs'), 'utf8')
for (const needle of ['spectatorViews', 'spectators = spectatorViews', 'spectator.IsVirtual', '!spectator.Connected']) {
  if (!server.includes(needle)) throw new Error(`server spectator payload missing contract: ${needle}`)
}
console.log('battle social spectator contract passed')
