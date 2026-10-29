import assert from 'node:assert/strict'
import { readFileSync, writeFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { parse, compileStyle } from '@vue/compiler-sfc'
const file = new URL('../src/l12/L12DeckEditor.vue', import.meta.url)
const source = readFileSync(file, 'utf8'), sha = value => createHash('sha256').update(value).digest('hex')
const { descriptor, errors } = parse(source, { filename: file.pathname })
assert.deepEqual(errors, [])
const style = descriptor.styles[0].content
const compiled = compileStyle({ source: style, filename: file.pathname, id: 'legacy-import-touch', scoped: true })
assert.deepEqual(compiled.errors, [], 'The actual scoped SFC stylesheet must compile')
// Inspect the unscoped production AST so selector matching is not a regex over
// unrelated mobile rules; cascade order determines the final local declaration.
const ast = compileStyle({ source: style, filename: file.pathname, id: 'legacy-import-touch', scoped: false }).rawResult.root
function declarations(selector) {
  const values = {}
  ast.walkRules(rule => { if (rule.selector.split(',').map(value => value.trim()).includes(selector)) rule.walkDecls(item => { values[item.prop] = item.value }) })
  return values
}
for (const role of ['summary', 'button', 'select']) {
  const rules = declarations(`.legacy-deck-import ${role}`)
  assert.equal(rules['min-height'], '38px', role + ' preserves the existing mobile hit minimum')
  assert.equal(rules['max-width'], '100%', role + ' stays within its own panel')
  assert.equal(rules['box-sizing'], 'border-box')
}
const buttons = declarations('.legacy-deck-import button'), open = declarations('.legacy-deck-import[open]')
assert.equal(buttons['white-space'], 'normal'); assert.equal(buttons['overflow-wrap'], 'anywhere')
assert.equal(buttons.margin, '4px 0', 'Horizontal margins cannot add overflow beyond max-width')
assert.match(open['max-height'], /(?:d?vh)/)
assert.equal(open['overflow-y'], 'auto'); assert.equal(open['overscroll-behavior'], 'contain')
const report = { passed: true, sourceSha256: sha(source), compiledActualScopedStyle: true,
  touchMinimum: 38, viewportBound: open['max-height'], browserGeometry: 'Main managed-browser acceptance remains separate' }
if (process.env.L12_LEGACY_STYLE_BASELINE) {
  const baseline = JSON.parse(readFileSync(process.env.L12_LEGACY_STYLE_BASELINE, 'utf8'))
  const sections = { script: source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1],
    template: source.match(/<template>([\s\S]*?)<\/template>\s*<style scoped>/)?.[1] }
  for (const [name, content] of Object.entries(sections)) assert.equal(sha(content), baseline.sectionSha256[name], name + ' must remain byte-for-byte unchanged')
  const marker = /\/\* Legacy import accessibility boundary\. \*\/\r?\n[\s\S]*?\/\* End legacy import accessibility boundary\. \*\/\r?\n/g
  const blocks = style.match(marker)
  assert.equal(blocks?.length, 1)
  assert.equal(sha(style.replace(marker, '')), baseline.sectionSha256.style, 'Removing only the added local CSS block exactly restores the prior stylesheet')
  Object.assign(report, { baselineSourceSha256: baseline.sourceSha256, scriptUnchanged: true, templateUnchanged: true,
    otherStyleUnchanged: true, unchangedSectionSha256: baseline.sectionSha256 })
}
if (process.env.L12_LEGACY_STYLE_RECEIPT) writeFileSync(process.env.L12_LEGACY_STYLE_RECEIPT, JSON.stringify(report, null, 2), { flag: 'wx' })
console.log('Legacy import actual scoped style guard passed: 38px controls, contained wrapping, viewport scroll bound; no runtime geometry claim.')
