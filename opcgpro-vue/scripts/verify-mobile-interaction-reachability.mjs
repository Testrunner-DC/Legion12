import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const promptMapPath = path.resolve(root, '..', 'docs', 'l12', 'BATTLE-PLAYER-INFORMATION-PROMPT-MAP-20260928.tsv')
const abilityMapPath = path.resolve(root, '..', 'docs', 'l12', 'BATTLE-PLAYER-INFORMATION-ABILITY-MAP-20260928.tsv')
const target = process.argv[2] || 'http://127.0.0.1:5297/__l12_battle_preview__'
const output = process.env.L12_MOBILE_REACHABILITY_OUT
if (!output) throw new Error('L12_MOBILE_REACHABILITY_OUT must point to the governed D/E acceptance directory')
const profileFilterSupplied = Object.hasOwn(process.env, 'L12_MOBILE_REACHABILITY_PROFILES')
const kindFilterSupplied = Object.hasOwn(process.env, 'L12_MOBILE_REACHABILITY_KINDS')
const requestedProfileNames = (process.env.L12_MOBILE_REACHABILITY_PROFILES || '').split(',').map(value => value.trim()).filter(Boolean)
const requestedKindNames = (process.env.L12_MOBILE_REACHABILITY_KINDS || '').split(',').map(value => value.trim()).filter(Boolean)
const specializedOnly = process.env.L12_MOBILE_REACHABILITY_SPECIALIZED_ONLY === '1'

function tsv(file) {
  const [header, ...lines] = fs.readFileSync(file, 'utf8').trim().split(/\r?\n/)
  const keys = header.split('\t')
  return lines.map(line => Object.fromEntries(line.split('\t').map((value, index) => [keys[index], value])))
}

