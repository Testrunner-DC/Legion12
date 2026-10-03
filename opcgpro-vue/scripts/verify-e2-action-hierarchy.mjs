import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2] || 'http://127.0.0.1:5297/__l12_battle_preview__'
const output = process.env.L12_E2_ACTION_OUT
assert(output && path.isAbsolute(output), 'L12_E2_ACTION_OUT must be a fresh absolute path')
assert(!fs.existsSync(output), `Refusing existing output directory: ${output}`)
fs.mkdirSync(output, { recursive: true })

const profiles = [
  { name: 'desktop-1920x1080', width: 1920, height: 1080, mobile: false },
  { name: 'desktop-2560x1440', width: 2560, height: 1440, mobile: false },
  { name: 'desktop-3440x1440', width: 3440, height: 1440, mobile: false },
  { name: 'mobile-844x390', width: 844, height: 390, mobile: true },
  { name: 'mobile-740x360', width: 740, height: 360, mobile: true },
  { name: 'mobile-568x320', width: 568, height: 320, mobile: true },
  { name: 'mobile-390x844', width: 390, height: 844, mobile: true },
  { name: 'mobile-360x640', width: 360, height: 640, mobile: true },
  { name: 'mobile-320x568', width: 320, height: 568, mobile: true },
  { name: 'mobile-safe-short-640x320', width: 640, height: 320, mobile: true, safe: true },
]
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const sourceFiles = ['GameActions.vue', 'GameBoard.vue', 'PlayerMat.vue', 'PromptOverlay.vue', 'battleActionPresentation.ts']
const sourceHashes = Object.fromEntries(sourceFiles.map(file => [file,
  crypto.createHash('sha256').update(fs.readFileSync(path.join(root, 'src/l12/game', file))).digest('hex')]))
const report = { schema: 1, status: 'running', startedAt: new Date().toISOString(), sourceHashes, base, profiles: [], interactions: [], errors: [] }
const writeReport = () => {
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  fs.writeFileSync(path.join(output, 'report.md'), [
    '# E2 action hierarchy browser verification',
    '', `Status: ${report.status}`, `Profiles: ${report.profiles.length}`, `Interactions: ${report.interactions.length}`,
    '', ...report.profiles.map(item => `- ${item.name}: max rect delta ${item.maxRectDelta}px; clock clipping ${item.clockClipping}; max overlap ${item.maxOverlap}px`),
    '', ...report.interactions.map(item => `- ${item.name}: ${item.result}`),
    ...(report.errors.length ? ['', '## Errors', ...report.errors.map(error => `- ${error}`)] : []),
  ].join('\n'))
}
writeReport()

function query(profile, extra) {
  const params = new URLSearchParams({ field: 'full', hand: '40', markers: '5', piles: '40', rankedClock: '1', totalMs: '359999999', operationMs: '359999999', disconnected: '1', reconnectMs: '359999999' })
  if (profile.mobile) { params.set('mobile', '1'); params.set('canvas', '1') }
  if (profile.safe) params.set('safe', '1')
  for (const [key, value] of new URLSearchParams(extra)) params.set(key, value)
  return params.toString()
}

async function load(page, profile, extra = '') {
  await page.setViewportSize({ width: profile.width, height: profile.height })
  await page.goto(`${base}?${query(profile, extra)}`, { waitUntil: 'domcontentloaded' })
  await page.locator('.game-page').waitFor()
  await page.waitForTimeout(80)
}

async function geometry(page) {
  return page.evaluate(() => {
    const rect = selector => {
      const element = document.querySelector(selector)
      if (!element) return null
      const box = element.getBoundingClientRect()
      return { x: box.x, y: box.y, width: box.width, height: box.height, right: box.right, bottom: box.bottom }
    }
    const overlap = (a, b) => a && b ? Math.max(0, Math.min(a.right, b.right) - Math.max(a.x, b.x))
      * Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.y, b.y)) : 0
    const clocks = [...document.querySelectorAll('.board-player-clock')].map(element => ({
      text: element.textContent?.replace(/\s+/g, ' ').trim() ?? '',
      clipped: element.scrollWidth > element.clientWidth + 1 || element.scrollHeight > element.clientHeight + 1,
    }))
    const route = rect('.battle-route-controls')
    const prompt = rect('.inline-prompt-controls')
    const record = rect('.mobile-record-trigger')
    return {
      rects: {
        stage: rect('[data-l12-game-stage]'), felt: rect('.felt-board'), enemy: rect('.opponent-half'), me: rect('.my-half'),
        rail: rect('.right-rail'), enemyClock: rect('.opponent-player-clock'), myClock: rect('.my-player-clock'), route,
      },
      clocks,
      overlaps: [overlap(route, prompt), overlap(route, record), overlap(prompt, record)],
      viewport: { width: innerWidth, height: innerHeight },
    }
  })
}

