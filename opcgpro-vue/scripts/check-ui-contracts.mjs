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
const sandbox = read('../src/l12/site/SandboxPage.vue')
const gmPanel = read('../src/l12/game/GmPanel.vue')
const l12Net = read('../src/l12/net.ts')
const adminPage = read('../src/l12/site/AdminPage.vue')
const platform = read('../src/l12/platform.ts')

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
  [deckEditor.includes('<h2>筛选</h2>') && !deckEditor.includes('<h2>构筑设定</h2>') && deckEditor.includes('costFilter') && deckEditor.includes('disasterFilter') && deckEditor.includes('sortMode'), '牌库编辑器必须复用卡牌档案的搜索、类型、卡池、费用、天灾等级与排序筛选（不含阵营）'],
  [deckEditor.includes('<option value="all">全部</option><option value="S01">S1</option><option value="S02">S2</option>'), '牌库编辑器卡池筛选必须明确为全部、S1、S2'],
  [deckEditor.indexOf('生成牌库图') > deckEditor.indexOf('另存为牌库') && deckEditor.indexOf('生成牌库图') < deckEditor.indexOf('删除牌库'), '生成牌库图必须位于另存为牌库与删除牌库之间'],
  [deckEditor.includes('createDeckImageBlob') && deckEditor.includes('deck-image-dialog') && deckEditor.includes('下载牌库图'), '牌库编辑器必须提供可预览、下载的真实牌库图生成流程'],
  [deckEditor.includes('牌库删除后不可找回') && deckEditor.includes('继续删除') && deckEditor.includes('pendingDeleteName') && !deckEditor.includes('@click="onDelete'), '全部牌库删除入口必须先经过统一确认弹框'],
  [gamePage.includes("import { gameAction, l12State, leaveRoom } from './net'") && gamePage.includes("game.value?.phase === 'GameOver'") && gamePage.includes('l12State.room.sandbox'), '对局结束或退出沙盒返回大厅前必须退出房间'],
  [sandbox.includes('createSandbox') && !sandbox.includes('沙盒服务器适配器尚未接入'), '单人测试沙盒必须连接正式规则内核，不得回退为占位页'],
  [gamePage.includes('<GmPanel v-if="l12State.gmEnabled"') && l12Net.includes('gmEnabled: false'), 'GM 面板必须只在服务端授权的沙盒快照中显示'],
  [gmPanel.includes("send({ type: 'gmAction'") || (gmPanel.includes('gmAction(') && l12Net.includes("send({ type: 'gmAction', command })")), 'GM 操作必须走独立 gmAction 消息，不得伪装成普通 gameAction'],
  [gmPanel.includes('导出可复现 JSON') && gmPanel.includes('/api/matches/'), 'GM 面板必须保留可复现记录导出入口'],
  [adminPage.includes('卡效原子化') && adminPage.includes('atom-flow') && adminPage.includes('原子定义 JSON'), '管理后台必须保留卡效原子组合、流程图与原始定义视图'],
  [adminPage.includes('旧实现兜底') && adminPage.includes('新旧实现不会同时结算'), '原子化后台必须明确显示旧实现兜底与防重复结算边界'],
  [platform.includes("effectAtoms: () => request<EffectAtomDescriptor[]>('/api/admin/effect-atoms')") && platform.includes('/api/admin/effects/coverage'), '卡效后台必须从服务端权威原子注册表读取数据'],
  [adminPage.includes('实战已验证') && adminPage.includes('effectCoverage.verifiedAbilities') && platform.includes('verifiedAbilities: number'), '原子化后台必须区分文本拆分与已接管实战执行的能力'],
]

const failures = contracts.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  console.error(`UI 契约检查失败：\n- ${failures.join('\n- ')}`)
  process.exit(1)
}

console.log(`UI 契约检查通过（${contracts.length} 项）`)
