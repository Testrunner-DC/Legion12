import { onBeforeUnmount, onMounted, ref, watch, type Ref } from 'vue'
import { audioPreferences, type L12AudioPreferences } from './audioPreferences'

// Editor-only policy. Battle/spectator coordinate contracts are not changed.
export function resolveDeckEditorPortrait(width: number, height: number, preference: L12AudioPreferences['mobileLayout'] = 'auto') {
  return preference !== 'off' && width <= 820 && height >= width * 1.05
}

export const deckEditorPortrait = ref(false)

export function useDeckEditorViewport(enabled: Ref<boolean>) {
  let settledWidth = 0
  let settledHeight = 0
  let composing = false
  let keyboardSession = false
  const editable = () => document.activeElement instanceof HTMLElement
    && document.activeElement.matches('input:not([type=checkbox]):not([type=radio]),textarea,[contenteditable=true]')

  function update() {
    if (!enabled.value) {
      deckEditorPortrait.value = false
      settledWidth = settledHeight = 0
      keyboardSession = false
      delete document.documentElement.dataset.l12EditorPortrait
      document.documentElement.style.removeProperty('--l12-editor-visible-height')
      document.documentElement.style.removeProperty('--l12-editor-visible-width')
      document.documentElement.style.removeProperty('--l12-editor-visible-top')
      document.documentElement.style.removeProperty('--l12-editor-visible-left')
      return
    }
    const visual = window.visualViewport
    const width = visual?.width ?? window.innerWidth
    const height = visual?.height ?? window.innerHeight
    // Keep the pre-keyboard mode through focusout: Safari may dismiss the
    // keyboard after blur. A timer must not decide when the layout can change.
    const sameWidth = Math.abs(width - settledWidth) <= 2
    if (editable() || composing) keyboardSession = true
    const keyboardReduced = keyboardSession && sameWidth && settledHeight > 0 && height < settledHeight * .78
    const hold = settledWidth > 0 && (editable() || composing || keyboardReduced)
    if (!hold) {
      settledWidth = width
      settledHeight = height
      keyboardSession = false
      deckEditorPortrait.value = resolveDeckEditorPortrait(width, height, audioPreferences.mobileLayout)
    }
    document.documentElement.dataset.l12EditorPortrait = String(deckEditorPortrait.value)
    document.documentElement.style.setProperty('--l12-editor-visible-height', `${height}px`)
    document.documentElement.style.setProperty('--l12-editor-visible-width', `${width}px`)
    document.documentElement.style.setProperty('--l12-editor-visible-top', `${visual?.offsetTop ?? 0}px`)
    document.documentElement.style.setProperty('--l12-editor-visible-left', `${visual?.offsetLeft ?? 0}px`)
  }
  const compositionStart = () => { composing = true; update() }
  const compositionEnd = () => { composing = false; update() }
  watch([enabled, () => audioPreferences.mobileLayout], update, { immediate: true })
  onMounted(() => {
    window.addEventListener('resize', update)
    window.visualViewport?.addEventListener('resize', update)
    window.visualViewport?.addEventListener('scroll', update)
    document.addEventListener('focusin', update)
    document.addEventListener('focusout', update)
    document.addEventListener('compositionstart', compositionStart)
    document.addEventListener('compositionend', compositionEnd)
    update()
  })
  onBeforeUnmount(() => {
    window.removeEventListener('resize', update)
    window.visualViewport?.removeEventListener('resize', update)
    window.visualViewport?.removeEventListener('scroll', update)
    document.removeEventListener('focusin', update)
    document.removeEventListener('focusout', update)
    document.removeEventListener('compositionstart', compositionStart)
    document.removeEventListener('compositionend', compositionEnd)
    deckEditorPortrait.value = false
    delete document.documentElement.dataset.l12EditorPortrait
    document.documentElement.style.removeProperty('--l12-editor-visible-height')
    document.documentElement.style.removeProperty('--l12-editor-visible-width')
    document.documentElement.style.removeProperty('--l12-editor-visible-top')
    document.documentElement.style.removeProperty('--l12-editor-visible-left')
  })
}
