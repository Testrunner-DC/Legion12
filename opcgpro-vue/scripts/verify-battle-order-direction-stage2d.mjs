import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const origin = process.argv[2] || 'http://127.0.0.1:5242/__l12_battle_preview__'
const output = path.resolve('artifacts/stage2d-order-direction')
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

try {
  const page = await browser.newPage()
  page.setDefaultTimeout(9000)
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname)
    ? route.continue() : route.abort())
  const profiles = [
    { name: 'desktop-1280', width: 1280, height: 720 },
    { name: 'mobile-568', width: 568, height: 320, mobile: true },
    { name: 'mobile-390-rotated', width: 390, height: 844, mobile: true },
    { name: 'mobile-844-safe-font', width: 844, height: 390, mobile: true, safe: true, font: true },
  ]
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
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ results }, null, 2))
  console.log(`Stage2D order direction: ${results.length} browser scenarios passed`)
} finally {
  await browser.close()
}
