<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, useId } from 'vue'
import UiButton from './UiButton.vue'
import UiNotice from './UiNotice.vue'

const props = withDefaults(defineProps<{
  title: string
  target?: string
  targetLabel?: string
  impact: string
  busy: boolean
  error?: string
  confirmLabel?: string
  severity?: 'danger' | 'warning'
  allowCancel?: boolean
}>(), {
  target: '', targetLabel: '操作对象', error: '', confirmLabel: '', severity: 'danger', allowCancel: true,
})
const emit = defineEmits<{ cancel: []; confirm: [] }>()
const dialog = ref<HTMLDialogElement>()
const titleId = `admin-risk-title-${useId()}`
const impactId = `admin-risk-impact-${useId()}`
let trigger: HTMLElement | null = null

function cancel(event?: Event) {
  event?.preventDefault()
  if (!props.busy && props.allowCancel) emit('cancel')
}

function trapFocus(event: KeyboardEvent) {
  if (event.key !== 'Tab') return
  const controls = [...(dialog.value?.querySelectorAll<HTMLElement>(
    'button:not(:disabled),input:not(:disabled),textarea:not(:disabled),select:not(:disabled),a[href],[tabindex="0"]',
  ) ?? [])]
  const first = controls[0]
  const last = controls[controls.length - 1]
  if (!first) { event.preventDefault(); dialog.value?.focus(); return }
  if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
  else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
}

onMounted(() => { trigger = document.activeElement as HTMLElement; dialog.value?.showModal() })
onBeforeUnmount(() => { dialog.value?.close(); if (trigger?.isConnected) trigger.focus() })
</script>

<template>
  <Teleport to="body">
    <dialog ref="dialog" class="admin-risk-dialog ui-dialog" :data-severity="severity" tabindex="-1" @keydown="trapFocus" :aria-labelledby="titleId" :aria-describedby="impactId" :aria-busy="busy" @cancel="cancel">
      <h2 :id="titleId">{{ title }}</h2>
      <p v-if="target" class="risk-target">{{ targetLabel }}：{{ target }}</p>
      <p :id="impactId">{{ impact }}</p>
      <slot/>
      <UiNotice v-if="error" kind="error" role="alert">{{ error }}</UiNotice>
      <footer class="ui-action-row">
        <UiButton v-if="allowCancel" autofocus :disabled="busy" @click="cancel">取消</UiButton>
        <UiButton :class="severity" :tone="severity" :autofocus="!allowCancel" :busy="busy" @click="emit('confirm')">{{ busy ? '正在执行…' : confirmLabel || title }}</UiButton>
      </footer>
    </dialog>
  </Teleport>
</template>

<style scoped>
.admin-risk-dialog{box-sizing:border-box;width:min(520px,calc(100vw - 32px));max-height:calc(100dvh - 32px);overflow:auto;margin:auto;padding:24px;border:1px solid #86505a;background:#111923;color:#e9eeeb;font-family:'Microsoft YaHei','微软雅黑',sans-serif;overflow-wrap:anywhere}.admin-risk-dialog[data-severity="warning"]{border-color:#9d7c36}.admin-risk-dialog::backdrop{background:#020609cc}.admin-risk-dialog h2{margin-top:0;font-size:22px}.admin-risk-dialog p{line-height:1.65}.risk-target{font-weight:700}.admin-risk-dialog footer{display:flex;justify-content:flex-end;flex-wrap:wrap;gap:12px;margin-top:24px}
</style>
