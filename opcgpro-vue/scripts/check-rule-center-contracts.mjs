import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const frontendRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const repoRoot = resolve(frontendRoot, '..')
const readFrontend = path => readFileSync(join(frontendRoot, path), 'utf8').replace(/\r\n/g, '\n')
const readRepo = path => readFileSync(join(repoRoot, path), 'utf8').replace(/\r\n/g, '\n')
const assert = (condition, message) => { if (!condition) throw new Error(`Rule center contract failed: ${message}`) }

const officialRules = readFrontend('src/l12/data/officialRules.ts')
const ruleRows = [...officialRules.matchAll(/^\s*\{"page":/gm)]
const blankPages = [...officialRules.matchAll(/^\s*\{"page":\s*""/gm)]
assert(ruleRows.length === 123, `expected 123 official rule blocks, found ${ruleRows.length}`)
assert(blankPages.length === 98, `expected 98 blank-page official rule blocks, found ${blankPages.length}`)

const data = readFrontend('src/l12/data/ruleCenterData.ts')
assert(data.includes("return `core-rule-${String(index + 1).padStart(3, '0')}`"), 'core blocks need stable generated IDs')
assert(data.includes("typeof row.page === 'string'") && !data.includes('text(row.page, 20)'), 'blank page values must stay compatible')
assert(data.includes('RULE_TOPIC_DEFINITIONS') && data.includes('schemaVersion: 2'), 'schema v2 fixed topics must remain explicit')
const ordering = readFrontend('src/l12/site/ruleRulingOrdering.ts')
for (const token of ['compareCardNumbers', 'numeric: true', 'rulingCardSortKey', 'compareCardRulingsByNumber', 'compareRulingsByScoreAndDate'])
  assert(ordering.includes(token), `ruling ordering helper missing ${token}`)

const player = readFrontend('src/l12/site/RuleCenterPage.vue')
assert((player.match(/getPublicContentBatch\(/g) ?? []).length >= 2, 'initial and resource refresh paths must use public batch reads')
assert(!player.includes("getPublicContent('rules."), 'rule center must not fan out single-content requests')
assert(!player.includes('setInterval('), 'rule center must not poll')
for (const token of ['useRoute()', 'useRouter()', 'l12-resource-rulesContent', 'nextRuleTransitionAt', 'loadDynamicContent', 'selectedTopics', 'selectedCategory', 'selectedProduct'])
  assert(player.includes(token), `player rule center missing ${token}`)
assert(player.includes("getPublicContentBatch(['rules.notice', 'rules.center', 'rules.rulings'])"), 'first content read must be one three-key batch')
const initialLoad = player.slice(player.indexOf('async function loadDynamicContent()'), player.indexOf('async function ensureCardCatalog()'))
assert((initialLoad.match(/getPublicContentBatch\(/g) ?? []).length === 1
  && (initialLoad.match(/getEffectiveOperationsPolicy\(/g) ?? []).length === 1
  && !initialLoad.includes('loadDeckCatalog('), 'first render must stay within two rule-center requests')
for (const token of ['contentError', 'loadDynamicContent">', 'router.replace', 'router.push', 'visibilitychange'])
  assert(player.includes(token), `retry/navigation contract missing ${token}`)
for (const token of ['规则资料首页', '常见问题', '单卡问答', '从属分类', '按产品系列浏览', '全部收起', '全部展开', 'tabForEntry'])
  assert(player.includes(token), `reference information architecture missing ${token}`)
for (const tab of ['core', 'quick-start', 'terms', 'construction', 'tournament', 'versions'])
  assert(player.includes(`id: '${tab}'`), `rule material home is missing ${tab}`)
assert(!player.includes('printRules') && !player.includes('打印 / 保存 PDF') && !player.includes('@media print'),
  'print/PDF controls and print-only styles must stay removed')
for (const token of ['CatalogCardDetails', 'CardImage', 'rulingHeading(item)', '待补关联·裁定', 'cardProductsForIds(item.cardIds)'])
  assert(player.includes(token), `card ruling presentation missing ${token}`)
for (const token of ['cardArchiveProducts', 'canonicalProductRanks', 'compareCardNumbers(right, left)', 'visibleProductOptions', 'hiddenProductCount', '展开更多产品', 'activeFaqFilters', '清除筛选'])
  assert(player.includes(token), `dense product browsing missing ${token}`)
assert(player.includes('width:min(100%,1680px)'), 'rule center content must remain bounded on ultra-wide screens')
assert(player.includes('compareCardRulingsByNumber(left, right') && player.includes('compareRulingsByScoreAndDate(left, right'),
  'card rulings must remain ordered by descending canonical card number')
assert(player.includes('v-if="openIds.has(item.id)"')
  && player.includes("if (next.has(item.id) && item.cardIds.length) void ensureCardCatalog()"),
  'linked card art must stay behind the expanded ruling boundary')
assert(/watch\(\[tab, faqMode\],[\s\S]*?ensureCardCatalog\(\)[\s\S]*?\}, \{ immediate: true \}\)/.test(player),
  'direct card FAQ entry must initialize card title metadata immediately')
for (const token of ['coreTableOfContents', 'scrollToCoreChapter', 'aria-label="规则手册章节目录"', 'rule-block-image', 'block.image.mobileUrl'])
  assert(player.includes(token), `core rule chapter directory or managed image presentation missing ${token}`)
assert(player.includes('ruleResults.value') && player.includes("const chapter = block.chapter?.trim() || '其他规则'"),
  'core directory must derive from the currently filtered published blocks')

const admin = readFrontend('src/l12/site/AdminRuleRulingsPanel.vue')
for (const token of ['SingleCardPicker', 'rule-item-publish', 'ruleHistory', 'historyChanges', '审核并发布此项', '高级：查看原始结构（只读）'])
  assert(admin.includes(token), `admin rule workflow missing ${token}`)
for (const token of ["'drafts'", "'sources'", "'published'", "'history'", 'workspaceCounts', 'admin-item-preview', '退回修改', '保存此项'])
  assert(admin.includes(token), `admin workspaces missing ${token}`)
for (const token of ['CatalogCardDetails', 'CardImage', 'normalizeAllRulingProducts', '自动归属产品', '不可手工修改', '待补关联'])
  assert(admin.includes(token), `admin derived card ruling workflow missing ${token}`)
for (const token of ["workspace === 'published'", 'published-preview', 'published-actions', 'revealDraftItem', '查看已发布内容'])
  assert(admin.includes(token), `published read-only workflow missing ${token}`)
for (const token of ['addCenterItem', 'moveCenterItem', 'deleteCenterItem', 'createRuleItem', 'deleteRuleItem', 'MediaUploadField', 'kind="rule"'])
  assert(admin.includes(token), `rule material block management missing ${token}`)
assert(!admin.includes('稳定 ID<input v-model.trim="item.row.id"'), 'rule material stable IDs must not be editable')
assert(!admin.includes('页码（可留空）') && !admin.includes('>主题<input v-model.trim="item.row.topic"')
  && !admin.includes('>栏目<input :value="item.collection"'), 'page, topic and raw collection fields must stay out of the rule material editor')
assert(admin.includes('width:min(100%,1680px)'), 'rule review workspace must remain bounded on ultra-wide screens')
assert(!admin.includes('产品（逗号分隔）'), 'card ruling products must not remain manually editable')
assert(!admin.includes('class="publish-queue"'), 'publishing controls must stay next to each reviewed object')
assert(!admin.includes('移动实体'), 'admin ruling copy must use game terminology')

const store = readRepo('服务端WebSocket/TwelveLegions/L12PlatformStore.SiteContent.cs')
for (const token of ['ProjectEffectiveRuleContent', 'NextRuleContentTransition', 'rule-item-publish', 'ExpectedVersion', 'schemaVersion', 'NormalizeRuleRulingProducts', '_officialCardProducts'])
  assert(store.includes(token), `server rule projection missing ${token}`)
for (const token of ['CreateRuleItem', 'DeleteRuleItem', 'NextRuleCenterItemId', 'rule-item-delete', 'HydrateRuleCenterMedia', '["rule"]', 'media.Kind != "rule"'])
  assert(store.includes(token), `server rule block lifecycle missing ${token}`)
const server = readRepo('服务端WebSocket/TwelveLegions/L12WebSocketServer.ResourceSync.cs')
assert(server.includes('RulesContentResource') && server.includes('ScheduleNextRulesContentTransition'),
  'rulesContent revisions and one-shot effective-time scheduling must remain enabled')
const portrait = readFrontend('scripts/verify-site-portrait-filters.mjs')
assert(portrait.includes("name: '规则章节筛选'") && portrait.includes(".rule-layout article').first().waitFor()"),
  'portrait verification must exercise loaded rule content and the collapsed mobile topic filter')

console.log(`Rule center contracts OK: ${ruleRows.length} official blocks, ${blankPages.length} blank page fields retained.`)
