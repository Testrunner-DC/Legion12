import assert from 'node:assert/strict'
import fs from 'node:fs'
import crypto from 'node:crypto'
import ts from 'typescript'
import { parse as parseSfc } from '@vue/compiler-sfc'

const names = ['AlternateArt', 'AlternateArtSearchPage', 'AlternateArtGrantNotification',
  'AlternateArtProduct', 'AlternateArtProductMergeResult', 'AlternateArtRarity',
  'GalleryPresentationCatalog', 'AlternateArtRankedParticipantDispatchPreview',
  'AlternateArtGrant', 'AlternateArtGrantSearchPage', 'AlternateArtAwardRule']
const moduleName = './platformAlternateArtContracts'
const contractsUrl = new URL('../src/l12/platformAlternateArtContracts.ts', import.meta.url)
const source = fs.readFileSync(contractsUrl, 'utf8')
const entry = fs.readFileSync(new URL('../src/l12/platform.ts', import.meta.url), 'utf8')
const parse = text => ts.createSourceFile('contracts.ts', text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const hash = text => crypto.createHash('sha256').update(text).digest('hex')
// The original eleven declarations are a closed, type-only compatibility boundary.
// Intentional schema evolution must update this fingerprint with its own regression.
const originalDeclarationsSha256 = '9023ed8519ad5a1ad3e2af72f6b48ada88f00ba2a084d8de460d9a2a872f0f2b'
const optionalInformationDeclarationsSha256 = '5792ab48db74963cdbedf56105bef3f5d870cd698aa0424a7737053dc096edc9'
function validate(contracts, platform) {
  const tree = parse(contracts), root = parse(platform)
  assert.equal(tree.parseDiagnostics.length, 0)
  assert.equal(root.parseDiagnostics.length, 0)
  assert(tree.statements.every(node => ts.isInterfaceDeclaration(node)
    && node.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.ExportKeyword)))
  assert.deepEqual(tree.statements.map(node => node.name.text), names)
  assert.equal(hash(tree.statements.map(node => node.getText(tree)).join('\n')), optionalInformationDeclarationsSha256,
    'pin the intentional optional display-information extension')
  const restored = contracts.replace(/\n  cardInformation\?: \{[^\n]*\} \| null\n\}/, ' }')
  assert.notEqual(restored, contracts, 'the new object is optional and nullable')
  const original = parse(restored)
  assert.equal(hash(original.statements.map(node => node.getText(original)).join('\n')), originalDeclarationsSha256,
    'removing only the display-information extension restores every original field byte-for-byte')
  const visit = node => {
    if (ts.isTypeReferenceNode(node)) {
      assert(ts.isIdentifier(node.typeName) && names.includes(node.typeName.text),
        'the type module must remain closed without a runtime or foreign dependency')
    }
    ts.forEachChild(node, visit)
  }
  tree.statements.forEach(visit)
  assert(!root.statements.some(node => (ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node))
    && names.includes(node.name.text)), 'declarations have exactly one owner')
  for (const [kind, predicate] of [['import', ts.isImportDeclaration], ['export', ts.isExportDeclaration]]) {
    const declarations = root.statements.filter(node => predicate(node)
      && node.moduleSpecifier?.text === moduleName)
    assert.equal(declarations.length, 1)
    const node = declarations[0]
    const clause = kind === 'import' ? node.importClause : node
    assert.equal(clause.isTypeOnly, true)
    const bindings = kind === 'import' ? clause.namedBindings : node.exportClause
    assert(bindings && bindings.elements.every(element => !element.propertyName))
    assert.deepEqual(bindings.elements.map(element => element.name.text), names)
  }
  const output = ts.transpileModule(contracts, { compilerOptions: {
    module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, removeComments: true,
  } }).outputText.trim()
  assert.equal(output, 'export {};', 'the new type module cannot add runtime behavior')
}
validate(source, entry)
const negatives = [
  () => validate(source.replace('AlternateArtProduct[] | null', 'AlternateArtProduct[]'), entry),
  () => validate(source.replace(" | 'ranked-participants'", ''), entry),
  () => validate(source.replace('export interface AlternateArtSearchPage', 'interface AlternateArtSearchPage'), entry),
  () => validate(source.replace('cardInformation?:', 'cardInformation:'), entry),
  () => validate(source.replace('cost?: number | null', 'cost: number'), entry),
  () => validate(source + '\nexport const runtime = 1', entry),
  () => validate(source, entry.replace('import type { AlternateArt,', 'import { AlternateArt,')),
  () => validate(source, entry.replace('export type { AlternateArt,', 'export { AlternateArt,')),
  () => validate(source, entry.replace('export type { AlternateArt,', 'export type { AlternateArt as RenamedArt,')),
  () => validate(source, entry.replace('import type { AlternateArt,', 'import type { AlternateArtProduct as AlternateArt,')),
]
for (const negative of negatives) assert.throws(negative)
const consumers = ['CardArchive.vue', 'L12DeckEditor.vue', 'game/GameBoard.vue',
  'site/AdminAlternateArtsPanel.vue', 'site/DeckLibraryPage.vue', 'site/ProfilePage.vue', 'site/SiteShell.vue']
for (const file of consumers) {
  const sfc = parseSfc(fs.readFileSync(new URL('../src/l12/' + file, import.meta.url), 'utf8'))
  assert.equal(sfc.errors.length, 0)
  const tree = parse(sfc.descriptor.scriptSetup.content)
  const imports = tree.statements.filter(node => ts.isImportDeclaration(node)
    && ['@/l12/platform', './platform', '../platform'].includes(node.moduleSpecifier.text))
  assert(imports.some(node => node.importClause?.namedBindings?.elements?.some(element =>
    names.includes((element.propertyName ?? element.name).text)
    && (node.importClause.isTypeOnly || element.isTypeOnly))), file + ' keeps its existing type import')
  assert(!tree.statements.some(node => ts.isImportDeclaration(node)
    && node.moduleSpecifier.text.includes('platformAlternateArtContracts')),
  file + ' does not need a new direct import')
}
assert(fs.readFileSync(new URL('./check-ui-contracts.mjs', import.meta.url), 'utf8')
  .includes("await import('./test-platform-alternate-art-contracts.mjs')"),
'the compatibility guard remains in the existing standard UI chain')
console.log(`Alternate art contracts: ${names.length} unchanged interfaces, ${consumers.length} original consumers, ${negatives.length} negative guards, closed type-only imports/exports and no runtime passed`)
