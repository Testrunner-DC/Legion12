<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { captureDeckAccountGuard, type DeckCard } from '@/l12/decks'
import SingleCardPicker, { type SingleCardPickerItem } from '@/l12/SingleCardPicker.vue'
import { platformState, publicDeckReadApi, type PublicDeckGuide, type PublicDeckMatchup, type PublicDeckReadGeneration } from '@/l12/platform'
import { useActionGate } from '@/l12/useActionGate'
import { currentReadGeneration, settlePublicDeckRead, validatePublicDeckContentCurrent, validatePublicDeckCurrent } from './publicDeckRead'

const props = defineProps<{ publicationId: string; catalog: DeckCard[] }>()
const emit = defineEmits<{ saved: [message: string]; dialogChange: [open: boolean] }>()
const { isPending, run } = useActionGate()
const guide = ref<PublicDeckGuide>(emptyGuide())
const matchups = ref<PublicDeckMatchup[]>([])
const loading = ref(false)
const error = ref('')
const revision = ref(0)
const updatedAt = ref('')
const readGeneration = ref<PublicDeckReadGeneration | null>(null)
const refreshRequired = ref(false)
const matchupPickerIndex = ref<number | null>(null)
const contentRoot = ref<HTMLElement | null>(null)
let contentEpoch = 0
let contentRequestSequence = 0
let draftEpoch = 0
let draftBaselineKey = ''
let pickerReturnFocus: HTMLElement | null = null
const pickerDialog = () => document.querySelector<HTMLElement>('.single-card-picker[aria-label="选择对方主宰"]')
watch(matchupPickerIndex, async (index, previous) => {
  const open = index !== null
  if (open && previous === null) pickerReturnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
  emit('dialogChange', open)
  await nextTick()
  if (open) pickerDialog()?.querySelector<HTMLElement>('button[aria-label="关闭"]')?.focus({ preventScroll: true })
  else if (pickerReturnFocus?.isConnected) pickerReturnFocus.focus({ preventScroll: true })
  else contentRoot.value?.querySelectorAll<HTMLElement>('.card-picker-trigger')[previous ?? 0]?.focus({ preventScroll: true })
})
function handlePickerKey(event: KeyboardEvent) {
  if (matchupPickerIndex.value === null) return
  if (event.key === 'Escape') { event.preventDefault(); matchupPickerIndex.value = null; return }
  if (event.key !== 'Tab') return
  const dialog = pickerDialog()
  const focusable = [...(dialog?.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),select:not(:disabled),[tabindex="0"]') ?? [])]
    .filter(element => element.getClientRects().length > 0)
  const first = focusable[0], last = focusable[focusable.length - 1]
  if (!first || !last) { event.preventDefault(); return }
  if (!dialog?.contains(document.activeElement) || event.shiftKey && document.activeElement === first || !event.shiftKey && document.activeElement === last) {
    event.preventDefault(); (event.shiftKey ? last : first).focus({ preventScroll: true })
  }
}
onMounted(() => window.addEventListener('keydown', handlePickerKey))
onBeforeUnmount(() => { contentEpoch++; contentRequestSequence++; window.removeEventListener('keydown', handlePickerKey); emit('dialogChange', false) })
const homeCities = computed(() => props.catalog.filter(card => card.cardType === 'master'))
const matchupPickerItems = computed<SingleCardPickerItem[]>(() => homeCities.value.map(card => ({
  id: card.id, cardId: card.id, cardImageId: card.id, number: card.number, name: card.nameZh, nameZh: card.nameZh, cardType: card.cardType,
  faction: card.faction, product: card.product, imageUrl: card.imageUrl,
})))
const actionKey = computed(() => `public-deck:${platformState.account?.id ?? 'anonymous'}:${props.publicationId}`)

function emptyGuide(): PublicDeckGuide {
  return { buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' }
}

function contentDraftKey() {
  return JSON.stringify({ guide: guide.value, matchups: matchups.value })
}

async function loadContent() {
  const epoch = ++contentEpoch, request = ++contentRequestSequence, publicationId = props.publicationId
  const actorCurrent = captureDeckAccountGuard()
  const current = () => request === contentRequestSequence && epoch === contentEpoch
    && publicationId === props.publicationId && actorCurrent()
  matchupPickerIndex.value = null
  guide.value = emptyGuide()
  matchups.value = []
  revision.value = 0
  updatedAt.value = ''
  readGeneration.value = null
  refreshRequired.value = false
  draftBaselineKey = contentDraftKey()
  if (!props.publicationId) return
  loading.value = true
  error.value = ''
  try {
    const result = await settlePublicDeckRead(publicDeckReadApi.current(publicationId), validatePublicDeckCurrent)
    if (!current()) return
    if (result.status !== 'available') { error.value = result.message; refreshRequired.value = result.status === 'refresh-required'; return }
    const entry = result.value
    if (!entry.summary.canEdit || entry.summary.id !== publicationId)
      throw new Error('只有公开牌库作者可以编辑这些内容')
    readGeneration.value = currentReadGeneration(entry)
    guide.value = { ...entry.guide }
    matchups.value = entry.matchups.map(item => ({ ...item }))
    revision.value = entry.contentRevision
    updatedAt.value = entry.contentUpdatedAt ?? ''
    draftBaselineKey = contentDraftKey()
  } catch (cause) {
    if (current()) error.value = cause instanceof Error ? cause.message : '公开内容加载失败'
  } finally {
    if (current()) loading.value = false
  }
}

function addMatchup() {
  const selected = new Set(matchups.value.map(item => item.opponentMasterId))
  const opponentMasterId = homeCities.value.find(item => !selected.has(item.id))?.id ?? ''
  matchups.value.push({ opponentMasterId, notes: '', keyCards: '', suggestedSwaps: '' })
}

function chooseMatchupMaster(card: SingleCardPickerItem) {
  if (matchupPickerIndex.value === null || !matchups.value[matchupPickerIndex.value]) return
  matchups.value[matchupPickerIndex.value]!.opponentMasterId = card.cardId
  matchupPickerIndex.value = null
}

function matchupMasterName(cardId: string) {
  return homeCities.value.find(card => card.id === cardId)?.nameZh || '选择对方主宰'
}

async function saveContent() {
  if (!props.publicationId || !readGeneration.value || refreshRequired.value || isPending(actionKey.value)) return
  const publicationId = props.publicationId, epoch = contentEpoch, request = ++contentRequestSequence
  const actorCurrent = captureDeckAccountGuard(), generation = { ...readGeneration.value }
  const submittedGuide = { ...guide.value }, submittedMatchups = matchups.value.map(item => ({ ...item }))
  const submittedDraftEpoch = draftEpoch, previousRevision = revision.value
  const current = () => request === contentRequestSequence && actorCurrent()
    && publicationId === props.publicationId && epoch === contentEpoch
  await run(actionKey.value, async () => {
    try {
      if (!current()) return
      const result = await settlePublicDeckRead(
        publicDeckReadApi.updateContent(generation.publicCode, generation.readToken, submittedGuide, submittedMatchups),
        value => validatePublicDeckContentCurrent(value, generation, previousRevision))
      if (!current()) return
      if (result.status !== 'available') {
        refreshRequired.value = result.status === 'refresh-required'
        error.value = result.message
        return
      }
      const details = result.value
      const draftUnchanged = draftEpoch === submittedDraftEpoch
      readGeneration.value = { id: details.id, publicCode: details.publicCode, readToken: details.readToken,
        catalogVersion: details.catalogVersion, policyVersion: details.policyVersion }
      if (draftUnchanged) {
        guide.value = { ...details.guide }
        matchups.value = details.matchups.map(item => ({ ...item }))
        draftBaselineKey = contentDraftKey()
      }
      revision.value = details.contentRevision
      updatedAt.value = details.contentUpdatedAt ?? ''
      refreshRequired.value = false
      error.value = ''
      emit('saved', draftUnchanged
        ? '指南和对局建议已保存，可返回公开详情查看'
        : '提交时的公开内容已保存；当前编辑仍有未保存修改')
    } catch (cause) {
      if (current())
        error.value = cause instanceof Error ? cause.message : '公开内容保存失败'
    }
  })
}

async function refreshContentGeneration() {
  if (!props.publicationId) return
  const publicationId = props.publicationId, epoch = contentEpoch, request = ++contentRequestSequence
  const hadGeneration = readGeneration.value !== null, baselineKey = draftBaselineKey
  const actorCurrent = captureDeckAccountGuard()
  const current = () => request === contentRequestSequence && actorCurrent()
    && publicationId === props.publicationId && epoch === contentEpoch
  loading.value = true
  try {
    const result = await settlePublicDeckRead(publicDeckReadApi.current(publicationId), validatePublicDeckCurrent)
    if (!current()) return
    if (result.status !== 'available') { error.value = result.message; return }
    if (!result.value.summary.canEdit || result.value.summary.id !== publicationId) {
      error.value = '只有公开牌库作者可以编辑这些内容'; return
    }
    const preserveDraft = hadGeneration || contentDraftKey() !== baselineKey
    readGeneration.value = currentReadGeneration(result.value)
    if (!preserveDraft) {
      guide.value = { ...result.value.guide }
      matchups.value = result.value.matchups.map(item => ({ ...item }))
      draftBaselineKey = contentDraftKey()
    }
    revision.value = result.value.contentRevision
    updatedAt.value = result.value.contentUpdatedAt ?? ''
    refreshRequired.value = false
    error.value = preserveDraft ? '已获取最新内容，当前指南和对局建议草稿仍保留。' : '已获取最新内容。'
  } catch (cause) {
    if (current()) error.value = cause instanceof Error ? cause.message : '公开内容加载失败'
  } finally {
    if (current()) loading.value = false
  }
}

function formatTime(value: string) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '尚未保存内容'
}

watch(() => JSON.stringify({ guide: guide.value, matchups: matchups.value }), () => draftEpoch++, { flush: 'sync' })
watch(() => [props.publicationId, platformState.account?.id, platformState.token], loadContent, { immediate: true, flush: 'sync' })
</script>

<template>
  <section ref="contentRoot" class="public-content-editor grand-panel" data-editor-workspace="public-content">
    <header>
      <div><p class="kicker">公开内容</p><h2>公开牌库内容</h2><p>编辑公开详情中的指南和对局建议；构筑版本仍由“更新公开牌库”保存。</p></div>
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
        <header><div><h3>对局建议</h3><p>按对方主宰分别填写；完全留空的内容不会出现在公开详情中。</p></div><button type="button" @click="addMatchup">添加主宰</button></header>
        <article v-for="(row,index) in matchups" :key="index" class="matchup-row">
          <label>对方主宰<button type="button" class="card-picker-trigger" @click="matchupPickerIndex = index">{{ matchupMasterName(row.opponentMasterId) }}</button></label>
          <label>对局思路<textarea v-model="row.notes" rows="3" maxlength="800"/></label>
          <label>关键牌<textarea v-model="row.keyCards" rows="2" maxlength="800"/></label>
          <label>建议换牌<textarea v-model="row.suggestedSwaps" rows="2" maxlength="800"/></label>
          <button type="button" class="danger" @click="matchups.splice(index,1)">移除此项</button>
        </article>
        <p v-if="!matchups.length" class="content-state">还没有对局建议，可按需要添加对方主宰。</p>
      </section>
      <footer><button v-if="refreshRequired || !readGeneration" type="button" :disabled="loading || isPending(actionKey)" @click="refreshContentGeneration">重新获取最新内容</button><button type="button" class="primary" :disabled="loading || !readGeneration || refreshRequired || isPending(actionKey)" @click="saveContent">{{ isPending(actionKey) ? '保存中…' : '保存公开内容' }}</button></footer>
    </template>
    <SingleCardPicker v-if="matchupPickerIndex !== null" title="选择对方主宰" :items="matchupPickerItems" :allowed-types="['master']" @select="chooseMatchupMaster" @close="matchupPickerIndex = null"/>
  </section>
</template>

<style scoped>
.public-content-editor{overflow:auto}.public-content-editor>header,.content-block>header{display:flex;align-items:flex-start;justify-content:space-between;gap:14px}.public-content-editor h2,.public-content-editor h3{margin:3px 0 8px}.public-content-editor header p{margin:0;color:#8f9995;line-height:1.6}.content-status{display:grid;flex:none;gap:4px;text-align:right}.content-status small{color:#8f9995}.content-block{margin-top:14px;padding:14px;border:1px solid #354041;background:#0b1112}.guide-grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}.guide-grid label:first-child{grid-column:1/-1}.public-content-editor label{display:grid;gap:6px;color:#bac4c5;font-weight:800}.public-content-editor textarea,.public-content-editor select{box-sizing:border-box;width:100%;padding:9px;border:1px solid #4c5a62;background:#081015;color:#eee;font:inherit;line-height:1.55;resize:vertical}.matchup-row{display:grid;grid-template-columns:1fr;align-items:stretch;gap:10px;margin-top:10px;padding:12px;border:1px solid #354041;background:#101820}.matchup-row label{width:100%}.matchup-row textarea{min-height:86px}.card-picker-trigger{width:100%;text-align:left!important}.matchup-row .danger{justify-self:end}.public-content-editor button{min-height:38px;padding:7px 11px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.public-content-editor .primary{border-color:#e0bf6d;background:#e0bf6d;color:#090c0e}.public-content-editor .danger{border-color:#9e3944;background:#4d171d}.public-content-editor>footer{display:flex;justify-content:flex-end;margin-top:14px}.content-state{color:#8f9995}.content-state.error{color:#e88992}@media(max-width:620px){.guide-grid{grid-template-columns:1fr}.guide-grid label:first-child{grid-column:auto}.public-content-editor>header,.content-block>header{flex-direction:column}.content-status{text-align:left}.matchup-row .danger{width:100%}}
</style>