function maxRectDelta(before, after) {
  let max = 0
  for (const key of Object.keys(before.rects)) {
    const left = before.rects[key]
    const right = after.rects[key]
    if (!left || !right) continue
    for (const field of ['x', 'y', 'width', 'height']) max = Math.max(max, Math.abs(left[field] - right[field]))
  }
  return Math.round(max * 100) / 100
}

async function centerHit(page, selector) {
  return page.locator(selector).first().evaluate(element => {
    const box = element.getBoundingClientRect()
    const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)
    return Boolean(hit && (hit === element || element.contains(hit)))
  })
}

async function sent(page) {
  return page.evaluate(() => window.__sentCommands?.at(-1)?.command ?? null)
}

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  for (const profile of profiles) {
    const context = await browser.newContext({ viewport: { width: profile.width, height: profile.height }, hasTouch: profile.mobile })
    const page = await context.newPage()
    page.setDefaultTimeout(12_000)
    const pageErrors = []
    page.on('pageerror', error => pageErrors.push(error.message))
    await load(page, profile)
    const before = await geometry(page)
    assert.equal(before.clocks.length, 2, `${profile.name}: both ranked clocks visible`)
    assert(before.clocks.every(clock => clock.text.includes('总时'))
      && before.clocks.some(clock => clock.text.includes('本次'))
      && before.clocks.some(clock => clock.text.includes('重连')), `${profile.name}: expanded ranked clock facts missing ${JSON.stringify(before.clocks)}`)
    assert(before.clocks.every(clock => !clock.clipped), `${profile.name}: expanded ranked clock clipped`)
    assert(await centerHit(page, '.battle-route-controls [aria-label="返回大厅"]'), `${profile.name}: return button is not topmost`)
    assert(await centerHit(page, '.battle-route-controls .surrender'), `${profile.name}: surrender button is not topmost`)

    await load(page, profile, 'board-target=1&inline-rich=1')
    const after = await geometry(page)
    const delta = maxRectDelta(before, after)
    assert(delta <= 1, `${profile.name}: board geometry changed by ${delta}px`)
    assert(Math.max(...after.overlaps) <= 1, `${profile.name}: action controls collide with route/record controls`)
    const prompt = page.locator('.inline-prompt-controls:visible')
    const text = await prompt.innerText()
    for (const expected of ['你的操作', '指定效果目标']) assert(text.includes(expected), `${profile.name}: missing ${expected}`)
    if (!profile.mobile) for (const expected of ['需选择 1 至 2 项', '已选择 0/2']) assert(text.includes(expected), `${profile.name}: missing ${expected}`)
    assert(await centerHit(page, '.formation-slot.targetable'), `${profile.name}: legal target is not topmost`)
    const legalStyle = await page.locator('.formation-slot.targetable').first().evaluate(element => ({
      border: getComputedStyle(element).borderColor, background: getComputedStyle(element).backgroundColor,
    }))
    const quietStyle = await page.locator('.formation-slot:not(.targetable):not(.available):not(.payment-resource)').first().evaluate(element => ({
      border: getComputedStyle(element).borderColor, background: getComputedStyle(element).backgroundColor,
    }))
    assert.notDeepEqual(legalStyle, quietStyle, `${profile.name}: legal target is not visually distinct`)
    await page.locator('.formation-slot.targetable').first().click()
    if (profile.mobile) {
      await prompt.getByRole('button', { name: '任务说明' }).click()
      const infoText = await page.locator('.inline-prompt-info-overlay').innerText()
      for (const expected of ['你的操作', '需选择 1 至 2 项', '已选择 1/2']) assert(infoText.includes(expected), `${profile.name}: task sheet missing ${expected}`)
      await page.locator('.inline-prompt-info-overlay').getByRole('button', { name: '返回选择' }).click()
      await prompt.getByRole('button', { name: '最小化' }).click()
      assert(await centerHit(page, '.board-control-restore'), `${profile.name}: restore control is not topmost`)
      await page.locator('.board-control-restore').click()
    }
    await prompt.getByRole('button', { name: '确认目标' }).click()
    const payload = await sent(page)
    assert.equal(payload?.type, 'resolvePrompt', `${profile.name}: target command type`)
    assert.equal(payload?.cardInstanceIds?.length, 1, `${profile.name}: target payload length`)

    if (['desktop-1920x1080', 'mobile-844x390', 'mobile-320x568'].includes(profile.name))
      await page.screenshot({ path: path.join(output, `${profile.name}-target.png`) })
    assert.deepEqual(pageErrors, [], `${profile.name}: page errors`)
    report.profiles.push({ name: profile.name, maxRectDelta: delta, clockClipping: before.clocks.filter(clock => clock.clipped).length, maxOverlap: Math.max(...after.overlaps) })
    await context.close()
    writeReport()
  }

  const context = await browser.newContext({ viewport: { width: 844, height: 390 }, hasTouch: true })
  const page = await context.newPage()
  page.setDefaultTimeout(12_000)
  const mobile = profiles.find(profile => profile.name === 'mobile-844x390')

  await load(page, mobile, 'field=empty&board-slot=1&slot-opponent=1&inline-rich=1')
  const slot = page.locator('.inline-prompt-controls')
  assert((await slot.innerText()).includes('点击绿色高亮格位即提交'), 'slot prompt must state direct submission')
  await page.locator('.formation-slot.available').first().click()
  let payload = await sent(page)
  assert.equal(payload?.type, 'resolvePrompt')
  assert.match(payload?.choice ?? '', /^\d+:\d+$/)
  report.interactions.push({ name: 'board-slot-direct-submit', result: JSON.stringify(payload) })

  await load(page, mobile, 'morale=5&morale-payment=1&payment-actions=1&inline-rich=1')
  await page.locator('.inline-prompt-controls').getByRole('button', { name: '任务说明' }).click()
  let infoText = await page.locator('.inline-prompt-info-overlay').innerText()
  assert(infoText.includes('待支付') && !infoText.includes('费用已支付'), 'pending payment must not appear paid')
  await page.locator('.inline-prompt-info-overlay').getByRole('button', { name: '返回选择' }).click()
  await page.locator('.my-half .resource-morale-summary').click()
  const picker = page.locator('.mobile-morale-overlay')
  await picker.locator('.mobile-morale-choice[aria-disabled="false"]').nth(0).click()
  await picker.locator('.mobile-morale-choice[aria-disabled="false"]').nth(1).click()
  await picker.getByRole('button', { name: '确认支付' }).click()
  payload = await sent(page)
  assert.equal(payload?.cardInstanceIds?.length, 2)
  report.interactions.push({ name: 'resource-payment', result: JSON.stringify(payload) })

  await load(page, mobile, 'morale=5&morale-payment=1&payment-actions=1&inline-rich=1&payment-paid=1')
  await page.locator('.inline-prompt-controls').getByRole('button', { name: '任务说明' }).click()
  infoText = await page.locator('.inline-prompt-info-overlay').innerText()
  assert(infoText.includes('已支付') && !infoText.includes('待支付'), 'paid payment must not appear pending')
  report.interactions.push({ name: 'payment-paid-separation', result: '费用已支付且未显示待支付' })

  await load(page, mobile, 'response-fixture=1')
  const overlay = page.locator('.prompt-choice-panel')
  assert((await overlay.innerText()).includes('你的操作') && (await overlay.innerText()).includes('需选择 1 项 · 已选择 0/1'))
  await overlay.getByRole('button', { name: /短来源/ }).click()
  await overlay.getByRole('button', { name: '最小化弹框' }).click()
  assert(await centerHit(page, '.prompt-minimized-bar button'), 'response restore must be topmost')
  await page.locator('.prompt-minimized-bar button').click()
  await page.locator('.prompt-action-footer .primary').click()
  payload = await sent(page)
  assert.equal(payload?.type, 'resolvePrompt')
  assert.deepEqual(payload?.cardInstanceIds, ['stack-a'])
  report.interactions.push({ name: 'response-minimize-restore-submit', result: JSON.stringify(payload) })

  await load(page, mobile, 'card-choice=1&choice-count=12')
  const body = page.locator('.prompt-choice-body')
  assert(await body.evaluate(element => element.scrollHeight >= element.clientHeight && element.scrollWidth >= element.clientWidth), 'card choice body must remain scroll-capable')
  await page.locator('.prompt-card-candidate').first().click()
  await page.locator('.prompt-action-footer .primary').click()
  payload = await sent(page)
  assert.equal(payload?.type, 'resolvePrompt')
  report.interactions.push({ name: 'card-choice-submit', result: JSON.stringify(payload) })

  await load(page, mobile)
  await page.getByRole('button', { name: '对局记录', exact: true }).click()
  await page.locator('.mobile-record-overlay').getByRole('button', { name: '最小化' }).click()
  assert(await centerHit(page, '.mobile-record-restore'), 'record restore must be topmost')
  await page.locator('.mobile-record-restore').click()
  await page.locator('.mobile-record-overlay').getByRole('button', { name: '关闭' }).click()
  page.once('dialog', dialog => dialog.dismiss())
  await page.getByRole('button', { name: '投降', exact: true }).click()
  assert(await centerHit(page, '.battle-route-controls [aria-label="返回大厅"]'), 'return remains clickable after surrender dismissal')
  report.interactions.push({ name: 'record-surrender-return', result: '记录最小化/恢复/关闭和投降实点；返回大厅保持顶层可点' })

  await load(page, mobile, 'information-contract=1&referee=1')
  await page.evaluate(() => { window.__l12State.game.phase = 'DisasterPreparation' })
  const readonly = page.locator('.prompt-choice-panel')
  await readonly.waitFor()
  assert((await readonly.innerText()).includes('当前玩家正在选择'), 'read-only actor must be neutral')
  const readonlyText = await readonly.innerText()
  assert(!readonlyText.includes('私密昵称'), 'read-only hierarchy must not reveal private names')
  report.interactions.push({ name: 'readonly-neutral', result: '当前玩家正在选择；无私人名' })

  await load(page, mobile)
  await page.evaluate(() => {
    window.__l12State.game.prompts = []
    window.__l12State.game.waitingPrompt = { playerIndex: 1, kind: 'response', waitingSummary: '对手正在选择是否响应' }
  })
  const waitingActions = page.locator('.l12-actions')
  await waitingActions.getByText('对手操作', { exact: true }).waitFor()
  assert.equal(await waitingActions.getByRole('button', { name: '结束回合' }).count(), 0, 'blocking opponent prompt must remove ordinary action submission')
  report.interactions.push({ name: 'waiting-actor', result: '对手操作；普通结束回合不呈现' })

  await load(page, mobile, 'gm=1&information-contract=1')
  await page.evaluate(() => { window.__l12State.game.prompts[0].playerIndex = 1 })
  const gmPrompt = page.locator('.prompt-choice-panel')
  await gmPrompt.getByText('你的操作', { exact: true }).waitFor()
  report.interactions.push({ name: 'gm-controlled-actor', result: '沙盒控制prompt.playerIndex=1时仍显示你的操作' })

  await load(page, mobile, 'clockPhase=Mulligan')
  await page.evaluate(() => { window.__l12State.game.players[0].mulliganDone = true })
  const mulliganActions = page.locator('.l12-actions')
  await mulliganActions.getByText('对手操作', { exact: true }).waitFor()
  assert(await mulliganActions.getByRole('button', { name: '等待对方' }).isDisabled(), 'submitted mulligan must stay disabled while waiting')
  report.interactions.push({ name: 'mulligan-submitted-actor', result: '对手操作；等待对方按钮禁用' })

  await context.close()
  for (const file of sourceFiles) assert.equal(crypto.createHash('sha256')
    .update(fs.readFileSync(path.join(root, 'src/l12/game', file))).digest('hex'), sourceHashes[file], `source changed during verification: ${file}`)
  report.completedAt = new Date().toISOString()
  report.status = 'passed'
} catch (error) {
  report.status = 'failed'
  report.errors.push(error instanceof Error ? `${error.stack ?? error.message}` : String(error))
  throw error
} finally {
  await browser.close()
  writeReport()
}

console.log(`E2 action hierarchy browser verification passed: ${report.profiles.length} profiles, ${report.interactions.length} interactions`)
