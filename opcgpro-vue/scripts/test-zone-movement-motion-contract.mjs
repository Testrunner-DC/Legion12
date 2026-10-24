import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse } from '@vue/compiler-sfc'
import ts from 'typescript'

// Keep the old single-entity/source-pixel contract. The only staged routes are
// the explicitly approved public legion entry and public disaster reveal; they
// are not a generic mid-flight enlargement exception for other moves or concealed cards.
export function hasScopedZoneMovementMotion(source) {
  try {
    source = source.replace(/\r\n?/g, '\n')
    for (const required of ['publicHolder.cloneNode(true)',
      'snapshotDecodedAuthoritativeCardVisual(publicHolder, sourceGhost)',
      'genericPublicSourceGhost(draft.from)', 'activeGhostAnimation = wrapper.animate(frames,',
      'disasterPublicSourceGhost()', 'disasterRevealFrames(movement)',
      "back.src = '/assets/l12/card-back-disaster.png'",
      "document.querySelector('[data-l12-battle-layout]')",
      'button.current-disaster-card[data-l12-zone="disaster"]',
      'publicHolder instanceof HTMLElement && !usesGraveyardAnchor && !draft.disasterReveal'])
      assert(source.includes(required), required)
    assert(!source.includes('--move-mid-x') && !source.includes('l12-zone-card-pulse'))
    const holderStart = source.indexOf('function createMovementHolder(')
    const holderEnd = source.indexOf('\nfunction showNext(', holderStart)
    const holder = source.slice(holderStart, holderEnd)
    assert(holder.includes('if (destination instanceof HTMLElement) coverTarget(movement, destination)'))
    assert(holder.includes("wrapper.style.visibility = 'hidden'\n      revealTarget(movement)"))
    assert(source.includes('if (start.ghost && !draft.disasterReveal) createMovementHolder(draft, start.ghost)'))
    assert(source.includes('if (movement?.disasterReveal && movement.sourceGhost && !movement.holderWrapper)'))
    assert(source.includes('|| !createMovementHolder(movement, movement.sourceGhost)'))
    const setup = parse(source).descriptor.scriptSetup?.content
    assert(setup)
    const tree = ts.createSourceFile('movement.ts', setup, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
    const actualFunction = name => {
      const declaration = tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
      assert(declaration, name)
      const js = ts.transpileModule(declaration.getText(tree), {
        compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext },
      }).outputText
      return Function(js + '\nreturn ' + name)()
    }
    const entry = actualFunction('isLegionEntryMovement')
    for (const type of ['play', 'put', 'enter', 'move', 'attach', 'reveal', 'counter-set', 'mill'])
      for (const cardType of ['legion', 'tactic', 'relic', 'master', undefined])
        for (const from of ['hand', 'library', 'graveyard', 'resolving', 'field'])
          for (const to of ['field', 'hand', 'graveyard'])
            for (const concealed of [false, true]) {
              const expected = !concealed && cardType === 'legion' && from !== 'field' && to === 'field'
                && ['play', 'put', 'enter'].includes(type)
              assert.equal(Boolean(entry({ type }, { cardType }, from, to, concealed)), expected)
            }
    assert.equal(Boolean(entry({ type: 'play' }, undefined, 'hand', 'field', false)), false)

    const impact = actualFunction('legionEntryImpact')
    for (const troops of [undefined, null, 0, -1, NaN, Infinity, '3000', true])
      assert.deepEqual(impact({ troops }), { strength: 0 })
    assert.deepEqual(impact({ troops: 2000 }), { tier: 'light', strength: .35 })
    assert.deepEqual(impact({ troops: 3000 }), { tier: 'medium', strength: .65 })
    assert.deepEqual(impact({ troops: 6000 }), { tier: 'heavy', strength: 1 })
    assert.deepEqual(impact({ troops: null, baseTroops: 6000 }), { tier: 'heavy', strength: 1 })
    assert.deepEqual(impact({ troops: 0, baseTroops: 6000 }), { strength: 0 })

    const simplified = actualFunction('simplifiedEntryMotion')
    // Execute only this actual pure preference predicate with isolated globals.
    const preference = Function('document', 'window', `return (${simplified.toString()})()`)
    for (const off of [false, true]) for (const reduce of [false, true])
      assert.equal(preference({ documentElement: { dataset: { l12Animation: off ? 'off' : 'standard' } } },
        { matchMedia: query => ({ matches: query === '(prefers-reduced-motion: reduce)' && reduce }) }), off || reduce)

    const declarations = new Map()
    const walk = node => {
      if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name)
        && ['frames', 'directFrames'].includes(node.name.text)) {
        assert(!declarations.has(node.name.text), 'ambiguous frame selector')
        declarations.set(node.name.text, node.initializer)
      }
      ts.forEachChild(node, walk)
    }
    const showNext = tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'showNext')
    assert(showNext)
    walk(showNext)
    const frames = declarations.get('frames')
    const direct = declarations.get('directFrames')
    assert(ts.isArrayLiteralExpression(direct) && direct.elements.length === 2)
    assert(ts.isConditionalExpression(frames) && frames.condition.getText(tree) === 'movement.disasterReveal')
    assert.equal(frames.whenTrue.getText(tree), 'disasterRevealFrames(movement)')
    const ordinaryFrames = frames.whenFalse
    assert(ts.isConditionalExpression(ordinaryFrames) && ordinaryFrames.condition.getText(tree) === 'movement.legionEntry')
    const entryFrames = ordinaryFrames.whenTrue
    assert(ts.isConditionalExpression(entryFrames) && entryFrames.condition.getText(tree) === 'movement.entrySimplified')
    assert.equal(entryFrames.whenTrue.getText(tree), 'directFrames')
    assert.equal(entryFrames.whenFalse.getText(tree), 'legionEntryFrames(movement)')
    assert(ts.isArrayLiteralExpression(ordinaryFrames.whenFalse) && ordinaryFrames.whenFalse.elements.length === 4)
    for (const regular of [direct, ordinaryFrames.whenFalse]) {
      assert(!/center|focus|legionEntryFrames|impactScale/.test(regular.getText(tree)))
    }
    const disasterFrames = tree.statements.find(node => ts.isFunctionDeclaration(node)
      && node.name?.text === 'disasterRevealFrames')
    assert(disasterFrames?.body)
    const simplifiedDisaster = disasterFrames.body.statements.find(node => ts.isIfStatement(node))
    assert(ts.isIfStatement(simplifiedDisaster)
      && simplifiedDisaster.expression.getText(tree) === 'movement.disasterSimplified')
    assert(ts.isReturnStatement(simplifiedDisaster.thenStatement)
      && ts.isArrayLiteralExpression(simplifiedDisaster.thenStatement.expression)
      && simplifiedDisaster.thenStatement.expression.elements.length === 2)
    const fullDisaster = disasterFrames.body.statements.at(-1)
    assert(ts.isReturnStatement(fullDisaster) && ts.isArrayLiteralExpression(fullDisaster.expression)
      && fullDisaster.expression.elements.length === 6)
    for (const css of [
      '.moving-card.legion-entry:not(.entry-simplified){animation-name:l12-legion-entry-flight;',
      '.moving-card.legion-entry.entry-simplified{animation-name:l12-legion-entry-handoff;',
      '.zone-card-movement:not(.movement-ready) .legion-entry-impact{animation-play-state:paused}',
    ]) assert(source.includes(css), css)
    return true
  } catch {
    return false
  }
}

