import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const origin = process.argv[2] || 'http://127.0.0.1:5242/__l12_battle_preview__'
const reloadOnly = process.argv.includes('--reload-only')
const output = path.resolve(process.env.L12_ORDER_DIRECTION_ARTIFACT_DIR || 'artifacts/stage2d-order-direction')
const scriptPath = fileURLToPath(import.meta.url)
const appRoot = path.resolve(path.dirname(scriptPath), '..')
const repoRoot = path.resolve(appRoot, '..')
if (reloadOnly) {
  assert.ok(process.env.L12_ORDER_DIRECTION_ARTIFACT_DIR, 'reload-only requires an explicit evidence directory')
  assert.ok(['127.0.0.1', 'localhost'].includes(new URL(origin).hostname), 'reload-only must remain local')
  const quote = value => `'${value.replaceAll("'", "''")}'`
  execFileSync('pwsh', ['-NoProfile', '-Command',
    `. ${quote(path.join(repoRoot, 'ops/windows/L12GeneratedStorage.ps1'))}; $null=Assert-L12GeneratedPath -Path ${quote(output)} -Purpose 'order reload evidence'`], { stdio: 'pipe' })
}
fs.mkdirSync(output, { recursive: true })
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []

async function submitted(page) {
  return page.evaluate(() => window.__sentCommands.at(-1)?.command ?? window.__sentCommands.at(-1))
}
async function view(page, profile, mode) {
  await page.setViewportSize({ width: profile.width, height: profile.height })
  await page.goto(`${origin}?canvas=1&${profile.mobile ? 'mobile=1&' : ''}hand=6&order-direction-fixture=${mode}`, { waitUntil: 'domcontentloaded' })
  if (profile.safe) await page.addStyleTag({ content: 'html { --l12-viewport-left:44px !important; --l12-viewport-width:756px !important; --l12-viewport-height:369px !important; }' })
  if (profile.font) await page.addStyleTag({ content: '.l12-prompt-overlay { --l12-board-copy: 16px !important; --l12-board-micro: 12px !important; }' })
  const panel = page.locator('.prompt-choice-panel:visible')
  await panel.waitFor()
  return panel
}
async function assertPanel(page, panel, label) {
  const evidence = await panel.evaluate(node => {
    const rect = node.getBoundingClientRect()
    const footer = node.querySelector('.prompt-action-footer')
    const foot = footer?.getBoundingClientRect()
    return { rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom },
      footer: foot && { left: foot.left, top: foot.top, right: foot.right, bottom: foot.bottom },
      viewport: { width: innerWidth, height: innerHeight } }
  })
  assert.ok(evidence.rect.left >= -1 && evidence.rect.top >= -1
    && evidence.rect.right <= evidence.viewport.width + 1 && evidence.rect.bottom <= evidence.viewport.height + 1,
  `${label}: prompt outside viewport ${JSON.stringify(evidence)}`)
  const errors = await page.evaluate(() => window.__sentCommands.length)
  assert.equal(errors, 0, `${label}: fixture sent an action before confirmation`)
}
async function assertHoverStable(page, panel, cards, label) {
  const card = cards.first()
  const footer = panel.locator('.prompt-action-footer button').last()
  const before = { card: await card.boundingBox(), footer: await footer.boundingBox() }
  const inspectorReservedBefore = await page.locator('.l12-prompt-overlay.inspector-active').count()
  await card.hover()
  const after = { card: await card.boundingBox(), footer: await footer.boundingBox() }
  const originalCenterStillHitsCard = await card.evaluate((node, box) => {
    if (!box) return false
    const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)
    return Boolean(hit && (hit === node || node.contains(hit)))
  }, before.card)
  const delta = (left, right) => left && right
    ? { x: Math.abs(left.x - right.x), y: Math.abs(left.y - right.y) }
    : { x: Number.POSITIVE_INFINITY, y: Number.POSITIVE_INFINITY }
  const evidence = {
    label, inspectorReservedBefore, originalCenterStillHitsCard,
    cardDelta: delta(before.card, after.card), footerDelta: delta(before.footer, after.footer),
  }
  assert.equal(inspectorReservedBefore, 1, `${label}: placement inspector space was not reserved before hover ${JSON.stringify(evidence)}`)
  assert.equal(originalCenterStillHitsCard, true, `${label}: original card center was intercepted after hover`)
  assert.ok(evidence.cardDelta.x <= 1 && evidence.cardDelta.y <= 1, `${label}: card moved after hover ${JSON.stringify(evidence)}`)
  assert.ok(evidence.footerDelta.x <= 1 && evidence.footerDelta.y <= 1, `${label}: action button moved after hover ${JSON.stringify(evidence)}`)
  results.push({ name: `${label}-hover-stable`, ...evidence })
}

