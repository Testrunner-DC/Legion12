import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'

const path = new URL('../src/l12/game/PromptOverlay.vue', import.meta.url)
const gameBoardPath = new URL('../src/l12/game/GameBoard.vue', import.meta.url)
const source = readFileSync(path, 'utf8')
const gameBoardSource = readFileSync(gameBoardPath, 'utf8')
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

assert.doesNotMatch(script, /masterDeckBannerUrl|masterProfileUrl/,
  'the opening must use the real master card face instead of a separate banner/profile treatment')
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
assert.match(script, /const openingIntroDurationMs = 4000/,
  'the approved four-second standard opening must have one explicit duration source')
assert.match(template, /'--opening-duration': `\$\{openingIntroDurationMs\}ms`/)
assert.match(style, /calc\(var\(--opening-duration\) \* var\(--l12-animation-scale,1\)\)/)
assert.match(script, /const duration = l12AnimationDuration\(openingIntroDurationMs\)[\s\S]*duration <= 0[\s\S]*diceAnimationReadyToSettle\.value = true[\s\S]*settleInitiativeDice\(\)/,
  'only the in-game off setting skips local rolling without fabricating a result')
assert.doesNotMatch(script, /prefers-reduced-motion/, 'the OS preference must not override standard/fast')

assert.match(script, /watch\(\(\) => \[props\.game\.matchId, props\.game\.phase\] as const/,
  'match and phase must share one lifecycle boundary instead of racing independent watchers')
assert.doesNotMatch(script, /watch\(\(\) => props\.game\.phase/,
  'a phase-only watcher could attribute the previous match result to a new match')
assert.doesNotMatch(script, /playMulliganDeal|mulliganDealTimer/,
  'the first opening slice must not retain the unapproved, unconsumed mulligan expansion')
assert.match(script, /waitingPrompt\.value\?\.kind !== 'initiative'[\s\S]*waitingPrompt\.value \|\| waitingDefense\.value/,
  'the non-choosing initiative viewer must keep the shared full opening instead of entering the passive wait tile')

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

// The first pointer movement may show the inspector before its click is delivered.
// Reserve the same reading lane before that movement, not only after focus appears.
{
  const declarations = scriptAst.statements.filter(ts.isVariableStatement)
    .flatMap(statement => [...statement.declarationList.declarations])
  const reservation = declarations.find(node => ts.isIdentifier(node.name)
    && node.name.text === 'reserveMulliganDetailSpace')
  assert.ok(reservation?.initializer, 'mulligan must reserve detail space before the first hover')
  const compiled = ts.transpileModule(`globalThis.__result = ${reservation.initializer.getText(scriptAst)};`, {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.None },
  }).outputText
  for (const [mobileLayout, mulligan, expected] of [
    [false, true, true], [false, false, false], [true, true, false], [true, false, false],
  ]) {
    const scope = {}
    const props = { mobileLayout }
    const phase = { value: mulligan }
    new Function('globalThis', 'computed', 'props', 'isMulliganPhase', compiled)(
      scope, getter => ({ get value() { return getter() } }), props, phase)
    assert.equal(scope.__result.value, expected, 'reserve only the non-mobile mulligan reading lane')
    props.inspectorVisible = true
    assert.equal(scope.__result.value, expected, 'inspector visibility cannot move the reserved lane')
  }
  assert.match(template, /'inspector-active': inspectorVisible \|\| reservePlacementDetailSpace \|\| reserveMulliganDetailSpace/,
    'mulligan and placement must share the existing inspector lane rather than create another overlay')
  assert.match(template, /@click="emit\('mulliganConfirm'\)"/,
    'layout reservation must preserve the authoritative mulligan confirmation event')
}

{
  let queried = false
  const offsets = { value: { 0: { x: 1, y: 1, scale: 1 } } }
  const syncTargets = runtimeFunction('syncMasterTargets', {
    nextTick(callback) { callback() },
    openingStage: { value: { getBoundingClientRect: () => ({ left: 0, top: 0, width: 100, height: 100 }), querySelector() { queried = true; throw new Error('deployed master must not query a landing card') } } },
    initiativePlayers: { value: [{ playerIndex: 0, master: { deployedAsLegion: true } }] },
    document: { querySelector() { queried = true; throw new Error('deployed master must not query a zone anchor') } },
    masterTargetOffsets: offsets,
  })
  syncTargets()
  assert.equal(queried, false, 'a master already deployed as a legion must not use the persistent master placeholder as a landing target')
  assert.deepEqual(offsets.value, {}, 'the previous match landing target must be cleared for a deployed master')
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
    openingIntroDurationMs: 4000,
    clearInterval() {},
    clearTimeout() {},
    setInterval() { intervalStarts += 1; return 11 },
    setTimeout(callback, delay) { timeoutDelay = delay; return 12 },
    window: { matchMedia: () => ({ matches: reduced }) },
    l12AnimationDuration: standard => animation === 'off' ? 0 : animation === 'fast' ? Math.round(standard * .55) : standard,
    syncMasterTargets() {},
    settleInitiativeDice() { settles += 1; return true },
  })
  start()
  return { delay: timeoutDelay ?? 0, intervalStarts, settles }
}

