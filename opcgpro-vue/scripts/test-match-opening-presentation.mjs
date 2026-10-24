import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'

const path = new URL('../src/l12/game/PromptOverlay.vue', import.meta.url)
const source = readFileSync(path, 'utf8')
const parsed = parse(source, { filename: 'PromptOverlay.vue' })
assert.deepEqual(parsed.errors, [], 'PromptOverlay opening presentation must remain a valid Vue SFC')
assert.ok(parsed.descriptor.scriptSetup?.content, 'PromptOverlay must retain script setup')
assert.ok(parsed.descriptor.template?.content, 'PromptOverlay must retain its real template')
assert.ok(parsed.descriptor.styles.length, 'PromptOverlay must retain scoped presentation styles')

const script = parsed.descriptor.scriptSetup.content
const template = parsed.descriptor.template.content
const style = parsed.descriptor.styles.map(block => block.content).join('\n')
const scriptAst = ts.createSourceFile('PromptOverlay.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
assert.deepEqual(scriptAst.parseDiagnostics, [], 'PromptOverlay opening script must parse as TypeScript')

assert.match(script, /import \{ masterDeckBannerUrl, masterProfileUrl \} from '\.\.\/specialAssets'/,
  'the opening and deck cover must share the formal master banner registry')
assert.match(script, /const initiativeViewer = computed\(\(\) => props\.game\.players\.find\(player => player\.playerIndex === props\.game\.you\)/,
  'left/right identity must belong to the actual viewer instead of the sandbox-controlled actor')
assert.match(script, /\.\.\.props\.game\.players\.filter\(player => player\.playerIndex !== initiativeViewer\.value\.playerIndex\),\s*initiativeViewer\.value/,
  'the opponent must render before the viewer so the stage remains opponent-left and viewer-right')

assert.match(script, /function hasAuthoritativeInitiativeRolls\(\)[\s\S]*props\.game\.players\.every[\s\S]*Number\.isInteger\(value\) && value >= 1 && value <= 6/,
  'local presentation may settle only after every authoritative die is valid')
assert.match(script, /function settleInitiativeDice\(\) \{\s*if \(!hasAuthoritativeInitiativeRolls\(\)\) return false/,
  'skip must never settle a temporary random face as the match result')
assert.match(script, /function skipOpeningIntro\(\)[\s\S]*openingIntroSkipped\.value = true[\s\S]*diceAnimationReadyToSettle\.value = true[\s\S]*settleInitiativeDice\(\)/,
  'skip must accelerate only local presentation and still consume the authoritative result')
assert.match(script, /l12AnimationDuration\(1250\)/,
  'the accepted 1.25 second standard dice cadence must consume the existing fast/off preference')
assert.match(script, /prefers-reduced-motion: reduce[\s\S]*duration <= 0[\s\S]*diceAnimationReadyToSettle\.value = true[\s\S]*settleInitiativeDice\(\)/,
  'off and operating-system reduced motion must skip local rolling without fabricating a result')

assert.match(script, /watch\(\(\) => \[props\.game\.matchId, props\.game\.phase\] as const/,
  'match and phase must share one lifecycle boundary instead of racing independent watchers')
assert.doesNotMatch(script, /watch\(\(\) => props\.game\.phase/,
  'a phase-only watcher could attribute the previous match result to a new match')
assert.doesNotMatch(script, /playMulliganDeal|mulliganDealTimer/,
  'the first opening slice must not retain the unapproved, unconsumed mulligan expansion')

function runtimeFunction(name, bindings = {}) {
  const declaration = scriptAst.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
  assert.ok(declaration, `missing runtime function: ${name}`)
  const compiled = ts.transpileModule(`${declaration.getText(scriptAst)}\nglobalThis.__result = ${name};`, {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.None },
  }).outputText
  const scope = {}
  const bindingNames = Object.keys(bindings)
  new Function('globalThis', ...bindingNames, compiled)(scope, ...bindingNames.map(key => bindings[key]))
  return scope.__result
}

function executeDiceStart(animation, reduced = false) {
  let timeoutDelay = null
  let intervalStarts = 0
  let settles = 0
  const start = runtimeFunction('startInitiativeDice', {
    diceTimer: null,
    diceSettleTimer: null,
    diceSettled: { value: true },
    diceAnimationReadyToSettle: { value: false },
    openingIntroSkipped: { value: false },
    animatedRolls: { value: [1, 1] },
    clearInterval() {},
    clearTimeout() {},
    setInterval() { intervalStarts += 1; return 11 },
    setTimeout(callback, delay) { timeoutDelay = delay; return 12 },
    window: { matchMedia: () => ({ matches: reduced }) },
    l12AnimationDuration: standard => animation === 'off' ? 0 : animation === 'fast' ? Math.round(standard * .55) : standard,
    settleInitiativeDice() { settles += 1; return true },
  })
  start()
  return { delay: timeoutDelay ?? 0, intervalStarts, settles }
}

assert.deepEqual(executeDiceStart('standard'), { delay: 1250, intervalStarts: 1, settles: 0 })
assert.deepEqual(executeDiceStart('fast'), { delay: 688, intervalStarts: 1, settles: 0 })
assert.deepEqual(executeDiceStart('off'), { delay: 0, intervalStarts: 0, settles: 1 })
assert.deepEqual(executeDiceStart('standard', true), { delay: 0, intervalStarts: 0, settles: 1 })

function controlledTimers() {
  let nextId = 1
  const pending = new Map()
  return {
    api: {
      set(callback, delay) { const id = nextId++; pending.set(id, { callback, delay }); return id },
      clear(id) { pending.delete(id) },
    },
    pending,
    runOnly() {
      assert.equal(pending.size, 1, 'exactly one result timer must be pending')
      const [id, item] = pending.entries().next().value
      pending.delete(id)
      item.callback()
      return item.delay
    },
  }
}

function controlledLifecycle() {
  const events = []
  const state = { message: '', dice: 'idle', starts: 0 }
  const timers = controlledTimers()
  const create = runtimeFunction('createInitiativeLifecycle')
  const lifecycle = create({
    clearResult() { events.push('clear-result'); state.message = '' },
    showResult() { events.push('show-result'); state.message = '选择先攻' },
    stopDice() { events.push('stop-dice'); state.dice = 'stopped' },
    startEntry() { events.push('start-entry'); state.dice = 'running'; state.starts += 1 },
  }, timers.api)
  return { lifecycle, timers, events, state }
}

const sameMatch = controlledLifecycle()
sameMatch.lifecycle.transition({ matchId: 'A', phase: 'Initiative' })
assert.deepEqual(sameMatch.events, ['clear-result', 'stop-dice', 'start-entry'],
  'initial entry must clear old state before starting the current match')
sameMatch.lifecycle.transition({ matchId: 'A', phase: 'DisasterPreparation' }, { matchId: 'A', phase: 'Initiative' })
assert.equal(sameMatch.state.message, '选择先攻')
assert.equal(sameMatch.state.dice, 'stopped')
assert.equal(sameMatch.timers.runOnly(), 650, 'same-match initiative exit must clear its result after 650ms')
assert.equal(sameMatch.state.message, '')

const nextInitiative = controlledLifecycle()
nextInitiative.lifecycle.transition({ matchId: 'A', phase: 'Initiative' })
nextInitiative.lifecycle.transition({ matchId: 'A', phase: 'DisasterPreparation' }, { matchId: 'A', phase: 'Initiative' })
nextInitiative.lifecycle.transition({ matchId: 'B', phase: 'Initiative' }, { matchId: 'A', phase: 'DisasterPreparation' })
assert.equal(nextInitiative.timers.pending.size, 0, 'a new match must cancel the previous result timer')
assert.equal(nextInitiative.state.message, '', 'a new match must clear the previous result')
assert.equal(nextInitiative.state.dice, 'running', 'A Initiative to B Initiative must leave the B entry running')
assert.deepEqual(nextInitiative.events.slice(-3), ['clear-result', 'stop-dice', 'start-entry'],
  'new-match cleanup must happen before its entry starts')

const nextLaterPhase = controlledLifecycle()
nextLaterPhase.lifecycle.transition({ matchId: 'A', phase: 'Initiative' })
nextLaterPhase.lifecycle.transition({ matchId: 'B', phase: 'DisasterPreparation' }, { matchId: 'A', phase: 'Initiative' })
assert.equal(nextLaterPhase.state.message, '', 'A Initiative to a later B phase must not fabricate a B result')
assert.equal(nextLaterPhase.state.dice, 'stopped')
assert.equal(nextLaterPhase.state.starts, 1, 'a later B phase must not replay the opening')
assert.equal(nextLaterPhase.timers.pending.size, 0)

const initialLaterPhase = controlledLifecycle()
initialLaterPhase.lifecycle.transition({ matchId: 'B', phase: 'Mulligan' })
assert.equal(initialLaterPhase.state.message, '', 'opening a match in a later phase must not backfill a result')
assert.equal(initialLaterPhase.state.starts, 0)

assert.equal((template.match(/data-ui-contract="match-opening-stage"/g) || []).length, 2,
  'the initiative owner and waiting player must share the same opening stage')
assert.match(template, /class="initiative-master-banner"[\s\S]*masterDeckBannerUrl\(player\.master\.masterId\)[\s\S]*@error="hideBrokenBanner"/,
  'formal banners need the existing profile fallback when an asset cannot load')
assert.match(template, /class="initiative-master-profile"[\s\S]*masterProfileUrl\(player\.master\.masterId, player\.master\.masterImageUrl\)/,
  'master identity must remain visible without a banner')
assert.match(template, /class="initiative-skip" @click="skipOpeningIntro">跳过演出<\/button>/,
  'the pure entrance must remain locally skippable')
assert.match(template, /class="initiative-resolution-message" role="status" aria-live="polite"/,
  'the selected side must use a brief accessible center result')
assert.match(template, /<Teleport :to="landscapeTeleportTarget\(\)">[\s\S]*initiative-resolution-message[\s\S]*<\/Teleport>\s*<Teleport :to="mobileLayout && minimized/,
  'the result must own an independent landscape teleport outside the conditional modal overlay')
assert.doesNotMatch(template, /先攻[^<]{0,12}(?:标记|飞向)|后攻[^<]{0,12}(?:标记|飞向)/,
  'no first/second marker may fly into or persist in either player area')
assert.match(template, /:data-tutorial-choice="choice"/,
  'the existing authoritative initiative choice remains the actual interaction')

for (const contract of [
  '.opening-initiative-panel', '.initiative-opening-stage', '.initiative-master-banner',
  '.initiative-master-profile', '.initiative-die', '.initiative-clash', '.initiative-resolution-message',
]) assert.ok(style.includes(contract), `opening style missing: ${contract}`)
assert.match(style, /\.initiative-skip\{[^}]*min-height:44px/,
  'the skip control must remain a real mobile touch target')
assert.match(style, /\.initiative-resolution-message\{[^}]*pointer-events:none/,
  'the brief result may not block the following disaster preparation interaction')
assert.match(style, /@media\(max-width:900px\) and \(max-height:500px\)/,
  'short landscape battle canvases need a bounded opening layout')
assert.match(style, /\.initiative-opening-stage\.skipped[^}]*animation:none!important/,
  'skip must end the entrance motion without changing game state')
assert.match(style, /@media\(prefers-reduced-motion:reduce\)[^{]*\{[^}]*initiative-opening-stage/,
  'operating-system reduced motion must disable the new entrance animation')
assert.match(style, /var\(--l12-animation-scale,1\)/,
  'new opening CSS must consume the existing fast/off animation scale')

console.log('Match opening presentation passed: controlled match lifecycle, authoritative dice, motion preferences and independent result.')
