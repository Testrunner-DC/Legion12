<script setup lang="ts">
import { masterProfileUrl } from './specialAssets'

withDefaults(defineProps<{
  masterId?: string
  masterName?: string
  name?: string
  meta?: string
  context?: string
  fallbackUrl?: string
  compact?: boolean
  selected?: boolean
}>(), {
  masterId: '', masterName: '', name: '', meta: '', context: '', fallbackUrl: '', compact: false, selected: false,
})
</script>

<template>
  <div class="deck-profile" :class="{ compact, selected, empty: !masterId }" data-deck-profile>
    <img v-if="masterId" class="deck-profile__portrait" :src="masterProfileUrl(masterId, fallbackUrl)" :alt="masterName || '主宰 Profile'"/>
    <span v-else class="deck-profile__portrait deck-profile__placeholder">库</span>
    <span class="deck-profile__copy">
      <small v-if="context">{{ context }}</small>
      <b>{{ name || '尚未选择牌库' }}</b>
      <span v-if="masterName">{{ masterName }}</span>
      <em v-if="meta">{{ meta }}</em>
    </span>
  </div>
</template>

<style scoped>
.deck-profile{display:grid;grid-template-columns:64px minmax(0,1fr);align-items:center;gap:12px;min-width:0;padding:10px;border:1px solid #3b484f;background:#0b1218;color:#f3f0e8;text-align:left}.deck-profile.selected{border-color:#e0c16d;background:#202017;box-shadow:inset 3px 0 #e0c16d}.deck-profile__portrait{display:block;width:64px;aspect-ratio:1;object-fit:cover;border:1px solid #65716f;border-radius:2px;background:#111b20}.deck-profile__placeholder{display:grid;place-items:center;background:linear-gradient(145deg,#6e1825,#13252a);font-size:20px;font-weight:900}.deck-profile__copy,.deck-profile__copy>*{display:block;min-width:0}.deck-profile__copy b,.deck-profile__copy span,.deck-profile__copy em{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.deck-profile__copy b{font-size:14px}.deck-profile__copy small{margin-bottom:4px;color:#65c7cd;font-size:8px;font-weight:900;letter-spacing:.08em}.deck-profile__copy span{margin-top:4px;color:#c9d0cc;font-size:10px}.deck-profile__copy em{margin-top:3px;color:#7f8c91;font-size:9px;font-style:normal}.deck-profile.compact{grid-template-columns:38px minmax(0,1fr);gap:8px;padding:7px}.deck-profile.compact .deck-profile__portrait{width:38px}.deck-profile.compact .deck-profile__copy b{font-size:11px}.deck-profile.compact .deck-profile__copy span,.deck-profile.compact .deck-profile__copy em{font-size:8px}
</style>
