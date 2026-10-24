import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2]
const output = process.env.L12_ATTACK_CONNECTION_OUT
assert.ok(base, 'preview URL is required')
assert.ok(output && path.isAbsolute(output), 'L12_ATTACK_CONNECTION_OUT must be an absolute controlled path')
assert.equal(fs.existsSync(output), false, `refusing to overwrite evidence: ${output}`)
fs.mkdirSync(output, { recursive: true })

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []

async function pageFor(viewport, reducedMotion = 'no-preference') {
  const context = await browser.newContext({ viewport, reducedMotion })
  const page = await context.newPage()
  const errors = []
  page.setDefaultTimeout(10_000)
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => {
    if (message.type() === 'warning' && !message.text().includes('[Vue Router warn]')) errors.push(message.text())
  })
  await page.addInitScript(() => {
    window.__attackDrawStarts = 0
    const observer = new MutationObserver(records => {
      for (const record of records) for (const node of record.addedNodes) {
        if (!(node instanceof Element)) continue
        if (node.matches('.attack-connection-layer.is-intro') || node.querySelector('.attack-connection-layer.is-intro'))
          window.__attackDrawStarts++
      }
    })
    observer.observe(document, { childList: true, subtree: true })
  })
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname)
    ? route.continue() : route.abort())
  return { context, page, errors }
}

async function declare(page, { attackerPlayer = 0, master = false } = {}) {
  return page.evaluate(({ attackerPlayer, master }) => {
    const game = window.__l12State.game
    const attacker = game.players[attackerPlayer].field.flat().find(Boolean)
    const defender = game.players[1 - attackerPlayer]
    const target = defender.field.flat().find(Boolean)
    const sequence = Math.max(0, ...game.recentEvents.map(event => event.sequence)) + 1
    game.phase = 'Defense'
    game.activePlayer = attackerPlayer
    game.pendingDefense = {
      attackerPlayer,
      attackerInstanceId: attacker.instanceId,
      target: master ? { type: 'master' } : { type: 'legion', instanceId: target.instanceId },
      stage: 'DefenseChoice',
      attackValue: attacker.troops || 3000,
    }
    game.revision++
    game.recentEvents = [...game.recentEvents, {
      sequence,
      type: 'attack',
      playerIndex: attackerPlayer,
      text: '进攻连接线验收',
      playerCombat: {
        combatId: `qa-${sequence}`,
        eventKind: 'attack',
        outcomeCode: 'declared',
        attackerInstanceId: attacker.instanceId,
        targetInstanceId: master ? undefined : target.instanceId,
      },
      cards: master ? [attacker] : [attacker, target],
    }]
    return { matchId: game.matchId, sequence, attackerId: attacker.instanceId, targetId: master ? `master-${1 - attackerPlayer}` : target.instanceId }
  }, { attackerPlayer, master })
}

async function inspect(page, expected) {
  const layer = page.locator('[data-ui-contract="authoritative-attack-connection"]')
  await layer.waitFor()
  return layer.evaluate((element, expected) => {
    const source = document.querySelector(`[data-l12-game-stage] [data-card-instance-id="${CSS.escape(expected.attackerId)}"]`)
    const target = document.querySelector(`[data-l12-game-stage] [data-card-instance-id="${CSS.escape(expected.targetId)}"]`)
    const sourceRect = source.getBoundingClientRect()
    const targetRect = target.getBoundingClientRect()
    const sourceDot = element.querySelector('.attack-connection-source')
    const targetDot = element.querySelector('.attack-connection-target')
    const core = element.querySelector('.attack-connection-core')
    const glow = element.querySelector('.attack-connection-glow')
    const orb = element.querySelector('.attack-connection-orb')
    const occluder = element.querySelector('.attack-connection-occluder')
    const combat = Array.from(document.querySelectorAll('.combat-versus'))
      .find(node => node instanceof HTMLElement && node.getClientRects().length > 0)
    const number = (node, attribute) => Number(node.getAttribute(attribute))
    const logicalSource = { x: number(sourceDot, 'cx'), y: number(sourceDot, 'cy') }
    const logicalTarget = { x: number(targetDot, 'cx'), y: number(targetDot, 'cy') }
    const viewportMode = document.documentElement.dataset.l12Viewport
    const rotated = document.documentElement.dataset.l12Rotated === 'true'
    return {
      key: element.dataset.attackPresentationKey,
      path: core.getAttribute('d'),
      marker: core.getAttribute('marker-end'),
      pointerEvents: getComputedStyle(element).pointerEvents,
      opacity: getComputedStyle(element).opacity,
      intro: element.classList.contains('is-intro'),
      resolving: element.classList.contains('is-resolving'),
      sourceColor: getComputedStyle(sourceDot).color,
      coreWidth: getComputedStyle(core).strokeWidth,
      glowWidth: getComputedStyle(glow).strokeWidth,
      coreMask: core.getAttribute('mask'),
      glowMask: glow.getAttribute('mask'),
      orbMask: orb?.getAttribute('mask') ?? null,
      occluder: occluder ? {
        x: number(occluder, 'x'), y: number(occluder, 'y'),
        width: number(occluder, 'width'), height: number(occluder, 'height'),
      } : null,
      combatRect: combat ? (() => {
        const rect = combat.getBoundingClientRect()
        return { left: rect.left, top: rect.top, width: rect.width, height: rect.height }
      })() : null,
      logicalSource,
      logicalTarget,
      sourceRect: { left: sourceRect.left, top: sourceRect.top, right: sourceRect.right, bottom: sourceRect.bottom },
      targetRect: { left: targetRect.left, top: targetRect.top, right: targetRect.right, bottom: targetRect.bottom },
      viewportMode,
      rotated,
      drawStarts: window.__attackDrawStarts,
    }
  }, expected)
}