// A browser reload remounts the real SFC with the same public fixture prompt.
// It does not preserve an unsubmitted local draft or simulate a server ACK.
async function verifyReloadOnly() {
  const sha256 = filename => createHash('sha256').update(fs.readFileSync(filename)).digest('hex').toUpperCase()
  const artwork = Object.fromEntries(['S02-06M2', 'ST02-M1', 'ST04-M1']
    .map(id => [id, `/assets/l12/special/master/${id}.png`]))
  const manifest = JSON.stringify({ schemaVersion: 3, catalogVersion: 'order-reload-controlled-local-art',
    assetVersion: 'order-reload-controlled-local-art', basePath: '/',
    cards: Object.fromEntries(Object.entries(artwork).map(([cardId, url]) => [cardId, {
      cardId, contentHash: sha256(path.join(appRoot, 'public', url)), width: 1240, height: 1732,
      orientation: 'portrait', variants: { thumbWebp: url, boardWebp: url, detailWebp: url },
    }])) })
  const fixtureBefore = 'const cards=players[0].hand.slice(0,3).map((entry,index)=>({...entry,name:names[index]}))'
  const fixtureAfter = 'const cards=players[0].hand.slice(0,3).map((entry,index)=>({...battleCard(battleArtwork[index],entry.instanceId),name:names[index]}))'
  const cases = []
  const broadcastSettings = { displaySeconds: 16, lobbyDelaySeconds: 0, intervalSeconds: 60,
    winStreakThreshold: 5, streakEndedThreshold: 5, minimumTierIndex: 0,
    winStreakEnabled: false, streakEndedEnabled: false, highestTierEnabled: false,
    factionTitleEnabled: false, masterTitleEnabled: false }
  for (const profile of [
    { name: 'desktop-1366-reload', width: 1366, height: 768 },
    { name: 'mobile-844-reload', width: 844, height: 390, mobile: true },
  ]) {
    const context = await browser.newContext({ viewport: { width: profile.width, height: profile.height }, hasTouch: Boolean(profile.mobile) })
    const page = await context.newPage()
    page.setDefaultTimeout(9000)
    const item = { name: profile.name, viewport: { width: profile.width, height: profile.height },
      dragInput: 'Playwright native HTML5 mouse drag (not finger drag or physical-device certification)',
      confirmInput: profile.mobile ? 'two browser touchscreen taps' : 'native mouse double click',
      errors: [], blockedExternalRequests: [], failedRequests: [], httpErrors: [], screenshots: [] }
    page.on('pageerror', error => item.errors.push(`pageerror: ${error.message}`))
    page.on('console', message => { if (message.type() === 'error') item.errors.push(`console: ${message.text()}`) })
    page.on('requestfailed', request => item.failedRequests.push({ url: request.url(), error: request.failure()?.errorText }))
    page.on('response', response => { if (response.status() >= 400) item.httpErrors.push({ url: response.url(), status: response.status() }) })
    await page.route('**/*', route => {
      if (['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname)) return route.continue()
      item.blockedExternalRequests.push(route.request().url())
      return route.abort()
    })
    await page.route('**/card-assets/card-assets.manifest.json*', route => route.fulfill({ contentType: 'application/json', body: manifest }))
    await page.route('**/api/ranked/broadcasts/settings', route => route.fulfill({ json: broadcastSettings }))
    await page.route('**/__l12_battle_preview__.js', async route => {
      const response = await route.fetch()
      const source = await response.text()
      assert.equal(source.split(fixtureBefore).length, 2, 'expected exactly one existing ordering fixture adapter')
      await route.fulfill({ response, body: source.replace(fixtureBefore, fixtureAfter) })
    })
    await page.addInitScript(() => {
      window.__orderReloadEvents = []
      for (const type of ['dragstart', 'drop', 'touchstart', 'click']) document.addEventListener(type, event => {
        if (event.target instanceof Element && event.target.closest('.prompt-choice-panel'))
          window.__orderReloadEvents.push({ type, trusted: event.isTrusted,
            card: event.target.closest('.prompt-card-candidate')?.querySelector('.prompt-card-candidate__name')?.textContent ?? null })
      }, true)
    })
    const promptFacts = () => page.evaluate(() => {
      const p = window.__l12State.game.prompts[0]
      return { promptId: p.promptId, createdRevision: p.createdRevision, controller: p.controller,
        validChoices: [...p.validChoices], placementMode: p.data.placementMode }
    })
    const names = cards => cards.locator('.prompt-card-candidate__name').allInnerTexts()
    const visibleArtwork = async (cards, label) => {
      assert.equal(await cards.count(), 3, `${label}: all ordering cards must remain present`)
      const facts = []
      for (const card of await cards.all()) {
        // The mobile strip intentionally scrolls; every card must be reachable,
        // not necessarily visible simultaneously in a narrower viewport.
        await card.scrollIntoViewIfNeeded()
        const image = card.locator('.l12-card-image img')
        await image.waitFor({ state: 'visible' })
        await image.evaluate(async node => { await node.decode() })
        const fact = await card.evaluate(node => {
          const image = node.querySelector('.l12-card-image img')
          const rect = image.getBoundingClientRect()
          const style = getComputedStyle(image)
          const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
          const canvas = document.createElement('canvas'); canvas.width = canvas.height = 32
          const ctx = canvas.getContext('2d'); ctx.drawImage(image, 0, 0, 32, 32)
          const pixels = ctx.getImageData(0, 0, 32, 32).data
          let nonBlack = 0
          for (let index = 0; index < pixels.length; index += 4)
            if (pixels[index + 3] > 0 && Math.max(pixels[index], pixels[index + 1], pixels[index + 2]) > 32) nonBlack++
          return { name: node.querySelector('.prompt-card-candidate__name').textContent,
            src: image.currentSrc, source: image.closest('picture').dataset.source,
            complete: image.complete, naturalWidth: image.naturalWidth, naturalHeight: image.naturalHeight,
            width: rect.width, height: rect.height, visible: style.visibility === 'visible' && Number(style.opacity) > 0,
            inViewport: rect.left >= 0 && rect.top >= 0 && rect.right <= innerWidth && rect.bottom <= innerHeight,
            centreOwned: Boolean(hit && node.contains(hit)), nonBlackRatio: nonBlack / (32 * 32) }
        })
        assert.ok(fact.complete && fact.naturalWidth > 0 && fact.naturalHeight > 0, `${label}: decoded image missing`)
        assert.equal(fact.source, 'sameOrigin', `${label}: placeholder is not artwork`)
        assert.ok(Object.values(artwork).includes(new URL(fact.src).pathname), `${label}: unexpected artwork ${fact.src}`)
        assert.ok(fact.visible && fact.inViewport && fact.centreOwned && fact.width > 20 && fact.height > 20,
          `${label}: artwork hidden or intercepted ${JSON.stringify(fact)}`)
        assert.ok(fact.nonBlackRatio > 0.1, `${label}: artwork is black ${JSON.stringify(fact)}`)
        facts.push(fact)
      }
      assert.equal(new Set(facts.map(fact => fact.src)).size, 3, `${label}: three distinct exact local images required`)
      return facts
    }
    try {
      const dragIndex = profile.mobile ? 1 : 2
      const expectedNames = profile.mobile ? ['乙号测试卡', '甲号测试卡', '丙号测试卡'] : ['丙号测试卡', '甲号测试卡', '乙号测试卡']
      const expectedIds = profile.mobile ? ['0-hand-showcase-1', '0-hand-showcase-0', '0-hand-showcase-2'] : ['0-hand-showcase-2', '0-hand-showcase-0', '0-hand-showcase-1']
      let panel = await view(page, profile, 'all-top-bottom')
      let cards = panel.locator('.all-placement-row .prompt-card-candidate')
      item.promptBefore = await promptFacts()
      item.artworkBefore = await visibleArtwork(cards, `${profile.name}-initial`)
      await cards.nth(0).scrollIntoViewIfNeeded()
      await cards.nth(dragIndex).hover()
      await cards.nth(dragIndex).dragTo(cards.nth(0))
      assert.deepEqual(await names(cards), expectedNames)
      await assertPanel(page, panel, `${profile.name}-before-reload`)
      item.artworkAfterFirstDrag = await visibleArtwork(cards, `${profile.name}-first-drag`)
      item.eventsBeforeReload = await page.evaluate(() => window.__orderReloadEvents)
      assert.ok(item.eventsBeforeReload.some(e => e.type === 'dragstart' && e.trusted && e.card === expectedNames[0])
        && item.eventsBeforeReload.some(e => e.type === 'drop' && e.trusted && e.card === '甲号测试卡'), 'first reorder must use trusted native drag/drop on the intended cards')
      item.screenshots.push(`${profile.name}-before-reload.png`)
      await page.screenshot({ path: path.join(output, item.screenshots.at(-1)) })
      await page.reload({ waitUntil: 'domcontentloaded' })
      panel = page.locator('.prompt-choice-panel:visible'); await panel.waitFor()
      cards = panel.locator('.all-placement-row .prompt-card-candidate')
      item.promptRestored = await promptFacts()
      assert.deepEqual(item.promptRestored, item.promptBefore, 'reload must restore the same prompt identity and candidates')
      // The unsubmitted draft is client-local: remount starts from authoritative validChoices.
      assert.deepEqual(await names(cards), ['甲号测试卡', '乙号测试卡', '丙号测试卡'])
      assert.deepEqual(await cards.locator('.prompt-card-candidate__order').allInnerTexts(), ['1', '2', '3'])
      item.artworkRestored = await visibleArtwork(cards, `${profile.name}-restored`)
      await assertPanel(page, panel, `${profile.name}-restored`)
      item.screenshots.push(`${profile.name}-restored.png`)
      await page.screenshot({ path: path.join(output, item.screenshots.at(-1)) })
      await cards.nth(0).scrollIntoViewIfNeeded()
      await cards.nth(dragIndex).hover()
      await cards.nth(dragIndex).dragTo(cards.nth(0))
      assert.deepEqual(await names(cards), expectedNames)
      assert.match(await panel.locator('.prompt-action-footer .order-final-preview').innerText(), new RegExp(expectedNames.join('.*')))
      item.artworkReordered = await visibleArtwork(cards, `${profile.name}-reordered`)
      await assertPanel(page, panel, `${profile.name}-reordered`)
      item.screenshots.push(`${profile.name}-reordered.png`)
      await page.screenshot({ path: path.join(output, item.screenshots.at(-1)) })
      const confirm = panel.getByRole('button', { name: '全部放回顶部' })
      if (profile.mobile) {
        const box = await confirm.boundingBox(); assert.ok(box)
        await page.touchscreen.tap(box.x + box.width / 2, box.y + box.height / 2)
        await page.touchscreen.tap(box.x + box.width / 2, box.y + box.height / 2)
      } else await confirm.dblclick()
      item.commands = await page.evaluate(() => window.__sentCommands)
      assert.equal(item.commands.length, 1, 'repeated physical confirmation must produce exactly one fixture command')
      const command = await submitted(page)
      assert.equal(command.type, 'resolvePrompt')
      assert.equal(command.promptId, item.promptBefore.promptId)
      assert.equal(command.createdRevision, item.promptBefore.createdRevision)
      assert.equal(command.controller, item.promptBefore.controller)
      assert.deepEqual(command.topCardInstanceIds, expectedIds)
      assert.deepEqual(command.bottomCardInstanceIds, [])
      item.eventsRestored = await page.evaluate(() => window.__orderReloadEvents)
      assert.ok(item.eventsRestored.some(e => e.type === 'dragstart' && e.trusted && e.card === expectedNames[0])
        && item.eventsRestored.some(e => e.type === 'drop' && e.trusted && e.card === '甲号测试卡'), 'restored reorder must use trusted native drag/drop on the intended cards')
      if (profile.mobile) assert.equal(item.eventsRestored.filter(e => e.type === 'touchstart' && e.trusted).length, 2)
      assert.deepEqual(item.errors, [], 'no unknown browser errors may be ignored')
      assert.deepEqual(item.failedRequests, [], 'no request failures may be ignored')
      assert.deepEqual(item.httpErrors, [], 'no HTTP failures may be ignored')
      assert.deepEqual(item.blockedExternalRequests, [], 'fixture must not attempt external requests')
      item.passed = true
      results.push(profile.name)
    } catch (error) {
      item.passed = false; item.error = error.stack || String(error)
      item.eventsAtFailure = await page.evaluate(() => window.__orderReloadEvents)
      const screenshot = `${profile.name}-failure.png`
      await page.screenshot({ path: path.join(output, screenshot) }); item.screenshots.push(screenshot)
    } finally { cases.push(item); await context.close() }
  }
  const report = { schemaVersion: 1, status: cases.every(item => item.passed) ? 'passed' : 'failed',
    generatedAt: new Date().toISOString(), mode: 'reload-only', origin,
    source: { head: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repoRoot, encoding: 'utf8' }).trim(),
      scriptSha256: sha256(scriptPath), promptOverlaySha256: sha256(path.join(appRoot, 'src/l12/game/PromptOverlay.vue')),
      previewSha256: sha256(path.join(appRoot, 'scripts/preview-l12-battle-layout.mjs')) },
    fixtureAdapter: { before: fixtureBefore, after: fixtureAfter,
      localApiRoutes: { '/api/ranked/broadcasts/settings': broadcastSettings } },
    runtime: { temp: process.env.TEMP, tmp: process.env.TMP, tmpdir: process.env.TMPDIR },
    artwork: Object.entries(artwork).map(([cardId, url]) => ({ cardId, url, sha256: sha256(path.join(appRoot, 'public', url)) })),
    cases, limits: ['Current real GamePage/PromptOverlay/PromptCardCandidate/CardImage SFCs with sanitized public fixture prompts; not the unknown original private match or card/mode.',
      'Reload remounts the same fixture prompt, resets an unsubmitted client-local draft to validChoices, then accepts a new order. No real reconnect transport or server ACK is claimed.',
      'The fixture socket only captures outbound commands. Existing server recovery evidence is separate and is not rerun here.',
      'Mobile viewport HTML5 mouse dragging is not finger-drag or physical-device evidence; confirmation alone uses browser touchscreen taps.',
      'Exact existing accepted local artwork and controlled schema3 manifest; no production/CDN access or generic substitute artwork. Original 34 scenarios are unchanged and not run in this mode.'] }
  fs.writeFileSync(path.join(output, 'reload-report.json'), JSON.stringify(report, null, 2))
  console.log(`Stage2D reload-only: ${cases.filter(item => item.passed).length}/${cases.length} passed`)
  if (report.status !== 'passed') process.exitCode = 1
}

