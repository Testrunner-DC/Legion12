import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { normalizeArticleBlockIds, serializeArticleBody } from '../src/l12/site/articleBlocks.ts'
import { maintenanceCountdown } from '../src/l12/site/maintenanceCountdown.ts'

// Git 在 Windows 工作区可能检出 CRLF；契约按语义比较换行，不改写被检查的源文件。
const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const shell = read('../src/l12/site/SiteShell.vue')
const mainNav = shell.match(/const mainNav = \[[\s\S]*?\n\]/)?.[0] ?? ''
const battleNav = shell.match(/const battleNav = \[[\s\S]*?\n\]/)?.[0] ?? ''
const router = read('../src/router/index.ts')
const boardComponent = read('../src/l12/game/GameBoard.vue')
const boardMobileStyle = read('../src/l12/game/GameBoard.mobile.css')
const board = `${boardComponent}\n${boardMobileStyle}`
const battleViewportLayout = read('../src/l12/game/battleViewportLayout.ts')
const mobileViewportStyle = read('../src/l12/mobileViewport.css')
const mobileViewportCheck = read('./test-mobile-viewport.mjs')
const phaseTrack = read('../src/l12/game/PhaseTrack.vue')
const battleLog = read('../src/l12/game/BattleEventLog.vue')
const battleLogViewModel = read('../src/l12/game/logViewModel.ts')
const battleDock = read('../src/l12/game/BattleUtilityDock.vue')
const actionLayer = read('../src/l12/game/ActionPresentationLayer.vue')
const actionPresentation = read('../src/l12/game/actionPresentation.ts')
const actionAudio = read('../src/l12/game/useL12ActionAudio.ts')
const zoneMovementLayer = read('../src/l12/game/ZoneMovementPresentationLayer.vue')
const combatMotionLayer = read('../src/l12/game/CombatMotionPresentationLayer.vue')
const osirisVictory = read('../src/l12/game/OsirisVictorySequence.vue')
const indexHtml = read('../index.html')
const faviconPath = new URL('../public/favicon.png', import.meta.url)
const blackLotusPath = new URL('../public/assets/l12/special/logo/black-lotus.png', import.meta.url)
const globalStyle = read('../src/style.css')
const siteUiSystem = read('../src/l12/site/uiSystem.css')
const prompt = read('../src/l12/game/PromptOverlay.vue')
const promptCardCandidate = read('../src/l12/game/PromptCardCandidate.vue')
const matchRecords = read('../src/l12/MatchRecords.vue')
const replayPage = read('../src/l12/ReplayPage.vue')
const replayModel = read('../src/l12/replayModel.ts')
const visualLayoutCheck = read('./verify-batch253-visual.mjs')
const responsiveTypeCheck = read('./verify-batch282-responsive-type.mjs')
const gameActions = read('../src/l12/game/GameActions.vue')
const lobby = read('../src/l12/site/BattleHubPage.vue')
const rankings = read('../src/l12/site/RankingsPage.vue')
const rankedIdentityBadge = read('../src/l12/RankedIdentityBadge.vue')
const rankedTicker = read('../src/l12/site/RankedBroadcastTicker.vue')
const rankedPlayback = read('../src/l12/site/rankedBroadcastPlayback.ts')
const adminOperations = read('../src/l12/site/AdminOperationsPanel.vue')
const disasterPoolPicker = read('../src/l12/site/DisasterPoolPicker.vue')
const savedDeckSelector = read('../src/l12/SavedDeckSelector.vue')
const deckEditor = read('../src/l12/L12DeckEditor.vue')
const gamePage = read('../src/l12/GamePage.vue')
const app = read('../src/App.vue')
const gameReentry = read('../src/l12/gameReentry.ts')
const backgroundMusic = read('../src/l12/backgroundMusic.ts')
const settingsModal = read('../src/l12/site/L12SettingsModal.vue')
const deckConstructionBrowser = read('../src/l12/site/DeckConstructionBrowser.vue')
const homePublishedCache = read('../src/l12/site/homePublishedCache.ts')
const mainEntry = read('../src/main.ts')
const playerMat = read('../src/l12/game/PlayerMat.vue')
const handArea = read('../src/l12/game/HandArea.vue')
const playerTurnClock = read('../src/l12/game/PlayerTurnClock.vue')
const adminIntegrity = read('../src/l12/site/AdminRankedIntegrityPanel.vue')
const graveyardOverlay = read('../src/l12/game/GraveyardOverlay.vue')
const masterOverlay = read('../src/l12/game/MasterOverlay.vue')
const globalBugFeedback = read('../src/l12/site/GlobalBugFeedback.vue')
const specialAssets = read('../src/l12/specialAssets.ts')
const cardTile = read('../src/l12/CardTile.vue')
const cardPresentation = read('../src/l12/cardPresentation.ts')
const l12Types = read('../src/l12/types.ts')
const cardArchive = read('../src/l12/CardArchive.vue')
const mobileDeferredCardImage = read('../src/l12/MobileDeferredCardImage.vue')
const cardDetailContent = read('../src/l12/CardDetailContent.vue')
const cardArchiveVersions = read('../src/l12/cardArchiveVersions.ts')
const galleryMarkup = cardArchive.match(/<template v-else>([\s\S]*?)<div v-if="!filteredGallery\.length"/)?.[1] ?? ''
const sandbox = read('../src/l12/site/SandboxPage.vue')
const gmPanel = read('../src/l12/game/GmPanel.vue')
const sandboxPicker = read('../src/l12/SingleCardPicker.vue')
const l12Net = read('../src/l12/net.ts')
const adminPage = read('../src/l12/site/AdminPage.vue')
const adminAlternateArts = read('../src/l12/site/AdminAlternateArtsPanel.vue')
const constructionRuleEditor = read('../src/l12/site/ConstructionRuleEditor.vue')
const adminArticles = read('../src/l12/site/AdminArticlesPanel.vue')
const adminSiteContent = read('../src/l12/site/AdminSiteContentPanel.vue')
const mediaUploadField = read('../src/l12/site/MediaUploadField.vue')
const articleBlockEditor = read('../src/l12/site/ArticleBlockEditor.vue')
const articleDocumentEditor = read('../src/l12/site/ArticleDocumentEditor.vue')
const articleBlockModel = read('../src/l12/site/articleBlocks.ts')
const articleRenderer = read('../src/l12/site/ArticleContentRenderer.vue')
const adminMatches = read('../src/l12/site/AdminMatchesPanel.vue')
const adminCardAnalytics = read('../src/l12/site/AdminCardAnalyticsPanel.vue')
const matchGovernance = read('../src/l12/matchGovernance.ts')
const adminMatchGovernance = read('../src/l12/site/AdminMatchGovernancePanel.vue')
const friendsPage = read('../src/l12/site/FriendsPage.vue')
const officialHome = read('../src/l12/site/OfficialHomePage.vue')
const newsPage = read('../src/l12/site/NewsPage.vue')
const homeContent = read('../src/l12/site/homeContent.ts')
const profilePage = read('../src/l12/site/ProfilePage.vue')
const profileAuthForm = profilePage.match(/<form class="account-form auth-form"[\s\S]*?<\/form>/)?.[0] ?? ''
const recoveryPage = read('../src/l12/site/AccountRecoveryPage.vue')
const ruleCenter = read('../src/l12/site/RuleCenterPage.vue')
const platform = read('../src/l12/platform.ts')
const audioPreferencesModule = read('../src/l12/audioPreferences.ts')
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
const l12TrialAdvancePlans = read('../../服务端WebSocket/TwelveLegions/L12TrialAdvanceEffectPlans.cs')
const l12GameEngine = read('../../服务端WebSocket/TwelveLegions/L12GameEngine.cs')
const l12AdminControlPlane = read('../../服务端WebSocket/TwelveLegions/L12AdminControlPlane.cs')
const l12StructuredSemantics = read('../../服务端WebSocket/TwelveLegions/L12StructuredCardRules.StatusSemantics.cs')
const l12OperationsStore = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.Operations.cs')
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
const deployTarget = read('../../ops/windows/L12DeployTarget.ps1')
if (!windowsVerify.includes('Source = "ops\\windows\\L12DeployTarget.ps1"; Target = "ops\\windows\\L12DeployTarget.ps1"')) {
  throw new Error('隔离发布构建必须复制部署目标校验文件，不得跳过新服务器白名单与指纹门禁。')
}
const serverDeploy = read('../../ops/server/deploy-l12-release.sh')
const nginxSite = read('../../ops/server/legion12-testrun.nginx')
const nginxHttpSite = read('../../ops/server/legion12-testrun-http.nginx')
const nginxTestrunPath = read('../../ops/server/legion12-testrun-path.nginx')
const nginxSharePages = read('../../ops/server/nginx-l12-share-pages.conf')
const activateSharePages = read('../../ops/server/activate-l12-share-pages.sh')
const bugQueue = read('../../ops/windows/Get-L12BugQueue.ps1')
const s1Cards = JSON.parse(read('../public/data/l12/cards.s1.json'))
const starterCards = JSON.parse(read('../public/data/l12/cards.st.json'))
const s2Cards = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/cards.s2.json'))
const archiveAssetCards = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/card-archive-assets.json')).cards
const presentationGalleryAssets = archiveAssetCards.filter(card => card.id !== 'S02-05C1B'
  && [/^S\d{2}-\d{4}[a-z]$/, /^S\d{2}-\d{2}[CM]\d+A$/, /^ST\d{2}-C1st$/].some(pattern => pattern.test(card.id)))
const moraleIdentities = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/morale-identities.json'))
const olympusDefaultMorale = s2Cards.find(card => card.id === 'S02-05C1')
const olympusAlternateArt = s2Cards.find(card => card.id === 'S02-05C1A')
const naturalGift = starterCards.find(card => card.id === 'ST06-10')

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
const maintenanceNow = Date.parse('2026-09-07T12:00:00Z')
const scheduledMaintenance = maintenanceCountdown({ enabled: true, message: '维护提示', broadcastMessage: '计划广播', startsAt: '2026-09-07T13:01:01Z' }, maintenanceNow)
const endingMaintenance = maintenanceCountdown({ enabled: true, message: '维护提示', broadcastMessage: '', startsAt: '2026-09-07T11:00:00Z', endsAt: '2026-09-07T12:01:01Z' }, maintenanceNow)
const openEndedMaintenance = maintenanceCountdown({ enabled: true, message: '维护提示', broadcastMessage: '' }, maintenanceNow)

