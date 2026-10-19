import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const front = path.resolve(scriptDir, '..')
const repo = path.resolve(front, '..')
const output = process.env.L12_TUTORIAL_PROGRESS_OUTPUT
  || (process.env.L12_ARTIFACT_ROOT ? path.join(process.env.L12_ARTIFACT_ROOT, 'tutorial-progress-recovery-20261019') : '')
assert.ok(output, 'L12_TUTORIAL_PROGRESS_OUTPUT or initialized L12_ARTIFACT_ROOT is required')
const powershell = process.env.SystemRoot
  ? path.join(process.env.SystemRoot, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe') : 'powershell.exe'
execFileSync(powershell, ['-NoProfile', '-Command',
  `. '${path.join(repo, 'ops/windows/L12GeneratedStorage.ps1').replaceAll("'", "''")}'; `
  + '$null=Assert-L12GeneratedPath -Path $env:L12_TUTORIAL_PROGRESS_OUTPUT -Purpose "tutorial progress recovery evidence"'], {
  env: { ...process.env, L12_TUTORIAL_PROGRESS_OUTPUT: output }, stdio: 'pipe',
})
fs.mkdirSync(output, { recursive: true })

const sourcePath = path.join(front, 'src/l12/tutorial/tutorialProgress.ts')
const source = fs.readFileSync(sourcePath, 'utf8')
const transpiled = ts.transpileModule(source, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 },
  reportDiagnostics: true, fileName: sourcePath,
})
assert.equal(transpiled.diagnostics?.length ?? 0, 0)
const progress = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString('base64')}`)

class MemoryStorage {
  values = new Map()
  writes = 0
  getItem(key) { return this.values.get(key) ?? null }
  setItem(key, value) { this.writes++; this.values.set(key, value) }
}
const denied = storage => ({
  getItem() { throw new Error('synthetic read denial') },
  setItem() { storage.writes++; assert.fail('unreadable storage must not be overwritten') },
})
const state = (introductionRead, reviewedScenes) => ({ version: 2, introductionRead, reviewedScenes })
const projection = (accountId, lessonId, overrides = {}) => ({ lessonId, version: 1, stepId: 'completed', status: 'completed',
  accountId, sessionId: `${accountId}-session`, connectionGeneration: 7, roomCode: 'ROOM', matchId: `${accountId}-${lessonId}`, ...overrides })
const binding = (accountId, lessonId, overrides = {}) => ({ accountId, sessionId: `${accountId}-session`,
  connectionGeneration: 7, matchId: `${accountId}-${lessonId}`, ...overrides })
const seed = (storage, accountId, value) => storage.setItem(progress.guidedTutorialStorageKey(accountId), JSON.stringify(value))
const stored = (storage, accountId) => JSON.parse(storage.getItem(progress.guidedTutorialStorageKey(accountId)))
const results = []
function check(name, fn) { fn(); results.push({ name, status: 'passed' }) }

check('recovered readable storage with the longer prefix wins without being overwritten', () => {
  const accountId = 'persistent-longer', storage = new MemoryStorage(), blocked = denied(storage)
  assert.equal(progress.markGuidedIntroductionRead(accountId, blocked), false)
  assert.equal(progress.recordGuidedSceneReview(projection(accountId, 'deploy'), binding(accountId, 'deploy'), blocked), false)
  seed(storage, accountId, state(true, ['deploy', 'move']))
  const writesBefore = storage.writes
  assert.deepEqual(progress.loadGuidedTutorialProgress(accountId, storage), state(true, ['deploy', 'move']))
  assert.equal(storage.writes, writesBefore)
  assert.equal(progress.recordGuidedSceneReview(projection(accountId, 'combat'), binding(accountId, 'combat'), storage), true)
  assert.deepEqual(stored(storage, accountId), state(true, ['deploy', 'move', 'combat']))
})

check('longer unsaved memory stays monotonic and persists after storage recovers', () => {
  const accountId = 'memory-longer', storage = new MemoryStorage(), blocked = denied(storage)
  assert.equal(progress.markGuidedIntroductionRead(accountId, blocked), false)
  for (const lessonId of ['deploy', 'move'])
    assert.equal(progress.recordGuidedSceneReview(projection(accountId, lessonId), binding(accountId, lessonId), blocked), false)
  seed(storage, accountId, state(true, ['deploy']))
  assert.deepEqual(progress.loadGuidedTutorialProgress(accountId, storage), state(true, ['deploy', 'move']))
  assert.equal(progress.markGuidedIntroductionRead(accountId, storage), true)
  assert.deepEqual(stored(storage, accountId), state(true, ['deploy', 'move']))
})

check('unsaved state survives a room change and retries only the same account', () => {
  const accountId = 'room-left-unsaved', otherAccount = 'room-left-other'
  const storage = new MemoryStorage(), blocked = denied(storage)
  assert.equal(progress.markGuidedIntroductionRead(accountId, blocked), false)
  assert.deepEqual(progress.guidedTutorialPersistenceState(accountId, blocked), { unsaved: true })
  assert.deepEqual(progress.guidedTutorialPersistenceState(otherAccount, storage), { unsaved: false })
  assert.equal(progress.retryGuidedTutorialProgress(otherAccount, storage), true)
  assert.equal(storage.getItem(progress.guidedTutorialStorageKey(accountId)), null,
    'another account must not flush the pending tutorial progress')
  assert.equal(progress.retryGuidedTutorialProgress(accountId, storage), true)
  assert.deepEqual(stored(storage, accountId), state(true, []))
  assert.deepEqual(progress.guidedTutorialPersistenceState(accountId, storage), { unsaved: false })
})

check('failed retry remains visible and a later retry is idempotent', () => {
  const accountId = 'retry-visible', storage = new MemoryStorage(), blocked = denied(storage)
  assert.equal(progress.markGuidedIntroductionRead(accountId, blocked), false)
  assert.equal(progress.retryGuidedTutorialProgress(accountId, blocked), false)
  assert.deepEqual(progress.guidedTutorialPersistenceState(accountId, blocked), { unsaved: true })
  assert.equal(progress.retryGuidedTutorialProgress(accountId, storage), true)
  const writes = storage.writes
  assert.equal(progress.retryGuidedTutorialProgress(accountId, storage), true)
  assert.equal(storage.writes, writes, 'an already persisted account must not be written twice')
})

check('failed explicit reset cannot revive old storage when reads recover', () => {
  const accountId = 'pending-reset', storage = new MemoryStorage()
  seed(storage, accountId, state(true, ['deploy', 'move', 'combat']))
  assert.equal(progress.restartGuidedTutorial(accountId, denied(storage)), false)
  assert.deepEqual(progress.loadGuidedTutorialProgress(accountId, storage), state(false, []))
  assert.deepEqual(stored(storage, accountId), state(true, ['deploy', 'move', 'combat']))
  assert.equal(progress.markGuidedIntroductionRead(accountId, storage), true)
  assert.deepEqual(stored(storage, accountId), state(true, []))
})

check('duplicate completion is stable and wrong identity cannot write', () => {
  const accountId = 'identity', otherAccount = 'other', storage = new MemoryStorage()
  assert.equal(progress.markGuidedIntroductionRead(accountId, storage), true)
  assert.equal(progress.recordGuidedSceneReview(projection(accountId, 'deploy'), binding(accountId, 'deploy'), storage), true)
  const expected = JSON.stringify(stored(storage, accountId))
  assert.equal(progress.recordGuidedSceneReview(projection(accountId, 'deploy'), binding(accountId, 'deploy'), storage), true)
  assert.equal(progress.recordGuidedSceneReview(projection(accountId, 'move'), binding(accountId, 'move', { sessionId: 'wrong' }), storage), false)
  assert.equal(JSON.stringify(stored(storage, accountId)), expected)
  assert.deepEqual(progress.loadGuidedTutorialProgress(otherAccount, storage), state(false, []))
})

const report = { status: 'passed', total: results.length, failed: 0, results,
  sourceSha256: crypto.createHash('sha256').update(source).digest('hex'),
  boundary: 'pure local progress recovery contract; no browser, build, server, network, Git or deployment' }
const reportPath = path.join(output, 'report.json')
fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`)
console.log(JSON.stringify({ status: report.status, total: report.total, reportPath,
  reportSha256: crypto.createHash('sha256').update(fs.readFileSync(reportPath)).digest('hex') }))
