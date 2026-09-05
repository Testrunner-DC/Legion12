<script setup lang="ts">
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
</script>

<template>
  <div
    class="prompt-card-candidate"
    :class="[`size-${size}`, { horizontal, selected, unavailable }]"
    role="button"
    tabindex="0"
    :aria-disabled="unavailable"
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
    <small v-if="meta" class="prompt-card-candidate__meta">{{ meta }}</small>
    <strong v-if="badge" class="prompt-card-candidate__badge">{{ badge }}</strong>
    <b v-if="selectionOrder" class="prompt-card-candidate__order">{{ selectionOrder }}</b>
    <button v-if="removable" type="button" class="prompt-card-candidate__remove"
      aria-label="撤回这张牌" @click.stop="emit('remove')">撤回</button>
  </div>
</template>

<style scoped>
.prompt-card-candidate{position:relative;display:flex;flex:0 0 116px;width:116px;min-width:116px;min-height:0;flex-direction:column;align-items:center;justify-content:flex-start;gap:5px;padding:5px;border:2px solid #d9d8cf;background:#101516;color:#fff;cursor:pointer}
.prompt-card-candidate:hover,.prompt-card-candidate.selected{border-color:#70d7df;background:#174e54;color:#fff}.prompt-card-candidate.unavailable{border-color:#3f4442;filter:brightness(.42);cursor:not-allowed}.prompt-card-candidate.unavailable:hover{background:#101516;box-shadow:none}
.prompt-card-candidate .l12-card-image{width:96px;height:134px;margin:0 auto}.prompt-card-candidate__name{display:block;width:100%;overflow:hidden;color:#fff;font-size:11px;font-weight:900;line-height:17px;text-align:center;text-overflow:ellipsis;white-space:nowrap}.prompt-card-candidate__meta{display:block;width:100%;overflow:hidden;color:#8edce2;font-size:8px;font-weight:800;line-height:11px;text-align:center;text-overflow:ellipsis;white-space:nowrap}
.prompt-card-candidate.horizontal{flex-basis:180px;width:180px;min-width:180px}.prompt-card-candidate.horizontal .l12-card-image{width:164px;height:auto;aspect-ratio:8/5}
.prompt-card-candidate.size-compact{flex-basis:80px;width:80px;min-width:80px;padding:3px;border-width:1px}.prompt-card-candidate.size-compact .l12-card-image{width:70px;height:98px}.prompt-card-candidate.size-compact .prompt-card-candidate__name{font-size:8px;line-height:12px}.prompt-card-candidate.size-compact.horizontal{flex-basis:112px;width:112px;min-width:112px}.prompt-card-candidate.size-compact.horizontal .l12-card-image{width:102px;height:auto;aspect-ratio:8/5}
.prompt-card-candidate.size-featured{flex-basis:min(616px,calc(100vw - 100px));width:min(616px,calc(100vw - 100px));max-width:616px}.prompt-card-candidate.size-featured .l12-card-image{width:min(588px,calc(100vw - 140px));height:min(368px,48vh)}.prompt-card-candidate.size-featured.horizontal .l12-card-image{height:auto;max-height:48vh;aspect-ratio:8/5}
.prompt-card-candidate__order{position:absolute;right:3px;top:3px;display:grid;min-width:20px;height:20px;padding:0 4px;place-items:center;border-radius:50%;background:#70d7df;color:#071012;font-size:10px}
.prompt-card-candidate__badge{position:absolute;left:7px;bottom:43px;padding:3px 6px;border:1px solid #f2d56d;background:rgba(20,14,2,.92);color:#ffe78d;font-size:9px;line-height:1;box-shadow:0 2px 8px rgba(0,0,0,.65)}
.prompt-card-candidate__remove{position:absolute;right:2px;top:2px;padding:2px 4px;border:0;background:#8c2931;color:#fff;font-size:7px;font-style:normal}
</style>
