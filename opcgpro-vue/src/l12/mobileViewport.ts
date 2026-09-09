import { onBeforeUnmount, onMounted, watch, type Ref } from 'vue'

let layout = { width: 0, height: 0, left: 0, top: 0, rotated: false, active: false }

// Coordinates used by body Teleports must be in the same space as their fixed host.
export function viewportRect(element: Element): DOMRect {
  const rect = element.getBoundingClientRect()
  if (!layout.active) return rect
  return layout.rotated
    ? new DOMRect(rect.top - layout.top, layout.height - (rect.right - layout.left), rect.height, rect.width)
    : new DOMRect(rect.left - layout.left, rect.top - layout.top, rect.width, rect.height)
}

export function visibleViewport() {
  if (layout.active) return { width: layout.width, height: layout.height }
  const viewport = window.visualViewport
  return { width: viewport?.width ?? window.innerWidth, height: viewport?.height ?? window.innerHeight }
}

export function useLandscapeViewport(enabled: Ref<boolean>) {
  let probe: HTMLDivElement | null = null
  let locked = false
  let generation = 0
  let attemptedGeneration = -1
  const editable = () => document.activeElement instanceof HTMLElement
    && document.activeElement.matches('input:not([type=checkbox]):not([type=radio]),textarea,[contenteditable=true]')
  function update() {
    if (!enabled.value) {
      layout.active = false
      delete document.documentElement.dataset.l12Viewport
      delete document.documentElement.dataset.l12Compact
      document.body.removeAttribute('data-l12-rotated')
      window.dispatchEvent(new Event('l12-viewport-change'))
      return
    }
    const visual = window.visualViewport
    const safe = probe ? getComputedStyle(probe) : null
    const inset = (value?: string) => Math.max(0, parseFloat(value ?? '') || 0)
    const left = (visual?.offsetLeft ?? 0) + inset(safe?.paddingLeft)
    const top = (visual?.offsetTop ?? 0) + inset(safe?.paddingTop)
    const width = Math.max(1, (visual?.width ?? innerWidth) - inset(safe?.paddingLeft) - inset(safe?.paddingRight))
    const height = Math.max(1, (visual?.height ?? innerHeight) - inset(safe?.paddingTop) - inset(safe?.paddingBottom))
    // CSS fallback rotates the complete fixed host, including body Teleports. Never
    // promise hardware orientation lock: mobile browsers commonly reject lock().
    const rotated = Math.min(innerWidth, innerHeight) <= 820 && innerHeight > innerWidth && !editable()
    const nextLayout = { active: true, rotated, left, top, width: rotated ? height : width, height: rotated ? width : height }
    const changed = JSON.stringify(layout) !== JSON.stringify(nextLayout)
    layout = nextLayout
    const root = document.documentElement
    root.dataset.l12Viewport = rotated ? 'landscape' : 'normal'
    root.dataset.l12Compact = String(layout.width < 820 || layout.height < 600)
    root.style.setProperty('--l12-viewport-width', `${layout.width}px`)
    root.style.setProperty('--l12-viewport-height', `${layout.height}px`)
    root.style.setProperty('--l12-viewport-left', `${left + (rotated ? width : 0)}px`)
    root.style.setProperty('--l12-viewport-top', `${top}px`)
    if (changed) window.dispatchEvent(new Event('l12-viewport-change'))
  }
  async function requestOrientation() {
    if (!enabled.value || locked || attemptedGeneration === generation || editable() || Math.min(innerWidth, innerHeight) > 820) return
    attemptedGeneration = generation
    const orientation = screen.orientation as ScreenOrientation & { lock?: (value: string) => Promise<void> }
    const current = generation
    try {
      await orientation?.lock?.('landscape')
      if (current !== generation || !enabled.value) { orientation?.unlock?.(); return }
      locked = Boolean(orientation?.lock)
    } catch { /* The CSS landscape canvas remains usable without fullscreen/lock. */ }
    update()
  }
  function focusChanged() { requestAnimationFrame(update) }
  watch(enabled, () => {
    generation++
    if (!enabled.value && locked) { screen.orientation?.unlock?.(); locked = false }
    update()
    void requestOrientation()
  })
  onMounted(() => {
    probe = document.createElement('div')
    probe.style.cssText = 'position:fixed;visibility:hidden;pointer-events:none;padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)'
    document.body.append(probe)
    window.addEventListener('resize', update)
    window.visualViewport?.addEventListener('resize', update)
    window.visualViewport?.addEventListener('scroll', update)
    document.addEventListener('focusin', focusChanged)
    document.addEventListener('focusout', focusChanged)
    document.addEventListener('pointerup', requestOrientation)
    update()
    void requestOrientation()
  })
  onBeforeUnmount(() => {
    generation++
    window.removeEventListener('resize', update)
    window.visualViewport?.removeEventListener('resize', update)
    window.visualViewport?.removeEventListener('scroll', update)
    document.removeEventListener('focusin', focusChanged)
    document.removeEventListener('focusout', focusChanged)
    document.removeEventListener('pointerup', requestOrientation)
    probe?.remove()
    if (locked) screen.orientation?.unlock?.()
    layout.active = false
    delete document.documentElement.dataset.l12Viewport
    delete document.documentElement.dataset.l12Compact
  })
}
