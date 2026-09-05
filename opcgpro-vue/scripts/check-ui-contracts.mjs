import { existsSync, readFileSync, readdirSync } from 'node:fs'

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
const promptCardCandidate = read('../src/l12/game/PromptCardCandidate.vue')
const matchRecords = read('../src/l12/MatchRecords.vue')
const replayPage = read('../src/l12/ReplayPage.vue')
const replayModel = read('../src/l12/replayModel.ts')
const gameActions = read('../src/l12/game/GameActions.vue')
const lobby = read('../src/l12/site/BattleHubPage.vue')
const rankings = read('../src/l12/site/RankingsPage.vue')
const rankedTicker = read('../src/l12/site/RankedBroadcastTicker.vue')
const rankedPlayback = read('../src/l12/site/rankedBroadcastPlayback.ts')
const adminOperations = read('../src/l12/site/AdminOperationsPanel.vue')
const disasterPoolPicker = read('../src/l12/site/DisasterPoolPicker.vue')
const savedDeckSelector = read('../src/l12/SavedDeckSelector.vue')
const deckEditor = read('../src/l12/L12DeckEditor.vue')
const gamePage = read('../src/l12/GamePage.vue')
const app = read('../src/App.vue')
const mainEntry = read('../src/main.ts')
const playerMat = read('../src/l12/game/PlayerMat.vue')
const adminIntegrity = read('../src/l12/site/AdminRankedIntegrityPanel.vue')
const graveyardOverlay = read('../src/l12/game/GraveyardOverlay.vue')
const masterOverlay = read('../src/l12/game/MasterOverlay.vue')
const globalBugFeedback = read('../src/l12/site/GlobalBugFeedback.vue')
const specialAssets = read('../src/l12/specialAssets.ts')
const cardTile = read('../src/l12/CardTile.vue')
const l12Types = read('../src/l12/types.ts')
const cardArchive = read('../src/l12/CardArchive.vue')
const cardArchiveVersions = read('../src/l12/cardArchiveVersions.ts')
const galleryMarkup = cardArchive.match(/<template v-else>([\s\S]*?)<div v-if="!filteredGallery\.length"/)?.[1] ?? ''
const sandbox = read('../src/l12/site/SandboxPage.vue')
const gmPanel = read('../src/l12/game/GmPanel.vue')
const sandboxPicker = read('../src/l12/game/SandboxCardPicker.vue')
const l12Net = read('../src/l12/net.ts')
const adminPage = read('../src/l12/site/AdminPage.vue')
const adminArticles = read('../src/l12/site/AdminArticlesPanel.vue')
const adminSiteContent = read('../src/l12/site/AdminSiteContentPanel.vue')
const mediaUploadField = read('../src/l12/site/MediaUploadField.vue')
const articleBlockEditor = read('../src/l12/site/ArticleBlockEditor.vue')
const articleDocumentEditor = read('../src/l12/site/ArticleDocumentEditor.vue')
const articleBlockModel = read('../src/l12/site/articleBlocks.ts')
const articleRenderer = read('../src/l12/site/ArticleContentRenderer.vue')
const adminMatches = read('../src/l12/site/AdminMatchesPanel.vue')
const adminCardAnalytics = read('../src/l12/site/AdminCardAnalyticsPanel.vue')
const friendsPage = read('../src/l12/site/FriendsPage.vue')
const officialHome = read('../src/l12/site/OfficialHomePage.vue')
const newsPage = read('../src/l12/site/NewsPage.vue')
const homeContent = read('../src/l12/site/homeContent.ts')
const profilePage = read('../src/l12/site/ProfilePage.vue')
const recoveryPage = read('../src/l12/site/AccountRecoveryPage.vue')
const ruleCenter = read('../src/l12/site/RuleCenterPage.vue')
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
const siteContentStore = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.SiteContent.cs')
const articleStore = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.Articles.cs')
const l12PromptModel = read('../../服务端WebSocket/TwelveLegions/Models.cs')
const l12PromptSetup = read('../../服务端WebSocket/TwelveLegions/L12PromptsAndSetup.cs')
const l12GameEngine = read('../../服务端WebSocket/TwelveLegions/L12GameEngine.cs')
const l12StructuredSemantics = read('../../服务端WebSocket/TwelveLegions/L12StructuredCardRules.StatusSemantics.cs')
const l12ServerDirectory = new URL('../../服务端WebSocket/TwelveLegions/', import.meta.url)
const l12ServerSources = readdirSync(l12ServerDirectory, { withFileTypes: true })
  .filter(entry => entry.isFile() && entry.name.endsWith('.cs'))
  .map(entry => readFileSync(new URL(entry.name, l12ServerDirectory), 'utf8').replace(/\r\n?/g, '\n'))
  .join('\n')
const internalModeChoices = [...new Set([...l12ServerSources.matchAll(/"(mode:[a-z0-9_-]+)"/g)].map(match => match[1]))]
const centralModeChoiceLabels = new Map([...l12PromptSetup.matchAll(/\["(mode:[a-z0-9_-]+)"\]\s*=\s*"([^"]+)"/g)]
  .map(match => [match[1], match[2]]))
const playerChoiceLabelLiterals = [...l12ServerSources.matchAll(/\["(?:mode:[a-z0-9_-]+|yes|no|skip)"\]\s*=\s*"([^"]+)"/g)]
  .map(match => match[1])
const inventedChoiceLabel = /(?:普通模式|强模式|选择效果模式|追加第二段效果|发动追加效果|不发动追加效果|只(?:结算|执行)|追加消耗|并强化军团|抽牌段)/
const cacheEnvironment = read('../../ops/windows/Initialize-L12BuildEnvironment.ps1')
const windowsVerify = read('../../ops/windows/verify-l12.ps1')
const windowsDeploy = read('../../ops/windows/deploy-l12.ps1')
const serverDeploy = read('../../ops/server/deploy-l12-release.sh')
const nginxSite = read('../../ops/server/legion12-testrun.nginx')
const nginxHttpSite = read('../../ops/server/legion12-testrun-http.nginx')
const bugQueue = read('../../ops/windows/Get-L12BugQueue.ps1')
const s1Cards = JSON.parse(read('../public/data/l12/cards.s1.json'))
const starterCards = JSON.parse(read('../public/data/l12/cards.st.json'))
const s2Cards = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/cards.s2.json'))
const archiveAssetCards = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/card-archive-assets.json')).cards
const presentationGalleryAssets = archiveAssetCards.filter(card => card.id !== 'S02-05C1B'
  && [/^S\d{2}-\d{4}[a-z]$/, /^S\d{2}-\d{2}[CM]1A$/, /^ST\d{2}-C1st$/].some(pattern => pattern.test(card.id)))
const moraleIdentities = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/morale-identities.json'))
const olympusDefaultMorale = s2Cards.find(card => card.id === 'S02-05C1')
const olympusAlternateArt = s2Cards.find(card => card.id === 'S02-05C1A')

const confirmedS1DisasterLevels = {
  'S01-0304': 2,
  'S01-0305': 1,
  'S01-0306': 1,
  'S01-0307': 1,
  'S01-0308': 1,
  'S01-0406': 1,
}
const siteWorkbenchSources = [adminSiteContent, adminArticles, mediaUploadField, articleDocumentEditor]
const hasUndersizedSiteWorkbenchText = siteWorkbenchSources.some(source =>
  /font-size\s*:\s*(?:[0-9]|1[01])px|font\s*:[^;{}]*\s(?:[0-9]|1[01])px/.test(source))

