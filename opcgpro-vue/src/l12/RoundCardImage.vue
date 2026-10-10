<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import CardImage from './CardImage.vue'
import { roundCardUrl } from './specialAssets'

const props = withDefaults(defineProps<{
  cardId?: string
  legacyUrl?: string
  objectPosition?: string
}>(), {
  cardId: '',
  legacyUrl: undefined,
  objectPosition: 'center 28%',
})

const dedicatedFailed = ref(false)
const dedicatedUrl = computed(() => roundCardUrl(props.cardId))
watch(() => props.cardId, () => { dedicatedFailed.value = false })
</script>

<template>
  <span class="l12-round-card-image" aria-hidden="true">
    <img v-if="dedicatedUrl && !dedicatedFailed" class="l12-round-card-image__dedicated"
      :src="dedicatedUrl" alt="" @error="dedicatedFailed = true" />
    <CardImage v-else class="l12-round-card-image__crop" :card-id="cardId" :legacy-url="legacyUrl"
      alt="" intent="thumb" fit="cover" :object-position="objectPosition" eager />
  </span>
</template>

<style scoped>
.l12-round-card-image,.l12-round-card-image__dedicated,.l12-round-card-image__crop{display:block;width:100%;height:100%;overflow:hidden;border-radius:50%;background:#090b0c}
.l12-round-card-image__dedicated{object-fit:cover;object-position:center}
.l12-round-card-image__crop :deep(.l12-card-image__img){object-fit:cover!important;object-position:var(--l12-round-card-position,center 28%)!important}
</style>
