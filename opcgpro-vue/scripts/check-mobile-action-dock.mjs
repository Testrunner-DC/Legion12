import assert from 'node:assert/strict'
import fs from 'node:fs'
import postcss from 'postcss'
const read = name => fs.readFileSync(new URL(`../${name}`, import.meta.url), 'utf8')
const board = read('src/l12/game/GameBoard.vue')
const dock = read('src/l12/game/MobileBattleDock.vue')
const css = read('src/l12/game/MobileBattleDock.css')
const portal = read('src/l12/game/BattleDockPortal.vue')
const legacy = read('src/l12/game/GameBoard.mobile.css')
const viewport = read('src/l12/mobileViewport.css')
const utility = read('src/l12/game/BattleUtilityDock.vue')
const playerMat = read('src/l12/game/PlayerMat.vue')
const identity = read('src/l12/game/BattlePlayerIdentity.vue')
const sandbox = read('src/l12/site/SandboxPage.vue')
const sandboxMobile = read('src/l12/site/SandboxPage.mobile.css')
const gamePage = read('src/l12/GamePage.vue')
const settings = read('src/l12/site/L12SettingsModal.vue')
const victory = read('src/l12/game/OsirisVictorySequence.vue')
const source = board.split('<style scoped>')[1].split('</style>')[0]
assert.ok(!source.includes('.mobile-battle-dock'), 'dock geometry entered desktop stylesheet')
assert.ok(board.includes('<MobileBattleDock v-if="mobileLandscapeViewport" />'))
assert.equal((board.match(/<BattleUtilityDock /g) || []).length, 1, 'utility must have one state owner')
assert.ok(portal.includes(':disabled="!target"'), 'desktop must retain original DOM locations')
assert.ok(portal.includes("target || 'body'"), 'unmounted target must not create invalid teleport')
for (const lane of ['route','tools','context','primary','utility']) assert.ok(dock.includes(`bind('${lane}', element)`))
assert.ok(gamePage.includes('<BattleDockPortal lane="route"><div')
  && dock.includes('class="mobile-battle-dock__route"')
  && portal.includes("props.lane === 'route' ? landscapeTeleportTarget() : null")
  && css.includes('.mobile-battle-dock__route > .battle-route-controls')
  && css.includes('z-index:var(--l12-battle-fixed-controls-z,2147483632)')
  && viewport.includes('--l12-battle-fixed-controls-z:2147483632'),
  'route controls must live outside the masked dock and share the protected host layer')
assert.ok(victory.includes('z-index:var(--l12-battle-victory-layer,5050)')
  && viewport.includes('--l12-battle-victory-layer:5050')
  && viewport.includes('--l12-battle-victory-layer:2147483628')
  && viewport.includes(':has(> :is(.osiris-victory-sequence,.game-over)) :is(.mobile-card-inspector-handle-global,.mobile-card-inspector.mobile-safe-overlay)')
  && gamePage.includes("gameOverMinimized || osirisSequencePlaying"),
  'special-victory visuals must block board hits below the protected route without hiding the return action')
for (const file of ['HandArea','PlayerMat']) assert.ok(read(`src/l12/game/${file}.vue`).includes('<BattleDockPortal lane="context">'))
for (const file of ['PromptOverlay','MasterOverlay','GraveyardOverlay','PlayerMat']) assert.ok(read(`src/l12/game/${file}.vue`).includes('mobileDock?.context'))
assert.ok(!board.includes('mobileInspectorOpen.value = true'), 'selecting an object must not force the inspector open')
assert.ok(!board.includes('mobileIdentityOpen'), 'mobile identity must stay expanded')
assert.ok(!board.includes('>双方信息</button>'), 'mobile identity must not expose a collapse control')
assert.ok(board.includes('class="mobile-player-summary-strip"')
  && !board.includes('mobile-player-details-overlay mobile-safe-overlay')
  && !board.includes('mobilePlayerDetailsOpen'),
  'both complete mobile player identities must stay expanded without a separate detail overlay')
assert.ok(board.includes('<BattleDockPortal lane="context"><button v-if="mobileLandscapeViewport" type="button" class="mobile-record-trigger"'), 'mobile record entry must own the context lane beneath transient combat information')
assert.ok(css.includes('.mobile-battle-dock__context { position:relative;')
  && css.includes('.mobile-battle-dock__context > .combat-presentation')
  && css.includes('position:absolute !important; z-index:3 !important; inset:0 0 auto !important;')
  && css.includes('pointer-events:none !important;')
  && css.includes('pointer-events:auto !important;'), 'transient combat information must overlay without consuming dock height, pass through record taps, and retain its explicit minimize action')
