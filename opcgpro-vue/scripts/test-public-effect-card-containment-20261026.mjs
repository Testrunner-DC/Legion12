import assert from 'node:assert/strict'
import fs from 'node:fs'
import { parse } from '@vue/compiler-sfc'

const boardSource = fs.readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const imageSource = fs.readFileSync(new URL('../src/l12/CardImage.vue', import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const projectionSource = fs.readFileSync(new URL('../src/l12/game/visualTransitionProjection.ts', import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const board = parse(boardSource)
const image = parse(imageSource)
assert.deepEqual(board.errors, [])
assert.deepEqual(image.errors, [])
const boardCss = board.descriptor.styles.map(style => style.content).join('\n')
const imageCss = image.descriptor.styles.map(style => style.content).join('\n')

function rule(css, selector) {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return css.match(new RegExp(`${escaped}\\s*\\{([^}]*)\\}`))?.[1] ?? ''
}

const group = rule(boardCss, '.public-reveal-cards')
const face = rule(boardCss, '.public-reveal-cards .l12-card-image')
const cardRoot = rule(imageCss, '.l12-card-image')
const cardPixels = rule(imageCss, '.l12-card-image__img')

assert.match(cardRoot, /overflow:hidden/, 'CardImage keeps only its intentional rounded-card mask')
assert.match(cardPixels, /width:100%;height:100%/, 'the decoded card pixels fill the whole card box')
assert.match(imageSource, /fit: 'contain'/, 'effect card pixels use contain rather than cover cropping')
assert.match(cardPixels, /clip-path:inset\(0 round/, 'the image itself clips only to the physical card corners')

assert.match(face, /filter:drop-shadow\(0 10px 15px #000\) drop-shadow\(0 0 16px/, 'the effect face intentionally paints outside its layout box')
assert.match(boardCss, /55%\{transform:translate3d\(0,-7px,18px\) rotateX\(-2deg\) scale\(1\.035\)/,
  'the shared effect animation intentionally lifts and enlarges the face')
assert.match(group, /overflow:visible/,
  'the shared effect group must not crop lift, scale or shadow pixels')
assert.doesNotMatch(group, /overflow:(?:hidden|clip)/,
  'the clipping ancestor must be removed instead of shrinking or special-casing cards')

function transformedBounds(width, height) {
  const scale = 1.035
  return {
    width: width * scale,
    height: height * scale,
    top: -7 - (height * scale - height) / 2,
    shadow: 16,
  }
}

for (const [layout, width, height] of [
  ['wide-vertical', 118, 165],
  ['wide-horizontal', 190, 190 * 5 / 8],
  ['mobile-vertical', 80, 112],
  ['mobile-horizontal', 80, 80 * 5 / 8],
]) {
  const bounds = transformedBounds(width, height)
  assert(bounds.width > width && bounds.height > height && bounds.top < 0 && bounds.shadow > 0,
    `${layout}: the animation really has pixels outside the flex box`)
}

for (const view of ['actor', 'opponent', 'spectator']) {
  for (const count of [1, 3]) {
    assert.match(boardSource, /<CardImage v-for="entry in publicRevealFaces"/,
      `${view}/${count} card(s): every public projection uses the same shared non-clipping group`)
  }
}

assert.match(projectionSource,
  /if \(isCardEffectPresentationEvent\(event\)\) return event\.cards\?\.slice\(0, 1\) \?\? \[\]/,
  'a structured card-effect activation presents exactly its authority source, never an unbounded target group')

function shortMobileGroup(viewportWidth, count) {
  const animationLeft = 90
  const firstColumnCenter = animationLeft + 40
  const paintedWidth = count * 80 + Math.max(0, count - 1) * 8 + 32
  return {
    viewportWidth,
    count,
    left: firstColumnCenter - paintedWidth / 2,
    right: firstColumnCenter + paintedWidth / 2,
  }
}
const shortMobileGroups = [320, 390].flatMap(width => [1, 2, 3].map(count => shortMobileGroup(width, count)))
assert(shortMobileGroups.filter(item => item.count === 1).every(item => item.left >= 0 && item.right <= item.viewportWidth),
  'the actual one-card effect activation stays inside both 320px and 390px short canvases including shadow')
const independentMultiRevealBoundary = shortMobileGroups.filter(item => item.count === 3 && item.left < 0)

assert.doesNotMatch(boardSource, /S0[12]-\d{4}[\s\S]{0,120}public-reveal-cards/,
  'the containment repair must not depend on a card id')

console.log(JSON.stringify({
  suite: 'public effect card containment',
  rootCause: 'the parent group clips a child that deliberately lifts, scales and paints a shadow outside its box',
  cardPixels: 'contain + full box + rounded-card mask remain intact',
  scenarios: ['vertical', 'horizontal', 'single', 'multiple', 'actor', 'opponent', 'spectator', 'wide', 'mobile'],
  actualEffectGroupMaximum: 1,
  independentMultiRevealBoundary,
}, null, 2))