const promptRows = tsv(promptMapPath)
const abilityRows = tsv(abilityMapPath)
const promptFamilies = [...new Set(promptRows.map(row => row.family).filter(Boolean))].sort()
const promptKinds = [...new Set(promptRows.map(row => row.kind).filter(Boolean))].sort()
const specializedKinds = new Set(['slot', 'resource-payment', 'resource-return'])
const genericKinds = promptKinds.filter(kind => !specializedKinds.has(kind))
const profiles = [
  { name: 'portrait-compact', width: 320, height: 568 },
  { name: 'portrait-standard', width: 390, height: 844 },
  { name: 'landscape-compact', width: 568, height: 320 },
  { name: 'landscape-standard', width: 844, height: 390 },
]
const requestedProfiles = new Set(requestedProfileNames)
const requestedKinds = new Set(requestedKindNames)
const targetUrl = new URL(target)
assert.equal(targetUrl.protocol, 'http:', 'preview target must use local HTTP')
assert.equal(targetUrl.hostname, '127.0.0.1', 'preview target must be loopback only')
assert.equal(targetUrl.port, '5297', 'preview target must use the reserved 5297 port')
assert.equal(targetUrl.pathname, '/__l12_battle_preview__', 'preview target must use the fixed battle preview path')
assert.equal(targetUrl.username, '', 'preview target must not contain credentials')
assert.equal(targetUrl.password, '', 'preview target must not contain credentials')
assert.equal(targetUrl.search, '', 'preview target must not contain a caller-supplied query')
assert.equal(targetUrl.hash, '', 'preview target must not contain a fragment')
assert(!specializedOnly || !kindFilterSupplied, 'specialized-only mode cannot be combined with a generic kind filter')
if (profileFilterSupplied) assert(requestedProfileNames.length > 0, 'profile filter must select at least one known profile')
if (kindFilterSupplied) assert(requestedKindNames.length > 0, 'kind filter must select at least one known generic kind')
const unknownProfiles = [...requestedProfiles].filter(name => !profiles.some(profile => profile.name === name))
const unknownKinds = [...requestedKinds].filter(kind => !genericKinds.includes(kind))
assert.deepEqual(unknownProfiles, [], `unknown mobile reachability profiles: ${unknownProfiles.join(', ')}`)
assert.deepEqual(unknownKinds, [], `unknown mobile reachability kinds: ${unknownKinds.join(', ')}`)
const activeProfiles = requestedProfiles.size ? profiles.filter(profile => requestedProfiles.has(profile.name)) : profiles
const activeGenericKinds = specializedOnly ? [] : requestedKinds.size ? genericKinds.filter(kind => requestedKinds.has(kind)) : genericKinds
assert(activeProfiles.length > 0, 'mobile reachability profile selection is empty')
assert(specializedOnly || activeGenericKinds.length > 0, 'mobile reachability generic kind selection is empty')
const pwsh = process.env.L12_PWSH
assert(pwsh && path.isAbsolute(pwsh) && fs.existsSync(pwsh) && fs.statSync(pwsh).isFile(), 'L12_PWSH must be an existing absolute executable selected by the shared initializer')
const storageGuard = path.resolve(root, '..', 'ops', 'windows', 'L12GeneratedStorage.ps1')
execFileSync(pwsh, [
  '-NoProfile', '-NonInteractive', '-Command',
  '. $env:L12_STORAGE_GUARD; '
    + 'Assert-L12GeneratedPath -Path $env:L12_ACCEPTANCE_OUT -Purpose "mobile interaction acceptance" | Out-Null; '
    + 'foreach($name in @("TEMP","TMP","TMPDIR")){'
    + '$value=[Environment]::GetEnvironmentVariable($name,"Process"); '
    + 'if([string]::IsNullOrWhiteSpace($value)){throw "L12_TEMP_PATH_MISSING: $name"}; '
    + 'Assert-L12GeneratedPath -Path $value -Purpose "mobile interaction $name" | Out-Null}',
], {
  env: { ...process.env, L12_STORAGE_GUARD: storageGuard, L12_ACCEPTANCE_OUT: output },
  stdio: 'pipe', timeout: 15_000, windowsHide: true,
})
if (fs.existsSync(output)) assert.equal(fs.readdirSync(output).length, 0, 'acceptance output must be a new or empty directory; refusing to overwrite evidence')
fs.mkdirSync(output, { recursive: true })
const results = []
const failures = []

function counts(values) {
  return Object.fromEntries([...new Set(values)].sort().map(value => [value, values.filter(item => item === value).length]))
}

async function settle(page) {
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
}

async function controlGeometry(locator, { scroll = true } = {}) {
  if (scroll) await locator.scrollIntoViewIfNeeded()
  return locator.evaluate(element => {
    const rect = element.getBoundingClientRect()
    const style = getComputedStyle(element)
    const x = Math.min(innerWidth - 1, Math.max(0, rect.left + rect.width / 2))
    const y = Math.min(innerHeight - 1, Math.max(0, rect.top + rect.height / 2))
    const hit = document.elementFromPoint(x, y)
    let clip = { left: Math.max(0, rect.left), right: Math.min(innerWidth, rect.right), top: Math.max(0, rect.top), bottom: Math.min(innerHeight, rect.bottom) }
    const clippingAncestors = []
    for (let ancestor = element.parentElement; ancestor; ancestor = ancestor.parentElement) {
      const ancestorStyle = getComputedStyle(ancestor)
      if (!/(auto|scroll|hidden|clip)/.test(`${ancestorStyle.overflow} ${ancestorStyle.overflowX} ${ancestorStyle.overflowY}`)) continue
      const bounds = ancestor.getBoundingClientRect()
      clippingAncestors.push({
        tag: ancestor.tagName.toLowerCase(),
        className: typeof ancestor.className === 'string' ? ancestor.className : '',
        width: bounds.width,
        height: bounds.height,
        overflow: `${ancestorStyle.overflow}/${ancestorStyle.overflowX}/${ancestorStyle.overflowY}`,
      })
      clip = { left: Math.max(clip.left, bounds.left), right: Math.min(clip.right, bounds.right), top: Math.max(clip.top, bounds.top), bottom: Math.min(clip.bottom, bounds.bottom) }
    }
    const visibleWidth = Math.max(0, clip.right - clip.left)
    const visibleHeight = Math.max(0, clip.bottom - clip.top)
    return {
      name: element.getAttribute('aria-label') || element.textContent?.trim().replace(/\s+/g, ' ').slice(0, 100) || '',
      width: rect.width, height: rect.height, left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom,
      displayed: style.display !== 'none' && style.visibility !== 'hidden' && style.opacity !== '0',
      visibleRatio: rect.width * rect.height ? visibleWidth * visibleHeight / (rect.width * rect.height) : 0,
      hit: hit === element || element.contains(hit),
      hitTarget: hit ? {
        tag: hit.tagName.toLowerCase(),
        className: typeof hit.className === 'string' ? hit.className : '',
        label: hit.getAttribute('aria-label') || hit.textContent?.trim().replace(/\s+/g, ' ').slice(0, 80) || '',
      } : null,
      disabled: Boolean(element.disabled || element.getAttribute('aria-disabled') === 'true'),
      clippingAncestors,
      x, y,
    }
  })
}

