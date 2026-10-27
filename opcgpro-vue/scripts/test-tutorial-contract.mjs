import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import ts from 'typescript'
import { parse, compileScript } from '@vue/compiler-sfc'
import { createServer } from 'vite'
import { computed, nextTick, reactive, ref, watch } from 'vue'

const front = path.resolve(import.meta.dirname, '..')
const runId = new Date().toISOString().replace(/[:.]/g, '-') + '-' + crypto.randomUUID().slice(0, 8)
const ciRoot = process.env.GITHUB_ACTIONS === 'true' && process.env.RUNNER_TEMP
  ? path.resolve(process.env.RUNNER_TEMP, 'l12-tutorial-contracts') : null
const output = process.env.L12_TUTORIAL_OUTPUT
  || (process.env.L12_ARTIFACT_ROOT && path.join(process.env.L12_ARTIFACT_ROOT, 'tutorial-build-20261018/frontend/contracts-' + runId))
  || (ciRoot && path.join(ciRoot, 'evidence', runId))
const cache = process.env.L12_TUTORIAL_VITE_CACHE
  || (process.env.L12_WORK_CACHE && path.join(process.env.L12_WORK_CACHE, 'tutorial-build-20261018/frontend/contracts-current'))
  || (ciRoot && path.join(ciRoot, 'cache'))
assert.ok(output && cache, 'Initialize L12 generated storage, or select explicit tutorial output and cache roots')
execFileSync(process.env.L12_PWSH || (process.platform === 'win32' ? 'pwsh.exe' : 'pwsh'), ['-NoProfile', '-NonInteractive', '-Command',
  '. $env:L12_INITIALIZER -CacheRoot $env:L12_TUTORIAL_VITE_CACHE | Out-Null; . $env:L12_GUARD; '
    + '@($env:L12_TUTORIAL_OUTPUT,$env:L12_TUTORIAL_VITE_CACHE,$env:TEMP) | ForEach-Object { '
    + '$null=Assert-L12GeneratedPath -Path $_ -Purpose "tutorial focused UI contract" }'], {
  stdio: 'pipe',
  env: { ...process.env,
    L12_TUTORIAL_OUTPUT: output,
    L12_TUTORIAL_VITE_CACHE: cache,
    L12_INITIALIZER: path.join(front, '../ops/windows/Initialize-L12BuildEnvironment.ps1'),
    L12_GUARD: path.join(front, '../ops/windows/L12GeneratedStorage.ps1') },
})
fs.mkdirSync(output, { recursive: true })

const files = {
  types: 'src/l12/types.ts', net: 'src/l12/net.ts', game: 'src/l12/GamePage.vue',
  board: 'src/l12/game/GameBoard.vue', combatMotion: 'src/l12/game/CombatMotionPresentationLayer.vue',
  hub: 'src/l12/site/BattleHubPage.vue', page: 'src/l12/site/TutorialPage.vue',
  overlay: 'src/l12/tutorial/TutorialGuidanceOverlay.vue', progress: 'src/l12/tutorial/tutorialProgress.ts',
  course: 'src/l12/tutorial/tutorialCourse.ts', guide: 'src/l12/tutorial/tutorialGuidance.ts',
  router: 'src/router/index.ts', admin: 'src/l12/site/adminSections.ts', sandbox: 'src/l12/site/SandboxPage.vue',
  script: 'scripts/test-tutorial-contract.mjs',
  finalChapter: 'src/l12/tutorial/tutorialFinalChapter.ts', finalChapterTests: 'scripts/test-tutorial-final-chapter.mjs',
}
const source = Object.fromEntries(Object.entries(files).map(([key, relative]) =>
  [key, fs.readFileSync(path.join(front, relative), 'utf8')]))
const cases = []
function check(name, work) { work(); cases.push(name); console.log(`PASS ${name}`) }

