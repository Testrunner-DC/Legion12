import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import ts from 'typescript'
import { compileScript, compileTemplate, parse } from '@vue/compiler-sfc'

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const front = path.resolve(scriptDir, '..')
const repo = path.resolve(front, '..')
const output = process.env.L12_TUTORIAL_REWORK_OUTPUT
  || (process.env.L12_ARTIFACT_ROOT ? path.join(process.env.L12_ARTIFACT_ROOT, 'tutorial-rework-20261018', 'focused-current') : '')
assert.ok(output, 'L12_TUTORIAL_REWORK_OUTPUT or initialized L12_ARTIFACT_ROOT is required')
const powershell = process.env.L12_PWSH || (process.platform === 'win32' ? 'pwsh.exe' : 'pwsh')
execFileSync(powershell, ['-NoProfile', '-Command',
  `. '${path.join(repo, 'ops/windows/L12GeneratedStorage.ps1').replaceAll("'", "''")}'; `
  + '$null=Assert-L12GeneratedPath -Path $env:L12_TUTORIAL_REWORK_OUTPUT -Purpose "tutorial guidance focused evidence"'], {
  env: { ...process.env, L12_TUTORIAL_REWORK_OUTPUT: output }, stdio: 'pipe',
})
fs.mkdirSync(output, { recursive: true })