async function tap(locator, label, minimum = 43) {
  const geometry = await controlGeometry(locator)
  const cardCandidate = await locator.evaluate(element => element.classList.contains('prompt-card-candidate'))
  assert(geometry.displayed, `${label}: control is hidden`)
  assert(geometry.name, `${label}: control has no accessible name or visible label`)
  if (!cardCandidate) assert(geometry.visibleRatio >= .98, `${label}: control cannot be brought fully into view ${JSON.stringify(geometry)}`)
  else assert(geometry.visibleRatio >= .6, `${label}: card candidate cannot be brought to a usable carousel position ${JSON.stringify(geometry)}`)
  assert(geometry.hit, `${label}: control center is intercepted ${JSON.stringify(geometry)}`)
  assert(geometry.width >= minimum && geometry.height >= minimum, `${label}: mobile hit target is smaller than ${minimum + 1}px ${JSON.stringify(geometry)}`)
  await locator.page().touchscreen.tap(geometry.x, geometry.y)
  await settle(locator.page())
  return geometry
}

async function tapBoardSurface(locator, label) {
  const geometry = await controlGeometry(locator)
  assert(geometry.displayed, `${label}: board surface is hidden`)
  assert(geometry.name, `${label}: board surface has no accessible name or visible label`)
  assert(geometry.visibleRatio >= .95, `${label}: board surface is clipped ${JSON.stringify(geometry)}`)
  assert(geometry.hit, `${label}: board surface center is intercepted ${JSON.stringify(geometry)}`)
  assert(geometry.width >= 18 && geometry.height >= 18, `${label}: compact board surface is too small ${JSON.stringify(geometry)}`)
  await locator.page().touchscreen.tap(geometry.x, geometry.y)
  await settle(locator.page())
  return geometry
}

async function auditButtons(container, label, minimum = 43) {
  const buttons = container.locator('button:visible:not(:disabled), [role="button"]:visible:not([aria-disabled="true"])')
  const count = await buttons.count()
  const geometries = []
  for (let index = 0; index < count; index++) {
    const button = buttons.nth(index)
    const geometry = await controlGeometry(button)
    const cardCandidate = await button.evaluate(element => element.classList.contains('prompt-card-candidate'))
    assert(geometry.name, `${label}: visible button ${index + 1} has no label`)
    if (!cardCandidate) assert(geometry.visibleRatio >= .98, `${label}: ${geometry.name} cannot be made visible ${JSON.stringify(geometry)}`)
    else assert(geometry.visibleRatio >= .6, `${label}: card candidate ${geometry.name} cannot be brought to a usable carousel position ${JSON.stringify(geometry)}`)
    assert(geometry.hit, `${label}: ${geometry.name} is intercepted ${JSON.stringify(geometry)}`)
    assert(geometry.width >= minimum && geometry.height >= minimum, `${label}: ${geometry.name} has a sub-${minimum + 1}px hit target ${JSON.stringify(geometry)}`)
    geometries.push(geometry)
  }
  assert(count > 0, `${label}: no enabled interactive button was rendered`)
  return geometries
}

