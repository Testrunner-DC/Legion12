<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

const report = ref('正在读取此页面的动效状态…')
const pending = ref(false)
const notice = ref('')
let generation = 0
function sampleRows() {
  return Array.from(document.querySelectorAll<HTMLElement>('.hot-deck-viewport')).slice(0, 2).map(viewport => {
    const track = viewport.querySelector<HTMLElement>('.hot-deck-track')
    const cover = viewport.querySelector<HTMLElement>('.deck-cover')
    const fan = viewport.querySelector<HTMLElement>('.deck-cover__cards')
    const style = track ? getComputedStyle(track) : null
    return {
      ready: track?.dataset.motionReady ?? null, pausedClass: viewport.classList.contains('paused'),
      hovered: viewport.matches(':hover'), focusVisible: Boolean(viewport.querySelector(':focus-visible')),
      width: viewport.getBoundingClientRect().width, scrollLeft: viewport.scrollLeft,
      animation: style?.animationName, duration: style?.animationDuration,
      iterations: style?.animationIterationCount, playState: style?.animationPlayState,
      transform: style?.transform, loopDuration: track?.style.getPropertyValue('--hot-deck-duration'),
      coverTransition: cover ? getComputedStyle(cover).transitionDuration : null,
      fanTransition: fan ? getComputedStyle(fan).transitionDuration : null,
      fanTransform: fan ? getComputedStyle(fan).transform : null,
    }
  })
}
async function capture() {
  if (pending.value) return
  pending.value = true
  const epoch = ++generation
  const before = sampleRows(), started = performance.now()
  await new Promise(resolve => window.setTimeout(resolve, 700))
  if (epoch !== generation) return
  const media = (query: string) => window.matchMedia(query).matches
  report.value = JSON.stringify({
    diagnostic: 'deck-motion-v1', frontend: import.meta.env.VITE_APP_VERSION,
    visibility: document.visibilityState, focused: document.hasFocus(),
    animationSetting: document.documentElement.dataset.l12Animation,
    reducedMotion: media('(prefers-reduced-motion: reduce)'), hover: media('(hover: hover)'),
    finePointer: media('(pointer: fine)'), anyHover: media('(any-hover: hover)'), anyFinePointer: media('(any-pointer: fine)'),
    elapsedMs: Math.round(performance.now() - started), before, after: sampleRows(),
    stylesheets: Array.from(document.styleSheets).map(sheet => sheet.href ? new URL(sheet.href).pathname.split('/').at(-1) : 'inline').slice(0, 32),
  }, null, 2)
  pending.value = false
}
async function copyReport() {
  try { await navigator.clipboard.writeText(report.value); notice.value = '已复制，请粘贴给我。' }
  catch { notice.value = '请选中下方内容后复制。' }
}
onMounted(() => { void capture() })
onBeforeUnmount(() => { generation++ })
</script>

<template>
  <aside class="deck-motion-diagnostics" aria-label="牌库动效只读诊断">
    <header><b>牌库动效诊断</b><span>仅本页显示，不上传数据</span></header>
    <p>请复制结果发给我。不含账号、牌库内容或凭据。</p>
    <div><button :disabled="pending" @click="capture">{{ pending ? '读取中…' : '重新读取' }}</button><button :disabled="pending" @click="copyReport">复制诊断结果</button></div>
    <textarea readonly aria-label="动效诊断结果" :value="report" spellcheck="false" />
    <p role="status">{{ notice }}</p>
  </aside>
</template>

<style scoped>
.deck-motion-diagnostics{position:fixed;z-index:95;right:12px;top:12px;width:min(430px,calc(100vw - 24px));box-sizing:border-box;padding:14px;border:1px solid #72bec7;background:#0c151bf5;color:#edf4f3;box-shadow:0 12px 35px #000a;font:13px/1.5 'Microsoft YaHei','微软雅黑',sans-serif}
header{display:flex;justify-content:space-between;gap:10px}header span,p{color:#b3c5c9;font-size:12px}p{margin:7px 0}div{display:flex;gap:8px;margin:9px 0}button{min-height:36px;padding:6px 12px;border:1px solid #687b84;background:#172a34;color:#fff;font:inherit;cursor:pointer}button:disabled{opacity:.55}textarea{display:block;box-sizing:border-box;width:100%;height:min(330px,45dvh);resize:vertical;border:1px solid #465963;background:#071015;color:#dce8e6;font:12px/1.5 'Microsoft YaHei','微软雅黑',sans-serif}
@media(max-width:700px){.deck-motion-diagnostics{top:66px}}
</style>
