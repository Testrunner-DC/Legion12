import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import ts from 'typescript'
import { buildBattlePreviewDocument } from './lib/battlePreviewDocument.mjs'

const root = resolve(import.meta.dirname, '..')
const source = await readFile(resolve(root, 'src/l12/game/battleViewportLayout.ts'), 'utf8')
const productionIndexHtml = await readFile(resolve(root, 'index.html'), 'utf8')
const previewSource = await readFile(resolve(root, 'scripts/preview-l12-battle-layout.mjs'), 'utf8')
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

const productionViewportTags = [...productionIndexHtml.matchAll(/<meta\b[^>]*name=["']viewport["'][^>]*>/gi)]
assert.equal(productionViewportTags.length, 1, 'production index must remain the single viewport-meta authority')
const previewDocument = buildBattlePreviewDocument(productionIndexHtml)
assert.match(previewDocument, /^<!doctype html>/i)
assert.equal([...previewDocument.matchAll(/<meta\b[^>]*name=["']viewport["'][^>]*>/gi)].length, 1)
assert.ok(previewDocument.includes(productionViewportTags[0][0]), 'preview must preserve the production viewport meta verbatim')
for (const fragment of [
  '<div id="l12-landscape-teleports"></div>', '<div id="app"></div>',
  'src="/@vite/client"', 'src="/__l12_battle_preview__.js"',
]) assert.ok(previewDocument.includes(fragment), `preview document omitted ${fragment}`)

assert.throws(
  () => buildBattlePreviewDocument(productionIndexHtml.replace(productionViewportTags[0][0], '')),
  /exactly one viewport meta; found 0/,
)
assert.throws(
  () => buildBattlePreviewDocument(productionIndexHtml.replace('</head>', `${productionViewportTags[0][0]}\n</head>`)),
  /exactly one viewport meta; found 2/,
)
assert.throws(
  () => buildBattlePreviewDocument(productionIndexHtml.replace('width=device-width', 'width=980')),
  /width=device-width/,
)
assert.throws(
  () => buildBattlePreviewDocument(productionIndexHtml.replace('initial-scale=1', 'initial-scale=0.75')),
  /initial-scale=1/,
)
assert.throws(() => buildBattlePreviewDocument('<meta name="viewport" name="viewport" content="width=device-width,initial-scale=1">'), /repeats the name attribute/)
assert.throws(() => buildBattlePreviewDocument('<meta name="viewport" content="width=device-width,initial-scale=1" content="width=980">'), /repeats the content attribute/)
assert.throws(() => buildBattlePreviewDocument('<meta name="viewport" content="width=device-width,initial-scale=1,initial-scale=1">'), /repeats or omits a directive name/)
assert.throws(() => buildBattlePreviewDocument('<meta name="viewport" content="width=device-width">'), /initial-scale=1/)
const reorderedViewport = "<META CONTENT='width=device-width, initial-scale=1.0' NAME='VIEWPORT'>"
assert.ok(buildBattlePreviewDocument(reorderedViewport).includes(reorderedViewport), 'attribute order/case and equivalent numeric scale must remain valid')
assert.match(previewSource, /buildBattlePreviewDocument\(productionIndexHtml\)/,
  'the public preview entry must construct its document through the validated helper')
assert.match(previewSource, /response\.end\(previewDocument\)/,
  'the public preview response must serve the validated document')
assert.ok(!previewSource.includes('width=device-width'),
  'the public preview entry must not duplicate the production viewport policy')

console.log('Battle viewport layout kernel: desktop/mobile isolation, compact boundary, GM reserve, 5 mobile ratios and the production viewport document contract passed.')