const helperPath = path.join(front, 'src/l12/tutorial/tutorialGuidance.ts')
const overlayPath = path.join(front, 'src/l12/tutorial/TutorialGuidanceOverlay.vue')
const introductionPath = path.join(front, 'src/l12/site/TutorialPage.vue')
const sfcPaths = [
  overlayPath,
  introductionPath,
  path.join(front, 'src/l12/game/GameBoard.vue'),
  path.join(front, 'src/l12/game/HandArea.vue'),
  path.join(front, 'src/l12/game/PlayerMat.vue'),
  path.join(front, 'src/l12/game/GameActions.vue'),
  path.join(front, 'src/l12/game/PromptOverlay.vue'),
]
const source = fs.readFileSync(helperPath, 'utf8')
const transpiled = ts.transpileModule(source, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 },
  reportDiagnostics: true, fileName: helperPath,
})
assert.equal(transpiled.diagnostics?.length ?? 0, 0, 'tutorial guidance helper must transpile without syntax diagnostics')
const guidance = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString('base64')}`)

function card(instanceId, overrides = {}) {
  return { instanceId, cardId: 'ST01-05', name: '食铁兽', cardType: 'legion', faction: 'tianting',
    cost: 3, baseTroops: 4000, troops: 4000, effectText: '无效果', disasterLevel: 0, tapped: false, summonRound: 0, ...overrides }
}

function player(playerIndex, overrides = {}) {
  return { playerIndex, name: playerIndex ? '教学对手' : '学习者', deckName: '教学公开构筑', faction: 'qin',
    master: { masterId: `M-${playerIndex}`, masterName: playerIndex ? '对方主宰' : '我方主宰', hp: 20, maxHp: 20 },
    libraryCount: 30, hand: [], handCount: 0, moraleDeckCount: 8,
    morale: [{ instanceId: `morale-${playerIndex}-a`, cardId: 'MORALE', tapped: false },
      { instanceId: `morale-${playerIndex}-b`, cardId: 'MORALE', tapped: false }],
    spendableResourceCount: 2, field: [[null, null, null], [null, null, null]], mulliganDone: true, ...overrides }
}

function game(self, enemy, overrides = {}) {
  return { matchId: 'match-guide', roomCode: 'ROOM', you: 0, revision: 10, activePlayer: 0, firstPlayer: 0,
    diceWinner: 0, initiativeRolls: [5, 2], phase: 'Main', round: 2, disasterMode: 'none', disasterValue: 0,
    players: [self, enemy], stateHash: 'hash', ...overrides }
}

function tutorial(lessonId, stepId = lessonId, status = 'active') {
  return { lessonId, stepId, status, version: 1, accountId: 'account', sessionId: 'session', connectionGeneration: 3,
    roomCode: 'ROOM', matchId: 'match-guide' }
}

const results = []
function check(name, fn) {
  fn()
  results.push({ name, status: 'passed' })
}

const enemy = player(1)
const first = card('practice-first', { currentCost: 2, cost: 7 })
const second = card('practice-second')
const deployGame = game(player(0, { hand: [first, second], handCount: 2 }), enemy)
const deployFacts = guidance.tutorialFacts(tutorial('deploy'), deployGame)
check('deploy mirrors frozen ordered DTO practice identity then uses exact instance', () => {
  assert.equal(deployFacts.practiceInstanceId, 'practice-first')
  assert.match(guidance.tutorialCardSelector(deployFacts.practiceInstanceId), /practice-first/)
})
check('displayed cost uses current Card DTO cost', () => assert.equal(deployFacts.practiceCost, 2))
check('authoritative spendableResourceCount wins over visible fallback', () => {
  assert.equal(deployFacts.spendableMorale, 2)
  assert.equal(deployFacts.spendableMoraleAuthoritative, true)
})
check('deploy dialogue explains reason and outcome before action', () => {
  const pages = guidance.tutorialDialoguePages(tutorial('deploy'), deployFacts, guidance.tutorialSnapshot(deployFacts))
  assert.ok(pages.length >= 4)
  assert.match(pages[0].body, /备好资源的练习场面/, 'Prepared practice resources must not be mistaken for the standard opening')
  assert.ok(pages.every(page => page.reason && page.outcome))
  assert.match(pages.map(page => `${page.reason} ${page.outcome}`).join(' '), /手牌/)
  assert.match(pages.map(page => `${page.body} ${page.outcome}`).join(' '), /牌库 30 张、手牌 2 张/)
  assert.match(pages.map(page => `${page.body} ${page.outcome}`).join(' '), /士气牌库 8 张/)
  const detail = pages.find(page => page.id === 'card')
  assert.equal(detail.anchor.selector, '.hand-card-wrap[data-tutorial-card-instance="practice-first"]')
  assert.doesNotMatch(detail.anchor.selector, /catalog-detail/)
  assert.match(detail.body, /需要 2 点费用.*兵力 4000.*效果.*无效果/)
  assert.doesNotMatch(`${detail.body} ${detail.outcome}`, /军团血量/)
})

const deployedSelf = player(0, { hand: [second], handCount: 1, morale: [
  { instanceId: 'morale-0-a', cardId: 'MORALE', tapped: true },
  { instanceId: 'morale-0-b', cardId: 'MORALE', tapped: false },
], spendableResourceCount: 1, field: [[first, null, null], [null, null, null]] })
const deployedFacts = guidance.tutorialFacts(tutorial('deploy', 'completed', 'completed'),
  game(deployedSelf, enemy, { revision: 11 }), 'practice-first')
check('completed deploy keeps exact practice identity when another Xi Shi remains in hand', () => {
  assert.equal(deployedFacts.practiceInstanceId, 'practice-first')
  assert.equal(deployedFacts.practicePosition, '0:0')
})
check('completion result states real resource and field consequences', () => {
  const pages = guidance.tutorialDialoguePages(tutorial('deploy', 'completed', 'completed'), deployedFacts,
    guidance.tutorialSnapshot(deployFacts))
  assert.equal(pages.length, 1)
  assert.match(`${pages[0].reason} ${pages[0].outcome}`, /场上力量|活跃士气/)
})
check('practice introduction follows its exact current card from hand to field without a detail-dialog anchor', () => {
  const page = guidance.tutorialDialoguePages(tutorial('deploy'), deployedFacts, guidance.tutorialSnapshot(deployFacts)).find(page => page.id === 'card')
  assert.equal(page.anchor.selector, '[data-tutorial-player-index="0"] [data-tutorial-anchor="field-slot"][data-tutorial-card-instance="practice-first"]')
  const missing = { ...deployFacts, practiceCard: null, practiceInstanceId: null }
  assert.equal(guidance.tutorialDialoguePages(tutorial('deploy'), missing, guidance.tutorialSnapshot(missing)).find(page => page.id === 'card').anchor, null)
})

const moveCard = card('move-practice')
const moveStart = guidance.tutorialFacts(tutorial('move'), game(player(0, { field: [[null, null, null], [null, moveCard, null]] }), enemy))
check('move anchors exact rear-center DTO instance', () => {
  assert.equal(moveStart.practiceInstanceId, 'move-practice')
  assert.equal(moveStart.practicePosition, '1:1')
})
check('move copy says positioning is not a troop increase', () => {
  const pages = guidance.tutorialDialoguePages(tutorial('move'), moveStart, guidance.tutorialSnapshot(moveStart))
  assert.match(pages.map(page => `${page.reason} ${page.outcome}`).join(' '), /不会直接提高兵力|不会凭空增加/)
  assert.match(pages.map(page => page.body).join(' '), /本场.*后排.*前排/)
  const visibleCopy = pages.map(page => page.body).join(' ')
  assert.match(visibleCopy, /本场食铁兽.*后排.*移到前排.*对面的空位.*进攻主宰/)
  assert.doesNotMatch(visibleCopy, /所有后排|后排军团(?:都)?不能进攻/, 'Do not turn this scene-specific movement purpose into a universal rear-row restriction')
  assert.match(visibleCopy, /支付1点费用.*新的站位/)
})

const combatCard = card('combat-practice')
const combatFacts = guidance.tutorialFacts(tutorial('combat', 'attack'), game(player(0, { field: [[combatCard, null, card('defender')], [null, null, null]] }), enemy))
check('combat anchors exact front-left DTO instance', () => {
  assert.equal(combatFacts.practiceInstanceId, 'combat-practice')
  assert.equal(combatFacts.practicePosition, '0:0')
})
check('combat copy connects attack to master-zero objective and resting cost', () => {
  const pages = guidance.tutorialDialoguePages(tutorial('combat', 'attack'), combatFacts, guidance.tutorialSnapshot(combatFacts))
  assert.match(pages.map(page => `${page.reason} ${page.outcome}`).join(' '), /主宰血量降到 0/)
  assert.match(pages.map(page => `${page.reason} ${page.outcome}`).join(' '), /进攻通常会让这张军团休整/)
})

const endTurnFacts = guidance.tutorialFacts(tutorial('combat', 'end-turn'), game(player(0, {
  field: [[combatCard, null, card('defender')], [null, null, null]],
}), player(1, { handCount: 4, moraleDeckCount: 6, morale: [
  { instanceId: 'enemy-morale-active', cardId: 'MORALE', tapped: false },
  { instanceId: 'enemy-morale-rested', cardId: 'MORALE', tapped: true },
] })))
check('end-turn copy explains opponent resource turn without inventing a delta', () => {
  const page = guidance.tutorialDialoguePages(tutorial('combat', 'end-turn'), endTurnFacts,
    guidance.tutorialSnapshot(endTurnFacts))[0]
  assert.match(page.body, /公开手牌数 4，士气区 2 张，其中活跃 1 张/)
  assert.match(page.outcome, /结束回合后.*补充资源并行动/)
})

const defenseGame = game(player(0), enemy, { phase: 'Defense', pendingDefense: {
  attackerPlayer: 1, attackerInstanceId: 'enemy-attacker', target: { type: 'master' }, stage: 'DefenseChoice', attackValue: 3,
} })
const defenseFacts = guidance.tutorialFacts(tutorial('combat', 'defend'), defenseGame)
check('defense reads actual pending attack value and target', () => {
  assert.equal(defenseFacts.pendingAttackValue, 3)
  assert.equal(defenseFacts.pendingDefenseTarget, 'master')
})
check('defense copy explains protection versus resource tradeoff', () => {
  const page = guidance.tutorialDialoguePages(tutorial('combat', 'defend'), defenseFacts, guidance.tutorialSnapshot(defenseFacts))[0]
  assert.match(`${page.reason} ${page.outcome}`, /保护主宰|投入手牌/)
  assert.match(page.body, /选择抵挡或不抵挡/)
  assert.match(page.outcome, /抵挡.*保护目标.*不抵挡.*承受/, 'The visible short line must retain the reason for the choice')
})
const firstChapterVisiblePages = [
  ...guidance.tutorialDialoguePages(tutorial('deploy'), deployFacts, guidance.tutorialSnapshot(deployFacts)),
  ...guidance.tutorialDialoguePages(tutorial('move'), moveStart, guidance.tutorialSnapshot(moveStart)),
  ...guidance.tutorialDialoguePages(tutorial('combat', 'attack'), combatFacts, guidance.tutorialSnapshot(combatFacts)),
  ...guidance.tutorialDialoguePages(tutorial('combat', 'end-turn'), endTurnFacts, guidance.tutorialSnapshot(endTurnFacts)),
  ...guidance.tutorialDialoguePages(tutorial('combat', 'defend'), defenseFacts, guidance.tutorialSnapshot(defenseFacts)),
]
const copyMetrics = {
  pages: firstChapterVisiblePages.length,
  bodyCharacters: firstChapterVisiblePages.reduce((sum, page) => sum + page.body.length, 0),
  outcomeCharacters: firstChapterVisiblePages.reduce((sum, page) => sum + page.outcome.length, 0),
  longestBody: Math.max(...firstChapterVisiblePages.map(page => page.body.length)),
  longestOutcome: Math.max(...firstChapterVisiblePages.map(page => page.outcome.length)),
}
check('first chapter visible copy stays within two short sentences plus one result line', () => {
  for (const page of firstChapterVisiblePages) {
    assert.ok(page.body.length <= 90, `${page.id} body is ${page.body.length} characters`)
    assert.ok(page.outcome.length <= 36, `${page.id} outcome is ${page.outcome.length} characters`)
    assert.ok(page.body.split(/[。！？]/).filter(Boolean).length <= 2, `${page.id} body has too many sentences`)
    assert.ok(page.outcome.split(/[。！？]/).filter(Boolean).length <= 1, `${page.id} outcome must be one line`)
  }
})

for (const sfcPath of sfcPaths) {
  check(`SFC parses: ${path.basename(sfcPath)}`, () => {
    const text = fs.readFileSync(sfcPath, 'utf8')
    const { descriptor, errors } = parse(text, { filename: sfcPath })
    assert.deepEqual(errors, [])
    if (descriptor.scriptSetup) compileScript(descriptor, { id: crypto.createHash('sha1').update(sfcPath).digest('hex').slice(0, 8) })
    if (descriptor.template) {
      const compiled = compileTemplate({ source: descriptor.template.content, filename: sfcPath,
        id: crypto.createHash('sha1').update(sfcPath).digest('hex').slice(0, 8) })
      assert.deepEqual(compiled.errors, [])
    }
  })
}

const overlaySource = fs.readFileSync(overlayPath, 'utf8')
check('overlay contract is explicit and cannot complete from timer or click-only action', () => {
  assert.match(overlaySource, /tutorial: ActiveTutorialState; game: GameState; busy\?: boolean; presentationBusy\?: boolean; acceptedHandoffIdentity\?: string/)
  assert.match(overlaySource, /props\.busy \|\| props\.presentationBusy \|\| props\.progressRetryRequired/)
  assert.match(overlaySource, /continueMainline: \[\]; restartScene: \[\]; exitTutorial: \[\]/)
  assert.match(overlaySource, /props\.tutorial\.status === 'completed'/)
  assert.match(overlaySource, /props\.acceptedHandoffIdentity === key/)
  assert.doesNotMatch(overlaySource, /completedHandoffKey/)
  assert.match(overlaySource, /props\.tutorial\.accountId[\s\S]*props\.tutorial\.sessionId[\s\S]*props\.tutorial\.connectionGeneration/)
  assert.match(overlaySource, /props\.tutorial\.roomCode[\s\S]*props\.tutorial\.matchId[\s\S]*props\.tutorial\.lessonId/)
  assert.match(overlaySource, /props\.connected[\s\S]*stateAligned\.value[\s\S]*props\.tutorial\.stepId !== 'completed'/)
  assert.match(overlaySource, /!completedResultVisible\(\)/)
  assert.doesNotMatch(overlaySource, /presentationInFlight|l12-zone-flight-ghost|l12-card-state-transition-ghost|l12-combat-defeat-ghost/)
  assert.doesNotMatch(overlaySource, /setInterval|setTimeout/)
})
check('scene transition cannot capture a practice identity from the departing match', () => {
  assert.match(overlaySource, /if \(props\.tutorial\.matchId !== props\.game\.matchId\)/)
  assert.match(overlaySource, /\[stateAligned\.value, facts\.value\.practiceInstanceId\]/)
  assert.match(overlaySource, /aligned && !practiceInstanceId\.value && instanceId/)
})
check('focus geometry is event-driven, transformed and non-blocking', () => {
  assert.match(overlaySource, /viewportRect/)
  assert.match(overlaySource, /MutationObserver/)
  assert.match(overlaySource, /l12-viewport-change/)
  assert.match(overlaySource, /pointer-events:none/)
  assert.match(overlaySource, /:global\(#l12-landscape-teleports > \.tutorial-guide-layer\)[\s\S]*?pointer-events:none/)
  assert.match(overlaySource, /:global\(#l12-landscape-teleports > \.tutorial-guidance-shell\)\{pointer-events:none\}/)
  assert.match(overlaySource, /if \(ownResponseGuide\.value\) return ownResponseGuide\.value/)
  assert.match(overlaySource, /!ownResponseGuide && !dialogueDone && tutorial\.status !== 'completed'/)
  assert.match(overlaySource, /不等于已经选择不抵挡/)
})
check('practical guidance never mounts a card-details modal or reopens it from page, card or DOM changes', () => {
  assert.doesNotMatch(overlaySource, /CatalogCardDetails|detailOpen|detailCard|detailFields|detailScrollKey/)
  assert.doesNotMatch(overlaySource, /catalog-detail-dialog|tutorial-detail-mask|tutorial-detail-focus/)
  assert.match(overlaySource, /document\.querySelectorAll\(anchor\.selector\)/)
  assert.doesNotMatch(overlaySource, /军团血量栏/)
})
check('ordinary card inspector and first introduction details still require the existing user controls', () => {
  const introduction = fs.readFileSync(introductionPath, 'utf8')
  assert.match(introduction, /@click="showCardDetails\(card\.id\)"/)
  assert.match(introduction, /<CatalogCardDetails v-if="detailCard"[^>]*@close="closeCardDetails"/)
  assert.equal((introduction.match(/showCardDetails\(/g) ?? []).length, 2, 'Only the existing function and direct user click may open first-introduction details')
  const board = fs.readFileSync(path.join(front, 'src/l12/game/GameBoard.vue'), 'utf8')
  assert.match(board, /@click="mobileInspectorOpen = !mobileInspectorOpen"/)
  assert.match(board, /v-if="mobileLandscapeViewport && mobileInspectorOpen"[^>]*role="dialog"[^>]*aria-label="卡牌详情"/)
})
check('mainline is linear and does not repeat body in a second result panel', () => {
  const template = parse(overlaySource, { filename: overlayPath }).descriptor.template?.content ?? ''
  assert.doesNotMatch(template, /我想了解|回到主线|tutorial-help|tutorial-cause-and-result|为什么|会发生什么/)
  assert.doesNotMatch(template, /tutorial-result-line/)
  assert.match(template, /<details class="tutorial-secondary-actions">/)
  assert.match(template, /重学本段/)
  assert.match(template, /退出教学/)
  assert.match(overlaySource, /data-tutorial-guide-id/)
  assert.match(overlaySource, /data-tutorial-scene/)
  assert.match(overlaySource, /data-tutorial-reading/)
  assert.doesNotMatch(template, /tutorial\.status === 'completed'[\s\S]{0,220}<button[^>]*tutorial-primary/)
  assert.match(template, /'下一步'/)
})
check('payment guidance counts distinct resources, not duplicate mobile and board nodes', () => {
  assert.match(overlaySource, /selectedPaymentCount:\s*tutorialPaymentSelectionCount\(Array\.from\(document\.querySelectorAll/)
  assert.match(overlaySource, /getAttribute\('data-tutorial-payment-resource'\)/)
  assert.match(overlaySource, /selectedPaymentCount < \(prompt\?\.minChoose/)
})
check('tutorial anchors expose only public own hand, library and exact practice instance', () => {
  const sources = sfcPaths.map(file => fs.readFileSync(file, 'utf8')).join('\n')
  assert.match(sources, /data-tutorial-anchor="hand"/)
  assert.match(sources, /data-tutorial-anchor="library"/)
  assert.match(overlaySource, /practiceInstanceId = ref/)
  assert.match(overlaySource, /tutorialFacts\(props\.tutorial, props\.game, practiceInstanceId\.value \|\| undefined\)/)
})
check('mobile teaching reveals only its bound card once per step and orientation', () => {
  assert.match(overlaySource, /document\.documentElement\.dataset\.l12Mobile === 'true'/)
  assert.match(overlaySource, /closest<HTMLElement>\('\.prompt-card-candidate'\)/)
  assert.match(overlaySource, /lastRevealedChoiceAnchor !== revealIdentity/)
  assert.match(overlaySource, /props\.tutorial\.matchId.*props\.tutorial\.stepId.*anchor\.selector.*dataset\.l12Rotated/)
  assert.doesNotMatch(overlaySource.match(/const revealIdentity = .*/)?.[0] ?? '', /viewport\.width|viewport\.height/)
  assert.match(overlaySource, /scrollIntoView\(\{ block: 'nearest', inline: 'nearest', behavior: 'instant' \}\)/)
  assert.doesNotMatch(overlaySource, /props\.mobileLayout/)
  const mobileCss = fs.readFileSync(path.join(front, 'src/l12/mobileViewport.css'), 'utf8')
  assert.match(mobileCss, /\.prompt-choice-body\s*\{[^}]*container-type: size/s)
  assert.match(mobileCss, /--l12-contained-card-h: max\(128px, min\(var\(--l12-dialog-card-h\), calc\(100cqh - 4px\)\)\)/)
  assert.match(mobileCss, /height: var\(--l12-contained-card-h\) !important/)
})
check('tutorial terminology does not expose internal lesson labels', () => {
  const template = parse(overlaySource, { filename: overlayPath }).descriptor.template?.content ?? ''
  const visibleTemplate = template.replace(':data-tutorial-scene="tutorial.lessonId"', '')
  assert.doesNotMatch(visibleTemplate, /lessonId|stepId|部署/)
  assert.doesNotMatch(template, /继续主线/)
  assert.match(template, /下一步/)
})

const sourceHashes = Object.fromEntries([helperPath, ...sfcPaths].map(file => [path.relative(repo, file).replaceAll('\\', '/'),
  crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')]))
const report = { status: 'passed', total: results.length, failed: 0, results, sourceHashes, copyMetrics,
  boundaries: ['pure focused helper/SFC compilation only', 'no browser, server, build, deployment or tutorial completion acceptance'] }
const reportPath = path.join(output, 'report.json')
fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`)
console.log(JSON.stringify({ status: report.status, total: report.total, reportPath, reportSha256:
  crypto.createHash('sha256').update(fs.readFileSync(reportPath)).digest('hex') }))
