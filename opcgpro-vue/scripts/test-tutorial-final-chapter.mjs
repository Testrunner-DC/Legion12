import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
import crypto from 'node:crypto'
import path from 'node:path'
import ts from 'typescript'

const front = path.resolve(import.meta.dirname, '..'), repo = path.dirname(front)
const output = process.env.L12_TUTORIAL_FINAL_OUTPUT
  || (process.env.L12_ARTIFACT_ROOT && path.join(process.env.L12_ARTIFACT_ROOT, 'tutorial-final-chapter-current'))
assert(output, 'Explicit governed evidence root is required')
execFileSync(process.env.L12_PWSH || 'pwsh', ['-NoProfile', '-Command',
  `. '${repo}/ops/windows/L12GeneratedStorage.ps1'; $null=Assert-L12GeneratedPath -Path $env:L12_TUTORIAL_FINAL_OUTPUT -Purpose 'final chapter focused evidence'`],
{ env: { ...process.env, L12_TUTORIAL_FINAL_OUTPUT: output }, stdio: 'pipe' })
fs.mkdirSync(output, { recursive: true })
const files = ['src/l12/tutorial/tutorialFinalChapter.ts', 'src/l12/tutorial/tutorialProgress.ts', 'src/l12/tutorial/tutorialCourse.ts',
  'src/l12/tutorial/tutorialGuidance.ts']
const moduleUrl = (file, replacements = []) => {
  const result = ts.transpileModule(fs.readFileSync(path.join(front, file), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.ES2022, target: ts.ScriptTarget.ES2022 }, reportDiagnostics: true,
  })
  assert.equal(result.diagnostics.length, 0)
  let js = result.outputText
  for (const [from, to] of replacements) js = js.replace(from, to)
  return 'data:text/javascript;base64,' + Buffer.from(js).toString('base64')
}
const h = await import(moduleUrl(files[0], [["'./tutorialGuidance'", JSON.stringify(moduleUrl(files[3]))]]))
const p = await import(moduleUrl(files[1])), c = await import(moduleUrl(files[2]))
const cases = []
const check = (name, run) => { run(); cases.push({ name, passed: true }) }
const storage = () => { const data = new Map(); return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value) } }
const owner = { accountId: 'new-final', sessionId: 'session', connectionGeneration: 3, matchId: 'match' }
const review = (lessonId, store, extra = {}) => p.recordGuidedSceneReview({ ...owner, roomCode: 'room', lessonId,
  version: lessonId === 'response' ? 2 : 1, ...(lessonId === 'response' ? { recipeId: 'public-response-v1' } : {}),
  stepId: 'completed', status: 'completed', ...extra }, owner, store)
const earlier = store => { p.restartGuidedTutorial(owner.accountId, store); p.markGuidedIntroductionRead(owner.accountId, store)
  for (const id of ['deploy', 'move', 'combat', 'response']) assert.equal(review(id, store), true) }