assert.deepEqual(executeDiceStart('standard'), { delay: 4000, intervalStarts: 1, settles: 0 })
assert.deepEqual(executeDiceStart('fast'), { delay: 2200, intervalStarts: 1, settles: 0 })
assert.deepEqual(executeDiceStart('off'), { delay: 0, intervalStarts: 0, settles: 1 })
assert.deepEqual(executeDiceStart('standard', true), { delay: 4000, intervalStarts: 1, settles: 0 })
assert.deepEqual(executeDiceStart('fast', true), { delay: 2200, intervalStarts: 1, settles: 0 })
assert.deepEqual(executeDiceStart('off', true), { delay: 0, intervalStarts: 0, settles: 1 })

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

assert.equal((template.match(/data-ui-contract="match-opening-stage"/g) || []).length, 1,
  'the initiative owner and waiting player must share one non-modal opening stage')
assert.match(template, /waiting: !isInitiative && \(waitingPrompt \|\| \(isMulliganPhase && !isMulligan\)\)/,
  'the waiting initiative viewer must not inherit ordinary waiting blur or tile styling')
const masterFaceSourcePattern = /class="opening-master-card"[\s\S]*<CardImage :card-id="player\.master\.masterId" :legacy-url="player\.master\.masterImageUrl"/
assert.match(template, masterFaceSourcePattern,
  'the opening must fly the real master card face into the current match')
assert.doesNotMatch(template.replace(':card-id="player.master.masterId"', ':card-id="game.players[0].master.masterId"'), masterFaceSourcePattern,
  'a hardcoded side cannot lend the current player master identity')
assert.doesNotMatch(template.replace(':legacy-url="player.master.masterImageUrl"', ':legacy-url="fakeCardFace"'), masterFaceSourcePattern,
  'an unrelated image cannot lend the authority master fallback')
assert.match(script, /data-tutorial-anchor="master"\]\[data-tutorial-player-index="\$\{player\.playerIndex\}"\]/,
  'the card landing must measure the existing player master-zone anchor')
assert.match(script, /'--master-target-scale': String\(target\?\.scale \?\? 0\)/,
  'a missing master-zone anchor must retract the presentation card instead of fabricating a landing target')
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
assert.match(template, /<template v-if="!readOnly && prompt && diceSettled">/,
  'a read-only spectator must never inherit the initiative choice controls')
assert.match(gameBoardSource, /const promptOverlayMounted = computed\(\(\) => !props\.readOnly[\s\S]*props\.game\.phase === 'DisasterPreparation' \|\| props\.game\.phase === 'Initiative'\)/,
  'read-only spectators must mount the shared opening during Initiative without broadening other prompt phases')
