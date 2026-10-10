import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdirSync, readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'
import { effectResultPresentationText } from '../src/l12/game/effectResultPresentation.ts'

const source = { instanceId: 'source-1', name: '普罗米修斯', hidden: false }
const result = (status, extra = {}) => ({
  sequence: 1, type: 'effect-result', text: '没有合法处理对象，跳过该效果段',
  effectText: '展示牌库顶卡牌', effectResultStatus: status,
  cards: [source], ...extra,
})
const withReason = (reason, sourceInstanceId = source.instanceId) => ({
  sourceInstanceId, actionLabel: '效果结果', outcomeLabel: `原因：${reason}`,
})
const present = (event, displayedSource = source) => effectResultPresentationText(event, displayedSource)

assert.equal(present(result('skipped', {
  playerLogSemantic: withReason('牌库为空，没有可展示的牌库顶卡牌'),
})), '展示牌库顶卡牌（跳过；原因：牌库为空，没有可展示的牌库顶卡牌）',
'普罗米修斯的公开空库原因不能被改写成无合法对象')
assert.equal(present(result('skipped', {
  playerLogSemantic: withReason('对方前排没有可处理的军团'),
})), '展示牌库顶卡牌（跳过；原因：对方前排没有可处理的军团）',
'万箭齐发的公开零对象原因要与空库区分')
assert.equal(present(result('skipped')), '展示牌库顶卡牌（跳过）',
  '缺少权威原因时不得从后台摘要推断无合法对象')
assert.equal(present(result('skipped', {
  playerLogSemantic: withReason('旁支私有信息', 'other-source'),
})), '展示牌库顶卡牌（跳过）', '来源实例不匹配时不能复用其他结算的理由')
assert.equal(present(result('failed', {
  playerLogSemantic: withReason('牌库为空，无法展示牌库顶部卡牌'),
})), '展示牌库顶卡牌（未能完成；原因：牌库为空，无法展示牌库顶部卡牌）')
assert.equal(present(result('declined')), '展示牌库顶卡牌（选择不发动）')
assert.equal(present(result('negated')), '展示牌库顶卡牌（被无效）')

const first = result('resolved', { effectSegmentIndex: 1, effectSegmentCount: 2 })
const declined = result('declined', { effectSegmentIndex: 2, effectSegmentCount: 2 })
const failed = result('failed', { effectSegmentIndex: 2, effectSegmentCount: 2 })
assert.equal(present(first), '展示牌库顶卡牌（第1/2段完成）')
assert.equal(present(declined), '展示牌库顶卡牌（第2/2段选择不发动）')
assert.equal(present(failed), '展示牌库顶卡牌（第2/2段未能完成）')
assert.equal(present(result('negated', { effectSegmentIndex: 1, effectSegmentCount: 2 })),
  '展示牌库顶卡牌（第1/2段被无效）')
assert.equal(present(result('resolved')), '展示牌库顶卡牌',
  '单段成功保持原效果文字，不编造额外结果')
for (const [index, count] of [[0, 2], [3, 2], [1.5, 2], [1, 0], [1, -2], [1, undefined]]) {
  assert.equal(present(result('failed', { effectSegmentIndex: index, effectSegmentCount: count })),
    '展示牌库顶卡牌（未能完成）', '无效段号不得展示')
}
assert.equal(present(result('failed', { effectSegmentIndex: 1, effectSegmentCount: 1 })),
  '展示牌库顶卡牌（未能完成）', '单段不需要第1/1段噪声')
assert.equal(present(result(undefined, { playerLogSemantic: withReason('旧回放不存在的理由') })),
  '展示牌库顶卡牌', '旧回放没有结果状态时不得补造')
assert.equal(present(result('unavailable')), '展示牌库顶卡牌')

