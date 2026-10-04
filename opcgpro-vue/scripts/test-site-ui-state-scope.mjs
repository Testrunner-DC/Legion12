import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { parse } from '@vue/compiler-dom'
import { parse as parseSfc } from '@vue/compiler-sfc'
import { usesSiteUiStates } from '../src/l12/site/siteUiStateScope.ts'

const cases = [
  [{}, true], [{ section: 'battle' }, true], [{ requiresAdmin: true }, true],
  [{ requiresAccount: true }, true], [{ immersive: true }, false],
  [{ landscapeCanvas: true }, false],
  [{ immersive: true, landscapeCanvas: true, editorAdaptiveCanvas: true }, false],
  [{ immersive: false, landscapeCanvas: false }, true],
]
for (const [meta, expected] of cases) assert.equal(usesSiteUiStates(meta), expected)
console.log(`Site UI state route policy: ${cases.length}/${cases.length}`)

// Inspect semantic dialog roots, not exact serialized class attributes or CSS text.
const dialogs = ['AdminAccountsPage.vue', 'AdminAlternateArtsPanel.vue',
  'RankedMasterTitleRulesModal.vue', 'ArticleContentRenderer.vue', 'BattleHubPage.vue',
  'DeckSnapshotViewer.vue', 'AdminRiskActionDialog.vue', 'MobileFilterSheet.vue']
for (const file of dialogs) {
  const source = fs.readFileSync(path.join(import.meta.dirname, '../src/l12/site', file), 'utf8')
  const ast = parse(parseSfc(source).descriptor.template.content, {comments:false})
  let found = 0
  function visit(node, scoped = false, teleported = false) {
    const classes = node.props?.find(p=>p.type===6 && p.name==='class')?.value?.content?.split(/\s+/) || []
    const participates = scoped || classes.includes('ui-state-scope') || classes.includes('ui-dialog')
    const detached = teleported || node.tag==='Teleport'
    if (detached && (node.tag==='dialog' || node.props?.some(p=>p.type===6 && p.name==='role' && p.value?.content==='dialog'))) {
      found++
    }
    if (detached && ['button','input','select','textarea','UiButton'].includes(node.tag)) {
      assert(participates, `${file}: detached ${node.tag} must opt into site states`)
    }
    for (const child of node.children || []) visit(child, participates, detached)
  }
  visit(ast)
  assert(found>0, `${file}: fixture audit must find a real dialog`)
}
console.log(`Site Teleport dialog state coverage: ${dialogs.length}/${dialogs.length}`)
