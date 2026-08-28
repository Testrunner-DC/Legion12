import { existsSync, readFileSync } from 'node:fs'

const read = path => {
  const url = new URL(path, import.meta.url)
  return existsSync(url) ? readFileSync(url, 'utf8').replace(/\r\n?/g, '\n') : ''
}

const s1 = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/cards.s1.json'))
const s2 = JSON.parse(read('../../服务端WebSocket/TwelveLegions/Data/cards.s2.json'))
const cards = [...s1, ...s2]
const ids = new Set(cards.map(card => card.id))
const cardAssets = read('../src/l12/cardAssets.ts')
const cardImage = read('../src/l12/CardImage.vue')
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

const contracts = [
  [s1.length === 133 && s2.length === 115 && cards.length === 248 && ids.size === 248, 'S01/S02 必须保持 133+115=248 张唯一卡号'],
  [cardAssets.includes('card-assets.manifest.json') && cardAssets.includes('resolveCardAsset') && cardAssets.includes('legacyUrl'), '必须提供按逻辑卡号解析且保留旧 imageUrl 的统一资源解析器'],
  [cardAssets.includes('cdnBaseUrl') && cardAssets.includes('sameOrigin') && cardAssets.includes('placeholder'), '资源候选必须按 CDN、同源优化资源、旧 imageUrl、占位单向降级'],
  [cardImage.includes('<picture') && cardAssets.includes('detailAvif') && cardAssets.includes('thumbWebp') && cardAssets.includes('boardWebp'), '公共卡图组件必须支持 240/480/960 WebP 与详情 AVIF'],
  [cardImage.includes(":loading=\"eager ? 'eager' : 'lazy'\"") && cardImage.includes('decoding="async"') && cardImage.includes('@error'), '公共卡图组件必须懒加载、异步解码并处理失败降级'],
  [cardImage.includes('fallbackCardAsset(props.cardId, props.legacyUrl, props.intent)'), 'manifest 返回前必须立即使用旧 imageUrl，避免首屏占位闪烁'],
  [consumers.every(path => read(path).includes('CardImage')), '全部 L12 卡图消费入口必须迁移到公共 CardImage'],
  [styledCardImageConsumers.every(path => read(path).includes('.l12-card-image')), '迁移后的 scoped/global 尺寸、横卡旋转与状态滤镜必须命中 CardImage 根节点'],
  [!read('../src/l12/L12DeckEditor.vue').includes('<span v-else>XII</span>'), '牌库编辑器迁移 CardImage 后不得残留失去相邻 v-if 的旧图片兜底分支'],
  [remainingDirectImages.every(tag => tag.includes('masterProfileUrl') || tag.includes('roundCardUrl')), '普通卡面不得绕过 CardImage；仅允许官方方形头像与圆形裁切专用资源保留原始 img'],
  [read('../src/l12/site/deckShare.ts').includes('resolveCardAssetUrls') && read('../src/l12/site/deckShare.ts').includes('for (const url of candidates)'), 'Canvas 牌库图必须逐个尝试 resolver 候选且单图失败可回落'],
  [serviceWorker.includes("caches.delete('l12-images-v1')") && !serviceWorker.includes("request.destination !== 'image'"), '旧广域图片 Service Worker 必须退役并只清理自身缓存'],
  [generator.includes("baseUrl = (args.get('--base-url') || '/card-assets'") && generator.includes('completeCatalog.length !== 248'), '生成器必须默认同源内容寻址路径并拒绝非 248 张目录'],
  [windowsDeploy.includes("PSObject.Properties['cardAssetsHash']") && windowsDeploy.includes("PSObject.Properties['cardAssetsArchive']") && windowsDeploy.includes("PSObject.Properties['cardAssetsSha256']"), '旧发布清单缺失优化卡图字段时必须在 StrictMode 下安全降级'],
  [windowsDeploy.includes('cardAssetsHash') && serverDeploy.includes('static_card_assets_dir') && serverDeploy.includes('validate_card_assets_tree') && serverDeploy.includes('manifest.cardCount !== 248'), '发布流程必须独立校验并复用完整的内容寻址优化卡图包'],
  [serverDeploy.includes('mv "$stage_card_assets_dir" "$card_assets_target"') && serverDeploy.includes('dist/card-assets') && serverDeploy.includes('nginx -T'), '服务端必须在验证完成后原子发布优化资产，并仅在 Nginx 缓存片段已接入时切换'],
  [nginxCache.includes('max-age=31536000') && nginxCache.includes('immutable') && nginxCache.includes('card-assets.manifest.json') && nginxCache.includes('max-age=300'), 'Nginx 必须区分哈希二进制一年缓存与 manifest 五分钟缓存'],
  [(nginxCache.match(/root \/opt\/legion12-test\/opcgpro-vue\/dist;/g) ?? []).length === 4, 'Nginx 的四类卡图 location 必须显式绑定当前发布目录，不能继承默认站点根目录'],
  [nginxCache.includes('location ~ "^/card-assets/') && nginxCache.includes('\\.(?:webp|avif)$" {'), 'Nginx 内容寻址正则必须整体加引号，避免花括号量词被解析为配置块'],
]

const failures = contracts.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  console.error(`L12 卡图架构契约失败：\n- ${failures.join('\n- ')}`)
  process.exit(1)
}

console.log(`L12 卡图架构契约通过（${contracts.length} 项，${cards.length} 张卡）`)
