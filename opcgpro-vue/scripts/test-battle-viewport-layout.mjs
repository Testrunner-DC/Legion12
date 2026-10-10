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
const { resolveBattleViewportLayout } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`)

const desktop = resolveBattleViewportLayout({
  viewportWidth: 1920,
  viewportHeight: 1080,
  stageWidth: 2048,
  stageHeight: 1264,
  gmPanelOpen: false,
  mobile: false,
})
assert.equal(desktop.mobile, false)
assert.equal(desktop.compact, false)
assert.equal(desktop.scale, Math.min(1, 1920 / 2048, (1080 - 124) / 1264))

const mobile = resolveBattleViewportLayout({
  viewportWidth: 844,
  viewportHeight: 390,
  stageWidth: 2048,
  stageHeight: 1264,
  gmPanelOpen: false,
  mobile: true,
})
assert.equal(mobile.mobile, true)
assert.equal(mobile.compact, true)
assert.equal(mobile.scale, 1, 'the logical mobile canvas must not inherit desktop stage scaling')

const shortDesktop = resolveBattleViewportLayout({
  viewportWidth: 1280,
  viewportHeight: 480,
  stageWidth: 2048,
  stageHeight: 1264,
  gmPanelOpen: false,
  mobile: false,
})
assert.equal(shortDesktop.compact, true)
assert.equal(shortDesktop.mobile, false, 'compact desktop geometry must not opt into the mobile battle tree')
assert(shortDesktop.scale < 1)

const gmWide = resolveBattleViewportLayout({
  viewportWidth: 1920,
  viewportHeight: 1400,
  stageWidth: 2304,
  stageHeight: 1296,
  gmPanelOpen: true,
  mobile: false,
})
assert.equal(gmWide.availableWidth, 1920 - 344)
assert.equal(gmWide.availableHeight, 1400 - 124)

const gmCompact = resolveBattleViewportLayout({
  viewportWidth: 800,
  viewportHeight: 590,
  stageWidth: 2304,
  stageHeight: 1296,
  gmPanelOpen: true,
  mobile: false,
})
assert.equal(gmCompact.availableWidth, 800, 'compact canvases must not reserve the desktop GM dock twice')

for (const [width, height] of [[667, 375], [740, 360], [844, 390], [915, 412], [1024, 768]]) {
  const result = resolveBattleViewportLayout({ viewportWidth: width, viewportHeight: height, stageWidth: 2048, stageHeight: 1264, gmPanelOpen: false, mobile: true })
  assert.equal(result.scale, 1, `${width}x${height} changed the logical mobile scale`)
  assert(result.availableWidth > 0 && result.availableHeight > 0)
}

console.log('Battle viewport layout kernel: desktop/mobile isolation, compact boundary, GM reserve and 5 mobile ratios passed.')