const source = readFileSync(new URL('../src/l12/game/ZoneMovementPresentationLayer.vue', import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
assert(hasScopedZoneMovementMotion(source), 'actual movement source must retain scoped choreography and old source/ordinary-move guards')
const mutations = [
  ['concealed entries', '!concealed && card?.cardType', 'card?.cardType'],
  ['non-legion entries', "card?.cardType === 'legion'", 'true'],
  ['field-to-field entries', "&& from !== 'field'", ''],
  ['non-field destination', "&& to === 'field'", ''],
  ['other event entries', "event.type === 'enter')", "event.type === 'enter' || event.type === 'move')"],
  ['source identity', 'publicHolder.cloneNode(true)', 'publicHolder.cloneNode(false)'],
  ['decoded source pixels', 'snapshotDecodedAuthoritativeCardVisual(publicHolder, sourceGhost)', 'unverifiedSourcePixels(publicHolder, sourceGhost)'],
  ['unknown-zone card back', 'genericPublicSourceGhost(draft.from)', 'guessedSourceFace(draft.from)'],
  ['single WAAPI path', 'activeGhostAnimation = wrapper.animate(frames,', 'activeGhostAnimation = wrapper.animate([],'],
  ['unscoped staged animation', 'const frames = movement.disasterReveal', 'const frames = true'],
  ['generic disaster staging', 'const frames = movement.disasterReveal', 'const frames = movement.disasterReveal || movement.legionEntry'],
  ['disaster uses legion choreography', '? disasterRevealFrames(movement)', '? legionEntryFrames(movement)'],
  ['disaster loses essential handoff', 'if (movement.disasterSimplified) return [', 'if (false) return ['],
  ['disaster back guessed from authority face', "back.src = '/assets/l12/card-back-disaster.png'", "back.src = movement.card.imageUrl"],
  ['disaster pixels snapshot from settled face', '&& !usesGraveyardAnchor && !draft.disasterReveal)', '&& !usesGraveyardAnchor)'],
  ['disaster target leaves battle layout', "document.querySelector('[data-l12-battle-layout]')", 'document'],
  ['disaster target widened to a zone', 'button.current-disaster-card[data-l12-zone="disaster"]', '[data-l12-zone="disaster"]'],
  ['disaster owner skips release', "wrapper.style.visibility = 'hidden'\n      revealTarget(movement)", "wrapper.style.visibility = 'hidden'"],
  ['reduced animation central flight', '? movement.entrySimplified ? directFrames : legionEntryFrames(movement)', '? movement.entrySimplified ? legionEntryFrames(movement) : legionEntryFrames(movement)'],
  ['regular move central flight', ': [\n              { transform:', ': legionEntryFrames(movement) || [\n              { transform:'],
  ['off preference', "dataset.l12Animation === 'off'", "dataset.l12Animation === 'unsupported'"],
  ['reduced preference', "'(prefers-reduced-motion: reduce)'", "'(unsupported: reduce)'"],
  ['string troop coercion', "typeof troops !== 'number' || !Number.isFinite(troops)", '!Number.isFinite(Number(troops))'],
  ['non-finite troops', '!Number.isFinite(troops)', 'false'],
  ['zero troop fallback', 'rawTroops == null ?', '!rawTroops ?'],
  ['unguarded template entry', '.moving-card.legion-entry:not(.entry-simplified)', '.moving-card'],
  ['unpaused preparation impact', '.zone-card-movement:not(.movement-ready) .legion-entry-impact', '.unrelated .legion-entry-impact'],
  ['old generic midpoint', '</style>', '.moving-card{--move-mid-x:0}</style>'],
  ['old generic pulse', '</style>', '@keyframes l12-zone-card-pulse{}</style>'],
]
for (const [name, before, after] of mutations) {
  assert(source.includes(before), `mutation target must exist: ${name}`)
  const changed = source.replace(before, after)
  assert.equal(hasScopedZoneMovementMotion(changed), false, `contract must reject ${name}`)
}
console.log(`Scoped zone-motion source contract passed: actual source + ${mutations.length} hostile mutations`)
