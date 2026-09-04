import { existsSync, readFileSync } from 'node:fs'

const read = path => {
  const url = new URL(path, import.meta.url)
  return existsSync(url) ? readFileSync(url, 'utf8').replace(/\r\n?/g, '\n') : ''
}

const s1 = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/cards.s1.json'))
const s2 = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/cards.s2.json'))
const st = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/cards.st.json'))
const archiveAssets = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/card-archive-assets.json'))
const productInclusions = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/card-product-inclusions.json'))
const cards = [...s1, ...s2, ...st]
const ids = new Set(cards.map(card => card.id))
const cardAssets = read('../src/l12/cardAssets.ts')
const cardImage = read('../src/l12/CardImage.vue')
const cardPresentation = read('../src/l12/cardPresentation.ts')
const specialAssets = read('../src/l12/specialAssets.ts')
const gameBoard = read('../src/l12/game/GameBoard.vue')
const serviceWorker = read('../public/sw.js')
const generator = read('./build-l12-card-cdn.mjs')
const windowsDeploy = read('../../ops/windows/deploy-l12.ps1')
const serverDeploy = read('../../ops/server/deploy-l12-release.sh')
const nginxCache = read('../../ops/server/nginx-l12-card-assets.conf')

const consumers = [
  '../src/l12/CardArchive.vue',
  '../src/l12/L12DeckEditor.vue',
  '../src/l12/CardTile.vue',
  '../src/l12/game/GameBoard.vue',
  '../src/l12/game/PlayerMat.vue',
  '../src/l12/game/PromptOverlay.vue',
  '../src/l12/game/GmPanel.vue',
  '../src/l12/game/SandboxCardPicker.vue',
  '../src/l12/game/MasterOverlay.vue',
  '../src/l12/site/AdminPage.vue',
  '../src/l12/site/DeckLibraryPage.vue',
]
const consumerSource = consumers.map(read).join('\n')
const remainingDirectImages = consumerSource.match(/<img[^>]*(?:imageUrl|masterImageUrl|imageFor)[^>]*>/g) ?? []
const styledCardImageConsumers = [
  '../src/style.css',
  '../src/l12/L12DeckEditor.vue',
  '../src/l12/game/GameBoard.vue',
  '../src/l12/game/GmPanel.vue',
  '../src/l12/game/MasterOverlay.vue',
  '../src/l12/game/PlayerMat.vue',
  '../src/l12/game/PromptOverlay.vue',
  '../src/l12/game/SandboxCardPicker.vue',
  '../src/l12/site/AdminPage.vue',
  '../src/l12/site/DeckLibraryPage.vue',
]
const disasterRoundIds = [
  'S01-DS01', 'S01-DS02', 'S01-DS03', 'S01-DS04', 'S01-DS05',
  'S01-DS06', 'S01-DS07', 'S01-DS08', 'S01-DS09', 'S01-DS10',
  'S02-DS01', 'S02-DS02', 'S02-DS03', 'S02-DS04', 'S02-DS05', 'S02-DS06',
  'ST-DS01', 'ST-DS02', 'ST-DS03',
]
const starterMasterProfileIds = ['ST01-M1', 'ST02-M1', 'ST03-M1', 'ST04-M1', 'ST05-M1', 'ST06-M1']

