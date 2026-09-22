export type MobileDialogFrame = {
  width: number
  height: number
}

export const MOBILE_DIALOG_COVERAGE = 0.75
export const MOBILE_DIALOG_ASPECT_RATIO = 16 / 9

/**
 * Fit one stable 16:9 dialog canvas inside 75% of the safe logical viewport.
 * The frame may become smaller on ultra-wide phones or 4:3 tablets, but it is
 * never stretched to follow the device ratio. Dialog content scrolls inside it.
 */
export function resolveMobileDialogFrame(viewportWidth: number, viewportHeight: number): MobileDialogFrame {
  const safeWidth = Math.max(1, viewportWidth)
  const safeHeight = Math.max(1, viewportHeight)
  const maxWidth = safeWidth * MOBILE_DIALOG_COVERAGE
  const maxHeight = safeHeight * MOBILE_DIALOG_COVERAGE
  const width = Math.min(maxWidth, maxHeight * MOBILE_DIALOG_ASPECT_RATIO)
  return { width, height: width / MOBILE_DIALOG_ASPECT_RATIO }
}
