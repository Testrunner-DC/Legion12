import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/game/publicRevealPresentation.ts', import.meta.url), 'utf8')
const output = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { createPublicRevealPresentationTracker } = await import(
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
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search')), ['full-card', 'linked-continuation'])
assert.deepEqual(classify(event(1, 'reveal'), event(3, 'search')), ['full-card', 'linked-continuation'],
  'one intervening private hand-add sequence still belongs to the producer transaction')
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search'), event(3, 'reveal'), event(4, 'search')),
  ['full-card', 'linked-continuation', 'full-card', 'linked-continuation'], 'a later real reveal must present again')
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'reveal'), event(3, 'search')),
  ['full-card', 'full-card', 'full-card'], 'ambiguous independent activations must never be joined by proximity')
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'effect-result'), event(3, 'search')),
  ['full-card', 'full-card', 'full-card'], 'an interleaved presentation must preserve the conservative fallback')
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search', { effectAbilityId:'other' })),
  ['full-card', 'full-card'])
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search', { playerIndex:1 })),
  ['full-card', 'full-card'])
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search', { cards:[{ ...publicCard, instanceId:'other' }] })),
  ['full-card', 'full-card'])
assert.deepEqual(classify(event(1, 'reveal'), event(2, 'search', { effectSceneId:undefined })),
  ['full-card', 'full-card'])
assert.deepEqual(classify(event(1, 'reveal'), event(4, 'search')),
  ['full-card', 'full-card'], 'a wider event gap is not a proven shared presentation')
assert.deepEqual(classify(event(1, 'reveal', { cards:[{ ...publicCard, hidden:true }] }), event(2, 'search')),
  ['full-card', 'full-card'], 'hidden identity must not authorize presentation linking')
{
  const tracker = createPublicRevealPresentationTracker()
  assert.equal(tracker.classify(event(1, 'reveal')), 'full-card')
  tracker.reset()
  assert.equal(tracker.classify(event(2, 'search')), 'full-card', 'recovery or match reset must clear pending correlation')
}

const gameBoard = readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')
assert.ok(gameBoard.includes('publicRevealPresentationTracker.reset()'), 'match/recovery reset clears the correlation')
assert.ok(gameBoard.includes("publicRevealPresentationTracker.classify(event) === 'linked-continuation'"),
  'the authoritative public event queue uses the tracker')
assert.ok(gameBoard.includes('cards: continuation || linkedPublicContinuation ? [] : next.cards'),
  'linked public facts retain a text continuation rather than a second full card')
assert.ok(gameBoard.includes('text: next.text'), 'linked public facts retain their result text')
console.log('Public reveal producer-pair presentation: 13 logic assertions and 4 consumer contracts passed.')
