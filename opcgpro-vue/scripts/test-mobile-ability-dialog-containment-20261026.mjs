import assert from 'node:assert/strict'
import fs from 'node:fs'

const tolerance = 1

export function hasSharedAbilityCardSizing(source) {
  const scope = 'html[data-l12-mobile=true][data-l12-viewport] :is(.master-dialog,.faction-effect-dialog)'
  const dialog = source.split(`${scope} {`)[1]?.split('}')[0] ?? ''
  const image = source.split(`${scope} > .l12-card-image {`)[1]?.split('}')[0] ?? ''
  return dialog.includes('--l12-ability-card-width: clamp(96px, calc(var(--l12-mobile-dialog-width) * .24), 156px)')
    && dialog.includes('grid-template-columns: var(--l12-ability-card-width) minmax(0, 1fr) !important')
    && image.includes('width: var(--l12-ability-card-width) !important')
    && image.includes('height: auto !important')
}
const viewportCss = fs.readFileSync(new URL('../src/l12/mobileViewport.css', import.meta.url), 'utf8')
assert(hasSharedAbilityCardSizing(viewportCss), 'grid and image must share the explicit logical-frame length, not different cqw containers')
for (const [before, after] of [
  ['--l12-ability-card-width: clamp(96px, calc(var(--l12-mobile-dialog-width) * .24), 156px)', '--l12-ability-card-width: 24cqw'],
  ['grid-template-columns: var(--l12-ability-card-width)', 'grid-template-columns: clamp(96px, 24cqw, 156px)'],
  ['width: var(--l12-ability-card-width) !important', 'width: clamp(96px, 24cqw, 156px) !important'],
  ['html[data-l12-mobile=true][data-l12-viewport] :is(.master-dialog,.faction-effect-dialog) {', ':is(.master-dialog,.faction-effect-dialog) {'],
]) {
  assert(viewportCss.includes(before), 'negative sizing target must exist')
  assert.equal(hasSharedAbilityCardSizing(viewportCss.replace(before, after)), false)
}

function inside(outer, inner) {
  return inner.left >= outer.left - tolerance
    && inner.top >= outer.top - tolerance
    && inner.right <= outer.right + tolerance
    && inner.bottom <= outer.bottom + tolerance
}

function intersects(a, b) {
  return Math.min(a.right, b.right) - Math.max(a.left, b.left) > tolerance
    && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > tolerance
}

function clippedRect(rect, clip) {
  if (!rect || !clip) return rect
  const result = { left: Math.max(rect.left, clip.left), top: Math.max(rect.top, clip.top),
    right: Math.min(rect.right, clip.right), bottom: Math.min(rect.bottom, clip.bottom) }
  return result.right > result.left && result.bottom > result.top ? result : null
}

// The older battle capture checked only these two conditions. It could pass
// while a card image covered the explanatory text inside the dialog.
function legacyViewportGuard(sample) {
  return inside(sample.viewport, sample.dialog)
    && sample.controls.every(control => inside(sample.viewport, control))
}

export function assessMobileAbilityDialog(sample) {
  const problems = []
  const { viewport, dialog, image, content, title, text, controls, scroll } = sample
  // A scrolled paragraph's layout box can extend under the fixed header even
  // though overflow:auto has clipped those pixels. Check its visible portion.
  const visibleTitle = clippedRect(title, scroll?.region)
  const visibleText = clippedRect(text, scroll?.region)
  if (!inside(viewport, dialog)) problems.push('dialog-outside-viewport')
  if (!inside(dialog, image)) problems.push('image-outside-dialog')
  if (!inside(dialog, content)) problems.push('content-outside-dialog')
  if (intersects(image, content)) problems.push('image-over-content')
  if (visibleTitle && intersects(image, visibleTitle)) problems.push('image-over-title')
  if (visibleText && intersects(image, visibleText)) problems.push('image-over-text')
  controls.forEach((control, index) => {
    if (!inside(viewport, control) || !inside(dialog, control)) problems.push(`control-${index}-outside-dialog`)
    if (visibleText && intersects(control, visibleText)) problems.push(`control-${index}-over-text`)
  })
  if (!scroll || scroll.scrollTop + scroll.clientHeight < scroll.scrollHeight - tolerance
    || !inside(scroll.region, scroll.finalLine)) problems.push('last-line-unreachable')
  return [...new Set(problems)]
}

