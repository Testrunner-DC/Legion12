import { onBeforeUnmount, onMounted, watch, type Ref } from 'vue'
import { audioPreferences, type L12AudioPreferences } from './audioPreferences'
import { resolveMobileDialogFrame } from './mobileDialogLayout'

type ViewportMode = {
  rotated: boolean
  mobile: boolean
  width: number
  height: number
}

let layout = {
  width: 0,
  height: 0,
  physicalWidth: 0,
  physicalHeight: 0,
  left: 0,
  top: 0,
  rotated: false,
  mobile: false,
  active: false,
}

function compactLandscape(width: number, height: number, relaxed: boolean) {
  const ratio = width / Math.max(1, height)
  const phone = width <= (relaxed ? 1180 : 1120)
    && height <= (relaxed ? 860 : 820)
    && ratio >= (relaxed ? 1.18 : 1.24)
  const tablet = width <= (relaxed ? 1400 : 1366)
    && height >= 700
    && height <= (relaxed ? 1120 : 1060)
    && ratio >= (relaxed ? 1.14 : 1.18)
    && ratio <= (relaxed ? 1.56 : 1.50)
  return phone || tablet
}

/** Resolve a logical canvas from the actual visual viewport only. */
export function resolveViewportMode(
  physicalWidth: number,
  physicalHeight: number,
  previous: Pick<ViewportMode, 'rotated' | 'mobile'> = { rotated: false, mobile: false },
  mobileLayout: L12AudioPreferences['mobileLayout'] = 'auto',
): ViewportMode {
  const portraitRatio = physicalHeight / Math.max(1, physicalWidth)
  const rotatedWidth = physicalHeight
  const rotatedHeight = physicalWidth
  // Rotation is geometry-only. Forcing the mobile layout must never loosen the
  // physical portrait thresholds or rotate an already-landscape viewport.
  const rotatedCandidate = compactLandscape(rotatedWidth, rotatedHeight, previous.rotated)
  const rotated = rotatedCandidate && portraitRatio >= (previous.rotated ? 1.05 : 1.12)
  const width = rotated ? rotatedWidth : physicalWidth
  const height = rotated ? rotatedHeight : physicalHeight
  const geometryMobile = compactLandscape(width, height, mobileLayout === 'auto' && previous.mobile)
  const mobile = mobileLayout === 'on' ? true : mobileLayout === 'off' ? false : geometryMobile
  return { rotated, mobile, width, height }
}

// Coordinates used by transformed canvas Teleports must be expressed in the
// same logical landscape coordinate space as the route canvas.
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

export function isMobileViewportExperience() {
  if (typeof window === 'undefined') return false
  if (layout.active) return layout.mobile
  const viewport = window.visualViewport
  const width = viewport?.width ?? window.innerWidth
  const height = viewport?.height ?? window.innerHeight
  return resolveViewportMode(width, height, undefined, audioPreferences.mobileLayout).mobile
}

// Compatibility entry used by the replay blocker. Its result is now based on
// geometry instead of device identity.
export function isMobileDeviceExperience() {
  return isMobileViewportExperience()
}

export function landscapeTeleportTarget() {
  return typeof document !== 'undefined' && document.getElementById('l12-landscape-teleports')
    ? '#l12-landscape-teleports'
    : 'body'
}

