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
  { width: 320, height: 568 },
  { width: 360, height: 800 },
  { width: 390, height: 844 },
  { width: 430, height: 932 },
  { width: 932, height: 430 },
  { width: 844, height: 390 },
  { width: 667, height: 375 },
  { width: 568, height: 320 },
]
const modes = ['full', 'crown', 'mixed', 'minimal', 'placement']

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

      const placement = await page.evaluate(() => {
        const box = selector => {
          const element = document.querySelector(selector)
          if (!element) return null
          const rect = element.getBoundingClientRect()
          return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom }
        }
        const names = [...document.querySelectorAll('.mobile-player-name strong')].map(element => {
          const rect = element.getBoundingClientRect()
          return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom }
        })
        return {
          panel: box('.player-panel'), stage: box('.felt-board'),
          record: box('.mobile-record-trigger'), desktopRecord: box('.record-log'),
          route: box('.battle-route-controls'), clock: box('.mobile-timed-clocks'),
          primary: box('.mobile-battle-dock__primary'), utility: box('.mobile-battle-dock__utility'),
          names,
        }
      })
      const overlapArea = (a, b) => !a || !b ? 0 : Math.max(0, Math.min(a.right, b.right) - Math.max(a.left, b.left))
        * Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top))
      assert.equal(overlapArea(placement.panel, placement.stage), 0,
        `${viewport.width}x${viewport.height}/${mode}: identity panel intrudes into battlefield ${JSON.stringify(placement)}`)
      if (isMobile) {
        for (const name of placement.names) {
          for (const key of ['record', 'route', 'clock', 'primary', 'utility', 'stage']) {
            assert.equal(overlapArea(name, placement[key]), 0,
              `${viewport.width}x${viewport.height}/${mode}: player name intrudes into ${key} ${JSON.stringify(placement)}`)
          }
        }
      } else {
        assert(placement.desktopRecord && placement.panel.bottom <= placement.desktopRecord.top + 1,
          `${viewport.width}x${viewport.height}/${mode}: desktop identity panel moved below match record ${JSON.stringify(placement)}`)
      }

      if (isMobile) {
        assert.equal(await page.locator('.mobile-player-name').count(), 2,
          `${viewport.width}x${viewport.height}/${mode}: persistent full-name strip is missing`)
        await page.screenshot({ path: path.join(output, `mobile-${viewport.width}x${viewport.height}-${mode}-strip.png`) })
        await page.locator('.mobile-player-name').first().click()
        await page.locator('.mobile-player-details-dialog').waitFor()
      } else {
        assert.equal(await page.locator('.mobile-player-name,.mobile-battle-dock,.mobile-record-trigger').count(), 0,
          `${viewport.width}x${viewport.height}/${mode}: mobile-only controls leaked into desktop layout`)
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
            contentBounds:bounds(item.querySelector('strong')),
            fits:item.scrollWidth <= item.clientWidth + 1 && item.scrollHeight <= item.clientHeight + 1,
          })),
          routeControls: optionalBounds('.battle-route-controls'),
          recordTrigger: optionalBounds('.mobile-record-trigger'),
          timedClocks: optionalBounds('.mobile-timed-clocks') }
      }, isMobile)

      assert.equal(report.identities.length, 2,
        `${viewport.width}x${viewport.height}/${mode}: both player identities must be present`)
      assert.equal(report.identities[0].name, '对方测试长昵称十二军团')
      assert.equal(report.identities[1].name, '我方测试昵称')
      for (const identity of report.identities) {
        assert(identity.scrollWidth <= identity.clientWidth + 1,
          `${viewport.width}x${viewport.height}/${mode}: player identity must not overflow horizontally ${JSON.stringify(identity)}`)
        assert(identity.name && identity.connection && (!isMobile || identity.master),
          `${viewport.width}x${viewport.height}/${mode}: lawful player fields are incomplete ${JSON.stringify(identity)}`)
        for (const row of identity.rows) {
          assert(row.left >= report.identityRoot.left - 1 && row.right <= report.identityRoot.right + 1,
            `${viewport.width}x${viewport.height}/${mode}: identity row left its host ${JSON.stringify({ row, host: report.identityRoot })}`)
          assert(row.top >= report.identityRoot.top - 1 && row.bottom <= report.identityRoot.bottom + 1,
            `${viewport.width}x${viewport.height}/${mode}: identity row was clipped ${JSON.stringify({ row, host: report.identityRoot })}`)
        }
        for (let index = 1; index < identity.rows.length; index += 1) {
          assert(viewport.height > viewport.width && isMobile
            ? identity.rows[index].right <= identity.rows[index - 1].left + 0.75
            : identity.rows[index].top >= identity.rows[index - 1].bottom - 0.75,
            `${viewport.width}x${viewport.height}/${mode}: identity facts must occupy separate rows ${JSON.stringify(identity.rows)}`)
        }
      }
      if (isMobile) {
        assert.equal(report.names.length, 2,
          `${viewport.width}x${viewport.height}/${mode}: both persistent names must be present`)
        assert.equal(report.names.every(item => item.text && item.fits), true,
          `${viewport.width}x${viewport.height}/${mode}: persistent name was clipped ${JSON.stringify(report.names)}`)
        assert.equal(report.names[0].text, '对方对方测试长昵称十二军团')
        assert.equal(report.names[1].text, '我方我方测试昵称')
        assert.equal(report.names.every(item => item.bounds.left >= -1 && item.bounds.top >= -1
          && item.bounds.right <= viewport.width + 1 && item.bounds.bottom <= viewport.height + 1), true,
        `${viewport.width}x${viewport.height}/${mode}: persistent name left the viewport ${JSON.stringify(report.names)}`)
        assert(report.recordTrigger && report.names.every(item => viewport.height > viewport.width
          ? item.contentBounds.left >= report.recordTrigger.right - 1
          : item.bounds.bottom <= report.recordTrigger.top + 1),
          `${viewport.width}x${viewport.height}/${mode}: player names must remain above match record ${JSON.stringify({ names: report.names, record: report.recordTrigger })}`)
        assert(report.identityRoot.left >= -1 && report.identityRoot.top >= -1
          && report.identityRoot.right <= viewport.width + 1 && report.identityRoot.bottom <= viewport.height + 1,
        `${viewport.width}x${viewport.height}/${mode}: detail dialog escaped viewport`)
      } else {
        assert(report.panelScrollHeight <= report.panelClientHeight + 1,
          `${viewport.width}x${viewport.height}/${mode}: desktop player panel content was clipped`)
      }

      const [enemyIdentity, myIdentity] = report.identities
      if (mode === 'full') {
        for (const identity of report.identities) {
          assert.match(identity.rank, /^\d+名$/, `${viewport.width}x${viewport.height}/${mode}: highest-tier rank missing`)
          assert.ok(identity.tier && !identity.tier.includes('天冠') && !identity.tier.includes('魔冠'),
            `${viewport.width}x${viewport.height}/${mode}: placement title must replace the redundant tier`)
          assert.ok(identity.masterTitle, `${viewport.width}x${viewport.height}/${mode}: master title missing`)
        }
      } else if (mode === 'crown') {
        for (const [index, identity] of report.identities.entries()) {
          assert.match(identity.rank, /^\d+名$/, `${viewport.width}x${viewport.height}/${mode}: highest-tier rank missing`)
          assert.match(identity.tier, index === 0 ? /混沌魔冠/ : /秩序天冠/,
            `${viewport.width}x${viewport.height}/${mode}: faction-specific highest tier must remain visible without a title`)
          assert.equal(identity.masterTitle, '', `${viewport.width}x${viewport.height}/${mode}: unavailable master title must be omitted`)
        }
      } else if (mode === 'mixed') {
        assert.equal(enemyIdentity.rank, '', `${viewport.width}x${viewport.height}/${mode}: non-Crown rank must be omitted`)
        assert.match(enemyIdentity.tier, /统领/, `${viewport.width}x${viewport.height}/${mode}: non-Crown tier missing`)
        assert.equal(enemyIdentity.masterTitle, '', `${viewport.width}x${viewport.height}/${mode}: unavailable master title must be omitted`)
        assert.equal(myIdentity.rank, '3名', `${viewport.width}x${viewport.height}/${mode}: highest-tier rank missing`)
        assert.match(myIdentity.tier, /秩序冠首/, `${viewport.width}x${viewport.height}/${mode}: placement title missing`)
        assert.ok(!myIdentity.tier.includes('冠冕'), `${viewport.width}x${viewport.height}/${mode}: tier must be hidden behind placement title`)
        assert.ok(myIdentity.masterTitle, `${viewport.width}x${viewport.height}/${mode}: master title missing`)
      } else if (mode === 'minimal') {
        assert.equal(report.identities.every(identity => identity.rank === ''), true,
          `${viewport.width}x${viewport.height}/${mode}: non-Crown ranks must be omitted`)
        assert.match(enemyIdentity.tier, /进阶/, `${viewport.width}x${viewport.height}/${mode}: opponent tier missing`)
        assert.match(myIdentity.tier, /精英/, `${viewport.width}x${viewport.height}/${mode}: player tier missing`)
        assert.equal(report.identities.every(identity => identity.masterTitle === ''), true,
          `${viewport.width}x${viewport.height}/${mode}: unavailable master titles must be omitted`)
      } else {
        assert.equal(report.identities.every(identity => identity.rank === ''), true,
          `${viewport.width}x${viewport.height}/${mode}: unavailable ranks must be omitted`)
        assert.equal(report.identities.every(identity => /定级/.test(identity.tier)), true,
          `${viewport.width}x${viewport.height}/${mode}: placement progress missing`)
        assert.equal(report.identities.every(identity => identity.masterTitle === ''), true,
          `${viewport.width}x${viewport.height}/${mode}: unavailable master titles must be omitted`)
      }

      const suffix = `${isMobile ? 'mobile' : 'desktop'}-${viewport.width}x${viewport.height}-${mode}`
      await page.screenshot({ path: path.join(output, `${suffix}.png`) })
      reports.push({ suffix, placement, ...report })
      if (isMobile && mode === 'mixed') {
        await page.getByRole('button', { name: '关闭双方玩家详情' }).click()
        await page.locator('.mobile-player-name.mine').click()
        assert.equal(await page.locator('.mobile-player-details-list .battle-player-identity.focused').count(), 1,
          `${viewport.width}x${viewport.height}: own-player detail entry did not focus an identity`)
        await page.getByRole('button', { name: '关闭双方玩家详情' }).click()
        await page.locator('.mobile-record-trigger').click()
        await page.getByRole('dialog', { name: '对局记录' }).waitFor()
        await page.getByRole('dialog', { name: '对局记录' }).getByRole('button', { name: '关闭' }).click()
      }
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
  console.log(JSON.stringify({ output, profiles: reports.length,
    screenshots: reports.length + mobile.length * modes.length, contactSheet, errors }, null, 2))
} finally {
  await browser.close()
}
