import { existsSync, readFileSync } from 'node:fs'

// Git 在 Windows 工作区可能检出 CRLF；契约按语义比较换行，不改写被检查的源文件。
const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const shell = read('../src/l12/site/SiteShell.vue')
const router = read('../src/router/index.ts')
const board = read('../src/l12/game/GameBoard.vue')
const actionLayer = read('../src/l12/game/ActionPresentationLayer.vue')
const actionPresentation = read('../src/l12/game/actionPresentation.ts')
const actionAudio = read('../src/l12/game/useL12ActionAudio.ts')
const zoneMovementLayer = read('../src/l12/game/ZoneMovementPresentationLayer.vue')
const combatMotionLayer = read('../src/l12/game/CombatMotionPresentationLayer.vue')
const osirisVictory = read('../src/l12/game/OsirisVictorySequence.vue')
const indexHtml = read('../index.html')
const faviconPath = new URL('../public/favicon.png', import.meta.url)
const globalStyle = read('../src/style.css')
const prompt = read('../src/l12/game/PromptOverlay.vue')
const matchRecords = read('../src/l12/MatchRecords.vue')
const gameActions = read('../src/l12/game/GameActions.vue')
const lobby = read('../src/l12/site/BattleHubPage.vue')
const deckEditor = read('../src/l12/L12DeckEditor.vue')
const gamePage = read('../src/l12/GamePage.vue')
const app = read('../src/App.vue')
const mainEntry = read('../src/main.ts')
const playerMat = read('../src/l12/game/PlayerMat.vue')
const graveyardOverlay = read('../src/l12/game/GraveyardOverlay.vue')
const masterOverlay = read('../src/l12/game/MasterOverlay.vue')
const globalBugFeedback = read('../src/l12/site/GlobalBugFeedback.vue')
const specialAssets = read('../src/l12/specialAssets.ts')
const cardTile = read('../src/l12/CardTile.vue')
const l12Types = read('../src/l12/types.ts')
const cardArchive = read('../src/l12/CardArchive.vue')
const sandbox = read('../src/l12/site/SandboxPage.vue')
const gmPanel = read('../src/l12/game/GmPanel.vue')
const sandboxPicker = read('../src/l12/game/SandboxCardPicker.vue')
const l12Net = read('../src/l12/net.ts')
const adminPage = read('../src/l12/site/AdminPage.vue')
const profilePage = read('../src/l12/site/ProfilePage.vue')
const recoveryPage = read('../src/l12/site/AccountRecoveryPage.vue')
const platform = read('../src/l12/platform.ts')
const decks = read('../src/l12/decks.ts')
const deckOrdering = read('../src/l12/deckOrdering.ts')
const deckShare = read('../src/l12/site/deckShare.ts')
const deckLibrary = read('../src/l12/site/DeckLibraryPage.vue')
const deckProfile = read('../src/l12/DeckProfile.vue')
const legacyLobby = read('../src/l12/LobbyPage.vue')
const tournamentCenter = read('../src/l12/site/TournamentCenterPage.vue')
const wsSmoke = read('../../scripts/ws-smoke.mjs')
const wsServer = read('../../服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')
const cacheEnvironment = read('../../ops/windows/Initialize-L12BuildEnvironment.ps1')
const windowsVerify = read('../../ops/windows/verify-l12.ps1')
const windowsDeploy = read('../../ops/windows/deploy-l12.ps1')
const serverDeploy = read('../../ops/server/deploy-l12-release.sh')
const bugQueue = read('../../ops/windows/Get-L12BugQueue.ps1')
const s1Cards = JSON.parse(read('../public/data/l12/cards.s1.json'))

const confirmedS1DisasterLevels = {
  'S01-0304': 2,
  'S01-0305': 1,
  'S01-0306': 1,
  'S01-0307': 1,
  'S01-0308': 1,
  'S01-0406': 1,
}