assert.ok(identity.includes('showMasterDetails?: boolean') && identity.includes('v-if="showMasterDetails" class="identity-master-row"'), 'current master details must be opt-in so desktop identity stays concise')
assert.equal((board.match(/show-master-details/g) || []).length, 2, 'current master details must appear in the two persistent mobile identities')
assert.ok(identity.includes('identity-rank-row') && identity.includes('identity-tier-row') && identity.includes('identity-master-title-row'), 'player identity must preserve authoritative rank, tier and master-title fields')
assert.ok(!playerMat.includes('<img v-if="mobileMoralePicker && factionLogoUrls[player.faction]"'), 'legacy mobile faction icon cannot remain inside the morale label')
assert.ok(!legacy.includes(':global(.mobile-action-dock)'), 'old independent fixed dock cannot reappear')
assert.ok(legacy.includes('--l12-mobile-right-rail-w:calc(var(--l12-mobile-dock-width) + 8px)'))
assert.ok(css.includes('--l12-mobile-left-rail-w:max(78px,calc(var(--l12-mobile-master-w) * 1.3))'))
assert.ok(css.includes('--l12-mobile-dock-width:clamp(112px,calc(var(--l12-viewport-width,100vw) * .15),132px)'))
assert.ok(css.includes('class="mobile-battle-dock__utility"') || dock.includes('class="mobile-battle-dock__utility"'))
assert.ok(legacy.includes('--l12-mobile-relic-w:calc(var(--l12-mobile-master-w) * .9)'))
assert.ok(legacy.includes('--l12-mobile-pile-w:calc(var(--l12-mobile-master-w) * .7)'))
assert.ok(legacy.includes('padding-bottom:calc(var(--l12-mobile-utility-h,34px) + 4px)'))
assert.ok(legacy.includes('outline-offset:-2px'))
assert.ok(legacy.includes('grid-column:1/-1 !important; grid-row:3 !important') && legacy.includes('object-fit:contain !important; object-position:center !important; transform:none !important'), 'mobile master markers must own one centred track below the commander cards with contained images')
assert.ok(sandbox.includes('<style src="./SandboxPage.mobile.css"></style>') && sandboxMobile.includes('html[data-l12-mobile=true][data-l12-viewport]') && sandboxMobile.includes('var(--l12-viewport-width') && sandboxMobile.includes('.saved-deck-selector') && sandboxMobile.includes('.l12-settings-modal'), 'sandbox entry, deck selector and settings must share the logical mobile canvas')
assert.ok(!sandboxMobile.includes('orientation:') && !sandboxMobile.includes('pointer:coarse'), 'sandbox mobile layout cannot infer physical orientation or device identity')
assert.ok(viewport.includes('> .mobile-safe-overlay:not(.minimized):not(.mobile-card-inspector)'))
postcss.parse(css).walkRules(rule => {
  for (const selector of rule.selectors) assert.ok(selector.includes('.mobile-battle-dock') || selector.includes('[data-l12-mobile=true]'), `ungated desktop selector ${selector}`)
})
for (const rule of ['overscroll-behavior:contain','overflow:auto','pointer-events:auto','min-height:34px','white-space:normal','z-index:2200','z-index:2147483620']) assert.ok(css.includes(rule), `missing dock contract ${rule}`)
assert.ok(!css.includes('.bug-feedback-trigger') && !utility.includes("l12-open-bug-feedback") && !utility.includes('Bug反馈'), 'battle dock must not own a direct feedback entry')
assert.ok(settings.includes('class="feedback-entry"') && settings.includes("emit('feedback')") && gamePage.includes('@feedback="openBattleFeedback"'), 'battle feedback must open from the shared Settings modal')
const runtime = read('scripts/verify-mobile-action-dock.mjs')
for (const invariant of ['elementFromPoint','fieldOverlap','laneOverlap','setSafeAreaInsetsOverride','setPageScaleFactor','maximum-real-actions','commandIs','leftRatio','relicRatio','pileRatio','handSelectionVisible','playerDetailsVisible','markerGeometry','playerIdentitiesAboveRecord','moraleLabels']) assert.ok(runtime.includes(invariant))

