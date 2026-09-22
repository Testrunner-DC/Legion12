import { onBeforeUnmount, onMounted, ref, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue'
import { isMobileViewportExperience, visibleViewport } from '../mobileViewport'

export type BattleStageSize = {
  width: number
  height: number
}

export type BattleViewportLayout = {
  compact: boolean
  mobile: boolean
  scale: number
  availableWidth: number
  availableHeight: number
}

type ResolveBattleViewportLayoutOptions = {
  viewportWidth: number
  viewportHeight: number
  stageWidth: number
  stageHeight: number
  gmPanelOpen: boolean
  mobile: boolean
}

const SITE_AND_OVERFLOW_RESERVE = 124
const GM_PANEL_RESERVE = 344

/**
 * Single sizing boundary for the battle board.
 *
 * Mobile mode owns its logical canvas and therefore renders at scale 1; its
 * internal CSS tokens adapt to that canvas. Desktop keeps the established
 * 2048/2304 design stage and scales it uniformly. Keeping these policies in
 * one pure function prevents a mobile tweak from silently changing desktop.
 */
export function resolveBattleViewportLayout(options: ResolveBattleViewportLayoutOptions): BattleViewportLayout {
  const viewportWidth = Math.max(1, options.viewportWidth)
  const viewportHeight = Math.max(1, options.viewportHeight)
  const stageWidth = Math.max(1, options.stageWidth)
  const stageHeight = Math.max(1, options.stageHeight)
  const compact = viewportWidth < 820 || viewportHeight < 600
  const availableHeight = Math.max(1, viewportHeight - SITE_AND_OVERFLOW_RESERVE)
  const availableWidth = Math.max(1, viewportWidth - (options.gmPanelOpen && !compact ? GM_PANEL_RESERVE : 0))
  const desktopScale = Math.min(1, availableWidth / stageWidth, availableHeight / stageHeight)

  return {
    compact,
    mobile: options.mobile,
    scale: options.mobile ? 1 : desktopScale,
    availableWidth,
    availableHeight,
  }
}

type UseBattleViewportLayoutOptions = {
  stageSize: Ref<BattleStageSize>
  gmPanelOpen: MaybeRefOrGetter<boolean>
  afterUpdate?: () => void
}

export function useBattleViewportLayout(options: UseBattleViewportLayoutOptions) {
  const scale = ref(1)
  const compactViewport = ref(false)
  const mobileLandscapeViewport = ref(false)

  function update() {
    const viewport = visibleViewport()
    const mobile = isMobileViewportExperience()
    const resolved = resolveBattleViewportLayout({
      viewportWidth: viewport.width,
      viewportHeight: viewport.height,
      stageWidth: options.stageSize.value.width,
      stageHeight: options.stageSize.value.height,
      gmPanelOpen: toValue(options.gmPanelOpen),
      mobile,
    })
    compactViewport.value = resolved.compact
    mobileLandscapeViewport.value = resolved.mobile
    scale.value = resolved.scale
    options.afterUpdate?.()
  }

  watch(options.stageSize, update)
  watch(() => toValue(options.gmPanelOpen), update)
  onMounted(() => {
    update()
    window.addEventListener('resize', update)
    window.addEventListener('l12-viewport-change', update)
    window.visualViewport?.addEventListener('resize', update)
  })
  onBeforeUnmount(() => {
    window.removeEventListener('resize', update)
    window.removeEventListener('l12-viewport-change', update)
    window.visualViewport?.removeEventListener('resize', update)
  })

  return {
    scale,
    compactViewport,
    mobileLandscapeViewport,
    update,
  }
}
