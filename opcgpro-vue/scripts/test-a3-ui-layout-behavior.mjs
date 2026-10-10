import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import ts from 'typescript'

const root = resolve(import.meta.dirname, '..')
const source = await readFile(resolve(root, 'src/l12/game/battleViewportLayout.ts'), 'utf8')
const pureSource = source
  .replace(/^import .*$/gm, '')
  .replace(/type UseBattleViewportLayoutOptions[\s\S]*$/m, '')
const compiled = ts.transpileModule(pureSource, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 },
}).outputText
const { resolveBattleViewportLayout } = await import(
  'data:text/javascript;base64,' + Buffer.from(compiled).toString('base64'))

const cases = [
  { name: 'narrow desktop', viewportWidth: 1280, viewportHeight: 720, stageWidth: 2048, stageHeight: 1264, gmPanelOpen: false, mobile: false },
  { name: 'wide desktop', viewportWidth: 3440, viewportHeight: 1440, stageWidth: 2048, stageHeight: 1264, gmPanelOpen: false, mobile: false },
  { name: 'GM dock', viewportWidth: 1920, viewportHeight: 1400, stageWidth: 2304, stageHeight: 1296, gmPanelOpen: true, mobile: false },
  { name: 'compact GM', viewportWidth: 800, viewportHeight: 590, stageWidth: 2304, stageHeight: 1296, gmPanelOpen: true, mobile: false },
  { name: 'portrait mobile', viewportWidth: 390, viewportHeight: 844, stageWidth: 2048, stageHeight: 1264, gmPanelOpen: false, mobile: true },
  { name: 'landscape mobile', viewportWidth: 844, viewportHeight: 390, stageWidth: 2048, stageHeight: 1264, gmPanelOpen: false, mobile: true },
]

for (const input of cases) {
  const result = resolveBattleViewportLayout(input)
  assert.equal(result.availableHeight, Math.max(1, input.viewportHeight - 124), input.name)
  assert.equal(result.mobile, input.mobile, input.name)
  if (input.name === 'GM dock') assert.equal(result.availableWidth, input.viewportWidth - 344, input.name)
  else assert.equal(result.availableWidth, input.viewportWidth, input.name)
  if (input.mobile) {
    assert.equal(result.scale, 1, input.name + ' must retain its logical canvas')
  } else {
    const expected = Math.min(result.availableWidth / input.stageWidth, result.availableHeight / input.stageHeight)
    assert.equal(result.scale, expected, input.name + ' must fit both axes without a scale floor')
    assert(result.scale * input.stageWidth <= result.availableWidth + 0.001, input.name + ' intrudes into the right dock')
    assert(result.scale * input.stageHeight <= result.availableHeight + 0.001, input.name + ' leaves the viewport vertically')
  }
}

assert(resolveBattleViewportLayout(cases[1]).scale > 1, 'wide desktop must not be capped at scale 1')
assert.equal(resolveBattleViewportLayout(cases[3]).compact, true, 'compact desktop must not reserve the GM dock')
console.log('A3 layout behavior: narrow/wide desktop, GM/right dock, portrait/landscape mobile passed.')
