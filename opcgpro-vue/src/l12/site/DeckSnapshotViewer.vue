<script setup lang="ts">
import { computed } from 'vue'
import { encodeDeckCode, downloadDeckImage } from './deckShare'
import DeckConstructionBrowser, { type ConstructionEntry } from './DeckConstructionBrowser.vue'
import { loadSavedDecks, saveDeck, type DeckCard, type SavedL12Deck } from '@/l12/decks'

const props = withDefaults(defineProps<{
  entries: ConstructionEntry[]
  catalog: DeckCard[]
  title: string
  masterId?: string
  deckName?: string
  eyebrow?: string
}>(), { masterId: '', deckName: '', eyebrow: '赛后不可变快照' })
const emit = defineEmits<{ close: []; notice: [message: string] }>()

const deck = computed<SavedL12Deck | null>(() => {
  if (!props.masterId.trim()) return null
  const expand = (section: string) => props.entries.filter(card => (card.section || 'main') === section)
    .flatMap(card => Array.from({ length: Math.max(0, Math.floor(card.quantity)) }, () => card.cardId))
  return {
    name: (props.deckName || props.title).slice(0, 24), masterId: props.masterId,
    cardIds: expand('main'), moraleIds: expand('morale'), specialIds: expand('special'), updatedAt: new Date().toISOString(),
  }
})
function uniqueDeckName(base: string) {
  const saved = loadSavedDecks()
  if (!saved[base]) return base
  let index = 2
  let name = `${base} ${index}`.slice(0, 24)
  while (saved[name]) name = `${base} ${++index}`.slice(0, 24)
  return name
}
async function copyCode() {
  if (!deck.value) return
  try { await navigator.clipboard.writeText(encodeDeckCode(deck.value)); emit('notice', '牌库码已复制') }
  catch { emit('notice', '当前浏览器无法复制牌库码') }
}
async function exportImage() {
  if (!deck.value) return
  try { await downloadDeckImage(deck.value, props.catalog); emit('notice', '牌库图已导出') }
  catch (error) { emit('notice', error instanceof Error ? error.message : '牌库图导出失败') }
}
async function copyToLibrary() {
  if (!deck.value) return
  const copy = { ...deck.value, name: uniqueDeckName(deck.value.name), updatedAt: new Date().toISOString() }
  try { const saved = await saveDeck(copy); emit('notice', `已复制《${saved.name}》到我的牌库`) }
  catch (error) { emit('notice', error instanceof Error ? error.message : '复制到我的牌库失败') }
}
</script>

<template>
  <Teleport to="body">
    <div class="deck-snapshot-viewer" data-ui-contract="shared-deck-snapshot-viewer" @click.self="emit('close')">
      <section class="deck-viewer-modal" role="dialog" aria-modal="true" :aria-label="title">
        <header><div><small>{{ eyebrow }}</small><h2>{{ title }}</h2></div><button type="button" aria-label="关闭构筑" @click="emit('close')">×</button></header>
        <DeckConstructionBrowser :entries="entries" :catalog="catalog" :title="title"/>
        <footer class="deck-viewer-actions"><button :disabled="!deck" @click="copyCode">复制牌库码</button><button :disabled="!deck" @click="exportImage">导出牌库图</button><button class="primary" :disabled="!deck" @click="copyToLibrary">复制到我的牌库</button><small v-if="!deck">快照缺少主宰，无法生成可复用牌库。</small></footer>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.deck-snapshot-viewer{position:fixed;z-index:5000;inset:0;display:grid;place-items:center;padding:20px;background:#010407d9;backdrop-filter:blur(8px);font-family:'Microsoft YaHei','微软雅黑',sans-serif;color:#fff}.deck-viewer-modal{box-sizing:border-box;width:min(1000px,95vw);max-height:92vh;overflow:auto;padding:18px;border:1px solid #67747a;background:#101821;box-shadow:0 24px 70px #000}.deck-viewer-modal>header{display:flex;align-items:center;justify-content:space-between;padding-bottom:12px;border-bottom:1px solid #35424a}.deck-viewer-modal h2{margin:4px 0}.deck-viewer-modal>header small{color:#d5b85e;font-size:14px;font-weight:900}.deck-viewer-modal>header button{width:36px;height:36px;border:1px solid #536068;background:#080e13;color:#fff;font-size:22px}.deck-viewer-modal>.construction-browser{margin-top:12px}.deck-viewer-actions{display:flex;justify-content:flex-end;gap:8px;flex-wrap:wrap;padding-top:12px;border-top:1px solid #35424a}.deck-viewer-actions button{padding:10px 13px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.deck-viewer-actions button.primary{border-color:#b79c4e;background:#2c2612;color:#f4dda0}.deck-viewer-actions button:disabled{cursor:not-allowed;opacity:.4}.deck-viewer-actions small{flex:1 1 100%;color:#d98f99;text-align:right}
@media(max-width:520px){.deck-snapshot-viewer{padding:8px}.deck-viewer-modal{width:100%;max-height:96vh;padding:12px}.deck-viewer-actions button{flex:1 1 100%}}
</style>