const contracts = [
  [mainEntry.includes("import './l12/site/uiSystem.css'")
    && shell.includes('data-l12-ui-system="site-v1"')
    && siteUiSystem.includes('--l12-ui-panel:') && siteUiSystem.includes('--l12-ui-control:')
    && siteUiSystem.includes('--l12-ui-radius-lg:') && siteUiSystem.includes('--l12-ui-shadow-dialog:')
    && siteUiSystem.includes('Immersive battle/replay and deck-editor'),
  '普通站点页面必须共用图鉴/主页基准的颜色、面板、控件、圆角、间距与弹框令牌，并与沉浸式对战/回放/构筑几何隔离'],
  [gmPanel.includes('SingleCardPicker') && adminCardAnalytics.includes('SingleCardPicker')
    && adminAlternateArts.includes('SingleCardPicker') && constructionRuleEditor.includes('SingleCardPicker')
    && boardComponent.includes('SingleCardPicker')
    && ![gmPanel, adminCardAnalytics, adminAlternateArts, constructionRuleEditor, boardComponent]
      .some(source => source.includes('SandboxCardPicker') || source.includes('FilteredSingleCardPicker')),
  'GM、单卡分析、异画、构筑规则和自定天灾必须复用唯一共享单卡选择器'],
  [scheduledMaintenance?.phase === 'scheduled' && scheduledMaintenance.countdown === '距离维护开始 01:01:01'
    && scheduledMaintenance.message === '维护提示'
    && endingMaintenance?.phase === 'active' && endingMaintenance.countdown === '距离维护结束 00:01:01'
    && openEndedMaintenance?.countdown === '维护进行中 · 结束时间待定'
    && maintenanceCountdown({ enabled: false, message: '旧计划', broadcastMessage: '', startsAt: '2026-09-07T13:00:00Z' }, maintenanceNow) === null
    && maintenanceCountdown({ enabled: true, message: '已结束', broadcastMessage: '', endsAt: '2026-09-07T12:00:00Z' }, maintenanceNow) === null,
    '维护倒计时必须区分计划、有限维护、无结束时间、停用及已结束状态'],
  [mainNav.includes("{ to: '/cards', icon: 'archive', label: '图鉴' }") && !mainNav.includes("label: '卡牌图鉴'")
    && battleNav.includes("{ to: '/', icon: 'home', label: '主页' }")
    && battleNav.includes("{ to: '/battle/tournaments', icon: 'tournament', label: '赛事' }")
    && battleNav.includes("{ to: '/battle/rankings', icon: 'ranking', label: '排行' }")
    && battleNav.includes("{ to: '/battle/records', icon: 'records', label: '对局' }")
    && shell.includes('.site-utilities{display:flex;flex-direction:column;gap:8px')
    && shell.includes('grid-template-columns:1fr 1fr;column-gap:0;row-gap:8px'),
    '官网与对战侧栏必须只缩短导航标签，底部四项工具按约8px纵向分隔且移动两列不扩大列间距'],
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
    && cardDetailContent.includes('props.card.products') && cardDetailContent.includes('收录产品')
    && cardDetailContent.includes('v-if="showCatalogOnly"')
    && cardArchive.includes('page.value === \'gallery\' ? galleryCards.value.length : logicalCards.value.length')
    && !cardArchive.includes('/ {{ cards.length }} 张'), '卡牌图鉴必须按权威收录产品载入展示用异画、区分奥林匹斯士气与神力编号、按任一版本搜索筛选，并按当前子页口径计数'],
  [cardArchiveVersions.includes('compareArchiveVersions') && cardArchiveVersions.includes('Number(Boolean(a.archiveBaseCardId)) - Number(Boolean(b.archiveBaseCardId))')
    && cardArchiveVersions.includes('productRank(a.product)')
    && cardArchiveVersions.includes('rarityValue(a.rarity)') && cardArchiveVersions.includes("if (!rarity?.trim()) return 100")
    && cardArchiveVersions.includes('defaultVersion: versions[0]'), '逻辑卡默认版本必须先选无异画基底的规则卡，再稳定按最早产品、最低已知罕贵度及卡号排序；异画不得抢占默认卡图'],
  [cardArchive.includes('class="archive-card-image"') && cardArchive.includes('class="archive-version-arrow previous"')
    && cardArchive.includes('class="archive-version-arrow next"') && cardArchive.includes('@click.stop="cycleVersion(entry, -1)"')
    && cardArchive.includes('@click.stop="cycleVersion(entry, 1)"') && cardArchive.includes(':card-id="displayedVersion(entry).id"')
    && cardArchive.includes(':card="selectedDetailCard"') && cardDetailContent.includes(':card-id="card.id"')
    && !cardDetailContent.includes('archive-version-arrow')
    && globalStyle.includes('.archive-version-arrow{') && globalStyle.includes('background:transparent'), '卡牌图鉴必须在中间结果卡图上以左右透明三角切换版本，并同步更新卡位与详情；详情区不得保留第二套切换按钮'],
  [presentationGalleryAssets.length === 41
    && presentationGalleryAssets.every(card => card.baseCardId && card.id !== 'S02-05C1B')
    && s2Cards.some(card => card.id === 'S02-05C1A')
    && cardArchive.includes("card.id !== 'S02-05C1B'")
    && cardArchive.includes("Boolean(card.archiveBaseCardId) || card.id === 'S02-05C1A'")
    && cardArchive.includes('cards.value.filter(isGalleryVariant)')
    && cardArchive.includes('v-for="card in filteredGallery" :key="card.id"')
    && !galleryMarkup.includes('archive-version-arrow'), '画廊必须逐卡展示41张登记展示资源与S02-05C1A，共42张异画；主宰异画编号不得写死为M1A；S02-05C1A在图鉴并入默认士气S02-05C1的版本切换、在画廊独立展示，不得成为新规则身份，也不得误收规则独立的奥林匹斯神力B面'],
  [cardArchive.includes("import MobileDeferredCardImage from './MobileDeferredCardImage.vue'")
    && (cardArchive.match(/<MobileDeferredCardImage/g) ?? []).length === 2
    && mobileDeferredCardImage.includes("window.matchMedia('(max-width: 900px), (pointer: coarse)').matches")
    && mobileDeferredCardImage.includes("rootMargin: '640px 0px'")
    && mobileDeferredCardImage.includes('const callbacks = new WeakMap<Element, () => void>()')
    && !cardArchive.includes('visibleCatalogRows') && !cardArchive.includes('visibleGalleryRows')
    && !cardArchive.includes('archive-group-nav') && !cardArchive.includes('renderLimit')
    && cardArchive.includes('v-for="entry in filteredCatalog"') && cardArchive.includes('v-for="card in filteredGallery"'), '图鉴必须恢复原有连续卡位与交互，仅在移动/粗指针设备按可视区延迟创建卡图，不得用分组、分页或截断改变浏览体验'],
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
    && cardArchive.includes('modalTrigger?.focus()') && cardArchive.includes(':card="modalDetailCard"')
    && cardArchive.includes('layout="modal"') && cardArchive.includes('title-id="archive-modal-title"')
    && cardDetailContent.includes('hasCostDimension(card)')
    && cardDetailContent.includes('card.troops !== undefined') && cardDetailContent.includes('card.hp !== undefined')
    && cardDetailContent.includes('card.disasterLevel !== undefined') && cardDetailContent.includes('card.trialValue !== undefined')
    && cardDetailContent.includes('<p class="l12-effect-body">{{ card.effect')
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
    && cardDetailContent.includes('hasCostDimension(card)'), '主宰只有血量维度；卡牌图鉴、筛选与详情不得把错误源数据中的数值展示为费用或兵力'],
  [naturalGift && naturalGift.cardType === 'tactic' && naturalGift.cost == null
    && decks.includes('export function filterableCardCost') && decks.includes("card.cardType === 'master' ? null : (card.cost ?? 0)")
    && cardArchive.includes('filterableCardCost(card)') && deckEditor.includes('filterableCardCost(card)')
    && sandboxPicker.includes('filterableCardCost(card.detailCard)'), '没有印刷费用的非主宰卡必须统一归入0费筛选；自然馈赠须能在图鉴、构筑与共享单卡卡查的0费条件中被找到'],
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
  [actionPresentation.includes("event.type === 'turn-start'") && actionPresentation.includes("event.text === '进入主要阶段'") && actionPresentation.includes("event.text === '执行结束阶段'")
    && actionPresentation.includes("event.type === 'move' && Boolean(event.effectSceneId)") && actionPresentation.includes("event.effectText?.trim() || event.text")
    && !actionPresentation.includes("执行抽牌阶段") && !actionPresentation.includes("执行士气阶段") && !actionPresentation.includes("event.type === 'play'") && !actionPresentation.includes("event.type === 'attack'"), '统一动作条只承载阶段变化及带权威场景标识的规则动作；普通位移和卡效不得误入'],
  [l12Types.includes('ruleActions?: Array<{ id: string; label: string; text: string; enabled?: boolean; disabledReason?: string; presentationSceneId?: string; targetKeys?: string[] }>')
    && playerMat.includes("card.ruleActions?.find(action => action.id === 'cavalryMove')")
    && playerMat.includes(':disabled="!canCavalryMove(player.field[row][slot]!)"')
    && playerMat.includes("?.disabledReason || cavalryMoveAction")
    && playerMat.includes("authority.targetKeys.includes(`${row}:${slot}`)")
    && playerMat.indexOf('if (authority) return') < playerMat.indexOf("card.profession !== '骑兵'"), '骑兵位移按钮必须优先读取服务端规则动作资格、禁用原因和文案，旧服务端兼容判断不得覆盖权威投影'],
  [board.includes("import ZoneMovementPresentationLayer from './ZoneMovementPresentationLayer.vue'") && board.includes('<ZoneMovementPresentationLayer :events="game.recentEvents ?? []"') && zoneMovementLayer.includes('data-ui-contract="authoritative-zone-card-movement"') && zoneMovementLayer.includes('viewportRect(element)') && read('../src/l12/mobileViewport.ts').includes('element.getBoundingClientRect()') && zoneMovementLayer.includes('data-l12-game-stage') && zoneMovementLayer.includes('data-card-instance-id') && zoneMovementLayer.includes('await nextTick()'), '打出、登场与区域移动必须读取更新前后真实 DOM 锚点并由独立卡牌实体连续飞行'],
  [zoneMovementLayer.includes('source.cloneNode(true)') && zoneMovementLayer.includes('wrapper.animate([') && !zoneMovementLayer.includes('--move-mid-x') && !zoneMovementLayer.includes('l12-zone-card-pulse'), '卡牌跨区域动画必须复用来源实体快照并做单次起终点位移，不得恢复中途放大、脉冲或三段跳变'],
  [zoneMovementLayer.includes('prepareMovementImage') && zoneMovementLayer.includes('resolveCardAssetUrls') && zoneMovementLayer.includes('preparedImageUrl') && zoneMovementLayer.includes(".filter(url => url !== CARD_IMAGE_PLACEHOLDER)"), '无来源实体的区域移动必须在动画前预解码真实卡图，XII占位只能作为全部候选失败后的最终兜底'],
  [board.includes("import CombatMotionPresentationLayer from './CombatMotionPresentationLayer.vue'") && board.includes('<CombatMotionPresentationLayer :events="game.recentEvents ?? []"')
    && combatMotionLayer.includes("event.type === 'attack'") && combatMotionLayer.includes("event.type === 'combat' || isDefeatLeave(event)")
    && combatMotionLayer.includes('fieldSnapshots') && combatMotionLayer.includes('defeatedInstances')
    && combatMotionLayer.includes("power.textContent = '0'") && combatMotionLayer.includes('l12-defeat-damage')
    && combatMotionLayer.includes("zoneElement('graveyard', owner)")
    && combatMotionLayer.includes('function combatDamageValue') && combatMotionLayer.includes('function defeatLabel')
    && combatMotionLayer.includes("return /击杀|消灭/.test") && combatMotionLayer.includes('/即将阵亡|代替承受|返回手牌|回到手牌|位移|移动|转移|弃置/')
    && combatMotionLayer.includes('confirmedDefeatIds.has(captured.card.instanceId)')
    && combatMotionLayer.includes("Number(right.event.type === 'combat')")
    && combatMotionLayer.includes("fontFamily: \"'Microsoft YaHei','微软雅黑',sans-serif\"")
    && !combatMotionLayer.includes('captured.card.baseTroops') && !combatMotionLayer.includes("font: '900 20px monospace'"), '进攻必须有轻量前冲；战斗或效果阵亡要使用前态快照去重；真实战斗优先采用权威战斗伤害，效果击杀不得猜测数值，返回/位移/弃置/替代不得误播阵亡'],
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
  [indexHtml.includes('<!-- l12-share-meta:start -->') && indexHtml.includes('<!-- l12-share-meta:end -->')
    && indexHtml.includes('property="og:title"') && indexHtml.includes('property="og:description"')
    && indexHtml.includes('property="og:image"') && indexHtml.includes('property="og:url"')
    && l12ServerSources.includes('internal static partial class L12SharePage')
    && l12ServerSources.includes('L12_PUBLIC_BASE_URL') && l12ServerSources.includes('PublicArticle(articleKey)')
    && l12ServerSources.includes('article.BodyMedia?.FirstOrDefault()?.DesktopUrl')
    && nginxSharePages.includes('location = /news')
    && nginxSharePages.includes('proxy_pass http://127.0.0.1:8083/_l12/share-page;')
    && nginxSite.includes('proxy_pass http://127.0.0.1:8084/_l12/share-page;')
    && nginxTestrunPath.includes('location ~ ^/testrun/news/[^/]+/?$')
    && nginxTestrunPath.includes('rewrite ^ /_l12/share-page break;')
    && nginxTestrunPath.includes('proxy_pass http://127.0.0.1:8084;')
    && activateSharePages.includes('include /etc/nginx/snippets/legion12-share-pages.conf;')
    && windowsDeploy.includes('$sharePageActivator') && windowsDeploy.includes('$sharePageSnippet'),
    '主页和资讯分享必须由服务端基于已发布快照注入完整OG信息，正式服与测试路径都要接入动态HTML且由部署流程启用'],
  [board.includes('data-ui-contract="opponent-status-safe-lane"') && board.includes('data-ui-contract="player-status-safe-lane"')
    && board.includes('opponent-player-clock') && board.includes('my-player-clock')
    && board.includes(':active="game.activePlayer === viewEnemy.playerIndex"') && board.includes(':active="game.activePlayer === viewMe.playerIndex"')
    && board.includes('.board-player-clock{position:relative;right:auto;top:auto;bottom:auto}')
    && board.includes("'timed-board': Boolean(l12State.rankedClock)") && board.includes('.board-center>.l12-hand{position:relative;z-index:40;box-sizing:border-box;width:calc(100% - 400px)')
    && !globalStyle.includes("content:'回合玩家'"), '双方回合标识与常驻计时必须位于棋盘上下的普通流安全轨道，避开双方手牌和操作条，当前回合只能控制高亮'],
  [playerTurnClock.includes('data-ui-contract="persistent-player-turn-clock"') && playerTurnClock.includes('总时') && playerTurnClock.includes('本次') && playerTurnClock.includes('重连')
    && playerTurnClock.includes('.player-turn-clock{box-sizing:border-box;display:grid;width:138px;')
    && playerTurnClock.includes("'untimed-clock': !clock") && playerTurnClock.includes('.player-turn-clock.untimed-clock{width:130px;min-height:0;padding:5px}')
    && !playerTurnClock.includes('无时限') && !playerTurnClock.includes('v-if="active"'), '双方回合玩家框必须常驻；排位显示总操作、本次操作或重连倒计时，无计时房间只保留回合玩家/等待回合并收缩'],
  [board.includes('data-ui-contract="complete-player-summary"') && board.includes('class="player-summary-primary"') && board.includes('class="player-summary-meta"') && board.includes('class="rank-number">第 {{ enemyBadge.rank }} 名') && board.includes('class="title-badge" variant="master-title"')
    && board.includes('v-if="battleTierLabel(enemyBadge)"') && board.includes('v-if="identityLabel(enemyBadge?.placementTitle)"') && board.includes('v-if="identityLabel(enemyBadge?.masterTitle)"')
    && board.includes('v-if="battleTierLabel(myBadge)"') && board.includes('v-if="identityLabel(myBadge?.placementTitle)"') && board.includes('v-if="identityLabel(myBadge?.masterTitle)"')
    && board.includes("return badge?.rank && label === '冠冕' ? '' : label")
    && board.includes("absentIdentityLabels = new Set(['未定级', '暂无段位', '无段位', '未评级', '暂无称号', '无称号', '未获得称号', '暂无'])")
    && !board.includes("|| '未定级'") && !board.includes("|| '暂无称号'")
    && board.includes('connectionLabel(viewEnemy.playerIndex)') && board.includes('connectionLabel(viewMe.playerIndex)')
    && !board.includes('<dt>主宰</dt>') && !board.includes('<dt>血量</dt>')
    && board.includes('.player-panel{box-sizing:border-box;height:auto!important;min-height:140px') && board.includes('.player-summary-meta{display:grid;')
    && board.includes('grid-template-columns:minmax(0,1fr);justify-items:start')
    && board.includes('.player-summary-meta>:is(.rank-badge,.placement-title-badge,.title-badge){width:max-content;min-width:0;max-width:100%}')
    && board.includes('.player-summary-meta>.connection-state{min-width:0;max-width:100%!important;') && board.includes('margin-left:0!important;')
    && rankings.includes("import RankedIdentityBadge from '@/l12/RankedIdentityBadge.vue'") && rankings.includes('<RankedIdentityBadge v-for="title in row.titles"') && rankings.includes(':variant="titleVariant(title)"')
    && profilePage.includes("import RankedIdentityBadge from '@/l12/RankedIdentityBadge.vue'") && profilePage.includes(':variant="profileTitleVariant(title)"')
    && rankedIdentityBadge.includes("variant?: 'tier' | 'faction-title' | 'master-title'") && rankedIdentityBadge.includes('--ranked-tier-badge-font-size: 15px;') && rankedIdentityBadge.includes('--ranked-title-badge-font-size: 15px;')
    && rankedIdentityBadge.includes('faction-order') && rankedIdentityBadge.includes('faction-chaos') && rankedIdentityBadge.includes('faction-fate')
    && rankedIdentityBadge.includes("const siteBrandIcon = '/favicon.png'") && rankedIdentityBadge.includes('class="identity-brand-logo"') && rankedIdentityBadge.includes("props.variant === 'faction-title' ? '◆'")
    && rankedIdentityBadge.includes('linear-gradient(135deg, #b47716 0%, #6f3d08 48%, #3a1d02 100%)')
    && rankedIdentityBadge.includes('color: inherit !important;') && rankedIdentityBadge.includes('max-width: none;') && rankedIdentityBadge.includes('text-overflow: clip;'), '排行榜、个人页和对战摘要必须复用同一排位身份徽章；对战摘要按全服名次、段位、派系段位称号、主宰称号分字段显示，缺失项不伪造占位'],
  [adminIntegrity.includes('data-ui-contract="ranked-integrity-review"') && adminIntegrity.includes('不自动扣减七曜') && adminIntegrity.includes('建议人工核对'), '防刷分信号必须只进入管理员人工复核，不得自动惩罚正常重复对局'],
  [shell.includes('friendApi.request(player.accountId)') && shell.includes('inviteFriend(player.accountId)') && shell.includes('spectateRoom(player.roomCode)')
    && shell.includes("player.activity === 'playing'") && shell.includes(':disabled="!player.canSpectate"')
    && shell.includes('const incomingRequestCount = computed') && shell.includes('class="utility-unread"')
    && shell.includes("player.friendStatus === 'accepted'") && shell.includes('>好友 · 邀战</button>') && shell.includes('>观战</button>'), '在线玩家窗口必须支持相邻的好友与观战操作，并在侧栏及玩家行提示未读好友申请'],
  [shell.includes('friendApi.resolve(player.accountId, accept)') && shell.includes("player.friendDirection === 'incoming'") && shell.includes('resolveOnlineFriend(player, false)') && shell.includes('resolveOnlineFriend(player, true)') && shell.includes('>拒绝</button>') && shell.includes("'接受'"), '在线玩家窗口必须允许直接接受或拒绝收到的好友申请，不能只显示待处理状态'],
  [!shell.includes('/assets/l12/card-back-navy.png'), '主页入口不得回退为卡背'],
  [shell.includes("{ to: '/battle', icon: 'battle', label: '大厅' }") && !shell.includes("label: '对战主页'") && router.includes("{ path: '/battle', name: 'battle', component: () => import('@/l12/site/BattleHubPage.vue')") && router.includes("{ path: '/battle/lobby', redirect: '/battle' }"), '对战区域必须直接以大厅为主页，不得恢复多余的对战主页层级'],
  [l12Net.includes("matchFound: null as null") && l12Net.includes("type: 'syncState'")
    && l12Net.includes('if (!message.queued && l12State.matchFound) return')
    && l12Net.includes('requestMatchedState(socket)') && l12Net.includes('l12State.matchFound = null')
    && lobby.includes('data-ui-contract="match-found-state-recovery"')
    && wsServer.includes('ConcurrentDictionary<Guid, L12OutboundConnection> _outboundConnections')
    && wsServer.includes('outbound.TryEnqueue(queued, message.ReplaceableGameState)') && wsServer.includes('"syncState" =>')
    && l12ServerSources.includes('session.RoomCode is not null')
    && l12ServerSources.includes('return RecoveryStateAsync(sessionId);'), '排位与休闲匹配成功后必须进入显式建局加载态，忽略迟到的未排队消息并主动恢复房间/对局快照；服务端发送必须按会话串行化'],
  [board.includes('Array.from({ length: 4 }'), '本局天灾必须固定为四个槽位'],
  [board.includes('data-ui-contract="persistent-board-safe-layout"') && board.includes('data-ui-contract="phase-safe-track"') && board.includes('--l12-board-seam-safe-height:44px') && board.includes('--l12-battlefield-half-height:350px') && board.includes('min-height:calc(var(--l12-battlefield-half-height) * 2 + var(--l12-board-seam-safe-height) + 10px)') && board.includes('grid-template-rows:minmax(var(--l12-battlefield-half-height),1fr) var(--l12-board-seam-safe-height) minmax(var(--l12-battlefield-half-height),1fr)') && board.includes('class="battlefield-half opponent-half"') && board.includes('class="battlefield-half my-half"'), '双方战场与中央镜像分隔必须使用明确三轨安全布局；外层必须为既定六格高度留足空间，不得依赖格子溢出显示'],
  [board.includes('? { width: 2304, height: 1296 }') && board.includes(': { width: 2048, height: 1264 }')
    && board.includes('width: `${stageSize.width}px`') && board.includes('height: `${stageSize.height}px`')
    && board.includes('.board-stage{aspect-ratio:16/9}')
    && board.includes('grid-template-columns:340px 92px minmax(0,1fr) 320px')
    && board.includes('.right-rail{display:grid;grid-template-rows:auto minmax(0,1fr) auto')
    && board.includes('.right-rail .action-panel{max-height:300px;overflow:auto}')
    && board.includes('grid-template-rows:var(--l12-hand-lane-height) 70px minmax(0,1fr) 70px var(--l12-hand-lane-height)')
    && (board.match(/class="opponent-hand"/g) ?? []).length === 2
    && handArea.includes('const cardWidth = computed(() => 114.4)') && handArea.includes('.l12-hand .hand-card-wrap .card-tile{width:114.4px;height:160.6px;flex-basis:114.4px;border:1px solid transparent')
    && handArea.includes('.l12-hand.hidden .card-back{box-sizing:border-box;width:114.4px;height:160.6px}')
    && board.includes('align-self:stretch;justify-self:center;transform:translateX(-10px)')
    && board.includes('.board-center>.opponent-hand{grid-row:1}') && board.includes('.board-center>.l12-hand:last-child{grid-row:5}')
    && !board.includes('.battlefield-half.opponent-half{transform:rotate(180deg)')
    && !board.includes('.opponent-hand{transform:rotate(180deg)'), '对局舞台必须保持16:9主布局；独立窄阶段列与左、中、右区不得互相侵入，双方手牌和文字不得倒置'],
  [board.includes('data-ui-contract="left-current-disaster"') && !board.includes('<h3>当前天灾</h3>')
    && board.includes('class="left-card-column"') && board.includes('class="grand-panel phase-column"')
    && board.includes('.phase-column{display:flex;min-width:0;min-height:0;margin-block:242px;')
    && board.indexOf('data-ui-contract="left-current-disaster"') < board.indexOf('data-ui-contract="selected-card-inspector-anchor"')
    && board.includes('data-ui-contract="phase-disaster-value"') && !board.includes('<h3>阶段条</h3>')
    && board.includes('<PhaseTrack vertical ') && board.includes('<span class="board-midline-anchor" aria-hidden="true" />')
    && !board.includes('<div class="disaster-zone"') && !board.includes('<PhaseTrack :phase=')
    && phaseTrack.includes('vertical?: boolean') && phaseTrack.includes('{ vertical }')
    && phaseTrack.includes('.l12-phase-track.vertical{position:relative;inset:auto;display:grid')
    && phaseTrack.includes('writing-mode:horizontal-tb;transform:none')
    && !phaseTrack.includes('<i>{{ index + 1 }}</i>') && phaseTrack.indexOf('class="round"') > phaseTrack.indexOf('v-for="item in phases"'), '左侧本局天灾与当前天灾保持独立并合计对齐选中卡；窄阶段列独立右移，不显示标题或序号，两字换行居中且TURN置底'],
  [board.includes('class="mobile-current-disaster-value" aria-label="当前天灾值"')
    && board.includes('<span>天灾值</span><b>{{ game.disasterValue }}</b>')
    && board.includes('class="mobile-detail-handle-reservation" aria-hidden="true"')
    && board.includes('--l12-mobile-current-disaster-h:clamp(')
    && board.includes('--l12-mobile-disaster-value-h:clamp(')
    && board.includes('--l12-mobile-disaster-orb:clamp(')
    && board.includes('grid-template-rows:var(--l12-mobile-current-disaster-h) var(--l12-mobile-disaster-value-h) calc(var(--l12-mobile-disaster-orb) * 2')
    && board.includes('.mobile-landscape-board .mobile-current-disaster-value { grid-row: 2;')
    && board.includes('.mobile-landscape-board .mobile-current-disaster-copy { display: none; }')
    && !board.includes('class="mobile-rail-disaster-value"'), '移动对局必须按当前天灾卡图、当前天灾值、本局天灾圆图的顺序独立显示；当前天灾卡图可点选但不得附加名称文字或占用右侧操作栏'],
  [(board.match(/<PlayerMat /g) ?? []).length === 2 && (board.match(/<HandArea /g) ?? []).length === 4
    && (board.match(/<GameActions /g) ?? []).length === 2 && board.includes('<BattleEventLog ')
    && board.includes('<PromptOverlay ') && board.includes('<GraveyardOverlay ') && board.includes('<MasterOverlay ')
    && board.includes('<ActionPresentationLayer ') && board.includes('<ZoneMovementPresentationLayer ')
    && board.includes('<CombatMotionPresentationLayer '), '16:9布局重排不得删除双方场面、手牌、操作、日志、Prompt、墓地、主宰及动作展示组件'],
  [board.includes('.battlefield-half::before') && board.includes("inset:0;box-sizing:border-box")
    && board.includes('.battlefield-half.my-half::before{inset:0;') && !board.includes('.battlefield-half.my-half::before{bottom:-10px;')
    && board.includes('box-sizing:border-box;width:100%') && board.includes('justify-self:center')
    && playerMat.includes('width:min(100%,1320px);margin-inline:auto')
    && playerMat.includes('.l12-player-mat{grid-template-columns:minmax(270px,300px) minmax(500px,1fr) 100px 156px}')
    && playerMat.includes('.battle-zone{transform:translateX(-74px)}')
    && board.includes('.battlefield-half.my-half{grid-row:3}'), '双方战场外框及指挥官圣物、六格、牌堆、状态列整组必须在中央可用区居中，不得只居中六格或挤压常驻UI'],
  [playerMat.includes('grid-template-columns:140px 100px') && playerMat.includes('.master-column .mini-master{width:140px;height:196px}')
    && playerMat.includes('.master-column .mini-master>span{left:8px;right:8px;bottom:40px;overflow:visible;white-space:nowrap;text-overflow:clip;line-height:1.35}')
    && playerMat.includes('.master-column .mini-master .master-health{right:6px;bottom:5px;min-width:58px!important;height:32px!important')
    && board.includes('.felt-board :deep(.formation){width:100%;height:350px;grid-template-columns:repeat(3,173px);grid-template-rows:repeat(2,173px);justify-content:end')
    && board.includes('.felt-board :deep(.formation-slot .card-tile),.felt-board :deep(.formation-slot .card-tile.tapped){width:114.4px;height:160.6px'), '主宰容器必须在可读基准字号下单行容纳至少5个汉字；六格战场与军团卡同步放大10%，格子保持正方形并锁定右侧向左延伸，卡牌必须完整留在格内且不小于圣物卡，主宰血量不得回退'],
  [playerMat.includes('data-ui-contract="mirrored-extra-zone"')
    && playerMat.includes('.side-opponent .special-lane{top:auto;bottom:calc(100% + 11px);align-content:end}')
    && playerMat.includes('.side-my .special-lane{left:6px;top:calc(100% + 11px);bottom:auto;width:152px;align-content:start}')
    && playerMat.includes('.trial-card,.side-my .trial-card{width:135.52px}')
    && playerMat.includes('.side-opponent .trial-zone{flex-direction:column-reverse}')
    && playerMat.includes('.trial-zone{position:relative;z-index:6;display:flex;'), '试炼及后续额外区必须移出六格战场，第一张对齐主宰，对方新卡向上、我方新卡向下镜像延伸，且不侵入手牌'],
  [playerMat.includes('.mat-piles .pile,.mat-piles .pile.deck{box-sizing:border-box;width:100px;height:140px;min-height:140px}')
    && playerMat.includes('.relic-zone{box-sizing:border-box;width:132.25px;height:185.15px;aspect-ratio:5/7;display:grid;place-items:center;overflow:hidden;container-type:size}')
    && playerMat.includes('.relic-zone :deep(.card-tile){width:127.65px;height:178.71px;flex-basis:127.65px;aspect-ratio:5/7}')
    && playerMat.includes('.relic-zone :deep(.card-tile.tapped){width:calc(100cqw * 5 / 7);height:100cqw;max-width:100cqh;max-height:100cqw;flex-basis:calc(100cqw * 5 / 7)}')
    && playerMat.includes('.mat-piles{transform:translateX(-24px)}')
    && playerMat.includes('.mat-piles .pile span{left:5px;bottom:5px;padding:3px 6px')
    && playerMat.includes('.mat-piles .pile .pile-count{right:5px;top:5px;min-width:34px!important;height:28px!important;padding:0 8px!important'), '圣物区及内部卡面必须保持5:7卡牌比例；牌库和墓地名称与留有内边距的计数盒必须完整收在各自容器内'],
  [handArea.includes('.hand-card-wrap .card-tile{width:114.4px;height:160.6px;flex-basis:114.4px;border:1px solid transparent;border-radius:0;box-shadow:none}')
    && handArea.includes('.hand-card-wrap.playable::after') && handArea.includes('.hand-card-wrap.selected .card-tile')
    && playerMat.includes("'battlefield-legion-card': isBattlefieldLegionCard(player.field[row][slot]!)")
    && playerMat.includes('.formation-slot :deep(.battlefield-legion-card){border:1px solid transparent;border-radius:0}')
    && playerMat.includes('.formation-slot:not(.counter-ready) :deep(.battlefield-legion-card)')
    && playerMat.includes('.formation-slot.source:not(.combat-target):not(.payment-resource):not(.payment-selected):not(.prompt-selected)')
    && playerMat.includes('.formation-slot:focus-visible')
    && playerMat.includes('data-ui-contract="actual-combat-target-only"'), '手牌与场上军团只移除卡片常驻装饰框，透明边线保持命中尺寸；六格边界及可用、选择、目标、支付、反击状态和键盘焦点标记必须保留'],
  [l12Types.includes("'lock' | 'power-up' | 'power-down' | 'disabled' | 'shield' | 'discard-end' | 'extra-attack'") && l12Types.includes('statusIcons?: string[]') && l12Types.includes('statusEffects?: CardStatusEffect[]'), '卡牌投影视图必须提供结构化 statusEffects/statusIcons 状态契约并兼容旧快照缺省'],
  [cardTile.includes('props.card.statusEffects ?? []') && cardTile.includes('props.card.statusIcons ?? []') && cardTile.includes('statusLabel(effect, kind)') && cardTile.includes(':title="status.label"') && cardTile.includes(':aria-label="status.label"')
    && cardTile.includes('data-ui-contract="status-icons-wrap-down"') && board.includes('--l12-card-status-size:clamp(8px,13.9cqw,19.5px)')
    && board.includes('--l12-card-keyword-font:clamp(6px,7.2cqw,11.25px)')
    && cardTile.includes('--l12-card-status-size:19.5px') && cardTile.includes('--l12-card-keyword-font:clamp(10px,var(--l12-board-micro,9px),11.25px)')
    && cardTile.includes('width:var(--l12-card-status-size);min-width:var(--l12-card-status-size);height:var(--l12-card-status-size);flex:0 0 var(--l12-card-status-size)')
    && cardTile.includes('font-size:var(--l12-card-keyword-font)') && !cardTile.includes('modifier.costDelta'), '状态图标、关键词和附着卡入口必须按卡牌容器连续缩放；首个状态对齐费用边且不侵入费用，新增图标宽度不足时向下换行'],
  [l12GameEngine.includes('new("keyword-disabled", "挑衅"') && l12GameEngine.includes('IsTauntSuppressed(controller)')
    && cardTile.includes("effect.kind.trim().toLowerCase() === 'keyword-disabled'")
    && cardTile.includes("keyword.disabled ? 'disabled-keyword-red-x' : undefined") && cardTile.includes(":class=\"{ 'disabled-keyword': keyword.disabled }\"")
    && cardTile.includes('color:#ff4b58'), '关键词被无效时必须从当前生效列表移出，但保留关键词名称、来源与醒目的红色X；不得伪装为关键词被去除'],
  [board.split("&& !(mode === 'attack' && selectedId)").length - 1 === 2 && playerMat.includes("targetable: Boolean(player.field[row][slot])") && playerMat.includes("source: isSelected(player.field[row][slot]?.instanceId)"), '进攻选目标时不得高亮整块玩家区域，只能标记进攻者与合法或已选目标'],
  [playerMat.includes('data-ui-contract="actual-combat-target-only"')
    && playerMat.includes('.formation-slot.combat-attacker{box-shadow:none!important}')
    && playerMat.includes('.formation-slot.combat-target,.mini-master.combat-target')
    && playerMat.includes('@keyframes l12-combat-target-cue'), '进攻结算中的发光动画只能落在真实被攻击对象；进攻来源、支援候选及其他可交互对象不得复用目标发光'],
  [playerMat.includes('temporaryMoraleCount') && playerMat.includes('data-ui-contract="temporary-morale-selectable-site-logo"')
    && (playerMat.match(/data-ui-contract="temporary-morale-selectable-site-logo"/g) ?? []).length === 1
    && playerMat.includes('props.player.spendableResourceCount ??')
    && playerMat.includes('temporaryMoraleChoiceId(index)')
    && playerMat.includes('temporaryMoralePayable(index)')
    && playerMat.includes(':src="siteBrandIconUrl" alt="临时士气"')
    && playerMat.includes("card.resourceType === 'black-lotus'")
    && !playerMat.includes("card.cardId === 'S02-0010'")
    && board.includes("resource.resourceType === 'black-lotus'")
    && playerMat.includes('class="black-lotus-logo" :src="blackLotusLogoUrl" alt="黑色莲花"')
    && board.includes("blackLotus ? '黑色莲花' : '士气'")
    && board.includes("blackLotus ? 'black-lotus' as const")
    && specialAssets.includes('/logo/black-lotus.png') && existsSync(blackLotusPath)
    && specialAssets.includes("siteBrandIconUrl = '/favicon.png'")
    && l12GameEngine.includes('current.TemporaryMorale = 0;'), '黑色莲花士气必须使用Lotus图标；临时士气必须逐个使用网站Logo、进入可选支付交互，并由权威结束阶段在休整时清空'],
  [playerMat.includes('.resource-zone,.resource-faction-action,.resource-morale-summary,.resource-morale-stack{width:156px;max-width:156px}')
    && playerMat.includes('.resource-morale-stack{min-height:54px;justify-content:center;gap:8px 10px;padding:10px}')
    && playerMat.includes('.resource-morale-stack .morale-orb{width:32px;height:32px;min-width:32px')
    && !playerMat.includes('visibleMoraleLimit')
    && playerMat.includes('return resources.map(({ resource }) => resource)')
    && board.includes('.mobile-landscape-board .battlefield-half :deep(.resource-morale-stack){display:grid;grid-template-columns:repeat(auto-fit,minmax(18px,1fr))')
    && playerMat.includes('v-for="index in visibleTemporaryMoraleCount"')
    && playerMat.includes('.resource-zone{grid-column:4;grid-row:1/-1;display:flex')
    && playerMat.includes('align-self:center')
    && playerMat.includes('.side-opponent .resource-morale-stack{order:1;flex-wrap:wrap-reverse;align-content:flex-end}')
    && !playerMat.includes('Array<null>'), '桌面士气区保持原有实时展示；自动横屏时根据可用宽高自适应网格、显示全部实际资源，且不预占未追加的图标'],
  [playerMat.includes('v-if="mobileMoralePicker" class="mobile-hand-count"')
    && playerMat.includes(":is=\"mobileMoralePicker ? 'button' : 'div'\"")
    && playerMat.includes('class="temporary-site-logo" :src="siteBrandIconUrl" alt="临时士气"')
    && gamePage.includes('class="balanced-copy-button" aria-label="返回大厅"')
    && gamePage.includes('<span class="route-label" aria-hidden="true"><span>返回</span><span>大厅</span></span>')
    && gamePage.includes('.battle-route-controls button{display:grid;min-width:0;min-height:32px;place-items:center')
    && gamePage.includes('.battle-route-controls .route-label>span{display:block;min-width:2em')
    && gamePage.includes('.battle-route-controls{top:106px;right:6px')
    && mobileViewportStyle.includes('transform: translateZ(0)')
    && mobileViewportStyle.includes('#l12-landscape-teleports > .mobile-safe-overlay:not(.minimized)')
    && gamePage.includes('height:calc(var(--l12-viewport-height,100vh) - 16px)')
    && board.includes('text-align:center; text-wrap:balance; overflow-wrap:anywhere;')
    && prompt.includes('--inspector-safe-lane:clamp(118px,19vw,258px)'), '移动专用手牌计数、阵营标识和路由换行不得泄漏到桌面；临时士气始终使用网站图标，桌面弹框安全区必须使用经验证的窄侧栏比例'],
  [board.includes('--l12-mobile-master-health-h:clamp(13px,calc(var(--l12-mobile-card-w) * .34),22px)')
    && board.includes('--l12-mobile-pile-badge-h:clamp(12px,calc(var(--l12-mobile-card-w) * .30),20px)')
    && board.includes('--l12-mobile-resource-summary-h:clamp(22px,calc(var(--l12-mobile-card-w) * .52),30px)')
    && board.includes('--l12-mobile-action-h:clamp(30px,calc(var(--l12-viewport-height,100vh) * .09),38px)')
    && board.includes('font-size:var(--l12-mobile-pile-count-font) !important')
    && board.includes('font-size:var(--l12-mobile-resource-font) !important')
    && board.includes('min-height:var(--l12-mobile-action-h) !important'), '移动主宰血量、牌堆数字、士气摘要与场面操作按钮必须从卡牌或逻辑视口比例令牌取值，不得恢复为各自固定尺寸'],
  [l12Types.includes('cannotUntapUntilRound?: number')
    && playerMat.includes('function moraleLocked(card: MoraleResource)')
    && playerMat.includes('lockedUntilRound > 0 && lockedUntilRound >= (props.round ?? 0)')
    && playerMat.includes('data-ui-contract="active-morale-lock"')
    && playerMat.includes('v-if="moraleLocked(morale)"')
    && playerMat.includes('本轮重置阶段无法转为活跃')
    && playerMat.includes('pointer-events:none')
    && playerMat.includes('.morale-orb.selected .morale-lock-icon')
    && playerMat.includes('.morale-orb.payable .morale-lock-icon'), '士气锁图标必须使用现有轮次投影只显示当前仍有效的 CannotUntapUntilRound，不拦截点击且不得覆盖支付高亮'],
  [prompt.includes("const declineChoices = new Set(['no', 'mode:none', 'skip', 'pass', 'decline', 'cancel'])")
    && prompt.includes("explicitLabel === '不响应' || explicitLabel === '不发动'")
    && prompt.includes(':data-ui-contract="isDeclineChoice(choice) ? \'minimum-decline-action\' : undefined"')
    && prompt.includes('min-width:112px!important;min-height:44px!important'), '所有“不响应”选项必须走统一拒绝动作识别，并保持至少112×44像素的可操作尺寸'],
  [prompt.includes("prompt.value?.kind === 'trigger-order'")
    && prompt.includes('return `结算 ${resolutionOrder}`')
    && prompt.includes('data-ui-contract="trigger-order-lifo-hint"')
    && prompt.includes('后选择的效果先结算'), '同一时点触发排序必须在选项角标显示真实逆序结算顺序，避免把点击序号误读为结算序号'],
  [gamePage.includes('data-ui-contract="manual-game-over-exit"')
    && gamePage.includes('<button @click="returnToLobby">返回大厅</button>')
    && !gamePage.includes('点击返回后才离开本局')
    && !gamePage.includes('双方都离开后关闭房间'), '胜负结算必须保持在结果页并保留明确返回按钮，不对玩家暴露房间保留机制'],
  [playerMat.includes('data-ui-contract="resource-faction-action"') && playerMat.includes('data-ui-contract="resource-morale-summary"') && playerMat.includes('data-ui-contract="resource-morale-label"')
    && playerMat.includes('data-ui-contract="resource-morale-count"') && playerMat.includes('data-ui-contract="resource-morale-stack"')
    && playerMat.indexOf('data-ui-contract="resource-faction-action"') > playerMat.indexOf('<div class="mat-piles">')
    && !playerMat.includes('player-resource-zone') && !playerMat.includes('resource-zone-heading') && !playerMat.includes('class="morale-rail"')
    && playerMat.includes('data-ui-contract="centered-resource-zone"')
    && playerMat.includes('.resource-faction-action{order:1;justify-self:start}') && playerMat.includes('.side-opponent .resource-faction-action{order:3}')
    && playerMat.includes('.resource-morale-summary{order:2;display:grid') && playerMat.includes('.side-opponent .resource-morale-summary{order:2}')
    && playerMat.includes('white-space:nowrap')
    && playerMat.includes('@click.stop="factionOpen = true; factionMinimized = false"')
    && playerMat.includes('@click.stop="inspectOrSelectMorale(morale.instanceId)"')
    && playerMat.includes('class="mobile-morale-stack-trigger"')
    && board.includes('mobileMoralePickerEnabled') && board.includes('mobileMoraleInteractive')
    && board.includes('mobile-morale-picker') && board.includes('@click="chooseMobileMorale(choice)"')
    && board.includes(':aria-disabled="!choice.selectable"') && board.includes("mobileMoraleReason.value=choice.disabledReason||'当前不能选择'")
    && board.includes('mobileRuneChoices') && board.includes("state: 'rune'"), '阵营效果、同行士气标题/计数和士气堆在桌面共用156px边界并保留文字内距；手机横屏整个士气区可打开图标化大面板查看，彼界符文置于首排；不可用资源仍可点出弹框级原因，支付类提示复用原确认、取消与支付交互'],
  [playerMat.includes("if (props.promptSlotIds?.includes(`${row}:${slot}`))")
    && playerMat.indexOf("if (props.promptSlotIds?.includes(`${row}:${slot}`))") < playerMat.indexOf("if (card && props.paymentChoiceIds?.includes(card.instanceId))")
    && playerMat.includes("available: promptSlotIds?.includes(`${row}:${slot}`) || isPlacementDestination")
    && board.includes('function resolveBoardSlotPrompt(playerIndex: number, row: number, slot: number)')
    && board.includes("if (prompt.validChoices.includes(choice)) command('resolvePrompt', { promptId: prompt.promptId, choice })")
    && board.includes('if (resolveBoardSlotPrompt(me.value.playerIndex, row, slot)) return')
    && board.includes('if (resolveBoardSlotPrompt(enemy.value.playerIndex, row, slot)) return'), '场面槽位Prompt必须以目标玩家和权威validChoices为准，已被声明费用军团占据的候选格仍可高亮点击并优先于资源支付路由；普通放置移动不得放宽'],
  [!board.includes('房间 {{ game.roomCode }}') && !board.includes('MATCH {{ game.matchId.slice')
    && board.includes('.right-rail .action-panel :deep(.l12-actions>p){display:none}')
    && (board.match(/<GameActions /g) ?? []).length === 2
    && board.includes('class="grand-panel action-panel"') && gameActions.includes("@click=\"emit('command','endTurn')\"")
    && gameActions.includes('结束回合'), '右栏不得常驻显示房间REV/MATCH元数据或操作说明段落，但包括结束回合在内的所有真实操作按钮必须保留'],
  [board.includes('class="left-disaster-row"')
    && board.indexOf('data-ui-contract="left-current-disaster"') < board.indexOf('class="left-detail-layout"')
    && board.includes('.left-disaster-row{display:grid;width:100%;grid-template-columns:132px minmax(0,1fr);gap:8px')
    && board.includes('.session-disaster-strip{display:grid;width:max-content;grid-template-columns:repeat(2,51.2px)')
    && board.includes('.session-disaster-strip button{width:51.2px;min-width:51.2px;height:51.2px'), '本局天灾与当前天灾必须保持两个独立面板并整体与选中卡牌共用左栏宽度；四枚圆形缩略图须缩小 20% 后按 2×2 排列'],
  [board.includes('<Teleport :to="landscapeTeleportTarget()" :disabled="!modalInspectorVisible">'), '弹框期间必须复用原选中卡牌详情框'],
  [!board.includes('class="modal-card-inspector"'), '不得重新引入第二套弹框卡牌详情'],
  [!board.includes('<CardTile'), '卡牌详情不得渲染战场角标 UI'],
  [!prompt.includes('class="prompt-card-inspector"') && !prompt.includes('class="prompt-card-detail"'), 'PromptOverlay 不得自建卡牌详情框'],
  [prompt.includes('<section v-if="minimized"') && prompt.includes('<section v-else-if="prompt"') && prompt.includes('minimizedChange'), 'Prompt 最小化后必须只保留展开条，并同步隐藏浮动卡牌详情'],
  [prompt.includes('.l12-prompt-overlay,.l12-prompt-overlay.minimized{z-index:3000!important}') && prompt.includes('@media(max-width:760px){.l12-prompt-overlay.minimized{right:10px;bottom:60px}') && masterOverlay.includes('.master-overlay.minimized{z-index:2000;inset:auto 16px 66px auto') && playerMat.includes('.faction-effect-overlay.minimized{z-index:2000;inset:auto 16px 66px auto') && globalBugFeedback.includes('.bug-feedback-trigger{position:fixed;z-index:1900;right:16px;bottom:16px'), '所有最小化展开入口必须在桌面与小屏幕避开全局 Bug 反馈按钮，可操作 Prompt 入口还必须高于被动展示层'],
  [prompt.includes('<section v-if="minimized" class="prompt-minimized-bar" role="status">\n        <button :aria-label="`展开：${overlayTitle}`"') && masterOverlay.includes('<section v-if="minimized" class="master-minimized">\n        <button :aria-label="`展开：${player.master.masterName} · 主宰效果`"') && playerMat.includes('<section v-if="factionMinimized" class="faction-minimized-bar">\n        <button :aria-label="`展开：${player.factionEffect?.name || \'阵营效果\'}`"') && playerMat.includes('<section v-if="abilityCardMinimized" class="faction-minimized-bar">\n        <button :aria-label="`展开：${abilityCardOpen.name}`"'), '弹框最小化后必须仅保留带上下文无障碍名称的展开按钮'],
  [gamePage.includes("import GameBoard from './game/GameBoard.vue'"), '对战入口必须唯一指向 src/l12/game/GameBoard.vue'],
  [!lobby.includes('l12State.room.decks'), '友谊战整备室不得同时渲染服务端预组与我的牌库'],
  [lobby.includes('platformState.account') && !lobby.includes('玩家昵称<input'), '对战大厅必须使用登录账号身份且不得保留手填昵称'],
  [/type:\s*'hello',[\s\S]{0,160}?authToken/.test(l12Net) && !l12Net.includes("type: 'hello', name"), 'WebSocket 握手必须使用账号令牌而非任意昵称'],
  [app.includes('startAutomaticConnection') && app.includes('[platformState.token, authState.verified]') && app.includes('token && verified') && app.includes('{ immediate: true }'), '只有经过服务端验证的登录玩家才能在全站启动自动连接'],
  [mainEntry.includes('initializeAuth()') && mainEntry.includes('await Promise.race([') && mainEntry.includes('window.setTimeout(resolve, 3_000)') && mainEntry.indexOf('initializeAuth()') < mainEntry.indexOf("mount('#app')"), '应用挂载前必须有界等待权威身份初始化，认证服务不可达时也不能让公共站点无限白屏'],
  [l12Net.includes('scheduleReconnect') && l12Net.includes('connectPromise') && l12Net.includes("type: 'ping'") && l12Net.includes("location.protocol === 'https:'"), 'WebSocket 必须防止并发建连、支持断线退避重连和正式站同源选址'],
  [decks.includes("platformRequest<SavedL12Deck[]>('/api/decks')") && decks.includes("method: 'PUT'") && decks.includes("method: 'DELETE'"), '玩家牌库必须与账号服务端持久化同步'],
  [lobby.includes('copyRoomCode') && lobby.includes('复制房间码'), '友谊战整备室必须保留房间码复制按钮'],
  [lobby.includes('isRoomHost') && lobby.includes('updateRoomOptions(editableRoomOptions.value)') && lobby.includes('保存房间规则') && l12Net.includes("type: 'updateRoomOptions'") && shell.includes('l12State.friendInvitation = null\n  resolveFriendInvitation(invitationId, accept)'), '好友邀请接受后必须立即关闭邀请层并进入整备室，且仅房主可在开战前调整房间规则'],
  [lobby.includes('v-model="roomOptions.useCardRestrictions"') && lobby.includes('不启用运营禁限卡') && lobby.includes('启用运营禁限卡') && !lobby.includes('v-model="roomOptions.matchModeId"') && lobby.includes('<option value="season">赛季天灾'), '好友房必须以是否启用运营禁限卡取代排位/休闲模式，并可选择赛季天灾快照'],
  [lobby.includes('maintenance.entryBlocked') && lobby.includes('维护即将开始/维护中，对局功能已关闭。')
    && lobby.includes('maintenanceCountdown(operationsPolicy.value.maintenance, policyNow.value)')
    && lobby.includes('watch(() => l12State.operationsPolicy') && lobby.includes('getEffectiveOperationsPolicy()')
    && lobby.includes('window.setInterval(() => void refreshOperationsPolicy(), 15_000)')
    && lobby.includes('window.clearInterval(maintenanceClockTimer)') && lobby.includes('window.clearInterval(operationsRefreshTimer)')
    && lobby.includes('v-if="maintenanceView"') && platform.includes('maintenance: { enabled: boolean; active: boolean')
    && platform.includes("platformRequest<EffectiveOperationsPolicy>('/api/operations/effective-policy', { cache: 'no-store' })")
    && l12OperationsStore.includes('public sealed record L12EffectiveMaintenanceView(\n    bool Enabled,')
    && adminOperations.includes('提前广播（小时）') && adminOperations.includes('预计维护时长（小时）'),
    '维护计划必须公开启用态，消费HTTP与WebSocket快照并持续刷新倒计时，失败保留快照且权威入口门禁不变'],
  [app.includes('/audio/legion12-site.mp3') && app.includes('/audio/legion12-battle-1.mp3')
    && app.includes('/audio/legion12-battle-2.mp3') && settingsModal.includes('v-model.number="audioPreferences.musicVolume"')
    && settingsModal.includes('v-model.number="audioPreferences.sfxVolume"') && settingsModal.includes('v-model="audioPreferences.cardSize"')
    && settingsModal.includes('v-model="audioPreferences.animation"') && settingsModal.includes('v-model="audioPreferences.mobileLayout"')
    && settingsModal.includes('<option value="auto">自动适配</option><option value="on">始终启用</option><option value="off">使用宽屏布局</option>')
    && platform.includes('/api/auth/audio-preferences')
    && app.includes('watch(audioPreferences, value => {\n  syncAudioStore()')
    && app.includes('generation !== audioSaveGeneration')
    && audioPreferencesModule.includes("localStorage.setItem('l12-audio-preferences-v1'")
    && audioPreferencesModule.includes('Number.isFinite(value)')
    && audioPreferencesModule.includes('store.setBgmVolume') && audioPreferencesModule.includes('store.setSfxVolume')
    && audioPreferencesModule.includes('export function l12MusicOutputVolume')
    && audioPreferencesModule.includes('return normalized * normalized')
    && app.includes('volume: l12MusicOutputVolume()')
    && settingsModal.includes('type="range" min="0" max="1" step="0.01"')
    && settingsModal.includes('class="volume-control"')
    && settingsModal.includes('.audio-setting{display:grid;min-width:0;grid-template-columns:minmax(0,1fr) 92px')
    && settingsModal.includes('.volume-control{display:grid;min-width:0;grid-template-rows:auto auto')
    && settingsModal.indexOf('<label class="volume-control"') < settingsModal.indexOf('<button type="button" class="toggle"')
    && audioPreferencesModule.includes('dataset.l12CardSize') && audioPreferencesModule.includes('dataset.l12Animation')
    && audioPreferencesModule.includes('dataset.l12MobileLayoutPreference')
    && shell.includes('<L12SettingsModal') && gamePage.includes('<L12SettingsModal'), '音乐、音效、卡牌尺寸与动画必须由官网/对局共用设置框，音乐低音量区使用细分感知曲线，并在每次操作时立即同步实际消费者、DOM显示和本地/账号持久化'],
  [backgroundMusic.includes('const FADE_DURATION_MS = 520') && backgroundMusic.includes('private generation = 0')
    && backgroundMusic.includes('private fades = new Map<HTMLAudioElement, number>()')
    && backgroundMusic.includes('this.fade(audio, volume, generation)') && backgroundMusic.includes('this.fade(previous.audio, 0, generation, true)')
    && backgroundMusic.includes('track.generation !== this.generation') && backgroundMusic.includes('destroy()')
    && app.includes('new BackgroundMusicController()') && !app.includes('new Audio('), '场景切换、对局曲目轮换及快速开关音乐必须由单一代次控制器淡出/淡入，过期ended回调不得叠播或改写当前曲目'],
  [audioPreferencesModule.includes('export function l12AnimationDuration')
    && combatMotionLayer.includes('if (!props.playbackSpeed) return l12AnimationDuration(standardMs, liveMinimumMs)')
    && combatMotionLayer.includes('presentationDuration(360, 24, 80)') && combatMotionLayer.includes('presentationDuration(920, 260, 160)')
    && zoneMovementLayer.includes('function movementDuration(movement: Movement)')
    && zoneMovementLayer.includes('if (!props.playbackSpeed) return l12AnimationDuration(standardMs, liveMinimumMs)')
    && zoneMovementLayer.includes("'--move-duration': `${movementDuration(active.value)}ms`")
    && zoneMovementLayer.includes('animation:l12-zone-card-flight var(--move-duration,.44s)')
    && zoneMovementLayer.includes('class="moving-card" data-essential-motion')
    && !zoneMovementLayer.includes('@media(prefers-reduced-motion:reduce){.moving-card{animation-duration:')
    && zoneMovementLayer.includes('timer = setTimeout(finish, duration + replayDuration')
    && board.includes('l12AnimationDuration(3000, 700)') && board.includes('l12AnimationDuration(900, 180)'), '动画设置必须被战斗WAAPI、跨区CSS/WAAPI及公开结算计时共同消费；视觉时长和移动队列锁必须同源，关闭时仍保留必要公开信息最短可读时间'],
  [app.includes(':root[data-l12-card-size="small"] .archive-grid')
    && app.includes(':root[data-l12-card-size="large"] .deck-card-grid')
    && app.includes(':root[data-l12-card-size="small"] .construction-grid')
    && app.includes(':root[data-l12-card-size="medium"] .construction-grid')
    && app.includes(':root[data-l12-card-size="large"] .mine-grid')
    && app.includes(':root[data-l12-card-size="small"] .hand-card-wrap')
    && app.includes(':root[data-l12-card-size="medium"] .hand-card-wrap')
    && app.includes(':root[data-l12-card-size="large"] .formation-slot .card-tile')
    && !app.includes('--l12-user-card-scale'), '卡牌尺寸偏好必须真实覆盖图鉴、牌库编辑器、公共牌库、构筑浏览器、手牌与场面，不得只写入未消费变量'],
  [battleDock.includes('position:relative') && battleDock.includes('width:100%;height:60px')
    && battleDock.includes('title="设置" aria-label="打开对局设置"')
    && battleDock.includes('@click="$emit(\'settings\')"')
    && board.includes('data-ui-contract="selected-card-utility-dock"')
    && board.includes('<BattleUtilityDock @settings="emit(\'settings\')"')
    && board.includes('.selected-card-utility-slot{box-sizing:border-box;width:100%;height:60px;flex:none}')
    && gamePage.includes('@settings="settingsOpen = true"')
    && gamePage.includes('<L12SettingsModal @close="settingsOpen = false"')
    && gamePage.includes('class="battle-settings-mask"'), '选中卡牌容器下方必须保留横向三按钮工具坞并为其避让，设置入口复用完整设置页且立即作用于当前对局'],
  [adminOperations.includes("id: 'announcements'") && adminOperations.includes('data-ui-contract="independent-long-term-announcements"')
    && adminOperations.includes('不设置结束时间') && adminOperations.includes('data-ui-contract="idempotent-server-start"')
    && lobby.includes('data-ui-contract="long-term-announcements-above-deck"')
    && lobby.indexOf('data-ui-contract="long-term-announcements-above-deck"') < lobby.indexOf('class="room-current-deck"')
    && platform.includes('/api/admin/operations/server/start'), '长期公告必须独立编辑、排序和定时，并显示在大厅更换牌库盒子上方；维护结束可空且显式启服使用独立幂等端点'],
  [officialHome.includes('loadPublishedHomeCache()') && officialHome.includes('savePublishedHomeCache(payload)')
    && officialHome.includes('generation !== refreshGeneration') && officialHome.includes('scheduleHomeRefresh()')
    && officialHome.includes('ready.value = hasPublishedSnapshot')
    && homePublishedCache.includes("const CACHE_KEY = 'l12-home-published-v2'")
    && homePublishedCache.includes("'l12-home-published-v1', 'l12-site-home-v1'")
    && !officialHome.includes('catch {\n    composition.value = defaultHomeComposition()'), '主页内容API短暂失败时必须冻结最后成功发布快照并退避重试，兼容旧缓存，且无快照时保持加载态而非覆盖成占位内容'],
  [l12Net.includes("export type SandboxDisasterMode = 'all' | 'random' | 'custom' | 'none'") && sandbox.includes('<option value="custom"') && !sandbox.includes('<option value="season"'), '沙盒只能使用全部、随机、自定或无天灾，不得接入赛季天灾池'],
  [lobby.includes('joinMatchmaking') && lobby.includes('七曜值') && lobby.includes('选择本赛季派系') && l12Net.includes("type: 'joinMatchmaking'") && l12Net.includes("type: 'pollMatchmaking'") && l12Net.includes("message.type === 'matchmakingRejected'") && l12Net.includes('startMatchmakingPolling()'), '公开匹配必须使用服务端权威队列、保留等待扩圈轮询并清理拒绝状态，在排位前选择赛季派系'],
  [lobby.includes('data-ui-contract="faction-totals-above-public-match"') && lobby.indexOf('data-ui-contract="faction-totals-above-public-match"') < lobby.indexOf('<section v-if="tab === \'match\'" class="mode-panel panel">'), '三派系七曜总量必须位于顶部模式标签之后、公开匹配面板之前，不能埋在公开匹配内容框内'],
  [rankings.includes("type RankingTab = 'players' | 'masters' | 'matchups' | 'history'") && rankings.includes('主宰对阵一览') && rankings.includes('历史荣誉') && rankings.includes('row.titles') && rankings.includes('title-badge') && rankings.includes('champion-title') && rankings.includes("type MasterSort = 'games' | 'winRate' | 'firstWinRate' | 'secondWinRate' | 'usageRate'") && rankings.includes('v-model="masterSort"') && rankings.includes('right[masterSort.value] - left[masterSort.value]') && adminOperations.includes('最高段位第一名称号') && adminOperations.includes('主宰最强玩家称号') && adminOperations.includes('rankedConfig.masterTitles'), '派系前五称号必须标明最高段位门槛，排行榜必须支持玩家榜、主宰榜、多维主宰排序、对阵一览、历史荣誉及醒目的多称号展示，后台必须支持逐主宰最强玩家称号'],
  [rankings.includes('<h1>排行榜</h1>') && !rankings.includes('<h1>排位排行榜</h1>')
    && rankings.includes("import { masterProfileUrl } from '@/l12/specialAssets'")
    && (rankings.match(/data-ui-contract="ranking-master-avatar"/g) ?? []).length >= 3
    && rankings.includes("const matrixMasterColumnWidth = '银臂努阿达'.length * 14 + 44")
    && rankings.includes('gridTemplateColumns: `64px repeat(${matrixMasters.length + 1}, ${matrixMasterColumnWidth}px)`')
    && rankings.includes('class="matrix-rank-head">排名</div>') && rankings.includes('class="matrix-rank-cell"')
    && rankings.includes('<template v-else><b>等待</b><span>更多对局</span></template>')
    && rankings.includes("if (value === undefined) return 'no-data'")
    && rankings.includes('.matrix-corner{position:sticky;z-index:6;top:0;left:64px')
    && rankings.includes('.matrix-row-head{position:sticky;z-index:3;left:64px')
    && rankings.includes('grid-auto-rows:76px')
    && rankings.includes('height:76px;min-height:76px;max-height:76px}')
    && rankings.includes('--ranking-master-avatar:44px'), '排行榜名称必须收口，矩阵首列排名、次列我方主宰、全部主宰列按银臂努阿达等宽，空样本两行且全行固定76px，并为44px主宰头像保留内距'],
  [disasterPoolPicker.includes('data-ui-contract="landscape-disaster-pool-picker"')
    && disasterPoolPicker.includes('class="pool-card-art"')
    && disasterPoolPicker.includes('intent="detail" fit="contain"')
    && disasterPoolPicker.includes('.pool-card-art{display:block;width:100%;aspect-ratio:8/5;overflow:hidden')
    && disasterPoolPicker.includes('class="pool-card-copy"')
    && disasterPoolPicker.includes('.pool-card-copy b,.pool-card-copy small{position:static')
    && !disasterPoolPicker.includes('intent="thumb"')
    && adminOperations.includes('<DisasterPoolPicker v-model="form.disasterPool.cardIds"')
    && tournamentCenter.includes('<DisasterPoolPicker v-model="form.disasterCardIds"'), '赛季与赛事天灾池必须复用公共选择器，以不旋转、不裁切的8:5横版完整卡图展示，并将卡名和卡号置于卡图外的独立信息区'],
  [lobby.includes('class="ranked-rules-button"') && lobby.includes('>排位规则</button>') && lobby.includes('rankedRulesOpen') && lobby.includes('七曜值') && lobby.includes('段位与派系称号') && lobby.includes('最强主宰规则') && lobby.includes('排位用时') && lobby.includes('本赛季天灾') && lobby.includes('本赛季禁限卡'), '大厅排位区必须提供排位规则按钮，并集中说明七曜、派系段位称号、最强主宰、动态时限和赛季天灾/禁限卡'],
  [lobby.includes('.ranked-rules-modal{grid-template-rows:auto minmax(0,1fr) auto') && lobby.includes('.ranked-rules-scroll{min-height:0;align-content:start;overflow-x:hidden;overflow-y:scroll'), '排位规则正文必须拥有独立纵向滚动区，在小视口中也能阅读全部内容'],
  [shell.includes('<router-link class="site-brand" to="/"') && !shell.includes('<span>LEGION 12</span>') && !shell.includes('<small>十二军团</small>') && shell.includes('.site-brand img{width:44px;height:44px;border:0;border-radius:0;object-fit:contain'), '主页侧栏品牌区必须只显示无外框的白色 Logo-Mini，不得附带中英文文字'],
  [globalBugFeedback.includes('v-model="form.bugDescription"') && globalBugFeedback.includes('v-model="form.suggestion"') && globalBugFeedback.includes('if (!bugDescription && !suggestion)') && globalBugFeedback.includes('提及卡牌的时候请勿使用俗称，最好使用卡牌编号（例：S01-0001）') && globalBugFeedback.includes('描述你希望优化的Bug、操作体验或界面效果'), '全局反馈必须分为 Bug 提交与优化建议，任填一项即可提交并保留明确填写提示'],
  [profilePage.includes('class="title-manager"') && profilePage.includes('ranked.profile.masterTitles') && profilePage.includes('saveRankedTitle') && platform.includes("'/api/ranked/title'") && board.includes('playerBadges') && board.includes('enemyBadge?.rank') && board.includes('battleTierLabel(enemyBadge)') && board.includes('enemyBadge?.placementTitle') && board.includes('myBadge?.masterTitle'), '个人页必须可选择已获得的最强主宰称号，对战右上玩家框须依次显示权威全服名次、段位、派系段位称号与所选主宰称号'],
  [board.includes("choiceMode === 'mixed-board-payment'") && board.includes("? '确认费用' : '确认发动'") && board.includes('lockedChoices'), '混合场面费用必须在同一场面直选条选择，唯一资源自动锁定且与弃置对象一并确认'],
  [rankedTicker.includes('@animationend="complete"') && rankedTicker.includes('animation:ranked-message-once 16s linear 1 both') && rankedPlayback.includes('claimNextRankedBroadcast') && rankedPlayback.includes('completeCurrentRankedBroadcast') && rankedPlayback.includes('accountId'), '排位广播必须按账号领取，完整播放一次后确认，不得在页面内循环重播同一消息'],
  [adminOperations.includes('data-ui-contract="ranked-broadcast-config"') && adminOperations.includes('rankedConfig.broadcast.displaySeconds') && adminOperations.includes('rankedConfig.broadcast.minimumTierIndex'), '排位广播的时长、大厅延迟、间隔、门槛和类别开关必须由后台统一配置'],
  [board.includes('selected-card-inspector-anchor') && board.includes(':style="modalInspectorVisible ? inspectorFloatStyle : undefined"'), '弹框期间详情必须由原选中卡牌框锚点定位'],
  [!board.includes('.modal-card-inspector') && !prompt.includes('.prompt-card-inspector'), '不得保留第二套弹框详情框样式'],
  [deckEditor.includes("deck.name === activeDeckName") && deckEditor.includes('.saved-list b{color:#f1eee5}') && deckEditor.includes('.saved-list span{color:#aab4b0}') && deckEditor.includes('.saved-list article.active{border-color:#86e8ee;background:#123e42'), '牌库编辑器左下牌库列表及当前牌库状态必须保持高对比'],
  [board.includes('card.playCost ?? card.currentCost ?? card.cost'), '手牌可打出校验必须使用服务端动态费用'],
  [battleLog.includes('class="event-message"') && battleLog.includes('overflow-wrap:anywhere'), '对局记录必须使用可换行的独立消息容器'],
  [board.includes('<Teleport :to="landscapeTeleportTarget()">') && board.includes('public-card-reveal-animation') && board.includes('.public-reveal-animation{z-index:903}') && board.includes("event.type === 'effect-trigger'") && board.includes("event.type === 'effect-response'") && board.includes("event.type === 'effect-activation'") && board.includes("event.type === 'reveal'") && board.includes("event.playerIndex !== props.game.you") && board.includes("event.type === 'effect-trigger' && /展示|公开/.test(event.text)") && board.includes("event.type === 'search' && /展示|加入手牌/") && board.includes('text: publicRevealText(event)') && board.includes('const override = event.effectText?.trim()') && board.indexOf('if (override) return override') < board.indexOf('/花魁的馈赠/.test(text)') && board.includes('花魁的馈赠将〈${card.name}〉加入手牌') && board.includes('l12AnimationDuration(3000, 700)') && !board.includes('reveal-confirm') && !board.includes('public-reveal-mask'), '公开展示、检索加入手牌、触发、响应与发动效果必须只向非发动方播放无蒙版非阻塞动画；标准三秒且关闭动画时仍保留可读下限，只呈现事件单条效果文本和涉及卡图，后台覆盖优先于花魁兼容文案'],
  [prompt.includes("const usesDetailCardImages = computed(() => isDisasterChoice.value || isInfoConfirm.value)") && prompt.includes(":intent=\"usesDetailCardImages ? 'detail' : 'thumb'\"") && prompt.split(":alt=\"entry.card.name || '天灾'\" intent=\"detail\"").length - 1 === 2 && prompt.includes("'disaster-choice': isDisasterChoice"), '公开天灾禁选、随机公开、触发确认及已公开历史必须请求详情级高清图，不得使用缩略图源'],
  [board.includes(':inspector-visible="modalInspectorVisible"') && prompt.includes("'inspector-active': inspectorVisible") && prompt.includes('--inspector-safe-lane:clamp(118px,19vw,258px)') && prompt.includes('--inspector-safe-lane:92px') && prompt.includes('@media(max-width:520px)')
    && board.includes('const logicalWidth = inspectorAnchor.value.offsetWidth') && board.includes('transform: `scale(${floatScale})`')
    && board.includes('const adaptiveBoardToken = (base: number, currentScale: number)')
    && board.includes("'--l12-board-copy': `${adaptiveBoardToken(13, floatScale)}px`") && board.includes("'--l12-board-meta': `${adaptiveBoardToken(11, floatScale)}px`") && board.includes("'--l12-effect-copy': `${adaptiveBoardToken(13, floatScale)}px`") && board.includes('inspector-style-scope') && board.includes('overflow:auto!important'), '弹框期间原选中详情必须固定侧置并保持原容器的大小和位置，继承连续缩放的语义字号层级，为核心弹框保留安全区，在窄屏与缩放下也不得互相遮挡'],
  [board.includes("event.type === 'disaster-reveal'") && board.includes("event.playerIndex === null")
    && battleLogViewModel.includes("case 'disaster':") && battleLogViewModel.includes("case 'disaster-active':")
    && battleLogViewModel.includes("case 'effect':"), '天灾必须向双方播放，已发动效果必须进入玩家白名单日志；内部响应阶段不得直接暴露'],
  [board.includes('resolvedDisasterIds') && board.includes("return 'active'") && board.includes("return 'resolved'")
    && board.includes('.session-disaster-strip button.unrevealed')
    && board.includes('.session-disaster-strip button.resolved{border-color:#76508f')
    && board.includes('.session-disaster-strip button.active{border-color:#bc6cff'), '本局天灾圆形图必须区分未发动灰色、已发动暗紫色与当前生效亮紫色，并由权威天灾实例状态驱动'],
  [board.includes('data-ui-contract="dice-event-animation"') && board.includes("event.type === 'dice'") && battleLogViewModel.includes("case 'dice':") && board.includes('@keyframes l12-dice-roll') && board.includes('.dice-reveal-animation{z-index:904}'), '普通掷骰事件必须在交互层下方播放非阻塞动画并保留可读日志'],
  [prompt.includes("prompt.value?.kind === 'option'") && prompt.includes('effect-option-list')
    && prompt.includes('orderedEffectChoices') && prompt.includes('declineChoices')
    && prompt.includes('.prompt-choices.effect-option-list{display:grid')
    && prompt.includes('grid-template-columns:repeat(auto-fit,minmax(150px,1fr))')
    && prompt.includes('overflow:visible'), '效果/费用分支必须按原顺序自适应同屏排列，三项不得依赖横向拖动，且不发动固定在最后'],
  [prompt.includes("booleanData(id, 'hasPrintedCost')") && prompt.includes('hasPrintedCost: detail.hasPrintedCost') && replayModel.includes("trait.endsWith('专属')"), '衍生卡在弹框与历史回放中不得伪造不存在的印刷费用'],
  [cardTile.includes('Math.max(0, props.card.playCost')
    && cardTile.includes('Math.max(0, props.card.troops)')
    && cardTile.includes('Math.max(0, props.card.displayBaseTroops')
    && prompt.includes("Math.max(0, card?.playCost")
    && prompt.includes("Math.max(0, card?.troops")
    && prompt.includes("Math.max(0, card?.baseTroops")
    && replayModel.includes("currentCost: Math.max(0")
    && replayModel.includes("hp: Math.max(0"), '卡牌、弹框和回放中的费用、兵力及血量不得显示负数'],
  [!matchRecords.includes("import GameBoard from './game/GameBoard.vue'")
    && !matchRecords.includes('selectMatch(matches.value[0])')
    && matchRecords.includes("router.push({ name: 'match-replay'")
    && matchRecords.includes("rememberImportedReplay(detail)")
    && matchRecords.includes("router.push({ name: 'json-replay'"), '对局记录只允许选择摘要；服务器记录由玩家确认后播放，本地JSON打开后直接进入独立回放路由，不得加入记录列表或嵌入渲染棋盘'],
  [matchRecords.includes('/api/matches?limit=10') && matchRecords.includes('<h1>对局回放</h1>')
    && matchRecords.includes('仅保存7天内最近10场回放，历史回放文件可能随版本更新失效。')
    && l12ServerSources.includes('IsWithinRecentPlayerReplayWindowAsync')
    && l12ServerSources.includes('RunPlayerReplayCleanupIfDueAsync'), '玩家回放必须限制7天内最近10场并由服务端统一可见性与每日清理保护'],
  [router.includes("name: 'json-replay'") && router.includes("name: 'match-replay'") && router.includes("name: 'admin-match-replay'")
    && replayPage.includes('<GameBoard v-else-if="currentGame" :game="currentGame" :replay-focus-card="replayFocusCard"')
    && replayPage.includes(':replay-playback-speed="playbackSpeed" read-only @replay-presentation-change="replayPresentationBusy = $event"')
    && replayPage.includes('>上一步</button>') && replayPage.includes("playing ? '暂停' : '播放'")
    && replayPage.includes("loadingReplayPage ? '加载中' : '下一步'") && replayPage.includes("isAdminReplay.value ? '返回后台对局档案' : '返回对局记录'")
    && gameReentry.includes('!route.replay') && l12Net.includes('replay: router.currentRoute.value.meta.replay === true')
    && board.includes('props.readOnly || !l12State.gmEnabled'), '回放必须使用与正式对战一致的独立全屏棋盘，左下提供上一步/播放/下一步，右上返回记录，并且不得被现存实时对局或沙盒控制状态污染'],
  [replayPage.includes('const playbackSpeed = ref<1 | 2 | 3>(1)')
    && replayPage.includes('const delay = Math.max(120, Math.round(650 / playbackSpeed.value))')
    && replayPage.includes('if (replayPresentationBusy.value)')
    && replayPage.includes('timer = window.setTimeout(() => void advancePlayback(generation), 40)')
    && !replayPage.includes('const interval = 2700 / playbackSpeed.value')
    && replayPage.includes('([1, 2, 3] as const)') && replayPage.includes('{{ speed.toFixed(1) }}')
    && replayPage.includes('if (atLast.value) return stop()') && replayPage.includes('class="replay-result"')
    && replayPage.includes("detail.value.match.winner ?? currentGame.value.winner")
    && replayPage.includes("result: winner === 0 ? '胜' : '负'") && replayPage.includes("result: winner === 1 ? '胜' : '负'"), '三类共用回放必须默认1倍速并提供1.0/2.0/3.0切换；按回放倍速缩短卡面动画且等待动画完成后推进，抵达最后一步立即停止并显示双方赛果'],
  [gamePage.includes('data-ui-contract="manual-game-over-exit"')
    && gamePage.includes("game.value?.phase === 'GameOver'")
    && gamePage.includes('leaveRoom()') && gamePage.includes("router.push('/lobby')")
    && !gamePage.includes('returnToRoom'), '胜负结算必须保持到玩家主动返回；返回时离开当前房间并直接进入对战大厅，不得回到原房间或赛事页'],
  [adminMatches.includes("name: 'admin-match-replay'")
    && replayPage.includes('Promise.all([adminApi.match(matchId), adminApi.replayPage(matchId)])')
    && replayPage.includes('ensureReplayStepLoaded') && replayPage.includes('detail.value.commands.push(...page.items)')
    && adminMatches.includes('view: view.value') && replayPage.includes("query: { ...route.query, section: 'matches'")
    && replayModel.includes('export function adminReplayDetail')
    && platform.includes('/replay?${params}')
    && !platform.includes('includeReplay=true')
    && l12ServerSources.includes('includeReplay ? "read-replay" : "read-detail"')
    && !replayPage.includes('adminApi.match(matchId, true)')
    && !adminMatches.includes('technical-replay') && !adminMatches.includes('JSON.stringify(detail.replay'), '玩家记录、JSON与后台对局档案必须共用同一个全屏回放播放器；后台回放须按稳定游标逐页读取，不能再请求一次性全量命令，并清除旧原始JSON渲染'],
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
  [playerMat.includes('const displayMoraleSlots = computed') && playerMat.includes('payable(right.resource) - payable(left.resource)') && playerMat.includes('rank(left.resource) - rank(right.resource)') && playerMat.includes("isGodPower ? (tapped ? 2 : 0) : (tapped ? 3 : 1)") && playerMat.includes(':key="morale.instanceId"') && playerMat.includes('inspectOrSelectMorale(morale.instanceId)') && board.includes(':key="choice.id"') && board.includes('togglePaymentResource(choice.id)'), '费用资源必须优先展示当前可支付实例，再按状态排序，桌面与大面板均必须保留真实士气实例 ID 作为支付与返还目标'],
  [playerMat.includes('class="god-power-logo"') && playerMat.includes('sepia(1) saturate(3.2)') && playerMat.includes('border-color:#f4dda1'), '神力必须使用淡黄色 Logo 与独立描边，不能继续与白色士气图标混淆'],
  [cardTile.includes('attachedGroups') && cardTile.includes('attached-card-orbs') && cardTile.includes("$emit('focusCard', group.card)"), '叠放卡牌必须由公共卡牌组件合并为圆形卡图，并可进入统一详情'],
  [l12StructuredSemantics.includes('GrantsStrongAttackWhileAttached')
    && l12StructuredSemantics.includes('HasEffectiveStrongAttack')
    && l12GameEngine.includes('HasEffectiveStrongAttack(card)'), '王者之剑、侵掠如火、自身与临时强攻必须共用同一有效关键词查询'],
  [cardTile.includes('const keywordRows = computed') && cardTile.includes('entries.slice(index * 3, index * 3 + 3)')
    && cardTile.includes('data-ui-contract="keywords-three-per-row-upward"') && cardTile.includes('class="card-keyword-row"')
    && cardTile.includes('bottom:var(--l12-card-keyword-bottom);display:flex') && cardTile.includes('flex-direction:column-reverse')
    && cardTile.includes('align-items:stretch;gap:var(--l12-card-keyword-gap);transform:translateX(-50%)')
    && cardTile.includes('.card-keyword-row{display:flex;width:100%;min-width:0;align-items:center;justify-content:center;gap:var(--l12-card-status-gap)}')
    && cardTile.includes('padding:var(--l12-card-keyword-pad);font-size:var(--l12-card-keyword-font)')
    && !cardTile.includes('status-strong'), '当前生效关键词必须在兵力上方整组居中，每行最多3个且换行向上生长，不得溢出卡牌容器或恢复单字角标'],
  [cardTile.includes("props.card.isMasterLegion === true") && cardTile.includes('displayBaseTroops'), '孙悟空等主宰军团化实体必须显示权威兵力且设定兵力不误判为增益'],
  [cardTile.includes('position:static!important') && cardTile.includes('object-position:center 14%'), '圆形叠放卡图不得被全局卡图定位规则覆盖'],
  [playerMat.includes("entry.id === 'trialAdvance'") && playerMat.includes('function canTrial') && playerMat.includes("'trialAdvance')"), '试炼军团必须拥有与进攻、移动并列的直接试炼按钮'],
  [playerMat.includes("!card.trialCompleted && (card.trialProgress ?? 0) < 8")
    && board.includes("!candidate.trialCompleted") && board.includes("(candidate.trialProgress ?? 0) < 8"), '当前可推进试炼的按钮与进度标记必须跳过已达8但尚未翻面的试炼，继续指向后续仍可推进的试炼'],
  [playerMat.includes("@click.stop=\"(!trial.hidden || side === 'my') && selectZoneCard(trial)\""), '试炼卡必须复用公开区域卡牌能力入口；己方未完成试炼仍可查看详情，对方未知试炼保持不可见'],
  [playerMat.includes('aspect-ratio:1752/1255') && playerMat.includes('class="trial-card-back"')
    && playerMat.includes('class="trial-progress"') && playerMat.includes('width:46px;min-width:46px;height:46px;min-height:46px')
    && playerMat.includes('font-variant-numeric:tabular-nums'), '试炼卡背必须保持正式横版素材比例，进度数字使用不会被挤压的独立大尺寸容器'],
  [handArea.includes('data-ui-contract="field-sized-safe-hand"') && handArea.includes('const cardWidth = computed(() => 114.4)')
    && handArea.includes('width:114.4px;height:160.6px;flex-basis:114.4px')
    && board.includes('.formation-slot .card-tile.tapped){width:114.4px;height:160.6px;flex-basis:114.4px}')
    && board.includes('.board-center{--l12-hand-lane-height:160px;display:grid;min-height:0;')
    && board.includes('width:calc(100% - 400px)') && board.includes('z-index:40')
    && handArea.includes('overflow-x:auto') && handArea.includes('ResizeObserver'), '双方手牌必须与场上军团同尺寸，限制在计时框与额外区之间并高于场面可点击，多数量时按实测宽度扇形收拢或横向滚动'],
  [board.includes('v-if="l12State.spectating" class="spectator-hand" hidden :count="viewMe.handCount || 0"')
    && board.includes('class="opponent-hand" hidden :count="viewEnemy.handCount || 0"')
    && !board.includes('class="spectator-hand" :cards="viewMe.hand"'), '观战者必须同时看到双方完整手牌数量与等量卡背，不得接收或渲染任一方手牌身份'],
  [playerMat.includes('class="master-marker-track"') && playerMat.includes('.master-marker-track{position:absolute') && playerMat.includes('.master-marker-track{top:-70px}.side-opponent .master-marker-track{top:auto;bottom:-70px}')
    && playerMat.includes('.rune-orb{width:36px;height:36px;min-width:36px') && playerMat.includes('.canopic-orb{width:36px;height:36px;min-width:36px'), '主宰附近符文与卡诺匹斯罐必须复用同一轨道、适量放大并与主宰保持间距'],
  [!playerMat.includes('class="rune-zone"') && !playerMat.includes('class="canopic-track"'), '符文与卡诺匹斯不得恢复各自独立的定位父级'],
  [board.includes('destructionRoundBackUrl'), '本局天灾圆形未知卡必须使用圆形天灾卡背'],
  [specialAssets.includes('disasterRoundUrl') && board.includes('disasterRoundUrl(card.cardId, card.imageUrl)') && board.includes('destructionRoundBackUrl'), '本局已知天灾必须使用官方圆形卡图，未知天灾继续使用专用卡背'],
  [lobby.includes('visibleDeckLabel') && lobby.includes('player.playerIndex === l12State.room?.yourPlayerIndex'), '房间内不得向对手公开牌库名称'],
  [decks.includes("构筑时不计入卡组数量") && decks.includes("`${counted}${uncounted ? `(${uncounted})` : ''}`"), '不计入构筑上下限的卡牌必须使用通用规则识别，并以 40(3) 形式单列数量'],
  [deckEditor.includes('publicDeckApi.publish') && deckEditor.includes("publicationId.value = ''") && deckEditor.includes("preservePublication = false"), '牌库编辑器须支持公开/更新公开牌库，并在新建、另存或切换本地牌库时隔离公开版本身份'],
  [deckLibrary.includes('publicDeckApi.list') && deckLibrary.includes('编辑公开牌库') && deckLibrary.includes('删除公开牌库') && deckLibrary.includes('ownerId === platformState.account?.id'), '公开牌库必须由服务端持久化，且仅作者显示编辑与删除入口'],
  [deckLibrary.includes('<DeckConstructionBrowser :entries="selectedEntries"')
    && deckLibrary.includes("add(deck.cardIds, 'main')") && deckLibrary.includes("add(deck.moraleIds, 'morale')")
    && deckLibrary.includes("add(deck.specialIds ?? [], 'special')") && deckLibrary.includes('automaticExtraCardIdsForMaster')
    && deckConstructionBrowser.includes('grid-template-rows:auto minmax(2.8em,auto) auto')
    && deckConstructionBrowser.includes('overflow-wrap:anywhere') && deckConstructionBrowser.includes('white-space:normal'), '公开牌库详情必须复用对局档案构筑查看器，展示主牌、士气与全部额外卡，卡框稳定且完整卡名不被裁切'],
  [prompt.includes('const displayCardIds = computed') && prompt.includes('if (displayCardIds.value.length) return displayCardIds.value')
    && prompt.includes('displayCardIds.value.length > 0') && prompt.includes('if (!p || !p.validChoices.includes(id)) return')
    && prompt.includes('v-for="choice in supplementalChoices"') && prompt.includes('@focus="focusChoice(choice)"'), '通用选项弹框必须把 displayCardIds 作为仅供查看的卡牌行，可打开卡牌详情但不得把展示牌误作合法选择，确认项保留在统一页脚'],
  [shell.includes('class="invitation-gate"') && !shell.includes('class="site-modal-mask invitation-gate"')
    && shell.includes('aria-modal="false"') && shell.includes('invitationMinimized')
    && shell.includes('class="invitation-stack"') && shell.includes('position:fixed;z-index:160;right:18px;bottom:18px')
    && l12Net.includes("message.type === 'friendInvitationResolved' || message.type === 'friendInvitationRevoked'")
    && l12Net.includes('l12State.friendInvitation?.invitationId === message.invitationId')
    && l12Net.includes('l12State.outgoingFriendInvitation?.invitationId === message.invitationId')
    && l12Net.includes("else if (message.type === 'friendRoomCreated')")
    && l12Net.includes("type: 'cancelFriendInvitation', invitationId")
    && shell.includes('@click="cancelOutgoingInvitation"') && shell.includes('outgoingInvitationMinimized')
    && l12Net.includes("l12State.spectating && message.message === '观战者不能执行对局操作'")
    && l12Net.includes('if (l12State.spectating || l12State.pendingAction) return'), '好友邀请必须是右下角无背板可最小化通知，发起方按服务端返回的精确邀请编号撤回，接收方只按匹配邀请撤销；观战端不得发送对局命令或重复显示权限提示'],
  [battleLog.includes("import { projectLog } from './logViewModel'") && battleLog.includes('computed(() => projectLog(props.events, props.you, props.names))')
    && battleLog.includes('class="combat-summary"') && battleLog.includes('class="combat-toggle"')
    && battleLog.includes('event-badge') && battleLog.includes('data-event-sequence')
    && battleLogViewModel.includes('PLAYER_LOG_VISIBLE_TYPES') && battleLogViewModel.includes('PLAYER_LOG_HIDDEN_TYPES')
    && battleLogViewModel.includes('PLAYER_LOG_REDLINE_TERMS') && battleLogViewModel.includes('playerLogContainsForbiddenTerms')
    && battleLogViewModel.includes("case 'reveal':") && battleLogViewModel.includes("'hand-add'")
    && battleLogViewModel.includes("case 'trial':") && battleLogViewModel.includes("case 'cost':")
    && battleLogViewModel.includes("case 'attach':") && battleLogViewModel.includes("case 'disaster-value':")
    && battleLogViewModel.includes('effectOutcomeBadges') && battleLogViewModel.includes('costMergesIntoFollowingResult')
    && battleLogViewModel.includes('isPrivateHandAddEvent') && battleLogViewModel.includes('precedingPublicAdd')
    && !battleLog.includes('event.text') && !battleLog.includes('omitted = new Set'), '玩家战报必须由白名单纯投影层生成，失败与内部流程默认隐藏；费用并入效果、进攻收拢为可展开小结，但符文、士气、试炼、公开区域与效果目标的实际变化不得被压缩丢失；玩家昵称、隐藏卡名和引擎原文不得直出'],
  [zoneMovementLayer.includes("event.type === 'reveal' && /加入手牌/.test(event.text)")
    && zoneMovementLayer.includes("to = 'hand'; label = '加入手牌'")
    && zoneMovementLayer.includes('publicHandAddCaption') && zoneMovementLayer.includes('movement-caption')
    && !zoneMovementLayer.includes("event.type === 'search'"), '公开展示并加入手牌必须进入统一区域移动队列并显示来源横幅，隐私入手不播放，历史 search 死分支不得恢复'],
  [platform.includes('views: number; likes: number; copies: number') && platform.includes('recordView: (id: string)')
    && platform.includes("sort?: 'copies' | 'likes' | 'views' | 'latest'")
    && wsServer.includes('/api/public-decks/{id}/view') && deckLibrary.includes('publicDeckApi.recordView(entry.id)')
    && deckLibrary.includes('b.copies - a.copies') && deckLibrary.includes('b.likes - a.likes')
    && deckLibrary.includes('(b.views ?? 0) - (a.views ?? 0)') && deckLibrary.includes('b.createdAt.localeCompare(a.createdAt)')
    && deckLibrary.includes('<option value="copies">最多复制</option>')
    && deckLibrary.includes('<option value="likes">最多点赞</option>')
    && deckLibrary.includes('<option value="views">最多浏览</option>')
    && deckLibrary.includes('<option value="latest">最新发布</option>')
    && deckLibrary.includes('v-model="seasonOnly"') && deckLibrary.includes('entry.seasonCompliant')
    && deckLibrary.includes('浏览量 {{ entry.views ?? 0 }}') && deckLibrary.includes('符合本赛季')
    && deckLibrary.includes('不符合本赛季') && deckLibrary.includes('查看构筑')
    && deckLibrary.includes('--deck-faction:') && deckLibrary.includes('rgba(var(--deck-faction),.2)')
    && deckLibrary.includes('color:#c7cecd;font-size:14px'), '公开牌库必须只提供最多复制、最多点赞、最多浏览、最新发布四项互斥排序，支持与本赛季合规筛选叠加，并保留浏览量/点赞/复制/赛季要求信息条与可读阵营底色'],
  [deckEditor.includes('masterProfileUrl(selectedMaster.id') && lobby.includes('border-radius:2px'), '主宰头像必须使用官方正方形资源'],
  [cardDetailContent.includes('trialValue') && cardDetailContent.includes('<dt>试炼值</dt>'), '卡牌档案必须展示试炼值'],
  [playerMat.includes('aria-disabled') && playerMat.includes('.morale-orb.active-morale[aria-disabled="true"]') && playerMat.includes('.morale-orb.active-god-power[aria-disabled="true"]'), '可用的活跃士气与神力必须始终高亮'],
  [playerMat.includes('class="morale-count resource-morale-count"') && playerMat.match(/resource-morale-count/g)?.length >= 2, '双方士气数量必须共用不溢出的独立计数器单元'],
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
  [decks.includes("card?.id === 'S02-01S1'") && decks.includes("card?.id === 'S02-06S2'")
    && decks.includes('isDerivedSpecialCard(card)') && deckEditor.includes('!isDerivedSpecialCard(card)'),
  '哮天犬与王者之剑必须共用衍生卡身份，不进入主牌库候选或保存校验'],
  [deckShare.includes('...(deck.specialIds ?? [])') && deckShare.includes('const extraIds = [...new Set([')
    && deckShare.includes("isHorizontalCardType(card?.cardType) ? 1752 / 1255 : 5 / 7")
    && deckShare.includes('extraBitmaps.forEach(bitmap => bitmap?.close())'), '牌库图必须包含显式试炼和主宰自动额外卡，且横版额外卡按正式横版比例绘制并释放位图'],
  [deckEditor.includes('const productOptions = computed(')
    && deckEditor.includes('const productFilters = ref<string[]>([])')
    && deckEditor.includes('v-for="value in productOptions"')
    && deckEditor.includes('productFilters.includes(value)')
    && deckEditor.includes('卡池（可多选）'), '牌库编辑器卡池筛选必须支持从完整目录动态列出并组合选择 S01、S02 与 ST 产品'],
  [deckEditor.includes('effectiveDeckLimit(card, masterId.value)')
    && deckEditor.includes('effectiveDeckLimit(entry.card, masterId)')
    && !deckEditor.includes('activeRestrictions') && !deckLibrary.includes('activeRestrictions')
    && decks.includes('rule.masterId === masterId') && decks.includes('rule.masterId === deck.masterId'),
  '通用牌库编辑、保存、导入与公开不得全局应用运营禁限卡；显式规则作用域仍保留主宰专属解析能力'],
  [cardArchive.includes('const productOptions = computed(') && cardArchive.includes('<option value="all">全部产品</option><option v-for="value in productOptions"') && sandboxPicker.includes('<option value="all">全部产品</option><option v-for="value in products"'), '卡牌图鉴与共享单卡选择器必须从权威完整目录动态列出产品，不得回退为旧双卡池硬编码'],
  [deckEditor.indexOf('生成牌库图') > deckEditor.indexOf('另存为牌库') && deckEditor.indexOf('生成牌库图') < deckEditor.indexOf('删除牌库'), '生成牌库图必须位于另存为牌库与删除牌库之间'],
  [deckEditor.includes('createDeckImageBlob') && deckEditor.includes('deck-image-dialog') && deckEditor.includes('下载牌库图'), '牌库编辑器必须提供可预览、下载的真实牌库图生成流程'],
  [deckOrdering.includes('TYPE_PRIORITY') && deckOrdering.includes('Number.NEGATIVE_INFINITY') && deckEditor.includes('compareDeckCards') && deckShare.includes('compareDeckCardIds') && deckLibrary.includes('compareDeckCardIds'), '牌库默认顺序必须统一为类型、本阵营/中立、费用高至低和编号前至后，并由编辑器、详情与牌库图复用'],
  [deckEditor.includes('牌库删除后不可找回') && deckEditor.includes('继续删除') && deckEditor.includes('pendingDeleteName') && !deckEditor.includes('@click="onDelete'), '全部牌库删除入口必须先经过统一确认弹框'],
  [gamePage.includes("import { gameAction, l12State, leaveRoom } from './net'") && gamePage.includes("game.value?.phase === 'GameOver'") && gamePage.includes('leaveRoom()') && gamePage.includes("router.push('/lobby')") && !gamePage.includes('returnToRoom'), '所有对局结算必须停留到玩家主动返回；返回后退出权威房间并进入对战大厅'],
  [l12Net.includes('leavingRoom: false') && l12Net.includes("if (l12State.leavingRoom) return") && l12Net.includes("socket.send(JSON.stringify({ type: 'leaveRoom' }))")
    && gameReentry.includes('!snapshot.leavingRoom') && l12Net.includes('gameReentry.requestExit(l12State.game?.matchId)'), '退出观战必须屏蔽迟到快照与自动路由，并在断线重连后优先重发退出请求'],
  [sandbox.includes('createSandbox') && sandbox.includes('沙盒不会进入个人对局记录或排位统计') && sandbox.includes('router-link v-if="!platformState.account" to="/me"') && !sandbox.includes('to="/profile"') && !sandbox.includes('沙盒服务器适配器尚未接入'), '单人测试沙盒必须连接正式规则内核且明确隔离正式记录；未登录入口必须直达实际账号页，不得回退为占位页或失效路由'],
  [gamePage.includes('<GmPanel v-if="l12State.gmEnabled"') && l12Net.includes('gmEnabled: false'), 'GM 面板必须只在服务端授权的沙盒快照中显示'],
  [gamePage.includes(':gm-panel-open="gmPanelOpen"') && gamePage.includes('@open-change="gmPanelOpen = $event"')
    && gmPanel.includes("openChange: [open: boolean]") && board.includes("'gm-panel-docked': gmPanelOpen && !compactViewport")
    && battleViewportLayout.includes('const GM_PANEL_RESERVE = 344')
    && battleViewportLayout.includes('options.gmPanelOpen && !compact ? GM_PANEL_RESERVE : 0')
    && board.includes('.board-viewport.gm-panel-docked{right:344px}'), '展开 GM 调试面板时必须为其保留独立停靠区并重算棋盘缩放，禁止覆盖计时、玩家信息或结束回合操作'],
  [gmPanel.includes("send({ type: 'gmAction'") || (gmPanel.includes('gmAction(') && l12Net.includes("pendingActionEnvelope = { type: 'gmAction', requestId: createActionRequestId(), command }")), 'GM 操作必须走独立 gmAction 消息，不得伪装成普通 gameAction'],
  [gmPanel.includes('导出可复现 JSON') && gmPanel.includes('/api/matches/'), 'GM 面板必须保留可复现记录导出入口'],
  [gmPanel.includes("run('setTroops'") && gmPanel.includes("run('startAttack'") && gmPanel.includes('发起规则内测试进攻'), 'GM 面板必须保留兵力设置与规则内测试进攻闭环'],
  [gmPanel.includes("run('addCard'") && gmPanel.includes('value: count.value') && gmPanel.includes('连续放置'), 'GM 卡牌区域操作必须支持连续构造同卡场景'],
  [gmPanel.includes('SingleCardPicker') && gmPanel.includes('选择卡片') && !gmPanel.includes('卡号，例如'), 'GM 卡牌与区域必须复用共享单卡选择器，不得恢复卡号输入框'],
  [gmPanel.includes('targetHand') && gmPanel.includes("run('moveHandCard'") && gmPanel.includes("run('playHandCard'"), 'GM 必须能查看并操作双方真实手牌实例'],
  [gmPanel.indexOf('主宰、天灾与阶段') < gmPanel.indexOf('卡牌与区域') && gmPanel.includes("run('nextPhase')"), 'GM 主宰、天灾与阶段必须位于卡牌与区域上方并可进入下一阶段'],
  [gmPanel.includes("run('returnCardToHand'") && gmPanel.includes('返回手牌'), 'GM 场上卡牌必须提供返回所有者手牌的操作'],
  [gmPanel.includes("run('resetCardEffects'") && gmPanel.includes('重置效果'), 'GM 场上卡牌必须提供重置所选卡牌回合1次效果限制的操作'],
  [!gmPanel.includes('手牌（GM 可操作）') && !gmPanel.includes('自动切换该方为回合玩家') && !gmPanel.includes('军团会返回棋盘'), 'GM 面板不得保留重复权限文字及已要求删除的说明'],
  [gameActions.includes("me.playerIndex === 1 - game.pendingDefense.attackerPlayer") && !gameActions.includes("game.activePlayer !== me.playerIndex") && gameActions.includes("game.activePlayer === me.playerIndex") && !gameActions.includes('game.activePlayer !== game.you'), '抵挡与支援必须依据本次战斗的防御方（含回合外反击）；阶段操作仍依据回合玩家，沙盒使用当前代操作席位'],
  [!board.includes('当前子阶段：') && !board.includes('data-ui-contract="combat-substage"') && board.includes('pending.attackValue > 0') && board.includes("pendingDefense?.stage === 'DefenseChoice'") && gameActions.includes("pendingDefense?.stage === 'DefenseChoice'"), '进攻界面必须消费服务端子阶段与冻结进攻值，只在 DefenseChoice 开放抵挡/支援，并禁止显示内部子阶段调试文字'],
  [prompt.includes("prompt.value?.data?.uiPattern === 'effect-decision'") && prompt.includes('isPureEffectDecision')
    && prompt.includes('isDirectActivationChoice')
    && prompt.includes("isDeclineChoice(id) ? '不发动' : '发动'") && prompt.includes('decisionEffectText')
    && prompt.includes("if (p.data?.choiceMode === 'instant' || isPureEffectDecision.value) { resolveChoice(id); return }")
    && prompt.includes('<footer v-if="!isPureEffectDecision"') && prompt.includes('!isPureEffectDecision">{{ kindLabel() }}')
    && prompt.includes('(hasCardChoices.value || (isEffectDecision.value && !isPureEffectDecision.value))'), '纯二选一卡效发动框必须仅显示来源、当前效果文本和等大的发动/不发动按钮，点击立即提交；费用选择不得误判为纯发动，拒绝动作须与确认选择保留在底部'],
  [l12TrialAdvancePlans.includes('["mode:none", "mode:trial", "mode:rune"]')
    && l12PromptSetup.includes('var isMultiModeDecision = kind == "option"')
    && l12PromptSetup.includes('validChoices.Any(choice => choice.Equals("mode:none"')
    && l12PromptSetup.includes('data["uiPattern"] = "effect-decision"')
    && centralModeChoiceLabels.get('mode:trial') === '试炼进度+1'
    && centralModeChoiceLabels.get('mode:rune') === '获得1枚符文'
    && centralModeChoiceLabels.get('mode:none') === '不发动'
    && prompt.includes('effect-option-list'), '兰斯洛特等多项选发效果必须在同一发动弹框展示每个实际效果选项及不发动，不能退化成无内容的确认框'],
  [cardPresentation.includes("tactic: '主动战术'")
    && !cardPresentation.includes("'counter-tactic': '反击战术'")
    && cardPresentation.includes('isCounterTacticCard')
    && cardPresentation.includes("cardType === 'tactic' && isCounterTactic")
    && decks.includes('isCounterTactic: authoritative?.isCounterTactic === true')
    && !decks.includes('S1_COUNTER_TACTICS')
    && board.includes('isCounterTacticCard(card)') && !board.includes('counterIds = new Set')
    && playerMat.includes('isCounterTacticCard(card)') && !playerMat.includes("['S01-0016'")
    && l12PromptSetup.includes('isCounterTactic')
    && deckConstructionBrowser.includes('cardTypeLabel(selected.cardType, selected.isCounterTactic)')
    && adminPage.includes('cardTypeLabel(card.cardType, card.isCounterTactic)')
    && adminPage.includes('cardTypeLabel(selectedEffect.cardType, selectedEffect.isCounterTactic)'), '主动/反击战术必须共用tactic类型并由独立属性贯穿目录、对战、弹框与后台；不得保留卡号清单或显示内部英文类型'],
  [l12PromptSetup.includes('"discard-or-decline", "optional-card", "search"') && l12PromptSetup.includes('data.TryAdd("layout", "single-row")') && l12PromptSetup.includes('data["displayCardIds"]') && prompt.includes("prompt.value?.data?.layout === 'single-row'") && prompt.includes('displayCardIds') && prompt.includes('unavailable'), '弃牌及查看多张选择部分必须使用横向全卡图列表，并将不合法卡灰置不可选'],
  [l12PromptSetup.includes('ExpandGraveyardSelectionDisplay(playerIndex, kind, validChoices, data)')
    && l12PromptSetup.includes('kind.Equals("grave-card"') && l12PromptSetup.includes('SelectMany(player => player.Graveyard)')
    && prompt.includes('displayCardIds') && prompt.includes(':unavailable="isCardSelectionPrompt && !prompt.validChoices.includes(choice)"'), '墓地选择必须统一展示对应墓地全部卡牌，并仅允许服务端合法候选被点击'],
  [prompt.includes('displayChoiceIds') && prompt.includes('disabledChoiceReason')
    && prompt.includes("'unavailable-choice': Boolean(disabledChoiceReason(choice))")
    && prompt.includes('<small v-if="disabledChoiceReason(choice)">{{ disabledChoiceReason(choice) }}</small>'), '多分支效果必须能同时展示可用与灰置选项，并显示服务端给出的不可用原因'],
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
  [prompt.includes('<template v-for="choice in primaryChoices" :key="choice">')
    && prompt.includes(':card-id="cardIdFor(choice)"')
    && prompt.includes('@focus="focusChoice(choice)" @select="toggle(choice)"')
    && prompt.includes('const currentChoices = computed(() => isMulligan.value ? (me.value.hand ?? []).map(card => card.instanceId) : (prompt.value?.validChoices ?? []))'), '天灾禁用等卡牌Prompt必须以服务端validChoices实例ID贯穿卡图、聚焦与提交，不得用显示顺序、名称或cardId替代实例ID'],
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
  [board.includes('const modalPresentationPaused = computed') && board.includes('hasBlockingPrompt.value && !promptMinimized.value') && board.includes(':paused="passivePresentationPaused"') && board.includes('v-if="publicReveal && !activeBoardPromptId"') && board.includes('v-if="diceReveal && !activeBoardPromptId"') && board.includes('v-if="combat && !activeBoardPromptId"') && board.split(':interaction-prompt-active="Boolean(hasBlockingPrompt)"').length === 3 && playerMat.includes('watch(() => props.interactionPromptActive') && prompt.includes('.l12-prompt-overlay,.l12-prompt-overlay.minimized{z-index:3000!important}') && board.includes('.board-target-controls{z-index:3000}') && board.includes('.card-inspector-floating{z-index:3100!important}') && actionLayer.includes('.l12-action-presentation{z-index:900}') && zoneMovementLayer.includes('.zone-card-movement{z-index:902}') && board.includes('watch(activeBoardPromptId, promptId => {') && zoneMovementLayer.includes('if (paused && active.value) cancelActiveMovement()') && !zoneMovementLayer.includes('queue.unshift(interrupted)'), '展开的 Prompt、墓地、主宰及移动端弹框必须暂停尚未开始的卡面动画；已开始的动画应立即结束且不重播，交互层仍保持最高层级'],
  [zoneMovementLayer.includes("event.type === 'disaster-reveal'") && zoneMovementLayer.includes("from = 'disaster'; to = 'disaster'; label = '天灾翻开'") && board.includes('data-l12-zone="disaster"') && zoneMovementLayer.includes('disaster-reveal-card') && zoneMovementLayer.includes('/assets/l12/card-back-disaster.png'), '没有触发效果的天灾公开也必须从天灾牌背翻出卡面，并进入统一的可暂停动画队列'],
  [globalStyle.includes('.l12-effect-body{font-size:var(--l12-effect-copy,clamp(') && globalStyle.includes('overflow-wrap:anywhere;white-space:pre-wrap') && globalStyle.includes('.l12-effect-body--compact{font-size:var(--l12-effect-copy,clamp(')
    && cardDetailContent.includes('<p class="l12-effect-body">{{ card.effect')
    && cardArchive.includes("import CardDetailContent from './CardDetailContent.vue'")
    && deckEditor.includes("import CardDetailContent from './CardDetailContent.vue'")
    && deckEditor.includes(':show-catalog-only="false"')
    && board.includes('<CardDetailContent :card="focusDetailCard" :show-catalog-only="false" />') && prompt.includes("'l12-effect-body': isEffectOptionList") && masterOverlay.includes('player.master.effectText') && masterOverlay.includes('class="l12-effect-body l12-effect-body--compact">{{ entry.label }}') && playerMat.includes('player.factionEffect?.effectText') && playerMat.includes('class="l12-effect-body l12-effect-body--compact"') && adminPage.includes('<p class="l12-effect-body">{{ selectedEffect.effectText') && adminPage.includes('<span class="l12-effect-body">{{ ability.costText }}</span>') && adminPage.includes('<span class="l12-effect-body">{{ ability.resolutionText }}</span>') && adminPage.includes('.security-metrics,.effect-segments{grid-template-columns:1fr}') && !cardDetailContent.includes('archive-number l12-effect-body') && !deckEditor.includes('<small class="l12-effect-body">{{ selected.number') && !globalStyle.includes('--l12-board-readable'), '全站卡效正文必须共用自适应语义字号并保留权威换行，覆盖卡牌详情、牌库编辑、对战、Prompt、主宰/阵营与管理后台；辅助信息按组件空间使用metadata/micro层级，不得恢复全项目14px硬阈值'],
  [gmPanel.includes("emit('armPlacement'") && gamePage.includes(':gm-placement="gmPlacement"') && board.includes("emit('gmPlacementResolved')") && board.includes('GM：请选择'), 'GM 打出军团必须回到棋盘并点击目标玩家的绿色空位'],
  [playerMat.includes('selectRunePayment') && playerMat.includes('`rune:${index}`') && playerMat.includes('payable: paymentChoiceIds'), '符文支付必须直接点击场上的可用符文，不得恢复编号弹框'],
  [playerMat.includes('data-ui-contract="independent-trial-action"') && playerMat.includes("emit('ability', player.field[row][slot]!, 'trialAdvance')")
    && playerMat.includes("(canUseAbilities(player.field[row][slot]!) || canTrial(player.field[row][slot]!))")
    && playerMat.includes('<button v-if="canTrial(player.field[row][slot]!)" type="button"')
    && !playerMat.includes('class="trial-direct-action"'), '全部试炼军团的试炼按钮必须与进攻/移动共用军团上方居中动作栏，但仍独立直达试炼且遵守资格与忙态'],
  [board.includes('data-ui-contract="cancel-local-attack-selection"') && board.includes('@click="cancelLocalAttackSelection"')
    && board.includes("if (mode.value !== 'attack' || combat.value || hasBlockingPrompt.value) return")
    && board.includes("mode.value = 'play'") && board.includes('.board-mode-hint button{min-width:58px;min-height:44px')
    && !board.includes("cancelLocalAttackSelection() {\n  command('attack'"), '选择进攻对象提示必须提供最小44px取消按钮，只清理未提交的本地进攻选择，不能发送撤销权威进攻的命令'],
  [board.includes('boardSlotTargetPlayerIndex') && board.includes('targetPlayerIndex') && playerMat.includes("promptSlotIds?.includes(`${row}:${slot}`)"), '跨阵营位移的目标阵地必须高亮实际被移动军团所在战场，不得回退为操作者自己的同坐标格'],
  [l12Net.includes("type: 'sandboxAction', requestId: createActionRequestId(), actingPlayerIndex, command") && board.includes('controlledPlayerIndex') && board.includes('const viewMe = computed(() => props.game.players[props.game.you])') && board.includes('const viewEnemy = computed(() => props.game.players[1 - props.game.you])') && board.includes('v-if="l12State.gmEnabled" class="opponent-hand" :cards="viewEnemy.hand"') && board.includes(':cards="viewMe.hand"') && board.includes(':controllable="isControlledPlayer(viewEnemy.playerIndex)"') && prompt.includes('sandboxAction(actingPlayerIndex, command)') && globalStyle.includes('.opponent-hand .hand-actions{top:calc(100% + 4px);bottom:auto}'), '沙盒必须固定我方在下、对方在上，不交换棋盘，同时可查看双方手牌并代行双方规则内选择；上方手牌操作按钮必须朝棋盘中心展开而不被裁切'],
  [board.includes(':mine="masterPlayerIndex === controlledPlayerIndex"') && board.includes("sandboxAction(controlledPlayerIndex.value, { type, ...extra })") && prompt.includes('sandboxAction(actingPlayerIndex, command)'), '沙盒代操作对方时必须按受控方索引开放主宰效果并完成后续提示，正式房仍只允许登录座位'],
  [board.includes('watch(activeBoardPromptId, promptId => {') && board.includes('graveyardPlayer.value = null') && board.includes('masterPlayerIndex.value = null') && board.includes('focusCard.value = null'), '任何场面直选 Prompt 开始时必须关闭墓地、效果弹框与浮动卡牌详情'],
  [board.includes('const hasBlockingPrompt = computed') && board.includes('function clearOrdinaryInteractionState()') && board.includes('watch(hasBlockingPrompt')
    && board.includes("if (hasBlockingPrompt.value && type !== 'resolvePrompt') return")
    && board.includes(':placement-mode="!hasBlockingPrompt &&') && prompt.includes('function withPromptBinding(')
    && prompt.includes('activationId: p.activationId') && board.includes('activationId: prompt.activationId'),
  '任意 Prompt（包括最小化状态）出现时必须清空并封锁普通手牌/战场操作；resolvePrompt 必须回传服务端下发的不可变事务绑定'],
  [graveyardOverlay.includes('function selectCard(card: Card)') && graveyardOverlay.includes("enabledAbilities.length === 1") && !graveyardOverlay.includes('graveyard-abilities'), '墓地主动效果必须点击卡牌本身进入是否发动流程，卡面不得重新覆盖效果文字按钮'],
  [sandboxPicker.includes('loadDeckCatalog') && !sandboxPicker.includes("fetch('/data/l12/cards.s1.json')") && sandboxPicker.includes('搜索卡名、编号或效果文字') && sandboxPicker.includes('全部阵营'), '共享单卡选择器必须复用卡牌图鉴的完整卡池搜索与筛选逻辑'],
  [starterCards.length === 76 && starterCards.some(card => card.id === 'ST06-01' && card.nameZh === '伊丽莎白一世'), '前端权威目录必须收录 76 张 ST 产品卡，并以数据库中的伊丽莎白一世为准'],
  [sandbox.includes('<option value="custom">自定天灾（四张始终公开）</option>') && board.includes("type: 'replaceDisaster'") && board.includes('index < 3'), '自定天灾必须四张公开、前三槽可更换且第四槽堙灭锁定'],
  [adminPage.includes('卡效统一工作台') && adminPage.includes('atom-flow') && adminPage.includes('原子定义 JSON'), '管理后台必须保留卡效统一工作台、原子组合、流程图与原始定义视图'],
  [adminPage.includes('旧实现兜底') && adminPage.includes('新旧实现不会同时结算'), '原子化后台必须明确显示旧实现兜底与防重复结算边界'],
  [platform.includes("effectAtoms: () => platformRequest<EffectAtomDescriptor[]>('/api/admin/effect-atoms')") && platform.includes('/api/admin/effects/coverage'), '卡效后台必须从服务端权威原子注册表读取数据'],
  [adminPage.includes('实战已验证') && adminPage.includes('effectCoverage.verifiedAbilities') && platform.includes('verifiedAbilities: number'), '原子化后台必须区分文本拆分与已接管实战执行的能力'],
  [adminPage.includes('class="effect-scroll"') && adminPage.includes('overflow-y:auto') && adminPage.includes('human-assisted') && adminPage.includes('confirmed'), '原子化能力清单必须可纵向滚动，并区分人工辅助与人工确认状态'],
  [platform.includes('siteContentApi') && officialHome.includes('v-if="ready"') && officialHome.includes('siteContentApi.home()') && platform.includes('platformRequest<Article[]>(`/api/articles') && adminPage.includes('AdminSiteContentPanel') && adminPage.includes("tab === 'content'") && newsPage.includes('articleApi.list') && !adminPage.includes('<section class="news-editor"'), '官网固定内容必须批量加载避免默认文案闪烁；资讯须使用独立稿件接口与后台工作台，不得继续嵌在官网内容表单中'],
  [adminPage.includes('站点内容工作台') && adminSiteContent.includes('<AdminArticlesPanel') && adminSiteContent.includes('kind="news"') && adminArticles.includes('class="article-editor') && adminArticles.includes('保存草稿') && adminArticles.includes('发布 / 安排发布') && adminArticles.includes('历史版本') && adminArticles.includes('MediaUploadField') && adminArticles.includes('v-model="selected.link"'), '后台资讯发布必须提供独立列表、完整稿件编辑、封面与链接、发布状态和历史版本恢复'],
  [mediaUploadField.includes('ORIGINAL_MAX_BYTES = 16 * 1024 * 1024')
    && mediaUploadField.includes('REQUEST_MAX_BYTES = 32 * 1024 * 1024')
    && mediaUploadField.includes('class="media-ratio-hint"')
    && !mediaUploadField.includes('上传尺寸参考') && !mediaUploadField.includes('安全区与裁切：')
    && platform.includes("response.status === 413 && path === '/api/admin/site/media'")
    && wsServer.includes('IHttpMaxRequestBodySizeFeature') && wsServer.includes('SiteMediaRequestMaxBytes')
    && nginxSite.includes('client_max_body_size 1m;')
    && nginxSite.includes('location = /api/admin/site/media')
    && nginxSite.includes('client_max_body_size 32m;') && nginxSite.includes('media_upload_too_large')
    && !nginxHttpSite.includes('location = /api/admin/site/media') && !nginxHttpSite.includes('proxy_pass')
    && nginxHttpSite.includes("return 503 'testrun TLS bootstrap in progress")
    && serverDeploy.includes("grep -Fq 'location = /api/admin/site/media'")
    && serverDeploy.includes("grep -Fq 'client_max_body_size 32m'"), '站点图片上传必须只在浏览器、TLS Nginx 精确路由和 ASP.NET 端保留格式与体积安全边界；界面只显示一行建议比例'],
  [mediaUploadField.includes("createImageBitmap(source, { imageOrientation: 'from-image' })")
    && mediaUploadField.includes('v-if="isHero" class="hero-upload-field"')
    && mediaUploadField.includes("type HeroVariantKey = 'desktop' | 'mobile' | 'thumbnail'")
    && mediaUploadField.includes('heroStatuses[spec.key].startsWith(\'已选择\')')
    && mediaUploadField.includes('renderIndependentVariant') && mediaUploadField.includes('renderFlexibleVariant')
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
  [mediaUploadField.includes('renderFlexibleVariant') && !mediaUploadField.includes('validateIndependentSource')
    && !mediaUploadField.includes('原图方向归一后至少') && !mediaUploadField.includes('必须使用')
    && platform.includes('flexibleDimensions: boolean')
    && siteContentStore.includes('Upload safety is deliberately limited')
    && !siteContentStore.includes('policy.FlexibleDimensions ? null'), '全部站点图片入口必须允许任意像素与长宽比，完整保留构图并仅等比例生成交付WebP；服务端只保留格式与体积安全校验'],
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
  [adminPage.includes('数据管理') && adminPage.includes('AdminMatchesPanel') && adminPage.includes('AdminGlobalDataPanel') && adminPage.includes('AdminCardAnalyticsPanel')
    && adminPage.includes("hasPermission('admin.matches.read')") && adminPage.includes("hasPermission('admin.analytics.read')")
    && platform.includes('/api/admin/matches') && platform.includes('/api/admin/analytics/global') && platform.includes('/api/admin/analytics/cards'), '后台必须以独立权限和正式模块提供对局档案、全局数据与卡牌数据，不得塞入 Bug 管理或复用玩家私有记录接口'],
  [adminMatches.includes("type MatchView = 'recent' | 'player' | 'sandbox'") && adminMatches.includes("view === 'recent'") && adminMatches.includes("view === 'player'")
    && adminMatches.includes('participant.deckCards') && adminMatches.includes('结构化对局时间线')
    && adminMatches.includes('进行中对局不展示私有构筑')
    && adminMatches.includes("mode: view.value === 'sandbox' ? 'sandbox'")
    && adminMatches.includes('管理员专用沙盒回放') && adminMatches.includes('实际通常保留约 7～14 天')
    && adminMatches.includes("summary.status === 'completed' || (view.value === 'sandbox' && summary.modeId === 'sandbox')")
    && adminMatches.includes("error.code === 'sandbox_replay_expired'") && adminMatches.includes('回放已过期')
    && replayPage.includes("reason.code === 'sandbox_replay_expired'") && replayPage.includes("? '回放已过期'"), '对局档案必须支持最近/按玩家查询和独立的管理员沙盒排查；沙盒不混入正式记录，管理员可排查各状态沙盒，过期回放明确失败关闭'],
  [adminMatches.includes('data-ui-contract="match-snapshot-view-construction"')
    && adminMatches.includes('<DeckConstructionBrowser :entries="deckViewer.deckCards"')
    && adminMatches.includes('copyArchivedDeckCode') && adminMatches.includes('exportArchivedDeckImage')
    && adminMatches.includes('copyArchivedDeckToLibrary') && adminMatches.includes("expand('special')")
    && deckConstructionBrowser.includes('aria-label="构筑筛选"')
    && deckConstructionBrowser.includes('entry.quantity') && deckConstructionBrowser.includes('const selected = computed')
    && !adminMatches.includes('v-for="card in participant.deckCards"'), '对局档案必须以“查看构筑”打开不可变当局快照，复用牌库式搜索、分类、数量与卡牌详情，并可复制牌库码、导出牌库图或复制到我的牌库，档案正文不得继续平铺单卡'],
  [adminCardAnalytics.includes('实际使用情况') && adminCardAnalytics.includes('构筑收录') && adminCardAnalytics.includes('实际抽到')
    && adminCardAnalytics.includes('从手牌打出') && adminCardAnalytics.includes('效果发动') && adminCardAnalytics.includes('正常结算')
    && adminCardAnalytics.includes('同条件未携带基线') && adminCardAnalytics.includes('不代表因果'), '卡牌数据必须展示独立使用指标、公平对照、样本与相关性边界，禁止用裸胜率冒充卡牌因果影响'],
  [adminCardAnalytics.includes('使用方主宰') && adminCardAnalytics.includes('对方主宰')
    && adminCardAnalytics.includes('function analyticsQuery() { return { ...filters.value } }')
    && adminCardAnalytics.includes('const query = analyticsQuery()')
    && adminCardAnalytics.includes('adminApi.cardAnalytics({ ...query')
    && adminCardAnalytics.includes('adminApi.cardAnalyticsDetail(cardId, query)')
    && adminCardAnalytics.includes('request === detailRequest')
    && adminCardAnalytics.includes('data-ui-contract="card-analytics-low-sample-warning"')
    && adminCardAnalytics.includes("return '低样本，仅供参考'")
    && platform.includes("opponentMasterId?: string") && l12ServerSources.includes('OpponentMasterId'), '卡牌数据必须区分使用方/对方主宰，并让样本、入组率、基线与明细使用同一筛选，低样本必须明确警示'],
  [adminCardAnalytics.includes('参赛方 × 对局') && adminCardAnalytics.includes('同条件未携带基线')
    && adminCardAnalytics.includes('detail.summary.drawnSamples') && adminCardAnalytics.includes('detail.summary.playedSamples')
    && adminCardAnalytics.includes('detail.summary.activatedSamples') && adminCardAnalytics.includes('detail.summary.settledSamples')
    && platform.includes('drawnSamples: number') && platform.includes('settledSamples: number'), '卡牌数据使用路径必须统一使用参赛方样本，不能混入事件次数；没有对照时必须显示空值，不能伪造0%基线'],
  [adminCardAnalytics.includes('首次抽到／打出回合') && adminCardAnalytics.includes('携带数量分布')
    && adminCardAnalytics.includes('主宰对阵热图') && adminCardAnalytics.includes('数据质量与覆盖')
    && adminCardAnalytics.includes('最近已结束对局') && adminCardAnalytics.includes('规则版本')
    && adminCardAnalytics.includes('赛季') && adminCardAnalytics.includes('先后手')
    && platform.includes('turnDistribution: Array') && platform.includes('quantityDistribution: Array')
    && platform.includes('matchups: Array') && platform.includes('metrics: AdminAnalyticsMetricCoverage[]'), '单卡事实仪表盘必须保留时点、携带量、对阵、切片、指标级覆盖与仅已结束下钻'],
  [platform.includes('function localDateBoundary') && platform.includes('toUtc: localDateBoundary(query.to, true)')
    && l12ServerSources.includes('QueryInclusiveEndDate') && l12ServerSources.includes('SanitizeAnalyticsRecentMatch')
    && l12ServerSources.includes('Status: "completed"') && l12ServerSources.includes('AccountId = null')
    && !adminCardAnalytics.includes('monospace'), '日期筛选的结束日必须包含当天；分析权限响应不得泄露近期对局身份/牌库，仪表盘必须保持黑体栈'],
  [adminCardAnalytics.includes("mode: 'ranked'") && adminCardAnalytics.includes('today.getDate() - 29')
    && adminCardAnalytics.includes('平均携带数量') && adminCardAnalytics.includes('averageQuantity.toFixed(2)')
    && !adminCardAnalytics.includes('v-model="filters.mode"')
    && adminCardAnalytics.includes('filters.effectVersion') && adminCardAnalytics.includes('sampleStructure?.distinctPlayers')
    && adminCardAnalytics.includes('comparison?.delta') && adminCardAnalytics.includes("interval?.status !== 'available'")
    && !adminCardAnalytics.includes('winRateDelta >= .05')
    && platform.includes('AdminAnalyticsConfidenceInterval') && platform.includes('averageQuantity: number'), '单卡仪表盘只能分析排位，默认当前卡效版本近30日；必须显示玩家样本结构和分层差值，仅可用的不确定性区间可用于方向着色'],
  [l12ServerSources.includes('PrepareAnalyticsScopeAsync')
    && l12ServerSources.includes('CREATE TEMP TABLE l12_analytics_eligible')
    && l12ServerSources.includes('CREATE TEMP TABLE l12_analytics_fact_stats')
    && l12ServerSources.includes('WilsonInterval') && l12ServerSources.includes('ReadStratifiedComparisonsAsync')
    && l12ServerSources.includes('not-estimated'), '单卡分析必须复用参赛方聚合与目标卡事实，并使用分层对照；未修正样本依赖不得伪造差值置信区间'],
  [profilePage.includes('class="admin-button"') && profilePage.includes('⚙ 管理后台') && profilePage.includes('反馈 Bug 和建议') && profilePage.includes('本赛季排位') && !profilePage.includes('自设卡背'), '个人中心须以按钮提供管理后台入口并整合反馈与排位资料，且不得出现未规划的自设卡背功能'],
  [profileAuthForm.includes('@submit.prevent="submitAuth"') && profileAuthForm.includes('type="submit"')
    && (profileAuthForm.match(/\brequired\b/g)?.length ?? 0) === 2
    && profileAuthForm.includes(':disabled="authBusy || !auth.username.trim() || !auth.password"')
    && profileAuthForm.includes('v-if="authNotice"') && profileAuthForm.includes('role="alert"')
    && profileAuthForm.includes('aria-live="assertive"') && profilePage.includes("if (authBusy.value) return")
    && profilePage.includes("authMode.value === 'login' ? '登录中…' : '正在建立账号…'"), '个人中心登录/注册必须使用语义表单，支持回车且空字段不可提交；请求中须禁用并以同步忙碌门禁阻止快速重复提交，结果须在表单旁无障碍播报'],
  [profilePage.includes('error instanceof PlatformRequestError') && profilePage.includes('error.status === 403')
    && profilePage.includes('error.status === 401 || error.status === 429')
    && profilePage.includes('当前连接的站点节点已失效') && profilePage.includes('彻底关闭浏览器中的所有十二军团页面')
    && profilePage.includes('暂时无法连接账号服务，请检查网络后重试')
    && profilePage.includes('else authNotice.value = authenticationFailureMessage(error)')
    && l12ServerSources.includes('"用户名或密码错误"') && l12ServerSources.includes('"login_rate_limited"'), '登录失败须就地覆盖安全统一的401、限流429、失效节点403及网络异常；不得用提示区分账号不存在、密码错误、禁用或删除'],
  [profilePage.includes("if (authMode.value === 'login') await login(auth.username, auth.password)")
    && profilePage.includes('authenticationCompleted = Boolean(platformState.account)')
    && profilePage.includes('账号已登录，但后续数据同步失败')
    && platform.includes('authState.verified = true') && platform.includes("localStorage.setItem('l12-auth-token', token)")
    && app.includes('if (token && verified) startAutomaticConnection()'), '登录成功必须先提交权威账号与令牌再启动全站WebSocket；后续牌库或资料同步失败不得把已成功认证伪装成登录失败'],
  [profilePage.includes('<p v-if="notice" class="notice" role="status" aria-live="polite" aria-atomic="true">')
    && profilePage.indexOf('<p v-if="notice" class="notice"') < profilePage.indexOf('<section v-if="ranked"')
    && profilePage.includes('.notice{position:fixed;') && profilePage.includes('z-index:90;'), '个人中心的称号、改密、邮箱与会话操作必须共用当前视口可见的状态播报，不得再把唯一反馈放到整页内容末尾'],
  [friendsPage.includes("tab === 'blocked'") && friendsPage.includes('friendApi.blocked()') && friendsPage.includes('selectedPresence?.canInvite') && friendsPage.includes('selectedPresence?.canSpectate'), '好友中心须支持申请、屏蔽，并按在线状态在邀请对战与观战之间切换'],
  [platform.includes('permissions?: string[]') && adminPage.includes("hasPermission('admin.bugs.read')") && adminPage.includes("hasPermission('admin.accounts.read')") && adminPage.includes("hasPermission('admin.operations.read')"), '后台前端入口必须消费服务端权限矩阵，不得只依赖散落角色字符串'],
  [platform.includes('let authRefreshPromise: Promise<PlatformAccount | null> | null = null')
    && platform.includes('if (authRefreshPromise) return authRefreshPromise')
    && platform.includes("platformRequest<PlatformAccount>('/api/auth/me', { signal: controller.signal })")
    && platform.includes('const requestToken = platformState.token') && platform.includes('if (platformState.token !== requestToken) return platformState.account')
    && platform.includes('remember(account, requestToken)') && platform.includes('AUTH_REFRESH_REQUEST_TIMEOUT_MS'), '账号初始化与权限刷新必须去重、有界读取 /api/auth/me，按请求令牌防竞态并以权威响应覆盖本地缓存'],
  [platform.includes('response.status === 401 && requestToken && platformState.token === requestToken') && platform.includes('forgetAccount(requestToken)') && platform.includes('error instanceof PlatformRequestError && error.status === 401') && platform.includes('throw error'), '任意携带当前令牌的 401 必须按请求令牌防竞态清理，网络与 5xx 则保留令牌并保持未验证'],
  [platform.includes('response.status === 403 && requestToken && platformState.token === requestToken') && platform.includes('authState.verified = false') && platform.includes('refreshCurrentAccount({ force: true })') && platform.includes('if (!authState.verified) return false'), '403 必须使权限 UI 立即失败关闭并触发去重身份刷新，缓存身份不得直接授予权限'],
  [router.includes("meta: { requiresAdmin: true }") && router.includes('router.beforeEach(async to =>') && router.includes('refreshCurrentAccount()') && router.includes('!authState.verified || !platformState.account') && router.includes("return { name: 'me', query: { redirect: to.fullPath } }") && adminPage.includes('await refreshCurrentAccount()') && adminPage.includes('if (!canAccessAdmin.value) return') && adminPage.includes('!authState.initialized || authState.refreshing'), '路由复用已验证身份以保持连接；首次身份验证、管理数据及未验证/非管理员访问仍必须失败关闭'],
  [platform.includes("'/api/auth/sessions/current'") && platform.includes("'/api/auth/sessions'") && profilePage.includes('登录设备与会话') && profilePage.includes('退出其他设备') && profilePage.includes('退出全部设备'), '账号安全页必须支持服务端会话列表、当前设备及全端撤销'],
  [platform.includes("path === '/api/auth/email/capability'") && platform.includes("path === '/api/auth/email/verify'") && platform.includes("path === '/api/auth/password/forgot'") && platform.includes("path === '/api/auth/password/reset'") && platform.includes("'/api/auth/email/bind'") && platform.includes("'/api/auth/email/unbind'") && profilePage.includes("authMode === 'login' && emailFeatureEnabled") && profilePage.includes('v-if="emailFeatureEnabled" class="email-manager"'), '邮箱关闭时必须隐藏绑定与找回入口；显式启用后仍保留完整端点且匿名恢复请求不得携带现有登录令牌'],
  [router.includes("path: '/auth/recovery'") && recoveryPage.includes("location.hash.replace(/^#/, '')") && recoveryPage.includes("history.replaceState(null, '',") && recoveryPage.includes('!emailFeatureEnabled') && recoveryPage.includes('邮箱绑定、验证与找回功能当前未开放') && recoveryPage.includes('确认验证邮箱') && !recoveryPage.includes('submitVerification() }'), '恢复页必须先读取服务端能力并在关闭时失败关闭；启用时从 URL fragment 读取并立即清除令牌，且不得自动消费一次性令牌'],
  [platform.includes('resetAccountPassword:') && platform.includes('deleteAccount:') && adminPage.includes('临时密码 123456') && adminPage.includes('删除与清理') && adminPage.includes('根 Admin 与操作者自身受保护'), '管理后台必须提供受保护的临时密码重置、全会话撤销与逻辑删除个人数据清理入口'],
  [platform.includes('mustChangePassword?: boolean') && router.includes('platformState.account.mustChangePassword') && profilePage.includes('必须修改密码'), '管理员重置后的账号必须被导航守卫限制到我的页并明确要求修改临时密码'],
  [platform.includes('options: { revokeServer?: boolean } = {}') && profilePage.includes('logout({ revokeServer: false })'), '服务端已撤销当前或全部会话后必须只清理本机状态，不得用失效令牌重复调用撤销接口'],
  [platform.includes('revokeSession: (id: string, sessionId: string)') && platform.includes('/sessions/${encodeURIComponent(sessionId)}') && adminPage.includes('revokeAccountSessions') && adminPage.includes('撤销会话'), '管理员必须能按账号撤销服务端会话'],
  [platform.includes("headers.set('X-Correlation-ID'") && platform.includes('PlatformRequestError') && adminPage.includes('关联 ID：'), 'HTTP 请求、错误提示与管理审计必须贯通关联 ID'],
  [platform.includes('/api/admin/v1/commands') && platform.includes('idempotencyKey: body.idempotencyKey || commandKey(prefix)')
    && platform.includes('dryRun: boolean; expectedVersion?: number') && platform.includes('failureReason?: string; correlationId: string; resourceVersion: number')
    && adminPage.includes('管理操作记录') && adminPage.includes('新操作直接执行并写入审计')
    && adminPage.includes('历史待处理请求不会自动执行') && adminPage.includes('命令详情') && adminPage.includes('失败：')
    && l12AdminControlPlane.includes('L12Authorization.HasPermission(command.Actor, permission)')
    && l12AdminControlPlane.includes('HighRiskAuditAvailable()') && l12AdminControlPlane.includes('idempotency_conflict')
    && l12AdminControlPlane.includes('version_conflict') && l12AdminControlPlane.includes('PersistAdminCommandResult')
    && l12AdminControlPlane.includes('Outcome = normalized.DryRun ? "dry-run"')
    && l12AdminControlPlane.includes('"approval_disabled"'), '后台新命令必须由有权限管理员直接执行，同时保留幂等、版本冲突、高风险审计失败关闭、持久结果和历史待审批隔离'],
  [platform.includes('/api/admin/v1/content/publish') && platform.includes('/api/admin/v1/content/rollback') && adminSiteContent.includes('直接发布') && adminSiteContent.includes('直接回滚') && !adminSiteContent.includes('双人审批'), '官网内容必须通过服务端批量命令直接发布与回滚，不得恢复前端逐键发布或双人审批'],
  [adminSiteContent.includes('previewContent') && adminSiteContent.includes('发布预览完成') && adminSiteContent.includes('wouldChange') && adminSiteContent.includes('未写入线上内容'), '内容后台必须展示不写入的发布预览与变化摘要'],
  [adminPage.includes('auditCommandId') && adminPage.includes('auditCorrelationId') && adminPage.includes('auditOutcome'), '审计页必须可按结果、命令 ID 与关联 ID 筛选'],
  [platform.includes("releaseArtifacts: () => platformRequest<VerifiedReleaseArtifact[]>('/api/admin/v1/releases/artifacts')") && !platform.includes('registerReleaseArtifact') && adminPage.includes('Web 端没有注册入口'), '发布后台只能读取适配器提供的已验证工件，不得提供客户端工件注册或自报 verified 入口'],
  [platform.includes('/api/admin/v1/releases/deploy') && platform.includes('/api/admin/v1/releases/rollback')
    && platform.includes("commandBody('release-deploy', { artifactId, environment, expectedVersion, dryRun, reason })")
    && platform.includes("commandBody('release-rollback', { targetRunId, expectedVersion, dryRun, reason })")
    && adminPage.includes("hasPermission('releases.execute')") && adminPage.includes('@click="submitRelease(true)">dry-run')
    && adminPage.includes('>执行发布</button>') && adminPage.includes('>回滚 dry-run</button>') && adminPage.includes('>执行回滚</button>')
    && adminPage.includes("result.applied ? '发布已执行并写入审计' : '发布预演完成，未执行激活'")
    && adminPage.includes("result.applied ? '回滚已执行并写入审计' : '回滚预演完成，未执行激活'")
    && !adminPage.includes('adminApi.reviewApproval'), '发布与回滚必须验证权限并携带环境版本、幂等键、理由和dry-run；执行结果写入审计，前端不得恢复审批执行入口'],
  [platform.includes("releaseEnvironments: () => platformRequest<ReleaseEnvironment[]>('/api/admin/v1/releases/environments')") && adminPage.includes('运行态只读快照') && adminPage.includes('WebSocket 冒烟') && adminPage.includes('发布、失败与回滚记录'), '运行态必须来自显式只读适配器快照，并展示健康、WS 冒烟、失败和回滚记录'],
  [platform.includes('disabled?: boolean') && platform.includes('/status`, {') && adminPage.includes('账号变更立即执行并完整审计') && adminPage.includes('撤销会话'), '账号禁用/启用必须直接执行、记录版本审计，并提供旧令牌与 WebSocket 会话撤销入口'],
  [platform.includes("securityStatus: () => platformRequest<SecurityStatus>('/api/admin/v1/security/status')") && platform.includes('/api/admin/v1/security/audit-archives') && adminPage.includes('高风险审计') && adminPage.includes('恢复演练'), '后台必须展示安全告警、独立审计归档 dry-run/复核与恢复演练入口'],
  [platform.includes("mfaCapability = () => platformRequest<MfaCapability>('/api/auth/mfa/capability')") && profilePage.includes('v-if="mfa?.enrollmentEnabled"') && !profilePage.includes('不会收集或保存 MFA 密钥'), 'MFA 未开放时必须隐藏工程提示，只有服务端明确开放注册能力后才显示入口'],
  [adminPage.includes('服务器离线恢复') && adminPage.includes('仅服务器 CLI 可用')
    && adminPage.includes('后台操作不要求另一名管理员批准')
    && !platform.includes('bootstrapSecondApprover') && !platform.includes('/offline-bootstrap')
    && !adminPage.includes('type="password"'), '受控发布恢复只能保留服务器CLI离线边界，后台不得新增恢复凭据或第二审批人入口'],
  [platform.includes('export const tournamentApi') && platform.includes('/api/tournaments/import-legacy') && platform.includes('/matches/${encodeURIComponent(matchId)}/rulings'), '赛事中心必须通过服务端 API 完成赛事、旧数据导入与裁判写入'],
  [adminPage.includes("'tournaments'") && adminPage.includes('TournamentCenterPage')
    && adminPage.includes('admin-mode embedded') && adminPage.includes('♜ 赛事管理')
    && tournamentCenter.includes('canGloballyManage') && tournamentCenter.includes('canGloballyRule')
    && tournamentCenter.includes("props.adminMode ? '赛事管理' : '赛事中心'")
    && tournamentCenter.includes('!props.adminMode&&item.status')
    && tournamentCenter.includes('!props.adminMode&&person.accountId')
    && tournamentCenter.includes('!props.adminMode&&detail.status'), '后台赛事管理必须复用赛事详情与控制流程，但不得暴露玩家创建、报名、改牌库或退赛入口'],
  [tournamentCenter.includes('预览导入（dry-run）') && tournamentCenter.includes('确认导入') && tournamentCenter.includes('legacyPreview.value.previewHash') && !tournamentCenter.includes('localStorage.setItem'), '本机旧赛事只能显式预览并确认导入，不得继续作为 localStorage 权威状态写回'],
  [tournamentCenter.includes('organizerAccountId === accountId.value') && tournamentCenter.includes('person.accountId===accountId') && tournamentCenter.includes('主办者与裁判权限仅在当前赛事内生效') && !tournamentCenter.includes('待审批命令'), '赛事主办者、裁判临时身份、牌库范围与并发写入必须使用服务端账号 ID 和赛事版本，且不得进入账号角色审批'],
  [tournamentCenter.includes("tournamentApi.getByCode(item.code)") && tournamentCenter.includes('选择牌库并报名') && tournamentCenter.includes("link.searchParams.set('code', item.code)") && tournamentCenter.includes('瑞士排名快照') && tournamentCenter.includes('v-for="snapshotRound in standingSnapshots"') && l12Net.includes("type: 'enterTournamentMatch'") && l12Net.includes("type: 'spectateTournamentMatch'") && profilePage.includes("!route.query.redirect.startsWith('//')") && gamePage.includes('@click="returnToLobby">返回赛事/大厅'), '赛事稳定分享链接必须在登录后按码打开详情并直接选择账号牌库报名；玩家/工作人员须按配对身份进入或观战，每轮排名快照可查，且等待对手时也能退出专属房，不得恢复房间码输入'],
  [playerMat.includes("emit('cardAction', 'freeMove'") && playerMat.includes("unit?.cardId === 'S02-0510' && unit.tapped") && board.includes("mode.value === 'freeMove' ? 'move'"), '希波吕忒休整时必须提供独立的免费前后位移入口，并复用规则内移动命令'],
  [wsSmoke.includes("ws.send(JSON.stringify({ type: 'deploymentProbe' }))") && !wsSmoke.includes("wait(m => m.type === 'session')"), '发布烟雾测试必须先执行无状态 WebSocket 探针，不得恢复为认证前等待 session'],
  [wsServer.includes('"deploymentProbe" =>') && wsServer.includes('protocolVersion = 1') && wsServer.includes('authentication = "token"'), '服务端必须保留无需账号且不写运行数据的发布探针协议'],
  [l12Net.includes("message.type === 'recoveryComplete'") && l12Net.includes("message.type === 'pong'")
    && l12Net.includes('connectionGeneration') && l12Net.includes('lastCloseCode')
    && l12Net.includes('lastHeartbeatAt') && l12Net.includes('lastPongAt')
    && l12Net.includes("l12State.recoveryPhase = 'snapshot-acknowledged'")
    && gameReentry.includes("snapshot.recoveryPhase === 'snapshot-acknowledged'")
    && gameReentry.includes("route.path !== '/game'") && gameReentry.includes('loaded.afterEach(')
    && gameReentry.includes('snapshot.game?.matchId !== exitMatchId')
    && l12Net.includes('createGameReentryController(') && !app.includes("router.push('/game')"), '断线恢复必须通过单一协调器在快照确认后对账路由，尊重主动退出，并记录连接代次、心跳、Pong与关闭原因'],
  [globalBugFeedback.includes('captureBugClientDiagnostic(route.path)')
    && !globalBugFeedback.includes('navigator.userAgent')
    && platform.includes('clientDiagnostic?: BugClientConnectionDiagnostic')
    && adminPage.includes('item.clientVersion') && adminPage.includes('item.serverVersion')
    && adminPage.includes('item.engineVersion'), 'Bug反馈只能附带白名单连接诊断，后台必须分别展示客户端、服务端与规则引擎版本'],
  [cacheEnvironment.includes('D:\\GPT\\Legion12\\cache\\primary') && cacheEnvironment.includes('NUGET_PACKAGES') && cacheEnvironment.includes('npm_config_cache') && cacheEnvironment.includes('DOTNET_CLI_HOME') && cacheEnvironment.includes('COREPACK_HOME'), 'Windows 构建必须统一使用可覆盖的 D 盘缓存根目录'],
  [windowsVerify.includes('Initialize-L12BuildEnvironment.ps1') && windowsDeploy.includes('Initialize-L12BuildEnvironment.ps1') && windowsDeploy.includes('"-CacheRoot", $resolvedCacheRoot'), '完整验证和部署子进程必须共用同一缓存根目录'],
  [serverDeploy.includes('readonly public_host="legion-12.com"') && serverDeploy.includes('readonly public_base="https://${public_host}"') && serverDeploy.includes('"wss://${public_host}/ws"') && serverDeploy.includes('域名：${public_host}') && !serverDeploy.includes('wss://legion12.grand-umi.com/ws') && windowsDeploy.includes('[string]$Server = "root@legion-12.com"') && windowsDeploy.includes('发布成功：https://legion-12.com/') && windowsDeploy.includes('Resolve-L12ProductionEndpoint -RemoteServer $Server') && windowsDeploy.includes('Resolve-L12SshOptions') && deployTarget.includes('$script:L12ProductionAddress = "154.201.80.91"') && deployTarget.includes('"StrictHostKeyChecking=yes"') && deployTarget.includes('ssh-keygen -F $endpoint.TrustedHostKeyAlias') && deployTarget.includes('"HostName=$($endpoint.TrustedHostKeyAlias)"') && deployTarget.includes('"HostKeyAlias=$($endpoint.TrustedHostKeyAlias)"') && !windowsDeploy.includes('legion12.grand-umi.com') && bugQueue.includes('[string]$ApiBase = "https://legion-12.com"'), '主站、SSH、API 与发布后 HTTP/WS 探针必须统一使用 legion-12.com；连接仅允许新生产节点且同时固定地址与已核验 IP 主机密钥，缺少可信条目严格失败关闭（行为由 test-l12-deploy-behavior.ps1 覆盖）'],
  [serverDeploy.includes('backup_runtime') && serverDeploy.includes('runtime-before-${short_commit}-${timestamp}.tar.gz')
    && serverDeploy.includes('部署前运行数据快照：${runtime_backup}') && serverDeploy.includes('prune_runtime_backups')
    && serverDeploy.includes('new_service_launch_attempted=1') && serverDeploy.includes('stop_service_and_confirm')
    && serverDeploy.includes('deployment-blocked.txt')
    && serverDeploy.includes('new-service-launch-attempted; runtime preserved; manual reconciliation required'), '发布前必须备份；新服务尝试启动后失败须停服保留新数据、备份和阻断记录，禁止自动回灌旧快照（由部署失败路径行为测试覆盖）'],
]

