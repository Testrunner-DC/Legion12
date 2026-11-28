// B5 阶段 3（脚本侧 composable，候选 1）：检视器浮层的定位与自适应字号。
// 只依赖「是否可见」这一个外部状态与锚点 DOM，其余全部自洽。
import { ref, watch, type Ref } from 'vue'
import { viewportRect } from '../../mobileViewport'

export const adaptiveBoardToken = (base: number, currentScale: number) => {
  const safeScale = Math.max(.1, Math.min(1, currentScale))
  const minimumScreenSize = base >= 13 ? 8 : base >= 11 ? 7 : 6
  return Math.max(base / Math.sqrt(safeScale), minimumScreenSize / safeScale)
}

export function useInspectorFloat(visible: Ref<boolean>) {
  const inspectorAnchor = ref<HTMLElement | null>(null)
  const inspectorFloatStyle = ref<Record<string, string>>({})

  function updateInspectorFloatRect() {
    if (!visible.value || !inspectorAnchor.value) return
    const rect = viewportRect(inspectorAnchor.value)
    const logicalWidth = inspectorAnchor.value.offsetWidth || rect.width
    const logicalHeight = inspectorAnchor.value.offsetHeight || rect.height
    const floatScale = logicalWidth > 0 ? rect.width / logicalWidth : 1
    inspectorFloatStyle.value = {
      left: `${rect.left}px`,
      top: `${rect.top}px`,
      width: `${logicalWidth}px`,
      height: `${logicalHeight}px`,
      transform: `scale(${floatScale})`,
      '--l12-board-copy': `${adaptiveBoardToken(13, floatScale)}px`,
      '--l12-board-meta': `${adaptiveBoardToken(11, floatScale)}px`,
      '--l12-board-micro': `${adaptiveBoardToken(9, floatScale)}px`,
      '--l12-effect-copy': `${adaptiveBoardToken(13, floatScale)}px`,
    }
  }

  watch(visible, value => {
    if (value) updateInspectorFloatRect()
  }, { flush: 'sync' })

  return { inspectorAnchor, inspectorFloatStyle, updateInspectorFloatRect }
}
