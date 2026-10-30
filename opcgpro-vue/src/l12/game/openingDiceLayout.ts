/** Presentation geometry in the stage's logical pixels, including rotated canvases. */
export function openingDiceLayout(width: number, height: number, viewportWidth = width) {
  const w = Math.max(1, width), h = Math.max(1, height)
  const clamp = (minimum: number, value: number, maximum: number) => Math.max(minimum, Math.min(value, maximum))
  const short = w <= 900 && h <= 500
  const master = Math.min(short ? clamp(112, w * .17, 164) : clamp(220, w * .24, 340), h * .5)
  const dice = Math.min(short ? clamp(64, w * .13, 92) : clamp(88, w * .12, 156), h * .28)
  const gap = clamp(8, w * .018, 24), padding = clamp(4, w * .007, 10)
  const edge = Math.min(18, w * .015)
  const scale = Math.min(1, Math.max(1, w - 2 * edge) / (2 * master + 2 * dice + 4 * padding + 3 * gap))
  const masterWidth = master * scale, diceFont = dice * scale, safeGap = gap * scale
  const dicePadding = padding * scale
  const diceRowWidth = 2 * (diceFont + 2 * dicePadding) + safeGap
  const offset = (diceRowWidth + masterWidth) / 2 + safeGap
  return { compact: short, masterWidth, diceFont, dicePadding, diceRowWidth, gap: safeGap, offset,
    shift: offset - clamp(150, viewportWidth * .22, 360) }
}
