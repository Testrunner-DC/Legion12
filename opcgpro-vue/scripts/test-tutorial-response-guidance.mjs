import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

const front = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repo = path.dirname(front)
const output = process.env.L12_TUTORIAL_RESPONSE_OUTPUT
  || (process.env.L12_ARTIFACT_ROOT ? path.join(process.env.L12_ARTIFACT_ROOT, 'tutorial-response-guidance-20261019') : '')
assert.ok(output, 'explicit governed D/E output or initialized L12_ARTIFACT_ROOT is required')
execFileSync(process.env.L12_PWSH || 'powershell.exe', ['-NoProfile', '-Command',
  `. '${path.join(repo, 'ops/windows/L12GeneratedStorage.ps1').replaceAll("'", "''")}'; `
  + '$null=Assert-L12GeneratedPath -Path $env:L12_TUTORIAL_RESPONSE_OUTPUT -Purpose "response teaching focused evidence"'], {
  env: { ...process.env, L12_TUTORIAL_RESPONSE_OUTPUT: output }, stdio: 'pipe',
})
fs.mkdirSync(output, { recursive: true })
const guidanceFile = path.join(front, 'src/l12/tutorial/tutorialGuidance.ts')
const responseFile = path.join(front, 'src/l12/tutorial/tutorialResponseGuidance.ts')
const progressFile = path.join(front, 'src/l12/tutorial/tutorialProgress.ts')
const toModule = (file, replacements = []) => {
  let source = fs.readFileSync(file, 'utf8')
  const emitted = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ES2022, target: ts.ScriptTarget.ES2022 }, reportDiagnostics: true })
  assert.equal(emitted.diagnostics.length, 0)
  source = emitted.outputText
  for (const [from, to] of replacements) source = source.replace(from, to)
  return `data:text/javascript;base64,${Buffer.from(source).toString('base64')}`
}
const helpers = await import(toModule(responseFile, [["'./tutorialGuidance'", JSON.stringify(toModule(guidanceFile))]]))
const sharedHelpers = await import(toModule(guidanceFile))
const checks = []
function check(name, fn) { fn(); checks.push({ name, passed: true }) }
check('response teaching exposes no optional explanation branch', () => {
  assert.equal('responseTutorialHelp' in helpers, false)
})
check('duplicate board and mobile nodes represent only one selected payment resource', () => {
  assert.equal(sharedHelpers.tutorialPaymentSelectionCount(['morale-a', 'morale-a']), 1)
  assert.equal(sharedHelpers.tutorialPaymentSelectionCount(['morale-a', 'morale-b', 'morale-a', 'morale-b']), 2)
  assert.equal(sharedHelpers.tutorialPaymentSelectionCount([null, '', null]), 0)
})
const card = (instanceId, cardId, name, extra = {}) => ({ instanceId, cardId, name,
  cardType: cardId === 'S01-0016' ? 'tactic' : 'legion', cost: cardId === 'S01-0016' ? 2 : 1,
  troops: 1000, tapped: false, summonRound: 1, ...extra })
const xi = card('xi-first', 'S01-0116', '西施')
const counter = card('counter-first', 'S01-0016', '绝对防御')
const discarded = card('discard-a', 'S01-0108', '韩信', { cost: 5 })
const player = (index, extra = {}) => ({ playerIndex: index, name: `actor-${index}`,
  hand: [], handCount: 0, morale: [{ instanceId: 'm0', tapped: false }, { instanceId: 'm1', tapped: true }],
  field: [[null, null, null], [null, null, null]], graveyard: [], ...extra })
