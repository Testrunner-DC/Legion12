import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const origin = process.argv[2] || 'http://127.0.0.1:5191'
const output = process.env.L12_REPLAY_OVERLAY_OUT || path.resolve('..', 'artifacts', 'replay-controls-overlay')
fs.mkdirSync(output, { recursive: true })

const account = {
  id: 'replay-overlay-qa', username: '回放层级验收', role: 'player', permissions: [],
  mustChangeUsername: false, mustChangePassword: false, createdAt: '2026-09-25T00:00:00Z',
}
const card = (instanceId, cardId, name, cardType = 'legion') => ({
  InstanceId: instanceId, CardId: cardId, Name: name, CardType: cardType, Faction: 'otherworld',
  Cost: 2, BaseTroops: 3000, Troops: 3000, DisasterLevel: 0, Tapped: false, SummonRound: 1,
})
const player = index => ({
  PlayerIndex: index, Name: index ? '乙' : '甲', DeckName: '', Faction: index ? 'tianting' : 'otherworld',
  MasterId: `MASTER-${index}`, MasterName: index ? '对方主宰' : '我方主宰', Hp: 8, MaxHp: 10,
  FactionEffect: { CardId: `FACTION-${index}`, Name: index ? '天廷阵营效果' : '彼界阵营效果', EffectText: '只读回放详情', Abilities: [] },
  Library: [], Hand: [], MoraleDeck: [], Morale: [], Field: [[null, null, null], [null, null, null]],
  Graveyard: [], Resolving: [], ExtraRelics: [], SpecialZones: { Runes: 0, TrialLevel: 0, GodPower: [], Trials: [] },
  MulliganDone: true,
})
const baseState = (phase, events = []) => ({
  MatchId: 'replay-overlay-match', RoomCode: 'OVERLAY', ActivePlayer: 0, FirstPlayer: 0, DiceWinner: 0,
  InitiativeRolls: [6, 2], Phase: phase, Round: 2, DisasterMode: 'all', DisasterValue: 1,
  DisasterPreparationStep: phase === 'DisasterPreparation' ? 1 : 0,
  Players: [player(0), player(1)], Events: events, Winner: phase === 'GameOver' ? 0 : null,
  BannedDisasters: [], RevealedDisasters: [], ChosenDisasters: [], SessionDisasters: [], DisasterDeck: [],
})
const command = (sequence, phase, events = []) => ({
  sequence, receivedUtc: '2026-09-25T00:00:00Z', playerIndex: sequence % 2,
  command: { type: sequence === 2 ? 'activateAbility' : 'passPriority', sourceInstanceId: sequence === 2 ? 'source-1' : undefined },
  accepted: true, revision: sequence, stateHash: `hash-${sequence}`, state: baseState(phase, events),
})
const shownCard = card('source-1', 'SOURCE-1', '展示测试卡')
const detail = {
  match: {
    matchId: 'replay-overlay-match', roomCode: 'OVERLAY', player0: '甲', player1: '乙', deck0: '', deck1: '',
    startedUtc: '2026-09-25T00:00:00Z', endedUtc: '2026-09-25T00:10:00Z', winner: 0, commandCount: 4,
  },
  viewerPlayerIndex: 0,
  commands: [
    command(1, 'Main'),
    command(2, 'Main', [{ Sequence: 2, Type: 'effect-result', PlayerIndex: 1, Text: '对方展示测试卡', Cards: [shownCard] }]),
    command(3, 'DisasterPreparation'),
    command(4, 'GameOver'),
  ],
}

async function seed(context) {
  await context.addInitScript(({ account }) => {
    localStorage.setItem('l12-account', JSON.stringify(account))
    localStorage.setItem('l12-auth-token', 'replay-overlay-token')
    localStorage.setItem('l12-nickname', account.username)
  }, { account })
}

function apiBody(pathname) {
  if (pathname === '/api/auth/me') return account
  if (pathname === `/api/matches/${detail.match.matchId}`) return detail
  if (pathname === '/api/friends/overview') return { friends: [], requests: [], blocked: [] }
  if (pathname === '/api/ranked/integrity/notifications') return { items: [], nextCursor: null }
  if (pathname === '/api/me/alternate-art-grant-notifications') return []
  return { message: 'not mocked' }
}

async function mockApi(page) {
  await page.route('**/api/**', route => {
    const url = new URL(route.request().url())
    const body = apiBody(url.pathname)
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
}

async function hitTarget(page, locator) {
  return locator.evaluate(element => {
    const rect = element.getBoundingClientRect()
    const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
    return {
      targetText: element.textContent?.trim(), targetClass: element.className,
      hitTag: hit?.tagName, hitClass: typeof hit?.className === 'string' ? hit.className : '', hitText: hit?.textContent?.trim(),
      contained: hit === element || element.contains(hit),
      rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom },
    }
  })
}

async function controlAudit(page) {
  return page.locator('.replay-controls').evaluate(element => {
    const controls = [...element.querySelectorAll('button')]
    const rect = element.getBoundingClientRect()
    return {
      visible: rect.width > 0 && rect.height > 0,
      clipped: rect.left < 0 || rect.top < 0 || rect.right > innerWidth || rect.bottom > innerHeight,
      overflowX: document.documentElement.scrollWidth > innerWidth + 1,
      buttons: controls.map(button => {
        const box = button.getBoundingClientRect()
        const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2)
        return {
          text: button.textContent?.trim(), enabled: !button.disabled,
          hit: hit === button || button.contains(hit),
          clipped: box.left < 0 || box.top < 0 || box.right > innerWidth || box.bottom > innerHeight,
        }
      }),
    }
  })
}