function assertCombatDecisionLayout(styles) {
  const parsed = postcss.parse(styles)
  const base = '#l12-landscape-teleports .mobile-battle-dock__primary .combat-resolution-panel'
  const declarations = selector => {
    const values = new Map()
    let count = 0
    parsed.walkRules(rule => {
      if (!rule.selectors.includes(selector)) return
      if (rule.parent.type !== 'root') return
      count++
      rule.walkDecls(declaration => values.set(declaration.prop, declaration.value))
    })
    assert.equal(count, 1, `combat layout must have one unambiguous rule: ${selector}`)
    return values
  }
  const panel = declarations(base), actions = declarations(`${base} .l12-actions`)
  const actor = declarations(`${base} .l12-actions > .action-actor-badge`)
  const buttons = declarations(`${base} .l12-actions > button`)
  const minimize = declarations('#l12-landscape-teleports .mobile-battle-dock .combat-decision-minimize')
  assert.equal(panel.get('height'), 'auto', 'combat decision panel must grow for actor and decision rows')
  assert.equal(panel.get('max-height'), 'none', 'combat decision panel must not clip its second row')
  assert.equal(actions.get('height'), 'auto', 'combat action grid must size both content rows')
  assert.equal(actions.get('grid-template-columns'), 'repeat(2,minmax(0,1fr))', 'combat decisions must remain equal peers')
  assert.equal(actor.get('grid-column'), '1/-1', 'combat actor must own one complete row before both decisions')
  assert.equal(buttons.get('grid-column'), 'auto', 'neither combat decision may consume the actor row')
  assert.equal(buttons.get('height'), '42px', 'combat decisions must retain the enlarged mobile hit-box height')
  assert.equal(buttons.get('min-height'), buttons.get('height'), 'combat decision hit boxes must remain equal')
  assert.equal(minimize.get('order'), '-1', 'combat minimize must precede long passive status in the bounded context')
  const shortButtons = new Map()
  let shortCount = 0
  parsed.walkRules(rule => {
    if (!rule.selectors.includes(`${base} .l12-actions > button`)
      || rule.parent.type !== 'atrule' || rule.parent.params !== '(max-height:340px)') return
    shortCount++
    rule.walkDecls(declaration => shortButtons.set(declaration.prop, declaration.value))
  })
  assert.equal(shortCount, 1, 'very short screens must have one explicit combat hit-box override')
  assert.equal(shortButtons.get('height'), '40px', 'very short screens must retain a 40px combat hit box')
  assert.equal(shortButtons.get('min-height'), shortButtons.get('height'), 'very short combat hit boxes must remain equal')
}
assertCombatDecisionLayout(css)
assert.throws(() => assertCombatDecisionLayout(css.replace('height:auto !important; max-height:none !important;', 'height:42px !important; max-height:42px !important;')), /must grow/)
assert.throws(() => assertCombatDecisionLayout(css.replace('grid-column:1/-1; justify-self:start;', 'grid-column:auto; justify-self:start;')), /complete row/)
assert.throws(() => assertCombatDecisionLayout(css.replace('width:auto !important; min-height:42px !important; height:42px !important;', 'width:auto !important; min-height:38px !important; height:42px !important;')), /remain equal/)
assert.throws(() => assertCombatDecisionLayout(css.replace('order:-1; pointer-events:auto !important;', 'pointer-events:auto !important;')), /minimize must precede/)
function assertContextScrollOwnership(styles) {
  const selector = '#l12-landscape-teleports .mobile-battle-dock__context > :is(.card-context-actions,.board-target-controls,.board-mode-hint)'
  const values = new Map()
  let count = 0
  postcss.parse(styles).walkRules(rule => {
    if (rule.selector !== selector) return
    count++
    assert.equal(rule.parent.type, 'root', 'interactive context rule must apply without a media override')
    rule.walkDecls(declaration => {
      assert.equal(declaration.important, true, 'interactive context must override the shared clipping declaration')
      values.set(declaration.prop, declaration.value)
    })
  })
  assert.equal(count, 1, 'interactive context scroll ownership must have one exact rule')
  assert.equal(values.get('max-height'), 'none', 'interactive context child must retain its intrinsic control height')
  assert.equal(values.get('overflow'), 'visible', 'only the existing context may clip and scroll interactive controls')
}
assertContextScrollOwnership(css)
assert.throws(() => assertContextScrollOwnership(css.replace('max-height:none !important; overflow:visible !important;', 'max-height:100% !important; overflow:hidden !important;')), /intrinsic control height/)
for (const consumer of ['gm-placement-controls', 'inline-prompt-controls', 'board-slot-controls', 'resource-payment-controls'])
  assert.ok(board.includes(consumer), `shared context regression must retain ${consumer}`)
const interaction = read('scripts/verify-mobile-interaction-matrix.mjs')
for (const invariant of ['visibleRatio', 'elementFromPoint', 'touchscreen.tap', 'actorAboveDecisions', 'sameHitBox', 'nativeContextReveal', 'mouse.wheel', 'scrollTop'])
  assert.ok(interaction.includes(invariant), `combat runtime must preserve ${invariant}`)
console.log('Mobile action dock contracts passed: single owners, safe canvas, flow lanes, restored prompts, Settings feedback and desktop isolation.')
