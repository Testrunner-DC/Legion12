<script setup lang="ts">
import { computed } from 'vue'

type SpectatorView = { accountId?: string | null; name: string }

const props = defineProps<{ spectators?: readonly SpectatorView[] }>()
const visibleSpectators = computed(() => (props.spectators ?? []).slice(0, 3))
const overflowCount = computed(() => Math.max(0, (props.spectators?.length ?? 0) - visibleSpectators.value.length))
</script>

<template>
  <section v-if="spectators?.length" class="battle-spectator-summary" aria-label="正在观战的玩家">
    <header><b>正在观战</b><span>{{ spectators.length }} 人</span></header>
    <div class="battle-spectator-summary__list" role="list">
      <span v-for="(spectator, index) in visibleSpectators" :key="spectator.accountId || `${spectator.name}-${index}`" role="listitem" :title="spectator.name">{{ spectator.name }}</span>
      <span v-if="overflowCount" class="battle-spectator-summary__overflow" role="listitem" :title="`还有 ${overflowCount} 位观战玩家`">… +{{ overflowCount }}</span>
    </div>
    <small>点击好友按钮查看完整名单</small>
  </section>
</template>

<style scoped>
.battle-spectator-summary{display:grid;box-sizing:border-box;min-height:82px;align-content:center;gap:8px;padding:12px 14px;border:1px solid rgba(240,239,229,.2);background:linear-gradient(135deg,rgba(8,15,19,.95),rgba(10,26,32,.92));color:#edf0e8}.battle-spectator-summary header{display:flex;align-items:baseline;justify-content:space-between;gap:8px}.battle-spectator-summary header b{color:#e3c86e;font-size:var(--l12-board-copy,13px);font-weight:900}.battle-spectator-summary header span,.battle-spectator-summary small{color:#91a4aa;font-size:calc(var(--l12-board-copy,13px) - 1px)}.battle-spectator-summary__list{display:flex;min-width:0;gap:5px;overflow:hidden}.battle-spectator-summary__list span{min-width:0;max-width:112px;padding:4px 7px;border:1px solid rgba(104,182,185,.42);background:rgba(17,48,55,.72);color:#d9eeec;font-size:calc(var(--l12-board-copy,13px) - 1px);overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.battle-spectator-summary__list .battle-spectator-summary__overflow{flex:none;border-color:rgba(226,195,105,.55);background:rgba(58,47,21,.72);color:#f0d88b}.battle-spectator-summary small{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
@media(max-width:700px){.battle-spectator-summary{display:none}}
</style>