const longBlockId = 'x'.repeat(80)
const blockIdFixtures = [
  { id: 'same', type: 'paragraph', text: '段落', marks: [], align: 'left' },
  { id: 'same', type: 'h2', text: '大标题', marks: [], align: 'center' },
  { id: '   ', type: 'h3', text: '小标题', marks: [], align: 'right' },
  { id: 'quote', type: 'quote', text: '引用内容', marks: [], align: 'left' },
  { id: 'bullet', type: 'bulletList', text: '项目一\n项目二', marks: [], align: 'left' },
  { id: 'ordered', type: 'orderedList', text: '第一项\n第二项', marks: [], align: 'justify' },
  { id: `${longBlockId}a`, type: 'image', mediaAssetId: 'media', alt: '插图', caption: '' },
  { id: `${longBlockId}b`, type: 'divider' },
]
const normalizedBlockIds = normalizeArticleBlockIds(blockIdFixtures, () => 'generated')
const normalizedIds = normalizedBlockIds.map(block => block.id)
const serializedBlockIds = JSON.parse(serializeArticleBody({ format: 'l12-blocks', version: 1, blocks: blockIdFixtures })).blocks.map(block => block.id)
contracts.push([
  normalizedBlockIds.map(block => block.type).join('|') === 'paragraph|h2|h3|quote|bulletList|orderedList|image|divider'
    && normalizedIds.every(id => id.trim() && id.length <= 80)
    && new Set(normalizedIds).size === normalizedIds.length
    && normalizedIds.includes('generated') && normalizedIds.includes('generated-2') && normalizedIds.includes('generated-3')
    && serializedBlockIds.every(id => id.trim() && id.length <= 80)
    && new Set(serializedBlockIds).size === serializedBlockIds.length
    && articleBlockModel.includes('normalizeArticleBlockIds(document.blocks)')
    && articleDocumentEditor.includes('normalizeArticleBlockIds(blocks.slice(0, 200))')
    && articleDocumentEditor.includes('element.dataset.blockId = block.id'),
  '文章正文每次序列化必须统一修复所有块类型的空白、超长和重复标识，生成过程不得再次碰撞，并将规范化标识回写连续编辑画布',
])

