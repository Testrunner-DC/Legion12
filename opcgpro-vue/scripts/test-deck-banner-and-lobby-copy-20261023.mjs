import assert from 'node:assert/strict'
import fs from 'node:fs'
import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse as parseTemplate } from '@vue/compiler-dom'

// Static-only source contract. No browser, server, build, network or entitlement writes.
const load = relative => {
  const filename = new URL(relative, import.meta.url)
  const source = fs.readFileSync(filename, 'utf8')
  const parsed = parseSfc(source, { filename: filename.pathname })
  assert.deepEqual(parsed.errors, [])
  assert.ok(parsed.descriptor.template)
  return { source, ast: parseTemplate(parsed.descriptor.template.content) }
}
const walk = node => [node, ...(node.children ?? []).flatMap(walk)]
const attr = (node, name) => node.props?.find(prop => prop.type === 6 && prop.name === name)?.value?.content
const hasClass = (node, name) => (attr(node, 'class') ?? '').split(/\s+/).includes(name)
const directive = (node, name, argument) => node.props?.find(prop => prop.type === 7 && prop.name === name
  && prop.arg?.content === argument)?.exp?.content
const editor = load('../src/l12/L12DeckEditor.vue')
const lobby = load('../src/l12/site/BattleHubPage.vue')
const results = []
const check = (name, verify) => {
  try { verify(); results.push({ name, status: 'passed' }) }
  catch (error) { results.push({ name, status: 'failed', error: error.message }); process.exitCode = 1 }
}

check('sidebar card banners omit legality while pool and validation stay', () => {
  const sidebar = walk(editor.ast).find(node => node.tag === 'aside' && hasClass(node, 'deck-list'))
  assert.ok(sidebar, 'Use the actual saved deck sidebar')
  const main = walk(sidebar).find(node => node.tag === 'section' && attr(node, 'data-deck-section') === 'main')
  assert.ok(main)
  assert.ok(walk(main).some(node => node.tag === 'CardImage' && hasClass(node, 'deck-entry-banner')))
  assert.doesNotMatch(main.loc.source, /entryIssue\(|seasonEntryIssue\(/, 'Sidebar banners must not render or style legality')
  const poolIssue = walk(editor.ast).find(node => node.tag === 'span' && hasClass(node, 'entry-issue'))
  assert.ok(poolIssue)
  assert.match(poolIssue.loc.source, /entryIssue\(/, 'Keep construction issue in pool')
  assert.match(poolIssue.loc.source, /seasonEntryIssue\(/, 'Keep season issue in pool')
  assert.match(editor.source, /const validation = computed\(/, 'Keep authoritative editor validation')
  assert.match(editor.source, /const seasonValidation = computed\(/, 'Keep season validation')
  assert.match(main.loc.source, /:disabled="entry\.count >= allowedCopies\(entry\.card\)/, 'Keep add-copy controls and caps')
  assert.match(main.loc.source, /alternate-art-banner/, 'Keep per-appearance card banners')
})

check('all lobby deck-cover buttons remove duplicate lower hints and retain actions', () => {
  const triggers = walk(lobby.ast).filter(node => node.tag === 'button' && hasClass(node, 'deck-selection-trigger'))
  assert.equal(triggers.length, 3, 'Room, matchmaking and friendly deck selectors')
  for (const button of triggers) {
    assert.equal(directive(button, 'on', 'click'), 'openDeckSelector')
    assert.equal(directive(button, 'bind', 'aria-label'), 'deckSelectionAriaLabel')
    const covers = walk(button).filter(node => node.tag === 'DeckCover')
    assert.equal(covers.length, 2, 'Selected and empty cover branches remain')
    assert.ok(covers.some(node => attr(node, 'context') === '点击封面更换牌库'))
    assert.ok(covers.some(node => attr(node, 'context') === '点击封面选择牌库'))
    assert.doesNotMatch(button.loc.source, /点击当前牌库封面即可更换/)
    if (directive(button, 'bind', 'disabled') !== 'me?.ready')
      assert.ok(!walk(button).some(node => hasClass(node, 'deck-selection-hint')))
  }
  const room = triggers.find(button => directive(button, 'bind', 'disabled') === 'me?.ready')
  assert.ok(room, 'Do not enable switching when ready')
  const reason = walk(room).filter(node => hasClass(node, 'deck-selection-hint'))
  assert.equal(reason.length, 1, 'Keep only the unique ready restriction, visible on mobile too')
  assert.ok(reason[0].props.some(prop => prop.type === 7 && prop.name === 'if' && prop.exp?.content === 'me?.ready'))
  assert.match(reason[0].loc.source, /已准备，取消准备后可更换/)
})

console.log(JSON.stringify({ scope: 'static-source-only', results }, null, 2))
