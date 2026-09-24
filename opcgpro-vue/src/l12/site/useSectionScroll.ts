import { nextTick, onBeforeUnmount, onMounted, watch } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRoute } from 'vue-router'

// SiteShell scrolls this element, not window. Store only positions, never personal data.
const positions = new Map<string, number>()
try {
  const saved = JSON.parse(sessionStorage.getItem('l12-section-scroll') || '[]')
  if (Array.isArray(saved)) for (const [key, value] of saved.slice(-100)) {
    if (typeof key === 'string' && typeof value === 'number' && Number.isFinite(value) && value >= 0) positions.set(key, value)
  }
} catch { /* Storage is optional; in-memory navigation still works. */ }
export function useSectionScroll(identity: () => string, contentReady: () => boolean = () => true) {
  const route = useRoute()
  let key = `${identity()}:${route.fullPath}`
  let observer: ResizeObserver | undefined
  let revision = 0
  let userScrolled = false
  const surface = () => document.querySelector<HTMLElement>('.site-content')
  const stop = () => { observer?.disconnect(); observer = undefined }
  const cancelRestore = () => { userScrolled = true; revision++; stop() }
  const save = () => {
    const element = surface()
    if (element) positions.set(key, element.scrollTop)
    if (positions.size > 100) positions.delete(positions.keys().next().value!)
    try { sessionStorage.setItem('l12-section-scroll', JSON.stringify([...positions])) } catch { /* Optional storage. */ }
    stop()
  }
  async function restore() {
    stop()
    const version = ++revision
    key = `${identity()}:${route.fullPath}`
    const top = positions.get(key) ?? 0
    await nextTick()
    if (version !== revision) return
    const element = surface()
    if (!element) return
    const apply = () => {
      if (version !== revision) return
      element.scrollTop = top
      if (contentReady() && element.scrollHeight - element.clientHeight >= top) stop()
    }
    observer = new ResizeObserver(apply)
    for (const child of element.children) observer.observe(child)
    apply()
  }
  onBeforeRouteUpdate(save)
  onBeforeRouteLeave(save)
  watch(() => [route.fullPath, identity()], () => { userScrolled = false; void restore() }, { flush: 'post' })
  watch(contentReady, () => { if (!userScrolled) void restore() }, { flush: 'post' })
  onMounted(() => {
    void restore()
    window.addEventListener('pagehide', save)
    surface()?.addEventListener('wheel', cancelRestore, { passive: true })
    surface()?.addEventListener('touchstart', cancelRestore, { passive: true })
  })
  onBeforeUnmount(() => {
    save(); revision++
    window.removeEventListener('pagehide', save)
    surface()?.removeEventListener('wheel', cancelRestore)
    surface()?.removeEventListener('touchstart', cancelRestore)
  })
}
