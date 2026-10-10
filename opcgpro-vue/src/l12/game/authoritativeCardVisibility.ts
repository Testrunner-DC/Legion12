type VisibilityLeaseState = {
  originalVisibility: string
  count: number
}

const visibilityLeases = new WeakMap<HTMLElement, VisibilityLeaseState>()

export function authoritativeCardVisibilityLeaseCount(target: HTMLElement) {
  return visibilityLeases.get(target)?.count ?? 0
}

/**
 * Hides one authoritative card node while one or more presentation ghosts own
 * its pixels. The first lease captures the inline value and the final release
 * restores it, so overlapping layers cannot restore a stale `hidden` value.
 */
export function acquireAuthoritativeCardVisibility(target: HTMLElement) {
  let state = visibilityLeases.get(target)
  if (!state) {
    state = { originalVisibility: target.style.visibility, count: 0 }
    visibilityLeases.set(target, state)
    target.style.visibility = 'hidden'
  }
  state.count += 1
  let released = false
  return () => {
    if (released) return
    released = true
    const current = visibilityLeases.get(target)
    if (current !== state) return
    current.count -= 1
    if (current.count > 0) return
    visibilityLeases.delete(target)
    target.style.visibility = current.originalVisibility
  }
}
