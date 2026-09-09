<script setup lang="ts">
import { ref, watch } from 'vue'
import { integrityApi, integrityRequestId } from '../rankedIntegrity'
import { platformState } from '../platform'
const props = defineProps<{ decisionId: string }>()
const emit = defineEmits<{ submitted: [] }>()
const statement = ref('')
const busy = ref(false)
const message = ref('')
const submitted = ref(false)
const requestIds = new Map<string, string>()
watch(() => props.decisionId, () => { statement.value = ''; message.value = ''; submitted.value = false })
async function submit() {
  if (busy.value || !statement.value.trim()) return
  const decision = props.decisionId; const account = platformState.account?.id
  const key = `${account}:${decision}:${statement.value.trim()}`
  if (!requestIds.has(key)) requestIds.set(key, integrityRequestId())
  busy.value = true; message.value = ''
  try {
    await integrityApi.appeal(decision, requestIds.get(key)!, statement.value.trim())
    if (decision !== props.decisionId || account !== platformState.account?.id) return
    submitted.value = true; message.value = '申诉已提交。可在判罚历史查看处理进度；申诉不会自动解除当前限制。'
    window.dispatchEvent(new Event('l12-integrity-changed')); emit('submitted')
  } catch (error) {
    if (decision === props.decisionId && account === platformState.account?.id) message.value = error instanceof Error ? error.message : '提交失败，请重试'
  } finally { busy.value = false }
}
</script>
<template>
  <form class="ranked-appeal-form" @submit.prevent="submit">
    <label>申诉说明<textarea v-model="statement" :disabled="busy || submitted" required maxlength="1000" rows="4" placeholder="说明你认为处置有误的原因、发生时间及可供核对的情况。请勿填写密码等敏感信息。"/></label>
    <small>最多1000字。同一处置存在待处理申诉时不能重复提交。</small>
    <p v-if="message" role="status">{{ message }}</p>
    <button :disabled="busy || submitted || !statement.trim()">{{ busy ? '提交中…' : submitted ? '已提交' : '提交申诉' }}</button>
  </form>
</template>
<style scoped>
.ranked-appeal-form{display:grid;gap:10px;margin-top:12px;color:#dce4e4}.ranked-appeal-form label{display:grid;gap:8px}.ranked-appeal-form textarea{box-sizing:border-box;width:100%;resize:vertical;max-height:240px;min-height:90px;padding:10px;border:1px solid #526571;background:#0b141c;color:#edf2ef;font:inherit}.ranked-appeal-form small{color:#a2b2bd;line-height:1.5}.ranked-appeal-form p{white-space:pre-wrap;overflow-wrap:anywhere}.ranked-appeal-form button{justify-self:end;min-height:40px;padding:8px 18px;border:1px solid #d8bd6a;background:#332b16;color:#f4e8b9;font:inherit}.ranked-appeal-form button:disabled{opacity:.55}
</style>