const viewport = { left: 0, top: 0, right: 844, bottom: 390 }
const dialog = { left: 120, top: 38, right: 724, bottom: 352 }
const controls = [
  { left: 630, top: 46, right: 674, bottom: 90 },
  { left: 675, top: 46, right: 719, bottom: 90 },
]
const overlap = {
  viewport, dialog, controls,
  image: { left: 136, top: 110, right: 290, bottom: 324 },
  content: { left: 265, top: 110, right: 710, bottom: 324 },
  title: { left: 265, top: 118, right: 580, bottom: 145 },
  text: { left: 255, top: 170, right: 705, bottom: 240 },
  scroll: {
    region: { left: 265, top: 110, right: 710, bottom: 324 },
    finalLine: { left: 278, top: 290, right: 590, bottom: 310 },
    scrollTop: 0, scrollHeight: 214, clientHeight: 214,
  },
}
assert.equal(legacyViewportGuard(overlap), true,
  'the old viewport-only guard must demonstrate this coverage gap')
assert.deepEqual(assessMobileAbilityDialog(overlap),
  ['image-over-content', 'image-over-title', 'image-over-text'],
  'the new geometry validator must reject image, title and text intrusion')

const contained = {
  ...overlap,
  image: { left: 136, top: 110, right: 254, bottom: 275 },
  content: { left: 272, top: 108, right: 710, bottom: 324 },
  title: { left: 272, top: 118, right: 580, bottom: 145 },
  text: { left: 272, top: 170, right: 705, bottom: 240 },
  scroll: { ...overlap.scroll, region: { left: 272, top: 108, right: 710, bottom: 324 } },
}
assert.deepEqual(assessMobileAbilityDialog(contained), [],
  'a separated image and fully reachable explanation must pass')
const scrolledBody = { ...contained, text: { ...contained.text, top: 20 } }
assert.deepEqual(assessMobileAbilityDialog(scrolledBody), [],
  'text clipped outside the actual scroll region is not painted over header buttons')
assert.deepEqual(assessMobileAbilityDialog({ ...scrolledBody,
  content: { ...contained.content, top: 38 },
  scroll: { ...contained.scroll, region: { ...contained.scroll.region, top: 38 } },
}), ['control-0-over-text', 'control-1-over-text'],
  'text genuinely visible behind fixed controls must still fail')
assert.deepEqual(assessMobileAbilityDialog({ ...contained,
  scroll: { ...contained.scroll, finalLine: { left: 278, top: 320, right: 590, bottom: 345 } },
}),
  ['last-line-unreachable'],
  'a clipped final sentence must fail even if every element fits the viewport')

// A real browser capture must supply computed rectangles for each shared
// dialog consumer. Synthetic cases above validate the oracle, not the product.
const geometryPath = typeof process === 'undefined' ? undefined : process.argv[2]
if (geometryPath) {
  const samples = JSON.parse(fs.readFileSync(geometryPath, 'utf8'))
  assert.ok(Array.isArray(samples) && samples.length > 0,
    'real geometry input must contain at least one browser-observed dialog')
  const has = (field, value) => samples.some(sample => sample[field] === value)
  for (const kind of ['faction', 'card-ability', 'master']) {
    assert.ok(has('kind', kind), `real geometry must cover the shared ${kind} consumer`)
  }
  for (const cardOrientation of ['portrait', 'landscape']) {
    assert.ok(has('cardOrientation', cardOrientation), `real geometry must cover ${cardOrientation} card art`)
  }
  for (const copyLength of ['short', 'long']) {
    assert.ok(has('copyLength', copyLength), `real geometry must cover ${copyLength} copy`)
  }
  for (const layout of ['native-landscape', 'rotated-portrait']) {
    assert.ok(has('layout', layout), `real geometry must cover ${layout}`)
  }
  for (const fontScale of [1, 1.12, 1.24]) {
    assert.ok(has('fontScale', fontScale), `real geometry must cover font scale ${fontScale}`)
  }
  assert.ok(samples.some(sample => sample.viewport.right - sample.viewport.left <= 568),
    'real geometry must include the narrow 568px logical canvas')
  assert.ok(samples.some(sample => sample.viewport.right - sample.viewport.left >= 844),
    'real geometry must include the regular 844px logical canvas')
  const failures = samples.map(sample => ({ name: sample.name, problems: assessMobileAbilityDialog(sample) }))
    .filter(result => result.problems.length > 0)
  assert.deepEqual(failures, [], `mobile ability dialog containment: ${JSON.stringify(failures)}`)
  console.log(`Mobile ability dialog containment: ${samples.length} browser samples passed`)
} else {
  console.log('Mobile ability dialog shared logical sizing and four negative guards passed; geometry oracle self-tests are not product browser acceptance')
}
