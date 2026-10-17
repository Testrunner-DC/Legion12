import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/deckEditorViewport.ts', import.meta.url), 'utf8')
const pure = source.slice(source.indexOf('export function resolveDeckEditorPortrait'), source.indexOf('export const deckEditorPortrait'))
const compiled = ts.transpileModule(pure, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 } }).outputText
const { resolveDeckEditorPortrait } = await import('data:text/javascript;base64,' + Buffer.from(compiled).toString('base64'))
const editorSource = readFileSync(new URL('../src/l12/L12DeckEditor.vue', import.meta.url), 'utf8')
const detailPolicy = editorSource.match(/function shouldOpenDeckEditorCardDetail\(width: number, height: number\) \{[\s\S]*?\n\}/)?.[0]
assert.ok(detailPolicy, 'Deck editor card-detail modal geometry policy is missing')
const detailCompiled = ts.transpileModule(`export ${detailPolicy}`, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 } }).outputText
const { shouldOpenDeckEditorCardDetail } = await import('data:text/javascript;base64,' + Buffer.from(detailCompiled).toString('base64'))
let assertions = 0
for (const [width,height] of [[320,568],[360,640],[390,844],[430,932],[600,900],[768,1024],[820,1000]]) {
  for (const preference of ['auto','on']) { assert.equal(resolveDeckEditorPortrait(width,height,preference), true); assertions++ }
  assert.equal(resolveDeckEditorPortrait(width,height,'off'), false); assertions++
}
for (const [width,height] of [[821,1000],[667,375],[844,390],[1024,768],[1366,768],[1920,1080],[820,860]]) {
  assert.equal(resolveDeckEditorPortrait(width,height), false); assertions++
}
for (const width of [319,320,599,600,819,820]) {
  assert.equal(resolveDeckEditorPortrait(width,Math.ceil(width*1.05)), true); assertions++
  assert.equal(resolveDeckEditorPortrait(width,Math.ceil(width*1.05)-1), false); assertions++
}
for (const [width,height,expected] of [
  [390,844,true], [820,521,true], [820,1000,true],
  [821,520,true], [821,521,false],
  [844,390,true], [844,520,true], [844,521,false],
  [900,520,true], [900,521,false], [901,520,false],
  [1024,500,false], [1366,768,false],
]) {
  assert.equal(shouldOpenDeckEditorCardDetail(width,height), expected, `${width}x${height} card-detail modal policy`); assertions++
}
const selectCard = editorSource.match(/function selectCard\(card: DeckCard\) \{[\s\S]*?\n\}/)?.[0] ?? ''
assert.match(selectCard, /shouldOpenDeckEditorCardDetail\(window\.innerWidth, window\.innerHeight\)/); assertions++
assert.doesNotMatch(selectCard, /matchMedia/); assertions++
console.log(`Editor-only portrait policy: ${assertions} geometry/preference/threshold assertions passed.`)
