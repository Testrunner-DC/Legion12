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

assert.match(source, /do \{[\s\S]*limit: analyticsListLimit[\s\S]*\} while \(cursor && items\.length < /,
  'list view must drain bounded server cursor pages before sorting')
assert.match(source, /items\.length < firstPage\.total/,
  'list view must reject incomplete full-list retrieval')
for (const key of ['name', 'sampleSize', 'includedMatches', 'inclusionRate', 'winRate', 'delta'])
  assert.ok(source.includes(`setSort('${key}')`), `missing sortable analytics column: ${key}`)
assert.match(source, /visibleListItems[\s\S]*\.slice\(/,
  'the fully sorted list must be paginated locally')

assert.match(source, /activeTab === 'single'/, 'single-card dashboard must be the primary subtab')
assert.match(source, /activeTab === 'list'/, 'sortable card list must be a separate subtab')
assert.match(source, /import PagedCollection from '\.\/PagedCollection\.vue'/,
  'long detail breakdowns must use the shared bounded pagination component')
assert.doesNotMatch(source, /onMounted\([\s\S]*await loadAnalytics\(\)/,
  'opening the primary single-card tab must not prefetch the complete list')
assert.match(source, /function observedPercent[\s\S]*coverageStatus === 'complete'/,
  'usage-rate enrichment must preserve unknown coverage instead of fabricating zeroes')

console.log('card analytics panel contract checks passed')