async function auditInitialButtons(container, label, minimum = 31) {
  const buttons = container.locator('button:visible:not(:disabled), [role="button"]:visible:not([aria-disabled="true"])')
  const count = await buttons.count()
  const geometries = []
  for (let index = 0; index < count; index++) {
    const geometry = await controlGeometry(buttons.nth(index), { scroll: false })
    assert(geometry.name, `${label}: initial button ${index + 1} has no label`)
    assert(geometry.visibleRatio >= .98, `${label}: ${geometry.name} is not initially visible ${JSON.stringify(geometry)}`)
    assert(geometry.hit, `${label}: ${geometry.name} is initially intercepted ${JSON.stringify(geometry)}`)
    assert(geometry.width >= minimum && geometry.height >= minimum, `${label}: ${geometry.name} has a sub-${minimum + 1}px hit target ${JSON.stringify(geometry)}`)
    geometries.push(geometry)
  }
  assert(count > 0, `${label}: no initially enabled interactive button was rendered`)
  return geometries
}

const normalizeText = value => value.replace(/\s+/g, ' ').trim()

async function loadPreview(page, query = 'field=full&hand=12&information-contract=1') {
  const requested = `${target}?canvas=1&mobile=1&${query}`
  const response = await page.goto(requested, { waitUntil: 'domcontentloaded' })
  assert(response?.ok(), `preview failed to load: ${response?.status()}`)
  const loaded = new URL(page.url())
  assert.equal(loaded.origin, targetUrl.origin, 'preview redirected away from the loopback origin')
  assert.equal(loaded.pathname, targetUrl.pathname, 'preview redirected away from the fixed battle path')
  await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
}

async function exerciseDirectChrome(page, rendered, label, expectedTaskCopy) {
  const initialRecord = await page.locator('.record-log').innerText()
  const initialScrollTop = await rendered.evaluate(element => element.scrollTop)
  assert.equal(initialScrollTop, 0, `${label}: direct action surface started scrolled`)
  const geometries = await auditInitialButtons(rendered, label, 31)
  assert.equal(await page.locator('.mobile-record-trigger:visible').count(), 0, `${label}: record entry must yield to the expanded direct action`)
  await tap(rendered.getByRole('button', { name: '任务说明', exact: true }), `${label}/task-info`, 31)
  const taskDialog = page.getByRole('dialog', { name: '当前选择说明', exact: true })
  await taskDialog.waitFor()
  const taskText = normalizeText(await taskDialog.innerText())
  assert(taskText.includes(expectedTaskCopy), `${label}: full task copy is not reachable`)
  assert(taskText.length > 30, `${label}: full task explanation is unexpectedly empty`)
  await tap(taskDialog.getByRole('button', { name: '返回选择', exact: true }), `${label}/return-to-selection`)
  await tap(rendered.getByRole('button', { name: '最小化', exact: true }), `${label}/minimize`, 31)
  const record = page.locator('.mobile-record-trigger:visible')
  await record.waitFor()
  await tap(record, `${label}/record`, 31)
  const recordDialog = page.getByRole('dialog', { name: '对局记录', exact: true })
  await recordDialog.waitFor()
  assert.equal(normalizeText(await recordDialog.locator('.event-list').innerText()), normalizeText(await page.locator('.record-log .event-list').innerText()), `${label}: mobile record content changed`)
  await tap(recordDialog.getByRole('button', { name: '关闭', exact: true }), `${label}/close-record`)
  const restore = page.locator('.board-control-restore:visible')
  await restore.waitFor()
  await tap(restore, `${label}/restore`, 31)
  await rendered.waitFor()
  assert.equal(normalizeText(await page.locator('.record-log').innerText()), normalizeText(initialRecord), `${label}: record source changed across minimize/restore`)
  return geometries
}

