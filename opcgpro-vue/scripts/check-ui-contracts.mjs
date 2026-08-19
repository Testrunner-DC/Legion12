import { readFileSync } from 'node:fs'

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8')
const shell = read('../src/l12/site/SiteShell.vue')
const board = read('../src/l12/game/GameBoard.vue')
const prompt = read('../src/l12/game/PromptOverlay.vue')
const lobby = read('../src/l12/site/BattleHubPage.vue')
const deckEditor = read('../src/l12/L12DeckEditor.vue')
const gamePage = read('../src/l12/GamePage.vue')
const playerMat = read('../src/l12/game/PlayerMat.vue')
const specialAssets = read('../src/l12/specialAssets.ts')
const cardTile = read('../src/l12/CardTile.vue')
const cardArchive = read('../src/l12/CardArchive.vue')

const contracts = [
  [shell.includes('const siteBrandIcon = defaultSiteLogoUrl'), '主页入口必须引用默认网页图标'],
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
  [board.includes('card.playCost ?? card.currentCost ?? card.cost'), '手牌可打出校验必须使用服务端动态费用'],
  [board.includes('class="event-message"') && board.includes('overflow-wrap:anywhere'), '对局记录必须使用可换行的独立消息容器'],
  [prompt.includes("prompt.value?.kind === 'option'") && prompt.includes('effect-option-list'), '效果模式选项必须使用纵向宽按钮'],
  [deckEditor.includes('saved-list'), '保留既有牌库编辑器回滚防护'],
  [!deckEditor.includes('STARTER COPY') && !deckEditor.includes('importPreset'), '牌库编辑器不得重新引入 Starter Copy 区块'],
  [specialAssets.includes('masterProfileUrl') && prompt.includes('masterProfileUrl(player.master.masterId'), '先后手掷骰必须使用官方主宰头像资源'],
  [playerMat.includes('godPowerLogoUrl') && specialAssets.includes('olympus-god-power.png'), '神力必须使用官方神力标志'],
  [lobby.includes('<div class="deck-thumb">库</div>') && lobby.includes('masterProfileUrl(deck.masterId)'), '牌库入口保留“库”标志，具体牌库按钮使用主宰头像'],
  [cardTile.includes('attachedGroups') && cardTile.includes('attached-card-orbs') && cardTile.includes("$emit('focusCard', group.card)"), '叠放卡牌必须由公共卡牌组件合并为圆形卡图，并可进入统一详情'],
  [cardTile.includes('position:static!important') && cardTile.includes('object-position:center 14%'), '圆形叠放卡图不得被全局卡图定位规则覆盖'],
  [playerMat.includes("entry.id === 'trialAdvance'") && playerMat.includes('function canTrial') && playerMat.includes("'trialAdvance')"), '试炼军团必须拥有与进攻、移动并列的直接试炼按钮'],
  [playerMat.includes('aspect-ratio:8/5') && playerMat.includes('.trial-card b{position:absolute;left:50%;top:50%'), '试炼卡必须保持横版比例并将进度数字置于中央'],
  [playerMat.includes('class="master-marker-track"') && playerMat.includes('.master-marker-track{position:absolute') && playerMat.includes('top:-39px'), '主宰附近圆形标识必须复用同一轨道并与主宰保持间距'],
  [!playerMat.includes('class="rune-zone"') && !playerMat.includes('class="canopic-track"'), '符文与卡诺匹斯不得恢复各自独立的定位父级'],
  [board.includes('destructionRoundBackUrl'), '本局天灾圆形未知卡必须使用圆形天灾卡背'],
  [lobby.includes('visibleDeckLabel') && lobby.includes('player.playerIndex === l12State.room?.yourPlayerIndex'), '房间内不得向对手公开牌库名称'],
  [deckEditor.includes('masterProfileUrl(selectedMaster.id') && lobby.includes('border-radius:2px'), '主宰头像必须使用官方正方形资源'],
  [cardArchive.includes('trialValue') && cardArchive.includes('<dt>试炼值</dt>'), '卡牌档案必须展示试炼值'],
  [playerMat.includes('aria-disabled') && playerMat.includes('.morale-orb.active-morale[aria-disabled="true"]') && playerMat.includes('.morale-orb.active-god-power[aria-disabled="true"]'), '可用的活跃士气与神力必须始终高亮'],
  [playerMat.includes('class="morale-count"') && playerMat.match(/class="morale-count"/g)?.length === 2, '双方士气数量必须共用不溢出的计数器'],
  [board.includes('promotionFoundationTargetIds') && board.includes('nextS2PromotionGodPowerDiscount'), '晋升登场必须高亮合法基底并纳入锻造炉减免'],
  [deckEditor.includes('主宰') && deckEditor.includes('主牌库') && deckEditor.includes('额外卡牌') && !deckEditor.includes('可用卡牌'), '牌库编辑器中区必须保持主宰/主牌库/额外卡牌三标签'],
  [gamePage.includes("import { gameAction, l12State, leaveRoom } from './net'") && gamePage.includes("game.value?.phase === 'GameOver'"), '对局结束返回大厅前必须退出已结束房间'],
]

const failures = contracts.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  console.error(`UI 契约检查失败：\n- ${failures.join('\n- ')}`)
  process.exit(1)
}

console.log(`UI 契约检查通过（${contracts.length} 项）`)