const contracts = [
  [indexHtml.includes('<link rel="icon" type="image/png" href="/favicon.png" />') && existsSync(faviconPath), '网页标签必须使用项目提供的 Logo-Mini PNG，不得回退默认 Vite 图标'],
  [board.includes("import ActionPresentationLayer from './ActionPresentationLayer.vue'") && board.includes('<ActionPresentationLayer :events="game.recentEvents ?? []"') && actionLayer.includes('data-ui-contract="authoritative-action-presentation"'), 'L12 对局必须消费服务端 recentEvents 播放统一阶段变化条'],
  [actionPresentation.includes("event.type === 'turn-start'") && actionPresentation.includes("event.text === '进入主要阶段'") && actionPresentation.includes("event.text === '执行结束阶段'") && !actionPresentation.includes("执行抽牌阶段") && !actionPresentation.includes("执行士气阶段") && !actionPresentation.includes("event.type === 'play'") && !actionPresentation.includes("event.type === 'attack'"), '通用动作条只能呈现回合开始、主要阶段和回合结束，不得承载其他阶段或卡牌动作'],
  [board.includes("import ZoneMovementPresentationLayer from './ZoneMovementPresentationLayer.vue'") && board.includes('<ZoneMovementPresentationLayer :events="game.recentEvents ?? []"') && zoneMovementLayer.includes('data-ui-contract="authoritative-zone-card-movement"') && zoneMovementLayer.includes('getBoundingClientRect()') && zoneMovementLayer.includes('data-l12-game-stage') && zoneMovementLayer.includes('data-card-instance-id') && zoneMovementLayer.includes('await nextTick()'), '打出、登场与区域移动必须读取更新前后真实 DOM 锚点并由独立卡牌实体连续飞行'],
  [zoneMovementLayer.includes('source.cloneNode(true)') && zoneMovementLayer.includes('wrapper.animate([') && !zoneMovementLayer.includes('--move-mid-x') && !zoneMovementLayer.includes('l12-zone-card-pulse'), '卡牌跨区域动画必须复用来源实体快照并做单次起终点位移，不得恢复中途放大、脉冲或三段跳变'],
  [board.includes("import CombatMotionPresentationLayer from './CombatMotionPresentationLayer.vue'") && board.includes('<CombatMotionPresentationLayer :events="game.recentEvents ?? []"') && combatMotionLayer.includes("event.type === 'attack'") && combatMotionLayer.includes("event.type === 'combat'") && combatMotionLayer.includes("badge.textContent = '0'") && combatMotionLayer.includes("zoneElement('graveyard', owner)"), '进攻必须有轻量前冲，战斗伤害后阵亡军团必须先显示兵力0再进入所有者墓地'],
  [zoneMovementLayer.includes("event.type === 'play'") && zoneMovementLayer.includes("event.type === 'put' || event.type === 'enter'") && zoneMovementLayer.includes("event.type === 'move'") && zoneMovementLayer.includes("event.type === 'counter-set'") && zoneMovementLayer.includes('/assets/l12/card-back-official.png') && zoneMovementLayer.includes('const concealed = !card || card.identityKnown === false') && zoneMovementLayer.includes('covered: card?.hidden === true') && !zoneMovementLayer.includes("event.type === 'counter-set' ||"), '跨区域动画必须覆盖打出、登场、位移和盖伏；未知身份使用卡背，拥有者已知盖伏卡仍显示灰置卡面'],
  [actionLayer.includes('lastSequence = highest') && zoneMovementLayer.includes('lastSequence = highest') && zoneMovementLayer.includes('item.sequence > lastSequence') && board.includes(':paused="Boolean(publicReveal || diceReveal || hiddenRevealCard)"'), '阶段条与跨区域卡牌动画必须以首次事件序列建立基线，并在既有展示/掷骰动画播放时排队'],
  [actionAudio.includes('createOscillator()') && actionAudio.includes('useAudioStore.getState()') && !actionAudio.includes('playBgm') && !actionAudio.includes('new Audio('), '基础动作音效必须使用本地程序化短音效、遵循音效设置且不得增加 BGM 或远程音频依赖'],
  [actionLayer.includes('pointer-events:none') && actionLayer.includes('@media(prefers-reduced-motion:reduce)') && !actionLayer.includes('action-mask'), '基础动作动画必须无蒙版、不可阻塞操作并遵循减少动态效果偏好'],
  [gamePage.includes("import OsirisVictorySequence from './game/OsirisVictorySequence.vue'") && gamePage.includes("item.type === 'special-victory'") && gamePage.includes("card.cardId === 'S01-02M2'") && gamePage.includes("game.phase === 'GameOver' && !osirisSequencePlaying") && osirisVictory.includes('data-ui-contract="osiris-special-victory-sequence"') && osirisVictory.includes("['S01-0216', 'S01-0217', 'S01-0218', 'S01-0219', 'S01-0220']") && osirisVictory.includes('<CardImage :card-id="cardId"') && osirisVictory.includes('points="50,92 20,18 92,56 8,56 80,18 50,92"') && osirisVictory.includes('setTimeout(() => emit(\'complete\'), 7000)') && osirisVictory.includes('card-id="S01-02M2"'), '奥西里斯特殊胜利必须以金色七秒序列沿倒五角星路径显示五张完整圣物卡，再显示中央奥西里斯与闪光'],
  [osirisVictory.includes('playL12OsirisVictorySound()') && actionAudio.includes('playL12OsirisVictorySound') && actionAudio.includes('[196, 220, 247, 294, 330]') && actionAudio.includes('isMuted || sfxVolume <= 0'), '奥西里斯特殊胜利必须使用本地轻量专属音效并遵循静音与音效音量设置'],
  [Object.entries(confirmedS1DisasterLevels).every(([id, level]) => s1Cards.find(card => card.id === id)?.disasterLevel === level), '第一季补充天灾等级必须进入前端卡牌目录'],
  [shell.includes('const siteBrandIcon = defaultSiteLogoUrl'), '主页入口必须引用默认网页图标'],
  [shell.includes('friendApi.request(player.accountId)') && shell.includes('inviteFriend(player.accountId)') && shell.includes('spectateRoom(player.roomCode)') && shell.includes("player.activity === 'playing'") && shell.includes(':disabled="!player.canSpectate"'), '在线玩家窗口必须支持直接添加好友、邀请空闲好友，并将对局中玩家替换为带权限原因的观战入口'],
  [shell.includes('friendApi.resolve(player.accountId, accept)') && shell.includes("player.friendDirection === 'incoming'") && shell.includes('resolveOnlineFriend(player, false)') && shell.includes('resolveOnlineFriend(player, true)') && shell.includes('>拒绝</button>') && shell.includes("'接受'"), '在线玩家窗口必须允许直接接受或拒绝收到的好友申请，不能只显示待处理状态'],
  [!shell.includes('/assets/l12/card-back-navy.png'), '主页入口不得回退为卡背'],
  [shell.includes("{ to: '/battle', icon: 'battle', label: '大厅' }") && !shell.includes("label: '对战主页'") && router.includes("{ path: '/battle', name: 'battle', component: () => import('@/l12/site/BattleHubPage.vue')") && router.includes("{ path: '/battle/lobby', redirect: '/battle' }"), '对战区域必须直接以大厅为主页，不得恢复多余的对战主页层级'],
  [board.includes('Array.from({ length: 4 }'), '本局天灾必须固定为四个槽位'],
  [board.includes('data-ui-contract="persistent-board-safe-layout"') && board.includes('data-ui-contract="phase-safe-track"') && board.includes('--l12-board-seam-safe-height:76px') && board.includes('grid-template-rows:minmax(272px,1fr) var(--l12-board-seam-safe-height) minmax(272px,1fr)') && board.includes('class="battlefield-half opponent-half"') && board.includes('class="battlefield-half my-half"'), '双方战场与中央阶段栏必须使用明确三轨安全布局，常驻 UI 不得依赖绝对定位互相覆盖'],
  [board.includes('.battlefield-half::before') && board.includes('.battlefield-half.my-half::before') && board.includes('box-sizing:border-box;width:100%'), '双方战场外框必须覆盖各自完整战场轨道，不得缩入我方后排'],
  [l12Types.includes("'lock' | 'power-up' | 'power-down' | 'disabled' | 'shield' | 'discard-end' | 'extra-attack'") && l12Types.includes('statusIcons?: string[]') && l12Types.includes('statusEffects?: CardStatusEffect[]'), '卡牌投影视图必须提供结构化 statusEffects/statusIcons 状态契约并兼容旧快照缺省'],
  [cardTile.includes('props.card.statusEffects ?? []') && cardTile.includes('props.card.statusIcons ?? []') && cardTile.includes('statusLabel(effect, kind)') && cardTile.includes(':title="status.label"') && cardTile.includes(':aria-label="status.label"') && cardTile.includes('card-status-icons') && cardTile.includes('has-status-effects .card-keyword-stack') && !cardTile.includes('modifier.costDelta'), '卡牌状态图标必须按结构化状态渲染准确提示、避让关键词，费用修正不得重复为状态图标'],
  [board.split("&& !(mode === 'attack' && selectedId)").length - 1 === 2 && playerMat.includes("targetable: Boolean(player.field[row][slot])") && playerMat.includes("source: selectedId === player.field[row][slot]?.instanceId"), '进攻选目标时不得高亮整块玩家区域，只能标记进攻者与合法或已选目标'],
  [globalStyle.includes('.battle-zone{position:relative}.battle-zone>.morale-rail{position:absolute') && globalStyle.includes('.l12-player-mat.side-opponent .battle-zone>.morale-rail{top:3px}') && globalStyle.includes('.l12-player-mat.side-my .battle-zone>.morale-rail{bottom:3px}'), '士气条必须脱离战场纵向占位并固定在主宰侧通道，不得挤压战场后侵入阶段安全轨道'],
  [board.includes('border-radius:50%') && board.includes('.session-disaster-strip'), '本局天灾必须保持圆形缩略图'],
  [board.includes('<Teleport to="body" :disabled="!modalInspectorVisible">'), '弹框期间必须复用原选中卡牌详情框'],
  [!board.includes('class="modal-card-inspector"'), '不得重新引入第二套弹框卡牌详情'],
  [!board.includes('<CardTile'), '卡牌详情不得渲染战场角标 UI'],
  [!prompt.includes('class="prompt-card-inspector"') && !prompt.includes('class="prompt-card-detail"'), 'PromptOverlay 不得自建卡牌详情框'],
  [prompt.includes('<section v-if="minimized"') && prompt.includes('<section v-else-if="prompt"') && prompt.includes('minimizedChange'), 'Prompt 最小化后必须只保留展开条，并同步隐藏浮动卡牌详情'],
  [prompt.includes('.l12-prompt-overlay.minimized{z-index:2000;inset:auto 16px 66px auto') && prompt.includes('@media(max-width:760px){.l12-prompt-overlay.minimized{right:10px;bottom:60px}') && masterOverlay.includes('.master-overlay.minimized{z-index:2000;inset:auto 16px 66px auto') && playerMat.includes('.faction-effect-overlay.minimized{z-index:2000;inset:auto 16px 66px auto') && globalBugFeedback.includes('.bug-feedback-trigger{position:fixed;z-index:1900;right:16px;bottom:16px'), '所有最小化展开入口必须在桌面与小屏幕避开全局 Bug 反馈按钮'],
  [prompt.includes('<section v-if="minimized" class="prompt-minimized-bar" role="status">\n        <button :aria-label="`展开：${overlayTitle}`"') && masterOverlay.includes('<section v-if="minimized" class="master-minimized">\n        <button :aria-label="`展开：${player.master.masterName} · 主宰效果`"') && playerMat.includes('<section v-if="factionMinimized" class="faction-minimized-bar">\n        <button :aria-label="`展开：${player.factionEffect?.name || \'阵营效果\'}`"') && playerMat.includes('<section v-if="abilityCardMinimized" class="faction-minimized-bar">\n        <button :aria-label="`展开：${abilityCardOpen.name}`"'), '弹框最小化后必须仅保留带上下文无障碍名称的展开按钮'],
  [gamePage.includes("import GameBoard from './game/GameBoard.vue'"), '对战入口必须唯一指向 src/l12/game/GameBoard.vue'],
  [!lobby.includes('l12State.room.decks'), '友谊战整备室不得同时渲染服务端预组与我的牌库'],
  [lobby.includes('platformState.account') && !lobby.includes('玩家昵称<input'), '对战大厅必须使用登录账号身份且不得保留手填昵称'],
  [l12Net.includes("JSON.stringify({ type: 'hello', authToken })") && !l12Net.includes("type: 'hello', name"), 'WebSocket 握手必须使用账号令牌而非任意昵称'],
  [app.includes('startAutomaticConnection') && app.includes('[platformState.token, authState.verified]') && app.includes('token && verified') && app.includes('{ immediate: true }'), '只有经过服务端验证的登录玩家才能在全站启动自动连接'],
  [mainEntry.includes('initializeAuth()') && mainEntry.includes('await Promise.race([') && mainEntry.includes('window.setTimeout(resolve, 3_000)') && mainEntry.indexOf('initializeAuth()') < mainEntry.indexOf("mount('#app')"), '应用挂载前必须有界等待权威身份初始化，认证服务不可达时也不能让公共站点无限白屏'],
  [l12Net.includes('scheduleReconnect') && l12Net.includes('connectPromise') && l12Net.includes("type: 'ping'") && l12Net.includes("location.protocol === 'https:'"), 'WebSocket 必须防止并发建连、支持断线退避重连和正式站同源选址'],
  [decks.includes("platformRequest<SavedL12Deck[]>('/api/decks')") && decks.includes("method: 'PUT'") && decks.includes("method: 'DELETE'"), '玩家牌库必须与账号服务端持久化同步'],
  [lobby.includes('copyRoomCode') && lobby.includes('复制房间码'), '友谊战整备室必须保留房间码复制按钮'],
  [lobby.includes('isRoomHost') && lobby.includes('updateRoomOptions(editableRoomOptions.value)') && lobby.includes('保存房间规则') && l12Net.includes("type: 'updateRoomOptions'") && shell.includes('l12State.friendInvitation = null\n  resolveFriendInvitation(invitationId, accept)'), '好友邀请接受后必须立即关闭邀请层并进入整备室，且仅房主可在开战前调整房间规则'],
  [lobby.includes('v-model="roomOptions.useCardRestrictions"') && lobby.includes('不启用运营禁限卡') && lobby.includes('启用运营禁限卡') && !lobby.includes('v-model="roomOptions.matchModeId"') && !lobby.includes('<option value="season"'), '好友房必须以是否启用运营禁限卡取代排位/休闲模式，且不得选择赛季天灾'],
  [l12Net.includes("export type SandboxDisasterMode = 'all' | 'random' | 'custom' | 'none'") && sandbox.includes('<option value="custom"') && !sandbox.includes('<option value="season"'), '沙盒只能使用全部、随机、自定或无天灾，不得接入赛季天灾池'],
  [lobby.includes('排位与休闲匹配的数据服务尚未接入') && lobby.includes('匹配服务待接入'), '公开匹配尚未实现时必须保持占位和禁用状态，不得伪造匹配结果'],
  [board.includes('selected-card-inspector-anchor') && board.includes(':style="modalInspectorVisible ? inspectorFloatStyle : undefined"'), '弹框期间详情必须由原选中卡牌框锚点定位'],
  [!board.includes('.modal-card-inspector') && !prompt.includes('.prompt-card-inspector'), '不得保留第二套弹框详情框样式'],
  [deckEditor.includes("deck.name === activeDeckName") && deckEditor.includes('.saved-list b{color:#f1eee5}') && deckEditor.includes('.saved-list span{color:#aab4b0}') && deckEditor.includes('.saved-list article.active{border-color:#86e8ee;background:#123e42'), '牌库编辑器左下牌库列表及当前牌库状态必须保持高对比'],
  [board.includes('card.playCost ?? card.currentCost ?? card.cost'), '手牌可打出校验必须使用服务端动态费用'],
  [board.includes('class="event-message"') && board.includes('overflow-wrap:anywhere'), '对局记录必须使用可换行的独立消息容器'],
  [board.includes('<Teleport to="body">') && board.includes('public-card-reveal-animation') && board.includes('z-index:2147483000') && board.includes("event.type === 'effect-trigger'") && board.includes("event.type === 'effect-response'") && board.includes("event.type === 'effect-activation'") && board.includes("event.type === 'reveal'") && board.includes("event.playerIndex !== props.game.you") && board.includes("event.type === 'effect-trigger' && /展示|公开/.test(event.text)") && board.includes("event.type === 'search' && /展示|加入手牌/") && board.includes('text: publicRevealText(event)') && board.includes('event.effectText?.trim() || event.text.trim()') && board.includes('花魁的馈赠将〈${card.name}〉加入手牌') && board.includes('}, 3000)') && !board.includes('reveal-confirm') && !board.includes('public-reveal-mask'), '公开展示、检索加入手牌、触发、响应与发动效果必须只向非发动方播放三秒无蒙版非阻塞动画；只呈现事件单条效果文本和涉及卡图，花魁的馈赠必须明确展示加入手牌的卡名'],
  [prompt.includes("const usesDetailCardImages = computed(() => isDisasterChoice.value || isInfoConfirm.value)") && prompt.includes(":intent=\"usesDetailCardImages ? 'detail' : 'thumb'\"") && prompt.split(":alt=\"entry.card.name || '天灾'\" intent=\"detail\"").length - 1 === 2 && prompt.includes("'disaster-choice': isDisasterChoice"), '公开天灾禁选、随机公开、触发确认及已公开历史必须请求详情级高清图，不得使用缩略图源'],
  [board.includes(':inspector-visible="modalInspectorVisible"') && prompt.includes("'inspector-active': inspectorVisible") && prompt.includes('--inspector-safe-lane:clamp(118px,19vw,258px)') && prompt.includes('@media(max-width:520px)') && board.includes("transform: 'none'") && board.includes('overflow:auto!important'), '弹框期间原选中详情必须固定侧置并为核心弹框保留安全区，在窄屏与缩放下也不得互相遮挡'],
  [board.includes("event.type === 'disaster-reveal'") && board.includes("event.playerIndex === null") && board.includes("'disaster-reveal': '天灾'") && board.includes("'effect-response': '响应'") && board.includes("'effect-activation': '发动'"), '天灾必须向双方播放，响应与发动动画必须进入可读日志'],
  [board.includes('data-ui-contract="dice-event-animation"') && board.includes("event.type === 'dice'") && board.includes("dice: '掷骰'") && board.includes('@keyframes l12-dice-roll') && board.includes('z-index:2147483001'), '普通掷骰事件必须在全局最上层播放非阻塞动画并保留可读日志'],
  [prompt.includes("prompt.value?.kind === 'option'") && prompt.includes('effect-option-list'), '效果模式选项必须使用纵向宽按钮'],
  [prompt.includes("booleanData(id, 'hasPrintedCost')") && prompt.includes('hasPrintedCost: detail.hasPrintedCost') && matchRecords.includes("trait.endsWith('专属')"), '衍生卡在弹框与历史回放中不得伪造不存在的印刷费用'],
  [prompt.includes('prompt.value?.data?.[id] ?? choiceLabels[id]') && prompt.includes("front: '前排'") && prompt.includes("trial: '试炼进度+1'"), '效果选项必须优先显示服务端效果原文，并为通用协议选项提供中文兜底'],
  [playerMat.includes('entry.enabled === false || entry.triggerOnly') && playerMat.includes('.faction-effect-actions button:disabled'), '不可发动与仅触发时发动的效果必须保留可查看文本、灰置且不可点击'],
  [deckEditor.includes('saved-list'), '保留既有牌库编辑器回滚防护'],
  [!deckEditor.includes('STARTER COPY') && !deckEditor.includes('importPreset'), '牌库编辑器不得重新引入 Starter Copy 区块'],
  [specialAssets.includes('masterProfileUrl') && prompt.includes('masterProfileUrl(player.master.masterId'), '先后手掷骰必须使用官方主宰头像资源'],
  [playerMat.includes('godPowerLogoUrl') && specialAssets.includes('olympus-god-power.png'), '神力必须使用官方神力标志'],
  [deckProfile.includes('data-deck-profile') && deckProfile.includes('masterProfileUrl(masterId, fallbackUrl)') && deckProfile.includes('class="deck-profile__portrait"'), '各类牌库框必须复用主宰 Profile 公共组件，不得各自裁切卡面'],
  [deckEditor.includes("import DeckProfile from './DeckProfile.vue'") && deckLibrary.includes("import DeckProfile from '@/l12/DeckProfile.vue'") && lobby.includes("import DeckProfile from '@/l12/DeckProfile.vue'") && sandbox.includes("import DeckProfile from '@/l12/DeckProfile.vue'") && legacyLobby.includes("import DeckProfile from './DeckProfile.vue'"), '牌库编辑器、牌库页、对战房间和沙盒的牌库框必须统一接入 DeckProfile'],
  [!deckLibrary.includes('<div class="banner-strip">') && !lobby.includes('<div class="deck-thumb">库</div>') && !legacyLobby.includes('class="commander-glyph">{{ deck.'), '牌库框不得恢复多卡裁切条或“库”占位图替代已选择主宰 Profile'],
  [playerMat.includes('const displayMoraleSlots = computed') && playerMat.includes('rank(left.resource) - rank(right.resource)') && playerMat.includes("isGodPower ? (tapped ? 2 : 0) : (tapped ? 3 : 1)") && playerMat.includes(':key="morale?.instanceId') && playerMat.includes('selectMoralePayment(morale.instanceId)'), '费用资源必须按状态排序展示，同时保留真实士气实例 ID 作为支付与返还目标'],
  [playerMat.includes('class="god-power-logo"') && playerMat.includes('sepia(1) saturate(3.2)') && playerMat.includes('border-color:#f4dda1'), '神力必须使用淡黄色 Logo 与独立描边，不能继续与白色士气图标混淆'],
  [cardTile.includes('attachedGroups') && cardTile.includes('attached-card-orbs') && cardTile.includes("$emit('focusCard', group.card)"), '叠放卡牌必须由公共卡牌组件合并为圆形卡图，并可进入统一详情'],
  [cardTile.includes('card.activeKeywords') && cardTile.includes('card-keyword-stack') && cardTile.includes('top:25px') && !cardTile.includes('status-strong'), '当前生效关键词必须以完整文字从费用下方向下排列，不得恢复底部单字角标'],
  [cardTile.includes("props.card.isMasterLegion === true") && cardTile.includes('displayBaseTroops'), '孙悟空等主宰军团化实体必须显示权威兵力且设定兵力不误判为增益'],
  [cardTile.includes('position:static!important') && cardTile.includes('object-position:center 14%'), '圆形叠放卡图不得被全局卡图定位规则覆盖'],
  [playerMat.includes("entry.id === 'trialAdvance'") && playerMat.includes('function canTrial') && playerMat.includes("'trialAdvance')"), '试炼军团必须拥有与进攻、移动并列的直接试炼按钮'],
  [playerMat.includes("@click.stop=\"!trial.hidden && selectZoneCard(trial)\""), '试炼卡必须复用公开区域卡牌能力入口，不能只有查看详情而无法发动'],
  [playerMat.includes('aspect-ratio:1752/1255') && playerMat.includes('class="trial-card-back"') && playerMat.includes('.trial-card b{position:absolute;left:50%;top:50%'), '试炼卡背必须保持正式横版素材比例并将进度数字置于中央'],
  [playerMat.includes('class="master-marker-track"') && playerMat.includes('.master-marker-track{position:absolute') && playerMat.includes('top:-39px'), '主宰附近圆形标识必须复用同一轨道并与主宰保持间距'],
  [!playerMat.includes('class="rune-zone"') && !playerMat.includes('class="canopic-track"'), '符文与卡诺匹斯不得恢复各自独立的定位父级'],
  [board.includes('destructionRoundBackUrl'), '本局天灾圆形未知卡必须使用圆形天灾卡背'],
  [lobby.includes('visibleDeckLabel') && lobby.includes('player.playerIndex === l12State.room?.yourPlayerIndex'), '房间内不得向对手公开牌库名称'],
  [decks.includes("构筑时不计入卡组数量") && decks.includes("`${counted}${uncounted ? `(${uncounted})` : ''}`"), '不计入构筑上下限的卡牌必须使用通用规则识别，并以 40(3) 形式单列数量'],
  [deckEditor.includes('publicDeckApi.publish') && deckEditor.includes("publicationId.value = ''") && deckEditor.includes("preservePublication = false"), '牌库编辑器须支持公开/更新公开牌库，并在新建、另存或切换本地牌库时隔离公开版本身份'],
  [deckLibrary.includes('publicDeckApi.list') && deckLibrary.includes('编辑公开牌库') && deckLibrary.includes('删除公开牌库') && deckLibrary.includes('ownerId === platformState.account?.id'), '公开牌库必须由服务端持久化，且仅作者显示编辑与删除入口'],
  [deckEditor.includes('masterProfileUrl(selectedMaster.id') && lobby.includes('border-radius:2px'), '主宰头像必须使用官方正方形资源'],
  [cardArchive.includes('trialValue') && cardArchive.includes('<dt>试炼值</dt>'), '卡牌档案必须展示试炼值'],
  [playerMat.includes('aria-disabled') && playerMat.includes('.morale-orb.active-morale[aria-disabled="true"]') && playerMat.includes('.morale-orb.active-god-power[aria-disabled="true"]'), '可用的活跃士气与神力必须始终高亮'],
  [playerMat.includes('class="morale-count"') && playerMat.match(/class="morale-count"/g)?.length === 2, '双方士气数量必须共用不溢出的计数器'],
  [board.includes('promotionFoundationTargetIds') && board.includes('nextS2PromotionGodPowerDiscount'), '晋升登场必须高亮合法基底并纳入锻造炉减免'],
  [deckEditor.includes('主宰') && deckEditor.includes('主牌库') && deckEditor.includes('额外卡牌') && !deckEditor.includes('可用卡牌'), '牌库编辑器中区必须保持主宰/主牌库/额外卡牌三标签'],
  [deckEditor.includes('<h2>筛选</h2>') && !deckEditor.includes('<h2>构筑设定</h2>') && deckEditor.includes('costFilter') && deckEditor.includes('disasterFilter') && deckEditor.includes('sortMode'), '牌库编辑器必须复用卡牌档案的搜索、类型、卡池、费用、天灾等级与排序筛选（不含阵营）'],
  [deckEditor.includes('<option value="all">全部卡池</option><option value="S01">S01</option><option value="S02">S02</option>'), '牌库编辑器卡池筛选必须统一使用全部卡池、S01、S02'],
  [deckEditor.includes('effectiveDeckLimit(card, masterId.value)')
    && deckEditor.includes('effectiveDeckLimit(entry.card, masterId)')
    && !deckEditor.includes('activeRestrictions') && !deckLibrary.includes('activeRestrictions')
    && decks.includes('rule.masterId === masterId') && decks.includes('rule.masterId === deck.masterId'),
  '通用牌库编辑、保存、导入与公开不得全局应用运营禁限卡；显式规则作用域仍保留主宰专属解析能力'],
  [cardArchive.includes('<option value="all">全部卡池</option><option value="S01">S01</option><option value="S02">S02</option>') && sandboxPicker.includes('<option value="all">全部卡池</option><option value="S01">S01</option><option value="S02">S02</option>'), '卡牌档案与沙盒选择器不得回退为 S1 + S2、S1、S2 旧称'],
  [deckEditor.indexOf('生成牌库图') > deckEditor.indexOf('另存为牌库') && deckEditor.indexOf('生成牌库图') < deckEditor.indexOf('删除牌库'), '生成牌库图必须位于另存为牌库与删除牌库之间'],
  [deckEditor.includes('createDeckImageBlob') && deckEditor.includes('deck-image-dialog') && deckEditor.includes('下载牌库图'), '牌库编辑器必须提供可预览、下载的真实牌库图生成流程'],
  [deckOrdering.includes('TYPE_PRIORITY') && deckOrdering.includes('Number.NEGATIVE_INFINITY') && deckEditor.includes('compareDeckCards') && deckShare.includes('compareDeckCardIds') && deckLibrary.includes('compareDeckCardIds'), '牌库默认顺序必须统一为类型、本阵营/中立、费用高至低和编号前至后，并由编辑器、详情与牌库图复用'],
  [deckEditor.includes('牌库删除后不可找回') && deckEditor.includes('继续删除') && deckEditor.includes('pendingDeleteName') && !deckEditor.includes('@click="onDelete'), '全部牌库删除入口必须先经过统一确认弹框'],
  [gamePage.includes("import { gameAction, l12State, leaveRoom, returnToRoom } from './net'") && gamePage.includes("game.value?.phase === 'GameOver'") && gamePage.includes('returnToRoom()') && gamePage.includes('l12State.spectating || l12State.room?.sandbox'), '正式对局结束必须保留房间并回到重新准备；沙盒和观战返回大厅必须退出权威房间'],
  [l12Net.includes('leavingRoom: false') && l12Net.includes("if (l12State.leavingRoom) return") && l12Net.includes("socket.send(JSON.stringify({ type: 'leaveRoom' }))") && app.includes('!l12State.leavingRoom'), '退出观战必须屏蔽迟到快照与自动路由，并在断线重连后优先重发退出请求'],
  [sandbox.includes('createSandbox') && !sandbox.includes('沙盒服务器适配器尚未接入'), '单人测试沙盒必须连接正式规则内核，不得回退为占位页'],
  [gamePage.includes('<GmPanel v-if="l12State.gmEnabled"') && l12Net.includes('gmEnabled: false'), 'GM 面板必须只在服务端授权的沙盒快照中显示'],
  [gmPanel.includes("send({ type: 'gmAction'") || (gmPanel.includes('gmAction(') && l12Net.includes("send({ type: 'gmAction', command })")), 'GM 操作必须走独立 gmAction 消息，不得伪装成普通 gameAction'],
  [gmPanel.includes('导出可复现 JSON') && gmPanel.includes('/api/matches/'), 'GM 面板必须保留可复现记录导出入口'],
  [gmPanel.includes("run('setTroops'") && gmPanel.includes("run('startAttack'") && gmPanel.includes('发起规则内测试进攻'), 'GM 面板必须保留兵力设置与规则内测试进攻闭环'],
  [gmPanel.includes("run('addCard'") && gmPanel.includes('value: count.value') && gmPanel.includes('连续放置'), 'GM 卡牌区域操作必须支持连续构造同卡场景'],
  [gmPanel.includes('SandboxCardPicker') && gmPanel.includes('选择卡片') && !gmPanel.includes('卡号，例如'), 'GM 卡牌与区域必须复用可筛选卡牌选择器，不得恢复卡号输入框'],
  [gmPanel.includes('targetHand') && gmPanel.includes("run('moveHandCard'") && gmPanel.includes("run('playHandCard'"), 'GM 必须能查看并操作双方真实手牌实例'],
  [gmPanel.indexOf('主宰、天灾与阶段') < gmPanel.indexOf('卡牌与区域') && gmPanel.includes("run('nextPhase')"), 'GM 主宰、天灾与阶段必须位于卡牌与区域上方并可进入下一阶段'],
  [gmPanel.includes("run('returnCardToHand'") && gmPanel.includes('返回手牌'), 'GM 场上卡牌必须提供返回所有者手牌的操作'],
  [gmPanel.includes("run('resetCardEffects'") && gmPanel.includes('重置效果'), 'GM 场上卡牌必须提供重置所选卡牌回合1次效果限制的操作'],
  [!gmPanel.includes('手牌（GM 可操作）') && !gmPanel.includes('自动切换该方为回合玩家') && !gmPanel.includes('军团会返回棋盘'), 'GM 面板不得保留重复权限文字及已要求删除的说明'],
  [gameActions.includes("game.activePlayer !== me.playerIndex") && gameActions.includes("game.activePlayer === me.playerIndex") && !gameActions.includes('game.activePlayer !== game.you'), '沙盒双方抵挡、支援和阶段操作必须依据当前代操作玩家而非登录座位'],
  [board.includes('data-ui-contract="combat-substage"') && board.includes('pending.attackValue > 0') && board.includes("pendingDefense?.stage === 'DefenseChoice'") && gameActions.includes("pendingDefense?.stage === 'DefenseChoice'"), '进攻界面必须消费服务端子阶段与冻结进攻值，且只在 DefenseChoice 开放抵挡/支援'],
  [gmPanel.includes("emit('armPlacement'") && gamePage.includes(':gm-placement="gmPlacement"') && board.includes("emit('gmPlacementResolved')") && board.includes('GM：请选择'), 'GM 打出军团必须回到棋盘并点击目标玩家的绿色空位'],
  [playerMat.includes('selectRunePayment') && playerMat.includes('`rune:${index}`') && playerMat.includes('payable: paymentChoiceIds'), '符文支付必须直接点击场上的可用符文，不得恢复编号弹框'],
  [board.includes('boardSlotTargetPlayerIndex') && board.includes('targetPlayerIndex') && playerMat.includes("promptSlotIds?.includes(`${row}:${slot}`)"), '跨阵营位移的目标阵地必须高亮实际被移动军团所在战场，不得回退为操作者自己的同坐标格'],
  [l12Net.includes("send({ type: 'sandboxAction', actingPlayerIndex, command })") && board.includes('controlledPlayerIndex') && board.includes('const viewMe = computed(() => props.game.players[props.game.you])') && board.includes('const viewEnemy = computed(() => props.game.players[1 - props.game.you])') && board.includes('v-if="l12State.gmEnabled" class="opponent-hand" :cards="viewEnemy.hand"') && board.includes(':cards="viewMe.hand"') && board.includes(':controllable="isControlledPlayer(viewEnemy.playerIndex)"') && prompt.includes('sandboxAction(actingPlayerIndex, command)') && globalStyle.includes('.opponent-hand .hand-actions{top:calc(100% + 4px);bottom:auto}'), '沙盒必须固定我方在下、对方在上，不交换棋盘，同时可查看双方手牌并代行双方规则内选择；上方手牌操作按钮必须朝棋盘中心展开而不被裁切'],
  [board.includes(':mine="masterPlayerIndex === controlledPlayerIndex"') && board.includes("sandboxAction(controlledPlayerIndex.value, { type, ...extra })") && prompt.includes('sandboxAction(actingPlayerIndex, command)'), '沙盒代操作对方时必须按受控方索引开放主宰效果并完成后续提示，正式房仍只允许登录座位'],
  [board.includes('watch(() => boardSlotPrompt.value?.promptId, promptId => {') && board.includes('graveyardPlayer.value = null') && board.includes('masterPlayerIndex.value = null'), '墓地主动支付后进入棋盘选位时必须关闭墓地和效果弹框'],
  [graveyardOverlay.includes('function selectCard(card: Card)') && graveyardOverlay.includes("enabledAbilities.length === 1") && !graveyardOverlay.includes('graveyard-abilities'), '墓地主动效果必须点击卡牌本身进入是否发动流程，卡面不得重新覆盖效果文字按钮'],
  [sandboxPicker.includes('cards.s1.json') && sandboxPicker.includes('cards.lookup.json') && sandboxPicker.includes('搜索卡名、编号或效果文字') && sandboxPicker.includes('全部阵营'), '沙盒卡牌选择器必须复用卡牌档案的双卡池搜索与筛选逻辑'],
  [sandbox.includes('<option value="custom">自定天灾（四张始终公开）</option>') && board.includes("type: 'replaceDisaster'") && board.includes('index < 3'), '自定天灾必须四张公开、前三槽可更换且第四槽堙灭锁定'],
  [adminPage.includes('卡效原子化') && adminPage.includes('atom-flow') && adminPage.includes('原子定义 JSON'), '管理后台必须保留卡效原子组合、流程图与原始定义视图'],
  [adminPage.includes('旧实现兜底') && adminPage.includes('新旧实现不会同时结算'), '原子化后台必须明确显示旧实现兜底与防重复结算边界'],
  [platform.includes("effectAtoms: () => platformRequest<EffectAtomDescriptor[]>('/api/admin/effect-atoms')") && platform.includes('/api/admin/effects/coverage'), '卡效后台必须从服务端权威原子注册表读取数据'],
  [adminPage.includes('实战已验证') && adminPage.includes('effectCoverage.verifiedAbilities') && platform.includes('verifiedAbilities: number'), '原子化后台必须区分文本拆分与已接管实战执行的能力'],
  [adminPage.includes('class="effect-scroll"') && adminPage.includes('overflow-y:auto') && adminPage.includes('human-assisted') && adminPage.includes('confirmed'), '原子化能力清单必须可纵向滚动，并区分人工辅助与人工确认状态'],
  [platform.includes('permissions?: string[]') && adminPage.includes("hasPermission('admin.bugs.read')") && adminPage.includes("hasPermission('admin.accounts.read')") && adminPage.includes("hasPermission('admin.operations.read')"), '后台前端入口必须消费服务端权限矩阵，不得只依赖散落角色字符串'],
  [platform.includes('let authRefreshPromise: Promise<PlatformAccount | null> | null = null') && platform.includes("platformRequest<PlatformAccount>('/api/auth/me')") && platform.includes('remember(account, requestToken)') && platform.includes('if (authRefreshPromise) return authRefreshPromise'), '账号初始化与权限刷新必须去重读取 /api/auth/me，并以权威响应覆盖本地缓存'],
  [platform.includes('response.status === 401 && requestToken && platformState.token === requestToken') && platform.includes('forgetAccount(requestToken)') && platform.includes('error instanceof PlatformRequestError && error.status === 401') && platform.includes('throw error'), '任意携带当前令牌的 401 必须按请求令牌防竞态清理，网络与 5xx 则保留令牌并保持未验证'],
  [platform.includes('response.status === 403 && requestToken && platformState.token === requestToken') && platform.includes('authState.verified = false') && platform.includes('refreshCurrentAccount({ force: true })') && platform.includes('if (!authState.verified) return false'), '403 必须使权限 UI 立即失败关闭并触发去重身份刷新，缓存身份不得直接授予权限'],
  [router.includes("meta: { requiresAdmin: true }") && router.includes('router.beforeEach(async to =>') && router.includes('refreshCurrentAccount({ force: true })') && router.includes("return { name: 'me', query: { redirect: to.fullPath } }") && adminPage.includes('await refreshCurrentAccount()') && adminPage.includes('if (!canAccessAdmin.value) return') && adminPage.includes('!authState.initialized || authState.refreshing'), '管理路由与 AdminPage 必须在加载管理数据前刷新权威身份，并在未验证或非管理员时失败关闭'],
  [platform.includes("'/api/auth/sessions/current'") && platform.includes("'/api/auth/sessions'") && profilePage.includes('登录设备与会话') && profilePage.includes('退出其他设备') && profilePage.includes('退出全部设备'), '账号安全页必须支持服务端会话列表、当前设备及全端撤销'],
  [platform.includes("path === '/api/auth/email/verify'") && platform.includes("path === '/api/auth/password/forgot'") && platform.includes("path === '/api/auth/password/reset'") && platform.includes("'/api/auth/email/bind'") && platform.includes("'/api/auth/email/unbind'") && profilePage.includes('邮箱与账号恢复') && profilePage.includes('忘记密码？使用已验证邮箱找回'), '邮箱验证、解绑和密码恢复端点必须完整接入，匿名恢复请求不得携带或清理现有登录令牌'],
  [router.includes("path: '/auth/recovery'") && recoveryPage.includes("location.hash.replace(/^#/, '')") && recoveryPage.includes("history.replaceState(null, '',") && recoveryPage.includes('确认验证邮箱') && !recoveryPage.includes('submitVerification() }'), '恢复页必须从 URL fragment 读取并立即清除令牌，邮箱链接不得自动消费一次性令牌'],
  [platform.includes('resetAccountPassword:') && platform.includes('deleteAccount:') && adminPage.includes('临时密码 123456') && adminPage.includes('删除与清理') && adminPage.includes('根 Admin 与操作者自身受保护'), '管理后台必须提供受保护的临时密码重置、全会话撤销与逻辑删除个人数据清理入口'],
  [platform.includes('mustChangePassword?: boolean') && router.includes('platformState.account.mustChangePassword') && profilePage.includes('必须修改密码'), '管理员重置后的账号必须被导航守卫限制到我的页并明确要求修改临时密码'],
  [platform.includes('options: { revokeServer?: boolean } = {}') && profilePage.includes('logout({ revokeServer: false })'), '服务端已撤销当前或全部会话后必须只清理本机状态，不得用失效令牌重复调用撤销接口'],
  [platform.includes('revokeSession: (id: string, sessionId: string)') && platform.includes('/sessions/${encodeURIComponent(sessionId)}') && adminPage.includes('revokeAccountSessions') && adminPage.includes('撤销会话'), '管理员必须能按账号撤销服务端会话'],
  [platform.includes("headers.set('X-Correlation-ID'") && platform.includes('PlatformRequestError') && adminPage.includes('关联 ID：'), 'HTTP 请求、错误提示与管理审计必须贯通关联 ID'],
  [platform.includes('/api/admin/v1/commands') && platform.includes('/api/admin/v1/approvals') && adminPage.includes('管理操作记录') && adminPage.includes('受控发布待复核') && adminPage.includes('失败：'), '后台必须提供持久命令、受控发布复核、命令详情与失败原因入口'],
  [platform.includes('/api/admin/v1/content/publish') && platform.includes('/api/admin/v1/content/rollback') && adminPage.includes('提交批量发布') && adminPage.includes('提交回滚审批'), '官网内容必须通过服务端批量发布与回滚命令，不得恢复前端逐键发布'],
  [adminPage.includes('预览 / dry-run') && adminPage.includes('发布预览（未写入）') && adminPage.includes('wouldChange'), '内容后台必须展示不写入的发布预览与变化摘要'],
  [adminPage.includes('auditCommandId') && adminPage.includes('auditCorrelationId') && adminPage.includes('auditOutcome'), '审计页必须可按结果、命令 ID 与关联 ID 筛选'],
  [platform.includes("releaseArtifacts: () => platformRequest<VerifiedReleaseArtifact[]>('/api/admin/v1/releases/artifacts')") && !platform.includes('registerReleaseArtifact') && adminPage.includes('Web 端没有注册入口'), '发布后台只能读取适配器提供的已验证工件，不得提供客户端工件注册或自报 verified 入口'],
  [platform.includes('/api/admin/v1/releases/deploy') && platform.includes('/api/admin/v1/releases/rollback') && adminPage.includes('发布 dry-run') && adminPage.includes('提交双人审批') && adminPage.includes('提交回滚审批'), '发布与回滚必须支持 dry-run、环境版本和双人审批入口'],
  [platform.includes("releaseEnvironments: () => platformRequest<ReleaseEnvironment[]>('/api/admin/v1/releases/environments')") && adminPage.includes('运行态只读快照') && adminPage.includes('WebSocket 冒烟') && adminPage.includes('发布、失败与回滚记录'), '运行态必须来自显式只读适配器快照，并展示健康、WS 冒烟、失败和回滚记录'],
  [platform.includes('disabled?: boolean') && platform.includes('/status`, {') && adminPage.includes('账号变更立即执行并完整审计') && adminPage.includes('撤销会话'), '账号禁用/启用必须直接执行、记录版本审计，并提供旧令牌与 WebSocket 会话撤销入口'],
  [platform.includes("securityStatus: () => platformRequest<SecurityStatus>('/api/admin/v1/security/status')") && platform.includes('/api/admin/v1/security/audit-archives') && adminPage.includes('高风险审计') && adminPage.includes('恢复演练'), '后台必须展示安全告警、独立审计归档 dry-run/复核与恢复演练入口'],
  [platform.includes("mfaCapability = () => platformRequest<MfaCapability>('/api/auth/mfa/capability')") && profilePage.includes('MFA：') && profilePage.includes('尚未启用') && profilePage.includes('不会收集或保存 MFA 密钥'), 'MFA 无安全凭据基础时必须明确保持关闭，前端不得伪造注册能力或收集密钥'],
  [adminPage.includes('仅服务器 CLI 可用') && adminPage.includes('发布复核离线恢复') && !platform.includes('bootstrapSecondApprover'), '受控发布恢复只能保留离线命令边界，不得新增 Web 凭据入口'],
  [platform.includes('export const tournamentApi') && platform.includes('/api/tournaments/import-legacy') && platform.includes('/matches/${encodeURIComponent(matchId)}/rulings'), '赛事中心必须通过服务端 API 完成赛事、旧数据导入与裁判写入'],
  [tournamentCenter.includes('预览导入（dry-run）') && tournamentCenter.includes('确认导入') && tournamentCenter.includes('legacyPreview.value.previewHash') && !tournamentCenter.includes('localStorage.setItem'), '本机旧赛事只能显式预览并确认导入，不得继续作为 localStorage 权威状态写回'],
  [tournamentCenter.includes('organizerAccountId === accountId.value') && tournamentCenter.includes('person.accountId===accountId') && tournamentCenter.includes('主办者与裁判权限仅在当前赛事内生效') && !tournamentCenter.includes('待审批命令'), '赛事主办者、裁判临时身份、牌库范围与并发写入必须使用服务端账号 ID 和赛事版本，且不得进入账号角色审批'],
  [playerMat.includes("emit('cardAction', 'freeMove'") && playerMat.includes("unit?.cardId === 'S02-0510' && unit.tapped") && board.includes("mode.value === 'freeMove' ? 'move'"), '希波吕忒休整时必须提供独立的免费前后位移入口，并复用规则内移动命令'],
  [wsSmoke.includes("ws.send(JSON.stringify({ type: 'deploymentProbe' }))") && !wsSmoke.includes("wait(m => m.type === 'session')"), '发布烟雾测试必须先执行无状态 WebSocket 探针，不得恢复为认证前等待 session'],
  [wsServer.includes('"deploymentProbe" =>') && wsServer.includes('protocolVersion = 1') && wsServer.includes('authentication = "token"'), '服务端必须保留无需账号且不写运行数据的发布探针协议'],
  [cacheEnvironment.includes('D:\\GPT\\Legion12\\cache\\primary') && cacheEnvironment.includes('NUGET_PACKAGES') && cacheEnvironment.includes('npm_config_cache') && cacheEnvironment.includes('DOTNET_CLI_HOME') && cacheEnvironment.includes('COREPACK_HOME'), 'Windows 构建必须统一使用可覆盖的 D 盘缓存根目录'],
  [windowsVerify.includes('Initialize-L12BuildEnvironment.ps1') && windowsDeploy.includes('Initialize-L12BuildEnvironment.ps1') && windowsDeploy.includes('"-CacheRoot", $resolvedCacheRoot'), '完整验证和部署子进程必须共用同一缓存根目录'],
  [serverDeploy.includes('readonly public_host="legion-12.com"') && serverDeploy.includes('readonly public_base="https://${public_host}"') && serverDeploy.includes('"wss://${public_host}/ws"') && serverDeploy.includes('域名：${public_host}') && !serverDeploy.includes('wss://legion12.grand-umi.com/ws') && windowsDeploy.includes('[string]$Server = "root@legion-12.com"') && windowsDeploy.includes('发布成功：https://legion-12.com/') && bugQueue.includes('[string]$ApiBase = "https://legion-12.com"'), '主站、SSH、API 与发布后 HTTP/WS 探针必须统一使用 legion-12.com，不得回退旧主站域名'],
  [serverDeploy.includes('backup_runtime') && serverDeploy.includes('restore_runtime_backup') && serverDeploy.includes('runtime-before-${short_commit}-${timestamp}.tar.gz') && serverDeploy.includes('部署前运行数据快照：${runtime_backup}') && serverDeploy.includes('prune_runtime_backups'), '生产发布必须在切换版本前快照持久化运行数据，并在程序回滚时恢复同版本数据且限制备份增长'],
]

const failures = contracts.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  console.error(`UI 契约检查失败：\n- ${failures.join('\n- ')}`)
  process.exit(1)
}

console.log(`UI 契约检查通过（${contracts.length} 项）`)
