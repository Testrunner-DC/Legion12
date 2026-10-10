<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { captureBugClientDiagnostic, l12State } from '@/l12/net'
import { platformState, submitBug } from '@/l12/platform'
import { landscapeTeleportTarget } from '@/l12/mobileViewport'
import { usesSiteUiStates } from './siteUiStateScope'
import { visibleFocusTarget } from './bugFeedbackEntry'

const route = useRoute()
const open = ref(false)
const dialog = ref<HTMLElement | null>(null)
const busy = ref(false)
const message = ref('')
const form = reactive({ bugDescription: '', suggestion: '' })
let mounted = true
let requestId = 0
let draftRevision = 0
let dialogEpoch = 0
let identityEpoch = 0
let returnFocus: HTMLElement | null = null
let fallbackFocus: HTMLElement | null = null
watch(() => [form.bugDescription, form.suggestion], () => { draftRevision++ }, { flush: 'sync' })
watch(open, () => { dialogEpoch++; message.value = '' }, { flush: 'sync' })
watch(() => [platformState.account?.id, platformState.token], (current, previous) => {
  identityEpoch++
  message.value = ''
  if (current[0] !== previous[0]) {
    form.bugDescription = ''
    form.suggestion = ''
  }
}, { flush: 'sync' })
function openFeedback(event: Event) {
  const detail = (event as CustomEvent<{ returnFocus?: HTMLElement; fallbackFocus?: HTMLElement }>).detail
  returnFocus = detail?.returnFocus ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null)
  fallbackFocus = detail?.fallbackFocus ?? null
  open.value = true
  void nextTick(() => dialog.value?.querySelector<HTMLTextAreaElement>('textarea')?.focus())
}
function closeFeedback() {
  open.value = false
  void nextTick(() => (visibleFocusTarget(returnFocus) ?? visibleFocusTarget(fallbackFocus))?.focus())
}
function onDialogKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') { event.stopPropagation(); closeFeedback(); return }
  if (event.key !== 'Tab' || !dialog.value) return
  event.stopPropagation()
  const actions = Array.from(dialog.value.querySelectorAll<HTMLElement>('button:not(:disabled),textarea:not(:disabled)'))
    .filter(element => element.tabIndex >= 0 && element.getClientRects().length > 0)
  if (!actions.length) return
  const first = actions[0]!, last = actions[actions.length - 1]!
  if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
  else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
}
function onWindowKeydown(event: KeyboardEvent) {
  if (!open.value || event.key !== 'Escape' || event.defaultPrevented) return
  event.preventDefault()
  event.stopImmediatePropagation()
  closeFeedback()
}
onMounted(() => {
  window.addEventListener('l12-open-bug-feedback', openFeedback)
  window.addEventListener('keydown', onWindowKeydown, true)
})
onBeforeUnmount(() => {
  mounted = false
  window.removeEventListener('l12-open-bug-feedback', openFeedback)
  window.removeEventListener('keydown', onWindowKeydown, true)
})

