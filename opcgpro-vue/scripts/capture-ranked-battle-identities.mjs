import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const sharp = require('sharp')
const target = process.argv[2] || 'http://127.0.0.1:5174/__l12_battle_preview__'
const output = process.env.L12_RANKED_IDENTITY_OUT
  || 'C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/ranked-battle-identity-20260924'
fs.mkdirSync(output, { recursive: true })

const desktop = [
  { width: 1920, height: 1080 },
  { width: 1440, height: 810 },
  { width: 1280, height: 720 },
]
const mobile = [
  { width: 932, height: 430 },
  { width: 844, height: 390 },
  { width: 667, height: 375 },
  { width: 568, height: 320 },
]
const modes = ['full', 'mixed', 'minimal', 'placement']

const browser = await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE
  ? { headless: true, executablePath: process.env.L12_CHROMIUM_EXECUTABLE }
  : { headless: true, channel: 'msedge' })
const reports = []
try {
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(
    new URL(route.request().url()).hostname) ? route.continue() : route.abort())

  for (const viewport of [...desktop, ...mobile]) {
    for (const mode of modes) {
      await page.setViewportSize(viewport)
      const isMobile = mobile.some(item => item.width === viewport.width && item.height === viewport.height)
      const query = new URLSearchParams({ identity: mode, hand: '8', rankedClock: '1' })
      if (isMobile) { query.set('mobile', '1'); query.set('canvas', '1') }
      await page.goto(`${target}?${query}`, { waitUntil: 'domcontentloaded' })
      await page.locator('.player-panel').waitFor()
      if (isMobile) await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
      await page.waitForTimeout(120)

      if (isMobile) {
        assert.equal(await page.locator('.mobile-player-name').count(), 2,
          `${viewport.width}x${viewport.height}/${mode}: persistent full-name strip is missing`)
        await page.locator('.mobile-player-name').first().evaluate(element => element.click())
        await page.locator('.mobile-player-details-dialog').waitFor()
      }

      const report = await page.evaluate(isMobileCanvas => {
        const bounds = element => {
          const box = element.getBoundingClientRect()
          return { left: box.left, right: box.right, top: box.top, bottom: box.bottom,
            width: box.width, height: box.height }
        }
        const panel = document.querySelector('.player-panel')
        const identityRoot = isMobileCanvas
          ? document.querySelector('.mobile-player-details-dialog') : panel
        const identities = [...identityRoot.querySelectorAll('.battle-player-identity')].map(identity => ({
          bounds:bounds(identity), scrollWidth:identity.scrollWidth, clientWidth:identity.clientWidth,
          scrollHeight:identity.scrollHeight, clientHeight:identity.clientHeight,
          name:identity.querySelector('.battle-player-identity__name strong')?.textContent?.trim() || '',
          rank:identity.querySelector('.identity-rank-row dd')?.textContent?.trim() || '',
          tier:identity.querySelector('.identity-tier-row dd')?.textContent?.trim() || '',
          masterTitle:identity.querySelector('.identity-master-title-row dd')?.textContent?.trim() || '',
          master:identity.querySelector('.identity-master-row dd')?.textContent?.trim() || '',
          connection:identity.querySelector('.identity-connection-row dd')?.textContent?.trim() || '',
          rows:[...identity.querySelectorAll('.battle-player-identity__facts>div')]
            .map(bounds).filter(box => box.width > 0 && box.height > 0),
        }))
        const optionalBounds = selector => {
          const element = document.querySelector(selector)
          return element ? bounds(element) : null
        }
        return { viewport: { width: innerWidth, height: innerHeight }, panel: bounds(panel),
          panelScrollHeight: panel.scrollHeight, panelClientHeight: panel.clientHeight, identities,
          identityRoot:bounds(identityRoot),
          names:[...document.querySelectorAll('.mobile-player-name')].map(item => ({
            text:item.textContent?.trim() || '', bounds:bounds(item),
            fits:item.scrollWidth <= item.clientWidth + 1 && item.scrollHeight <= item.clientHeight + 1,
          })),
          routeControls: optionalBounds('.battle-route-controls'),
          recordTrigger: optionalBounds('.mobile-record-trigger'),
          timedClocks: optionalBounds('.mobile-timed-clocks') }
      }, isMobile)

      assert.equal(report.identities.length, 2,
        `${viewport.width}x${viewport.height}/${mode}: both player identities must be present`)
      for (const identity of report.identities) {
        assert(identity.scrollWidth <= identity.clientWidth + 1,
          `${viewport.width}x${viewport.height}/${mode}: player identity must not overflow horizontally ${JSON.stringify(identity)}`)
        assert(identity.name && identity.master && identity.connection,
          `${viewport.width}x${viewport.height}/${mode}: lawful player fields are incomplete ${JSON.stringify(identity)}`)
        for (const row of identity.rows) {
          assert(row.left >= report.identityRoot.left - 1 && row.right <= report.identityRoot.right + 1,
            `${viewport.width}x${viewport.height}/${mode}: identity row left its host ${JSON.stringify({ row, host: report.identityRoot })}`)
          assert(row.top >= report.identityRoot.top - 1 && row.bottom <= report.identityRoot.bottom + 1,
            `${viewport.width}x${viewport.height}/${mode}: identity row was clipped ${JSON.stringify({ row, host: report.identityRoot })}`)
        }
        for (let index = 1; index < identity.rows.length; index += 1) {
          assert(identity.rows[index].top >= identity.rows[index - 1].bottom - 0.75,
            `${viewport.width}x${viewport.height}/${mode}: identity facts must occupy separate rows ${JSON.stringify(identity.rows)}`)
        }
      }
      if (isMobile) {
        assert.equal(report.names.length, 2,
          `${viewport.width}x${viewport.height}/${mode}: both persistent names must be present`)
        assert.equal(report.names.every(item => item.text && item.fits), true,
          `${viewport.width}x${viewport.height}/${mode}: persistent name was clipped ${JSON.stringify(report.names)}`)
        assert.equal(report.names.every(item => item.bounds.left >= -1 && item.bounds.top >= -1
          && item.bounds.right <= viewport.width + 1 && item.bounds.bottom <= viewport.height + 1), true,
        `${viewport.width}x${viewport.height}/${mode}: persistent name left the viewport ${JSON.stringify(report.names)}`)
        assert(report.recordTrigger && report.names.every(item => item.bounds.bottom <= report.recordTrigger.top + 1),
          `${viewport.width}x${viewport.height}/${mode}: player names must remain above match record`)
        assert(report.identityRoot.left >= -1 && report.identityRoot.top >= -1
          && report.identityRoot.right <= viewport.width + 1 && report.identityRoot.bottom <= viewport.height + 1,
        `${viewport.width}x${viewport.height}/${mode}: detail dialog escaped viewport`)
      } else {
        assert(report.panelScrollHeight <= report.panelClientHeight + 1,
          `${viewport.width}x${viewport.height}/${mode}: desktop player panel content was clipped`)
      }

      for (const identity of report.identities) {
        if (mode === 'placement') {
          assert.equal(identity.rank, '', `${viewport.width}x${viewport.height}/${mode}: unavailable rank must be omitted`)
          assert.match(identity.tier, /定级/, `${viewport.width}x${viewport.height}/${mode}: placement title missing`)
          assert.equal(identity.masterTitle, '', `${viewport.width}x${viewport.height}/${mode}: unavailable master title must be omitted`)
        } else {
          assert.match(identity.rank, /^第 \d+ 名$/, `${viewport.width}x${viewport.height}/${mode}: authoritative rank missing`)
          assert.ok(identity.tier, `${viewport.width}x${viewport.height}/${mode}: tier/title row missing`)
          assert.equal(Boolean(identity.masterTitle), mode === 'full' || (mode === 'mixed' && identity.rank === '第 3 名'),
            `${viewport.width}x${viewport.height}/${mode}: master title availability changed`)
        }
      }

      const suffix = `${isMobile ? 'mobile' : 'desktop'}-${viewport.width}x${viewport.height}-${mode}`
      await page.screenshot({ path: path.join(output, `${suffix}.png`) })
      reports.push({ suffix, ...report })
    }
  }
  assert.deepEqual(errors, [], 'ranked identity fixture must not throw page errors')
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(reports, null, 2))
  const cellWidth = 400
  const cellHeight = 252
  const columns = 4
  const rows = Math.ceil(reports.length / columns)
  const contactLayers = []
  for (const [index, report] of reports.entries()) {
    const left = (index % columns) * cellWidth
    const top = Math.floor(index / columns) * cellHeight
    const thumbnail = await sharp(path.join(output, `${report.suffix}.png`))
      .resize(380, 214, { fit: 'contain', background: '#070b0d' }).png().toBuffer()
    const caption = Buffer.from(`<svg width="380" height="28" xmlns="http://www.w3.org/2000/svg"><rect width="100%" height="100%" fill="#10191d"/><text x="8" y="19" fill="#e9e6dc" font-size="14" font-family="Microsoft YaHei, sans-serif">${report.suffix}</text></svg>`)
    contactLayers.push({ input: caption, left: left + 10, top: top + 5 })
    contactLayers.push({ input: thumbnail, left: left + 10, top: top + 33 })
  }
  const contactSheet = path.join(output, '各比例验收总览.png')
  await sharp({ create: { width: columns * cellWidth, height: rows * cellHeight, channels: 4, background: '#06090b' } })
    .composite(contactLayers).png().toFile(contactSheet)
  console.log(JSON.stringify({ output, screenshots: reports.length, contactSheet, errors }, null, 2))
} finally {
  await browser.close()
}