assert.doesNotMatch(template, /opening-initiative-panel|initiative-opening-stage/,
  'the accepted opening must not return to the framed two-banner panel')
assert.match(template, /class="opening-versus"[^>]*>对阵<\/strong>/,
  'the centered versus beat must remain plain settlement-style text')
assert.match(script, /import \{ getFactionPresentation \} from '\.\.\/factionPresentation'/,
  'master entrance glow must reuse the shared faction presentation registry')
assert.match(script, /function openingPlayerIdentity\([\s\S]*props\.readOnly[\s\S]*player\.name[\s\S]*player\.playerIndex === props\.game\.you \? '你' : player\.name \|\| '对手'/,
  'live opening must show self as you and the opponent public player id while replay shows both ids')
assert.match(template, /class="opening-versus-player"[\s\S]*player\.playerIndex !== game\.you[\s\S]*player\.master\.masterName[\s\S]*openingPlayerIdentity\(player\)[\s\S]*<template v-else>[\s\S]*openingPlayerIdentity\(player\)[\s\S]*player\.master\.masterName/,
  'opponent identity/master must sit above versus while self identity/master sits below it')
assert.match(template, /class="opening-die"[\s\S]*:aria-label="`\$\{openingPlayerIdentity\(player\)\}掷出\$\{animatedRolls\[player\.playerIndex\] \?\? 1\}点`"/,
  'each die must keep an accessible player mapping without painting a second visible player id')
assert.doesNotMatch(template, /class="opening-die"[\s\S]{0,240}<small>\{\{ openingPlayerIdentity\(player\) \}\}<\/small>/,
  'dice must not replace the persistent matchup identity with a second visible player id')
assert.doesNotMatch(template, /class="opening-versus-player"[^>]*:style=/,
  'central player ids must not inherit a faction-colored inline style')
assert.doesNotMatch(script, /--opening-perspective|--opening-depth-origin/,
  'opening masters must not receive per-side depth projection variables')
assert.match(script, /'--opening-rotation': '0deg'/,
  'both opening master faces must remain upright while moving to their side')
assert.doesNotMatch(template, /opening-master-card[\s\S]{0,420}opening-master-name/,
  'player identity must not remain attached below either flying master card')
assert.match(template, /class="mulligan-stage"[\s\S]*你是先手[\s\S]*你是后手[\s\S]*请确认调度的手牌/,
  'mulligan must explain the player role above the real hand without a modal panel')
assert.match(template, /class="mulligan-card"[\s\S]*:name="card\.name"/,
  'mulligan cards need a dedicated local presentation hook')
assert.doesNotMatch(template, /data-tutorial-mulligan-card[\s\S]{0,260}meta="手牌"/,
  'mulligan cards must not repeat the obvious hand-zone label')
assert.match(template, /data-tutorial-action="keep-all-mulligan"[\s\S]*>全部保留<\/button>[\s\S]*data-tutorial-action="confirm-mulligan"[\s\S]*确认调度/,
  'mulligan must expose the two approved actions below the hand')
assert.match(gameBoardSource, /function keepAllMulligan\(\) \{\s*mulliganIds\.value = \[\]\s*freeCommand\('mulligan'\)/,
  'keep all must atomically submit an explicit empty selection through the existing command')
assert.match(gameBoardSource, /@mulligan-keep-all="keepAllMulligan"/,
  'the real board must bind the approved keep-all action')

for (const contract of [
  '.opening-presentation', '.opening-master-card', '.opening-versus',
  '.opening-die', '.opening-action', '.mulligan-stage', '.initiative-resolution-message',
]) assert.ok(style.includes(contract), `opening style missing: ${contract}`)
assert.match(style, /\.initiative-skip\{[^}]*min-height:44px/,
  'the skip control must remain a real mobile touch target')
assert.match(style, /\.initiative-resolution-message\{[^}]*pointer-events:none/,
  'the brief result may not block the following disaster preparation interaction')
assert.match(style, /\.l12-prompt-overlay\.mobile-safe-overlay \.mulligan-stage \.mulligan-card-row\{[^}]*max-height:100%/,
  'the mulligan hand must override the generic mobile strip cap so fixed-height cards are not clipped')
assert.match(style, /\.l12-prompt-overlay\.mobile-safe-overlay \.prompt-card-strip\{[^}]*max-height:54vh/,
  'other mobile prompt strips must retain their bounded scrolling height')
assert.match(style, /\.opening-master-card\{[^}]*drop-shadow\(0 0 24px var\(--opening-faction-color/,
  'each master must carry its shared faction-color glow')
assert.match(style, /\.opening-versus-player\.opponent\{top:calc\(43% - clamp\([^}]+\}[\s\S]*\.opening-versus-player\.mine\{top:calc\(43% \+ clamp\(/,
  'opponent information must be above versus while self information remains below it')
assert.match(style, /\.opening-versus-player small\{color:#76dce3/,
  'both player ids must use the fixed interface cyan instead of faction colors')
assert.match(style, /\.opening-master-card :deep\(\.l12-card-image\)\{[^}]*width:100%[^}]*height:auto[^}]*aspect-ratio:5\/7/,
  'opening masters must retain a normal face-on card ratio')
assert.doesNotMatch(style, /perspective\(|rotateY\(|scaleX\(1\.25\)|opening-master-depth|preserve-3d/,
  'the opening must not distort master faces with a 3D projection or horizontal compensation')
assert.match(style, /\.opening-master-card\{[^}]*width:clamp\(220px,24vw,340px\)/,
  'desktop face-on cards must leave a clear central dice and versus lane')
assert.match(script, /'--opening-side-x': mine \? 'clamp\(150px, 22vw, 360px\)' : 'clamp\(-360px, -22vw, -150px\)'/,
  'both master cards must advance toward the center instead of remaining at the screen edges')
assert.match(script, /'--opening-side-y': mine \? 'clamp\(40px, 5vh, 64px\)' : 'clamp\(-64px, -5vh, -40px\)'/,
  'the opponent-high and viewer-low split must match the marked vertical footprints')
assert.match(style, /@keyframes opening-master-sequence[\s\S]*var\(--opening-side-x\)[\s\S]*var\(--opening-side-y\)/,
  'the split beat must retain the accepted opponent-high and viewer-low placement')
assert.match(style, /@keyframes opening-master-sequence\{[\s\S]*45%,100%\{opacity:1;transform:translate3d\(calc\(-50% \+ var\(--opening-side-x\)\),calc\(-50% \+ var\(--opening-side-y\)\),0\) rotateZ\(0\) scale\(1\)\}/,
  'opening masters must finish at their two side positions instead of landing on the board or disappearing')
assert.match(style, /\.opening-presentation\.settled \.opening-master-card,[^\{]*\.opening-presentation\.skipped \.opening-master-card\{opacity:1;transform:translate3d\(calc\(-50% \+ var\(--opening-side-x\)\),calc\(-50% \+ var\(--opening-side-y\)\),0\) rotateZ\(0\) scale\(1\);animation:none\}/,
  'settled and skipped initiative states must retain both face-on masters at the sides')
assert.match(style, /\.opening-presentation\.settled \.opening-versus,[^\{]*\.opening-presentation\.skipped \.opening-versus-player\{opacity:1;animation:none\}/,
  'settled and skipped initiative states must retain the same identities and versus composition')
assert.doesNotMatch(style, /\.opening-presentation\.settled \.opening-versus[^\{]*\{display:none\}/,
  'dice settlement must not hide the opening matchup composition')
assert.match(style, /@keyframes opening-versus-sequence\{[\s\S]*42%,100%\{opacity:1/,
  'the matchup composition must remain continuous while dice enter')
assert.match(style, /@media\(max-width:900px\) and \(max-height:500px\)\{\.opening-master-card\{width:clamp\(112px,17vw,164px\)/,
  'short landscape must keep the face-on cards contained after reducing their size')
assert.match(style, /data-l12-rotated="true"[\s\S]*\.opening-master-card\)\{width:164px\}/,
  'rotated portrait must use the contained logical-canvas master footprint')
assert.match(style, /\.opening-die b\{[^}]*font-size:clamp\(88px,12vw,156px\)/,
  'desktop dice must occupy the central opening composition')
assert.match(style, /\.mulligan-card-row :deep\(\.mulligan-card\.prompt-card-candidate\)\{[^}]*width:150px[^}]*border:0[^}]*background:transparent/,
  'only mulligan cards must use the larger frameless presentation')
assert.match(style, /\.mulligan-card-row :deep\(\.mulligan-card \.prompt-card-candidate__meta\)\{display:none\}/,
  'the mulligan-only zone meta line must not reserve visual space')
assert.match(style, /\.mulligan-card-row :deep\(\.mulligan-card\.selected \.l12-card-image\)\{[^}]*grayscale\(\.24\)[^}]*brightness\(\.82\)[^}]*transform:none/,
  'selected mulligan cards must retain the light gray image state')
assert.match(style, /\.mulligan-card-row\{[^}]*padding:28px 2px 16px[^}]*\}[\s\S]*\.mulligan-card-row :deep\(\.mulligan-card\.selected\)\{transform:translateY\(-12px\)\}/,
  'selected mulligan cards must lift slightly inside explicit desktop headroom')
assert.match(style, /\.l12-prompt-overlay\.mobile-safe-overlay \.mulligan-stage \.mulligan-card-row\{[^}]*padding:20px 2px 8px[^}]*overflow-y:hidden/,
  'mobile mulligan rows must reserve enough top headroom before clipping overflow')
assert.match(style, /html\[data-l12-mobile="true"\]\[data-l12-viewport\][^}]*\.mulligan-card\.prompt-card-candidate\)[^{]*\{[^}]*height:100%!important[^}]*min-height:0!important[^}]*max-height:184px!important/,
  'mobile and rotated mulligan cards must fit their actual row height, capped at the original 184px footprint')
assert.match(style, /html\[data-l12-mobile="true"\]\[data-l12-viewport\][^}]*\.mulligan-card \.l12-card-image\)[^{]*\{[^}]*height:calc\(100% - 32px\)!important[^}]*max-height:148px!important/,
  'short rows must shrink the image while preserving the complete name and spacing')
assert.doesNotMatch(style, /data-l12-rotated="true"[^}]*\.mulligan-card\.prompt-card-candidate\)[^{]*\{[^}]*height:184px!important/,
  'a more specific rotated rule must not override the actual-row height constraint')
assert.doesNotMatch(style, /data-l12-rotated="true"[^}]*\.mulligan-card \.l12-card-image\)[^{]*\{[^}]*height:148px!important/,
  'rotated images must share the same bounded height owner as normal short landscape')
assert.match(style, /@media\(max-width:900px\) and \(max-height:500px\)/,
  'short landscape battle canvases need a bounded opening layout')
assert.match(style, /\.opening-presentation\.skipped[^}]*opening-master-card/,
  'skip must end the entrance motion without changing game state')
assert.match(style, /html\[data-l12-animation="off"\] \.opening-master-card\)\{opacity:1;transform:translate3d\(calc\(-50% \+ var\(--opening-side-x\)\),calc\(-50% \+ var\(--opening-side-y\)\),0\)!important\}/,
  'animation off must retain the same static two-side master layout')
assert.match(style, /html\[data-l12-animation="off"\] \.opening-versus-player\)\{opacity:1/,
  'animation off must retain the same static matchup identity and versus')
assert.doesNotMatch(style, /prefers-reduced-motion/, 'opening CSS cannot override standard/fast with the OS hint')
assert.match(style, /var\(--l12-animation-scale,1\)/,
  'new opening CSS must consume the existing fast/off animation scale')

console.log('Match opening presentation passed: controlled match lifecycle, authoritative dice, motion preferences and independent result.')
