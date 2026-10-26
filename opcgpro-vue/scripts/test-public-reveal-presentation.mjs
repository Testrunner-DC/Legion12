import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/game/publicRevealPresentation.ts', import.meta.url), 'utf8')
const output = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { createPublicRevealPresentationTracker, findLinkedPublicContinuationParent } = await import(
  `data:text/javascript;base64,${Buffer.from(output).toString('base64')}`)

const publicCard = { instanceId:'focused-oiran-public-card', cardId:'S01-0212', name:'合成公开高天原军团',
  cardType:'legion', faction:'gaotianyuan', cost:1, baseTroops:2000, troops:2000, disasterLevel:0, tapped:false, summonRound:0 }
const abilityId = 'S01-0419:ability:play:97db2728de7886de'
const event = (sequence, type, overrides = {}) => ({
  sequence, type, playerIndex:0, text:type === 'reveal' ? '展示并加入手牌' : '加入手牌结果',
  effectAbilityId:abilityId, effectSceneId:`${abilityId}:presentation:${type === 'reveal' ? 'reveal-add' : 'search-add'}`,
  cards:[publicCard], ...overrides,
})
const classify = (...events) => {
  const tracker = createPublicRevealPresentationTracker()
  return events.map(item => tracker.classify(item))
}
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search')), [
  { kind:'full-card' },
  { kind:'linked-continuation', parentSequence:1 },
], 'the proven producer pair must carry its exact parent presentation sequence')
assert.deepEqual(classify(event(1, 'reveal'), event(3, 'search')), [
  { kind:'full-card' }, { kind:'linked-continuation', parentSequence:1 },
],
  'one intervening private hand-add sequence still belongs to the producer transaction')
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search'), event(3, 'reveal'), event(4, 'search')),
  [
    { kind:'full-card' }, { kind:'linked-continuation', parentSequence:1 },
    { kind:'full-card' }, { kind:'linked-continuation', parentSequence:3 },
  ], 'a later real reveal must present again with its own parent identity')
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'reveal'), event(3, 'search')),
  [{ kind:'full-card' }, { kind:'full-card' }, { kind:'full-card' }],
  'ambiguous independent activations must never be joined by proximity')
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'effect-result'), event(3, 'search')),
  [{ kind:'full-card' }, { kind:'full-card' }, { kind:'full-card' }],
  'an interleaved presentation must preserve the conservative fallback')
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search', { effectAbilityId:'other' })),
  [{ kind:'full-card' }, { kind:'full-card' }])
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search', { playerIndex:1 })),
  [{ kind:'full-card' }, { kind:'full-card' }])
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search', { cards:[{ ...publicCard, instanceId:'other' }] })),
  [{ kind:'full-card' }, { kind:'full-card' }])
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search', { effectSceneId:undefined })),
  [{ kind:'full-card' }, { kind:'full-card' }])
assert.deepEqual(classify(event(1, 'reveal'), event(4, 'search')),
  [{ kind:'full-card' }, { kind:'full-card' }], 'a wider event gap is not a proven shared presentation')
assert.deepEqual(classify(event(1, 'reveal', { cards:[{ ...publicCard, hidden:true }] }), event(2, 'search')),
  [{ kind:'full-card' }, { kind:'full-card' }], 'hidden identity must not authorize presentation linking')
{
  const tracker = createPublicRevealPresentationTracker()
  assert.deepEqual(tracker.classify(event(1, 'reveal')), { kind:'full-card' })
  tracker.reset()
  assert.deepEqual(tracker.classify(event(2, 'search')), { kind:'full-card' },
    'recovery or match reset must clear pending correlation')
}

const parentItem = { sequence:1, linkedContinuationTexts:[] }
const unrelatedItem = { sequence:2, linkedContinuationTexts:[] }
const linkedDecision = { kind:'linked-continuation', parentSequence:1 }
assert.equal(findLinkedPublicContinuationParent(linkedDecision, new Map([[1, parentItem]]), [parentItem]), parentItem,
  'the exact parent may be reused while it is the current tail of this delivered batch')
assert.equal(findLinkedPublicContinuationParent(linkedDecision, new Map([[1, parentItem]]), [parentItem, unrelatedItem]), null,
  'a continuation must not jump across another authoritative presentation reservation')
assert.equal(findLinkedPublicContinuationParent(linkedDecision, new Map(), [parentItem]), null,
  'a parent from an earlier snapshot or already-started presentation cannot be joined retroactively')
assert.equal(findLinkedPublicContinuationParent({ kind:'full-card' }, new Map([[1, parentItem]]), [parentItem]), null,
  'an unproven event must retain the conservative independent fallback')

const gameBoard = readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')
assert.ok(gameBoard.includes('publicRevealPresentationTracker.reset()'), 'match/recovery reset clears the correlation')
assert.ok(gameBoard.includes('const publicRevealDecision = publicRevealPresentationTracker.classify(event)'),
  'the authoritative public event queue keeps the exact tracker decision')
assert.ok(gameBoard.includes('findLinkedPublicContinuationParent(publicRevealDecision, batchParents, publicRevealQueue)'),
  'a continuation may join only its exact queued parent from the same delivered batch and queue tail')
assert.ok(gameBoard.includes('parent.linkedContinuationTexts.push(publicRevealText(event))'),
  'the proven child keeps its result text inside the parent presentation')
assert.ok(gameBoard.includes("text: [next.text, ...next.linkedContinuationTexts].join('\\n')"),
  'the one shared presentation keeps both authoritative texts in sequence order')
assert.ok(gameBoard.includes("linkedPublicContinuation: publicRevealDecision.kind === 'linked-continuation'"),
  'a proven late or interaction-separated child retains the prior text-only fallback')
assert.ok(gameBoard.includes('reservation: cardPresentationCoordinator.reserve(event.sequence, 10)'),
  'independent and fallback events still reserve their own authority sequence')
assert.ok(gameBoard.includes('if (parent) {') && gameBoard.includes('continue'),
  'a successfully folded child does not create a second reservation or queue item')
console.log('Public reveal producer-pair presentation: 17 logic assertions and 7 consumer contracts passed.')
await import('./test-public-effect-card-containment-20261026.mjs')