const game = (self = player(0, { hand: [xi, counter, discarded], handCount: 3 }), extra = {}) => ({
  you: 0, players: [self, player(1)], prompts: [], ...extra,
})
const initial = helpers.responseTutorialFacts(game())
check('learns exact own recipe instances from real ordered cards', () => {
  assert.equal(initial.legionId, xi.instanceId); assert.equal(initial.counterId, counter.instanceId)
  assert.equal(initial.activeMorale, 1); assert.equal(initial.handCount, 3)
})
check('reads actual current cost and troops, never replaces them by printed defaults', () => {
  const facts = helpers.responseTutorialFacts(game(player(0, { hand: [{ ...xi, currentCost: 0, troops: 2300 }, counter] })))
  const text = helpers.responseTutorialDialogue('response-legion', facts).map(page => page.body).join(' ')
  assert.match(text, /费用 0、兵力 2300/)
})
check('retains owner-known covered counter on reconnect', () => {
  const facts = helpers.responseTutorialFacts(game(player(0, { field: [[xi, null, null], [counter, null, null]].map(row => row.map(c => c?.instanceId === counter.instanceId ? { ...c, hidden: true, identityKnown: true } : c)) })))
  assert.equal(facts.counterId, counter.instanceId)
})
check('unknown covered identity is not guessed', () => {
  const facts = helpers.responseTutorialFacts(game(player(0, { field: [[null, null, null], [{ ...counter, hidden: true, identityKnown: false }, null, null]] })))
  assert.equal(facts.counter, null); assert.equal(facts.counterId, null)
})
check('bound instance does not switch to another same-name card', () => {
  const facts = helpers.responseTutorialFacts(game(player(0, { hand: [{ ...xi, instanceId: 'xi-second' }, counter] })), { legionId: xi.instanceId, counterId: counter.instanceId })
  assert.equal(facts.legion, null); assert.equal(facts.legionId, xi.instanceId)
})
check('does not access opponent hidden hand', () => {
  const enemy = player(1)
  Object.defineProperty(enemy, 'hand', { get() { throw new Error('private opponent hand accessed') } })
  assert.equal(helpers.responseTutorialFacts(game(undefined, { players: [player(0, { hand: [xi, counter] }), enemy] })).counterId, counter.instanceId)
})
const phases = ['response-legion', 'response-cover', 'response-end-turn', 'response-choose', 'response-discard', 'completed', 'response-retry']
for (const phase of phases) check(`${phase}: explains cause and gain before acting or reviewing`, () => {
  const pages = helpers.responseTutorialDialogue(phase, initial)
  assert.ok(pages.length > 0)
  assert.ok(pages.every(page => page.body && page.reason && page.outcome))
  assert.doesNotMatch(pages.map(page => `${page.title}${page.body}${page.reason}${page.outcome}`).join(' '), /部署|法力|DTO|matchId|权威/)
})
check('explains card activation versus effect activation without Angus exception', () => {
  const text = helpers.responseTutorialDialogue('response-choose', initial).map(page => `${page.body} ${page.reason} ${page.outcome}`).join(' ')
  assert.match(text, /卡的发动与效果的发动/); assert.match(text, /不是抵挡进攻|不是一次进攻/)
})
const prompt = { promptId: 'cost-"-id', playerIndex: 0, kind: 'discard-cost', validChoices: [discarded.instanceId], minChoose: 1, maxChoose: 1 }
const costFacts = helpers.responseTutorialFacts(game(undefined, { prompts: [prompt] }))
check('discard recommendation is own visible hand and current valid choice only', () => {
  assert.equal(costFacts.discard.instanceId, discarded.instanceId)
  const text = helpers.responseTutorialDialogue('response-discard', costFacts)[0].body
  assert.match(text, /韩信/); assert.match(text, /不需要再支付/)
  assert.equal(helpers.responseTutorialFacts(game(undefined, { prompts: [{ ...prompt, playerIndex: 1 }] })).discard, null)
})
const dom = { selected: false, playButton: false, availableSlots: false, endTurn: false, selectedPromptChoice: false }
check('prompt card and confirmation bind exact prompt, escaped safely', () => {
  const action = helpers.responseTutorialAction('response-discard', costFacts, dom)
  assert.match(action.anchor.selector, /cost-\\"-id/); assert.match(action.anchor.selector, /discard-a/)
  const confirm = helpers.responseTutorialAction('response-discard', costFacts, { ...dom, selectedPromptChoice: true })
  assert.match(confirm.anchor.selector, /confirm-prompt/); assert.doesNotMatch(confirm.anchor.selector, /data-tutorial-choice/)
})
check('placement points to front legion and back covered counter only', () => {
  assert.match(helpers.responseTutorialAction('response-legion', initial, { ...dom, availableSlots: true }).anchor.selector, /data-tutorial-row="0"/)
  assert.match(helpers.responseTutorialAction('response-cover', initial, { ...dom, availableSlots: true }).anchor.selector, /data-tutorial-row="1"/)
})
check('result keeps paid costs and automatically continues only after the real result', () => {
  const page = helpers.responseTutorialDialogue('completed', initial)[0]
  assert.match(page.body, /不会自动退回/); assert.match(page.outcome, /结果已提交.*自动进入下一步/)
  assert.doesNotMatch(page.outcome, /确认结果.*才记录|继续主线/)
})
check('unknown step waits without providing an action or completion', () => {
  const page = helpers.responseTutorialDialogue('unknown-future', initial)[0]
  assert.equal(page.anchor, null); assert.match(page.outcome, /不会/)
})
const progress = await import(toModule(progressFile))
function storage() {
  const values = new Map()
  return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) }
}
function completion(account, lessonId, extra = {}) {
  return { accountId: account, sessionId: 'session', connectionGeneration: 1,
    matchId: `match-${lessonId}`, lessonId, version: 1, stepId: 'completed', status: 'completed', ...extra }
}
function review(account, lessonId, store, extra = {}) {
  return progress.recordGuidedSceneReview(completion(account, lessonId, extra), {
    accountId: account, sessionId: 'session', connectionGeneration: 1, matchId: `match-${lessonId}`,
  }, store)
}
function firstChapter(account, store) {
  progress.markGuidedIntroductionRead(account, store)
  for (const id of ['deploy', 'move', 'combat']) assert.equal(review(account, id, store), true)
}
check('response review cannot skip first chapter', () => {
  const store = storage()
  assert.equal(review('chapter-required', 'response', store), false)
  progress.markGuidedIntroductionRead('chapter-required', store)
  assert.equal(review('chapter-required', 'response', store), false)
})
check('first chapter remains three scenes and never implies second chapter completion', () => {
  const store = storage(); firstChapter('first-not-response', store)
  assert.deepEqual(progress.loadGuidedTutorialProgress('first-not-response', store), {
    version: 2, introductionRead: true, reviewedScenes: ['deploy', 'move', 'combat'],
  })
})
check('explicit response review adds independent completion without changing first chapter prefix', () => {
  const store = storage(); firstChapter('response-review', store)
  assert.equal(review('response-review', 'response', store), true)
  assert.deepEqual(progress.loadGuidedTutorialProgress('response-review', store), {
    version: 2, introductionRead: true, reviewedScenes: ['deploy', 'move', 'combat'], responseReviewed: true,
  })
})
check('old progress and malformed response flag cannot promote an unfinished course', () => {
  const store = storage(); const account = 'malformed-response'
  store.setItem(progress.guidedTutorialStorageKey(account), JSON.stringify({ version: 2, introductionRead: true,
    reviewedScenes: ['deploy', 'response', 'combat'], responseReviewed: true }))
  assert.deepEqual(progress.loadGuidedTutorialProgress(account, store), { version: 2, introductionRead: true, reviewedScenes: ['deploy'] })
})
check('response progress keeps full account connection and result binding', () => {
  const store = storage(); firstChapter('bound-response', store)
  for (const extra of [{ accountId: 'other' }, { sessionId: 'other' }, { connectionGeneration: 2 },
    { matchId: 'other' }, { status: 'active' }, { stepId: 'response-retry' }])
    assert.equal(review('bound-response', 'response', store, extra), false)
  assert.equal(progress.loadGuidedTutorialProgress('bound-response', store).responseReviewed, undefined)
})
check('relearning clears response completion together with first chapter', () => {
  const store = storage(); firstChapter('reset-response', store); review('reset-response', 'response', store)
  assert.equal(progress.restartGuidedTutorial('reset-response', store), true)
  assert.deepEqual(progress.loadGuidedTutorialProgress('reset-response', store), { version: 2, introductionRead: false, reviewedScenes: [] })
})
check('unsaved response review stays account-local and survives later readable merge', () => {
  const store = storage(); firstChapter('unsaved-response', store)
  const denied = { getItem: store.getItem, setItem() { throw new Error('synthetic denied write') } }
  assert.equal(review('unsaved-response', 'response', denied), false)
  assert.equal(progress.loadGuidedTutorialProgress('unsaved-response', store).responseReviewed, true)
  assert.equal(progress.loadGuidedTutorialProgress('other-response', store).responseReviewed, undefined)
})
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')
fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status: 'passed', checks,
  sources: [guidanceFile, responseFile, progressFile].map(file => ({ file, sha256: hash(file) })),
  scope: 'pure teaching guidance only; no backend lesson, real UI or deployment claim' }, null, 2))
console.log(`Response teaching guidance: ${checks.length} checks passed`)
