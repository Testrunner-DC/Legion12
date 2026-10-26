type VisibilityLeaseState = {
  originalVisibility: string
  count: number
}

const visibilityLeases = new WeakMap<HTMLElement, VisibilityLeaseState>()

export function resolveAuthoritativeCardVisibilitySurface(target: HTMLElement) {
  if (typeof target.matches === 'function'
    && target.matches('[data-l12-authoritative-card-visibility-surface]')) return target
  const surface = typeof target.querySelector === 'function'
    ? target.querySelector<HTMLElement>('[data-l12-authoritative-card-visibility-surface]') : null
  if (surface) return surface
  // A persistent zone is board furniture, not card pixels. If its card face is
  // temporarily absent, keep the frame and metadata visible instead of taking
  // a visibility lease on the whole zone.
  if (typeof target.matches === 'function' && target.matches('[data-l12-persistent-card-zone]')) return null
  return target
}

export function authoritativeCardVisibilityLeaseCount(target: HTMLElement) {
  const surface = resolveAuthoritativeCardVisibilitySurface(target)
  return surface ? visibilityLeases.get(surface)?.count ?? 0 : 0
}

/**
 * Hides one authoritative card node while one or more presentation ghosts own
 * its pixels. The first lease captures the inline value and the final release
 * restores it, so overlapping layers cannot restore a stale `hidden` value.
 */
export function acquireAuthoritativeCardVisibility(target: HTMLElement) {
  const surface = resolveAuthoritativeCardVisibilitySurface(target)
  if (!surface) return () => {}
  let state = visibilityLeases.get(surface)
  if (!state) {
    state = { originalVisibility: surface.style.visibility, count: 0 }
    visibilityLeases.set(surface, state)
    surface.style.visibility = 'hidden'
  }
  state.count += 1
  let released = false
  return () => {
    if (released) return
    released = true
    const current = visibilityLeases.get(surface)
    if (current !== state) return
    current.count -= 1
    if (current.count > 0) return
    visibilityLeases.delete(surface)
    surface.style.visibility = current.originalVisibility
  }
}