const profiles = [
  { name: 'desktop-player', viewport: { width: 1440, height: 900 }, query: 'canvas=1&field=full', attackerPlayer: 0 },
  { name: 'mobile-844x390-opponent', viewport: { width: 844, height: 390 }, query: 'mobile=1&canvas=1&field=full', attackerPlayer: 1 },
  { name: 'mobile-390x844-master', viewport: { width: 390, height: 844 }, query: 'mobile=1&canvas=1&field=full', attackerPlayer: 0, master: true },
  { name: 'spectator-public', viewport: { width: 1280, height: 720 }, query: 'canvas=1&field=full&referee=1', attackerPlayer: 1 },
]

try {
  for (const profile of profiles) {
    const { context, page, errors } = await pageFor(profile.viewport)
    await page.goto(`${base}?${profile.query}`, { waitUntil: 'domcontentloaded' })
    await page.locator('[data-l12-game-stage]').waitFor()
    await page.waitForTimeout(300)
    const expected = await declare(page, profile)
    await page.waitForTimeout(60)
    const initial = await inspect(page, expected)
    assert.equal(initial.key, `${expected.matchId}:${expected.sequence}`, `${profile.name} must use the authority sequence`)
    assert.ok(initial.path && !initial.path.includes('NaN'), `${profile.name} path must be finite`)
    assert.match(initial.path, / Q /, `${profile.name} must use a restrained quadratic connection`)
    assert.equal(initial.coreWidth, '4px', `${profile.name} core must remain substantial`)
    assert.equal(initial.glowWidth, '12px', `${profile.name} glow must support the thicker core`)
    assert.match(initial.coreMask, /^url\("?#l12-attack-connection-occlusion-/, `${profile.name} core must pass under combat information`)
    assert.equal(initial.glowMask, initial.coreMask, `${profile.name} glow must share the occlusion mask`)
    assert.equal(initial.orbMask, initial.coreMask, `${profile.name} travel marker must share the occlusion mask`)
    assert.ok(initial.occluder && Object.values(initial.occluder).every(Number.isFinite),
      `${profile.name} combat information mask must use finite logical geometry`)
    if (profile.name === 'desktop-player') {
      assert.ok(initial.combatRect, 'desktop combat information must be measurable')
      assert.ok(Math.abs(initial.occluder.x - initial.combatRect.left) <= 6
        && Math.abs(initial.occluder.y - initial.combatRect.top) <= 6,
      'desktop mask must cover the visible combat information surface')
    }
    assert.match(initial.marker, /^url\("?#l12-attack-connection-arrow-/, `${profile.name} must retain direction`)
    assert.equal(initial.pointerEvents, 'none', `${profile.name} overlay cannot consume board input`)
    assert.equal(initial.drawStarts, 1, `${profile.name} fresh attack must draw exactly once`)
    if (profile.attackerPlayer === 0) assert.equal(initial.sourceColor, 'rgb(101, 220, 228)')
    else assert.equal(initial.sourceColor, 'rgb(240, 111, 120)')
    await page.waitForTimeout(520)
    await page.evaluate(() => {
      const game = window.__l12State.game
      game.revision++
      game.pendingDefense = { ...game.pendingDefense, stage: 'DefenderAttackTiming' }
      game.recentEvents = [...game.recentEvents]
    })
    await page.waitForTimeout(100)
    const retransmit = await inspect(page, expected)
    assert.equal(retransmit.key, initial.key, `${profile.name} retransmit must keep the same visual owner`)
    assert.equal(retransmit.drawStarts, 1, `${profile.name} retransmit/stage update cannot replay`)
    await page.screenshot({ path: path.join(output, `${profile.name}.png`) })
    await page.evaluate(() => {
      const game = window.__l12State.game
      game.revision++
      game.pendingDefense = { ...game.pendingDefense, stage: 'CombatDamage' }
    })
    await page.waitForTimeout(220)
    const resolving = await inspect(page, expected)
    assert.equal(resolving.resolving, true)
    assert.equal(resolving.opacity, '0', `${profile.name} line must fade before damage presentation`)
    await page.evaluate(() => { window.__l12State.game.pendingDefense = null; window.__l12State.game.revision++ })
    await page.waitForTimeout(50)
    assert.equal(await page.locator('[data-ui-contract="authoritative-attack-connection"]').count(), 0,
      `${profile.name} invalidated target must remove the connection`)
    assert.deepEqual(errors, [], `${profile.name} browser errors`)
    results.push({ profile: profile.name, expected, initial, retransmit, resolving, errors })
    await context.close()
  }

  {
    const { context, page, errors } = await pageFor({ width: 1280, height: 720 })
    await page.goto(`${base}?canvas=1&field=full&referee=1&combat-stage=DefenseChoice`, { waitUntil: 'domcontentloaded' })
    await page.locator('[data-ui-contract="authoritative-attack-connection"]').waitFor()
    await page.waitForTimeout(550)
    const baseline = await page.locator('[data-ui-contract="authoritative-attack-connection"]').evaluate(element => ({
      intro: element.classList.contains('is-intro'),
      drawStarts: window.__attackDrawStarts,
      key: element.dataset.attackPresentationKey,
    }))
    assert.equal(baseline.intro, false, 'spectator/reconnect baseline cannot play historical intro')
    assert.equal(baseline.drawStarts, 0, 'spectator/reconnect baseline cannot emit draw animation')
    assert.deepEqual(errors, [], 'spectator/reconnect baseline browser errors')
    results.push({ profile: 'spectator-reconnect-baseline', baseline, errors })
    await context.close()
  }

  {
    const { context, page, errors } = await pageFor({ width: 844, height: 390 }, 'reduce')
    await page.goto(`${base}?mobile=1&canvas=1&field=full`, { waitUntil: 'domcontentloaded' })
    await page.locator('[data-l12-game-stage]').waitFor()
    await page.waitForTimeout(300)
    const expected = await declare(page, { attackerPlayer: 0 })
    await page.locator('[data-ui-contract="authoritative-attack-connection"]').waitFor()
    const reduced = await page.locator('[data-ui-contract="authoritative-attack-connection"]').evaluate(element => ({
      coreAnimation: getComputedStyle(element.querySelector('.attack-connection-core')).animationName,
      glowDisplay: getComputedStyle(element.querySelector('.attack-connection-glow')).display,
      orbCount: element.querySelectorAll('.attack-connection-orb').length,
      key: element.dataset.attackPresentationKey,
    }))
    assert.notEqual(reduced.coreAnimation, 'none', 'standard attack motion must override the OS hint')
    assert.notEqual(reduced.glowDisplay, 'none', 'standard attack glow must remain visible under the OS hint')
    assert.equal(reduced.key, `${expected.matchId}:${expected.sequence}`)
    assert.deepEqual(errors, [], 'reduced-motion browser errors')
    results.push({ profile: 'system-reduce-standard', reduced, errors })
    await context.close()
  }

  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ passed: true, results }, null, 2))
  console.log(JSON.stringify({ passed: true, profiles: results.map(result => result.profile) }, null, 2))
} catch (error) {
  fs.writeFileSync(path.join(output, 'failure.json'), JSON.stringify({ passed: false, error: String(error), stack: error?.stack }, null, 2))
  throw error
} finally {
  await browser.close()
}