const contracts = [
  [moraleIdentities.length === 6
    && moraleIdentities.every(identity => identity.displayName.startsWith('士气·') && !identity.canonicalCardId.startsWith('ST'))
    && moraleIdentities.find(identity => identity.faction === 'olympus')?.canonicalCardId === 'S02-05C1'
    && moraleIdentities.find(identity => identity.faction === 'olympus')?.godPowerCardId === 'S02-05C1'
    && moraleIdentities.find(identity => identity.faction === 'olympus')?.godPowerDisplayNumber === 'S02-05C1(B)'
    && moraleIdentities.find(identity => identity.faction === 'otherworld')?.canonicalCardId === 'S02-06C1'
    && decks.includes('morale-identities.json') && decks.includes('canonicalMoraleCardId')
    && cardArchive.includes('loadCardArchiveCatalog') && sandboxPicker.includes('loadDeckCatalog'), '所有页面与运行时必须共用权威阵营士气身份，ST版本不得抢占基准，奥林匹斯普通士气与神力反面必须保持分离'],
  [decks.includes('export async function loadCardArchiveCatalog()') && decks.includes('productInclusionsByCardId.has(card.cardNo)')
    && decks.includes('cardArchiveAssets.forEach') && !decks.includes("if (card.id === 'S02-05C1') return 'S02-05C1(B)'")
    && decks.includes('const catalogById = new Map(catalog.map(card => [card.id, card]))')
    && decks.includes('const base = byId.get(asset.baseCardId) ?? catalogById.get(asset.baseCardId)')
    && decks.includes('archiveBaseCardId: asset.baseCardId')
    && cardArchive.includes('groupArchiveCards(cards.value)') && cardArchive.includes('entry.versions.some(card =>')
    && cardArchive.includes('selectedProducts') && cardArchive.includes('收录产品')
    && cardArchive.includes('page.value === \'gallery\' ? galleryCards.value.length : logicalCards.value.length')
    && !cardArchive.includes('/ {{ cards.length }} 张'), '卡牌图鉴必须按权威收录产品载入展示用异画、区分奥林匹斯士气与神力编号、按任一版本搜索筛选，并按当前子页口径计数'],
  [cardArchiveVersions.includes('compareArchiveVersions') && cardArchiveVersions.includes('Number(Boolean(a.archiveBaseCardId)) - Number(Boolean(b.archiveBaseCardId))')
    && cardArchiveVersions.includes('productRank(a.product)')
    && cardArchiveVersions.includes('rarityValue(a.rarity)') && cardArchiveVersions.includes("if (!rarity?.trim()) return 100")
    && cardArchiveVersions.includes('defaultVersion: versions[0]'), '逻辑卡默认版本必须先选无异画基底的规则卡，再稳定按最早产品、最低已知罕贵度及卡号排序；异画不得抢占默认卡图'],
  [cardArchive.includes('class="archive-card-image"') && cardArchive.includes('class="archive-version-arrow previous"')
    && cardArchive.includes('class="archive-version-arrow next"') && cardArchive.includes('@click.stop="cycleVersion(entry, -1)"')
    && cardArchive.includes('@click.stop="cycleVersion(entry, 1)"') && cardArchive.includes(':card-id="displayedVersion(entry).id"')
    && cardArchive.includes(':card-id="selected.id"') && !cardArchive.includes('<div class="archive-detail-image" :class="{ horizontal: isHorizontalCardType(selected.cardType) }">\n          <CardImage :card-id="selected.id" :legacy-url="selected.imageUrl" :alt="selected.nameZh" intent="detail" eager/>\n          <template')
    && globalStyle.includes('.archive-version-arrow{') && globalStyle.includes('background:transparent'), '卡牌图鉴必须在中间结果卡图上以左右透明三角切换版本，并同步更新卡位与详情；详情区不得保留第二套切换按钮'],
  [presentationGalleryAssets.length === 37
    && presentationGalleryAssets.every(card => card.baseCardId && card.id !== 'S02-05C1B')
    && s2Cards.some(card => card.id === 'S02-05C1A')
    && cardArchive.includes("card.id !== 'S02-05C1B'")
    && cardArchive.includes("Boolean(card.archiveBaseCardId) || card.id === 'S02-05C1A'")
    && cardArchive.includes('cards.value.filter(isGalleryVariant)')
    && cardArchive.includes('v-for="card in filteredGallery" :key="card.id"')
    && !galleryMarkup.includes('archive-version-arrow'), '画廊必须逐卡展示37张登记展示资源与S02-05C1A，共38张异画；S02-05C1A在图鉴并入默认士气S02-05C1的版本切换、在画廊独立展示，不得成为新规则身份，也不得误收规则独立的奥林匹斯神力B面'],
  [(cardArchive.match(/@dblclick\.stop="openDetail/g) ?? []).length === 2
    && (cardArchive.match(/class="archive-image-open"/g) ?? []).length === 2
    && !cardArchive.includes('<button class="archive-image-open"')
    && !cardArchive.includes('class="archive-card-image" role="button"')
    && (cardArchive.match(/@keydown\.enter\.prevent="select[^\"]+openDetail/g) ?? []).length === 2
    && (cardArchive.match(/@keydown\.space\.prevent="select/g) ?? []).length === 2
    && (cardArchive.match(/@keydown\.enter\.stop @keydown\.space\.stop/g) ?? []).length === 2
    && cardArchive.includes('role="dialog" aria-modal="true" aria-labelledby="archive-modal-title"')
    && cardArchive.includes('@click.self="closeDetail"') && cardArchive.includes("event.key === 'Escape'")
    && cardArchive.includes('aria-label="关闭卡牌详情"') && cardArchive.includes('modalCloseButton.value?.focus()')
    && cardArchive.includes('modalTrigger?.focus()') && cardArchive.includes('hasCostDimension(modalCard)')
    && cardArchive.includes('modalCard.troops !== undefined') && cardArchive.includes('modalCard.hp !== undefined')
    && cardArchive.includes('modalCard.disasterLevel !== undefined') && cardArchive.includes('modalCard.trialValue !== undefined')
    && cardArchive.includes('<p class="l12-effect-body">{{ modalCard.effect')
    && cardArchive.includes('modalVersions.value = versions.length > 1 ? [...versions] : []')
    && cardArchive.includes('class="archive-modal-version-arrow previous"')
    && cardArchive.includes('class="archive-modal-version-arrow next"')
    && cardArchive.includes('@click="cycleModalVersion(-1)"') && cardArchive.includes('@click="cycleModalVersion(1)"')
    && globalStyle.includes('.archive-modal{') && globalStyle.includes('.archive-modal-image.horizontal{')
    && globalStyle.includes('aspect-ratio:8/5'), '全卡池与画廊必须支持双击卡图或聚焦卡位按Enter打开同一L12详情模态框；全卡池多版本大图保留左右三角并同步全部详情，Space只选择且版本箭头不得冒泡误开；详情按卡类显示真实维度、保留效果换行和横版方向，并支持关闭、遮罩、Esc与焦点恢复'],
  [cardArchive.includes('<h1>卡牌图鉴</h1>') && legacyLobby.includes('<h2>卡牌图鉴</h2>')
    && cardArchive.includes('aria-label="卡牌图鉴子页"') && cardArchive.includes('>全卡池</button>') && cardArchive.includes('>画廊</button>')
    && ![cardArchive, legacyLobby, sandboxPicker, gmPanel, adminCardAnalytics, ruleCenter].some(source => source.includes('卡牌档案')), '全站用户可见名称必须统一为“卡牌图鉴”，主标题保持“卡牌图鉴”，子页签必须为“全卡池／画廊”，不得残留旧称“卡牌档案”'],
  [decks.includes('function normalizeCardDimensions(card: DeckCard)')
    && decks.includes("card.cardType === 'master'") && decks.includes('cost: undefined, troops: undefined')
    && cardArchive.includes('function hasCostDimension(card: CatalogCard)')
    && cardArchive.includes('hasCostDimension(displayedVersion(entry))')
    && cardArchive.includes('hasCostDimension(selected)'), '主宰只有血量维度；卡牌图鉴、筛选与详情不得把错误源数据中的数值展示为费用或兵力'],
  [cardArchiveVersions.includes('identity.versionCardIds.map')
    && cardArchiveVersions.includes("if (card.id === 'S02-05C1B') return `rules:${ruleIdentity(card)}`")
    && moraleIdentities.find(identity => identity.faction === 'olympus')?.versionCardIds.includes('S02-05C1')
    && moraleIdentities.find(identity => identity.faction === 'olympus')?.versionCardIds.includes('S02-05C1A')
    && olympusDefaultMorale?.nameZh === '士气·奥林匹斯' && olympusAlternateArt?.nameZh === '士气·奥林匹斯'
    && olympusDefaultMorale.product === olympusAlternateArt.product
    && olympusDefaultMorale.number.localeCompare(olympusAlternateArt.number, 'zh-CN', { numeric: true }) < 0
    && cardArchiveVersions.includes('defaultVersion: versions[0]'), 'S02-05C1必须是奥林匹斯士气组默认版本，S02-05C1A只作同组异画版本并在画廊独立展示，不得形成新规则或构筑身份'],
  [indexHtml.includes('<link rel="icon" type="image/png" href="/favicon.png" />') && existsSync(faviconPath), '网页标签必须使用项目提供的 Logo-Mini PNG，不得回退默认 Vite 图标'],
  [board.includes("import ActionPresentationLayer from './ActionPresentationLayer.vue'") && board.includes('<ActionPresentationLayer :events="game.recentEvents ?? []"') && actionLayer.includes('data-ui-contract="authoritative-action-presentation"'), 'L12 对局必须消费服务端 recentEvents 播放统一阶段变化条'],
  [actionPresentation.includes("event.type === 'turn-start'") && actionPresentation.includes("event.text === '进入主要阶段'") && actionPresentation.includes("event.text === '执行结束阶段'") && !actionPresentation.includes("执行抽牌阶段") && !actionPresentation.includes("执行士气阶段") && !actionPresentation.includes("event.type === 'play'") && !actionPresentation.includes("event.type === 'attack'"), '通用动作条只能呈现回合开始、主要阶段和回合结束，不得承载其他阶段或卡牌动作'],
  [board.includes("import ZoneMovementPresentationLayer from './ZoneMovementPresentationLayer.vue'") && board.includes('<ZoneMovementPresentationLayer :events="game.recentEvents ?? []"') && zoneMovementLayer.includes('data-ui-contract="authoritative-zone-card-movement"') && zoneMovementLayer.includes('getBoundingClientRect()') && zoneMovementLayer.includes('data-l12-game-stage') && zoneMovementLayer.includes('data-card-instance-id') && zoneMovementLayer.includes('await nextTick()'), '打出、登场与区域移动必须读取更新前后真实 DOM 锚点并由独立卡牌实体连续飞行'],
  [zoneMovementLayer.includes('source.cloneNode(true)') && zoneMovementLayer.includes('wrapper.animate([') && !zoneMovementLayer.includes('--move-mid-x') && !zoneMovementLayer.includes('l12-zone-card-pulse'), '卡牌跨区域动画必须复用来源实体快照并做单次起终点位移，不得恢复中途放大、脉冲或三段跳变'],
  [zoneMovementLayer.includes('prepareMovementImage') && zoneMovementLayer.includes('resolveCardAssetUrls') && zoneMovementLayer.includes('preparedImageUrl') && zoneMovementLayer.includes(".filter(url => url !== CARD_IMAGE_PLACEHOLDER)"), '无来源实体的区域移动必须在动画前预解码真实卡图，XII占位只能作为全部候选失败后的最终兜底'],
  [board.includes("import CombatMotionPresentationLayer from './CombatMotionPresentationLayer.vue'") && board.includes('<CombatMotionPresentationLayer :events="game.recentEvents ?? []"') && combatMotionLayer.includes("event.type === 'attack'") && combatMotionLayer.includes("event.type === 'combat'") && combatMotionLayer.includes("badge.textContent = '0'") && combatMotionLayer.includes("zoneElement('graveyard', owner)"), '进攻必须有轻量前冲，战斗伤害后阵亡军团必须先显示兵力0再进入所有者墓地'],
  [zoneMovementLayer.includes("event.type === 'play'") && zoneMovementLayer.includes("event.type === 'put' || event.type === 'enter'") && zoneMovementLayer.includes("event.type === 'move'") && zoneMovementLayer.includes("event.type === 'counter-set'") && zoneMovementLayer.includes('/assets/l12/card-back-official.png') && zoneMovementLayer.includes('covered: card?.hidden === true') && !zoneMovementLayer.includes("event.type === 'counter-set' ||"), '跨区域动画必须覆盖打出、登场、位移和盖伏；未知身份使用卡背，拥有者已知盖伏卡仍显示灰置卡面'],
  [zoneMovementLayer.includes("if (!card || event.type === 'counter-set') return true") && zoneMovementLayer.includes("if (!card.cardId || card.cardId === 'hidden-card') return true") && zoneMovementLayer.includes('return card.hidden === true && card.identityKnown !== true') && zoneMovementLayer.includes('const concealed = isMovementIdentityConcealed(event, card)') && !zoneMovementLayer.includes('const concealed = !card || card.identityKnown === false'), '公开打出的正面卡在全程跨区移动中不得因 identityKnown 默认值回退卡背；无卡身份、占位卡或真正未知盖伏卡仍必须保持卡背'],
  [actionLayer.includes('lastSequence = highest') && zoneMovementLayer.includes('lastSequence = highest') && zoneMovementLayer.includes('item.sequence > lastSequence') && board.includes(':paused="passivePresentationPaused"'), '阶段条与跨区域卡牌动画必须以首次事件序列建立基线，并在既有展示/掷骰动画播放时排队'],
  [actionAudio.includes('createOscillator()') && actionAudio.includes('useAudioStore.getState()') && !actionAudio.includes('playBgm') && !actionAudio.includes('new Audio('), '基础动作音效必须使用本地程序化短音效、遵循音效设置且不得增加 BGM 或远程音频依赖'],
  [actionLayer.includes('pointer-events:none') && actionLayer.includes('@media(prefers-reduced-motion:reduce)') && !actionLayer.includes('action-mask'), '基础动作动画必须无蒙版、不可阻塞操作并遵循减少动态效果偏好'],
  [gamePage.includes("import OsirisVictorySequence from './game/OsirisVictorySequence.vue'") && gamePage.includes("item.type === 'special-victory'") && gamePage.includes("card.cardId === 'S01-02M2'") && gamePage.includes("game.phase === 'GameOver' && !osirisSequencePlaying") && osirisVictory.includes('data-ui-contract="osiris-special-victory-sequence"') && osirisVictory.includes("['S01-0216', 'S01-0217', 'S01-0218', 'S01-0219', 'S01-0220']") && osirisVictory.includes('<CardImage :card-id="cardId"') && osirisVictory.includes('points="50,92 20,18 92,56 8,56 80,18 50,92"') && osirisVictory.includes('setTimeout(() => emit(\'complete\'), 7000)') && osirisVictory.includes('card-id="S01-02M2"'), '奥西里斯特殊胜利必须以金色七秒序列沿倒五角星路径显示五张完整圣物卡，再显示中央奥西里斯与闪光'],
  [board.includes('function activateOsirisVictory()') && board.includes("command('activateAbility', { cardInstanceId: osiris.instanceId, ability: 'isisVictory' })") && board.includes("if (ability === 'isisVictory')") && board.includes('graveyardPlayer.value = null') && board.includes('masterPlayerIndex.value = null') && board.includes('focusCard.value = null') && playerMat.includes('canActivateOsiris') && playerMat.includes("emit('ability', masterCard, 'isisVictory')") && graveyardOverlay.includes("emit('ability', card, 'isisVictory')"), '点击伊西斯效果、五个卡诺匹斯区域或墓地奥西里斯必须共用同一权威奥西里斯实例命令与可用性，并在发送前关闭墓地、主宰弹框和详情'],
  [gamePage.includes('lastOsirisVictorySequence') && gamePage.includes('osirisSequenceMatchId') && gamePage.includes('lastOsirisVictorySequence = highest') && gamePage.includes('lastOsirisVictorySequence = Math.max(lastOsirisVictorySequence, highest)') && gamePage.includes('event.sequence > lastOsirisVictorySequence') && gamePage.includes('osirisSequenceKey.value =') && !gamePage.includes("const osirisSequenceKey = computed(() =>"), '奥西里斯胜利事件必须按 matchId+sequence 只消费一次，首快照只建立基线，重连或事件窗口截断后不得倒退序号并重播'],
  [osirisVictory.includes('playL12OsirisVictorySound()') && actionAudio.includes('playL12OsirisVictorySound') && actionAudio.includes('[196, 220, 247, 294, 330]') && actionAudio.includes('isMuted || sfxVolume <= 0'), '奥西里斯特殊胜利必须使用本地轻量专属音效并遵循静音与音效音量设置'],
  [Object.entries(confirmedS1DisasterLevels).every(([id, level]) => s1Cards.find(card => card.id === id)?.disasterLevel === level), '第一季补充天灾等级必须进入前端卡牌目录'],
  [shell.includes("const siteBrandIcon = '/favicon.png'") && shell.includes('filter:brightness(0) invert(1)'), '主页入口必须复用标签页Logo并以白色显示'],
  [indexHtml.includes('<title>十二军团</title>') && !indexHtml.includes('十二军团 · 联网对战'), '网页标题必须统一为十二军团'],
  [playerMat.includes('data-ui-contract="ranked-player-clock"') && playerMat.includes('总时') && playerMat.includes('本次') && playerMat.includes('重连'), '排位双方玩家区域必须显示总操作、本次操作和掉线重连倒计时'],
  [adminIntegrity.includes('data-ui-contract="ranked-integrity-review"') && adminIntegrity.includes('不自动扣减七曜') && adminIntegrity.includes('建议人工核对'), '防刷分信号必须只进入管理员人工复核，不得自动惩罚正常重复对局'],
  [shell.includes('friendApi.request(player.accountId)') && shell.includes('inviteFriend(player.accountId)') && shell.includes('spectateRoom(player.roomCode)') && shell.includes("player.activity === 'playing'") && shell.includes(':disabled="!player.canSpectate"'), '在线玩家窗口必须支持直接添加好友、邀请空闲好友，并将对局中玩家替换为带权限原因的观战入口'],
  [shell.includes('friendApi.resolve(player.accountId, accept)') && shell.includes("player.friendDirection === 'incoming'") && shell.includes('resolveOnlineFriend(player, false)') && shell.includes('resolveOnlineFriend(player, true)') && shell.includes('>拒绝</button>') && shell.includes("'接受'"), '在线玩家窗口必须允许直接接受或拒绝收到的好友申请，不能只显示待处理状态'],
  [!shell.includes('/assets/l12/card-back-navy.png'), '主页入口不得回退为卡背'],
  [shell.includes("{ to: '/battle', icon: 'battle', label: '大厅' }") && !shell.includes("label: '对战主页'") && router.includes("{ path: '/battle', name: 'battle', component: () => import('@/l12/site/BattleHubPage.vue')") && router.includes("{ path: '/battle/lobby', redirect: '/battle' }"), '对战区域必须直接以大厅为主页，不得恢复多余的对战主页层级'],
  [l12Net.includes("matchFound: null as null") && l12Net.includes("type: 'syncState'")
    && l12Net.includes('if (!message.queued && l12State.matchFound) return')
    && l12Net.includes('requestMatchedState(socket)') && l12Net.includes('l12State.matchFound = null')
    && lobby.includes('data-ui-contract="match-found-state-recovery"')
    && wsServer.includes('ConcurrentDictionary<Guid, SemaphoreSlim> _socketSendGates')
    && wsServer.includes('await gate.WaitAsync(cancellationToken)') && wsServer.includes('"syncState" =>')
    && l12ServerSources.includes('session.RoomCode is not null')
    && l12ServerSources.includes('return RecoveryStateAsync(sessionId);'), '排位与休闲匹配成功后必须进入显式建局加载态，忽略迟到的未排队消息并主动恢复房间/对局快照；服务端发送必须按会话串行化'],
  [board.includes('Array.from({ length: 4 }'), '本局天灾必须固定为四个槽位'],
  [board.includes('data-ui-contract="persistent-board-safe-layout"') && board.includes('data-ui-contract="phase-safe-track"') && board.includes('--l12-board-seam-safe-height:76px') && board.includes('grid-template-rows:minmax(272px,1fr) var(--l12-board-seam-safe-height) minmax(272px,1fr)') && board.includes('class="battlefield-half opponent-half"') && board.includes('class="battlefield-half my-half"'), '双方战场与中央阶段栏必须使用明确三轨安全布局，常驻 UI 不得依赖绝对定位互相覆盖'],
  [board.includes('.battlefield-half::before') && board.includes('.battlefield-half.my-half::before{bottom:-10px;')
    && board.includes('box-sizing:border-box;width:100%') && board.includes('.battlefield-half.my-half{grid-row:3}'), '双方战场外框必须覆盖各自完整战场轨道；仅向下延展我方外框，不得缩入我方后排或挤压其他常驻UI'],
  [l12Types.includes("'lock' | 'power-up' | 'power-down' | 'disabled' | 'shield' | 'discard-end' | 'extra-attack'") && l12Types.includes('statusIcons?: string[]') && l12Types.includes('statusEffects?: CardStatusEffect[]'), '卡牌投影视图必须提供结构化 statusEffects/statusIcons 状态契约并兼容旧快照缺省'],
  [cardTile.includes('props.card.statusEffects ?? []') && cardTile.includes('props.card.statusIcons ?? []') && cardTile.includes('statusLabel(effect, kind)') && cardTile.includes(':title="status.label"') && cardTile.includes(':aria-label="status.label"') && cardTile.includes('card-status-icons') && cardTile.includes('has-status-effects .card-keyword-stack') && !cardTile.includes('modifier.costDelta'), '卡牌状态图标必须按结构化状态渲染准确提示、避让关键词，费用修正不得重复为状态图标'],
  [board.split("&& !(mode === 'attack' && selectedId)").length - 1 === 2 && playerMat.includes("targetable: Boolean(player.field[row][slot])") && playerMat.includes("source: isSelected(player.field[row][slot]?.instanceId)"), '进攻选目标时不得高亮整块玩家区域，只能标记进攻者与合法或已选目标'],
  [globalStyle.includes('.battle-zone{position:relative}.battle-zone>.morale-rail{position:absolute') && globalStyle.includes('.l12-player-mat.side-opponent .battle-zone>.morale-rail{top:3px}') && globalStyle.includes('.l12-player-mat.side-my .battle-zone>.morale-rail{bottom:3px}'), '士气条必须脱离战场纵向占位并固定在主宰侧通道，不得挤压战场后侵入阶段安全轨道'],
  [board.includes('border-radius:50%') && board.includes('.session-disaster-strip'), '本局天灾必须保持圆形缩略图'],
  [board.includes('<Teleport to="body" :disabled="!modalInspectorVisible">'), '弹框期间必须复用原选中卡牌详情框'],
  [!board.includes('class="modal-card-inspector"'), '不得重新引入第二套弹框卡牌详情'],
  [!board.includes('<CardTile'), '卡牌详情不得渲染战场角标 UI'],
  [!prompt.includes('class="prompt-card-inspector"') && !prompt.includes('class="prompt-card-detail"'), 'PromptOverlay 不得自建卡牌详情框'],
  [prompt.includes('<section v-if="minimized"') && prompt.includes('<section v-else-if="prompt"') && prompt.includes('minimizedChange'), 'Prompt 最小化后必须只保留展开条，并同步隐藏浮动卡牌详情'],
  [prompt.includes('.l12-prompt-overlay,.l12-prompt-overlay.minimized{z-index:3000!important}') && prompt.includes('@media(max-width:760px){.l12-prompt-overlay.minimized{right:10px;bottom:60px}') && masterOverlay.includes('.master-overlay.minimized{z-index:2000;inset:auto 16px 66px auto') && playerMat.includes('.faction-effect-overlay.minimized{z-index:2000;inset:auto 16px 66px auto') && globalBugFeedback.includes('.bug-feedback-trigger{position:fixed;z-index:1900;right:16px;bottom:16px'), '所有最小化展开入口必须在桌面与小屏幕避开全局 Bug 反馈按钮，可操作 Prompt 入口还必须高于被动展示层'],
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
  [lobby.includes('joinMatchmaking') && lobby.includes('七曜值') && lobby.includes('选择本赛季派系') && l12Net.includes("type: 'joinMatchmaking'") && l12Net.includes("type: 'pollMatchmaking'") && l12Net.includes("message.type === 'matchmakingRejected'") && l12Net.includes('startMatchmakingPolling()'), '公开匹配必须使用服务端权威队列、保留等待扩圈轮询并清理拒绝状态，在排位前选择赛季派系'],
  [lobby.includes('data-ui-contract="faction-totals-above-public-match"') && lobby.indexOf('data-ui-contract="faction-totals-above-public-match"') < lobby.indexOf('<section v-if="tab === \'match\'" class="mode-panel panel">'), '三派系七曜总量必须位于顶部模式标签之后、公开匹配面板之前，不能埋在公开匹配内容框内'],
  [rankings.includes("type RankingTab = 'players' | 'masters' | 'matchups' | 'history'") && rankings.includes('主宰对阵一览') && rankings.includes('历史荣誉') && rankings.includes('row.titles') && rankings.includes('title-badge') && rankings.includes('champion-title') && adminOperations.includes('最高段位第一名称号') && adminOperations.includes('主宰最强玩家称号') && adminOperations.includes('rankedConfig.masterTitles'), '派系前五称号必须标明最高段位门槛，排行榜必须支持玩家榜、主宰榜、对阵一览、历史荣誉及醒目的多称号展示，后台必须支持逐主宰最强玩家称号'],
  [disasterPoolPicker.includes('data-ui-contract="landscape-disaster-pool-picker"')
    && disasterPoolPicker.includes('class="pool-card-art"')
    && disasterPoolPicker.includes('intent="detail" fit="contain"')
    && disasterPoolPicker.includes('.pool-card-art{display:block;width:100%;aspect-ratio:8/5;overflow:hidden')
    && disasterPoolPicker.includes('class="pool-card-copy"')
    && disasterPoolPicker.includes('.pool-card-copy b,.pool-card-copy small{position:static')
    && !disasterPoolPicker.includes('intent="thumb"')
    && adminOperations.includes('<DisasterPoolPicker v-model="form.disasterPool.cardIds"')
    && tournamentCenter.includes('<DisasterPoolPicker v-model="form.disasterCardIds"'), '赛季与赛事天灾池必须复用公共选择器，以不旋转、不裁切的8:5横版完整卡图展示，并将卡名和卡号置于卡图外的独立信息区'],
  [lobby.includes('class="ranked-rules-button"') && lobby.includes('>排位规则</button>') && lobby.includes('rankedRulesOpen') && lobby.includes('七曜值') && lobby.includes('段位与派系称号') && lobby.includes('最强主宰规则') && lobby.includes('时间限制') && lobby.includes('本赛季天灾') && lobby.includes('本赛季禁限卡'), '大厅排位区必须提供排位规则按钮，并集中说明七曜、派系段位称号、最强主宰、时限和赛季天灾/禁限卡'],
  [lobby.includes('.ranked-rules-modal{grid-template-rows:auto minmax(0,1fr) auto') && lobby.includes('.ranked-rules-scroll{min-height:0;align-content:start;overflow-x:hidden;overflow-y:scroll'), '排位规则正文必须拥有独立纵向滚动区，在小视口中也能阅读全部内容'],
  [shell.includes('<router-link class="site-brand" to="/"') && !shell.includes('<span>LEGION 12</span>') && !shell.includes('<small>十二军团</small>') && shell.includes('.site-brand img{width:44px;height:44px;border:0;border-radius:0;object-fit:contain'), '主页侧栏品牌区必须只显示无外框的白色 Logo-Mini，不得附带中英文文字'],
  [globalBugFeedback.includes('v-model="form.bugDescription"') && globalBugFeedback.includes('v-model="form.suggestion"') && globalBugFeedback.includes('if (!bugDescription && !suggestion)') && globalBugFeedback.includes('提及卡牌的时候请勿使用俗称，最好使用卡牌编号（例：S01-0001）') && globalBugFeedback.includes('描述你希望优化的Bug、操作体验或界面效果'), '全局反馈必须分为 Bug 提交与优化建议，任填一项即可提交并保留明确填写提示'],
  [profilePage.includes('class="title-manager"') && profilePage.includes('ranked.profile.masterTitles') && profilePage.includes('saveRankedTitle') && platform.includes("'/api/ranked/title'") && board.includes('playerBadges') && board.includes('enemyBadge.rankLabel') && board.includes('myBadge.masterTitle'), '个人页必须可选择已获得的最强主宰称号，对战右上玩家框须显示权威段位/派系排名称号与所选主宰称号'],
  [board.includes("choiceMode === 'mixed-board-payment'") && board.includes("? '确认费用' : '确认发动'") && board.includes('lockedChoices'), '混合场面费用必须在同一场面直选条选择，唯一资源自动锁定且与弃置对象一并确认'],
  [rankedTicker.includes('@animationend="complete"') && rankedTicker.includes('animation:ranked-message-once 16s linear 1 both') && rankedPlayback.includes('claimNextRankedBroadcast') && rankedPlayback.includes('completeCurrentRankedBroadcast') && rankedPlayback.includes('accountId'), '排位广播必须按账号领取，完整播放一次后确认，不得在页面内循环重播同一消息'],
  [board.includes('selected-card-inspector-anchor') && board.includes(':style="modalInspectorVisible ? inspectorFloatStyle : undefined"'), '弹框期间详情必须由原选中卡牌框锚点定位'],
  [!board.includes('.modal-card-inspector') && !prompt.includes('.prompt-card-inspector'), '不得保留第二套弹框详情框样式'],
  [deckEditor.includes("deck.name === activeDeckName") && deckEditor.includes('.saved-list b{color:#f1eee5}') && deckEditor.includes('.saved-list span{color:#aab4b0}') && deckEditor.includes('.saved-list article.active{border-color:#86e8ee;background:#123e42'), '牌库编辑器左下牌库列表及当前牌库状态必须保持高对比'],
  [board.includes('card.playCost ?? card.currentCost ?? card.cost'), '手牌可打出校验必须使用服务端动态费用'],
  [board.includes('class="event-message"') && board.includes('overflow-wrap:anywhere'), '对局记录必须使用可换行的独立消息容器'],
  [board.includes('<Teleport to="body">') && board.includes('public-card-reveal-animation') && board.includes('.public-reveal-animation{z-index:903}') && board.includes("event.type === 'effect-trigger'") && board.includes("event.type === 'effect-response'") && board.includes("event.type === 'effect-activation'") && board.includes("event.type === 'reveal'") && board.includes("event.playerIndex !== props.game.you") && board.includes("event.type === 'effect-trigger' && /展示|公开/.test(event.text)") && board.includes("event.type === 'search' && /展示|加入手牌/") && board.includes('text: publicRevealText(event)') && board.includes('event.effectText?.trim() || event.text.trim()') && board.includes('花魁的馈赠将〈${card.name}〉加入手牌') && board.includes('}, 3000)') && !board.includes('reveal-confirm') && !board.includes('public-reveal-mask'), '公开展示、检索加入手牌、触发、响应与发动效果必须只向非发动方播放三秒无蒙版非阻塞动画；只呈现事件单条效果文本和涉及卡图，花魁的馈赠必须明确展示加入手牌的卡名'],
  [prompt.includes("const usesDetailCardImages = computed(() => isDisasterChoice.value || isInfoConfirm.value)") && prompt.includes(":intent=\"usesDetailCardImages ? 'detail' : 'thumb'\"") && prompt.split(":alt=\"entry.card.name || '天灾'\" intent=\"detail\"").length - 1 === 2 && prompt.includes("'disaster-choice': isDisasterChoice"), '公开天灾禁选、随机公开、触发确认及已公开历史必须请求详情级高清图，不得使用缩略图源'],
  [board.includes(':inspector-visible="modalInspectorVisible"') && prompt.includes("'inspector-active': inspectorVisible") && prompt.includes('--inspector-safe-lane:clamp(118px,19vw,258px)') && prompt.includes('@media(max-width:520px)') && board.includes("transform: 'none'") && board.includes('overflow:auto!important'), '弹框期间原选中详情必须固定侧置并为核心弹框保留安全区，在窄屏与缩放下也不得互相遮挡'],
  [board.includes("event.type === 'disaster-reveal'") && board.includes("event.playerIndex === null") && board.includes("'disaster-reveal': '天灾'") && board.includes("'effect-response': '响应'") && board.includes("'effect-activation': '发动'"), '天灾必须向双方播放，响应与发动动画必须进入可读日志'],
  [board.includes('data-ui-contract="dice-event-animation"') && board.includes("event.type === 'dice'") && board.includes("dice: '掷骰'") && board.includes('@keyframes l12-dice-roll') && board.includes('.dice-reveal-animation{z-index:904}'), '普通掷骰事件必须在交互层下方播放非阻塞动画并保留可读日志'],
  [prompt.includes("prompt.value?.kind === 'option'") && prompt.includes('effect-option-list')
    && prompt.includes('orderedEffectChoices') && prompt.includes('declineChoices')
    && prompt.includes('.prompt-choices.effect-option-list{display:grid')
    && prompt.includes('grid-template-columns:repeat(auto-fit,minmax(150px,1fr))')
    && prompt.includes('overflow:visible'), '效果/费用分支必须按原顺序自适应同屏排列，三项不得依赖横向拖动，且不发动固定在最后'],
  [prompt.includes("booleanData(id, 'hasPrintedCost')") && prompt.includes('hasPrintedCost: detail.hasPrintedCost') && replayModel.includes("trait.endsWith('专属')"), '衍生卡在弹框与历史回放中不得伪造不存在的印刷费用'],
  [!matchRecords.includes("import GameBoard from './game/GameBoard.vue'")
    && !matchRecords.includes('selectMatch(matches.value[0])')
    && matchRecords.includes("router.push({ name: 'match-replay'")
    && matchRecords.includes("router.push({ name: 'json-replay'"), '对局记录只允许选择摘要；服务器记录与JSON均须在玩家点击播放后进入独立回放路由，不得默认加载或嵌入渲染棋盘'],
  [router.includes("name: 'json-replay'") && router.includes("name: 'match-replay'") && router.includes("name: 'admin-match-replay'")
    && replayPage.includes('<GameBoard v-if="currentGame" :game="currentGame" read-only />')
    && replayPage.includes('>上一步</button>') && replayPage.includes("playing ? '暂停' : '播放'")
    && replayPage.includes('>下一步</button>') && replayPage.includes("isAdminReplay.value ? '返回后台对局档案' : '返回对局记录'")
    && app.includes("route.meta.replay !== true") && board.includes('props.readOnly || !l12State.gmEnabled'), '回放必须使用与正式对战一致的独立全屏棋盘，左下提供上一步/播放/下一步，右上返回记录，并且不得被现存实时对局或沙盒控制状态污染'],
  [replayPage.includes('const playbackSpeed = ref<1 | 2 | 3>(1)')
    && replayPage.includes('const interval = 2700 / playbackSpeed.value')
    && replayPage.includes('([1, 2, 3] as const)') && replayPage.includes('{{ speed.toFixed(1) }}')
    && replayPage.includes('if (atLast.value) stop()') && replayPage.includes('class="replay-result"')
    && replayPage.includes("detail.value.match.winner ?? currentGame.value.winner")
    && replayPage.includes("result: winner === 0 ? '胜' : '负'") && replayPage.includes("result: winner === 1 ? '胜' : '负'"), '三类共用回放必须默认1倍速并提供1.0/2.0/3.0切换；旧0.9秒节奏作为3倍速，抵达最后一步立即停止并显示双方赛果'],
  [adminMatches.includes("name: 'admin-match-replay'")
    && replayPage.includes('adminReplayDetail(await adminApi.match(matchId, true))')
    && adminMatches.includes('view: view.value') && replayPage.includes("query: { ...route.query, section: 'matches'")
    && replayModel.includes('export function adminReplayDetail')
    && platform.includes("includeReplay ? '?includeReplay=true' : ''")
    && l12ServerSources.includes('includeReplay ? "read-replay" : "read-detail"')
    && !adminMatches.includes('technical-replay') && !adminMatches.includes('JSON.stringify(detail.replay'), '玩家记录、JSON与后台对局档案必须共用同一个全屏回放播放器；后台仅点击播放后读取命令，并清除旧原始JSON渲染'],
  [prompt.includes('naturalChoiceLabel(prompt.value?.choiceLabels?.[id], id)') && prompt.includes('safeChoiceFallback(id)') && !prompt.includes('const choiceLabels: Record<string, string>'), '效果选项必须优先显示服务端权威自然语言标签，并且只能用不泄露协议值的通用文案兜底'],
  [playerMat.includes('entry.enabled === false || entry.triggerOnly') && playerMat.includes('.faction-effect-actions button:disabled'), '不可发动与仅触发时发动的效果必须保留可查看文本、灰置且不可点击'],
  [deckEditor.includes('saved-list'), '保留既有牌库编辑器回滚防护'],
  [!deckEditor.includes('STARTER COPY') && !deckEditor.includes('importPreset'), '牌库编辑器不得重新引入 Starter Copy 区块'],
  [specialAssets.includes('masterProfileUrl') && prompt.includes('masterProfileUrl(player.master.masterId'), '先后手掷骰必须使用官方主宰头像资源'],
  [playerMat.includes('godPowerLogoUrl') && specialAssets.includes('olympus-god-power.png'), '神力必须使用官方神力标志'],
  [deckProfile.includes('data-deck-profile') && deckProfile.includes('masterProfileUrl(masterId, fallbackUrl)') && deckProfile.includes('class="deck-profile__portrait"'), '各类牌库框必须复用主宰 Profile 公共组件，不得各自裁切卡面'],
  [savedDeckSelector.includes('data-ui-contract="l12-saved-deck-selector"')
    && savedDeckSelector.includes('validateDeck(deck, props.catalog, props.restrictions)')
    && savedDeckSelector.includes("row.error || '符合当前模式规则'")
    && savedDeckSelector.includes('当前模式没有可用牌库'), '大厅共享牌库选择器必须逐副展示当前模式合法性并明确提示无可用牌库'],
  [savedDeckSelector.includes("emit('cancel')") && savedDeckSelector.includes("emit('confirm', selected.value.deck)")
    && !savedDeckSelector.includes('saveSelectedDeckName(')
    && savedDeckSelector.includes('draftName.value = props.currentDeckName'), '共享牌库选择器取消必须无副作用，且只有确认事件可提交草稿选择'],
  [decks.includes("'ranked', 'casual', 'friendly', 'sandbox-player', 'sandbox-opponent'")
    && decks.includes('scopedSelectedDeckStorageKey(scope)') && decks.includes('saveSelectedDeckName')
    && decks.includes('L12_DECK_SELECTION_SCOPES.forEach'), '当前牌库必须按排位、休闲、好友房及沙盒双方隔离持久化，并在删除牌库时清理失效指针'],
  [lobby.includes("import SavedDeckSelector from '@/l12/SavedDeckSelector.vue'")
    && lobby.includes("scope === 'ranked'") && lobby.includes("scope === 'friendly' && friendlyUsesRestrictions()")
    && lobby.includes("if (tab.value === 'sandbox') return 'sandbox-player'")
    && !lobby.includes('/decks?from=%2Fbattle%2Flobby')
    && !lobby.includes('class="room-decks"'), '排位、休闲、好友房及大厅沙盒入口必须复用闭环选择器，好友房按房规启用禁限卡且不得跳转牌库页'],
  [sandbox.includes("import SavedDeckSelector from '@/l12/SavedDeckSelector.vue'")
    && sandbox.includes("selectorTarget = 'sandbox-player'") && sandbox.includes("selectorTarget = 'sandbox-opponent'")
    && !sandbox.includes('operationsPolicy') && !sandbox.includes('<select v-model="playerDeckName"')
    && !sandbox.includes('<select v-model="opponentDeckName"'), '沙盒双方必须复用同一保存牌库选择器，且不得套用赛季禁限卡或退回原生下拉'],
  [deckEditor.includes("import DeckProfile from './DeckProfile.vue'") && deckLibrary.includes("import DeckProfile from '@/l12/DeckProfile.vue'") && lobby.includes("import DeckProfile from '@/l12/DeckProfile.vue'") && sandbox.includes("import DeckProfile from '@/l12/DeckProfile.vue'") && legacyLobby.includes("import DeckProfile from './DeckProfile.vue'"), '牌库编辑器、牌库页、对战房间和沙盒的牌库框必须统一接入 DeckProfile'],
  [!deckLibrary.includes('<div class="banner-strip">') && !lobby.includes('<div class="deck-thumb">库</div>') && !legacyLobby.includes('class="commander-glyph">{{ deck.'), '牌库框不得恢复多卡裁切条或“库”占位图替代已选择主宰 Profile'],
  [playerMat.includes('const displayMoraleSlots = computed') && playerMat.includes('rank(left.resource) - rank(right.resource)') && playerMat.includes("isGodPower ? (tapped ? 2 : 0) : (tapped ? 3 : 1)") && playerMat.includes(':key="morale?.instanceId') && playerMat.includes('selectMoralePayment(morale.instanceId)'), '费用资源必须按状态排序展示，同时保留真实士气实例 ID 作为支付与返还目标'],
  [playerMat.includes('class="god-power-logo"') && playerMat.includes('sepia(1) saturate(3.2)') && playerMat.includes('border-color:#f4dda1'), '神力必须使用淡黄色 Logo 与独立描边，不能继续与白色士气图标混淆'],
  [cardTile.includes('attachedGroups') && cardTile.includes('attached-card-orbs') && cardTile.includes("$emit('focusCard', group.card)"), '叠放卡牌必须由公共卡牌组件合并为圆形卡图，并可进入统一详情'],
  [l12StructuredSemantics.includes('GrantsStrongAttackWhileAttached')
    && l12StructuredSemantics.includes('HasEffectiveStrongAttack')
    && l12GameEngine.includes('HasEffectiveStrongAttack(card)'), '王者之剑、侵掠如火、自身与临时强攻必须共用同一有效关键词查询'],
  [cardTile.includes('card.activeKeywords') && cardTile.includes('card-keyword-stack') && cardTile.includes('top:25px') && !cardTile.includes('status-strong'), '当前生效关键词必须以完整文字从费用下方向下排列，不得恢复底部单字角标'],
  [cardTile.includes("props.card.isMasterLegion === true") && cardTile.includes('displayBaseTroops'), '孙悟空等主宰军团化实体必须显示权威兵力且设定兵力不误判为增益'],
  [cardTile.includes('position:static!important') && cardTile.includes('object-position:center 14%'), '圆形叠放卡图不得被全局卡图定位规则覆盖'],
  [playerMat.includes("entry.id === 'trialAdvance'") && playerMat.includes('function canTrial') && playerMat.includes("'trialAdvance')"), '试炼军团必须拥有与进攻、移动并列的直接试炼按钮'],
  [playerMat.includes("@click.stop=\"(!trial.hidden || side === 'my') && selectZoneCard(trial)\""), '试炼卡必须复用公开区域卡牌能力入口；己方未完成试炼仍可查看详情，对方未知试炼保持不可见'],
  [playerMat.includes('aspect-ratio:1752/1255') && playerMat.includes('class="trial-card-back"') && playerMat.includes('.trial-card b{position:absolute;left:50%;top:50%'), '试炼卡背必须保持正式横版素材比例并将进度数字置于中央'],
  [playerMat.includes('class="master-marker-track"') && playerMat.includes('.master-marker-track{position:absolute') && playerMat.includes('top:-39px'), '主宰附近圆形标识必须复用同一轨道并与主宰保持间距'],
  [!playerMat.includes('class="rune-zone"') && !playerMat.includes('class="canopic-track"'), '符文与卡诺匹斯不得恢复各自独立的定位父级'],
  [board.includes('destructionRoundBackUrl'), '本局天灾圆形未知卡必须使用圆形天灾卡背'],
  [specialAssets.includes('disasterRoundUrl') && board.includes('disasterRoundUrl(card.cardId, card.imageUrl)') && board.includes('destructionRoundBackUrl'), '本局已知天灾必须使用官方圆形卡图，未知天灾继续使用专用卡背'],
  [lobby.includes('visibleDeckLabel') && lobby.includes('player.playerIndex === l12State.room?.yourPlayerIndex'), '房间内不得向对手公开牌库名称'],
  [decks.includes("构筑时不计入卡组数量") && decks.includes("`${counted}${uncounted ? `(${uncounted})` : ''}`"), '不计入构筑上下限的卡牌必须使用通用规则识别，并以 40(3) 形式单列数量'],
  [deckEditor.includes('publicDeckApi.publish') && deckEditor.includes("publicationId.value = ''") && deckEditor.includes("preservePublication = false"), '牌库编辑器须支持公开/更新公开牌库，并在新建、另存或切换本地牌库时隔离公开版本身份'],
  [deckLibrary.includes('publicDeckApi.list') && deckLibrary.includes('编辑公开牌库') && deckLibrary.includes('删除公开牌库') && deckLibrary.includes('ownerId === platformState.account?.id'), '公开牌库必须由服务端持久化，且仅作者显示编辑与删除入口'],
  [platform.includes('views: number; likes: number; copies: number') && platform.includes('recordView: (id: string)')
    && wsServer.includes('/api/public-decks/{id}/view') && deckLibrary.includes('publicDeckApi.recordView(entry.id)')
    && deckLibrary.includes('(b.views ?? 0) - (a.views ?? 0)') && deckLibrary.includes('b.likes - a.likes')
    && deckLibrary.includes('b.copies - a.copies') && deckLibrary.includes('b.updatedAt.localeCompare(a.updatedAt)')
    && deckLibrary.includes('浏览量 {{ entry.views ?? 0 }}') && deckLibrary.includes('符合本赛季')
    && deckLibrary.includes('不符合本赛季') && deckLibrary.includes('查看构筑')
    && deckLibrary.includes('--deck-faction:') && deckLibrary.includes('rgba(var(--deck-faction),.2)')
    && deckLibrary.includes('color:#c7cecd;font-size:12px'), '公开牌库浏览量须持久化；热门排序固定为浏览量、点赞、复制、最新时间，信息条按浏览量/点赞/复制/赛季要求/查看构筑排列，并使用低亮度阵营底色与可读高对比文字'],
  [deckEditor.includes('masterProfileUrl(selectedMaster.id') && lobby.includes('border-radius:2px'), '主宰头像必须使用官方正方形资源'],
  [cardArchive.includes('trialValue') && cardArchive.includes('<dt>试炼值</dt>'), '卡牌档案必须展示试炼值'],
  [playerMat.includes('aria-disabled') && playerMat.includes('.morale-orb.active-morale[aria-disabled="true"]') && playerMat.includes('.morale-orb.active-god-power[aria-disabled="true"]'), '可用的活跃士气与神力必须始终高亮'],
  [playerMat.includes('class="morale-count"') && playerMat.match(/class="morale-count"/g)?.length === 2, '双方士气数量必须共用不溢出的计数器'],
  [board.includes('promotionFoundationTargetIds') && board.includes('promotionOptions')
    && l12ServerSources.includes('NextS2PromotionGodPowerDiscount'), '晋升登场必须高亮服务端权威合法基底并纳入锻造炉减免'],
  [deckEditor.includes('主宰') && deckEditor.includes('主牌库') && deckEditor.includes('额外卡牌') && !deckEditor.includes('可用卡牌'), '牌库编辑器中区必须保持主宰/主牌库/额外卡牌三标签'],
  [deckEditor.includes('class="catalog-filter-bar" aria-label="主牌库筛选"') && !deckEditor.includes('<h2>构筑设定</h2>') && deckEditor.includes('costFilter') && deckEditor.includes('disasterFilter') && deckEditor.includes('sortMode'), '牌库编辑器必须把卡牌档案式筛选放在主牌库卡池上方，并保留搜索、类型、卡池、费用、天灾等级与排序（不含阵营）'],
  [deckEditor.includes('class="deck-detail-panel grand-panel"') && deckEditor.includes('class="saved-decks-panel grand-panel"') && deckEditor.includes('.saved-list{display:grid;gap:5px;overflow-y:auto'), '牌库编辑器卡牌详情与已保存牌库必须使用独立盒子，且已保存牌库可独立纵向滚动'],
  [deckEditor.indexOf('class="current-deck-summary"') > deckEditor.indexOf('class="deck-catalog grand-panel"')
    && deckEditor.indexOf('class="current-deck-summary"') < deckEditor.indexOf('<p class="kicker">CARD POOL</p>')
    && deckEditor.includes('masterProfileUrl(selectedMaster.id') && deckEditor.includes('士气 {{ moraleIds.length }} 张')
    && !deckEditor.includes('class="master-preview"'), '当前主宰头像、名称、阵营与士气数必须左对齐置于中间卡池盒顶部，左栏只保留选中卡牌详情'],
  [(deckEditor.match(/class="deck-entry-row"/g)?.length ?? 0) >= 3
    && deckEditor.includes('class="selected-extra-cards"') && deckEditor.includes('v-for="trial in selectedTrials"')
    && deckEditor.includes('v-for="card in automaticExtraCards"'), '右下显式试炼与主宰自动额外卡必须复用主牌库横向条目视觉'],
  [decks.includes("'S01-02M1': ['S01-02M2']") && decks.includes('export function automaticExtraCardIdsForMaster')
    && deckEditor.includes('automaticExtraCardIdsForMaster(selectedMaster.value?.id)')
    && deckShare.includes('automaticExtraCardIdsForMaster(deck.masterId)'), '主宰自动额外卡映射必须由 decks.ts 公共函数统一提供给编辑器与牌库图导出'],
  [deckShare.includes('...(deck.specialIds ?? [])') && deckShare.includes('const extraIds = [...new Set([')
    && deckShare.includes("isHorizontalCardType(card?.cardType) ? 1752 / 1255 : 5 / 7")
    && deckShare.includes('extraBitmaps.forEach(bitmap => bitmap?.close())'), '牌库图必须包含显式试炼和主宰自动额外卡，且横版额外卡按正式横版比例绘制并释放位图'],
  [deckEditor.includes('const productOptions = computed(') && deckEditor.includes('<option value="all">全部卡池</option><option v-for="value in productOptions"'), '牌库编辑器卡池筛选必须使用全部卡池并从完整目录动态列出 S01、S02 与 ST 产品'],
  [deckEditor.includes('effectiveDeckLimit(card, masterId.value)')
    && deckEditor.includes('effectiveDeckLimit(entry.card, masterId)')
    && !deckEditor.includes('activeRestrictions') && !deckLibrary.includes('activeRestrictions')
    && decks.includes('rule.masterId === masterId') && decks.includes('rule.masterId === deck.masterId'),
  '通用牌库编辑、保存、导入与公开不得全局应用运营禁限卡；显式规则作用域仍保留主宰专属解析能力'],
  [cardArchive.includes('const productOptions = computed(') && cardArchive.includes('<option value="all">全部产品</option><option v-for="value in productOptions"') && sandboxPicker.includes('<option value="all">全部卡池</option><option v-for="value in products"'), '卡牌档案必须按权威收录产品筛选，沙盒选择器仍从完整可玩目录动态列出卡池，不得回退为旧双卡池硬编码'],
  [deckEditor.indexOf('生成牌库图') > deckEditor.indexOf('另存为牌库') && deckEditor.indexOf('生成牌库图') < deckEditor.indexOf('删除牌库'), '生成牌库图必须位于另存为牌库与删除牌库之间'],
  [deckEditor.includes('createDeckImageBlob') && deckEditor.includes('deck-image-dialog') && deckEditor.includes('下载牌库图'), '牌库编辑器必须提供可预览、下载的真实牌库图生成流程'],
  [deckOrdering.includes('TYPE_PRIORITY') && deckOrdering.includes('Number.NEGATIVE_INFINITY') && deckEditor.includes('compareDeckCards') && deckShare.includes('compareDeckCardIds') && deckLibrary.includes('compareDeckCardIds'), '牌库默认顺序必须统一为类型、本阵营/中立、费用高至低和编号前至后，并由编辑器、详情与牌库图复用'],
  [deckEditor.includes('牌库删除后不可找回') && deckEditor.includes('继续删除') && deckEditor.includes('pendingDeleteName') && !deckEditor.includes('@click="onDelete'), '全部牌库删除入口必须先经过统一确认弹框'],
  [gamePage.includes("import { gameAction, l12State, leaveRoom, returnToRoom } from './net'") && gamePage.includes("game.value?.phase === 'GameOver'") && gamePage.includes('returnToRoom()') && gamePage.includes('l12State.spectating || l12State.room?.sandbox'), '正式对局结束必须保留房间并回到重新准备；沙盒和观战返回大厅必须退出权威房间'],
  [l12Net.includes('leavingRoom: false') && l12Net.includes("if (l12State.leavingRoom) return") && l12Net.includes("socket.send(JSON.stringify({ type: 'leaveRoom' }))") && app.includes('!l12State.leavingRoom'), '退出观战必须屏蔽迟到快照与自动路由，并在断线重连后优先重发退出请求'],
  [sandbox.includes('createSandbox') && sandbox.includes('沙盒不会进入个人对局记录或排位统计') && !sandbox.includes('沙盒服务器适配器尚未接入'), '单人测试沙盒必须连接正式规则内核且明确隔离正式记录，不得回退为占位页'],
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
  [!board.includes('当前子阶段：') && !board.includes('data-ui-contract="combat-substage"') && board.includes('pending.attackValue > 0') && board.includes("pendingDefense?.stage === 'DefenseChoice'") && gameActions.includes("pendingDefense?.stage === 'DefenseChoice'"), '进攻界面必须消费服务端子阶段与冻结进攻值，只在 DefenseChoice 开放抵挡/支援，并禁止显示内部子阶段调试文字'],
  [prompt.includes("prompt.value?.data?.uiPattern === 'effect-decision'") && prompt.includes("return 'OPTION'") && prompt.includes("return '发动'") && prompt.includes("return '不发动'") && prompt.includes('decisionEffectText') && prompt.includes('!isEffectDecision.value') && prompt.includes('确认选择'), '可选卡效发动框必须统一为 OPTION、来源名、当前效果文本、发动/不发动与确认选择，且不得居中展示来源卡图'],
  [l12PromptSetup.includes('"discard-or-decline", "optional-card", "search"') && l12PromptSetup.includes('data.TryAdd("layout", "single-row")') && l12PromptSetup.includes('data["displayCardIds"]') && prompt.includes("prompt.value?.data?.layout === 'single-row'") && prompt.includes('displayCardIds') && prompt.includes('unavailable'), '弃牌及查看多张选择部分必须使用横向全卡图列表，并将不合法卡灰置不可选'],
  [prompt.includes("import PromptCardCandidate from './PromptCardCandidate.vue'") && (prompt.match(/<PromptCardCandidate/g)?.length ?? 0) >= 6
    && promptCardCandidate.indexOf('<CardImage') < promptCardCandidate.indexOf('prompt-card-candidate__name')
    && promptCardCandidate.indexOf('prompt-card-candidate__name') < promptCardCandidate.indexOf('prompt-card-candidate__meta')
    && promptCardCandidate.includes('v-if="cardId || legacyUrl"') && promptCardCandidate.includes('props.unavailable')
    && prompt.includes('function cardName(id: string)') && prompt.includes('function cardMeta(id: string)')
    && prompt.includes("? '匿名手牌' : ''") && prompt.includes(':name="cardName(choice)" :meta="cardMeta(choice)"')
    && promptCardCandidate.includes('<div') && promptCardCandidate.includes('<button v-if="removable"') && !promptCardCandidate.includes('<i v-if="removable"')
    && prompt.includes(':badge="selectionHint(choice)"') && promptCardCandidate.includes('prompt-card-candidate__badge')
    && prompt.includes("prompt.value?.data?.cardSelection === 'true'") && prompt.includes(':unavailable="isCardSelectionPrompt && !prompt.validChoices.includes(choice)"')
    && prompt.includes("'prompt-card-strip': hasCardChoices") && prompt.includes('overflow-x:auto;overflow-y:hidden')
    && !prompt.includes('card-grid') && !prompt.includes('placement-mini-card') && !prompt.includes('prompt-featured-card'), '普通选卡、非法灰置、回顶回底排序与调度必须复用同一卡牌候选组件，卡图在上卡名在下且超出横向滚动'],
  [prompt.includes('previewPresentation') && prompt.includes('showPreviewCard')
    && prompt.includes("['handled-card', 'information-card'].includes")
    && prompt.includes('v-if="showPreviewCard') && !prompt.includes('v-if="previewCardId && !displayedChoices.includes(previewCardId)"'), '效果决定与场面目标不得因 previewCardId 显示来源卡图；仅处理中卡牌与信息公开确认可显式开启预览'],
  [prompt.includes('const cardId = cardIdFor(id)') && prompt.includes('if (!card && !imageUrl && !cardId) return null')
    && prompt.includes('cardId: detail.cardId') && promptCardCandidate.includes(':card-id="cardId"'), 'Prompt 候选只要具有 cardId 就必须创建详情并渲染 CardImage，不得依赖旧 imageUrl 才显示卡图'],
  [l12PromptSetup.includes('"optional-cards", "order", "trial-order"')
    && l12PromptSetup.includes('var isCardChoice = explicitDisplayIds.Length > 0')
    && l12PromptSetup.includes('data.TryAdd($"{id}:name", card.Name)')
    && l12PromptSetup.includes('data.TryAdd($"{id}:zone", zone)')
    && l12PromptSetup.includes('validChoices.Concat(explicitlyDisplayedIds).Concat(previewCardIds)'), 'CreatePrompt 公共层必须按卡实例或元数据识别全部卡牌选择 kind，补齐展示非法项、独立预览与名称、卡号、类型、图片和区域元数据'],
  [l12PromptSetup.includes('CreateAnonymousHandChoicePrompt') && l12PromptSetup.includes('/assets/l12/card-back-official.png')
    && l12PromptSetup.includes('prompt.HiddenChoiceMap[slots[index]]') && !l12PromptSetup.includes('data[$"{slots[index]}:cardId"]'), '故意隐藏的对方手牌选择必须只投影匿名槽位、卡背和匿名标签，不得泄露真实卡牌身份'],
  [l12Types.includes('choiceLabels: Record<string, string>') && prompt.includes('prompt.value?.choiceLabels?.[id]') && prompt.includes('safeChoiceFallback') && prompt.includes('isInternalChoiceValue') && !prompt.includes("?? id.replace(':', ' 排第 ')") && !prompt.includes('?? choiceLabels[id] ?? cardFor(id)?.name ?? id'), '玩家可见选择必须使用服务端自然语言标签，前端不得把 mode、continuation、action 或其他内部 choice id 直接显示为兜底'],
  [l12Types.includes('promotionOptions?: Record<string, string[]>')
    && board.includes('me.value.promotionOptions?.[card.instanceId] ?? []')
    && board.includes('promotionFoundationIdsFor(selectedHandCard.value).includes(card.instanceId)')
    && !board.includes('const promotionFoundations:')
    && !board.includes('effectText?.match(/消耗并翻转'), '晋升按钮与底座目标必须完全消费服务端按实例下发的合法集合，不得在前端维护卡号表或解析卡面费用'],
  [internalModeChoices.every(choice => centralModeChoiceLabels.has(choice)) && [...centralModeChoiceLabels.values()].every(label => !/(?:mode|continuation|action):/i.test(label)) && l12PromptModel.includes('Dictionary<string, string> ChoiceLabels') && l12PromptSetup.includes('BuildPlayerChoiceLabels') && l12GameEngine.includes('prompt.MinChoose, prompt.MaxChoose, prompt.Data, prompt.ChoiceLabels'), '服务端所有 mode:* 选项必须在 Prompt 公共入口具有自然语言标签，快照只投影标签而不得把内部值当玩家文案'],
  [playerChoiceLabelLiterals.length > 0 && playerChoiceLabelLiterals.every(label => !inventedChoiceLabel.test(label)), '玩家效果选项必须使用卡面效果原文或准确费用动作，不得显示普通/强模式、追加效果、只结算等程序概括'],
  [windowsVerify.includes('Get-ChildItem -LiteralPath (Join-Path $repoRoot "服务端WebSocket\\TwelveLegions") -File -Filter "*.cs"') && windowsVerify.includes('Copy-Item -Destination $isolatedServerSourceRoot -Force'), '提交级隔离前端构建必须复制全部服务端 Prompt 定义，玩家文案全量扫描不得因缺文件失败或产生局部扫描假阳性'],
  [board.includes('const passivePresentationPaused = computed') && board.includes('activeBoardPromptId.value') && board.includes(':paused="passivePresentationPaused"') && board.includes('v-if="publicReveal && !activeBoardPromptId"') && board.includes('v-if="diceReveal && !activeBoardPromptId"') && board.includes('v-if="combat && !activeBoardPromptId"') && board.split(':interaction-prompt-active="Boolean(activeBoardPromptId)"').length === 3 && playerMat.includes('watch(() => props.interactionPromptActive') && prompt.includes('.l12-prompt-overlay,.l12-prompt-overlay.minimized{z-index:3000!important}') && board.includes('.board-target-controls{z-index:3000}') && board.includes('.card-inspector-floating{z-index:3100!important}') && actionLayer.includes('.l12-action-presentation{z-index:900}') && zoneMovementLayer.includes('.zone-card-movement{z-index:902}') && board.includes('watch(activeBoardPromptId, promptId => {') && actionLayer.includes('if (paused && active.value)') && zoneMovementLayer.includes('if (paused && active.value) cancelActiveMovement()'), '普通卡牌、触发、响应与掷骰展示不得遮挡或延迟可操作 Prompt/场面选择；交互层必须始终更高，弹框详情仍浮在蒙版上方，且场面选择立即可操作'],
  [globalStyle.includes('.l12-effect-body{font-size:14px!important') && globalStyle.includes('white-space:pre-wrap') && globalStyle.includes('.l12-effect-body--compact{font-size:12px!important') && cardArchive.includes('<p class="l12-effect-body">{{ selected.effect') && deckEditor.includes('<p class="l12-effect-body l12-effect-body--compact">{{ selected.effect') && board.includes('inspector-effect l12-effect-body l12-effect-body--compact') && prompt.includes("'l12-effect-body': isEffectOptionList") && masterOverlay.includes('player.master.effectText') && masterOverlay.includes('class="l12-effect-body l12-effect-body--compact">{{ entry.label }}') && playerMat.includes('player.factionEffect?.effectText') && playerMat.includes('class="l12-effect-body l12-effect-body--compact"') && adminPage.includes('<p class="l12-effect-body">{{ selectedEffect.effectText') && adminPage.includes('<p class="l12-effect-body">{{ ability.text }}') && !cardArchive.includes('archive-number l12-effect-body') && !deckEditor.includes('<small class="l12-effect-body">{{ selected.number'), '全站卡效正文必须共用语义字号并保留权威换行，覆盖卡牌详情、牌库编辑、对战、Prompt、主宰/阵营与管理后台；费用、编号、数值与标签等辅助信息不得被扩大'],
  [gmPanel.includes("emit('armPlacement'") && gamePage.includes(':gm-placement="gmPlacement"') && board.includes("emit('gmPlacementResolved')") && board.includes('GM：请选择'), 'GM 打出军团必须回到棋盘并点击目标玩家的绿色空位'],
  [playerMat.includes('selectRunePayment') && playerMat.includes('`rune:${index}`') && playerMat.includes('payable: paymentChoiceIds'), '符文支付必须直接点击场上的可用符文，不得恢复编号弹框'],
  [playerMat.includes('data-ui-contract="independent-trial-action"') && playerMat.includes('class="trial-direct-action"') && playerMat.includes("emit('ability', player.field[row][slot]!, 'trialAdvance')") && playerMat.includes('.trial-direct-action{position:absolute;z-index:32;right:4px;bottom:4px') && !playerMat.includes('<button v-if="canTrial(player.field[row][slot]!)"'), '全部试炼军团的试炼按钮必须独立于通用动作栏呈现，并固定在不受动作栏宽度裁切的位置'],
  [board.includes('boardSlotTargetPlayerIndex') && board.includes('targetPlayerIndex') && playerMat.includes("promptSlotIds?.includes(`${row}:${slot}`)"), '跨阵营位移的目标阵地必须高亮实际被移动军团所在战场，不得回退为操作者自己的同坐标格'],
  [l12Net.includes("send({ type: 'sandboxAction', actingPlayerIndex, command })") && board.includes('controlledPlayerIndex') && board.includes('const viewMe = computed(() => props.game.players[props.game.you])') && board.includes('const viewEnemy = computed(() => props.game.players[1 - props.game.you])') && board.includes('v-if="l12State.gmEnabled" class="opponent-hand" :cards="viewEnemy.hand"') && board.includes(':cards="viewMe.hand"') && board.includes(':controllable="isControlledPlayer(viewEnemy.playerIndex)"') && prompt.includes('sandboxAction(actingPlayerIndex, command)') && globalStyle.includes('.opponent-hand .hand-actions{top:calc(100% + 4px);bottom:auto}'), '沙盒必须固定我方在下、对方在上，不交换棋盘，同时可查看双方手牌并代行双方规则内选择；上方手牌操作按钮必须朝棋盘中心展开而不被裁切'],
  [board.includes(':mine="masterPlayerIndex === controlledPlayerIndex"') && board.includes("sandboxAction(controlledPlayerIndex.value, { type, ...extra })") && prompt.includes('sandboxAction(actingPlayerIndex, command)'), '沙盒代操作对方时必须按受控方索引开放主宰效果并完成后续提示，正式房仍只允许登录座位'],
  [board.includes('watch(activeBoardPromptId, promptId => {') && board.includes('graveyardPlayer.value = null') && board.includes('masterPlayerIndex.value = null') && board.includes('focusCard.value = null'), '任何场面直选 Prompt 开始时必须关闭墓地、效果弹框与浮动卡牌详情'],
  [graveyardOverlay.includes('function selectCard(card: Card)') && graveyardOverlay.includes("enabledAbilities.length === 1") && !graveyardOverlay.includes('graveyard-abilities'), '墓地主动效果必须点击卡牌本身进入是否发动流程，卡面不得重新覆盖效果文字按钮'],
  [sandboxPicker.includes('loadDeckCatalog') && !sandboxPicker.includes("fetch('/data/l12/cards.s1.json')") && sandboxPicker.includes('搜索卡名、编号或效果文字') && sandboxPicker.includes('全部阵营'), '沙盒卡牌选择器必须复用卡牌档案的完整卡池搜索与筛选逻辑'],
  [starterCards.length === 76 && starterCards.some(card => card.id === 'ST06-01' && card.nameZh === '伊丽莎白一世'), '前端权威目录必须收录 76 张 ST 产品卡，并以数据库中的伊丽莎白一世为准'],
  [sandbox.includes('<option value="custom">自定天灾（四张始终公开）</option>') && board.includes("type: 'replaceDisaster'") && board.includes('index < 3'), '自定天灾必须四张公开、前三槽可更换且第四槽堙灭锁定'],
  [adminPage.includes('卡效原子化') && adminPage.includes('atom-flow') && adminPage.includes('原子定义 JSON'), '管理后台必须保留卡效原子组合、流程图与原始定义视图'],
  [adminPage.includes('旧实现兜底') && adminPage.includes('新旧实现不会同时结算'), '原子化后台必须明确显示旧实现兜底与防重复结算边界'],
  [platform.includes("effectAtoms: () => platformRequest<EffectAtomDescriptor[]>('/api/admin/effect-atoms')") && platform.includes('/api/admin/effects/coverage'), '卡效后台必须从服务端权威原子注册表读取数据'],
  [adminPage.includes('实战已验证') && adminPage.includes('effectCoverage.verifiedAbilities') && platform.includes('verifiedAbilities: number'), '原子化后台必须区分文本拆分与已接管实战执行的能力'],
  [adminPage.includes('class="effect-scroll"') && adminPage.includes('overflow-y:auto') && adminPage.includes('human-assisted') && adminPage.includes('confirmed'), '原子化能力清单必须可纵向滚动，并区分人工辅助与人工确认状态'],
  [platform.includes('siteContentApi') && officialHome.includes('v-if="ready"') && officialHome.includes('siteContentApi.home()') && platform.includes('platformRequest<Article[]>(`/api/articles') && adminPage.includes('AdminSiteContentPanel') && adminPage.includes("tab === 'content'") && newsPage.includes('articleApi.list') && !adminPage.includes('<section class="news-editor"'), '官网固定内容必须批量加载避免默认文案闪烁；资讯须使用独立稿件接口与后台工作台，不得继续嵌在官网内容表单中'],
  [adminPage.includes('站点内容工作台') && adminSiteContent.includes('<AdminArticlesPanel') && adminSiteContent.includes('kind="news"') && adminArticles.includes('class="article-editor') && adminArticles.includes('保存草稿') && adminArticles.includes('发布 / 安排发布') && adminArticles.includes('历史版本') && adminArticles.includes('MediaUploadField') && adminArticles.includes('v-model="selected.link"'), '后台资讯发布必须提供独立列表、完整稿件编辑、封面与链接、发布状态和历史版本恢复'],
  [mediaUploadField.includes('ORIGINAL_MAX_BYTES = 16 * 1024 * 1024')
    && mediaUploadField.includes('REQUEST_MAX_BYTES = 32 * 1024 * 1024')
    && mediaUploadField.includes('class="media-spec" aria-label="图片上传尺寸参考"')
    && mediaUploadField.includes('用途：') && mediaUploadField.includes('安全区与裁切：')
    && platform.includes("response.status === 413 && path === '/api/admin/site/media'")
    && wsServer.includes('IHttpMaxRequestBodySizeFeature') && wsServer.includes('SiteMediaRequestMaxBytes')
    && nginxSite.includes('client_max_body_size 1m;')
    && [nginxSite, nginxHttpSite].every(source => source.includes('location = /api/admin/site/media')
      && source.includes('client_max_body_size 32m;') && source.includes('media_upload_too_large'))
    && serverDeploy.includes("grep -Fq 'location = /api/admin/site/media'")
    && serverDeploy.includes("grep -Fq 'client_max_body_size 32m'"), '站点图片上传必须在浏览器、Nginx 精确路由和 ASP.NET 端统一执行 16MB 原图/32MB 请求边界，并在控件近旁显示用途、比例、像素和裁切安全区'],
  [mediaUploadField.includes("createImageBitmap(source, { imageOrientation: 'from-image' })")
    && mediaUploadField.includes('v-if="isHero" class="hero-upload-field"')
    && mediaUploadField.includes("type HeroVariantKey = 'desktop' | 'mobile' | 'thumbnail'")
    && mediaUploadField.includes('heroStatuses[spec.key].startsWith(\'已通过\')')
    && mediaUploadField.includes('三个版本均为必填') && mediaUploadField.includes('仅缩放、不裁切')
    && mediaUploadField.includes("form.append('independentVariants', 'true')")
    && mediaUploadField.includes("form.append('desktopAltText'") && mediaUploadField.includes("form.append('mobileAltText'")
    && mediaUploadField.includes("form.append('thumbnailAltText'")
    && platform.includes('desktopAltText: string; mobileAltText: string; thumbnailAltText: string; independentVariants: boolean')
    && siteContentStore.includes('SanitizeDeliveryWebp') && siteContentStore.includes('StripWebpMetadata')
    && siteContentStore.includes('chunk is not ("EXIF" or "XMP " or "ICCP")')
    && siteContentStore.includes('payload[0] &= 0x10') && siteContentStore.includes('StoreImmutableMediaGroup')
    && siteContentStore.includes('new PendingMediaFile(desktopWebp')
    && siteContentStore.includes('row.ContentHash = string.IsNullOrWhiteSpace(row.ContentHash) ? row.OriginalHash')
    && adminSiteContent.includes('独立三版本') && adminSiteContent.includes('三个版本统一保护、统一软删除'), '轮播素材必须以桌面、移动、缩略三个独立构图文件和各自替代文字一次原子提交；原图按方向解码，服务端按RIFF区块净化交付WebP并按单一素材组哈希、引用与软删除，旧素材仍可读'],
  [articleDocumentEditor.includes('contenteditable="true"') && articleDocumentEditor.includes('class="editor-canvas"')
    && articleDocumentEditor.includes("command('bold')") && articleDocumentEditor.includes("command('italic')")
    && articleDocumentEditor.includes("command('underline')") && articleDocumentEditor.includes("command('strikeThrough')")
    && articleDocumentEditor.includes("setAlignment('left')") && articleDocumentEditor.includes("setAlignment('center')")
    && articleDocumentEditor.includes("setAlignment('right')") && articleDocumentEditor.includes("setAlignment('justify')")
    && articleDocumentEditor.includes("command('insertUnorderedList')") && articleDocumentEditor.includes("command('insertOrderedList')")
    && articleDocumentEditor.includes('↶ 撤销') && articleDocumentEditor.includes('↷ 重做')
    && articleDocumentEditor.includes('MediaUploadField kind="article"') && articleDocumentEditor.includes('预览文章')
    && !articleDocumentEditor.includes('class="editor-block"') && !articleDocumentEditor.includes('<textarea')
    && articleBlockModel.includes("format: 'l12-blocks'") && articleBlockModel.includes('safeArticleHref')
    && articleBlockModel.includes("'underline'") && articleBlockModel.includes("'strikethrough'") && articleBlockModel.includes('ArticleTextAlign')
    && articleRenderer.includes('<blockquote') && articleRenderer.includes('<figure') && articleRenderer.includes('alignStyle') && !articleRenderer.includes('v-html')
    && adminArticles.includes('<ArticleDocumentEditor') && newsPage.includes('<ArticleContentRenderer')
    && articleStore.includes('RequireOnlyProperties') && articleStore.includes('正文内容块类型不在白名单中')
    && articleStore.includes('NormalizeOptionalUrl(href, "正文链接", allowRelative: true)'), '资讯正文必须使用单一连续稿纸、完整排版工具栏、结构化白名单和固定 Vue 节点渲染，兼容旧纯文本且不开放任意 HTML'],
  [!articleDocumentEditor.includes('window.prompt') && articleDocumentEditor.includes('class="editor-dialog"')
    && articleDocumentEditor.includes('@click="applyLink"') && articleDocumentEditor.includes('@click="removeLink"')
    && articleDocumentEditor.includes('safeArticleHref(linkHref.value)') && articleDocumentEditor.includes("key === 'k'")
    && articleDocumentEditor.includes('HISTORY_DEBOUNCE_MS = 700')
    && articleDocumentEditor.includes('historyTimer = setTimeout(flushHistory, HISTORY_DEBOUNCE_MS)')
    && articleDocumentEditor.includes('@blur="flushHistory"') && articleDocumentEditor.includes('function syncCanvas()')
    && articleDocumentEditor.includes('点击图片可再次编辑'), '正文编辑器必须提供非浏览器prompt的链接/图片面板、快捷键、合并式输入历史及正文图片就地再编辑'],
  [mediaUploadField.includes('current.flexibleDimensions') && mediaUploadField.includes('renderFlexibleVariant')
    && mediaUploadField.includes('正文插图不限制像素尺寸和长宽比') && mediaUploadField.includes('不裁切、不拉伸')
    && platform.includes('flexibleDimensions: boolean')
    && siteContentStore.includes('FlexibleDimensions = false') && siteContentStore.includes('"article", "资讯正文图片", 0, 0, 0, 0, 0, 0')
    && siteContentStore.includes('policy.FlexibleDimensions ? null'), '资讯正文插图必须允许任意像素与长宽比，完整保留构图并仅等比例生成交付WebP；封面和轮播继续执行各自固定规格'],
  [router.includes("path: '/news/:articleId'") && newsPage.includes('showingNewsDetail')
    && newsPage.includes('← 返回资讯一览') && newsPage.includes(':to="kind === \'news\' ? `/news/${entry.id}`')
    && !newsPage.includes('<details v-if="entry.body"') && officialHome.includes("fallback === '/news' ? `/news/${article.id}`")
    && adminSiteContent.includes(':value="`/news/${article.id}`"'), '每篇资讯必须从一览页或首页进入独立详情页，详情页右上角可返回资讯一览；通知按钮新建链接也必须指向单篇详情'],
  [articleStore.includes('row.Summary = kind == "video" ? string.Empty')
    && articleStore.includes('row.Body = kind == "video" ? string.Empty')
    && articleStore.includes('VideoAuthorRequired') && articleStore.includes('新视频发布前必须填写作者名')
    && articleStore.includes('视频发布前必须填写播放链接')
    && adminArticles.includes("props.kind === 'video'") && adminArticles.includes('selected.videoAuthorName')
    && adminArticles.includes("summary: props.kind === 'video' ? ''") && adminArticles.includes("body: props.kind === 'video' ? ''")
    && officialHome.includes('v-if="item.videoAuthorName"') && officialHome.includes('isInternal(item.link)')
    && officialHome.includes('<time>{{ dateLabel(item) }}</time>') && !officialHome.includes('作者：{{')
    && newsPage.includes('entry.videoAuthorName') && newsPage.includes(':href="entry.link || undefined"'), '视频内容必须只暴露封面、标题、无前缀作者名、发布时间和整卡跳转链接，新稿作者与链接发布必填，旧空作者不显示占位'],
  [siteContentStore.includes('["news"] = new("news", "资讯封面", 1600, 900, 1280, 720, 480, 270')
    && siteContentStore.includes('["video"] = new("video", "视频封面", 1280, 720, 1280, 720, 480, 270')
    && siteContentStore.includes('["product"] = new("product", "商品图片", 1600, 1200, 1200, 900, 480, 360')
    && officialHome.includes('news.value.slice(0, 5)') && officialHome.includes('videos.value.slice(0, 5)')
    && (officialHome.match(/class="featured-editorial-layout"/g)?.length ?? 0) === 2
    && officialHome.includes('资讯一主四辅布局占位范例') && officialHome.includes('视频一主四辅布局占位范例')
    && officialHome.includes('class="featured-editorial-support"') && officialHome.includes('grid-template-columns:repeat(2,minmax(0,1fr))')
    && officialHome.includes('<small>{{ dateLabel(item) }} · {{ item.category }}</small><b>{{ item.title }}</b></span></component></div>')
    && officialHome.includes('.home-editorial-card{display:flex;min-width:0;flex-direction:column;border:0;background:transparent;box-shadow:none')
    && officialHome.includes('.home-card-copy{display:flex;min-width:0;flex-direction:column;gap:8px;padding:14px 0 16px;border-bottom:1px solid')
    && officialHome.includes('.home-editorial-card:hover>picture img,.home-editorial-card:hover>img')
    && officialHome.includes('@media(max-width:1100px){.featured-editorial-layout{grid-template-columns:1fr}.featured-editorial-support{grid-template-columns:repeat(2,minmax(0,1fr))}}')
    && officialHome.includes('@media(max-width:650px){.featured-editorial-layout,.featured-editorial-support{grid-template-columns:1fr')
    && !officialHome.includes('.home-editorial-card:hover{border-color:')
    && officialHome.includes('aspect-ratio:16/9') && officialHome.includes('aspect-ratio:4/3')
    && newsPage.includes("'product-list': kind === 'product'") && newsPage.includes('.news-list.product-list article>img{aspect-ratio:4/3}')
    && newsPage.includes('grid-template-columns:repeat(auto-fit,minmax(min(300px,100%),1fr))'), '首页资讯与视频必须共用开放式 16:9 一主四辅响应式布局，不得恢复卡片套盒；资讯/视频/商品衍生图和列表分别稳定为 16:9、16:9、4:3，窄宽度自动换列且不挤压'],
  [articleStore.includes('.OrderByDescending(row => row.Published!.Pinned)\n                .ThenByDescending(row => row.Published!.PublishedAt)\n                .ThenBy(row => row.Id)')
    && !articleStore.includes('.ThenBy(row => row.Published!.SortOrder)')
    && articleStore.includes('.OrderByDescending(row => row.Pinned)\n                .ThenByDescending(row => row.PublishAt ?? DateTimeOffset.MinValue)\n                .ThenBy(row => row.Id)')
    && !articleStore.includes('.OrderBy(row => row.SortOrder).ThenByDescending(row => row.UpdatedAt)')
    && articleStore.includes('row.Status == "scheduled" && row.PublishAt <= now')
    && adminArticles.includes('未来时间到点自动公开') && adminArticles.includes('公开排序只使用置顶与此时间')
    && adminArticles.includes("'尚未设置发布时间'") && !adminArticles.includes('new Date(article.updatedAt)')
    && !adminArticles.includes('排序值<input'), '资讯、视频和商品的公开顺序及后台预览列表必须统一为置顶优先、发布时间倒序、ID 稳定并列；未来发布时间到点自动公开，编辑时间和人工排序不得参与'],
  [officialHome.includes('.hero-copy>small,.hero-copy h1,.hero-copy p,.hero-copy strong{white-space:pre;overflow-wrap:normal;word-break:normal}')
    && !officialHome.includes('<div class="hero-shade"')
    && officialHome.includes('.hero-copy,.hero-copy h1,.hero-copy p,.hero-copy strong{ text-shadow:0 2px 5px rgba(0,0,0,.68) }')
    && adminSiteContent.includes('longestHeroLine') && adminSiteContent.includes('前台不会自动换行')
    && adminSiteContent.includes('aria-label="轮播文案手动换行预览"')
    && adminSiteContent.includes('.hero-copy-preview>*{display:block;width:max-content;max-width:none;margin:0 0 8px;white-space:pre;overflow-wrap:normal;word-break:normal}')
    && homeContent.includes("summary: String(item.summary ?? '')")
    && siteContentStore.includes('ValidateHeroCopyString(slide, "eyebrow", 80)')
    && siteContentStore.includes("character is not ('\\r' or '\\n')"), '轮播前台和后台预览必须只按管理员输入的换行符断行，不得随视口自动折行；后台须逐字段提示最长单行并保留内部换行'],
  [!hasUndersizedSiteWorkbenchText && adminSiteContent.includes('font-size:14px')
    && adminArticles.includes('font-size:14px') && mediaUploadField.includes('font-size:14px')
    && articleDocumentEditor.includes('font-size:16px'), '站点内容工作台、素材上传与正文编辑器不得恢复 9–11px 密集排版；正文画布应为16px，控件与辅助文字至少12px并保留分组间距'],
  [adminPage.includes('对局与数据') && adminPage.includes('AdminMatchesPanel') && adminPage.includes('AdminCardAnalyticsPanel')
    && adminPage.includes("hasPermission('admin.matches.read')") && adminPage.includes("hasPermission('admin.analytics.read')")
    && platform.includes('/api/admin/matches') && platform.includes('/api/admin/analytics/cards'), '后台必须以独立权限和正式模块提供对局档案与单卡分析，不得塞入 Bug 管理或复用玩家私有记录接口'],
  [adminMatches.includes("view === 'recent'") && adminMatches.includes("view === 'player'")
    && adminMatches.includes('participant.deckCards') && adminMatches.includes('结构化对局时间线')
    && adminMatches.includes('进行中对局不展示私有构筑') && adminMatches.includes('沙盒不进入档案与分析'), '对局档案必须支持最近/按玩家查询、不可变构筑和权威时间线，并对进行中隐藏信息及沙盒数据失败关闭'],
  [adminCardAnalytics.includes('使用漏斗') && adminCardAnalytics.includes('构筑收录') && adminCardAnalytics.includes('实际抽到')
    && adminCardAnalytics.includes('从手牌打出') && adminCardAnalytics.includes('效果发动') && adminCardAnalytics.includes('正常结算')
    && adminCardAnalytics.includes('同条件基线') && adminCardAnalytics.includes('不把相关性描述成因果'), '单卡分析必须展示收录至结算漏斗、同条件基线、样本与相关性边界，禁止用裸胜率冒充卡牌因果影响'],
  [profilePage.includes('class="admin-button"') && profilePage.includes('⚙ 管理后台') && profilePage.includes('反馈 Bug 和建议') && profilePage.includes('本赛季排位') && !profilePage.includes('自设卡背'), '个人中心须以按钮提供管理后台入口并整合反馈与排位资料，且不得出现未规划的自设卡背功能'],
  [friendsPage.includes("tab === 'blocked'") && friendsPage.includes('friendApi.blocked()') && friendsPage.includes('selectedPresence?.canInvite') && friendsPage.includes('selectedPresence?.canSpectate'), '好友中心须支持申请、屏蔽，并按在线状态在邀请对战与观战之间切换'],
  [platform.includes('permissions?: string[]') && adminPage.includes("hasPermission('admin.bugs.read')") && adminPage.includes("hasPermission('admin.accounts.read')") && adminPage.includes("hasPermission('admin.operations.read')"), '后台前端入口必须消费服务端权限矩阵，不得只依赖散落角色字符串'],
  [platform.includes('let authRefreshPromise: Promise<PlatformAccount | null> | null = null') && platform.includes("platformRequest<PlatformAccount>('/api/auth/me')") && platform.includes('remember(account, requestToken)') && platform.includes('if (authRefreshPromise) return authRefreshPromise'), '账号初始化与权限刷新必须去重读取 /api/auth/me，并以权威响应覆盖本地缓存'],
  [platform.includes('response.status === 401 && requestToken && platformState.token === requestToken') && platform.includes('forgetAccount(requestToken)') && platform.includes('error instanceof PlatformRequestError && error.status === 401') && platform.includes('throw error'), '任意携带当前令牌的 401 必须按请求令牌防竞态清理，网络与 5xx 则保留令牌并保持未验证'],
  [platform.includes('response.status === 403 && requestToken && platformState.token === requestToken') && platform.includes('authState.verified = false') && platform.includes('refreshCurrentAccount({ force: true })') && platform.includes('if (!authState.verified) return false'), '403 必须使权限 UI 立即失败关闭并触发去重身份刷新，缓存身份不得直接授予权限'],
  [router.includes("meta: { requiresAdmin: true }") && router.includes('router.beforeEach(async to =>') && router.includes('refreshCurrentAccount({ force: true })') && router.includes("return { name: 'me', query: { redirect: to.fullPath } }") && adminPage.includes('await refreshCurrentAccount()') && adminPage.includes('if (!canAccessAdmin.value) return') && adminPage.includes('!authState.initialized || authState.refreshing'), '管理路由与 AdminPage 必须在加载管理数据前刷新权威身份，并在未验证或非管理员时失败关闭'],
  [platform.includes("'/api/auth/sessions/current'") && platform.includes("'/api/auth/sessions'") && profilePage.includes('登录设备与会话') && profilePage.includes('退出其他设备') && profilePage.includes('退出全部设备'), '账号安全页必须支持服务端会话列表、当前设备及全端撤销'],
  [platform.includes("path === '/api/auth/email/capability'") && platform.includes("path === '/api/auth/email/verify'") && platform.includes("path === '/api/auth/password/forgot'") && platform.includes("path === '/api/auth/password/reset'") && platform.includes("'/api/auth/email/bind'") && platform.includes("'/api/auth/email/unbind'") && profilePage.includes("authMode === 'login' && emailFeatureEnabled") && profilePage.includes('v-if="emailFeatureEnabled" class="email-manager"'), '邮箱关闭时必须隐藏绑定与找回入口；显式启用后仍保留完整端点且匿名恢复请求不得携带现有登录令牌'],
  [router.includes("path: '/auth/recovery'") && recoveryPage.includes("location.hash.replace(/^#/, '')") && recoveryPage.includes("history.replaceState(null, '',") && recoveryPage.includes('!emailFeatureEnabled') && recoveryPage.includes('邮箱绑定、验证与找回功能当前未开放') && recoveryPage.includes('确认验证邮箱') && !recoveryPage.includes('submitVerification() }'), '恢复页必须先读取服务端能力并在关闭时失败关闭；启用时从 URL fragment 读取并立即清除令牌，且不得自动消费一次性令牌'],
  [platform.includes('resetAccountPassword:') && platform.includes('deleteAccount:') && adminPage.includes('临时密码 123456') && adminPage.includes('删除与清理') && adminPage.includes('根 Admin 与操作者自身受保护'), '管理后台必须提供受保护的临时密码重置、全会话撤销与逻辑删除个人数据清理入口'],
  [platform.includes('mustChangePassword?: boolean') && router.includes('platformState.account.mustChangePassword') && profilePage.includes('必须修改密码'), '管理员重置后的账号必须被导航守卫限制到我的页并明确要求修改临时密码'],
  [platform.includes('options: { revokeServer?: boolean } = {}') && profilePage.includes('logout({ revokeServer: false })'), '服务端已撤销当前或全部会话后必须只清理本机状态，不得用失效令牌重复调用撤销接口'],
  [platform.includes('revokeSession: (id: string, sessionId: string)') && platform.includes('/sessions/${encodeURIComponent(sessionId)}') && adminPage.includes('revokeAccountSessions') && adminPage.includes('撤销会话'), '管理员必须能按账号撤销服务端会话'],
  [platform.includes("headers.set('X-Correlation-ID'") && platform.includes('PlatformRequestError') && adminPage.includes('关联 ID：'), 'HTTP 请求、错误提示与管理审计必须贯通关联 ID'],
  [platform.includes('/api/admin/v1/commands') && platform.includes('/api/admin/v1/approvals') && adminPage.includes('管理操作记录') && adminPage.includes('受控发布待复核') && adminPage.includes('失败：'), '后台必须提供持久命令、受控发布复核、命令详情与失败原因入口'],
  [platform.includes('/api/admin/v1/content/publish') && platform.includes('/api/admin/v1/content/rollback') && adminSiteContent.includes('直接发布') && adminSiteContent.includes('直接回滚') && !adminSiteContent.includes('双人审批'), '官网内容必须通过服务端批量命令直接发布与回滚，不得恢复前端逐键发布或双人审批'],
  [adminSiteContent.includes('previewContent') && adminSiteContent.includes('发布预览完成') && adminSiteContent.includes('wouldChange') && adminSiteContent.includes('未写入线上内容'), '内容后台必须展示不写入的发布预览与变化摘要'],
  [adminPage.includes('auditCommandId') && adminPage.includes('auditCorrelationId') && adminPage.includes('auditOutcome'), '审计页必须可按结果、命令 ID 与关联 ID 筛选'],
  [platform.includes("releaseArtifacts: () => platformRequest<VerifiedReleaseArtifact[]>('/api/admin/v1/releases/artifacts')") && !platform.includes('registerReleaseArtifact') && adminPage.includes('Web 端没有注册入口'), '发布后台只能读取适配器提供的已验证工件，不得提供客户端工件注册或自报 verified 入口'],
  [platform.includes('/api/admin/v1/releases/deploy') && platform.includes('/api/admin/v1/releases/rollback') && adminPage.includes('发布 dry-run') && adminPage.includes('提交双人审批') && adminPage.includes('提交回滚审批'), '发布与回滚必须支持 dry-run、环境版本和双人审批入口'],
  [platform.includes("releaseEnvironments: () => platformRequest<ReleaseEnvironment[]>('/api/admin/v1/releases/environments')") && adminPage.includes('运行态只读快照') && adminPage.includes('WebSocket 冒烟') && adminPage.includes('发布、失败与回滚记录'), '运行态必须来自显式只读适配器快照，并展示健康、WS 冒烟、失败和回滚记录'],
  [platform.includes('disabled?: boolean') && platform.includes('/status`, {') && adminPage.includes('账号变更立即执行并完整审计') && adminPage.includes('撤销会话'), '账号禁用/启用必须直接执行、记录版本审计，并提供旧令牌与 WebSocket 会话撤销入口'],
  [platform.includes("securityStatus: () => platformRequest<SecurityStatus>('/api/admin/v1/security/status')") && platform.includes('/api/admin/v1/security/audit-archives') && adminPage.includes('高风险审计') && adminPage.includes('恢复演练'), '后台必须展示安全告警、独立审计归档 dry-run/复核与恢复演练入口'],
  [platform.includes("mfaCapability = () => platformRequest<MfaCapability>('/api/auth/mfa/capability')") && profilePage.includes('v-if="mfa?.enrollmentEnabled"') && !profilePage.includes('不会收集或保存 MFA 密钥'), 'MFA 未开放时必须隐藏工程提示，只有服务端明确开放注册能力后才显示入口'],
  [adminPage.includes('仅服务器 CLI 可用') && adminPage.includes('发布复核离线恢复') && !platform.includes('bootstrapSecondApprover'), '受控发布恢复只能保留离线命令边界，不得新增 Web 凭据入口'],
  [platform.includes('export const tournamentApi') && platform.includes('/api/tournaments/import-legacy') && platform.includes('/matches/${encodeURIComponent(matchId)}/rulings'), '赛事中心必须通过服务端 API 完成赛事、旧数据导入与裁判写入'],
  [tournamentCenter.includes('预览导入（dry-run）') && tournamentCenter.includes('确认导入') && tournamentCenter.includes('legacyPreview.value.previewHash') && !tournamentCenter.includes('localStorage.setItem'), '本机旧赛事只能显式预览并确认导入，不得继续作为 localStorage 权威状态写回'],
  [tournamentCenter.includes('organizerAccountId === accountId.value') && tournamentCenter.includes('person.accountId===accountId') && tournamentCenter.includes('主办者与裁判权限仅在当前赛事内生效') && !tournamentCenter.includes('待审批命令'), '赛事主办者、裁判临时身份、牌库范围与并发写入必须使用服务端账号 ID 和赛事版本，且不得进入账号角色审批'],
  [tournamentCenter.includes("tournamentApi.getByCode(item.code)") && tournamentCenter.includes('选择牌库并报名') && tournamentCenter.includes("link.searchParams.set('code', item.code)") && tournamentCenter.includes('瑞士排名快照') && tournamentCenter.includes('v-for="snapshotRound in standingSnapshots"') && l12Net.includes("type: 'enterTournamentMatch'") && l12Net.includes("type: 'spectateTournamentMatch'") && profilePage.includes("!route.query.redirect.startsWith('//')") && gamePage.includes('@click="returnToLobby">返回赛事/大厅'), '赛事稳定分享链接必须在登录后按码打开详情并直接选择账号牌库报名；玩家/工作人员须按配对身份进入或观战，每轮排名快照可查，且等待对手时也能退出专属房，不得恢复房间码输入'],
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