async function loadReplayPage(page) {
  await mockApi(page)
  await page.goto(`${origin}/battle/records/replay/${detail.match.matchId}`, { waitUntil: 'networkidle' })
  await page.locator('.replay-controls').waitFor()
}

async function moveToStep(page, target) {
  const progress = page.locator('.replay-controls small')
  for (let guard = 0; guard < 60; guard++) {
    const current = Number((await progress.textContent())?.match(/步骤\s+(\d+)/)?.[1] ?? 1) - 1
    if (current === target) return
    const label = current < target ? '下一步' : '上一步'
    const button = page.getByRole('button', { name: label, exact: true })
    await button.waitFor()
    if (!await button.isEnabled()) await page.waitForTimeout(100)
    else await button.click()
  }
  throw new Error(`could not move replay to step ${target}`)
}

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  const context = await browser.newContext({ viewport: { width: 1366, height: 768 } })
  await seed(context)
  const page = await context.newPage()
  page.setDefaultTimeout(10_000)
  const pageErrors = []
  page.on('pageerror', error => pageErrors.push(error.stack ?? error.message))
  await loadReplayPage(page)
  const controls = page.locator('.replay-controls')

  const baseline = await controlAudit(page)
  assert.equal(baseline.visible, true)
  assert.equal(baseline.clipped, false)
  assert.equal(baseline.overflowX, false)
  assert(baseline.buttons.filter(button => button.enabled).every(button => button.hit), `baseline controls are intercepted: ${JSON.stringify(baseline)}`)

  const speed2 = page.getByRole('button', { name: '2.0', exact: true })
  await speed2.click()
  assert.equal(await speed2.getAttribute('aria-pressed'), 'true', 'speed click must update the selected speed')
  await page.getByRole('button', { name: '播放', exact: true }).click()
  await page.getByRole('button', { name: '暂停', exact: true }).waitFor()
  await page.getByRole('button', { name: '暂停', exact: true }).click()
  await page.getByRole('button', { name: '播放', exact: true }).waitFor()

  await moveToStep(page, 1)
  await page.locator('.public-reveal-animation').waitFor()
  const animationAudit = await controlAudit(page)
  assert(animationAudit.buttons.filter(button => button.enabled).every(button => button.hit), `card animation intercepts controls: ${JSON.stringify(animationAudit)}`)
  assert.equal(await page.getByRole('button', { name: '下一步', exact: true }).isEnabled(), false, 'card presentation must keep step advance disabled until it completes')
  await page.screenshot({ path: path.join(output, 'card-animation-controls-1366x768.png') })
  await page.locator('.public-reveal-animation').waitFor({ state: 'detached' })
  assert.equal(await page.getByRole('button', { name: '下一步', exact: true }).isEnabled(), true, 'next step must recover after card presentation completes')
  await page.locator('.resource-faction-action').last().click()
  await page.locator('.faction-effect-overlay').waitFor()
  assert((await controlAudit(page)).buttons.filter(button => button.enabled).every(button => button.hit), 'card detail must not intercept replay controls')
  await page.locator('.faction-effect-overlay').getByRole('button', { name: '关闭', exact: true }).click()
  await page.locator('.faction-effect-overlay').waitFor({ state: 'detached' })

  await page.getByRole('button', { name: '下一步', exact: true }).click()
  await page.locator('.l12-prompt-overlay').waitFor()
  const play = page.getByRole('button', { name: '播放', exact: true })
  const diagnosis = {
    teleportZ: await page.locator('#l12-landscape-teleports').evaluate(element => getComputedStyle(element).zIndex),
    controlZ: await controls.evaluate(element => getComputedStyle(element).zIndex),
    promptZ: await page.locator('.l12-prompt-overlay').evaluate(element => getComputedStyle(element).zIndex),
    playHit: await hitTarget(page, play),
  }
  fs.writeFileSync(path.join(output, 'stacking-diagnosis.json'), JSON.stringify(diagnosis, null, 2))
  await page.screenshot({ path: path.join(output, 'disaster-preparation-controls-1366x768.png') })
  assert.equal(diagnosis.playHit.contained, true, `replay play control is intercepted: ${JSON.stringify(diagnosis)}`)

  await moveToStep(page, 0)
  const tools = page.getByRole('button', { name: '打开对局工具', exact: true })
  await tools.click()
  await page.getByRole('dialog', { name: '对局工具' }).waitFor()
  const blockedHit = await hitTarget(page, page.getByRole('button', { name: '播放', exact: true }))
  assert.equal(blockedHit.contained, false, 'a true blocking dialog must stay above replay controls')
  await assert.rejects(
    page.getByRole('button', { name: '播放', exact: true }).click({ timeout: 700 }),
    /intercepts pointer events|Timeout/,
    'a true blocking dialog must reject a real click on replay controls',
  )
  await page.screenshot({ path: path.join(output, 'blocking-dialog-1366x768.png') })
  await page.getByRole('button', { name: '关闭对局工具', exact: true }).click()

  await moveToStep(page, 3)
  await page.getByText('对局结束', { exact: true }).waitFor()
  const endAudit = await controlAudit(page)
  assert(endAudit.buttons.filter(button => button.enabled).every(button => button.hit), `end state controls are intercepted: ${JSON.stringify(endAudit)}`)
  await page.getByRole('button', { name: '播放', exact: true }).click()
  assert.match((await page.locator('.replay-controls small').textContent()) ?? '', /步骤 1 \/ 4/, 'playing at replay end must restart from step one')
  await page.getByRole('button', { name: '暂停', exact: true }).click()
  await page.screenshot({ path: path.join(output, 'replay-ended-controls-1366x768.png') })
  assert.deepEqual(pageErrors, [], `page errors: ${pageErrors.join(' | ')}`)
  await context.close()

  const matrix = []
  const viewports = [
    { width: 1920, height: 1080 }, { width: 1366, height: 768 },
    { width: 1280, height: 720 }, { width: 2560, height: 1440 },
  ]
  for (const viewport of viewports) {
    for (const zoom of [0.8, 1, 1.25]) {
      const matrixContext = await browser.newContext({ viewport })
      await seed(matrixContext)
      const matrixPage = await matrixContext.newPage()
      matrixPage.setDefaultTimeout(10_000)
      await loadReplayPage(matrixPage)
      await matrixPage.locator('html').evaluate((element, value) => { element.style.zoom = String(value) }, zoom)
      await moveToStep(matrixPage, 2)
      await matrixPage.locator('.l12-prompt-overlay').waitFor()
      const audit = await controlAudit(matrixPage)
      assert.equal(audit.visible, true, `controls hidden at ${viewport.width}x${viewport.height} ${zoom}`)
      assert.equal(audit.clipped, false, `controls clipped at ${viewport.width}x${viewport.height} ${zoom}`)
      assert.equal(audit.overflowX, false, `horizontal page overflow at ${viewport.width}x${viewport.height} ${zoom}`)
      assert(audit.buttons.filter(button => button.enabled).every(button => button.hit), `control hit failed at ${viewport.width}x${viewport.height} ${zoom}: ${JSON.stringify(audit)}`)
      const speed3 = matrixPage.getByRole('button', { name: '3.0', exact: true })
      await speed3.click()
      assert.equal(await speed3.getAttribute('aria-pressed'), 'true', `real speed click failed at ${viewport.width}x${viewport.height} ${zoom}`)
      const file = `prompt-${viewport.width}x${viewport.height}-zoom-${Math.round(zoom * 100)}.png`
      await matrixPage.screenshot({ path: path.join(output, file) })
      matrix.push({ viewport, zoom, file, audit })
      await matrixContext.close()
    }
  }

  const loadingContext = await browser.newContext({ viewport: { width: 1366, height: 768 } })
  await seed(loadingContext)
  const loadingPage = await loadingContext.newPage()
  let releaseMatch
  const matchGate = new Promise(resolve => { releaseMatch = resolve })
  await loadingPage.route('**/api/**', async route => {
    const url = new URL(route.request().url())
    if (url.pathname === `/api/matches/${detail.match.matchId}`) await matchGate
    const body = apiBody(url.pathname)
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  const navigation = loadingPage.goto(`${origin}/battle/records/replay/${detail.match.matchId}`, { waitUntil: 'domcontentloaded' })
  await loadingPage.locator('.replay-loading').waitFor()
  assert.equal(await loadingPage.locator('.replay-controls').count(), 0, 'controls must stay unavailable until replay data is loaded')
  await loadingPage.screenshot({ path: path.join(output, 'loading-blocker-1366x768.png') })
  releaseMatch()
  await navigation
  await loadingPage.locator('.replay-controls').waitFor()
  await loadingContext.close()

  fs.writeFileSync(path.join(output, 'manifest.json'), JSON.stringify({
    generatedAt: new Date().toISOString(), status: 'passed',
    rootCause: 'replay controls outside #l12-landscape-teleports could not overtake its z-index:100000 stacking context',
    matrix,
    states: ['loading', 'paused', 'playing', 'speed', 'previous', 'next', 'progress', 'card-animation', 'card-detail', 'ordinary-prompt', 'blocking-dialog', 'replay-ended'],
  }, null, 2))
} finally {
  await browser.close()
}

console.log(`Replay overlay control verification passed. Evidence: ${output}`)
