import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { compileStyle, compileTemplate, parse } from '@vue/compiler-sfc'

const read = relative => readFileSync(new URL(`../${relative}`, import.meta.url), 'utf8')
const playerMat = read('src/l12/game/PlayerMat.vue')
const movement = read('src/l12/game/ZoneMovementPresentationLayer.vue')
const visibilitySource = read('src/l12/game/authoritativeCardVisibility.ts')
const masterZoneTemplate = playerMat.slice(playerMat.indexOf('<div class="master-column"'), playerMat.indexOf('<div class="relic-zone"'))
const parsedPlayerMat = parse(playerMat, { filename: 'PlayerMat.vue' })
assert.deepEqual(parsedPlayerMat.errors, [], 'PlayerMat must remain a valid SFC')
assert.deepEqual(compileTemplate({
  source: parsedPlayerMat.descriptor.template?.content ?? '', filename: 'PlayerMat.vue', id: 'master-zone-lifecycle',
}).errors, [], 'the persistent master-zone template must compile')
for (const style of parsedPlayerMat.descriptor.styles) assert.deepEqual(compileStyle({
  source: style.content, filename: 'PlayerMat.vue', id: 'master-zone-lifecycle', scoped: style.scoped,
}).errors, [], 'the persistent master-zone styles must compile')
const output = ts.transpileModule(visibilitySource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const {
  acquireAuthoritativeCardVisibility,
  authoritativeCardVisibilityLeaseCount,
  resolveAuthoritativeCardVisibilitySurface,
} = await import(`data:text/javascript;base64,${Buffer.from(output).toString('base64')}`)

function element({ visibility = '', persistent = false, surface = null } = {}) {
  return {
    style: { visibility },
    matches(selector) {
      return selector === '[data-l12-persistent-card-zone]' ? persistent
        : selector === '[data-l12-authoritative-card-visibility-surface]' && this.isSurface === true
    },
    querySelector(selector) {
      return selector === '[data-l12-authoritative-card-visibility-surface]' ? surface?.current ?? null : null
    },
  }
}

assert.match(masterZoneTemplate,
  /<button[^>]*class="mini-master"[^>]*data-l12-persistent-card-zone[\s\S]*?<i[^>]*class="master-visual-body"[^>]*data-l12-authoritative-card-visibility-surface[\s\S]*?<CardImage v-if="!player\.master\.deployedAsLegion"/,
  'the persistent master zone must own a stable visual-body anchor; only its card face may follow deployedAsLegion')
assert.doesNotMatch(masterZoneTemplate, /<span[^>]*class="master-visual-body"/,
  'the visual-body must not be a direct span child captured by existing master-name desktop/mobile selectors')
assert.match(playerMat, /\.master-visual-body\{[^}]*position:absolute;[^}]*inset:0;[^}]*display:grid;[^}]*font-style:normal/,
  'the non-text visual body must retain a full-frame absolute grid without inheriting italic element defaults')
assert.doesNotMatch(`${masterZoneTemplate}\n${movement}\n${visibilitySource}`, /S01-0417|孙悟空/,
  'the persistent master-zone lifecycle must be shared and must not special-case Sun Wukong')
assert.match(playerMat, /'master-legion-deployed': player\.master\.deployedAsLegion/,
  'both players and spectator projections must expose the same state-derived persistent-zone contract')

const firstFace = Object.assign(element(), { isSurface: true })
const currentFace = { current: firstFace }
const masterZone = element({ persistent: true, surface: currentFace })
assert.equal(resolveAuthoritativeCardVisibilitySurface(masterZone), firstFace,
  'master visibility ownership resolves to card pixels rather than the persistent zone frame')
const releaseEntry = acquireAuthoritativeCardVisibility(masterZone)
const releaseReveal = acquireAuthoritativeCardVisibility(masterZone)
assert.equal(masterZone.style.visibility, '', 'master-to-legion entry keeps the master-zone frame visible')
assert.equal(firstFace.style.visibility, 'hidden', 'the presentation owner covers only the authoritative card pixels')
assert.equal(authoritativeCardVisibilityLeaseCount(masterZone), 2,
  'player, opponent, and spectator surfaces share the same overlap-safe visibility ledger')
releaseEntry()
assert.equal(firstFace.style.visibility, 'hidden', 'a skip/cancel cannot reveal pixels retained by another owner')
releaseReveal()
assert.equal(firstFace.style.visibility, '', 'the final handoff restores the settled master face')

const oldFace = Object.assign(element(), { isSurface: true })
currentFace.current = oldFace
const releaseOld = acquireAuthoritativeCardVisibility(masterZone)
const newFace = Object.assign(element({ visibility: 'visible' }), { isSurface: true })
currentFace.current = newFace
const releaseNew = acquireAuthoritativeCardVisibility(masterZone)
assert.equal(oldFace.style.visibility, 'hidden')
assert.equal(newFace.style.visibility, 'hidden', 'a return-to-master DOM replacement is covered before the old face is released')
releaseOld()
assert.equal(oldFace.style.visibility, '', 'a detached old face restores only itself')
assert.equal(newFace.style.visibility, 'hidden', 'a late old callback cannot reveal the replacement face')
releaseNew()
assert.equal(newFace.style.visibility, 'visible', 'the replacement restores its own original visibility exactly')

currentFace.current = null
const releaseNoFace = acquireAuthoritativeCardVisibility(masterZone)
assert.equal(masterZone.style.visibility, '', 'a deployed master with no face still retains its zone, name, and health')
assert.equal(authoritativeCardVisibilityLeaseCount(masterZone), 0,
  'a missing source or not-yet-mounted destination does not fabricate a visibility lease on the frame')
releaseNoFace()

const ordinaryCard = element()
const releaseOrdinary = acquireAuthoritativeCardVisibility(ordinaryCard)
assert.equal(ordinaryCard.style.visibility, 'hidden', 'ordinary cards preserve the existing whole-card visibility behavior')
releaseOrdinary()
assert.equal(ordinaryCard.style.visibility, '')

assert.match(movement, /coveredVisibilitySurface\?: HTMLElement \| null/,
  'zone movement must remember the concrete visual body, not only the stable outer master anchor')
assert.match(movement,
  /movement\.coveredTarget === target[\s\S]*movement\.coveredVisibilitySurface === visibilitySurface/,
  'an unchanged outer master anchor may early-return only while its internal visual body is also unchanged')
assert.match(movement, /attributeFilter:\['data-card-instance-id'\]/,
  'the destination observer remains identity-scoped and must not self-loop on style visibility')

console.log('master zone lifecycle contracts passed')