contracts.push([
  shell.includes("title: '卡牌效果'")
    && shell.includes("title: '对局与房间'")
    && shell.includes("title: '排位与维护'")
    && shell.includes("title: '界面与设置'")
    && ['陵墓构造体', '栖木猎鹰', '荷鲁斯', '信仰狂热者', '兰斯洛特', '月读', '托勒密十三世', '海伦', '黄金圣甲虫', '梅林']
      .every(cardName => shell.includes(cardName))
    && shell.includes('entry.sections')
    && shell.includes('class="update-section"'),
  '更新日志必须按卡牌效果、对局房间、排位维护及界面设置分组，并点名本批完成的卡效结果',
])

contracts.push([
  shell.includes("title: '卡牌结算、账号治理、对战界面与长局稳定性更新'")
    && shell.match(/version: releaseVersion/g)?.length === 1
    && shell.includes("title: '操作响应与连接稳定性'")
    && shell.includes("title: '快照、观战与回放'")
    && shell.includes('稳定请求标识')
    && shell.includes('增量对局快照')
    && shell.includes('最近128条'),
  '正式发布的玩家更新日志必须明确说明操作隔离、重连幂等、增量快照及近期日志上限',
])

const latestReleaseEntry = shell.slice(
  shell.indexOf("date: '2026-09-22'"),
  shell.indexOf("date: '2026-09-14'"),
)
const currentReleaseEntry = shell.slice(
  shell.indexOf("date: '2026-09-14'"),
  shell.indexOf("date: '2026-09-13'"),
)
const previousReleaseEntry = shell.slice(
  shell.indexOf("date: '2026-09-13'"),
  shell.indexOf("date: '2026-09-10'"),
)
const migratedReleaseEntry = shell.slice(
  shell.indexOf("date: '2026-09-10'"),
  shell.indexOf("date: '2026-09-09'"),
)
const internalReleaseTerms = ['后台', '管理员', '审计', '存储维护', '处置', '处罚']
contracts.push([
  latestReleaseEntry.includes('version: releaseVersion')
    && latestReleaseEntry.includes("title: '移动端横屏与对局操作'")
    && latestReleaseEntry.includes("title: '天灾与卡面呈现'")
    && latestReleaseEntry.includes("title: '卡牌效果与结算'")
    && latestReleaseEntry.includes("title: '规则中心与卡牌收藏'")
    && latestReleaseEntry.includes("title: '反击、试炼与战斗限制'")
    && latestReleaseEntry.includes("title: '账号、战绩、回放与个人页面'")
    && latestReleaseEntry.includes("title: '排位战绩与数据一致性'")
    && latestReleaseEntry.includes("title: '历史排位结果'")
    && ['确认抵挡／不抵挡', '确认支援／不支援', '没有额外触发效果的天灾公开', '诸神黄昏',
      '贝奥武夫', '尼托克丽丝', '梅林', '洛基', '猎杀时刻', '魔龙降世', '野外扎营',
      '山河社稷图', '观星', '法老王的庆典', '众神之乡', '无骨者伊瓦尔', '花魁的馈赠',
      '武运在天 铠甲在前', '柏拉图', '普罗米修斯', '符文之力', '特勒马科斯', '卡牌详情抽屉',
      '对手手牌数', '宫廷魔术师', '试炼军团', '7天内最近10场', '异画', '昵称修改',
      '个人战绩、主宰战绩、排行榜、胜率与先后手数据中同步剔除', '历史对局会以结算当时保存的结果为准']
      .every(detail => latestReleaseEntry.includes(detail))
    && internalReleaseTerms.every(term => !latestReleaseEntry.includes(term)),
  '最新玩家更新日志必须覆盖本期移动端、天灾卡面、牌库整理、卡效结算、规则中心、异画与改名功能，并排除后台和内部治理内容',
])
contracts.push([
  currentReleaseEntry.includes("version: '815771d6d204651076a9db77bc68b0dad52d276a'")
    && currentReleaseEntry.includes("title: '移动端对战页面'")
    && currentReleaseEntry.includes("title: '登场费用、牌库顶与持续效果'")
    && currentReleaseEntry.includes("title: '士气、符文与主动装备'")
    && currentReleaseEntry.includes("title: '军团主动、位移与时点'")
    && currentReleaseEntry.includes("title: '公开触发与发动取消'")
    && currentReleaseEntry.includes("title: '阵亡与击杀触发'")
    && currentReleaseEntry.includes("title: '多对象与连续结算'")
    && ['嬴政', '天照', '孙悟空', '山河社稷图', '普罗米修斯', '特勒马科斯', '野外扎营', '花魁的馈赠',
      '阿麦金', '西芙', '安卡神碑', '神剑格拉墨', '黄泉之门', '八尺琼勾玉', '匠神锻造炉',
      '莫瑞甘', '阿尔忒弥斯', '奈芙蒂斯', '服部半藏', '银臂努阿达', '无眠之夜', '荷鲁斯',
      '腐秽大地', '驱魔道士陆瑛', '安德华拉诺特', '玛格丽特', '圣杯', '安格斯·麦·奥格',
      '月读', '爱丽丝', '黑胡子蒂奇', '传奇的拉格纳', '奥拉夫二世', '雷神之锤', '赫拉克勒斯',
      '洛基', '孙武', '阿尔维达', '忒修斯', '尼托克丽丝', '血斧艾瑞克', '陵墓圣武士',
      '坂本龙马', '亚瑟王', '无名的渗透者', '墨子', '贝奥武夫', '布伦希尔德', '哈特谢普苏特',
      '井伊直虎', '埃涅阿斯', '金发哈拉尔', '圣女贞德', '吉原的花魁', '英灵殿', '土方岁三']
      .every(cardName => currentReleaseEntry.includes(cardName))
    && ['同时按可用宽度和高度等比缩放并居中', '完整收进一屏', '取消整次发动', '最多1张',
      '直接规则动作', '一次响应内的连续结算', '已结算、被无效、已跳过、未完成和已放弃',
      '重连、回放、日志和卡牌动效会保持同一实际结果']
      .every(detail => currentReleaseEntry.includes(detail))
    && internalReleaseTerms.every(term => !currentReleaseEntry.includes(term)),
  '当前玩家更新日志必须覆盖上一正式版本后的移动端一屏适配和全部玩家可见卡效批次，并排除后台和内部治理内容',
])
contracts.push([
  previousReleaseEntry.includes("version: '086f6f796d52bd7eefa09217da8e8bffe9e74e57'")
    && previousReleaseEntry.includes("title: '页面与对战操作'")
    && previousReleaseEntry.includes("title: '主动效果与费用判定'")
    && previousReleaseEntry.includes("title: '打出费用与响应判定'")
    && previousReleaseEntry.includes("title: '结算结果与响应顺序'")
    && previousReleaseEntry.includes("title: '反击战术与多段效果'")
    && previousReleaseEntry.includes("title: '对象重验与连续结算'")
    && previousReleaseEntry.includes("title: '跨回合与主动状态'")
    && previousReleaseEntry.includes("title: '诸神巅能力结算'")
    && previousReleaseEntry.includes("title: '登场对象、操作提示与卡图'")
    && previousReleaseEntry.includes("title: '排位结果与申诉'")
    && ['须佐之男', '山河社稷图', '草薙剑', '奥尔加', '众神之乡', '安卡神碑',
      '传奇的拉格纳', '无情者哈拉尔', '血斧艾瑞克', '齐格鲁德', '卡纽特大帝',
      '莫德雷德', '伊西斯', '步行者罗洛', '槲寄生符咒', '落穴陷阱', '孙悟空',
      '土方岁三', '阿麦金', '万物统御之戒', '自然馈赠', '杨戬专属', '哪吒专属']
      .every(cardName => previousReleaseEntry.includes(cardName))
    && ['主宰效果免耗', '傲慢之罪', '选择完成前不会先扣费', '冒号只分隔该能力自己的费用与效果',
      '再让登场费用-1', '之后才让军团离开手牌', '实际成为场上军团后', '消耗3符文把费用减至0',
      '一次选择最多2个对象', '职介和试炼值不属于特征', '通用特征会随持有者改为当前阵营特征', '最低显示为0',
      '真正结算后再播放对应分支', '发动声明不会重复播放', '重连和回放后仍保持一致']
      .every(detail => previousReleaseEntry.includes(detail))
    && ['白起', '绝对防御', '拼死反抗', '摄政皇权', '暗度陈仓', '不朽之礼', '切腹仪式',
      '神妙行军', '地主的胁迫', '乾坤·阴', '特洛伊木马', '雷神索尔', '探寻天空之城',
      '克利奥帕特拉七世', '希波吕忒', '神农鼎', '诸神巅', '嬴政', '天照', '梅林']
      .every(cardName => previousReleaseEntry.includes(cardName))
    && ['已结算、被无效、已跳过、未完成、已放弃', '声明时已经支付的费用不会返还',
      '后段可以分别显示已结算、未完成、已跳过或已放弃', '先弃置再抽牌',
      '多次伤害分配会逐个重新核对对象', '孙悟空》更新主卡图']
      .every(detail => previousReleaseEntry.includes(detail))
    && ['历史异常对局被确认无效时', '保护后续正常结算', '当前七曜值、定级进度和隐藏分保持不变']
      .every(detail => previousReleaseEntry.includes(detail))
    && internalReleaseTerms.every(term => !previousReleaseEntry.includes(term)),
  '上一期玩家更新日志必须固定到对应正式版本，继续覆盖主动效果、费用边界、支付取消与场上响应修改',
])
contracts.push([
  migratedReleaseEntry.includes("version: '5c2c7d0a7771f7d87af22c456f2b73795953c05a'")
    && migratedReleaseEntry.includes("title: '卡牌效果与对战规则'")
    && migratedReleaseEntry.includes("title: '对战页面与恢复'")
    && migratedReleaseEntry.includes("title: '牌库、排行与大厅'")
    && ['雷神之锤', '梅杰德', '孙悟空', '安格斯·麦·奥格']
      .every(cardName => migratedReleaseEntry.includes(cardName))
    && internalReleaseTerms.every(term => !migratedReleaseEntry.includes(term)),
  '误发到资讯的5c2c7d0版本必须迁回原更新日志弹框，并只保留玩家可感知内容',
])