async function installPrompt(page, kind, family, sequence) {
  await page.evaluate(({ kind, family, sequence }) => {
    const game = window.__l12State.game
    const me = game.players[game.you]
    const field = me.field.flat().filter(Boolean).slice(0, 3)
    const hand = me.hand.slice(0, 3)
    const cards = family === 'target' ? field : hand
    const candidateIds = cards.map(card => card.instanceId)
    while (candidateIds.length < 3) candidateIds.push(`fixture-choice-${candidateIds.length + 1}`)
    let choices = ['mode:use', 'mode:none']
    let minChoose = 1
    let maxChoose = 1
    const data = {}
    if (['card-selection', 'target', 'payment'].includes(family)) {
      choices = [...candidateIds.slice(0, 3), 'skip']
      data.cardSelection = 'true'
      data.displayCardIds = candidateIds.slice(0, 3).join('|')
      candidateIds.slice(0, 3).forEach((id, index) => {
        const card = cards[index]
        data[`${id}:cardId`] = card?.cardId || 'S01-0001'
        data[`${id}:name`] = card?.name || `移动端候选卡 ${index + 1}`
        data[`${id}:cardType`] = card?.cardType || 'legion'
        data[`${id}:effect`] = `第 ${index + 1} 个候选的完整效果说明；确认前必须保持可读和可点。`
      })
    }
    if (family === 'arrangement') {
      choices = candidateIds.slice(0, 3)
      minChoose = 3
      maxChoose = 3
      if (kind === 'trigger-order') data.choiceMode = 'ordered'
      else data.placementMode = kind === 'order' ? 'all-bottom' : 'ordered'
      data.displayCardIds = choices.join('|')
      choices.forEach((id, index) => {
        data[`${id}:cardId`] = cards[index]?.cardId || 'S01-0001'
        data[`${id}:name`] = cards[index]?.name || `排序卡 ${index + 1}`
        data[`${id}:cardType`] = cards[index]?.cardType || 'legion'
      })
    }
    if (family === 'information') choices = ['confirm']
    if (family === 'response') choices = kind === 'response-target' ? ['stack-a', 'stack-b', 'stack-c'] : ['respond', 'pass']
    if (family === 'position') choices = ['front-left', 'back-right', 'skip']
    if (family === 'setup') choices = ['yes', 'no']
    if (family === 'other') choices = ['first', 'second']
    if (family === 'effect-decision') data.choiceMode = 'instant'
    if (family === 'information') data.choiceMode = 'instant'
    if (family === 'setup' && kind !== 'initiative') data.choiceMode = 'instant'
    if (kind === 'response') data.choiceMode = 'instant'
    if (kind === 'response-target') {
      data['stack-a'] = '短来源效果'
      data['stack-b'] = '同名来源的第二段效果；公开目标为长名称军团'
      data['stack-c'] = '第三个来源效果，包含费用与后续状态'
    }
    const labels = Object.fromEntries(choices.map((choice, index) => [choice,
      choice === 'skip' || choice === 'mode:none' ? '不发动'
        : choice === 'pass' ? '不响应'
          : choice === 'confirm' ? '确认信息'
            : choice === 'yes' ? '发动'
              : choice === 'no' ? '不发动'
                : `效果选项 ${index + 1}`]))
    const consequences = Object.fromEntries(choices.map(choice => [choice, `${labels[choice]}后继续权威结算；对象失效时不自动改选。`]))
    const prompt = {
      promptId: `mobile-reachability-${sequence}-${kind || 'dynamic'}`,
      playerIndex: game.you,
      kind: kind || 'option',
      text: `〈移动端全类型验收〉${family}`,
      validChoices: choices,
      minChoose,
      maxChoose,
      choiceLabels: labels,
      data,
      createdRevision: sequence,
      controller: game.you,
      presentation: {
        title: `移动端全类型验收 · ${kind || 'dynamic'}`,
        situation: `这是 ${family} 交互家族的实际组件验收。长文本用于验证窄屏内部滚动、按钮可见性和布局稳定性。`.repeat(3),
        instruction: `请完成 ${minChoose} 至 ${maxChoose} 项选择。`,
        waitingSummary: `等待玩家处理 ${family}`,
        choiceConsequences: consequences,
        paymentStatus: family === 'payment' ? 'pending' : null,
        paymentSummary: family === 'payment' ? '尚未支付费用' : null,
        submissionConsequence: '确认后锁定当前选择并继续结算。',
      },
    }
    game.phase = kind === 'initiative' ? 'DisasterPreparation' : 'Main'
    game.activePlayer = game.you
    game.waitingPrompt = null
    game.prompts = [prompt]
    window.__sentCommands.length = 0
  }, { kind, family, sequence })
  await settle(page)
}

