import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'

const names = ['SiteContentKind', 'SiteMediaKind', 'SiteMediaPolicy', 'SiteMedia', 'SiteMediaSearch',
  'SiteMediaEmbed', 'SiteCategory', 'ArticleStatus', 'Article', 'ArticleRevision', 'ArticleDraft',
  'ModianImportState', 'ModianImportPreviewItem', 'ModianImportPreview', 'ModianImportItemResult',
  'ModianImportBatchResult', 'SiteHomePayload']
const localNames = names.filter(name => !['SiteMediaEmbed', 'ArticleStatus', 'ModianImportState',
  'ModianImportPreviewItem', 'ModianImportItemResult'].includes(name))
const moduleSource = fs.readFileSync(new URL('../src/l12/platformSiteContentContracts.ts', import.meta.url), 'utf8')
const platform = fs.readFileSync(new URL('../src/l12/platform.ts', import.meta.url), 'utf8')
const parse = source => ts.createSourceFile('contracts.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
function validate(source, entry) {
  const tree = parse(source), root = parse(entry)
  assert.equal(tree.parseDiagnostics.length, 0)
  assert(tree.statements.every(node => ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node)))
  assert(tree.statements.every(node => node.modifiers?.some(token => token.kind === ts.SyntaxKind.ExportKeyword)))
  assert.deepEqual(tree.statements.map(node => node.name.text).sort(), [...names].sort())
  assert(!root.statements.some(node => (ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node)) && names.includes(node.name.text)))
  const imports = root.statements.filter(node => ts.isImportDeclaration(node) && node.moduleSpecifier.text === './platformSiteContentContracts')
  assert.equal(imports.length, 1)
  assert.equal(imports[0].importClause.isTypeOnly, true)
  assert(imports[0].importClause.namedBindings.elements.every(node => !node.propertyName))
  assert.deepEqual(imports[0].importClause.namedBindings.elements.map(node => node.name.text).sort(), [...localNames].sort())
  const exports = root.statements.filter(node => ts.isExportDeclaration(node) && node.moduleSpecifier?.text === './platformSiteContentContracts')
  assert.equal(exports.length, 1)
  assert.equal(exports[0].isTypeOnly, true)
  assert(exports[0].exportClause.elements.every(node => !node.propertyName))
  assert.deepEqual(exports[0].exportClause.elements.map(node => node.name.text).sort(), [...names].sort())
  const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext,
    target: ts.ScriptTarget.ES2022, removeComments: true } }).outputText.trim()
  assert.equal(output, 'export {};', 'the domain contract must have no runtime behavior or dependencies')
}
validate(moduleSource, platform)
assert.throws(() => validate(moduleSource + '\nexport const runtime = 1', platform))
assert.throws(() => validate(moduleSource, platform.replace('export type { SiteContentKind', 'export { SiteContentKind')))
assert.throws(() => validate(moduleSource, platform.replace('import type { SiteContentKind', 'import { SiteContentKind')))
assert.throws(() => validate(moduleSource.replace('export interface SiteHomePayload', 'interface SiteHomePayload'), platform))
assert.throws(() => validate(moduleSource, platform.replace('export type { SiteContentKind', 'export type { SiteMedia as SiteContentKind')))
assert.throws(() => validate(moduleSource, platform.replace('import type { SiteContentKind', 'import type { SiteMedia as SiteContentKind')))
const uiGuard = fs.readFileSync(new URL('./check-ui-contracts.mjs', import.meta.url), 'utf8')
assert(uiGuard.includes("const siteContentContracts = read('../src/l12/platformSiteContentContracts.ts')"))
for (const fact of ['desktopAltText: string; mobileAltText: string; thumbnailAltText: string; independentVariants: boolean', 'flexibleDimensions: boolean']) {
  assert(moduleSource.includes(fact), 'the actual media contract must retain its original fields')
  assert(uiGuard.includes(`siteContentContracts.includes('${fact}')`), 'the UI guard must read the real declaration owner')
  assert(!uiGuard.includes(`platform.includes('${fact}')`), 'the old API entry is no longer the declaration owner')
}
console.log('Site content contracts: 17 original type exports, 12 local type imports, no runtime, six negative guards and two real media guard mappings passed')