const titleRules = read('../src/l12/site/RankedMasterTitleRulesModal.vue')
const friendNotifications = read('../src/l12/site/FriendRequestNotifications.vue')
const setupDecisionClock = read('../src/l12/game/SetupDecisionClock.vue')
const catalogDetails = read('../src/l12/CatalogCardDetails.vue')
contracts.push(
  [platform.includes('mustChangeUsername?: boolean')
    && platform.includes("'/api/auth/change-username'")
    && router.includes("reason: 'username-change-required'")
    && profilePage.includes('class="username-change-gate"')
    && profilePage.includes('aria-modal="true"')
    && profilePage.includes('2–11 个可见字符')
    && profilePage.includes('退出账号') && profilePage.includes('确认修改')
    && adminPage.includes('待修改用户名'),
    '违禁或超长存量用户名必须脱敏并在不可关闭的界面中完成改名，完成前阻断其他账号功能'],
  [rankings.includes('>最强称号规则</button>') && profilePage.includes('>最强称号规则</button>')
    && profilePage.includes('class="title-manager-heading"')
    && profilePage.includes('.title-manager-heading{display:flex;grid-column:1/-1;align-items:center;justify-content:space-between;gap:10px}')
    && profilePage.includes('.title-manager-heading button{flex:0 0 auto;margin-left:auto}')
    && rankings.includes('class="ranking-search"') && titleRules.includes('近30日') && titleRules.includes('40场')
    && titleRules.includes('20场') && titleRules.includes('10名') && titleRules.includes('UTC+8')
    && titleRules.includes('15场') && titleRules.includes('镜像局'),
    '排行榜和称号管理必须提供同一完整最强称号说明，搜索框独立收窄'],
  [sandboxPicker.includes('native-orientation') && sandboxPicker.includes('CatalogCardDetails')
    && sandboxPicker.includes('max-width:760px') && catalogDetails.includes('effect'),
    'GM选牌必须保留横卡自然方向、可查看详情且窄桌面筛选不越界'],
  [app.includes('FriendRequestNotifications') && friendNotifications.includes("resolve('block')")
    && friendNotifications.includes("resolve('reject')") && friendNotifications.includes("resolve('accept')")
    && friendNotifications.includes('playL12FriendRequestSound') && friendNotifications.includes('expected !== generation'),
    '全局好友申请必须具备提示音、通过拒绝屏蔽及账号代次隔离'],
  [platform.includes('interface RankedTimeControlConfig') && platform.includes('DEFAULT_RANKED_TIME_CONTROL')
    && adminOperations.includes('data-ui-contract="ranked-time-control-config"')
    && adminOperations.includes('disasterDecisionSeconds') && adminOperations.includes('mulliganDecisionSeconds')
    && setupDecisionClock.includes("['DisasterPreparation', 'Mulligan'].includes(props.phase)")
    && setupDecisionClock.includes('operationRemainingMs') && setupDecisionClock.includes('receivedAtMs')
    && prompt.includes(':phase="game.phase"') && playerTurnClock.includes("props.phase")
    && gameActions.includes('game.firstPlayer === me.playerIndex')
    && !setupDecisionClock.includes('60_000') && !playerTurnClock.includes('60_000')
    && lobby.includes('ranked.config.timeControl.disasterDecisionSeconds')
    && lobby.includes('超时保留原手牌') && !lobby.includes('总操作时间25分钟'),
    '排位调度与天灾步骤必须读取服务端冻结时限，后台可配置且玩家规则说明同步，不得硬编码一分钟'],
  [playerMat.includes('inheritAttrs: false') && playerMat.includes('v-bind="$attrs"')
    && board.includes('--l12-board-copy') && board.includes('--l12-board-meta') && board.includes('--l12-board-micro') && board.includes('flex-wrap:wrap'),
    '多根PlayerMat必须显式向场面根节点传递外框样式，缩放后的正文、辅助与微标签层级不得破坏布局'],
  [board.includes("import CardDetailContent from '../CardDetailContent.vue'")
    && (board.match(/<CardDetailContent :card="focusDetailCard" :show-catalog-only="false" \/>/g) ?? []).length === 2
    && board.includes('const focusDetailCard = computed<DeckCard | null>')
    && board.includes('class="grand-panel card-inspector archive-detail"')
    && board.includes('class="archive-detail mobile-card-detail-body"')
    && !board.includes('class="inspector-effect') && !board.includes('class="mobile-inspector-effect')
    && cardDetailContent.includes('<template v-if="showCatalogOnly">'),
    '桌面、移动端对战与回放的选中卡牌必须直接复用图鉴详情组件，并统一隐藏收录产品和刊物记录，不得保留平行旧模板'],
  [battleViewportLayout.includes('const SITE_AND_OVERFLOW_RESERVE = 124')
    && battleViewportLayout.includes('viewportHeight - SITE_AND_OVERFLOW_RESERVE')
    && visualLayoutCheck.includes("throw new Error('Hand leaves viewport at '")
    && visualLayoutCheck.includes("throw new Error('Utility dock leaves viewport at '"),
    '16:9棋盘缩放必须为手牌扇面和左下工具保留绘制边界，视觉验收须阻止二者离开视口'],
  [battleLogViewModel.includes("'turn-start'") && battleLogViewModel.includes("'prompt-resolved'") && battleLogViewModel.includes('PLAYER_LOG_HIDDEN_TYPES')
    && battleLog.includes('emit(') && battleDock.includes('L12Settings') === false
    && board.includes('BattleUtilityDock'),
    '对局日志保留回合分组并隐藏选择流程噪声，选中卡牌下方的独立工具坞通过现有设置入口工作'],
  [battleDock.includes('grid-template-columns:repeat(3,1fr)')
    && battleDock.includes('title="设置" aria-label="打开对局设置"')
    && battleDock.includes('title="好友" aria-label="打开好友功能"')
    && battleDock.includes('title="对局工具" aria-label="打开对局工具"')
    && battleDock.includes('<FriendsPage />') && battleDock.includes('friendApi.block(accountId)')
    && battleDock.includes('输入所出现的Bug给对手申请平局')
    && battleDock.includes('governance.value?.canRequestDraw') && battleDock.includes('governance.value?.drawUnavailableReason')
    && battleDock.includes('drawRequest.value?.viewerCanRespond') && battleDock.includes('每场对局双方合计仅可发起一次')
    && battleDock.includes("new CustomEvent('l12-open-bug-feedback')"),
    '选中卡牌下方工具坞必须是连续三等宽纯图标入口，并复用设置、好友、普通Bug反馈与好友屏蔽能力'],
  [matchGovernance.includes("type: 'requestMatchDraw'")
    && matchGovernance.includes("type: 'resolveMatchDraw'")
    && matchGovernance.includes("type: 'reportOpponent'")
    && matchGovernance.includes('/api/admin/match-governance/draw-requests')
    && matchGovernance.includes('/api/admin/match-governance/player-reports')
    && adminMatchGovernance.includes('平局申请与玩家举报独立于普通 Bug')
    && adminPage.includes("hasPermission('admin.match-governance.read')")
    && gamePage.includes("event.type === 'game-draw'")
    && gamePage.includes("agreedDraw ? '平局' : '对局无效'"),
    '平局申请和玩家举报必须使用独立治理协议、后台与RBAC；同意平局不得沿用无效局文案'],
  [responsiveTypeCheck.includes('{ width: 1920, height: 1080 }') && responsiveTypeCheck.includes('{ width: 1440, height: 810 }')
    && responsiveTypeCheck.includes('{ width: 1280, height: 720 }') && responsiveTypeCheck.includes('{ width: 760, height: 900 }')
    && responsiveTypeCheck.includes('{ width: 390, height: 844 }') && responsiveTypeCheck.includes('[data-card-detail-context="builder"] .archive-effect .l12-effect-body')
    && responsiveTypeCheck.includes('selected-card effect prose must wrap without horizontal overflow'),
    '响应式字号专项必须覆盖三档16:9桌面、760与390窄宽，并实际选中卡牌验证正文和标签而非只检查空详情'],
  [battleViewportLayout.includes('Math.min(1, availableWidth / stageWidth, availableHeight / stageHeight)')
    && battleViewportLayout.includes('scale: options.mobile ? 1 : desktopScale')
    && !battleViewportLayout.includes('Math.max(.7')
    && mobileViewportStyle.includes('.board-viewport.compact-viewport')
    && mobileViewportStyle.includes('overflow: hidden !important')
    && mobileViewportStyle.includes('transform-origin: center')
    && mobileViewportCheck.includes('{width:375,height:667,mobile:true,rotated:true}')
    && mobileViewportCheck.includes('{width:740,height:360,mobile:true,rotated:false}')
    && mobileViewportCheck.includes('{width:1366,height:768,mobile:false,rotated:false}')
    && mobileViewportCheck.includes("assert.equal(result.overflowY,'hidden')")
    && mobileViewportCheck.includes('result.stage.bottom<=result.board.bottom+1'),
    '移动端自动横屏与实际横屏必须同时按宽高完整缩放棋盘，禁止恢复最低缩放或双轴滚动，并覆盖短屏边界'],
  [board.includes("filter(id => id !== 'skip' && id !== 'cancel')")
    && board.includes('function cancelResourcePayment()')
    && board.includes('resourceSelectionPrompt.validChoices.includes(\'cancel\')')
    && board.includes('@click="cancelResourcePayment">{{ resourceSelectionPrompt.data?.cancel ?? \'取消打出\' }}</button>')
    && prompt.includes("'decline', 'cancel'")
    && prompt.includes("id === 'cancel' && p.data?.allowCancel === 'true'"),
    '打出前支付取消必须复用既有支付控制条与Prompt底部次级按钮，且取消值不得混入资源或卡牌选择'],
  [adminAlternateArts.includes('查看已登记异画')
    && adminAlternateArts.includes('adminApi.searchAlternateArts')
    && adminAlternateArts.includes('registryFilters.baseCard')
    && adminAlternateArts.includes('pageSize: registry.value.pageSize')
    && !/async function load\(\)[\s\S]*?Promise\.all\(\[\s*adminApi\.alternateArts\(/.test(adminAlternateArts),
    '异画后台初始加载不得拉取全量登记表，必须通过弹框按名称、编号、绑定卡查询并分页'],
  [shell.includes('alternateArtApi.notifications()')
    && shell.includes('alternateArtApi.acknowledgeNotification')
    && shell.includes('<h2 id="alternate-art-notification-title">获得异画！</h2>')
    && !shell.includes('<h2 id="alternate-art-notification-title">获得异画使用权</h2>')
    && shell.includes('“{{ currentAlternateArtNotification.reason }}”收到“{{ currentAlternateArtNotification.displayName }}·{{ currentAlternateArtNotification.artCode }}”的使用权')
    && profilePage.includes('<h2>我的异画</h2>') && profilePage.includes('获得时间：')
    && !profilePage.includes('<h2>数据与工具</h2>'),
    '异画到账标题必须为获得异画；权益必须持久提示，个人页展示名称、编号、对应原画和获得时间，并完全移除数据与工具板块'],
  [deckEditor.includes('alternateArtCopies') && deckEditor.includes('addAppearance(entry)')
    && deckEditor.includes('removeAppearance(entry)') && deckEditor.includes('原画与异画合计最多')
    && deckEditor.includes("entry.art?.artCode || entry.card.number"),
    '牌库编辑器必须把已拥有异画与原画平行展示，以同一规则卡编号共享数量上限并保存逐副本卡图'],
)

const failures = contracts.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  console.error(`UI 契约检查失败：\n- ${failures.join('\n- ')}`)
  process.exit(1)
}

console.log(`UI 契约检查通过（${contracts.length} 项）`)