try {
  if (reloadOnly) await verifyReloadOnly()
  else {
  const page = await browser.newPage()
  page.setDefaultTimeout(9000)
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname)
    ? route.continue() : route.abort())
  const profiles = [
    { name: 'desktop-1280', width: 1280, height: 720 },
    { name: 'desktop-1366', width: 1366, height: 768 },
    { name: 'mobile-568', width: 568, height: 320, mobile: true },
    { name: 'mobile-390-rotated', width: 390, height: 844, mobile: true },
    { name: 'mobile-844-safe-font', width: 844, height: 390, mobile: true, safe: true, font: true },
  ]
  {
    const profile = { name: 'desktop-1366-native-drag', width: 1366, height: 768 }
    const panel = await view(page, profile, 'all-top-bottom')
    const cards = panel.locator('.all-placement-row .prompt-card-candidate')
    await cards.nth(2).dragTo(cards.nth(0))
    assert.deepEqual(await cards.locator('.prompt-card-candidate__name').allInnerTexts(),
      ['丙号测试卡', '甲号测试卡', '乙号测试卡'])
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 0,
      'native drag must not submit before explicit confirmation')
    await panel.getByRole('button', { name: '全部放回顶部' }).click()
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1,
      'native drag confirmation must submit exactly one command')
    assert.deepEqual((await submitted(page)).topCardInstanceIds,
      ['0-hand-showcase-2', '0-hand-showcase-0', '0-hand-showcase-1'])
    results.push('desktop-1366-native-drag')
  }
  for (const profile of profiles) {
    {
      const panel = await view(page, profile, 'trigger-order')
      const choices = panel.locator('.prompt-choices > button')
      assert.equal(await choices.count(), 3)
      for (const index of [1, 0, 2]) await choices.nth(index).click()
      assert.deepEqual(await choices.locator('.trigger-order-hint').allInnerTexts(),
        ['第2个发动 · 第2个结算', '第1个发动 · 第3个结算', '第3个发动 · 第1个结算'])
      const preview = panel.getByRole('region', { name: '确认前的实际结算顺序' })
      assert.deepEqual(await preview.locator('li').allInnerTexts(),
        ['丙号测试卡的触发效果', '甲号测试卡的触发效果', '乙号测试卡的触发效果'])
      assert.match(await panel.locator('.prompt-action-footer .order-final-preview').innerText(),
        /实际结算.*丙号测试卡.*甲号测试卡.*乙号测试卡/)
      await assertPanel(page, panel, `${profile.name}-trigger`)
      await page.screenshot({ path: path.join(output, `${profile.name}-trigger.png`) })
      await panel.getByRole('button', { name: '确认发动顺序' }).click()
      assert.deepEqual((await submitted(page)).cardInstanceIds, ['fixture-trigger-1', 'fixture-trigger-0', 'fixture-trigger-2'])
      results.push(`${profile.name}-trigger`)
    }
    for (const [mode, destination] of [['all-top-bottom', 'top'], ['all-top-bottom', 'bottom'], ['all-bottom', 'bottom']]) {
      const panel = await view(page, profile, mode)
      const cards = panel.locator('.all-placement-row .prompt-card-candidate')
      if (!profile.mobile) await assertHoverStable(page, panel, cards, `${profile.name}-${mode}-${destination}`)
      await cards.nth(0).click()
      await cards.nth(2).click()
      assert.deepEqual(await cards.locator('.prompt-card-candidate__name').allInnerTexts(),
        ['丙号测试卡', '乙号测试卡', '甲号测试卡'])
      assert.deepEqual(await cards.locator('.prompt-card-candidate__order').allInnerTexts(), ['1', '2', '3'])
      assert.match(await panel.locator('.placement-direction-hint').innerText(), /放回顶部时左侧最靠牌库顶.*先抽完牌库中的其他牌/)
      assert.match(await panel.locator('.prompt-action-footer .order-final-preview').innerText(),
        /丙号测试卡.*乙号测试卡.*甲号测试卡/)
      await assertPanel(page, panel, `${profile.name}-${mode}-${destination}`)
      await page.screenshot({ path: path.join(output, `${profile.name}-${mode}-${destination}.png`) })
      await panel.getByRole('button', { name: destination === 'top' ? '全部放回顶部' : '全部放回底部' }).click()
      const command = await submitted(page)
      assert.deepEqual(command.topCardInstanceIds, destination === 'top' ? ['0-hand-showcase-2', '0-hand-showcase-1', '0-hand-showcase-0'] : [])
      assert.deepEqual(command.bottomCardInstanceIds, destination === 'bottom' ? ['0-hand-showcase-2', '0-hand-showcase-1', '0-hand-showcase-0'] : [])
      results.push(`${profile.name}-${mode}-${destination}`)
    }
    {
      const panel = await view(page, profile, 'split-top-bottom')
      const unassigned = panel.locator('.placement-candidates .prompt-card-candidate')
      if (!profile.mobile) await assertHoverStable(page, panel, unassigned, `${profile.name}-split`)
      await unassigned.nth(0).click()
      await panel.getByRole('button', { name: '放回顶部' }).click()
      await unassigned.nth(0).click()
      await panel.getByRole('button', { name: '放回底部' }).click()
      await unassigned.nth(0).click()
      await panel.getByRole('button', { name: '放回底部' }).click()
      const bottom = panel.locator('.placement-destination.bottom .prompt-card-candidate')
      await bottom.nth(0).click()
      await bottom.nth(1).click()
      assert.deepEqual(await bottom.locator('.prompt-card-candidate__name').allInnerTexts(), ['丙号测试卡', '乙号测试卡'])
      assert.deepEqual(await bottom.locator('.prompt-card-candidate__order').allInnerTexts(), ['1', '2'])
      assert.match(await panel.locator('.placement-destination.bottom header').innerText(), /其他牌抽完后.*右侧最靠底/)
      assert.match(await panel.locator('.prompt-action-footer .order-final-preview').innerText(),
        /顶部先抽到.*甲号测试卡.*底部待其他牌抽完后.*丙号测试卡.*乙号测试卡/)
      await assertPanel(page, panel, `${profile.name}-split`)
      await page.screenshot({ path: path.join(output, `${profile.name}-split.png`) })
      await panel.getByRole('button', { name: '确认排列' }).click()
      const command = await submitted(page)
      assert.deepEqual(command.topCardInstanceIds, ['0-hand-showcase-0'])
      assert.deepEqual(command.bottomCardInstanceIds, ['0-hand-showcase-2', '0-hand-showcase-1'])
      results.push(`${profile.name}-split`)
    }
  }
  }
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ results }, null, 2))
  console.log(`Stage2D order direction: ${results.length} browser scenarios passed`)
} finally {
  await browser.close()
}
