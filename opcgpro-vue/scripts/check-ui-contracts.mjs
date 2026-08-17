import { readFileSync } from 'node:fs'

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8')
const shell = read('../src/l12/site/SiteShell.vue')
const board = read('../src/l12/game/GameBoard.vue')
const prompt = read('../src/l12/game/PromptOverlay.vue')
const lobby = read('../src/l12/site/BattleHubPage.vue')
const deckEditor = read('../src/l12/L12DeckEditor.vue')
const gamePage = read('../src/l12/GamePage.vue')

const contracts = [
  [shell.includes("const siteBrandIcon = '/assets/l12/site-icon-default.png'"), '主页入口必须引用默认网页图标'],
  [!shell.includes('/assets/l12/card-back-navy.png'), '主页入口不得回退为卡背'],
  [board.includes('Array.from({ length: 4 }'), '本局天灾必须固定为四个槽位'],
  [board.includes('border-radius:50%') && board.includes('.session-disaster-strip'), '本局天灾必须保持圆形缩略图'],
  [board.includes('<Teleport to="body" :disabled="!modalInspectorVisible">'), '弹框期间必须复用原选中卡牌详情框'],
  [!board.includes('class="modal-card-inspector"'), '不得重新引入第二套弹框卡牌详情'],
  [!board.includes('<CardTile'), '卡牌详情不得渲染战场角标 UI'],
  [!prompt.includes('class="prompt-card-inspector"') && !prompt.includes('class="prompt-card-detail"'), 'PromptOverlay 不得自建卡牌详情框'],
  [gamePage.includes("import GameBoard from './game/GameBoard.vue'"), '对战入口必须唯一指向 src/l12/game/GameBoard.vue'],
  [!lobby.includes('l12State.room.decks'), '友谊战整备室不得同时渲染服务端预组与我的牌库'],
  [lobby.includes('copyRoomCode') && lobby.includes('复制房间码'), '友谊战整备室必须保留房间码复制按钮'],
  [board.includes('selected-card-inspector-anchor') && board.includes(':style="modalInspectorVisible ? inspectorFloatStyle : undefined"'), '弹框期间详情必须由原选中卡牌框锚点定位'],
  [!board.includes('.modal-card-inspector') && !prompt.includes('.prompt-card-inspector'), '不得保留第二套弹框详情框样式'],
  [deckEditor.includes("deck.name === activeDeckName") && deckEditor.includes('.saved-list b{color:#f1eee5}') && deckEditor.includes('.saved-list span{color:#aab4b0}') && deckEditor.includes('.saved-list article.active{border-color:#86e8ee;background:#123e42'), '牌库编辑器左下牌库列表及当前牌库状态必须保持高对比'],
]

const failures = contracts.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  console.error(`UI 契约检查失败：\n- ${failures.join('\n- ')}`)
  process.exit(1)
}

console.log(`UI 契约检查通过（${contracts.length} 项）`)
