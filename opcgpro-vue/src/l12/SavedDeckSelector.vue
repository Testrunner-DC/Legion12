<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import DeckProfile from './DeckProfile.vue'
import { deckCountSummary, validateDeck, type DeckCard, type L12DeckSelectionScope,
  type SavedL12Deck } from './decks'
import type { OperationsCardRestriction } from './platform'

const props = withDefaults(defineProps<{
  open: boolean
  mode: L12DeckSelectionScope
  decks: SavedL12Deck[]
  catalog: DeckCard[]
  currentDeckName?: string
  restrictions?: readonly OperationsCardRestriction[]
  loading?: boolean
  disabled?: boolean
}>(), {
  currentDeckName: '',
  restrictions: () => [],
  loading: false,
  disabled: false,
})

const emit = defineEmits<{
  cancel: []
  confirm: [deck: SavedL12Deck]
}>()

const draftName = ref('')
watch(() => props.open, open => {
  if (open) draftName.value = props.currentDeckName
}, { immediate: true })

const modeLabel = computed(() => ({
  ranked: '排位匹配',
  casual: '休闲匹配',
  friendly: '好友房',
  'sandbox-player': '沙盒 · 我方',
  'sandbox-opponent': '沙盒 · 对手',
})[props.mode])
const byId = computed(() => new Map(props.catalog.map(card => [card.id, card])))
const rows = computed(() => props.decks.map(deck => ({
  deck,
  error: props.loading ? '正在加载牌库规则' : validateDeck(deck, props.catalog, props.restrictions),
})))
const usableCount = computed(() => rows.value.filter(row => !row.error).length)
const selected = computed(() => rows.value.find(row => row.deck.name === draftName.value))

function cancel() {
  draftName.value = props.currentDeckName
  emit('cancel')
}

function confirm() {
  if (props.disabled || !selected.value || selected.value.error) return
  emit('confirm', selected.value.deck)
}
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="saved-deck-selector-mask" data-ui-contract="l12-saved-deck-selector"
      @click.self="cancel">
      <section class="saved-deck-selector" role="dialog" aria-modal="true" :aria-label="`${modeLabel}更换牌库`">
        <header>
          <div><small>SAVED DECKS · {{ modeLabel }}</small><h2>更换牌库</h2>
            <p>这里只选择已保存牌库；确认前不会改变当前模式。</p></div>
          <button type="button" aria-label="取消更换牌库" @click="cancel">×</button>
        </header>

        <div v-if="!decks.length" class="selector-empty">
          <b>没有已保存牌库</b><span>请先在牌库页建立并保存牌库，再返回此大厅选择。</span>
        </div>
        <div v-else class="selector-list">
          <button v-for="row in rows" :key="row.deck.name" type="button"
            :class="{ selected: draftName === row.deck.name, invalid: !!row.error }"
            :disabled="!!row.error || disabled" :aria-pressed="draftName === row.deck.name"
            @click="draftName = row.deck.name">
            <DeckProfile compact :master-id="row.deck.masterId" :master-name="byId.get(row.deck.masterId)?.nameZh"
              :fallback-url="byId.get(row.deck.masterId)?.imageUrl" :name="row.deck.name"
              :meta="`${deckCountSummary(row.deck.cardIds, byId).label} 张主牌 · ${row.deck.moraleIds.length} 张士气`"
              :selected="draftName === row.deck.name"/>
            <span class="legality" :class="{ valid: !row.error }">{{ row.error || '符合当前模式规则' }}</span>
          </button>
        </div>

        <p v-if="decks.length && !usableCount" class="no-usable-deck">
          当前模式没有可用牌库。请根据上方原因调整构筑或切换模式。
        </p>
        <footer>
          <span>{{ usableCount }} / {{ decks.length }} 副可用</span>
          <button type="button" @click="cancel">取消</button>
          <button class="confirm" type="button" :disabled="disabled || !selected || !!selected.error" @click="confirm">确认使用</button>
        </footer>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.saved-deck-selector-mask{position:fixed;z-index:120;inset:0;display:grid;place-items:center;padding:20px;background:rgba(1,4,6,.82);backdrop-filter:blur(8px)}
.saved-deck-selector{display:flex;width:min(760px,96vw);max-height:min(760px,92vh);flex-direction:column;overflow:hidden;border:1px solid #596770;background:#101821;box-shadow:0 30px 90px #000b;color:#f1eee5;font-family:'Microsoft YaHei','微软雅黑',sans-serif}
.saved-deck-selector>header{display:flex;align-items:flex-start;justify-content:space-between;gap:18px;padding:22px;border-bottom:1px solid #354149}.saved-deck-selector small{color:#55c6cd;font:900 9px monospace;letter-spacing:.18em}.saved-deck-selector h2{margin:5px 0 4px;font-size:24px}.saved-deck-selector header p{margin:0;color:#829097;font-size:11px}.saved-deck-selector header>button{width:34px;height:34px;border:1px solid #4c5a63;background:#0a1117;color:#fff;font-size:20px}
.selector-list{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;padding:18px;overflow:auto}.selector-list>button{display:grid;min-width:0;padding:0;border:1px solid #39464f;background:#0a1117;color:#fff;text-align:left}.selector-list>button:not(:disabled):hover,.selector-list>button.selected{border-color:#e1c36f}.selector-list>button.invalid{opacity:.56;cursor:not-allowed}.selector-list :deep(.deck-profile){width:100%;border:0;background:transparent}.legality{display:block;min-height:30px;padding:8px 12px;border-top:1px solid #303b42;color:#e79ca3;font-size:10px;line-height:1.45}.legality.valid{color:#7ed4ad}.selector-empty{display:grid;min-height:260px;place-items:center;align-content:center;gap:8px;padding:30px;color:#7e8b92;text-align:center}.selector-empty b{color:#d9dde0}.selector-empty span{font-size:11px}.no-usable-deck{margin:0 18px 14px;padding:10px;border-left:3px solid #b53a47;background:#241118;color:#efb0b6;font-size:11px}.saved-deck-selector>footer{display:flex;align-items:center;justify-content:flex-end;gap:9px;padding:15px 18px;border-top:1px solid #354149}.saved-deck-selector footer>span{margin-right:auto;color:#7e8b92;font-size:10px}.saved-deck-selector footer button{padding:10px 15px;border:1px solid #52606a;background:#121b23;color:#fff;font-weight:900}.saved-deck-selector footer .confirm{border-color:#e0c16c;background:#e0c16c;color:#090d0f}.saved-deck-selector footer button:disabled{opacity:.38;cursor:not-allowed}
@media(max-width:640px){.saved-deck-selector-mask{padding:10px}.selector-list{grid-template-columns:1fr}.saved-deck-selector>footer{flex-wrap:wrap}.saved-deck-selector footer>span{width:100%;margin-right:0}}
</style>