const hidden = { ...source, hidden: true }
assert.equal(present(result('skipped', {
  effectText: '', cards: [hidden], playerLogSemantic: withReason('隐私信息'),
}), hidden), '效果（跳过）', '隐藏来源不能因为实例号吻合而公开理由或卡名')
assert.equal(effectResultPresentationText(result('skipped', {
  effectText: '', cards: [], playerLogSemantic: withReason('隐私信息'),
}), undefined), '效果（跳过）', '没有公开来源时只给中性提示')
const longName = { ...source, name: '长名'.repeat(60) }
const longReason = '公开原因'.repeat(80)
assert.equal(present(result('failed', {
  effectText: '', effectSegmentIndex: 12, effectSegmentCount: 20,
  playerLogSemantic: withReason(longReason),
}), longName), `〈${longName.name}〉的效果（第12/20段未能完成；原因：${longReason}）`,
  '长卡名、长理由与多段序号不能截断权威内容')
assert.equal(present(result('skipped', { effectText: '第一行\n第二行' })),
  '第一行\n第二行（跳过）', '后台效果文字的手动换行必须保留')
for (const type of ['effect-trigger', 'effect-response', 'effect-activation']) {
  assert.equal(present(result('failed', { type })), '展示牌库顶卡牌',
    `${type} 不得显示成结算失败`)
}

const board = readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')
assert(board.includes("if (event.type === 'effect-result') return effectResultPresentationText(event, presentationCards(event)[0])"),
  '真实动画入口必须只为 effect-result 调用权威结果格式化')
assert(board.includes('event.sequence > lastPublicRevealSequence.value')
  && board.includes('lastPublicRevealSequence.value = Math.max(lastPublicRevealSequence.value, event.sequence)'),
  '重复投影仍按已有 sequence 水位去重')

