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
const powershell = process.env.SystemRoot ? path.join(process.env.SystemRoot, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe') : 'powershell.exe'
execFileSync(powershell, ['-NoProfile', '-Command',
  `. '${path.join(repo, 'ops/windows/L12GeneratedStorage.ps1').replaceAll("'", "''")}'; `
  + '$null=Assert-L12GeneratedPath -Path $env:L12_TUTORIAL_REWORK_OUTPUT -Purpose "tutorial guidance focused evidence"'], {
  env: { ...process.env, L12_TUTORIAL_REWORK_OUTPUT: output }, stdio: 'pipe',
})
fs.mkdirSync(output, { recursive: true })

const helperPath = path.join(front, 'src/l12/tutorial/tutorialGuidance.ts')
const overlayPath = path.join(front, 'src/l12/tutorial/TutorialGuidanceOverlay.vue')
const sfcPaths = [
  overlayPath,
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
  return { instanceId, cardId: 'S01-0116', name: '西施', cardType: 'legion', faction: 'qin',
    cost: 1, baseTroops: 2, troops: 2, disasterLevel: 0, tapped: false, summonRound: 0, ...overrides }
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
  assert.ok(pages.every(page => page.reason && page.outcome))
  assert.match(pages.map(page => `${page.reason} ${page.outcome}`).join(' '), /手牌/)
  assert.match(pages.map(page => `${page.body} ${page.outcome}`).join(' '), /牌库 30 张、手牌 2 张/)
  assert.match(pages.map(page => `${page.body} ${page.outcome}`).join(' '), /士气牌库 8 张/)
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

const moveCard = card('move-practice')
const moveStart = guidance.tutorialFacts(tutorial('move'), game(player(0, { field: [[null, null, null], [null, moveCard, null]] }), enemy))
check('move anchors exact rear-center DTO instance', () => {
  assert.equal(moveStart.practiceInstanceId, 'move-practice')
  assert.equal(moveStart.practicePosition, '1:1')
})
check('move copy says positioning is not a troop increase', () => {
  const pages = guidance.tutorialDialoguePages(tutorial('move'), moveStart, guidance.tutorialSnapshot(moveStart))
  assert.match(pages.map(page => `${page.reason} ${page.outcome}`).join(' '), /不会直接提高兵力|不会凭空增加/)
})

const combatCard = card('combat-practice')
const combatFacts = guidance.tutorialFacts(tutorial('combat', 'attack'), game(player(0, { field: [[combatCard, null, card('defender')], [null, null, null]] }), enemy))
check('combat anchors exact front-left DTO instance', () => {
  assert.equal(combatFacts.practiceInstanceId, 'combat-practice')
  assert.equal(combatFacts.practicePosition, '0:0')
})
check('combat copy connects attack to master-zero objective and opportunity cost', () => {
  const pages = guidance.tutorialDialoguePages(tutorial('combat', 'attack'), combatFacts, guidance.tutorialSnapshot(combatFacts))
  assert.match(pages.map(page => `${page.reason} ${page.outcome}`).join(' '), /主宰血量降到 0/)
  assert.match(pages.map(page => `${page.reason} ${page.outcome}`).join(' '), /进攻机会/)
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
  assert.match(page.body, /具体变化请看接下来的场面/)
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
  assert.match(page.body, /请根据现在可见的场面/)
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
  assert.match(overlaySource, /tutorial: ActiveTutorialState; game: GameState; busy\?: boolean/)
  assert.match(overlaySource, /continueMainline: \[\]; restartScene: \[\]; exitTutorial: \[\]/)
  assert.match(overlaySource, /props\.tutorial\.status === 'completed'/)
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
  assert.match(overlaySource, /!ownResponseGuide && \(!dialogueDone/)
  assert.match(overlaySource, /不等于已经选择不抵挡/)
})
check('mainline and contextual questions remain in one overlay', () => {
  assert.match(overlaySource, /我想了解…/)
  assert.match(overlaySource, /明白了，回到主线/)
  assert.match(overlaySource, /为什么/)
  assert.match(overlaySource, /会发生什么/)
  assert.match(overlaySource, /data-tutorial-guide-id/)
  assert.match(overlaySource, /data-tutorial-scene/)
  assert.match(overlaySource, /data-tutorial-reading/)
})
check('tutorial anchors expose only public own hand, library and exact practice instance', () => {
  const sources = sfcPaths.map(file => fs.readFileSync(file, 'utf8')).join('\n')
  assert.match(sources, /data-tutorial-anchor="hand"/)
  assert.match(sources, /data-tutorial-anchor="library"/)
  assert.match(overlaySource, /practiceInstanceId = ref/)
  assert.match(overlaySource, /tutorialFacts\(props\.tutorial, props\.game, practiceInstanceId\.value \|\| undefined\)/)
})
check('tutorial terminology does not expose internal lesson labels', () => {
  const template = parse(overlaySource, { filename: overlayPath }).descriptor.template?.content ?? ''
  const visibleTemplate = template.replace(':data-tutorial-scene="tutorial.lessonId"', '')
  assert.doesNotMatch(visibleTemplate, /lessonId|stepId|部署/)
  assert.match(template, /继续主线/)
})

const sourceHashes = Object.fromEntries([helperPath, ...sfcPaths].map(file => [path.relative(repo, file).replaceAll('\\', '/'),
  crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')]))
const report = { status: 'passed', total: results.length, failed: 0, results, sourceHashes,
  boundaries: ['pure focused helper/SFC compilation only', 'no browser, server, build, deployment or tutorial completion acceptance'] }
const reportPath = path.join(output, 'report.json')
fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`)
console.log(JSON.stringify({ status: report.status, total: report.total, reportPath, reportSha256:
  crypto.createHash('sha256').update(fs.readFileSync(reportPath)).digest('hex') }))
