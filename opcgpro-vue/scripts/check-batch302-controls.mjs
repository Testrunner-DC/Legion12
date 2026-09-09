import assert from 'node:assert/strict'
import fs from 'node:fs'
import { maintenanceCountdown } from '../src/l12/site/maintenanceCountdown.ts'
import './test-card-analytics-panel.mjs'
const start = Date.parse('2026-09-10T12:00:00Z')
const config = { enabled: true, message: '自定义维护提示', broadcastMessage: '广播', startsAt: new Date(start).toISOString(), advanceBroadcastHours: 4 }
assert.equal(maintenanceCountdown(config, start - 4 * 3600000 - 1), null)
assert.equal(maintenanceCountdown(config, start - 4 * 3600000)?.message, config.message)
assert.equal(maintenanceCountdown(config, start)?.phase, 'active')
assert.equal(maintenanceCountdown({ ...config, immediateActive: true }, start - 12 * 3600000)?.phase, 'active')
const read = name => fs.readFileSync(new URL(`../src/l12/site/${name}`, import.meta.url), 'utf8')
const editor = fs.readFileSync(new URL('../src/l12/L12DeckEditor.vue', import.meta.url), 'utf8')
const banners = editor.match(/<CardImage class="deck-entry-banner"[^>]+>/g) || []
assert.equal(banners.length, 3)
assert.ok(banners.every(banner => banner.includes('native-orientation')))
assert.match(read('SiteShell.vue'), /entry in updateEntries\.slice\(0, 10\)/)
const lobby = read('BattleHubPage.vue')
assert.doesNotMatch(lobby, /ranked\.profile\.faction = undefined/)
assert.match(lobby, /确认清零并更改/)
assert.match(lobby, /取消，保留当前派系/)
assert.match(lobby, /返回，不更改/)
for (const panel of ['AdminPage.vue','AdminArticlesPanel.vue','AdminMatchesPanel.vue','AdminMatchGovernancePanel.vue','AdminRankedIntegrityPanel.vue','AdminSiteContentPanel.vue','AdminOperationsPanel.vue'])
  assert.match(read(panel), /<PagedCollection/)
console.log('BATCH302 控件约束通过：广播边界、日志10份、派系确认、7类后台分页')
