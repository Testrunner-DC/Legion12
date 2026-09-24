<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { DeckCard } from '@/l12/decks'
import { platformState, publicDeckApi, type PublicDeckGuide, type PublicDeckMatchup } from '@/l12/platform'
import { useActionGate } from '@/l12/useActionGate'

const props = defineProps<{ publicationId: string; catalog: DeckCard[] }>()
const emit = defineEmits<{ saved: [message: string] }>()
const { isPending, run } = useActionGate()
const guide = ref<PublicDeckGuide>(emptyGuide())
const matchups = ref<PublicDeckMatchup[]>([])
const loading = ref(false)
const error = ref('')
const revision = ref(0)
const updatedAt = ref('')
const masters = computed(() => props.catalog.filter(card => card.cardType === 'master' || card.cardType === 'divinity'))
const actionKey = computed(() => `public-deck:${platformState.account?.id ?? 'anonymous'}:${props.publicationId}`)

function emptyGuide(): PublicDeckGuide {
  return { buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' }
}

async function loadContent() {
  if (!props.publicationId) return
  loading.value = true
  error.value = ''
  try {
    const entry = await publicDeckApi.get(props.publicationId)
    if (entry.ownerId !== platformState.account?.id) throw new Error('只有公开牌库作者可以编辑这些内容')
    guide.value = { ...(entry.details?.guide ?? emptyGuide()) }
    matchups.value = (entry.details?.matchups ?? []).map(item => ({ ...item }))
    revision.value = entry.details?.contentRevision ?? 0
    updatedAt.value = entry.details?.contentUpdatedAt ?? ''
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '公开内容加载失败'
  } finally {
    loading.value = false
  }
}

function addMatchup() {
  const selected = new Set(matchups.value.map(item => item.opponentMasterId))
  const opponentMasterId = masters.value.find(item => !selected.has(item.id))?.id ?? ''
  matchups.value.push({ opponentMasterId, notes: '', keyCards: '', suggestedSwaps: '' })
}

async function saveContent() {
  if (!props.publicationId || isPending(actionKey.value)) return
  const accountId = platformState.account?.id
  await run(actionKey.value, async () => {
    try {
      const details = await publicDeckApi.updateContent(props.publicationId, guide.value, matchups.value)
      if (accountId !== platformState.account?.id) return
      guide.value = { ...details.guide }
      matchups.value = details.matchups.map(item => ({ ...item }))
      revision.value = details.contentRevision
      updatedAt.value = details.contentUpdatedAt ?? ''
      error.value = ''
      emit('saved', '指南和对局建议已保存，可返回公开详情查看')
    } catch (cause) {
      if (accountId === platformState.account?.id)
        error.value = cause instanceof Error ? cause.message : '公开内容保存失败'
    }
  })
}

function formatTime(value: string) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '尚未保存内容'
}

watch(() => props.publicationId, loadContent, { immediate: true })
</script>

<template>
  <section class="public-content-editor grand-panel" data-editor-workspace="public-content">
    <header>
      <div><p class="kicker">PUBLIC CONTENT</p><h2>公开牌库内容</h2><p>编辑公开详情中的指南和对局建议；构筑版本仍由“更新公开牌库”保存。</p></div>
      <div class="content-status"><b>修订 {{ revision }}</b><small>{{ formatTime(updatedAt) }}</small></div>
    </header>
    <p v-if="loading" class="content-state">正在载入公开内容……</p>
    <template v-else>
      <p v-if="error" class="content-state error">{{ error }}</p>
      <section class="content-block">
        <h3>牌库指南</h3>
        <div class="guide-grid">
          <label>构筑思路<textarea v-model="guide.buildIdea" rows="5" maxlength="1200"/></label>
          <label>起手建议<textarea v-model="guide.opening" rows="4" maxlength="1200"/></label>
          <label>关键牌与配合<textarea v-model="guide.keyCards" rows="4" maxlength="1200"/></label>
          <label>常见展开<textarea v-model="guide.commonSequence" rows="4" maxlength="1200"/></label>
          <label>替换建议<textarea v-model="guide.substitutions" rows="4" maxlength="1200"/></label>
        </div>
      </section>
      <section class="content-block">
        <header><div><h3>对局建议</h3><p>按敌方主宰分别填写；空字段会在详情中明确显示为暂未填写。</p></div><button type="button" @click="addMatchup">添加主宰</button></header>
        <article v-for="(row,index) in matchups" :key="`${row.opponentMasterId}-${index}`" class="matchup-row">
          <label>敌方主宰<select v-model="row.opponentMasterId"><option value="" disabled>请选择</option><option v-for="candidate in masters" :key="candidate.id" :value="candidate.id">{{ candidate.nameZh }}</option></select></label>
          <label>对局思路<textarea v-model="row.notes" rows="3" maxlength="800"/></label>
          <label>关键牌<textarea v-model="row.keyCards" rows="2" maxlength="800"/></label>
          <label>建议换牌<textarea v-model="row.suggestedSwaps" rows="2" maxlength="800"/></label>
          <button type="button" class="danger" @click="matchups.splice(index,1)">移除此项</button>
        </article>
        <p v-if="!matchups.length" class="content-state">还没有对局建议，可按需要添加敌方主宰。</p>
      </section>
      <footer><button type="button" class="primary" :disabled="isPending(actionKey)" @click="saveContent">{{ isPending(actionKey) ? '保存中…' : '保存公开内容' }}</button></footer>
    </template>
  </section>
</template>

<style scoped>
.public-content-editor{overflow:auto}.public-content-editor>header,.content-block>header{display:flex;align-items:flex-start;justify-content:space-between;gap:14px}.public-content-editor h2,.public-content-editor h3{margin:3px 0 8px}.public-content-editor header p{margin:0;color:#8f9995;line-height:1.6}.content-status{display:grid;flex:none;gap:4px;text-align:right}.content-status small{color:#8f9995}.content-block{margin-top:14px;padding:14px;border:1px solid #354041;background:#0b1112}.guide-grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}.guide-grid label:first-child{grid-column:1/-1}.public-content-editor label{display:grid;gap:6px;color:#bac4c5;font-weight:800}.public-content-editor textarea,.public-content-editor select{box-sizing:border-box;width:100%;padding:9px;border:1px solid #4c5a62;background:#081015;color:#eee;font:inherit;line-height:1.55;resize:vertical}.matchup-row{display:grid;grid-template-columns:180px 1fr 1fr 1fr auto;align-items:end;gap:8px;margin-top:10px;padding:10px;border:1px solid #354041;background:#101820}.public-content-editor button{min-height:38px;padding:7px 11px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.public-content-editor .primary{border-color:#e0bf6d;background:#e0bf6d;color:#090c0e}.public-content-editor .danger{border-color:#9e3944;background:#4d171d}.public-content-editor>footer{display:flex;justify-content:flex-end;margin-top:14px}.content-state{color:#8f9995}.content-state.error{color:#e88992}@media(max-width:1000px){.matchup-row{grid-template-columns:1fr 1fr}.matchup-row label:first-child,.matchup-row .danger{grid-column:1/-1}}@media(max-width:620px){.guide-grid,.matchup-row{grid-template-columns:1fr}.guide-grid label:first-child,.matchup-row label:first-child,.matchup-row .danger{grid-column:auto}.public-content-editor>header,.content-block>header{flex-direction:column}.content-status{text-align:left}}
</style>
