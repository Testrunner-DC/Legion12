export type ScrollPosition = { left: number; width: number; contentWidth: number; stepWidth?: number }
export type ScrollRect = { left: number; top: number; right: number; bottom: number }

export function horizontalScrollState(position: ScrollPosition) {
  const width = Math.max(0, Number.isFinite(position.width) ? position.width : 0)
  const maximum = Math.max(0, (Number.isFinite(position.contentWidth) ? position.contentWidth : 0) - width)
  const left = Math.min(maximum, Math.max(0, Number.isFinite(position.left) ? position.left : 0))
  const overflowing = width > 0 && maximum > 2
  return { left, maximum, overflowing,
    previous: overflowing && left > 2, next: overflowing && maximum - left > 2 }
}

export function horizontalScrollDestination(position: ScrollPosition, direction: -1 | 1) {
  const state = horizontalScrollState(position)
  if (!state.overflowing) return state.left
  const page = Number.isFinite(position.stepWidth) ? position.stepWidth! : position.width
  return Math.max(0, Math.min(state.maximum, state.left + direction * Math.max(1, page * .8)))
}

export function clipScrollRect(rect: ScrollRect, clip: ScrollRect, x = true, y = true): ScrollRect {
  return {
    left: x ? Math.max(rect.left, clip.left) : rect.left,
    right: x ? Math.min(rect.right, clip.right) : rect.right,
    top: y ? Math.max(rect.top, clip.top) : rect.top,
    bottom: y ? Math.min(rect.bottom, clip.bottom) : rect.bottom,
  }
}

// Both rects are already in the same existing logical viewport coordinates.
// Absolute offsets use the host's padding box, not its outer border; scrolling
// and a board-stage scale must therefore be accounted for exactly once.
export function scrollControlFrame(visible: ScrollRect, host: ScrollRect,
  metrics: { width: number; height: number; borderLeft: number; borderTop: number; scrollLeft: number; scrollTop: number }) {
  const scaleX = (host.right - host.left) / metrics.width
  const scaleY = (host.bottom - host.top) / metrics.height
  if (!(scaleX > 0 && scaleY > 0 && Number.isFinite(scaleX) && Number.isFinite(scaleY))
    || visible.right - visible.left < 80 || visible.bottom - visible.top < 38) return null
  return {
    left: (visible.left - host.left) / scaleX - metrics.borderLeft + metrics.scrollLeft,
    top: ((visible.top + visible.bottom) / 2 - host.top) / scaleY - metrics.borderTop + metrics.scrollTop,
    width: (visible.right - visible.left) / scaleX,
    buttonWidth: 38 / scaleX,
    buttonHeight: 38 / scaleY,
  }
}