const contracts = [
  [s1.length === 133 && s2.length === 115 && st.length === 76 && cards.length === 324 && ids.size === 324, 'S01/S02/ST 必须保持 133+115+76=324 张唯一卡号'],
  [cardAssets.includes('card-assets.manifest.json') && cardAssets.includes('resolveCardAsset') && cardAssets.includes('legacyUrl'), '必须提供按逻辑卡号解析且保留旧 imageUrl 的统一资源解析器'],
  [cardAssets.includes('missingEntryRefreshAfter') && cardAssets.includes('loadCardAssetManifest(true)') && cardAssets.includes("cache: force ? 'reload' : 'no-cache'") && cardAssets.includes('if (manifestPromise)'), '已加载旧清单但缺少新卡号时必须共享一次强制刷新，避免旧页面永久显示占位图或并发重复请求'],
  [cardAssets.includes('explicitCdnBaseUrl') && cardAssets.includes('manifestCdnBaseUrl') && cardAssets.indexOf("sourceFor('sameOrigin'") < cardAssets.indexOf("!explicitCdnBaseUrl && manifestCdnBaseUrl") && cardAssets.includes('placeholder'), '未显式启用 CDN 时必须优先使用同源优化资源，清单 CDN 仅作后备'],
  [cardImage.includes('<picture') && cardAssets.includes('detailAvif') && cardAssets.includes('thumbWebp') && cardAssets.includes('boardWebp'), '公共卡图组件必须支持 240/480/960 WebP 与详情 AVIF'],
  [cardImage.includes("props.intent === 'thumb' && resolved.value.orientation === 'landscape'") && cardImage.includes("'landscape-thumbnail-image': landscapeThumbnail") && cardImage.includes(":data-orientation=\"resolved.orientation || 'unknown'\"") && cardImage.includes('rotate(90deg)'), '横版资源必须由公共卡图组件按清单方向仅在缩略图顺时针旋转，详情保持原始横向'],
  [[['ST06-S1', 'trial'], ['ST-DS01', 'destruction'], ['ST-DS02', 'destruction'], ['ST-DS03', 'destruction']].every(([id, type]) => st.some(card => card.id === id && card.cardType === type)) && cardPresentation.includes("'destruction'") && cardPresentation.includes("'trial'"), '四张 ST 横版卡必须进入统一横卡类型规则'],
  [cardImage.includes(":loading=\"eager ? 'eager' : 'lazy'\"") && cardImage.includes('decoding="async"') && cardImage.includes('@error'), '公共卡图组件必须懒加载、异步解码并处理失败降级'],
  [cardImage.includes('failedUrl') && cardImage.includes('activeUrls.includes(failedUrl)') && !cardImage.includes('type="image/webp" :srcset="imageUrl"'), '失败降级必须忽略旧图片节点的迟到事件，且不得用重复 WebP source 跳过同源候选'],
  [cardAssets.includes('peekCardAsset') && cardImage.includes('resolutionComplete') && cardImage.includes('v-if="imageReady"') && cardImage.includes('l12-card-image__resolving'), 'manifest 已缓存时必须同步使用真实卡图；未解析时只保留稳定暗色框，不得先闪现 XII 占位图'],
  [consumers.every(path => read(path).includes('CardImage')), '全部 L12 卡图消费入口必须迁移到公共 CardImage'],
  [styledCardImageConsumers.every(path => read(path).includes('.l12-card-image')), '迁移后的 scoped/global 尺寸、横卡旋转与状态滤镜必须命中 CardImage 根节点'],
  [!read('../src/l12/L12DeckEditor.vue').includes('<span v-else>XII</span>'), '牌库编辑器迁移 CardImage 后不得残留失去相邻 v-if 的旧图片兜底分支'],
  [remainingDirectImages.every(tag => tag.includes('masterProfileUrl') || tag.includes('roundCardUrl') || tag.includes('disasterRoundUrl')), '普通卡面不得绕过 CardImage；仅允许官方方形头像与圆形裁切专用资源保留原始 img'],
  [disasterRoundIds.every(id => specialAssets.includes(`'${id}': '${id}.png'`) && existsSync(new URL(`../public/assets/l12/special/round/${id}.png`, import.meta.url))), '全19张S01/S02/ST天灾圆形卡图必须有独立映射与本地资源'],
  [starterMasterProfileIds.every(id => existsSync(new URL(`../public/assets/l12/special/master/${id}.png`, import.meta.url))), '六张ST初始主宰必须接入共用Profile资源目录'],
  [specialAssets.includes("masterProfileAssetRevision = '20260903-2'") && specialAssets.includes('.png?v=${masterProfileAssetRevision}'), '主宰Profile必须使用显式资源版本，避免新物料被旧404缓存阻断'],
  [gameBoard.includes('disasterRoundUrl(card.cardId, card.imageUrl)') && gameBoard.includes('destructionRoundBackUrl') && gameBoard.includes('intent="detail"'), '本局天灾圆形序列必须使用专用圆图，未知卡使用圆形卡背，详情仍使用高清完整卡面'],
  [read('../src/l12/site/deckShare.ts').includes('resolveCardAssetUrls') && read('../src/l12/site/deckShare.ts').includes('for (const url of candidates)'), 'Canvas 牌库图必须逐个尝试 resolver 候选且单图失败可回落'],
  [serviceWorker.includes("caches.delete('l12-images-v1')") && !serviceWorker.includes("request.destination !== 'image'"), '旧广域图片 Service Worker 必须退役并只清理自身缓存'],
  [generator.includes("baseUrl = (args.get('--base-url') || '/card-assets'") && generator.includes('expectedPlayableCardCount = 324') && generator.includes('expectedPresentationCardCount = 29'), '生成器必须默认同源内容寻址路径并严格区分324张可玩卡与29张展示版本'],
  [productInclusions.cards?.length === 349 && new Set(productInclusions.cards.map(card => card.cardId)).size === 349 && productInclusions.products?.length === 13, '收录产品目录必须保持349个唯一实体编号和13项产品'],
  [archiveAssets.cards?.length === 29 && archiveAssets.cards.every(card => (/[a-z]$/.test(card.id) || /C1A$/.test(card.id)) && ids.has(card.baseCardId)), '25张预组复刻与4张既有士气展示版本必须指向现有可玩规则基底'],
  [windowsDeploy.includes("PSObject.Properties['cardAssetsHash']") && windowsDeploy.includes("PSObject.Properties['cardAssetsArchive']") && windowsDeploy.includes("PSObject.Properties['cardAssetsSha256']"), '旧发布清单缺失优化卡图字段时必须在 StrictMode 下安全降级'],
  [windowsDeploy.includes('cardAssetsHash') && serverDeploy.includes('static_card_assets_dir') && serverDeploy.includes('validate_card_assets_tree') && serverDeploy.includes('manifest.cardCount !== 353'), '发布流程必须独立校验并复用完整的内容寻址优化卡图包'],
  [serverDeploy.includes('mv "$stage_card_assets_dir" "$card_assets_target"') && serverDeploy.includes('dist/card-assets') && serverDeploy.includes('nginx -T'), '服务端必须在验证完成后原子发布优化资产，并仅在 Nginx 缓存片段已接入时切换'],
  [nginxCache.includes('max-age=31536000') && nginxCache.includes('immutable') && nginxCache.includes('card-assets.manifest.json') && nginxCache.includes('max-age=300'), 'Nginx 必须区分哈希二进制一年缓存与 manifest 五分钟缓存'],
  [nginxCache.includes('(?:S|ST)[0-9]{2}-[A-Za-z0-9]+'), 'Nginx 内容寻址路径必须同时覆盖 S01/S02 与 ST01-ST06 卡号，禁止新产品卡图落入 no-store 兜底'],
  [(nginxCache.match(/root \/opt\/legion12-test\/opcgpro-vue\/dist;/g) ?? []).length === 4, 'Nginx 的四类卡图 location 必须显式绑定当前发布目录，不能继承默认站点根目录'],
  [nginxCache.includes('location ~ "^/card-assets/') && nginxCache.includes('\\.(?:webp|avif)$" {'), 'Nginx 内容寻址正则必须整体加引号，避免花括号量词被解析为配置块'],
]

const failures = contracts.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  console.error(`L12 卡图架构契约失败：\n- ${failures.join('\n- ')}`)
  process.exit(1)
}

console.log(`L12 卡图架构契约通过（${contracts.length} 项，${cards.length} 张卡）`)
