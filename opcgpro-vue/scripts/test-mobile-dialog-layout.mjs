import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import ts from 'typescript'

const root = resolve(import.meta.dirname, '..')
const source = await readFile(resolve(root, 'src/l12/mobileDialogLayout.ts'), 'utf8')
const compiled = ts.transpileModule(source, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 },
}).outputText
const { MOBILE_DIALOG_ASPECT_RATIO, MOBILE_DIALOG_COVERAGE, resolveMobileDialogFrame } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`)

assert.equal(MOBILE_DIALOG_COVERAGE, 0.75)
assert.equal(MOBILE_DIALOG_ASPECT_RATIO, 16 / 9)

for (const [width, height] of [[568, 320], [667, 375], [844, 390], [932, 430], [1024, 600], [1024, 768]]) {
  const frame = resolveMobileDialogFrame(width, height)
  assert(frame.width <= width * 0.75 + 0.01, `${width}x${height} dialog exceeds 75% width`)
  assert(frame.height <= height * 0.75 + 0.01, `${width}x${height} dialog exceeds 75% height`)
  assert(Math.abs(frame.width / frame.height - 16 / 9) < 0.001, `${width}x${height} dialog aspect ratio drifted`)
  assert(frame.width > 0 && frame.height > 0)
}

const ultraWide = resolveMobileDialogFrame(932, 430)
assert.equal(ultraWide.height, 430 * 0.75)
const tablet = resolveMobileDialogFrame(1024, 768)
assert.equal(tablet.width, 1024 * 0.75)

console.log('Mobile dialog layout: 75% safe-canvas cap and stable 16:9 ratio passed across 6 viewport profiles.')
