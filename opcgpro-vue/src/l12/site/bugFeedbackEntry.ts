import { nextTick } from 'vue'

type FocusTarget = HTMLElement | null | undefined

export function rememberSettingsOpener(event?: Event) {
  if (event?.currentTarget instanceof HTMLElement) return event.currentTarget
  return document.activeElement instanceof HTMLElement ? document.activeElement : null
}

export function visibleFocusTarget(target: FocusTarget) {
  if (!target?.isConnected || target.getClientRects().length === 0
    || target.matches(':disabled') || target.closest('[inert]')) return null
  const style = getComputedStyle(target), rect = target.getBoundingClientRect()
  // A transformed, closed navigation drawer still has client rects.
  // Restore to the supplied visible fallback instead of its off-screen button.
  return style.display !== 'none' && style.visibility !== 'hidden'
    && rect.right > 0 && rect.bottom > 0 && rect.left < innerWidth && rect.top < innerHeight ? target : null
}

export async function closeSettingsAndRestore(close: () => void, returnFocus: FocusTarget, fallbackFocus?: FocusTarget) {
  close()
  await nextTick()
  ;(visibleFocusTarget(returnFocus) ?? visibleFocusTarget(fallbackFocus))?.focus()
}

export async function openBugFeedbackFromSettings(close: () => void, returnFocus: FocusTarget, fallbackFocus?: FocusTarget) {
  close()
  await nextTick()
  window.dispatchEvent(new CustomEvent('l12-open-bug-feedback', {
    detail: { returnFocus, fallbackFocus },
  }))
}
