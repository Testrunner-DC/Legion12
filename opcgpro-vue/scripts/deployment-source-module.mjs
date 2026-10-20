import fs from 'node:fs'
import ts from 'typescript'

// Compile the real immutable build-base resolver, not a second test mapping.
export function deploymentSourceModuleUrl(base = '/') {
  const filename = new URL('../src/l12/deploymentBase.ts', import.meta.url)
  const source = fs.readFileSync(filename, 'utf8')
    .replaceAll('import.meta.env.BASE_URL', JSON.stringify(base))
  const output = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  }).outputText
  return `data:text/javascript;base64,${Buffer.from(output).toString('base64')}`
}
