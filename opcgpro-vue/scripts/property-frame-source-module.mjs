import fs from 'node:fs'
import crypto from 'node:crypto'
import assert from 'node:assert/strict'
import ts from 'typescript'

// Compile the actual module and its one immutable JSON dependency for data-URI
// fixtures. This is a bundling adapter, not a replacement implementation.
export function propertyFrameSourceModuleUrl() {
  const source = fs.readFileSync(new URL('../src/l12/gameStatePropertyFrame.ts', import.meta.url), 'utf8')
  const names = JSON.parse(fs.readFileSync(new URL('../src/l12/gameStatePropertyNamesV1.json', import.meta.url), 'utf8'))
  assert.equal(crypto.createHash('sha256').update(JSON.stringify(names)).digest('hex').toUpperCase(),
    '9193B87AD3D50DD4B14D445A2B09FDCE62B2503746B2AA61D0B6F72E0FD9DC45')
  const anchor = "import propertyNames from './gameStatePropertyNamesV1.json'"
  assert.equal(source.split(anchor).length, 2)
  const compiled = ts.transpileModule(source.replace(anchor, `const propertyNames = ${JSON.stringify(names)}`),
    { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 }, reportDiagnostics: true })
  assert.equal((compiled.diagnostics ?? []).filter(value => value.category === ts.DiagnosticCategory.Error).length, 0)
  return 'data:text/javascript;base64,' + Buffer.from(compiled.outputText).toString('base64')
}
