import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const source = readFileSync(new URL('../src/l12/site/AdminCardAnalyticsPanel.vue', import.meta.url), 'utf8')

assert.match(source, /import SandboxCardPicker,[\s\S]*from '@\/l12\/game\/SandboxCardPicker\.vue'/,
  'analytics must reuse the GM card picker')
assert.match(source, /<SandboxCardPicker v-if="pickerOpen"[^>]*@select="chooseCard"/,
  'single-card selection must be wired to the shared picker')
assert.doesNotMatch(source, /<SandboxCardPicker[^>]*allowed-types/,
  'analytics must allow the full catalog rather than narrowing picker card types')

for (const range of ["applyRange('all')", "applyRange('7d')", "applyRange('30d')", "applyRange('season')"])
  assert.ok(source.includes(range), `missing analytics time range: ${range}`)
assert.match(source, /currentSeason\.value\?\.id/,
  'current-season range must use the authoritative operations-policy season id')

assert.match(source, /page: listPage\.value[\s\S]*limit: listPageSize[\s\S]*sort: serverSort\(\)[\s\S]*direction: sortDirection\.value/,
  'list view must delegate sorting and bounded pagination to the server')
assert.doesNotMatch(source, /2000|完整筛选清单|visibleListItems|analyticsListLimit/,
  'list view must not restore full-list retrieval or local pagination')
for (const key of ['name', 'sampleSize', 'inclusionRate', 'winRate', 'gih', 'iwd'])
  assert.ok(source.includes(`setSort('${key}')`), `missing sortable analytics column: ${key}`)

assert.match(source, /activeTab === 'single'/, 'single-card dashboard must be the primary subtab')
assert.match(source, /activeTab === 'list'/, 'sortable card list must be a separate subtab')
assert.match(source, /activeTab === 'master'/, 'master analytics must be a separate subtab')
assert.match(source, /AdminMasterAnalyticsPanel/, 'master analytics subtab must use its dedicated panel')
assert.match(source, /import PagedCollection from '\.\/PagedCollection\.vue'/,
  'long detail breakdowns must use the shared bounded pagination component')
assert.doesNotMatch(source, /onMounted\([\s\S]*await loadAnalytics\(\)/,
  'opening the primary single-card tab must not prefetch the complete list')
assert.match(source, /function observedPercent[\s\S]*coverageStatus === 'complete'/,
  'usage-rate enrichment must preserve unknown coverage instead of fabricating zeroes')

console.log('card analytics panel contract checks passed')
