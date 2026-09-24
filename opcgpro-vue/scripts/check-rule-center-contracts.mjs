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

const player = readFrontend('src/l12/site/RuleCenterPage.vue')
assert((player.match(/getPublicContentBatch\(/g) ?? []).length >= 2, 'initial and resource refresh paths must use public batch reads')
assert(!player.includes("getPublicContent('rules."), 'rule center must not fan out single-content requests')
assert(!player.includes('setInterval('), 'rule center must not poll')
for (const token of ['useRoute()', 'useRouter()', 'l12-resource-rulesContent', 'nextRuleTransitionAt', 'loadDynamicContent', 'selectedTopics'])
  assert(player.includes(token), `player rule center missing ${token}`)
assert(player.includes("getPublicContentBatch(['rules.notice', 'rules.center', 'rules.rulings'])"), 'first content read must be one three-key batch')
const initialLoad = player.slice(player.indexOf('async function loadDynamicContent()'), player.indexOf('async function ensureCardCatalog()'))
assert((initialLoad.match(/getPublicContentBatch\(/g) ?? []).length === 1
  && (initialLoad.match(/getEffectiveOperationsPolicy\(/g) ?? []).length === 1
  && !initialLoad.includes('loadDeckCatalog('), 'first render must stay within two rule-center requests')
for (const token of ['contentError', 'loadDynamicContent">', 'router.replace', 'router.push', 'visibilitychange'])
  assert(player.includes(token), `retry/navigation contract missing ${token}`)

const admin = readFrontend('src/l12/site/AdminRuleRulingsPanel.vue')
for (const token of ['SingleCardPicker', 'rule-item-publish', 'ruleHistory', 'historyChanges', '审核并发布此项', '高级：查看原始结构（只读）'])
  assert(admin.includes(token), `admin rule workflow missing ${token}`)
assert(!admin.includes('移动实体'), 'admin ruling copy must use game terminology')

const store = readRepo('服务端WebSocket/TwelveLegions/L12PlatformStore.SiteContent.cs')
for (const token of ['ProjectEffectiveRuleContent', 'NextRuleContentTransition', 'rule-item-publish', 'ExpectedVersion', 'schemaVersion'])
  assert(store.includes(token), `server rule projection missing ${token}`)
const server = readRepo('服务端WebSocket/TwelveLegions/L12WebSocketServer.ResourceSync.cs')
assert(server.includes('RulesContentResource') && server.includes('ScheduleNextRulesContentTransition'),
  'rulesContent revisions and one-shot effective-time scheduling must remain enabled')
const portrait = readFrontend('scripts/verify-site-portrait-filters.mjs')
assert(portrait.includes("name: '规则主题筛选'") && portrait.includes(".rule-layout article').first().waitFor()"),
  'portrait verification must exercise loaded rule content and the collapsed mobile topic filter')

console.log(`Rule center contracts OK: ${ruleRows.length} official blocks, ${blankPages.length} blank page fields retained.`)
