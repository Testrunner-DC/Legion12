import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/operations-config-preview')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import { createApp, h, ref } from 'vue'
import AdminOperationsPanel from '/src/l12/site/AdminOperationsPanel.vue'
import { authState, platformState } from '/src/l12/platform.ts'
import { l12State } from '/src/l12/net.ts'
import '/src/style.css'
platformState.account = { id: 'qa-admin', username: '维护测试管理员', role: 'admin', permissions: ['admin.operations.read', 'admin.operations.write'] }
platformState.token = 'fixture-only'
authState.initialized = true
authState.verified = true
l12State.endpoint = location.origin.replace('http', 'ws') + '/ws'
const notice = ref('')
createApp({ render: () => h('main', { style: 'max-width:1200px;margin:20px auto' }, [
  h('p', { id: 'qa-notice' }, notice.value),
  h(AdminOperationsPanel, { onNotice: value => { notice.value = value } }),
]) }).mount('#app')
`

const server = await createServer({
  root,
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'operations-preview-fixture',
    resolveId(id) { if (id === '/__operations_preview__.js') return id },
    load(id) { if (id === '/__operations_preview__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if (request.url === '/__operations_preview__') {
          response.setHeader('Content-Type', 'text/html')
          response.end('<meta name="viewport" content="width=device-width,initial-scale=1"><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__operations_preview__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const rankedConfig = {
  placementMatches: 5,
  placementMaximum: 29999,
  broadcastEnabled: true,
  factions: [],
  masterTitles: [],
  timeControl: { totalTimeSeconds: 1500, operationTimeSeconds: 240, reconnectGraceSeconds: 240,
    disasterDecisionSeconds: 60, mulliganDecisionSeconds: 60 },
  broadcast: { displaySeconds: 16, lobbyDelaySeconds: 3, intervalSeconds: 15, winStreakThreshold: 5,
    streakEndedThreshold: 5, minimumTierIndex: 0, winStreakEnabled: true, streakEndedEnabled: true,
    highestTierEnabled: true, factionTitleEnabled: true, masterTitleEnabled: true },
}

function initialConfig() {
  return {
    season: { id: 'S01', name: '第一赛季', status: 'active', startsAt: null, endsAt: null },
    disasterPool: { cardIds: ['S01-DS01', 'S01-DS02', 'S01-DS03', 'S01-DS04', 'S01-DS05', 'S01-DS06', 'S01-DS07', 'S01-DS08', 'S01-DS10'], annihilationLocked: true },
    cardRestrictions: [],
    defaultPresetDeckIds: [],
    matchModes: [{ id: 'ranked', name: '排位对战', enabled: true }, { id: 'casual', name: '休闲对战', enabled: true }],
    defaultRoomConfig: { matchModeId: 'casual', spectating: 'public', handVisibility: 'request', disasterMode: 'all' },
    featureFlags: { tournaments: true, publicDecks: true },
    maintenance: { enabled: false, message: '', startsAt: null, endsAt: null, advanceBroadcastHours: 2, expectedDurationHours: 6 },
    announcements: [],
  }
}

function serverNormalized(config) {
  const date = value => value ? new Date(value).toISOString().replace('.000Z', '+00:00') : null
  // Deliberately return a different property/key order and nullable optionals.
  return {
    maintenance: { message: config.maintenance.message.trim(), enabled: config.maintenance.enabled,
      endsAt: date(config.maintenance.endsAt), startsAt: date(config.maintenance.startsAt),
      expectedDurationHours: config.maintenance.expectedDurationHours,
      advanceBroadcastHours: config.maintenance.advanceBroadcastHours },
    featureFlags: { publicDecks: config.featureFlags.publicDecks, tournaments: config.featureFlags.tournaments },
    defaultRoomConfig: { ...config.defaultRoomConfig },
    matchModes: [...config.matchModes].sort((left, right) => left.id.localeCompare(right.id)),
    defaultPresetDeckIds: [...config.defaultPresetDeckIds],
    cardRestrictions: [...config.cardRestrictions],
    disasterPool: { ...config.disasterPool, cardIds: [...config.disasterPool.cardIds] },
    season: { ...config.season, startsAt: date(config.season.startsAt), endsAt: date(config.season.endsAt) },
    announcements: [...config.announcements],
  }
}

let browser
let version = 58
let maintenanceRevision = 1
let currentConfig = initialConfig()
const sectionRevisions = { room: 1, features: 1, announcements: 1, maintenance: 1 }
const fieldRevisions = {
  'maintenance/enabled': 1,
  'maintenance/message': 1,
  'maintenance/startsAt': 1,
  'maintenance/endsAt': 1,
  'maintenance/advanceBroadcastHours': 1,
  'maintenance/expectedDurationHours': 1,
}
let failNextApply = false
const requests = []
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const context = await browser.newContext({ viewport: { width: 1380, height: 1000 }, timezoneId: 'Asia/Shanghai' })
  const page = await context.newPage()
  const pageErrors = []
  page.on('pageerror', error => pageErrors.push(error.message))
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.hostname !== '127.0.0.1') return route.abort()
    if (url.pathname.startsWith('/data/l12/')) return route.fulfill({ json: [] })
    if (!url.pathname.startsWith('/api/')) return route.continue()
    const body = request.postDataJSON?.()
    requests.push({ path: url.pathname, method: request.method(), body })
    if (url.pathname === '/api/admin/operations/config' && request.method() === 'GET') return route.fulfill({ json: {
      version, versionId: 'fixture-' + version, updatedBy: '维护测试管理员', updatedAt: '2026-09-26T18:00:00Z', config: currentConfig,
      sectionRevisions, fieldRevisions,
    } })
    if (/^\/api\/admin\/operations\/config\/sections\/[^/]+\/history$/.test(url.pathname)) return route.fulfill({ json: [] })
    if (url.pathname === '/api/admin/runtime/status') return route.fulfill({ json: {} })
    if (url.pathname === '/api/admin/ranked/config') return route.fulfill({ json: rankedConfig })
    if (url.pathname === '/api/ranked/broadcasts') return route.fulfill({ json: [] })
    if (url.pathname === '/api/admin/seasons') return route.fulfill({ json: {
      operationsVersion: version, current: { definitionId: 'season-current', lifecycleStatus: 'active', revision: 1,
        seasonId: 'S01', name: '第一赛季', configuration: { disasterPool: currentConfig.disasterPool,
          cardRestrictions: [], defaultPresetDeckIds: [], ranked: rankedConfig } }, next: null,
    } })
    if (url.pathname === '/api/admin/operations/config/sections/maintenance/preview') {
      const normalized = serverNormalized({ ...currentConfig, maintenance: body.config.maintenance })
      return route.fulfill({ json: { valid: true, section: 'maintenance', currentRevision: maintenanceRevision,
        nextRevision: maintenanceRevision + 1, normalized: { maintenance: normalized.maintenance },
        expectedFieldRevisions: body.expectedFieldRevisions,
        changes: Object.keys(body.expectedFieldRevisions), warnings: ['maintenance-enabled'] } })
    }
    if (url.pathname === '/api/admin/operations/config/sections/maintenance' && request.method() === 'PUT') {
      if (failNextApply) {
        failNextApply = false
        return route.fulfill({ status: 409, json: { error: 'operations_field_conflict', message: '预约维护字段已变化' } })
      }
      currentConfig = serverNormalized({ ...currentConfig, maintenance: body.config.maintenance })
      version += 1
      maintenanceRevision += 1
      sectionRevisions.maintenance = maintenanceRevision
      for (const field of Object.keys(body.expectedFieldRevisions)) fieldRevisions[field] += 1
      return route.fulfill({ json: { applied: true, current: { section: 'maintenance',
        revision: maintenanceRevision, operationsVersion: version, versionId: 'fixture-' + version,
        updatedBy: '维护测试管理员', updatedAt: '2026-09-26T18:10:00Z',
        config: { maintenance: currentConfig.maintenance }, fieldRevisions },
        historyEntry: { id: 'fixture-' + version, section: 'maintenance', revision: maintenanceRevision,
          operationsVersion: version, action: 'section:maintenance:apply',
          config: { maintenance: currentConfig.maintenance }, changedFields: Object.keys(body.expectedFieldRevisions),
          actorId: 'qa-admin', actorName: '维护测试管理员', reason: body.reason,
          createdAt: '2026-09-26T18:10:00Z' }, changes: Object.keys(body.expectedFieldRevisions) } })
    }
    return route.fulfill({ status: 404, json: { message: 'unexpected fixture request: ' + url.pathname } })
  })

  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__operations_preview__`)
  await page.getByText('维护与启服', { exact: true }).click()
  const planToggle = page.locator('label').filter({ hasText: '启用维护计划' }).locator('input')
  const message = page.getByLabel('维护提示')
  const startsAt = page.getByLabel('开始时间', { exact: true })
  const endsAt = page.getByLabel('结束时间（可选）')
  const noEnd = page.locator('label').filter({ hasText: '不设置结束时间' }).locator('input')
  const reason = page.getByPlaceholder('此分区的变更理由（必填）')
  const previewButton = page.getByRole('button', { name: '预览分区差异', exact: true })
  const saveButton = page.getByRole('button', { name: '保存当前分区', exact: true })

  await planToggle.check()
  await message.fill('正式服维护 · 中文提示')
  await startsAt.fill('2026-09-27T03:00')
  await noEnd.uncheck()
  await endsAt.fill('2026-09-27T09:00')
  await previewButton.click()
  await page.getByText('预览通过 · maintenance r1 → r2', { exact: false }).waitFor()
  const finitePreview = requests.filter(item => item.path.endsWith('/preview')).at(-1)
  assert.equal(finitePreview.body.config.maintenance.startsAt, '2026-09-26T19:00:00.000Z', '北京时间必须转为同一 UTC 时刻')
  assert.equal(finitePreview.body.config.maintenance.endsAt, '2026-09-27T01:00:00.000Z', '结束时间必须保持北京时间语义')
  assert.equal(finitePreview.body.config.maintenance.message, '正式服维护 · 中文提示')

  await reason.fill('预览后填写的维护理由')
  await saveButton.click()
  await page.getByRole('dialog').waitFor()
  await message.evaluate(element => {
    element.value = '弹框打开后的未预览篡改'
    element.dispatchEvent(new Event('input', { bubbles: true }))
  })
  currentConfig.announcements = [{ id: 'concurrent', content: '另一管理员公告', enabled: true, sortOrder: 0 }]
  await page.getByRole('button', { name: '确认保存分区', exact: true }).click()
  await page.getByText('maintenance 分区 r2 已保存并写入审计', { exact: false }).waitFor()
  const finiteApply = requests.filter(item => item.path === '/api/admin/operations/config/sections/maintenance' && item.method === 'PUT').at(-1)
  assert.equal(finiteApply.body.expectedRevision, 1)
  assert.equal(finiteApply.body.reason, '预览后填写的维护理由')
  assert.equal(finiteApply.body.config.maintenance.message, '正式服维护 · 中文提示', '确认弹框必须提交已预览快照')
  assert.equal(finiteApply.body.config.maintenance.startsAt, '2026-09-26T19:00:00.000Z')
  assert.equal('announcements' in finiteApply.body.config, false, '分区保存不得提交或覆盖其它分区')
  assert.equal(currentConfig.announcements[0].content, '另一管理员公告', '服务端合并必须保留并发更新的其它分区')

  await noEnd.check()
  assert.equal(await endsAt.isDisabled(), true)
  await message.fill('无结束时间维护')
  await previewButton.click()
  await page.getByText('预览通过 · maintenance r2 → r3', { exact: false }).waitFor()
  await reason.fill('同样在预览后填写')
  const applyCountBeforeEdit = requests.filter(item => item.path === '/api/admin/operations/config/sections/maintenance' && item.method === 'PUT').length
  await message.fill('真实修改必须失效')
  await saveButton.click()
  await page.getByText('配置已变化或尚未通过预览', { exact: false }).waitFor()
  assert.equal(requests.filter(item => item.path === '/api/admin/operations/config/sections/maintenance' && item.method === 'PUT').length, applyCountBeforeEdit)
  assert.equal(await page.getByRole('dialog').count(), 0, '失效预览不得打开风险确认')

  await previewButton.click()
  await page.getByText('预览通过 · maintenance r2 → r3', { exact: false }).waitFor()
  await reason.fill('版本冲突必须失败关闭')
  failNextApply = true
  await saveButton.click()
  await page.getByRole('dialog').waitFor()
  await page.getByRole('button', { name: '确认保存分区', exact: true }).click()
  await page.getByRole('alert').filter({ hasText: '预约维护字段已变化' }).waitFor()
  const conflictApply = requests.filter(item => item.path === '/api/admin/operations/config/sections/maintenance' && item.method === 'PUT').at(-1)
  assert.equal(conflictApply.body.expectedRevision, 2)
  assert.equal('endsAt' in conflictApply.body.config.maintenance, false, '无结束时间应稳定规范化为省略字段')
  assert.equal(version, 59, '字段冲突不得更新本地或服务端版本')
  assert.equal(await page.getByRole('dialog').count(), 1, '失败后风险确认必须保持打开')

  await page.screenshot({ path: path.join(output, 'maintenance-preview-version-conflict.png'), fullPage: true })
  assert.deepEqual(pageErrors, [])
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ passed: true,
    finitePreview: finitePreview.body.config.maintenance, finiteApply: finiteApply.body.config.maintenance,
    conflictApply: conflictApply.body.config.maintenance }, null, 2))
  console.log('Operations section preview UI passed: section-only payload, field revisions, unrelated-section preservation, frozen confirm submission and field-conflict fail-closed.')
} finally {
  await browser?.close()
  await server.close()
}