async function submit() {
  if (busy.value || !mounted) return
  const bugDescription = form.bugDescription.trim()
  const suggestion = form.suggestion.trim()
  if (!bugDescription && !suggestion) { message.value = 'Bug提交或优化建议至少填写一项'; return }
  const currentRequest = ++requestId
  const submittedDraft = draftRevision
  const submittedDialog = dialogEpoch
  const submittedIdentity = identityEpoch
  const context = { page: route.path, roomCode: l12State.room?.roomCode, matchId: l12State.game?.matchId }
  const sameIdentity = () => mounted && submittedIdentity === identityEpoch
  const ownsDraft = () => sameIdentity() && currentRequest === requestId && open.value
    && submittedDialog === dialogEpoch && submittedDraft === draftRevision
  busy.value = true
  message.value = ''
  try {
    const diagnostic = await captureBugClientDiagnostic(context.page)
    // Diagnostics may finish after logout or component disposal. Never post the old draft as a new user.
    if (!sameIdentity()) return
    const clientDiagnostic = { ...diagnostic, roomCode: context.roomCode, matchId: context.matchId }
    const report = await submitBug({
      title: bugDescription && suggestion ? 'Bug与优化建议' : bugDescription ? 'Bug提交' : '优化建议',
      description: [bugDescription ? `【Bug提交】\n${bugDescription}` : '', suggestion ? `【优化建议】\n${suggestion}` : ''].filter(Boolean).join('\n\n'),
      ...context,
      version: String(import.meta.env.VITE_APP_VERSION || 'dev'),
      clientDiagnostic,
    })
    if (ownsDraft()) {
      form.bugDescription = ''
      form.suggestion = ''
      message.value = `已提交：${report.id}`
    }
  } catch (error) {
    if (ownsDraft()) message.value = error instanceof Error ? error.message : '提交失败'
  } finally {
    if (mounted && currentRequest === requestId) busy.value = false
  }
}
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()">
    <div v-if="open" class="bug-feedback-mask" @click.self="closeFeedback">
      <section ref="dialog" class="bug-feedback-dialog" :class="{ 'ui-state-scope': usesSiteUiStates(route.meta) }" role="dialog" aria-modal="true" aria-labelledby="bug-feedback-title" @keydown="onDialogKeydown">
        <header><div><small>BUG REPORT</small><h2 id="bug-feedback-title">反馈 Bug</h2></div><button aria-label="关闭反馈" @click="closeFeedback">×</button></header>
        <p>Bug提交和优化建议可分别填写，任意一项有内容即可提交。页面、房间、对局、版本与时间会自动附带。</p>
        <label>Bug提交<textarea v-model="form.bugDescription" maxlength="5000" rows="6" placeholder="描述大厅或对局中触发Bug的操作和实际现象；提及卡牌的时候请勿使用俗称，最好使用卡牌编号（例：S01-0001）……提交时会自动附带当前页面信息。"/></label>
        <label>优化建议<textarea v-model="form.suggestion" maxlength="5000" rows="5" placeholder="描述你希望优化的Bug、操作体验或界面效果"/></label>
        <div class="bug-context"><span>提交身份：{{ platformState.account?.username || '匿名玩家' }}</span><span>页面：{{ route.fullPath }}</span><span v-if="l12State.room">房间：{{ l12State.room.roomCode }}</span></div>
        <p v-if="message" class="bug-message">{{ message }}</p>
        <footer><button @click="closeFeedback">取消</button><button class="submit" :disabled="busy" @click="submit">{{ busy ? '提交中…' : '提交反馈' }}</button></footer>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.bug-feedback-mask{position:fixed;z-index:5000;inset:0;display:grid;grid-template-columns:minmax(0,1fr);place-items:center;padding:20px;background:rgba(1,4,7,.78);backdrop-filter:blur(8px)}.bug-feedback-dialog{box-sizing:border-box;width:min(560px,95vw);max-width:100%;min-width:0;max-height:calc(100dvh - 40px);overflow:auto;padding:22px;border:1px solid #687277;background:#101820;color:#f3f0e8;box-shadow:0 30px 90px #000;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.bug-feedback-dialog header{display:flex;align-items:center;justify-content:space-between}.bug-feedback-dialog small{color:#d5b85e;font:900 14px monospace;letter-spacing:.18em}.bug-feedback-dialog h2{margin:4px 0;font-size:24px}.bug-feedback-dialog header button{width:34px;height:34px;border:1px solid #515d64;background:#080d11;color:#fff;font-size:20px}.bug-feedback-dialog>p{color:#89959b;font-size:14px;line-height:1.7}.bug-feedback-dialog label{display:block;margin-top:15px;color:#c7ccca;font-size:14px;font-weight:900}.bug-feedback-dialog input,.bug-feedback-dialog textarea{box-sizing:border-box;width:100%;margin-top:7px;padding:12px;border:1px solid #46535b;background:#070d12;color:#fff;font:700 14px 'Microsoft YaHei','微软雅黑';outline:none;resize:vertical}.bug-feedback-dialog input:focus,.bug-feedback-dialog textarea:focus{border-color:#56bec5}.bug-context{display:flex;flex-wrap:wrap;gap:6px;margin-top:12px}.bug-context span{padding:4px 7px;background:#172129;color:#88959a;font-size:14px}.bug-message{color:#e5c76d!important;font-weight:900}.bug-feedback-dialog footer{display:flex;flex-wrap:wrap;justify-content:center;gap:10px;margin-top:18px}.bug-feedback-dialog footer button{min-width:120px;padding:11px;border:1px solid #5b676d;background:#121b22;color:#fff;font-weight:900}.bug-feedback-dialog footer .submit{border-color:#d8ba62;background:#d8ba62;color:#111}
@media(max-width:760px),(max-height:520px){.bug-feedback-dialog{padding:17px}}
</style>