if (process.argv.includes('--engine')) {
  const assemblyDirectory = process.env.L12_EFFECT_SAMPLE_ASSEMBLY_DIR
  const expectedCommit = process.env.L12_EFFECT_SAMPLE_COMMIT
  assert(assemblyDirectory && expectedCommit,
    '--engine requires an explicitly identified real engine assembly and its exact commit')
  const probe = new URL('./probe-battle-log-qianyang.ps1', import.meta.url).pathname.replace(/^\/(?=[A-Za-z]:)/, '')
  const sample = name => JSON.parse(execFileSync('pwsh', [
    '-NoProfile', '-File', probe, '-Scenario', name, '-Configuration', 'Release',
    '-AssemblyDirectory', assemblyDirectory, '-ExpectedCommit', expectedCommit,
  ], { encoding: 'utf8' }))
  for (const [scenario, status, reason] of [
    ['prometheus-empty', 'skipped', '牌库为空，没有可展示的牌库顶卡牌'],
    ['yin-empty', 'failed', '牌库为空，无法展示牌库顶部卡牌'],
    ['volley-empty', 'skipped', '对方前排没有可处理的军团'],
  ]) {
    const views = sample(scenario)
    for (const view of [views.owner, views.opponent, views.spectator]) {
      const event = view.find(item => item.type === 'effect-result'
        && item.effectResultStatus === status && item.playerLogSemantic?.outcomeLabel?.includes(reason))
      assert(event, `${scenario}: real projected result must reach this recipient`)
      const shown = present(event, event.cards?.[0])
      assert(shown.includes(reason), `${scenario}: animation must display the real safe reason`)
      assert(!shown.includes('无合法处理对象'), `${scenario}: no generic zero-target claim`)
      assert(!shown.includes(event.cards?.[0]?.instanceId ?? '\u0000'),
        `${scenario}: internal source instance id must not reach the animation`)
    }
  }
  const positive = sample('volley-positive')
  for (const view of [positive.owner, positive.opponent, positive.spectator]) {
    const event = view.find(item => item.type === 'effect-result' && item.effectResultStatus === 'resolved')
    assert(event && !present(event, event.cards?.[0]).includes('跳过'),
      'normal successful target must not become a skipped result')
  }
  const hidden = sample('hidden-draw')
  for (const view of [hidden.owner, hidden.opponent, hidden.spectator]) {
    for (const event of view.filter(item => item.type === 'effect-result')) {
      const shown = present(event, event.cards?.[0])
      assert(!shown.includes('stage4a-private-target'),
        'private target instance must not enter owner, opponent or spectator presentation')
    }
  }
  console.log('effect result presentation: real owner/opponent/spectator engine projections passed')
}
if (process.argv.includes('--browser')) {
  const require = createRequire(import.meta.url)
  const { chromium } = require(process.env.L12_PLAYWRIGHT
    || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  const browser = await chromium.launch({ channel: 'msedge', headless: true })
  const url = process.env.L12_BATTLE_PREVIEW_URL || 'http://127.0.0.1:5198/__l12_battle_preview__'
  const screenshotDirectory = process.env.L12_EFFECT_SCREENSHOT_DIR
  if (screenshotDirectory) mkdirSync(screenshotDirectory, { recursive: true })
  const liveCard = (name, instanceId) => ({
    instanceId, cardId: 'S02-05M2', name, cardType: 'master', faction: '奥林匹斯',
    cost: 0, baseTroops: 0, troops: 0, disasterLevel: 0,
  })
  const liveEvent = (sequence, effectText, cardName = '普罗米修斯') => ({
    sequence, type: 'effect-result', playerIndex: 0, text: '旧摘要',
    effectSceneId: 'stage3b-browser', effectResultStatus: 'skipped', effectText,
    cards: [liveCard(cardName, `stage3b-${cardName}`)],
  })
  async function livePage() {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } })
    await page.addInitScript(() => localStorage.setItem('l12-audio-preferences-v1',
      JSON.stringify({ animation: 'off' })))
    await page.goto(url, { waitUntil: 'domcontentloaded' })
    await page.locator('.my-half').waitFor()
    await page.evaluate(() => {
      window.__stage3bSeen = []
      window.__stage3bTimer = window.setInterval(() => {
        const overlay = document.querySelector('.public-reveal-animation')
        if (!overlay) return
        const text = overlay.querySelector('strong')?.textContent ?? ''
        const card = overlay.querySelector('img')?.alt ?? ''
        const key = `${text}|${card}`
        if (window.__stage3bSeen.at(-1) !== key) window.__stage3bSeen.push(key)
      }, 20)
    })
    return page
  }
  async function push(page, events) {
    await page.evaluate(items => {
      const state = window.__l12State
      state.game.recentEvents = [...state.game.recentEvents, ...items]
    }, events)
  }
  async function seen(page) { return page.evaluate(() => [...window.__stage3bSeen]) }
  try {
    const duplicate = await livePage()
    try {
      await push(duplicate, [liveEvent(1000, '第一次展示'), liveEvent(1000, '重复展示')])
      await duplicate.waitForTimeout(2300)
      assert.deepEqual(await seen(duplicate), ['第一次展示（跳过）|普罗米修斯'],
        '同一批同 sequence 的 effect-result 只播放第一次')
      await push(duplicate, [liveEvent(1000, '重连补发')])
      await duplicate.waitForTimeout(450)
      assert.deepEqual(await seen(duplicate), ['第一次展示（跳过）|普罗米修斯'],
        '已消费的 sequence 再次投影不得重播')
    } finally { await duplicate.close() }

    for (const incoming of [
      [liveEvent(1002, '后段'), liveEvent(1001, '前段')],
      [liveEvent(1001, '前段'), liveEvent(1002, '后段')],
    ]) {
      const ordered = await livePage()
      try {
        await push(ordered, incoming)
        await ordered.waitForTimeout(2300)
        assert.deepEqual(await seen(ordered), [
          '前段（跳过）|普罗米修斯', '后段（跳过）|普罗米修斯',
        ], '同批乱序按权威 sequence 顺序各展示一次')
      } finally { await ordered.close() }
    }

    const mixed = await livePage()
    try {
      await push(mixed, [liveEvent(1000, '先前已展示')])
      await mixed.waitForTimeout(1200)
      await push(mixed, [liveEvent(999, '过期'), liveEvent(1001, '新事件')])
      await mixed.waitForTimeout(1200)
      assert.deepEqual(await seen(mixed), [
        '先前已展示（跳过）|普罗米修斯', '新事件（跳过）|普罗米修斯',
      ], '混合旧/新事件只展示未消费的新序号')
    } finally { await mixed.close() }

    const sameCopy = await livePage()
    try {
      await push(sameCopy, [liveEvent(1001, '相同文案', '来源甲'),
        liveEvent(1002, '相同文案', '来源乙')])
      await sameCopy.waitForTimeout(2300)
      assert.deepEqual(await seen(sameCopy), [
        '相同文案（跳过）|来源甲', '相同文案（跳过）|来源乙',
      ], '不同 sequence 的同文案事件必须分别展示')
    } finally { await sameCopy.close() }

    for (const viewport of [
      { width: 1440, height: 900, mobile: false },
      { width: 568, height: 320, mobile: true },
      { width: 667, height: 375, mobile: true },
      { width: 844, height: 390, mobile: true },
      { width: 390, height: 844, mobile: true },
    ]) {
      const page = await browser.newPage({ viewport })
      try {
        await page.goto(`${url}${viewport.mobile ? '?mobile=1&canvas=1' : ''}`,
          { waitUntil: 'domcontentloaded' })
        await page.locator('.my-half').waitFor()
        const event = liveEvent(1000,
          '查看牌库顶部三张卡牌，选择一张奥林匹斯卡牌展示并加入手牌，其余卡牌按选择的顺序放回牌库。',
          '普罗米修斯之奥林匹斯远见预言')
        event.effectSegmentIndex = 12
        event.effectSegmentCount = 20
        event.playerLogSemantic = {
          sourceInstanceId: event.cards[0].instanceId,
          actionLabel: '效果结果',
          outcomeLabel: `原因：${'牌库为空，没有可展示的牌库顶卡牌。'.repeat(15)}`,
        }
        await push(page, [event])
        await page.locator('.public-reveal-animation').waitFor()
        await page.waitForTimeout(250)
        const geometry = await page.evaluate(() => {
          const box = element => element?.getBoundingClientRect().toJSON() ?? null
          const overlay = document.querySelector('.public-reveal-animation')
          const strong = overlay?.querySelector('strong')
          return {
            overlay: box(overlay),
            returnButton: box(document.querySelector('button[aria-label="返回大厅"]')),
            surrender: box(document.querySelector('button.surrender')),
            textOverflowX: strong ? strong.scrollWidth - strong.clientWidth : null,
            textOverflowY: strong ? strong.scrollHeight - strong.clientHeight : null,
            textOverflowMode: strong ? getComputedStyle(strong).overflowY : null,
          }
        })
        const { overlay } = geometry
        const size = `${viewport.width}×${viewport.height}`
        assert(overlay && overlay.left >= -1 && overlay.top >= -1
          && overlay.right <= viewport.width + 1 && overlay.bottom <= viewport.height + 1,
        `${size}: long authoritative result must stay inside the physical viewport: ${JSON.stringify(geometry)}`)
        assert(geometry.textOverflowX <= 1, `${size}: effect text may not be horizontally clipped`)
        assert(geometry.textOverflowY <= 1 || geometry.textOverflowMode === 'auto',
          `${size}: long reason must remain within a scrollable text box`)
        const overlaps = (left, right) => left && right
          && Math.min(left.right, right.right) > Math.max(left.left, right.left)
          && Math.min(left.bottom, right.bottom) > Math.max(left.top, right.top)
        for (const [name, control] of [
          ['返回', geometry.returnButton], ['投降', geometry.surrender],
        ]) assert(!overlaps(overlay, control), `${size}: result overlay covers ${name}`)
        if (viewport.mobile && viewport.width > viewport.height)
          assert(overlay.bottom <= viewport.height - 64 + 1,
            `${size}: leave the bottom 64px clear; actual replay controls are covered by the dedicated ReplayPage gate`)
        if (screenshotDirectory) await page.screenshot({
          path: path.join(screenshotDirectory, `stage3b1-${viewport.width}x${viewport.height}.png`),
        })
      } finally { await page.close() }
    }
  } finally { await browser.close() }
  console.log('effect result presentation: real Edge queue ordering, re-delivery, return/surrender and five viewport geometries passed')
}
console.log('effect result presentation: reason, status, segments, privacy and compatibility passed')
