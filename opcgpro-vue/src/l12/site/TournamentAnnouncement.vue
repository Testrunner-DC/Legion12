<script setup lang="ts">
import { computed, ref, watch } from 'vue'

const props = defineProps<{
  tournamentId: string
  version: number
  announcement?: string | null
  canEdit: boolean
  busy: boolean
}>()
const emit = defineEmits<{ save: [value: { tournamentId: string; version: number; text: string }] }>()
const draft = ref(props.announcement ?? '')
const baseline = ref(props.announcement ?? '')
const baselineVersion = ref(props.version)
const conflict = ref(false)
const invalid = computed(() => draft.value.length > 2000)

function resetDraft() {
  draft.value = props.announcement ?? ''
  baseline.value = draft.value
  baselineVersion.value = props.version
  conflict.value = false
}
watch(() => props.tournamentId, resetDraft, { flush: 'sync' })
watch(() => [props.version, props.announcement] as const, () => {
  if (draft.value === baseline.value || draft.value === (props.announcement ?? '')) resetDraft()
  else if (props.version !== baselineVersion.value || (props.announcement ?? '') !== baseline.value) conflict.value = true
}, { flush: 'sync' })

function save() {
  if (!props.canEdit || props.busy || conflict.value || invalid.value || draft.value === baseline.value) return
  emit('save', { tournamentId: props.tournamentId, version: baselineVersion.value, text: draft.value })
}
</script>

<template>
  <section v-if="canEdit || (announcement && announcement.trim())" class="tournament-announcement">
    <h2>赛事公告</h2>
    <template v-if="canEdit">
      <label for="tournament-announcement-text">公告内容</label>
      <textarea id="tournament-announcement-text" v-model="draft" :disabled="busy" rows="5"
        placeholder="可填写赛事联系方式、奖励说明和其他安排"></textarea>
      <p>{{ draft.length }} / 2000</p>
      <p v-if="invalid" role="alert">公告不能超过 2000 个字符，请修改后再保存。</p>
      <p v-if="conflict" role="alert">赛事信息已更新，草稿仍在。请先查看最新公告，再决定是否重新填写。</p>
      <div class="actions">
        <button type="button" :disabled="busy || !canEdit || invalid || conflict || draft === baseline" @click="save">保存公告</button>
        <button v-if="conflict" type="button" :disabled="busy" @click="resetDraft">查看最新公告</button>
      </div>
    </template>
    <p v-else class="announcement-text">{{ announcement }}</p>
  </section>
</template>

<style scoped>
.tournament-announcement{display:grid;gap:10px;min-width:0}
.tournament-announcement h2,.tournament-announcement p{margin:0}
.tournament-announcement label{font-weight:700}
.tournament-announcement textarea{box-sizing:border-box;width:100%;min-width:0;resize:vertical}
.announcement-text{white-space:pre-wrap;overflow-wrap:anywhere}
.actions{display:flex;flex-wrap:wrap;gap:8px}
</style>