import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'
import { computed, ref } from 'vue'

const front = path.resolve(import.meta.dirname, '..')
const env = { ...process.env,
  L12_MOTION_OUTPUT: process.env.L12_HOT_DECK_MOTION_OUTPUT || 'D:/GPT/Legion12/artifacts/hot-deck-motion-20261021',
  L12_MOTION_GUARD: path.join(front, '../ops/windows/L12GeneratedStorage.ps1') }
execFileSync(env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-Command',
  '. $env:L12_MOTION_GUARD; $null=Assert-L12GeneratedPath -Path $env:L12_MOTION_OUTPUT -Purpose "hot deck motion regression"'], { env, stdio: 'pipe' })
const source = (process.argv.includes('--baseline')
  ? execFileSync('git', ['show', 'HEAD:opcgpro-vue/src/l12/site/DeckLibraryPage.vue'], { cwd: front, encoding: 'utf8' })
  : fs.readFileSync(path.join(front, 'src/l12/site/DeckLibraryPage.vue'), 'utf8')).replace(/\r\n/g, '\n')
const setup = parse(source).descriptor.scriptSetup.content
const appStyles = parse(fs.readFileSync(path.join(front, 'src/App.vue'), 'utf8')).descriptor.styles.map(style => style.content).join('\n')
assert.match(appStyles, /:root\[data-l12-animation="off"\] \*:not\(\[data-essential-motion\]\):not\(:where\(\.deck-page,\.deck-page \*,\.deck-cover,\.deck-cover \*,\.deck-cover__footer,\.deck-cover__footer \*\)\)/,
  'the real application animation-off guard must exclude deck UI, not only the isolated component fixture')
assert.match(appStyles, /animation-duration:\.001ms!important;animation-iteration-count:1!important;transition-duration:\.001ms!important;scroll-behavior:auto!important/,
  'nonessential battle motion still obeys the existing off preference')
const tree = ts.createSourceFile('DeckLibraryPage.ts', setup, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
assert.equal(tree.statements.filter(item => ts.isExpressionStatement(item) && ts.isCallExpression(item.expression)
  && item.expression.expression.getText(tree) === 'onMounted').length, 1,
  'motion listeners must share the existing page initialization lifecycle')
function actual(names, dependencies) {
  const selected = names.map(name => {
    const node = tree.statements.find(item => ts.isFunctionDeclaration(item) && item.name?.text === name
      || ts.isVariableStatement(item) && item.declarationList.declarations.some(value => value.name.getText(tree) === name))
    assert(node, 'missing actual setup declaration: ' + name)
    return node.getText(tree)
  }).join('\n')
  const js = ts.transpileModule(selected, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText
  return Function(...Object.keys(dependencies), js + '\nreturn {' + names.join(',') + '}')(...Object.values(dependencies))
}
const hotSummaries = ref(Array.from({ length: 16 }, (_, index) => ({ id: 'hot-' + index })))
const { hotDeckRows } = actual(['hotDeckRows'], { computed, hotSummaries })
assert.equal(hotDeckRows.value.length, 2, 'retain the original two independently moving rows')
assert.deepEqual(hotDeckRows.value.map(row => row.map(item => item.id)), [0, 1].map(parity => hotSummaries.value.filter((_, index) => index % 2 === parity).map(item => item.id)), 'retain the original odd/even rank grouping')
hotSummaries.value = []
assert.deepEqual(hotDeckRows.value, [])
hotSummaries.value = [{ id: 'only' }]
assert.deepEqual(hotDeckRows.value.map(row => row.map(item => item.id)), [['only'], ['only']], 'retain the existing single-summary fallback')

const { updateHotDeckDuration } = actual(['HOT_DECK_PIXELS_PER_SECOND', 'updateHotDeckDuration'], {})
const track = { dataset: { hotRowTrack: '1' }, style: { setProperty(key, value) { this[key] = value } } }
let width = 0
const loop = { parentElement: track, getBoundingClientRect: () => ({ width }) }
updateHotDeckDuration(loop)
assert.equal(track.dataset.motionReady, undefined, 'zero-width initial layout must not start an invalid animation')
for (const [row, speed] of [[1, 48], [2, 46]]) {
  track.dataset.hotRowTrack = String(row)
  for (width of [800, 5248, 3808]) {
    updateHotDeckDuration(loop)
    assert.equal(track.dataset.motionReady, 'true')
    assert.ok(Math.abs(width / Number.parseFloat(track.style['--hot-deck-duration']) - speed) < .001, 'resize must retain the existing per-row speed')
  }
}
assert.match(source, /watch\(\[hotDeckRows, tab\]/, 'recreated plaza DOM must rebind motion even if the summaries did not change')
assert.match(source, /visibilitychange/, 'foreground recovery must remeasure initially hidden layout')
assert.match(source, /window\.addEventListener\('resize'/, 'resize must also recover without ResizeObserver')
assert.match(source, /window\.addEventListener\('pointerup'/, 'touch released outside the viewport must clear its pause')
const hotDeckTouchPaused = ref([false, false]), hotDeckTouchPointers = new Map()
const touches = actual(['handleHotDeckPointerDown', 'releaseHotDeckTouches'], { hotDeckTouchPaused, hotDeckTouchPointers })
const touch = pointerId => ({ pointerType: 'touch', pointerId })
touches.handleHotDeckPointerDown(touch(1), 0); touches.handleHotDeckPointerDown(touch(2), 1)
touches.releaseHotDeckTouches(touch(1))
assert.deepEqual(hotDeckTouchPaused.value, [false, true], 'releasing row one must retain row two finger pause')
touches.handleHotDeckPointerDown(touch(3), 1); touches.releaseHotDeckTouches(touch(2))
assert.deepEqual(hotDeckTouchPaused.value, [false, true], 'another finger in the same row must retain its pause')
touches.releaseHotDeckTouches(touch(3)); touches.releaseHotDeckTouches(touch(999))
assert.deepEqual(hotDeckTouchPaused.value, [false, false])
assert.match(source, /scrollbar-width:none/)
assert.match(source, /::-webkit-scrollbar\{display:none\}/)
assert.doesNotMatch(source, /scrollbar-width:thin/)
assert.doesNotMatch(source, /prefers-reduced-motion/,
  'human ruling: deck scrolling and hover remain enabled independently of the system motion preference')
for (const name of ['DeckCover.vue', 'SavedDeckSelector.vue', 'LobbyPage.vue', 'L12DeckEditor.vue']) {
  const component = parse(fs.readFileSync(path.join(front, 'src/l12', name), 'utf8')).descriptor
  assert(component.styles.every(style => !style.content.includes('prefers-reduced-motion')),
    `shared deck-cover hover must not be suppressed in ${name}`)
}
const battleMotion = fs.readFileSync(path.join(front, 'src/l12/motion.css'), 'utf8')
assert.match(battleMotion, /@media \(prefers-reduced-motion: reduce\)/,
  'the bounded deck ruling must not remove the existing other-motion safety fallback')
assert.match(source, /:has\(:focus-visible\) \.hot-deck-track\{transform:none!important\}/, 'keyboard focus must reveal the first copy without moving a mouse-clicked clone')
console.log('hot-deck-motion: two-row grouping, 48/46px/s, recovery, keyboard/touch pause, hidden scrollbar and system-independent deck motion passed')
