import { nextTick, onBeforeUnmount } from 'vue'
import { l12AnimationDuration } from './audioPreferences'

/** Reorders retained children without animating layout properties. */
export function useFlip(selector = '[data-flip-id]') {
  let rects = new Map<string, DOMRect>()
  let running: Animation[] = []
  const keyOf = (element: Element) => (element as HTMLElement).dataset.flipId ?? null

  function capture(container: HTMLElement | null) {
    rects = new Map()
    if (!container) return
    for (const element of container.querySelectorAll(selector)) {
      const key = keyOf(element)
      if (key) rects.set(key, element.getBoundingClientRect())
    }
  }

  async function play(container: HTMLElement | null, durationMs = 260) {
    if (!container || rects.size === 0) return
    await nextTick()
    const duration = l12AnimationDuration(durationMs, 0)
    if (duration <= 0) { rects.clear(); return }
    running.forEach(animation => animation.cancel())
    running = []
    for (const element of container.querySelectorAll(selector)) {
      const key = keyOf(element)
      const previous = key ? rects.get(key) : undefined
      if (!previous) continue
      const current = element.getBoundingClientRect()
      const dx = previous.left - current.left
      const dy = previous.top - current.top
      if (Math.abs(dx) < 1 && Math.abs(dy) < 1) continue
      running.push((element as HTMLElement).animate(
        [{ transform: `translate(${dx}px,${dy}px)` }, { transform: 'translate(0,0)' }],
        { duration, easing: 'cubic-bezier(.22,1,.36,1)' },
      ))
    }
    rects.clear()
  }

  onBeforeUnmount(() => running.forEach(animation => animation.cancel()))
  return { capture, play }
}
