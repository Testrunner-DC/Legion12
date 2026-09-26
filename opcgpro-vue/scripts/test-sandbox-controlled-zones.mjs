import fs from 'node:fs'

const board = fs.readFileSync('src/l12/game/GameBoard.vue', 'utf8')
const graveyard = fs.readFileSync('src/l12/game/GraveyardOverlay.vue', 'utf8')

if (!board.includes(':actor-player-index="controlledPlayerIndex"'))
  throw new Error('墓地区域未使用统一沙盒受控席')
if (board.includes(':own-player-index="game.you"'))
  throw new Error('墓地区域仍把观察者误作操作主体')
if (!board.includes(':mine="masterPlayerIndex === controlledPlayerIndex"'))
  throw new Error('主宰区域未使用统一沙盒受控席')
if (!graveyard.includes('actorPlayerIndex') || graveyard.includes('ownPlayerIndex'))
  throw new Error('墓地组件仍自行维护含混的 own 权限概念')
if (!graveyard.includes('player.value.playerIndex !== props.actorPlayerIndex'))
  throw new Error('墓地卡牌能力没有按当前操作席鉴权')

console.log('sandbox controlled-zone tests passed')