async function runGenericScenario(page, profile, kind, family, sequence) {
  const label = `${profile.name}/${kind || 'dynamic'}/${family}`
  await loadPreview(page)
  await installPrompt(page, kind, family, sequence)
  const rendered = page.locator('.prompt-choice-panel:visible, .inline-prompt-controls:visible').first()
  await rendered.waitFor()
  if (await rendered.evaluate(element => element.classList.contains('inline-prompt-controls'))) {
    const geometries = await exerciseDirectChrome(page, rendered, label, '移动端全类型验收')
    const decline = rendered.getByRole('button', { name: /^(不发动|取消|取消打出)$/ }).first()
    if (await decline.count()) await tap(decline, `${label}/decline`, 31)
    else {
      const target = page.locator('.formation-slot.targetable, .mobile-morale-choice[aria-disabled="false"]').first()
      if (await target.count()) await tap(target, `${label}/target`, 31)
      const confirm = rendered.locator('button.primary:visible:not(:disabled)').first()
      if (await confirm.count()) await tap(confirm, `${label}/confirm`, 31)
    }
    const sent = await page.evaluate(() => window.__sentCommands.length)
    assert(sent > 0, `${label}: direct touch path did not submit a command`)
    if (profile.name === 'portrait-compact') await page.screenshot({ path: path.join(output, `${profile.name}-${kind || 'dynamic'}-${family}-inline.png`) })
    return { label, kind, family, surface: 'inline', controls: geometries.length, commandCount: sent, selected: 0 }
  }
  const panel = rendered
  const panelRect = await panel.evaluate(element => {
    const rect = element.getBoundingClientRect()
    return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: innerWidth, height: innerHeight }
  })
  assert(panelRect.left >= -1 && panelRect.right <= panelRect.width + 1 && panelRect.top >= -1 && panelRect.bottom <= panelRect.height + 1,
    `${label}: prompt panel escapes viewport ${JSON.stringify(panelRect)}`)
  const geometries = await auditButtons(panel, label)

  const minimize = panel.getByRole('button', { name: '最小化弹框', exact: true })
  await tap(minimize, `${label}/minimize`)
  const restore = page.locator('.prompt-minimized-bar button:visible')
  await restore.waitFor()
  await tap(restore, `${label}/restore`)
  await panel.waitFor()

  const choices = kind === 'order'
    ? panel.locator('.all-placement-row [role="button"]:visible:not([aria-disabled="true"])')
    : panel.locator('.prompt-choices button:visible:not(:disabled), .prompt-choices [role="button"]:visible:not([aria-disabled="true"])')
  assert(await choices.count() > 0, `${label}: no actionable choice`)
  const needed = kind === 'order' ? Math.min(await choices.count(), 2) : family === 'arrangement' ? Math.min(await choices.count(), 3) : 1
  for (let index = 0; index < needed; index++) {
    const before = await page.evaluate(() => window.__sentCommands.length)
    await tap(choices.nth(index), `${label}/choice-${index + 1}`)
    const after = await page.evaluate(() => window.__sentCommands.length)
    if (after > before) break
  }
  if (await panel.count()) {
    const confirm = panel.locator('.prompt-action-footer button.primary:visible:not(:disabled), .prompt-action-footer .prompt-confirm-choice:visible:not(:disabled)').first()
    if (await confirm.count()) await tap(confirm, `${label}/confirm`)
  }
  const sent = await page.evaluate(() => window.__sentCommands.length)
  const selected = await panel.locator('.selected').count().catch(() => 0)
  assert(sent > 0 || selected > 0, `${label}: touch produced neither a submitted command nor a selected state`)
  if (profile.name === 'portrait-compact' || (profile.name === 'landscape-compact' && ['card-selection', 'arrangement', 'response'].includes(family))) {
    await page.screenshot({ path: path.join(output, `${profile.name}-${kind || 'dynamic'}-${family}.png`) })
  }
  return { label, kind, family, controls: geometries.length, commandCount: sent, selected }
}

