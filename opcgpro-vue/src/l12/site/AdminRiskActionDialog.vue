<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
const props = defineProps<{ title: string; target: string; impact: string; busy: boolean; error?: string }>()
const emit = defineEmits<{ cancel: []; confirm: [] }>()
const dialog = ref<HTMLDialogElement>()
let trigger: HTMLElement | null = null
function cancel(event?: Event) { event?.preventDefault(); if (!props.busy) emit('cancel') }
function trapFocus(event: KeyboardEvent) {
  if (event.key !== 'Tab') return
  const controls = [...(dialog.value?.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),[tabindex="0"]') ?? [])]
  const first = controls[0]; const last = controls[controls.length - 1]
  if (!first) { event.preventDefault(); dialog.value?.focus(); return }
  if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
  else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
}
onMounted(() => { trigger = document.activeElement as HTMLElement; dialog.value?.showModal() })
onBeforeUnmount(() => { dialog.value?.close(); trigger?.isConnected && trigger.focus() })
</script>
<template>
  <Teleport to="body">
    <dialog ref="dialog" class="admin-risk-dialog" tabindex="-1" @keydown="trapFocus" aria-labelledby="admin-risk-title" aria-describedby="admin-risk-impact" :aria-busy="busy" @cancel="cancel">
      <h2 id="admin-risk-title">{{ title }}</h2>
      <p class="risk-target">目标账号：{{ target }}</p>
      <p id="admin-risk-impact">{{ impact }}</p>
      <p v-if="error" role="alert">{{ error }}</p>
      <footer><button type="button" autofocus :disabled="busy" @click="cancel">取消</button><button type="button" class="danger" :disabled="busy" @click="emit('confirm')">{{ busy ? '正在执行…' : title }}</button></footer>
    </dialog>
  </Teleport>
</template>
<style scoped>
.admin-risk-dialog{box-sizing:border-box;width:min(520px,calc(100vw - 32px));max-height:calc(100dvh - 32px);overflow:auto;margin:auto;padding:24px;border:1px solid #86505a;background:#111923;color:#e9eeeb;font-family:inherit;overflow-wrap:anywhere}.admin-risk-dialog::backdrop{background:#020609cc}.admin-risk-dialog h2{margin-top:0;font-size:22px}.admin-risk-dialog p{line-height:1.65}.risk-target{font-weight:700}.admin-risk-dialog footer{display:flex;justify-content:flex-end;flex-wrap:wrap;gap:12px;margin-top:24px}.admin-risk-dialog button{min-height:44px;padding:10px 18px;border:1px solid #53616a;background:#101a22;color:#e9eeeb;font:inherit}.admin-risk-dialog .danger{border-color:#a95565;background:#401720;color:#ffd3da}.admin-risk-dialog button:disabled{opacity:.6}.admin-risk-dialog button:focus-visible{outline:2px solid #f1d67d;outline-offset:3px}
</style>