check('one continuous first chapter opens inline without a second directory link', () => {
  assert.match(source.hub, /<TutorialPage v-else embedded\s*\/>/)
  assert.doesNotMatch(source.hub + source.page, /进入教学目录|lesson-grid|开始本关|三关可任选/)
  for (const chapter of ['初识十二军团', '卡牌效果与响应', '完成一场对局']) assert.ok(source.course.includes(chapter))
  assert.match(source.page, /你已完成新手教学/)
  for (const id of ['goal', 'cards', 'hand', 'morale', 'practice']) assert.match(source.course, new RegExp(`id: '${id}'`))
  assert.doesNotMatch(source.page + source.overlay, /questionOpen|我想了解|回到主线/)
  const introductionTemplate = source.page.slice(source.page.indexOf('<section v-if="introductionOpen"'), source.page.indexOf('<section v-else-if'))
  const mainActions = introductionTemplate.slice(introductionTemplate.indexOf('<div class="conversation-choices">'))
  assert.equal((mainActions.match(/<button/g) ?? []).length, 1, 'Each introduction step has one main action')
})
check('introductions explain goals, card types, resource origins and tradeoffs in Legion12 terms', () => {
  for (const text of ['血量降至0', '主宰', '军团', '战术', '圣物', '手牌来自牌库', '牌库顶部抽1张',
    '先攻首回合不抽', '士气牌库', '追加2张', '追加1张', '重置阶段', '费用', '兵力', '效果']) assert.ok(source.course.includes(text), text)
  assert.doesNotMatch(source.page + source.course + source.overlay + source.guide, /部署|法力|召唤/)
  const template = parse(source.overlay, { filename: files.overlay }).descriptor.template.content
  assert.doesNotMatch(template, /\{\{[^}]*\b(?:instanceId|cardInstanceId|lessonId|stepId)\b/)
  assert.match(source.page, /CatalogCardDetails/)
  assert.match(source.page, /loadDeckCatalog/)
  assert.match(source.page, /const labels = new Set\(\['费用', '兵力', '血量'\]\)/, 'Highlight only fields present in the shared details, including real master HP')
  assert.match(source.page, /:aria-label="`查看\$\{card\.name\}卡牌详情`"/)
  assert.doesNotMatch(source.page, /<dt>费用<\/dt>|<dt>兵力<\/dt>|<b>效果<\/b>/, '教学页不得复制卡牌详情')
})
check('guide stays on the real board with linear dialogue and non-intercepting focus', () => {
  assert.doesNotMatch(source.page + source.overlay + source.game, /继续主线|继续教学主线/)
  const template = parse(source.overlay, { filename: files.overlay }).descriptor.template.content
  assert.doesNotMatch(template, /\{\{\s*tutorial\.(?:lessonId|stepId)|服务器权威|不会通关|判定说明/)
  assert.doesNotMatch(template, /tutorial-help|tutorial-cause-and-result|为什么|会发生什么/)
  assert.doesNotMatch(template, /tutorial-result-line/)
  assert.match(source.overlay, /pointer-events:\s*none/)
  assert.match(source.overlay, /44px/)
  assert.match(source.overlay, /<BattleOverlayPortal force>/, 'Teaching must share the prompt teleport host even on desktop')
  assert.match(source.overlay, /continueMainline/)
  assert.match(source.game, /@continue-mainline="continueTutorialMainline"/)
  assert.match(source.overlay, /acceptedHandoffIdentity/)
  assert.doesNotMatch(source.overlay, /completedHandoffKey|presentationInFlight/)
  assert.match(source.overlay, /props\.tutorial\.stepId !== 'completed'/)
  assert.match(source.overlay, /props\.connected/)
  assert.match(source.game, /:connected="l12State\.status === 'online'"/)
  assert.match(source.game, /:busy="Boolean\(tutorialTransition \|\| l12State\.pendingAction\)" :presentation-busy="tutorialPresentationBusy"/)
  assert.match(source.game, /:key="game\.matchId"[\s\S]*@presentation-change="battlePresentationListener"/)
  assert.doesNotMatch(template, /tutorial\.status === 'completed'[\s\S]{0,220}<button[^>]*tutorial-primary/)
  assert.match(source.page + source.overlay, /下一步/)
})
check('live presentation busy is exposed without changing replay speed gating', () => {
  assert.match(source.board, /presentationChange: \[busy: boolean\]/)
  assert.match(source.board, /const cardPresentationBusy = computed/)
  assert.match(source.board, /watch\(cardPresentationBusy, busy => emit\('presentationChange', busy\)/)
  assert.match(source.board, /const replayCardPresentationBusy = computed\(\(\) => Boolean\(props\.replayPlaybackSpeed && \(/)
  assert.match(source.combatMotion, /emit\('presentationChange', Boolean\(preparationCount \|\| animations\.size\)\)/)
  assert.match(source.combatMotion, /emit\('busyChange', Boolean\(props\.playbackSpeed && animations\.size\)\)/)
})
check('tutorial entry stays independent while personal sandbox requires a valid account', () => {
  assert.ok(source.router.includes("path: '/battle/tutorial'"))
  assert.match(source.router, /path: '\/battle\/tutorial'[\s\S]{0,180}BattleHubPage\.vue'[\s\S]{0,120}props: \{ initialTab: 'tutorial' \}/)
  assert.doesNotMatch(source.router, /path: '\/battle\/tutorial'[\s\S]{0,180}TutorialPage\.vue'/)
  assert.match(source.hub, /initialTab\?: 'match' \| 'friendly' \| 'tutorial'/)
  assert.match(source.hub, /watch\(\(\) => props\.initialTab, next => \{ tab\.value = next \}, \{ immediate: true \}\)/)
  assert.match(source.router, /path: '\/sandbox'[\s\S]{0,180}SandboxPage\.vue'[\s\S]{0,120}requiresAccount: true/)
  assert.match(source.router, /path: '\/admin\/operations\/sandbox'[\s\S]{0,180}redirect: to => \(\{ path: '\/sandbox', query: to\.query, hash: to\.hash \}\)/)
  assert.doesNotMatch(source.admin, /id: 'sandbox'/)
  assert.match(source.sandbox, /authState\.verified && !!platformState\.token && !!platformState\.account\?\.id/)
  assert.match(source.sandbox, /!platformState\.account\.mustChangePassword && !platformState\.account\.mustChangeUsername/)
  assert.doesNotMatch(source.sandbox, /hasPermission\('admin\.operations\.write'\)/)
})
check('wire request is exact and has no client fixture authority', () => {
  assert.match(source.net, /pendingActionEnvelope = \{ type: 'createTutorial', lessonId, requestId \}/)
  const fn = source.net.slice(source.net.indexOf('export function createTutorial('), source.net.indexOf('export const joinRoom'))
  assert.doesNotMatch(fn, /deck|actor|seed|checkpoint|gm|setTimeout/)
  assert.match(fn, /l12State\.matchmaking\?\.queued/)
})
check('all tutorial and sandbox wire failures enter the real pending-action consumer', () => {
  for (const type of ['tutorialRejected', 'tutorialUnavailable', 'tutorialRequestExpired', 'tutorialRestartRequired', 'sandboxForbidden'])
    assert.ok(source.net.includes(`message.type === '${type}'`) || source.net.includes(`message.type === '${type}'`))
})
check('tutorial capability conflicts fail closed without enabling GM or referee controls', () => {
  assert.match(source.net, /tutorialCapabilityConflict = Boolean\(projectedTutorial && \(l12State\.gmEnabled \|\| l12State\.spectating\)\)/)
  assert.match(source.net, /if \(tutorialCapabilityConflict\) \{\s*l12State\.gmEnabled = false\s*l12State\.refereeControlsEnabled = false/)
})
check('completion is bound to authoritative completed tutorial identity only', () => {
  assert.ok(source.page.includes('l12State.accountId === accountId.value'))
  assert.ok(source.page.includes('l12State.accountId === expectedAccountId'))
  assert.match(source.game, /current\?\.status !== 'completed'/)
  assert.match(source.game, /platformState\.account\?\.id === current\.accountId/)
  assert.match(source.game, /l12State\.connectionGeneration === current\.connectionGeneration/)
  assert.doesNotMatch(source.game, /recordTutorialCompletion\(/)
  assert.match(source.game, /recordGuidedSceneReview/)
  assert.match(source.game, /tutorialHandoffDecision/)
  assert.match(source.game, /acceptedTutorialHandoffIdentity/)
  assert.match(source.game, /if \(!beginTutorialHandoff\([\s\S]*?\)\) return/)
  assert.match(source.game, /acceptedTutorialHandoffIdentity\.value = identity/)
  assert.match(source.overlay, /props\.tutorial\.accountId[\s\S]*props\.tutorial\.sessionId[\s\S]*props\.tutorial\.connectionGeneration/)
  assert.match(source.overlay, /props\.tutorial\.roomCode[\s\S]*props\.tutorial\.matchId[\s\S]*props\.tutorial\.lessonId/)
  assert.match(source.game, /tutorialRoom \? '\/battle\/tutorial'/)
  assert.match(source.game, /sandboxRoom \? '\/sandbox'/)
  assert.match(source.page, /v-if="l12State\.notice"[^>]*role="alert"/)
  assert.match(source.page, /guidedTutorialPersistenceState/)
  assert.match(source.page, /retryGuidedTutorialProgress/)
  assert.match(source.page, /matchmakingBusy[\s\S]*请先取消当前匹配，再开始教学/)
  assert.match(source.page, /请先结束并退出当前对局，再开始教学/)
})
check('new SFC script blocks compile independently', () => {
  for (const name of ['page', 'overlay']) {
    const descriptor = parse(source[name], { filename: files[name] }).descriptor
    assert.ok(compileScript(descriptor, { id: `tutorial-${name}` }).content)
  }
})

const progressCompiled = ts.transpileModule(source.progress, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 }, reportDiagnostics: true,
})
assert.deepEqual((progressCompiled.diagnostics || []).filter(item => item.category === ts.DiagnosticCategory.Error), [])
const progress = await import('data:text/javascript;base64,' + Buffer.from(progressCompiled.outputText).toString('base64'))
class MemoryStorage {
  values = new Map()
  writes = 0
  getItem(key) { return this.values.get(key) ?? null }
  setItem(key, value) { this.writes++; this.values.set(key, String(value)) }
}
const projection = (overrides = {}) => ({ lessonId: 'deploy', version: 1, stepId: 'completed', status: 'completed',
  accountId: 'account-a', sessionId: 'session-a1', connectionGeneration: 11,
  roomCode: 'TUTOR1', matchId: 'match-a1', ...overrides })
const binding = (overrides = {}) => ({ accountId: 'account-a', sessionId: 'session-a1',
  connectionGeneration: 11, matchId: 'match-a1', ...overrides })

// Execute the exact setup functions/expressions with bounded dependencies, not
// a second implementation of handoff. DOM rendering is covered separately.
function setupNodes(sfc) {
  const setup = parse(sfc).descriptor.scriptSetup.content
  return ts.createSourceFile('setup.ts', setup, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
}
function actualFunctions(sfc, names, dependencies) {
  const tree = setupNodes(sfc)
  const selected = names.map(name => {
    const node = tree.statements.find(item => ts.isFunctionDeclaration(item) && item.name?.text === name)
    assert(node, `Actual setup function missing: ${name}`)
    return node.getText(tree)
  }).join('\n')
  const js = ts.transpileModule(selected, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  return Function(...Object.keys(dependencies), `${js}\nreturn {${names.join(',')}}`)(...Object.values(dependencies))
}
function actualInitializer(sfc, name, dependencies) {
  const tree = setupNodes(sfc)
  const declaration = tree.statements.filter(ts.isVariableStatement)
    .flatMap(item => [...item.declarationList.declarations]).find(item => item.name.getText(tree) === name)
  assert(declaration?.initializer, `Actual setup initializer missing: ${name}`)
  const js = ts.transpileModule(`const result = ${declaration.initializer.getText(tree)}`, {
    compilerOptions: { target: ts.ScriptTarget.ES2022 },
  }).outputText
  return Function(...Object.keys(dependencies), `${js}\nreturn result`)(...Object.values(dependencies))
}
check('actual match-bound presentation listener preserves tutorial busy without accepting stale matches', async () => {
  const l12State = reactive({ game: { matchId: 'tutorial-a', phase: 'Main' } })
  const game = computed(() => l12State.game)
  const tutorialMatchId = ref('tutorial-a')
  const tutorial = computed(() => game.value.matchId === tutorialMatchId.value ? { matchId: tutorialMatchId.value } : null)
  const tutorialPresentationBusy = ref(false), gameOverMinimized = ref(false)
  const actual = actualFunctions(source.game, ['createGameOverPresentationHandoff'], {
    computed, game, gameOverMinimized, nextTick, ref, tutorial, tutorialPresentationBusy, watch,
  }).createGameOverPresentationHandoff()
  const firstHandler = actual.presentationListener.value
  firstHandler(true)
  assert.equal(tutorialPresentationBusy.value, true)
  firstHandler(false)
  assert.equal(tutorialPresentationBusy.value, false)

  l12State.game = { matchId: 'tutorial-b', phase: 'Main' }
  tutorialMatchId.value = 'tutorial-b'
  await nextTick()
  const secondHandler = actual.presentationListener.value
  assert.notEqual(secondHandler, firstHandler)
  firstHandler(true)
  assert.equal(tutorialPresentationBusy.value, false, 'old board callback cannot mark the current tutorial busy')
  secondHandler(true)
  assert.equal(tutorialPresentationBusy.value, true, 'current tutorial still consumes the real board busy signal')
  secondHandler(false)
  assert.equal(tutorialPresentationBusy.value, false)
})
check('actual wire parser accepts only the named public response recipe and bounded focus', () => {
  const parser = actualFunctions(`<script setup lang="ts">${source.net}</script>`, ['tutorialProjection'], {
    tutorialLessonIds: new Set(['deploy', 'move', 'combat', 'response']),
  }).tutorialProjection
  const value = { lessonId: 'response', version: 2, recipeId: 'public-response-v1',
    stepId: 'response-artifact', status: 'active', focus: { artifactInstanceId: 'artifact',
      counterInstanceId: 'counter', costCardInstanceId: null, legionInstanceId: null } }
  assert.deepEqual(parser(value), value)
  for (const extra of [{ recipeId: 'other' }, { version: 3 }, { lessonId: 'combat' }, { focus: null },
    { focus: {} }, { focus: { ...value.focus, legionInstanceId: true } },
    { focus: { ...value.focus, legionInstanceId: ' ' } },
    { focus: { ...value.focus, legionInstanceId: 'a'.repeat(129) } }]) assert.equal(parser({ ...value, ...extra }), null)
  assert.equal(parser({ lessonId: 'deploy', version: 1, stepId: 'deploy', status: 'active' }).version, 1)
  assert.equal(parser({ ...value, version: 1 }).version, 1, 'Legacy wire identity is retained but not promoted to the new recipe')
})
check('actual board busy includes preparation without mounted animations and preserves legacy replay projection', () => {
  const inputs = { computed, props: reactive({ replayPlaybackSpeed: null }), hiddenRevealCard: ref(null),
    publicReveal: ref(null), diceReveal: ref(null), dicePresentationPending: ref(false),
    replayZonePresentationBusy: ref(false), replaySequencePresentationBusy: ref(false),
    replayCombatPresentationBusy: ref(false), combatPresentationBusy: ref(false) }
  const live = actualInitializer(source.board, 'cardPresentationBusy', inputs)
  const replay = actualInitializer(source.board, 'replayCardPresentationBusy', inputs)
  assert.equal(live.value, false)
  inputs.replayZonePresentationBusy.value = true
  assert.equal(live.value, true, 'preparing zone images must block before any animation DOM exists')
  assert.equal(replay.value, false, 'live teaching must not enable the replay clock')
  inputs.replayZonePresentationBusy.value = false
  inputs.replaySequencePresentationBusy.value = true
  assert.equal(live.value, true, 'a reserved sequence blocks handoff')
  inputs.replaySequencePresentationBusy.value = false
  inputs.combatPresentationBusy.value = true
  assert.equal(live.value, true)
  inputs.combatPresentationBusy.value = false
  inputs.dicePresentationPending.value = true
  inputs.props.replayPlaybackSpeed = 1
  assert.equal(live.value, true)
  assert.equal(replay.value, false, 'the new signal must not add dice to the legacy replay barrier')
})
check('actual combat notifier reports preparation for live teaching while keeping replay output unchanged', () => {
  const props = { playbackSpeed: null }, animations = new Set(), events = []
  const run = preparationCount => actualFunctions(source.combatMotion, ['notifyBusy'], {
    props, animations, preparationCount, emit: (name, busy) => events.push([name, busy]),
  }).notifyBusy()
  run(1)
  assert.deepEqual(events.splice(0), [['busyChange', false], ['presentationChange', true]])
  animations.add('synthetic-animation'); props.playbackSpeed = 2; run(0)
  assert.deepEqual(events.splice(0), [['busyChange', true], ['presentationChange', true]])
  animations.clear(); run(0)
  assert.deepEqual(events, [['busyChange', false], ['presentationChange', false]])
})
check('actual parent and overlay retry a rejected completion and accept duplicates only once', () => {
  const current = projection(), tutorial = ref(current), tutorialTransition = ref(null)
  const tutorialPresentationBusy = ref(true), acceptedTutorialHandoffIdentity = ref('')
  const l12State = { pendingAction: false, status: 'online', sessionId: current.sessionId,
    connectionGeneration: current.connectionGeneration, notice: '' }
  let readable = false, leaves = 0, emitted = 0
  const parent = actualFunctions(source.game, ['beginTutorialHandoff', 'tutorialCompletionIdentity', 'continueTutorialMainline'], {
    tutorial, tutorialTransition, tutorialPresentationBusy, acceptedTutorialHandoffIdentity, l12State,
    platformState: { account: { id: current.accountId }, token: 'synthetic' },
    game: ref({ matchId: current.matchId }), tutorialProgressUnsaved: ref(false), tutorialProgressRetryIdentity: ref(''),
    recordGuidedSceneReview: () => readable,
    loadGuidedTutorialProgress: () => ({ reviewedScenes: readable ? ['deploy'] : [] }),
    guidedTutorialPersistenceState: () => ({ unsaved: false }), nextTutorialScene: () => 'move',
    tutorialTransitionTimer: null, tutorialTransitionSlow: ref(false), setTimeout: () => 1,
    leaveRoom: () => { leaves++; l12State.pendingAction = true },
  })
  const key = parent.tutorialCompletionIdentity(current)
  const props = { tutorial: current, connected: true,
    get busy() { return Boolean(tutorialTransition.value || l12State.pendingAction) },
    get presentationBusy() { return tutorialPresentationBusy.value },
    get acceptedHandoffIdentity() { return acceptedTutorialHandoffIdentity.value } }
  const child = actualFunctions(source.overlay, ['attemptCompletedHandoff'], {
    props, completedIdentity: ref(key), stateAligned: ref(true), completedResultVisible: () => true,
    emit: () => { emitted++; parent.continueTutorialMainline() },
  })
  child.attemptCompletedHandoff()
  assert.equal(emitted, 0, 'busy preparation without DOM cannot request handoff')
  tutorialPresentationBusy.value = false
  child.attemptCompletedHandoff()
  assert.equal(leaves, 0); assert.equal(acceptedTutorialHandoffIdentity.value, '')
  readable = true
  child.attemptCompletedHandoff()
  assert.equal(leaves, 1); assert.equal(acceptedTutorialHandoffIdentity.value, key)
  tutorialTransition.value = null; l12State.pendingAction = false
  child.attemptCompletedHandoff()
  assert.equal(leaves, 1); assert.equal(emitted, 2, 'parent acknowledgement stops repeated completed snapshots')
})
check('reading and completion use separate real consumers while presentation release is watched', () => {
  const dialogueIndex = ref(0), dialogueDone = ref(false), instructionBlocked = ref(true)
  const child = actualFunctions(source.overlay, ['advanceDialogue'], { dialogueIndex, dialogueDone, instructionBlocked,
    props: { tutorial: { lessonId: 'deploy' } }, finaleIntroduced: ref(false) })
  child.advanceDialogue(); assert.equal(dialogueIndex.value, 0)
  instructionBlocked.value = false
  child.advanceDialogue(); assert.equal(dialogueIndex.value, 1)
  dialogueDone.value = true
  child.advanceDialogue(); assert.equal(dialogueIndex.value, 1)
  assert.match(source.overlay, /watch\(\(\) => \[completedIdentity\.value[\s\S]*?props\.presentationBusy[\s\S]*?\] as const, queueRefresh/)
  assert.match(source.game, /:busy="Boolean\(tutorialTransition \|\| l12State\.pendingAction\)" :presentation-busy="tutorialPresentationBusy"/)
})
check('finale automatically records once but keeps victory until the original exit action', () => {
  const current = projection({ lessonId: 'finale' }), tutorial = ref(current)
  const acceptedTutorialHandoffIdentity = ref('')
  const tutorialPresentationBusy = ref(true), tutorialTransition = ref(null)
  const l12State = reactive({ pendingAction: false, status: 'online', sessionId: current.sessionId,
    connectionGeneration: current.connectionGeneration })
  let actualExitGuard = ref(true)
  const tutorialFinaleExitBlocked = { get value() { return actualExitGuard.value } }
  let records = 0, exits = 0
  const actual = actualFunctions(source.game, ['tutorialCompletionIdentity', 'continueTutorialMainline', 'returnToLobby'], {
    tutorial, acceptedTutorialHandoffIdentity, tutorialFinaleExitBlocked, tutorialTransition, tutorialPresentationBusy, l12State,
    platformState: { account: { id: current.accountId } }, game: ref({ matchId: current.matchId }),
    tutorialProgressUnsaved: ref(false), tutorialProgressRetryIdentity: ref(''),
    recordGuidedSceneReview: () => { records++; return true }, loadGuidedTutorialProgress: () => ({ finaleReviewed: true }),
    guidedTutorialPersistenceState: () => ({ unsaved: false }),
    beginTutorialHandoff: target => { assert.equal(target, null); exits++; return true },
  })
  actualExitGuard = actualInitializer(source.game, 'tutorialFinaleExitBlocked', { computed, tutorial,
    tutorialPresentationBusy, l12State, tutorialTransition, acceptedTutorialHandoffIdentity,
    tutorialCompletionIdentity: actual.tutorialCompletionIdentity })
  actual.returnToLobby(); actual.continueTutorialMainline()
  assert.equal(records, 0); assert.equal(exits, 0, 'Early click while presentation is busy cannot leave or record')
  tutorialPresentationBusy.value = false
  actual.returnToLobby(); assert.equal(exits, 0, 'Presentation release alone is not a learning acknowledgement')
  actual.continueTutorialMainline(); actual.continueTutorialMainline()
  assert.equal(records, 1); assert.equal(exits, 0, 'Recording completion cannot dismiss the actual victory result')
  actual.returnToLobby(); assert.equal(exits, 1, 'The existing final exit uses the real roomLeft handoff')
  assert.match(source.overlay, /props\.tutorial\.lessonId === 'finale' \? '\.game-over\[role="dialog"\]'/)
})
check('completed response without a card anchor uses the visible own master instead of deadlocking', () => {
  const selections = [], element = new (class VisibleMaster {})()
  const result = actualFunctions(source.overlay, ['completedResultVisible'], {
    props: { tutorial: { lessonId: 'response' } },
    document: { querySelectorAll: selector => { selections.push(selector); return [element] } },
    HTMLElement: element.constructor, visibleGuide: ref({ anchor: null }),
    facts: ref({ self: { playerIndex: 1 } }), visibleViewport: () => ({ width: 844, height: 390 }),
    tutorialPlayerSelector: index => `[data-tutorial-player-index="${index}"]`, visibleElement: () => true,
  })
  assert.equal(result.completedResultVisible(), true)
  assert.deepEqual(selections, ['[data-tutorial-player-index="1"] [data-tutorial-anchor="master"]'])
})
check('actual action copy names the current practice card instead of a stale recipe card', () => {
  for (const name of ['食铁兽', '西施']) {
    const guide = actualFunctions(source.overlay, ['deployOrMoveGuide'], {
      facts: ref({ practiceInstanceId: 'synthetic-practice', practiceCard: { name }, resourcePrompt: null }),
      practiceHandSelector: () => '[data-synthetic-hand]', practiceSlotSelector: () => '[data-synthetic-slot]',
      domState: ref({ selectedPractice: false, availableSlots: false }),
    })
    for (const kind of ['deploy', 'move']) {
      const result = guide.deployOrMoveGuide(kind)
      assert(result.body.includes(name)); assert.equal(result.anchor.label, name)
      if (name === '食铁兽') assert(!result.body.includes('西施'))
    }
  }
})
check('successful owned chapter retirement clears only its normal closure copy, never an unexpected error', () => {
  const l12State = { notice: '房间已关闭' }
  const actual = actualFunctions(source.game, ['clearExpectedTutorialExitNotice'], { l12State })
  actual.clearExpectedTutorialExitNotice()
  assert.equal(l12State.notice, '')
  l12State.notice = '已离开结算并返回大厅'; actual.clearExpectedTutorialExitNotice()
  assert.equal(l12State.notice, '')
  for (const message of ['下一段练习未能建立', '账号或连接已变化', '未知错误', '']) {
    l12State.notice = message; actual.clearExpectedTutorialExitNotice(); assert.equal(l12State.notice, message)
  }
  assert.match(source.game, /if \(decision !== 'ready'\) return\s+if \(!transition.target\) \{ clearExpectedTutorialExitNotice\(\)/)
})
check('cold storage read denial exposes a bound retry and accepts exactly one handoff after recovery', () => {
  const accountId = 'cold-handoff', current = projection({ accountId }), storage = new MemoryStorage()
  storage.values.set(progress.guidedTutorialStorageKey(accountId), JSON.stringify({ version: 2, introductionRead: true, reviewedScenes: [] }))
  let denied = true, leaves = 0, emitted = 0
  const selected = { getItem(key) { if (denied) throw new Error('synthetic cold read denial'); return storage.getItem(key) },
    setItem: (key, value) => storage.setItem(key, value) }
  const tutorial = ref(current), tutorialTransition = ref(null), tutorialProgressRetryIdentity = ref('')
  const tutorialProgressUnsaved = ref(false), acceptedTutorialHandoffIdentity = ref('')
  const l12State = { pendingAction: false, status: 'online', sessionId: current.sessionId,
    connectionGeneration: current.connectionGeneration, notice: '' }
  const parent = actualFunctions(source.game,
    ['beginTutorialHandoff', 'tutorialCompletionIdentity', 'continueTutorialMainline', 'retryTutorialProgressSave'], {
      tutorial, tutorialTransition, tutorialPresentationBusy: ref(false), tutorialProgressRetryIdentity,
      tutorialProgressUnsaved, acceptedTutorialHandoffIdentity, l12State,
      platformState: { account: { id: accountId }, token: 'synthetic' }, game: ref({ matchId: current.matchId }),
      recordGuidedSceneReview: (view, owner) => progress.recordGuidedSceneReview(view, owner, selected),
      loadGuidedTutorialProgress: owner => progress.loadGuidedTutorialProgress(owner, selected),
      guidedTutorialPersistenceState: owner => progress.guidedTutorialPersistenceState(owner, selected),
      retryGuidedTutorialProgress: owner => progress.retryGuidedTutorialProgress(owner, selected),
      nextTutorialScene: () => 'move', tutorialTransitionTimer: null,
      tutorialTransitionSlow: ref(false), setTimeout: () => 1, leaveRoom: () => { leaves++; l12State.pendingAction = true },
    })
  const key = parent.tutorialCompletionIdentity(current)
  const child = actualFunctions(source.overlay, ['attemptCompletedHandoff'], {
    props: { tutorial: current, connected: true, busy: false,
      get acceptedHandoffIdentity() { return acceptedTutorialHandoffIdentity.value },
      get progressRetryRequired() { return tutorialProgressRetryIdentity.value === key } },
    completedIdentity: ref(key), stateAligned: ref(true), completedResultVisible: () => true,
    emit: () => { emitted++; parent.continueTutorialMainline() },
  })
  child.attemptCompletedHandoff()
  assert.equal(leaves, 0); assert.equal(tutorialProgressUnsaved.value, false)
  assert.equal(tutorialProgressRetryIdentity.value, parent.tutorialCompletionIdentity(current))
  assert.match(source.overlay, /v-if="progressRetryRequired"[\s\S]*?重试进度/)
  child.attemptCompletedHandoff()
  assert.equal(emitted, 1, 'scroll, resize or DOM refresh must not spin after a failed cold read')
  denied = false
  child.attemptCompletedHandoff()
  assert.equal(emitted, 1, 'storage recovery must not bypass the explicit failed-progress retry')
  parent.retryTutorialProgressSave()
  assert.equal(leaves, 1); assert.equal(tutorialProgressRetryIdentity.value, '')
  assert.deepEqual(progress.loadGuidedTutorialProgress(accountId, storage).reviewedScenes, ['deploy'])
  tutorialTransition.value = null; l12State.pendingAction = false
  parent.retryTutorialProgressSave(); parent.continueTutorialMainline()
  assert.equal(leaves, 1)
})
check('actual teaching-page retry preserves unsaved learning across roomLeft and stays account-local', () => {
  const owner = 'page-unsaved', other = 'page-other', storage = new MemoryStorage()
  const blocked = { getItem: key => storage.getItem(key), setItem() { throw new Error('synthetic quota') } }
  assert.equal(progress.markGuidedIntroductionRead(owner, blocked), false)
  assert.equal(progress.recordGuidedSceneReview(projection({ accountId: owner }), binding({ accountId: owner }), blocked), false)
  // roomLeft may clear transport notices; persistence state belongs to the account, not that notice.
  assert.equal(progress.guidedTutorialPersistenceState(owner, storage).unsaved, true)
  const accountId = ref(other), unsaved = ref(true), pageProgress = ref(null)
  const page = actualFunctions(source.page, ['retryProgressSave'], { accountId, unsaved, progress: pageProgress,
    retryGuidedTutorialProgress: id => progress.retryGuidedTutorialProgress(id, storage),
    guidedTutorialPersistenceState: id => progress.guidedTutorialPersistenceState(id, storage),
    loadGuidedTutorialProgress: id => progress.loadGuidedTutorialProgress(id, storage),
  })
  page.retryProgressSave()
  assert.equal(storage.getItem(progress.guidedTutorialStorageKey(owner)), null)
  accountId.value = owner; page.retryProgressSave()
  assert.equal(unsaved.value, false)
  assert.deepEqual(pageProgress.value.reviewedScenes, ['deploy'])
  const writes = storage.writes; page.retryProgressSave(); assert.equal(storage.writes, writes)
})

check('active, wrong-version, wrong-match and wrong-identity states never record completion', () => {
  const storage = new MemoryStorage()
  assert.equal(progress.recordTutorialCompletion(projection({ status: 'active' }), binding(), storage), false)
  assert.equal(progress.recordTutorialCompletion(projection({ version: 2 }), binding(), storage), false)
  assert.equal(progress.recordTutorialCompletion(projection(), binding({ matchId: 'other' }), storage), false)
  assert.equal(progress.recordTutorialCompletion(projection(), binding({ sessionId: 'other' }), storage), false)
  assert.equal(progress.recordTutorialCompletion(projection(), binding({ connectionGeneration: 12 }), storage), false)
  assert.equal(storage.writes, 0)
})
check('completed lessons persist per account and exact match only', () => {
  const storage = new MemoryStorage()
  assert.equal(progress.recordTutorialCompletion(projection(), binding(), storage, '2026-10-18T00:00:00Z'), true)
  assert.equal(progress.recordTutorialCompletion(projection(), binding(), storage, '2026-10-18T00:00:01Z'), true)
  assert.deepEqual(progress.loadTutorialProgress('account-a', storage), ['deploy'])
  assert.deepEqual(progress.loadTutorialProgress('account-b', storage), [])
  assert.equal(storage.writes, 1)
})
check('unreadable, unwritable, and blocked browser storage never throws or claims completion', () => {
  const unreadable = { getItem() { throw new Error('synthetic read denial') }, setItem() { assert.fail('must not overwrite unreadable progress') } }
  const unwritable = { getItem() { return null }, setItem() { throw new Error('synthetic quota') } }
  assert.deepEqual(progress.loadTutorialProgress('account-a', unreadable), [])
  assert.equal(progress.recordTutorialCompletion(projection(), binding(), unreadable), false)
  assert.equal(progress.recordTutorialCompletion(projection(), binding(), unwritable), false)
  Object.defineProperty(globalThis, 'window', { configurable: true, get() { throw new Error('synthetic security denial') } })
  assert.deepEqual(progress.loadTutorialProgress('account-a'), [])
  assert.equal(progress.recordTutorialCompletion(projection(), binding()), false)
})
check('account ABA cannot apply an old completion and a fresh A binding can persist', () => {
  const storage = new MemoryStorage()
  assert.equal(progress.recordTutorialCompletion(projection(), binding({ accountId: 'account-b' }), storage), false)
  assert.equal(progress.recordTutorialCompletion(projection(), binding({ sessionId: 'session-a2', connectionGeneration: 12 }), storage), false)
  const fresh = projection({ sessionId: 'session-a3', connectionGeneration: 13, matchId: 'match-a3', lessonId: 'move' })
  assert.equal(progress.recordTutorialCompletion(fresh, binding({ sessionId: 'session-a3', connectionGeneration: 13, matchId: 'match-a3' }), storage), true)
  assert.deepEqual(progress.loadTutorialProgress('account-a', storage), ['move'])
})

check('legacy action completions cannot promote the new conversation-led chapter', () => {
  const storage = new MemoryStorage()
  progress.recordTutorialCompletion(projection({ accountId: 'legacy-account' }), binding({ accountId: 'legacy-account' }), storage)
  assert.deepEqual(progress.loadGuidedTutorialProgress('legacy-account', storage), { version: 2, introductionRead: false, reviewedScenes: [] })
  assert.equal(progress.recordGuidedSceneReview(projection({ accountId: 'legacy-account' }), binding({ accountId: 'legacy-account' }), storage), false)
})
check('guided learning requires introduction, authoritative result, review and ordered scenes', () => {
  const storage = new MemoryStorage(), accountId = 'guided-account'
  const view = extra => projection({ accountId, ...extra }), owner = extra => binding({ accountId, ...extra })
  assert.equal(progress.markGuidedIntroductionRead(accountId, storage), true)
  assert.equal(progress.recordGuidedSceneReview(view({ status: 'active' }), owner(), storage), false)
  assert.equal(progress.recordGuidedSceneReview(view({ stepId: 'deploy' }), owner(), storage), false)
  assert.equal(progress.recordGuidedSceneReview(view({ lessonId: 'move' }), owner(), storage), false)
  assert.equal(progress.recordGuidedSceneReview(view({}), owner({ connectionGeneration: 99 }), storage), false)
  assert.equal(progress.recordGuidedSceneReview(view({}), owner({ sessionId: 'new-session' }), storage), false)
  assert.equal(progress.recordGuidedSceneReview(view({}), owner({ matchId: 'wrong-match' }), storage), false)
  for (const lessonId of ['deploy', 'move', 'combat']) assert.equal(progress.recordGuidedSceneReview(view({ lessonId }), owner(), storage), true)
  assert.deepEqual(progress.loadGuidedTutorialProgress(accountId, storage).reviewedScenes, ['deploy', 'move', 'combat'])
  assert.deepEqual(progress.loadGuidedTutorialProgress('another-guided-account', storage).reviewedScenes, [])
  assert.equal(progress.restartGuidedTutorial(accountId, storage), true)
  assert.deepEqual(progress.loadGuidedTutorialProgress(accountId, storage), { version: 2, introductionRead: false, reviewedScenes: [] })
})
check('unsaved learning stays bounded to this account and does not overwrite unreadable storage', () => {
  let writes = 0
  const denied = { getItem() { throw Error('denied') }, setItem() { writes++; assert.fail('unreadable key cannot be overwritten') } }
  assert.equal(progress.markGuidedIntroductionRead('unsaved-account', denied), false)
  assert.equal(progress.recordGuidedSceneReview(projection({ accountId: 'unsaved-account' }), binding({ accountId: 'unsaved-account' }), denied), false)
  assert.deepEqual(progress.loadGuidedTutorialProgress('unsaved-account', denied).reviewedScenes, ['deploy'])
  assert.equal(progress.loadGuidedTutorialProgress('other-unsaved-account', denied).introductionRead, false)
  assert.equal(writes, 0)
  const quota = { getItem() { return null }, setItem() { throw Error('quota') } }
  assert.equal(progress.markGuidedIntroductionRead('quota-account', quota), false)
  assert.equal(progress.loadGuidedTutorialProgress('quota-account', quota).introductionRead, true)
})
check('malformed or out-of-order chapter records cannot claim all learning is done', () => {
  const storage = new MemoryStorage()
  storage.setItem(progress.guidedTutorialStorageKey('malformed-account'), JSON.stringify({ version: 2, introductionRead: true, reviewedScenes: ['deploy', 'combat', 'move'] }))
  assert.deepEqual(progress.loadGuidedTutorialProgress('malformed-account', storage).reviewedScenes, ['deploy'])
  const mutable = progress.loadGuidedTutorialProgress('malformed-account', storage)
  mutable.reviewedScenes.push('move', 'combat')
  assert.deepEqual(progress.loadGuidedTutorialProgress('malformed-account', storage).reviewedScenes, ['deploy'])
})
const course = await import('data:text/javascript;base64,' + Buffer.from(ts.transpileModule(source.course, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText).toString('base64'))
check('new practice waits for actual roomLeft and fences account, token, generation and unrelated rooms', () => {
  const expected = { accountId: 'a', token: 'token-a', sessionId: 's', connectionGeneration: 9, roomCode: 'old-room', matchId: 'old-match' }
  const cleared = { accountId: 'a', token: 'token-a', connectedAccountId: 'a', sessionId: 's', connectionGeneration: 9,
    online: true, available: true, roomCode: null, matchId: null, leaving: false, pending: false }
  assert.equal(course.tutorialHandoffDecision(expected, cleared), 'ready')
  for (const extra of [{ roomCode: 'old-room' }, { matchId: 'old-match' }, { leaving: true }, { pending: true }])
    assert.equal(course.tutorialHandoffDecision(expected, { ...cleared, ...extra }), 'waiting')
  for (const extra of [{ accountId: 'b' }, { connectedAccountId: 'b' }, { token: 'new-token' }, { sessionId: 'other' },
    { connectionGeneration: 10 }, { online: false }, { available: false }, { roomCode: 'unrelated' }, { matchId: 'unrelated' }])
    assert.equal(course.tutorialHandoffDecision(expected, { ...cleared, ...extra }), 'cancelled')
})
check('hand and morale sources are separate concepts with real first-player exceptions', () => {
  const ids = course.tutorialIntroduction.map(step => step.id)
  assert.deepEqual(ids, ['goal', 'cards', 'hand', 'morale', 'practice'])
  assert.deepEqual(course.tutorialScenes, ['deploy', 'move', 'combat'])
  for (const step of course.tutorialIntroduction) {
    assert.ok(step.title && step.text)
    assert.equal('question' in step || 'answer' in step, false)
  }
  assert.match(course.tutorialIntroduction.find(step => step.id === 'hand').flow, /牌库.*手牌/)
  assert.match(course.tutorialIntroduction.find(step => step.id === 'morale').text, /追加.*重置阶段.*恢复活跃/)
  assert.match(course.tutorialIntroduction.find(step => step.id === 'morale').flow, /士气牌库.*活跃士气.*休整/)
  assert.deepEqual(course.tutorialIntroduction.find(step => step.id === 'goal').cards.map(card => card.name), ['嫦娥', '迦具土'])
  const shown = course.tutorialIntroduction.flatMap(step => step.cards.map(card => card.id))
  for (const id of ['ST01-M1', 'ST04-M1', 'S01-0104', 'ST01-05', 'ST01-10', 'S01-0117']) assert.ok(shown.includes(id), id)
})
check('every introduction card belongs to the frozen public decks, including the real morale reference', () => {
  const snapshot = JSON.parse(fs.readFileSync(path.join(front, '../服务端WebSocket/TwelveLegions/Data/tutorial-decks.v1.json'), 'utf8'))
  const available = new Set(snapshot.decks.flatMap(deck => [deck.masterId, ...deck.cardIds, ...deck.moraleIds, ...deck.specialIds]))
  for (const card of course.tutorialIntroduction.flatMap(step => step.cards)) assert(available.has(card.id), card.id)
  assert.equal(course.tutorialIntroduction.find(step => step.id === 'morale').cards[0].id,
    snapshot.decks.find(deck => deck.role === 'student').moraleIds[0])
})

class BrowserStorage extends MemoryStorage {
  removeItem(key) { this.values.delete(key) }
  clear() { this.values.clear() }
}
class FakeWebSocket {
  static CONNECTING = 0; static OPEN = 1; static CLOSING = 2; static CLOSED = 3; static instances = []
  readyState = FakeWebSocket.CONNECTING; sent = []; onopen = null; onmessage = null; onerror = null; onclose = null
  constructor(url) {
    this.url = url
    Object.defineProperty(this, '__v_skip', { value: true })
    FakeWebSocket.instances.push(this)
  }
  open() { this.readyState = FakeWebSocket.OPEN; this.onopen?.({}) }
  receive(payload) { this.onmessage?.({ data: JSON.stringify(payload) }) }
  send(payload) { this.sent.push(String(payload)) }
  close(code = 1000, reason = '') { this.readyState = FakeWebSocket.CLOSED; this.onclose?.({ code, reason }) }
}
const browserStorage = new BrowserStorage()
browserStorage.setItem('l12-auth-token', 'tutorial-focused-token')
globalThis.localStorage = browserStorage
globalThis.location = { protocol: 'https:', host: 'legion-12.com', hostname: 'legion-12.com' }
Object.defineProperty(globalThis, 'window', { configurable: true, writable: true, value:
  { localStorage: browserStorage, setTimeout, clearTimeout, setInterval, clearInterval,
    addEventListener() {}, removeEventListener() {}, dispatchEvent() {} } })
globalThis.CustomEvent = class { constructor(type, options) { this.type = type; this.detail = options?.detail } }
globalThis.WebSocket = FakeWebSocket

console.log('INFO loading actual net consumer through focused Vite SSR')
const vite = await createServer({ root: front, cacheDir: cache, configFile: false, appType: 'custom',
  logLevel: 'silent', server: { middlewareMode: true }, optimizeDeps: { noDiscovery: true } })
try {
  console.log('INFO focused Vite SSR ready')
  const net = await vite.ssrLoadModule('/src/l12/net.ts')
  console.log('INFO actual net module loaded')
  const connected = net.connect()
  const socket = FakeWebSocket.instances[0]
  socket.open()
  socket.receive({ type: 'session', accountId: 'account-a', sessionId: 'tutorial-session', name: '教学玩家',
    tutorialAvailable: true, connectionGeneration: 21, capabilities: { requestIds: true, deltaGameState: true } })
  socket.receive({ type: 'recoveryComplete', connectionGeneration: 21 })
  await connected
  console.log('INFO actual net handshake complete')
  check('actual net consumer sends exact create request and releases every tutorial rejection by matching id', () => {
    for (const type of ['tutorialUnavailable', 'tutorialRequestExpired', 'tutorialRestartRequired', 'tutorialRejected']) {
      const requestId = net.createTutorial('deploy')
      const sent = JSON.parse(socket.sent.at(-1))
      assert.deepEqual(Object.keys(sent).sort(), ['lessonId', 'requestId', 'type'])
      assert.deepEqual(sent, { type: 'createTutorial', lessonId: 'deploy', requestId })
      assert.equal(net.l12State.pendingAction, true)
      socket.receive({ type, requestId: 'stale-request', message: '迟到失败' })
      assert.equal(net.l12State.pendingAction, true, `${type} stale response must not release current request`)
      assert.equal(net.l12State.room, null); assert.equal(net.l12State.game, null)
      socket.receive({ type, requestId, message: `可重试 ${type}` })
      assert.equal(net.l12State.pendingAction, false)
      assert.equal(net.l12State.notice, `可重试 ${type}`)
    }
  })
  check('actual sandboxForbidden consumer releases matching actions without discarding room identity', () => {
    net.l12State.gmEnabled = true
    net.l12State.room = { roomCode: 'SANDBX', sandbox: true }
    net.l12State.game = { matchId: 'sandbox-match', roomCode: 'SANDBX', revision: 1, phase: 'Main', players: [] }
    net.sandboxAction(0, { type: 'endTurn' })
    const request = JSON.parse(socket.sent.at(-1))
    socket.receive({ type: 'sandboxForbidden', requestId: request.requestId, message: '沙盒权限已失效' })
    assert.equal(net.l12State.pendingAction, false)
    assert.equal(net.l12State.room.roomCode, 'SANDBX')
    assert.equal(net.l12State.game.matchId, 'sandbox-match')
  })
  check('actual permission rejection releases response settings without claiming a durable change', () => {
    net.l12State.responsePreference = { confirmedMode: 'valid-only', syncPending: true }
    const requestId = net.setResponsePreference('default')
    socket.receive({ type: 'responsePreferenceRejected', requestId, message: '操作权限已变化' })
    assert.deepEqual(net.l12State.responsePreference, { confirmedMode: 'valid-only', syncPending: true })
    assert.deepEqual(net.l12State.responsePreferenceResult, {
      confirmedMode: 'valid-only', syncPending: true, matchApplied: false, accountSynced: false, requestId,
    })
    assert.equal(net.l12State.notice, '操作权限已变化')
    const dock = fs.readFileSync(path.join(front, 'src/l12/game/BattleUtilityDock.vue'), 'utf8')
    assert.match(dock, /responseRequestId\.value && result\.requestId !== responseRequestId\.value/)
    assert.match(dock, /responseDraft\.value = result\.confirmedMode[\s\S]*busy\.value = false[\s\S]*responseRequestId\.value = ''/)
  })
  check('actual gameState consumer disables GM/referee on tutorial conflict', () => {
    net.l12State.room = { roomCode: 'TUTOR2', started: true, players: [], tutorial: { lessonId: 'move', version: 1, stepId: 'move', status: 'active' } }
    socket.receive({ type: 'gameState', gmEnabled: true, refereeControlsEnabled: true, spectating: false,
      tutorial: { lessonId: 'move', version: 1, stepId: 'move', status: 'active' },
      state: { matchId: 'tutorial-conflict', roomCode: 'TUTOR2', revision: 1, phase: 'Main', players: [] } })
    assert.equal(net.l12State.gmEnabled, false)
    assert.equal(net.l12State.refereeControlsEnabled, false)
    assert.equal(net.l12State.tutorial, null)
  })
  check('replacement session clears tutorial binding without clearing same-account observation fences', () => {
    net.l12State.tutorial = projection({ status: 'active', sessionId: 'tutorial-session', connectionGeneration: 21 })
    socket.receive({ type: 'session', accountId: 'account-a', sessionId: 'replacement-session', name: '教学玩家',
      tutorialAvailable: true, connectionGeneration: 22, capabilities: { requestIds: true, deltaGameState: true } })
    assert.equal(net.l12State.tutorial, null)
    assert.equal(net.l12State.sessionId, 'replacement-session')
    assert.equal(net.l12State.tutorialAvailable, true)
    assert.match(source.net, /if \(l12State\.accountId !== nextAccountId\) retiredObservation = null/)
  })
  net.disconnect()
} finally { await vite.close() }

await import('./test-tutorial-final-chapter.mjs')

const report = { status: 'GREEN', count: cases.length, cases,
  sourceSha256: Object.fromEntries(Object.entries(files).map(([key, relative]) =>
    [key, crypto.createHash('sha256').update(fs.readFileSync(path.join(front, relative))).digest('hex').toUpperCase()])),
  boundaries: ['Focused Node/Vite SSR consumer test; no browser viewport claim.', 'Synthetic socket messages; no server acknowledgement claim.'] }
fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n')
console.log(`Tutorial focused contracts: ${cases.length} passed`)