async function runSpecializedScenario(page, profile, kind) {
  const definitions = {
    slot: { query: 'field=empty&board-slot=1&slot-opponent=1&inline-rich=1', copy: '选择登场格位' },
    'resource-payment': { query: 'morale=5&morale-payment=1&payment-actions=1&inline-rich=1', copy: '支付士气' },
    'resource-return': { query: 'morale=5&morale-payment=1&payment-return=1&inline-rich=1', copy: '返还士气' },
  }
  const definition = definitions[kind]
  const label = `${profile.name}/${kind}/specialized`
  await loadPreview(page, definition.query)
  const rendered = page.locator('.inline-prompt-controls:visible')
  await rendered.waitFor()
  const geometries = await exerciseDirectChrome(page, rendered, label, definition.copy)
  if (kind === 'slot') {
    await tapBoardSurface(page.locator('.formation-slot.available').first(), `${label}/available-slot`)
  } else {
    await tapBoardSurface(page.locator('.my-half .resource-morale-summary'), `${label}/open-resource-picker`)
    const picker = page.locator('.mobile-morale-overlay:visible')
    await picker.waitFor()
    const resources = picker.locator('.mobile-morale-choice[aria-disabled="false"]')
    const required = Math.min(2, await resources.count())
    assert(required > 0, `${label}: no selectable resource`)
    for (let index = 0; index < required; index++) await tap(resources.nth(index), `${label}/resource-${index + 1}`, 31)
    const confirm = picker.getByRole('button', { name: kind === 'resource-return' ? '确认返还' : '确认支付', exact: true })
    await tap(confirm, `${label}/confirm-resource`, 31)
  }
  const sent = await page.evaluate(() => window.__sentCommands.length)
  assert(sent > 0, `${label}: specialized touch path did not submit a command`)
  if (profile.name === 'portrait-compact') await page.screenshot({ path: path.join(output, `${profile.name}-${kind}-specialized.png`) })
  return { label, kind, family: 'specialized', surface: 'inline', controls: geometries.length, commandCount: sent, selected: 0 }
}

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  for (const profile of activeProfiles) {
    const context = await browser.newContext({ viewport: { width: profile.width, height: profile.height }, hasTouch: true })
    const page = await context.newPage()
    page.setDefaultTimeout(4_000)
    const pageErrors = []
    page.on('pageerror', error => pageErrors.push(error.message))
    await page.addInitScript(() => {
      const native = window.matchMedia.bind(window)
      window.matchMedia = query => query.includes('pointer: coarse')
        ? { matches: true, media: query, addEventListener() {}, removeEventListener() {} }
        : native(query)
    })
    await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())
    let sequence = 1
    for (const kind of activeGenericKinds) {
      const family = promptRows.find(row => row.kind === kind)?.family || 'dynamic'
      console.log(JSON.stringify({ phase: 'start', profile: profile.name, kind, family }))
      try {
        results.push(await runGenericScenario(page, profile, kind, family, sequence++))
        console.log(JSON.stringify({ phase: 'passed', profile: profile.name, kind, family }))
      }
      catch (error) {
        const failure = { profile: profile.name, kind, family, error: error instanceof Error ? error.message : String(error) }
        failures.push(failure)
        await page.screenshot({ path: path.join(output, `failure-${profile.name}-${kind || 'dynamic'}-${family}.png`), fullPage: true }).catch(() => {})
        console.log(JSON.stringify({ phase: 'failed', ...failure }))
      }
    }
    if (!requestedKinds.size && !specializedOnly) try {
      results.push(await runGenericScenario(page, profile, '', 'dynamic', sequence++))
      console.log(JSON.stringify({ phase: 'passed', profile: profile.name, kind: '', family: 'dynamic' }))
    }
    catch (error) {
      const failure = { profile: profile.name, kind: '', family: 'dynamic', error: error instanceof Error ? error.message : String(error) }
      failures.push(failure)
      await page.screenshot({ path: path.join(output, `failure-${profile.name}-dynamic.png`), fullPage: true }).catch(() => {})
      console.log(JSON.stringify({ phase: 'failed', ...failure }))
    }
    if (!requestedKinds.size) for (const kind of specializedKinds) {
      try {
        results.push(await runSpecializedScenario(page, profile, kind))
        console.log(JSON.stringify({ phase: 'passed', profile: profile.name, kind, family: 'specialized' }))
      } catch (error) {
        const failure = { profile: profile.name, kind, family: 'specialized', error: error instanceof Error ? error.message : String(error) }
        failures.push(failure)
        await page.screenshot({ path: path.join(output, `failure-${profile.name}-${kind}-specialized.png`), fullPage: true }).catch(() => {})
        console.log(JSON.stringify({ phase: 'failed', ...failure }))
      }
    }
    assert.deepEqual(pageErrors, [], `${profile.name}: page errors ${pageErrors.join(' | ')}`)
    await context.close()
  }
} finally {
  await browser.close()
}

