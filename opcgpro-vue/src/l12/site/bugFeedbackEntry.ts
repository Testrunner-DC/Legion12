import { nextTick } from 'vue'

type FocusTarget = HTMLElement | null | undefined

const dialogFocusableSelector = [
  'a[href]',
  'button:not(:disabled)',
  'input:not(:disabled)',
  'select:not(:disabled)',
  'textarea:not(:disabled)',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

function dialogFocusTargets(dialog: HTMLElement | null) {
  return Array.from(dialog?.querySelectorAll<HTMLElement>(dialogFocusableSelector) ?? [])
    .filter(element => element.tabIndex >= 0 && element.getClientRects().length > 0
      && getComputedStyle(element).display !== 'none'
      && getComputedStyle(element).visibility !== 'hidden'
      && !element.closest('[hidden],[inert]'))
}

export function focusDialog(dialog: HTMLElement | null) {
  const target = dialogFocusTargets(dialog)[0] ?? dialog
  target?.focus()
  return target ?? null
}

export function handleDialogKeydown(event: KeyboardEvent, dialog: HTMLElement | null, close: () => void) {
  if (event.key === 'Escape') {
    event.preventDefault()
    event.stopPropagation()
    close()
    return
  }
  if (event.key !== 'Tab' || !dialog) return
  const actions = dialogFocusTargets(dialog)
  if (!actions.length) {
    event.preventDefault()
    event.stopPropagation()
    dialog.focus()
    return
  }
  const first = actions[0]!, last = actions[actions.length - 1]!
  const active = document.activeElement
  if (event.shiftKey && (active === first || !dialog.contains(active))) {
    event.preventDefault()
    event.stopPropagation()
    last.focus()
  } else if (!event.shiftKey && (active === last || !dialog.contains(active))) {
    event.preventDefault()
    event.stopPropagation()
    first.focus()
  }
}

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