export function useLandscapeViewport(enabled: Ref<boolean>) {
  let probe: HTMLDivElement | null = null
  let previous: Pick<ViewportMode, 'rotated' | 'mobile'> = { rotated: false, mobile: false }
  const editable = () => document.activeElement instanceof HTMLElement
    && document.activeElement.matches('input:not([type=checkbox]):not([type=radio]),textarea,[contenteditable=true]')

  function clear() {
    layout.active = false
    const root = document.documentElement
    delete root.dataset.l12Viewport
    delete root.dataset.l12Compact
    delete root.dataset.l12Mobile
    delete root.dataset.l12Rotated
    root.style.removeProperty('--l12-viewport-width')
    root.style.removeProperty('--l12-viewport-height')
    root.style.removeProperty('--l12-physical-width')
    root.style.removeProperty('--l12-physical-height')
    root.style.removeProperty('--l12-viewport-left')
    root.style.removeProperty('--l12-viewport-top')
    root.style.removeProperty('--l12-mobile-dialog-width')
    root.style.removeProperty('--l12-mobile-dialog-height')
    document.body.removeAttribute('data-l12-rotated')
  }

  function update() {
    if (!enabled.value) {
      clear()
      window.dispatchEvent(new Event('l12-viewport-change'))
      return
    }
    const visual = window.visualViewport
    const safe = probe ? getComputedStyle(probe) : null
    const inset = (value?: string) => Math.max(0, parseFloat(value ?? '') || 0)
    const safeLeft = inset(safe?.paddingLeft)
    const safeRight = inset(safe?.paddingRight)
    const safeTop = inset(safe?.paddingTop)
    const safeBottom = inset(safe?.paddingBottom)
    const left = (visual?.offsetLeft ?? 0) + safeLeft
    const top = (visual?.offsetTop ?? 0) + safeTop
    const physicalWidth = Math.max(1, (visual?.width ?? innerWidth) - safeLeft - safeRight)
    const physicalHeight = Math.max(1, (visual?.height ?? innerHeight) - safeTop - safeBottom)
    const resolved = resolveViewportMode(physicalWidth, physicalHeight, previous, audioPreferences.mobileLayout)
    const mode = editable()
      ? {
          rotated: previous.rotated,
          mobile: previous.mobile,
          width: previous.rotated ? physicalHeight : physicalWidth,
          height: previous.rotated ? physicalWidth : physicalHeight,
        }
      : resolved
    previous = { rotated: mode.rotated, mobile: mode.mobile }
    const nextLayout = {
      active: true,
      rotated: mode.rotated,
      mobile: mode.mobile,
      left,
      top,
      width: mode.width,
      height: mode.height,
      physicalWidth,
      physicalHeight,
    }
    const changed = JSON.stringify(layout) !== JSON.stringify(nextLayout)
    layout = nextLayout
    const root = document.documentElement
    const dialogFrame = resolveMobileDialogFrame(mode.width, mode.height)
    root.dataset.l12Viewport = 'landscape'
    root.dataset.l12Compact = String(mode.mobile || mode.width < 820 || mode.height < 600)
    root.dataset.l12Mobile = String(mode.mobile)
    root.dataset.l12Rotated = String(mode.rotated)
    root.style.setProperty('--l12-viewport-width', `${mode.width}px`)
    root.style.setProperty('--l12-viewport-height', `${mode.height}px`)
    root.style.setProperty('--l12-physical-width', `${physicalWidth}px`)
    root.style.setProperty('--l12-physical-height', `${physicalHeight}px`)
    root.style.setProperty('--l12-viewport-left', `${left}px`)
    root.style.setProperty('--l12-viewport-top', `${top}px`)
    root.style.setProperty('--l12-mobile-dialog-width', `${dialogFrame.width}px`)
    root.style.setProperty('--l12-mobile-dialog-height', `${dialogFrame.height}px`)
    if (changed) window.dispatchEvent(new Event('l12-viewport-change'))
  }

  watch([enabled, () => audioPreferences.mobileLayout], update)
  onMounted(() => {
    probe = document.createElement('div')
    probe.style.cssText = 'position:fixed;visibility:hidden;pointer-events:none;padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)'
    document.body.append(probe)
    window.addEventListener('resize', update)
    window.visualViewport?.addEventListener('resize', update)
    window.visualViewport?.addEventListener('scroll', update)
    document.addEventListener('focusin', update)
    document.addEventListener('focusout', update)
    update()
  })
  onBeforeUnmount(() => {
    window.removeEventListener('resize', update)
    window.visualViewport?.removeEventListener('resize', update)
    window.visualViewport?.removeEventListener('scroll', update)
    document.removeEventListener('focusin', update)
    document.removeEventListener('focusout', update)
    probe?.remove()
    clear()
  })
}