const abilityCategories = abilityRows.flatMap(row => row.categories.split(';')).filter(Boolean)
const report = {
  status: failures.length ? 'failed' : 'passed',
  generatedAt: new Date().toISOString(),
  target,
  source: {
    promptRows: promptRows.length,
    promptKinds: promptKinds.length,
    genericPromptKinds: genericKinds.length,
    specializedPromptKinds: [...specializedKinds],
    promptFamilies: promptFamilies.length,
    abilityRows: abilityRows.length,
    promptFamilyCounts: counts(promptRows.map(row => row.family).filter(Boolean)),
    abilityCategoryCounts: counts(abilityCategories),
  },
  profiles,
  activeProfiles,
  activeGenericKinds,
  filters: {
    profilesSupplied: profileFilterSupplied,
    kindsSupplied: kindFilterSupplied,
    requestedProfiles: requestedProfileNames,
    requestedKinds: requestedKindNames,
    specializedOnly,
  },
  results,
  failures,
}
fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2), { flag: 'wx' })
assert.equal(failures.length, 0, `mobile interaction reachability failures: ${JSON.stringify(failures.slice(0, 12))}`)
console.log(JSON.stringify({ status: report.status, profiles: profiles.length, promptKinds: promptKinds.length, genericPromptKinds: genericKinds.length, promptFamilies: promptFamilies.length, abilityRows: abilityRows.length, scenarios: results.length, output }, null, 2))
