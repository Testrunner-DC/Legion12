<script setup lang="ts">
import { computed } from 'vue'
import CardImage from '../CardImage.vue'
import type { CardImageIntent } from '../cardAssets'

const props = withDefaults(defineProps<{
  cardId?: string
  legacyUrl?: string
  name: string
  meta?: string
  horizontal?: boolean
  selected?: boolean
  unavailable?: boolean
  intent?: CardImageIntent
  size?: 'compact' | 'standard' | 'featured'
  selectionOrder?: number
  removable?: boolean
  badge?: string
}>(), {
  cardId: '',
  legacyUrl: undefined,
  meta: '',
  horizontal: false,
  selected: false,
  unavailable: false,
  intent: 'thumb',
  size: 'standard',
  selectionOrder: undefined,
  removable: false,
  badge: '',
})

const emit = defineEmits<{
  focus: []
  select: []
  remove: []
}>()

function select() {
  emit('focus')
  if (!props.unavailable) emit('select')
}

const stateLabel = computed(() => [
  props.selected ? '已选择' : '',
  props.unavailable ? '当前不可选择' : '',
].filter(Boolean).join(' · '))
</script>

<template>
  <div
    class="prompt-card-candidate"
    data-ui-contract="equal-card-option"
    :class="[`size-${size}`, { horizontal, selected, unavailable }]"
    role="button"
    tabindex="0"
    :aria-disabled="unavailable"
    :aria-pressed="selected"
    :aria-label="stateLabel ? `${name}，${stateLabel}` : name"
    @mouseenter="emit('focus')"
    @focus="emit('focus')"
    @click="select"
    @keydown.enter.prevent="select"
    @keydown.space.prevent="select"
  >
    <CardImage
      v-if="cardId || legacyUrl"
      :card-id="cardId"
      :legacy-url="legacyUrl"
      :alt="name"
      :intent="intent"
      eager
    />
    <span class="prompt-card-candidate__name">{{ name }}</span>
    <small class="prompt-card-candidate__meta" :class="{ empty: !meta }" :aria-hidden="!meta">{{ meta }}</small>
    <em class="prompt-card-candidate__state" :class="{ empty: !stateLabel }" :aria-hidden="!stateLabel">{{ stateLabel }}</em>
    <strong v-if="badge" class="prompt-card-candidate__badge">{{ badge }}</strong>
    <b v-if="selectionOrder" class="prompt-card-candidate__order">{{ selectionOrder }}</b>
    <button v-if="removable" type="button" class="prompt-card-candidate__remove"
      aria-label="撤回这张牌" @click.stop="emit('remove')">撤回</button>
  </div>
</template>

<style scoped>
.prompt-card-candidate{position:relative;display:flex;box-sizing:border-box;flex:0 0 116px;width:116px;min-width:116px;height:224px;min-height:224px;max-height:224px;flex-direction:column;align-items:center;justify-content:flex-start;gap:5px;padding:5px;border:2px solid #d9d8cf;background:#101516;color:#fff;cursor:pointer;overflow:hidden}
.prompt-card-candidate:hover,.prompt-card-candidate.selected{border-color:#70d7df;background:#174e54;color:#fff}.prompt-card-candidate.unavailable{border-color:#a65b63;background:#1b1416;cursor:not-allowed}.prompt-card-candidate.unavailable:hover{background:#1b1416;box-shadow:none}.prompt-card-candidate.unavailable .l12-card-image{filter:grayscale(.85);opacity:.42}.prompt-card-candidate.unavailable .prompt-card-candidate__name{color:#d4b8bc}.prompt-card-candidate.unavailable .prompt-card-candidate__meta{color:#bd8a90}
.prompt-card-candidate .l12-card-image{width:96px;height:134px;margin:0 auto;object-fit:contain}.prompt-card-candidate__name{display:-webkit-box;box-sizing:border-box;width:100%;height:34px;min-height:34px;overflow:hidden;color:#fff;font-size:var(--l12-board-copy,13px);font-weight:900;line-height:17px;text-align:center;white-space:normal;overflow-wrap:anywhere;-webkit-box-orient:vertical;-webkit-line-clamp:2}.prompt-card-candidate__meta{display:block;width:100%;overflow:hidden;color:#8edce2;font-size:var(--l12-board-copy,13px);font-weight:800;line-height:11px;text-align:center;text-overflow:ellipsis;white-space:nowrap}
.prompt-card-candidate.horizontal{flex-basis:116px;width:116px;min-width:116px}.prompt-card-candidate.horizontal .l12-card-image{width:96px;height:134px;aspect-ratio:auto;object-fit:contain}
.prompt-card-candidate.size-compact{flex-basis:80px;width:80px;min-width:80px;height:172px;min-height:172px;max-height:172px;padding:3px;border-width:1px}.prompt-card-candidate.size-compact .l12-card-image{width:70px;height:98px}.prompt-card-candidate.size-compact .prompt-card-candidate__name{height:24px;min-height:24px;font-size:var(--l12-board-copy,13px);line-height:12px}.prompt-card-candidate.size-compact.horizontal{flex-basis:80px;width:80px;min-width:80px}.prompt-card-candidate.size-compact.horizontal .l12-card-image{width:70px;height:98px;aspect-ratio:auto;object-fit:contain}
.prompt-card-candidate.size-featured{flex-basis:min(616px,calc(100vw - 100px));width:min(616px,calc(100vw - 100px));max-width:616px;height:auto;min-height:0;max-height:none;overflow:visible}.prompt-card-candidate.size-featured .l12-card-image{width:min(588px,calc(100vw - 140px));height:min(368px,48vh)}.prompt-card-candidate.size-featured.horizontal .l12-card-image{width:min(588px,calc(100vw - 140px));height:auto;max-height:48vh;aspect-ratio:8/5}
.prompt-card-candidate__order{position:absolute;right:3px;top:3px;display:grid;min-width:20px;height:20px;padding:0 4px;place-items:center;border-radius:50%;background:#70d7df;color:#071012;font-size:var(--l12-board-copy,13px)}
.prompt-card-candidate__badge{position:absolute;left:3px;top:3px;padding:3px 6px;border:1px solid #f2d56d;background:rgba(20,14,2,.96);color:#ffe78d;font-size:var(--l12-board-copy,13px);line-height:1;box-shadow:0 2px 8px rgba(0,0,0,.65)}
.prompt-card-candidate__meta.empty,.prompt-card-candidate__state.empty{visibility:hidden}.prompt-card-candidate__state{display:block;box-sizing:border-box;min-height:1.45em;max-width:100%;margin:0;padding:2px 4px;border:1px solid #70d7df;background:#0b3034;color:#d8ffff;font-size:var(--l12-board-micro,9px);font-style:normal;font-weight:900;line-height:1.2;text-align:center;overflow-wrap:anywhere}.prompt-card-candidate.unavailable .prompt-card-candidate__state{border-color:#b95f68;background:#411d23;color:#ffe0e3}.prompt-card-candidate.selected.unavailable .prompt-card-candidate__state{border-color:#e4bd58;background:#443711;color:#fff1b5}
.prompt-card-candidate__remove{position:absolute;right:2px;top:2px;padding:2px 4px;border:0;background:#8c2931;color:#fff;font-size:var(--l12-board-copy,13px);font-style:normal}
</style>