check('six internal scenes remain three chapters and advance only once in order', () => {
  assert.equal(c.tutorialChapters.length, 3)
  assert.deepEqual(c.tutorialSceneSequence, ['deploy', 'move', 'combat', 'response', 'preparation', 'finale'])
  for (let i = 0; i < c.tutorialSceneSequence.length; i++) assert.equal(c.nextTutorialScene(c.tutorialSceneSequence[i]), c.tutorialSceneSequence[i + 1] ?? null)
  assert.equal(c.nextTutorialScene('future'), null)
})
check('preparation is not final completion and finale cannot skip it', () => {
  const store = storage(); earlier(store)
  assert.equal(review('finale', store), false)
  assert.equal(review('preparation', store), true)
  assert.equal(p.loadGuidedTutorialProgress(owner.accountId, store).finaleReviewed, undefined)
  assert.equal(review('finale', store), true)
  assert.equal(p.loadGuidedTutorialProgress(owner.accountId, store).finaleReviewed, true)
})
check('active defeat, wrong step, version or identity cannot count as final completion', () => {
  const store = storage(); earlier(store); review('preparation', store)
  for (const extra of [{ status: 'active' }, { stepId: 'finale-retry' }, { version: 2 }, { accountId: 'other' },
    { sessionId: 'other' }, { connectionGeneration: 4 }, { matchId: 'other' }]) assert.equal(review('finale', store, extra), false)
  assert.equal(p.loadGuidedTutorialProgress(owner.accountId, store).finaleReviewed, undefined)
})
check('orphan final flags cannot bypass the public response recipe', () => {
  const store = storage(), account = 'forged-final'
  store.setItem(p.guidedTutorialStorageKey(account), JSON.stringify({ version: 2, introductionRead: true,
    reviewedScenes: ['deploy', 'move', 'combat'], responseReviewed: true, preparationReviewed: true, finaleReviewed: true }))
  assert.deepEqual(p.loadGuidedTutorialProgress(account, store), { version: 2, introductionRead: true, reviewedScenes: ['deploy', 'move', 'combat'] })
})
check('unsaved final progress remains account-local and survives safe retry', () => {
  const store = storage(); earlier(store); review('preparation', store)
  const denied = { getItem: store.getItem, setItem() { throw new Error('synthetic quota') } }
  assert.equal(review('finale', denied), false)
  assert.equal(p.loadGuidedTutorialProgress(owner.accountId, store).finaleReviewed, true)
  assert.equal(p.loadGuidedTutorialProgress('other-account', store).finaleReviewed, undefined)
  assert.equal(p.retryGuidedTutorialProgress(owner.accountId, store), true)
  assert.equal(JSON.parse(store.getItem(p.guidedTutorialStorageKey(owner.accountId))).finaleReviewed, true)
  p.restartGuidedTutorial(owner.accountId, store)
  assert.equal(p.loadGuidedTutorialProgress(owner.accountId, store).finaleReviewed, undefined)
})
const attacker = { instanceId: 'attacker', name: '食铁兽', cardId: 'ST01-05', cardType: 'legion', hidden: false }
const own = { playerIndex: 0, field: [[attacker, null, null], [null, null, null]], master: { hp: 8 } }
const enemy = { playerIndex: 1, master: { hp: 2 } }
const game = { you: 0, activePlayer: 0, phase: 'Main', players: [own, enemy], prompts: [], legalAttackTargets: { attacker: ['master'] } }
check('final attack hint uses exact authoritative targets, never a local attack rule', () => {
  assert.equal(h.finaleAttacker(game), attacker)
  assert.equal(h.finaleAttacker({ ...game, legalAttackTargets: {} }), null)
  assert.equal(h.finaleAttacker({ ...game, legalAttackTargets: { attacker: ['other-legion'] } }), null)
  assert.equal(h.finaleAttacker({ ...game, players: [{ ...own, field: [[{ ...attacker, hidden: true }]] }, enemy] }), null)
})
check('final hints never inspect opponent hidden cards or mutate state', () => {
  const hiddenEnemy = { ...enemy }
  for (const key of ['hand', 'field', 'library']) Object.defineProperty(hiddenEnemy, key, { get() { throw new Error('opponent hidden read') } })
  const state = { ...game, players: [own, hiddenEnemy] }, before = JSON.stringify(game)
  assert(h.finaleAction(state, { selected: false, attackButton: false, targetMaster: false }).body.includes('2'))
  assert.equal(JSON.stringify(game), before)
})
check('preparation includes real resource exceptions, disaster timing and actual mulligan', () => {
  for (const kind of ['initiative', 'disaster-ban', 'disaster-reveal', 'disaster-pick']) {
    const state = { ...game, prompts: [{ promptId: 'prompt', playerIndex: 0, kind }] }
    assert.equal(h.preparationDialogue(state).length, 1)
    assert(h.preparationAction(state, false).anchor.selector.includes('prompt'))
  }
  assert.match(h.preparationDialogue({ ...game, phase: 'Mulligan' })[0].body, /40张主卡和8张士气/)
  assert.match(h.preparationAction({ ...game, phase: 'Mulligan' }, false).anchor.selector, /confirm-mulligan/)
})
check('final chapter has one introduction and no repeated reading gate for each attack', () => {
  assert.equal(h.finaleDialogue('finale-ready', game).length, 1)
  assert.equal(h.finaleDialogue('attack', game).length, 0)
  assert.equal(h.finaleDialogue('end-turn', game).length, 0)
})
fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status: 'passed', cases,
  sources: files.map(file => ({ file, sha256: crypto.createHash('sha256').update(fs.readFileSync(path.join(front, file))).digest('hex') })),
  boundary: 'focused pure guidance/progress only, not actual room or full-course UI acceptance' }, null, 2))
console.log(`Final chapter: ${cases.length} checks passed`)
