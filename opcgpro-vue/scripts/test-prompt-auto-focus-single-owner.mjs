import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const prompt = readFileSync(new URL('../src/l12/game/PromptOverlay.vue', import.meta.url), 'utf8')
const board = readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')
const coordinator = readFileSync(new URL('../src/l12/game/presentationSequenceCoordinator.ts', import.meta.url), 'utf8')

assert.match(prompt, /autoFocusCard: \[card: Card \| null\]/,
  'automatic prompt source focus has an explicit nullable provenance channel')
assert.match(prompt, /emit\('autoFocusCard', sourceInstanceId \? cardObjectFor\(sourceInstanceId\) \?\? null : null\)/,
  'automatic source focus clears when the prompt no longer owns a source')
assert.match(prompt, /function focusChoice[\s\S]*emit\('focusCard', card\)/,
  'player choice hover remains on the explicit focus channel')
assert.match(board, /const inspectorFocusCard = computed\(\(\) => focusCard\.value \?\? promptAutoFocusCard\.value\)/,
  'explicit focus takes priority over automatic prompt focus')
assert.match(board, /presentationVisualOwnerKeys\.value\.has\(card\.instanceId\)[\s\S]*cardIsVisibleInAuthoritySurface\(card\.instanceId\)/,
  'automatic card art yields to both an exact moving owner and a visible authority surface')
assert.match(board, /if \(!card \|\| focusCard\.value\) return false/,
  'any explicit card focus wins intact, including a card different from the automatic source')
assert.match(board, /instanceId === `master-\$\{player\.playerIndex\}`[\s\S]*player\.graveyard\?\.at\(-1\)/,
  'static ownership includes the visible master and public graveyard top without projecting hidden cards')
assert.match(board, /data-inspector-provenance="inspectorProvenance"/,
  'the inspector exposes automatic versus explicit provenance to browser acceptance')
assert.match(board, /card-inspector-auto-face-suppressed[\s\S]*archive-detail-image\)\{display:none!important\}/,
  'automatic duplicate suppression removes only the detail image and keeps its copy')
assert.match(board, /@focus-card="focusCard = \$event" @auto-focus-card="promptAutoFocusCard = \$event"/,
  'automatic and explicit focus are wired independently')
assert.match(board, /public-reveal-cards \.l12-card-image\{border:0!important;outline:0!important;background:transparent!important;box-shadow:none!important;/,
  'public card animation adds no rectangular frame or backing plate')
assert.match(board, /source\.kind === 'placeholder'[\s\S]*acquirePublicRevealAuthority\(card\)[\s\S]*<Transition name="public-reveal" @after-leave="finishPublicReveal">/,
  'full-card public reveal acquires exact authority only after decode and keeps it through the completed leave transition')
assert.match(board, /public-reveal-card-pending\{visibility:hidden!important/,
  'an undecoded or failed reveal cannot replace the unique visible authority with a blank card')
assert.match(board, /publicRevealReadyInstanceIds\.value\.has\(card\.instanceId\)/,
  'automatic detail yields to a public reveal only after that exact image decoded')
assert.match(coordinator, /onVisualOwnershipChange\?\.\(visualKey, true\)/,
  'the shared coordinator publishes exact-instance acquisition')
assert.match(coordinator, /onVisualOwnershipChange\?\.\(visualKey, false\)/,
  'the shared coordinator publishes exact-instance terminal release')

console.log('Prompt automatic focus single-owner contract passed: 16/16 assertions')
