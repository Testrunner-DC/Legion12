import type { ObjectDirective } from 'vue'
import { isMobileViewportExperience, viewportRect, visibleViewport } from './mobileViewport'
import { clipScrollRect, horizontalScrollDestination, horizontalScrollState, scrollControlFrame,
  type ScrollPosition } from './horizontalScrollGeometry'
import './horizontalScrollArrows.css'

export type HorizontalScrollArrowOptions = string | {
  label: string
  // Transform-based carousels explicitly adapt their existing single track;
  // they must never be mistaken for a native overflow:hidden scroll viewport.
  adapter?: { position: () => ScrollPosition; move: (left: number) => void; circular?: boolean; interact?: (active: boolean) => void }
}

type Controller = { update: (options: HorizontalScrollArrowOptions | undefined) => void; dispose: () => void }
const controllers = new WeakMap<HTMLElement, Controller>()
let sequence = 0
const clips = (value: string) => /^(auto|scroll|hidden|clip)$/.test(value)

function createController(target: HTMLElement, initial: HorizontalScrollArrowOptions | undefined): Controller {
  let options = initial
  let stopped = false
  let frame = 0
  let observing = new Set<Element>()
  let extraPadding = 0
  let basePadding: { left: number; right: number } | undefined
  let hovered = false
  let inViewport = true
  let refreshGutters = false
  const viewportKey = () => {
    const viewport = visibleViewport()
    return `${viewport.width}:${viewport.height}:${isMobileViewportExperience()}:${document.documentElement.dataset.l12Rotated ?? ''}`
  }
  let previousViewport = viewportKey()
  const originalId = target.id
  const generatedId = `l12-horizontal-scroll-${++sequence}`
  if (!originalId) target.id = generatedId
  const layer = document.createElement('span')
  layer.className = 'l12-horizontal-scroll-arrows'
  layer.dataset.l12ScrollArrows = ''
  layer.setAttribute('role', 'group')
  layer.hidden = true
  const previous = document.createElement('button')
  const next = document.createElement('button')
  for (const [button, direction] of [[previous, -1], [next, 1]] as const) {
    button.type = 'button'
    button.className = direction < 0 ? 'l12-scroll-previous' : 'l12-scroll-next'
    button.textContent = direction < 0 ? '‹' : '›'
    button.setAttribute('aria-controls', target.id)
    // The controls are outside the target. They may not enter its drag owner,
    // sorting gesture, selection handlers or implicit form submission.
    button.addEventListener('pointerdown', event => event.stopPropagation())
    const isolateActivationKey = (event: KeyboardEvent) => {
      if (event.key === 'Enter' || event.key === ' ') event.stopPropagation()
    }
    button.addEventListener('keydown', isolateActivationKey)
    button.addEventListener('keyup', isolateActivationKey)
    button.addEventListener('click', event => {
      event.preventDefault()
      event.stopPropagation()
      if (button.disabled) return
      interact()
      const adapter = typeof options === 'object' ? options.adapter : undefined
      const position = adapter?.position() ?? nativePosition()
      const maximum = horizontalScrollState(position).maximum
      const candidate = position.left + direction * position.width * .8
      const left = adapter?.circular && maximum > 0 ? (candidate % maximum + maximum) % maximum
        : horizontalScrollDestination(position, direction)
      if (adapter) adapter.move(left)
      else target.scrollTo({ left, behavior: 'instant' })
      schedule()
    })
    layer.append(button)
  }

  const interact = () => {
    if (stopped) return
    if (typeof options === 'object') options.adapter?.interact?.(hovered || layer.contains(document.activeElement))
    schedule()
  }
  layer.addEventListener('pointerenter', () => { hovered = true; interact() })
  layer.addEventListener('pointerleave', () => { hovered = false; interact() })
  layer.addEventListener('focusin', interact)
  layer.addEventListener('focusout', () => queueMicrotask(interact))

  function nativePosition(): ScrollPosition {
    const style = getComputedStyle(target)
    return { left: target.scrollLeft, width: target.clientWidth, contentWidth: target.scrollWidth,
      stepWidth: target.clientWidth - (Number.parseFloat(style.paddingLeft) || 0) - (Number.parseFloat(style.paddingRight) || 0) }
  }
  function clearGutters() {
    target.removeAttribute('data-l12-horizontal-scrollable')
    target.removeAttribute('data-l12-native-scroll-arrows')
    target.style.removeProperty('--l12-scroll-gutter-start')
    target.style.removeProperty('--l12-scroll-gutter-end')
    target.style.removeProperty('--l12-scroll-adapter-gutter')
    extraPadding = 0
    basePadding = undefined
  }
  function hideControls(restoreLayout: boolean) {
    layer.hidden = true
    hovered = false
    if (typeof options === 'object') options.adapter?.interact?.(false)
    if (restoreLayout) clearGutters()
  }
  function viewportChanged() {
    const current = viewportKey()
    if (current !== previousViewport) refreshGutters = true
    previousViewport = current
    schedule()
  }
  function schedule() {
    if (stopped || frame) return
    frame = requestAnimationFrame(() => { frame = 0; measure() })
  }
  function observeChildren() {
    const current = new Set<Element>([target, ...target.children])
    for (const element of observing) if (!current.has(element)) resize.unobserve(element)
    for (const element of current) if (!observing.has(element)) resize.observe(element)
    observing = current
  }
  function measure() {
    layer.hidden = true
    if (stopped || !target.isConnected || !target.parentElement || !isMobileViewportExperience()) { hideControls(true); return }
    if (!inViewport) { hideControls(false); return }
    const preservedLeft = refreshGutters ? target.scrollLeft : undefined
    if (refreshGutters) { clearGutters(); refreshGutters = false }
    const style = getComputedStyle(target)
    const adapter = typeof options === 'object' ? options.adapter : undefined
    if (!adapter && !/^(auto|scroll)$/.test(style.overflowX)) { hideControls(true); return }
    const native = nativePosition()
    // Own gutters must not keep a formerly long, now-short list overflowing.
    const natural = adapter?.position() ?? { ...native, contentWidth: native.contentWidth - extraPadding }
    if (!horizontalScrollState(natural).overflowing || style.visibility === 'hidden' || style.display === 'none') { hideControls(true); return }
    if (adapter && extraPadding) clearGutters()
    basePadding ??= { left: Number.parseFloat(style.paddingLeft) || 0, right: Number.parseFloat(style.paddingRight) || 0 }
    const scale = viewportRect(target).width / target.offsetWidth
    if (!adapter) {
      const clearance = Number.parseFloat(style.getPropertyValue('--l12-scroll-edge-clearance')) || 0
      const start = Math.max(basePadding.left, 42 / (scale > 0 ? scale : 1) + clearance)
      const end = Math.max(basePadding.right, start)
      extraPadding = start + end - basePadding.left - basePadding.right
      target.style.setProperty('--l12-scroll-gutter-start', `${start}px`)
      target.style.setProperty('--l12-scroll-gutter-end', `${end}px`)
      target.setAttribute('data-l12-native-scroll-arrows', 'true')
    } else {
      target.style.setProperty('--l12-scroll-adapter-gutter', `${42 / (scale > 0 ? scale : 1)}px`)
    }
    target.setAttribute('data-l12-horizontal-scrollable', 'true')
    // Repeated focus events also announce the existing viewport. Never remove
    // gutters on an unchanged announcement; a real resize rebases them once
    // and restores the prior offset after padding, avoiding an end-edge clamp.
    if (!adapter && preservedLeft !== undefined) target.scrollLeft = preservedLeft
    const state = horizontalScrollState(adapter?.position() ?? nativePosition())
    // A sibling before the real target preserves its last-child contract and
    // leaves its card children/ref ownership unchanged. Reversible gutters
    // belong to this same native viewport. No wrapper,
    // node move, body portal or global presentation registry is introduced.
    if (layer.parentElement !== target.parentElement || layer.nextSibling !== target) {
      target.parentElement.insertBefore(layer, target)
    }
    // display:none has no offsetParent. Read the containing block while the
    // new controls are paint-hidden, then keep them hidden on every rejection.
    layer.style.visibility = 'hidden'
    layer.hidden = false
    const host = layer.offsetParent
    layer.hidden = true
    if (!(host instanceof HTMLElement)) { hideControls(true); return }
    const canvas = target.closest('.l12-landscape-surface,#l12-landscape-teleports')
    if (canvas && !canvas.contains(host)) { hideControls(true); return }
    const viewport = visibleViewport()
    const physical = document.documentElement.hasAttribute('data-l12-viewport') ? undefined : window.visualViewport
    let visible = clipScrollRect(viewportRect(target), { left: physical?.offsetLeft ?? 0, top: physical?.offsetTop ?? 0,
      right: (physical?.offsetLeft ?? 0) + viewport.width, bottom: (physical?.offsetTop ?? 0) + viewport.height })
    for (let parent: HTMLElement | null = target.parentElement; parent; parent = parent.parentElement) {
      const parentStyle = getComputedStyle(parent)
      if (clips(parentStyle.overflowX) || clips(parentStyle.overflowY)) {
        visible = clipScrollRect(visible, viewportRect(parent), clips(parentStyle.overflowX), clips(parentStyle.overflowY))
      }
      if (parent === canvas) break
    }
    const geometry = scrollControlFrame(visible, viewportRect(host), {
      width: host.offsetWidth, height: host.offsetHeight, borderLeft: host.clientLeft, borderTop: host.clientTop,
      scrollLeft: host.scrollLeft, scrollTop: host.scrollTop,
    })
    if (!geometry) { hideControls(visible.right - visible.left < 80); return }
    const label = typeof options === 'string' ? options : options?.label ?? '内容'
    layer.setAttribute('aria-label', `${label}横向查看`)
    previous.setAttribute('aria-label', `向左查看${label}`)
    next.setAttribute('aria-label', `向右查看${label}`)
    previous.disabled = !adapter?.circular && !state.previous
    next.disabled = !adapter?.circular && !state.next
    Object.assign(layer.style, { left: `${geometry.left}px`, top: `${geometry.top}px`, width: `${geometry.width}px`,
      height: `${geometry.buttonHeight}px`, zIndex: String((Number.parseInt(style.zIndex) || 0) + 1) })
    layer.style.setProperty('--l12-scroll-arrow-width', `${geometry.buttonWidth}px`)
    layer.style.visibility = ''
    layer.hidden = false
    if (adapter && layer.contains(document.activeElement)) adapter.interact?.(true)
  }
  const resize = new ResizeObserver(schedule)
  const intersection = typeof IntersectionObserver === 'undefined' ? undefined : new IntersectionObserver(entries => {
    inViewport = entries.some(entry => entry.isIntersecting)
    schedule()
  })
  intersection?.observe(target)
  const children = new MutationObserver(() => { observeChildren(); schedule() })
  children.observe(target, { childList: true })
  observeChildren()
  // Only these explicit targets are measured. There is no document mutation
  // scan, interval or new gesture. Ancestor scrolling also changes the anchor.
  const onScroll = (event: Event) => {
    if (event.target === document || event.target === target
      || event.target instanceof Element && event.target.contains(target)) schedule()
  }
  document.addEventListener('scroll', onScroll, true)
  target.addEventListener('load', schedule, true)
  window.addEventListener('resize', viewportChanged)
  window.addEventListener('l12-viewport-change', viewportChanged)
  window.visualViewport?.addEventListener('resize', viewportChanged)
  window.visualViewport?.addEventListener('scroll', schedule)
  schedule()
  return {
    update: nextOptions => { options = nextOptions; observeChildren(); schedule() },
    dispose: () => {
      stopped = true
      if (typeof options === 'object') options.adapter?.interact?.(false)
      if (frame) cancelAnimationFrame(frame)
      children.disconnect()
      resize.disconnect()
      intersection?.disconnect()
      observing.clear()
      document.removeEventListener('scroll', onScroll, true)
      target.removeEventListener('load', schedule, true)
      window.removeEventListener('resize', viewportChanged)
      window.removeEventListener('l12-viewport-change', viewportChanged)
      window.visualViewport?.removeEventListener('resize', viewportChanged)
      window.visualViewport?.removeEventListener('scroll', schedule)
      layer.remove()
      clearGutters()
      if (!originalId && target.id === generatedId) target.removeAttribute('id')
    },
  }
}

export const vHorizontalScrollArrows: ObjectDirective<HTMLElement, HorizontalScrollArrowOptions | undefined> = {
  mounted: (element, binding) => { controllers.set(element, createController(element, binding.value)) },
  updated: (element, binding) => controllers.get(element)?.update(binding.value),
  beforeUnmount: element => { controllers.get(element)?.dispose(); controllers.delete(element) },
}
