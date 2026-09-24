<script setup lang="ts">
import type { PlayerView } from '../types'
import RankedIdentityBadge from '../RankedIdentityBadge.vue'

defineProps<{
  sideLabel: string
  player: PlayerView
  rank?: number | null
  tierLabel: string
  placementTitle: string
  masterTitle: string
  faction: string
  factionLabel: string
  connectionLabel: string
  connected: boolean | null
  showMasterDetails?: boolean
}>()
</script>

<template>
  <article class="battle-player-identity" :class="{ 'is-mine': sideLabel === '我方' }">
    <header class="battle-player-identity__name">
      <b>{{ sideLabel }}</b>
      <strong :title="player.name || '未命名玩家'">{{ player.name || '未命名玩家' }}</strong>
    </header>
    <dl class="battle-player-identity__facts">
      <div v-if="rank" class="identity-rank-row"><dt>名次</dt><dd>第 {{ rank }} 名</dd></div>
      <div v-if="tierLabel || placementTitle" class="identity-tier-row">
        <dt>段位</dt>
        <dd>
          <RankedIdentityBadge v-if="tierLabel" variant="tier" :faction="faction" :label="tierLabel" compact />
          <RankedIdentityBadge v-if="placementTitle" variant="faction-title" :faction="faction" :label="placementTitle" compact />
        </dd>
      </div>
      <div v-if="masterTitle" class="identity-master-title-row">
        <dt>主宰称号</dt>
        <dd><RankedIdentityBadge variant="master-title" :faction="faction" :label="masterTitle" compact /></dd>
      </div>
      <div v-if="showMasterDetails" class="identity-master-row">
        <dt>主宰</dt>
        <dd><strong>{{ player.master.masterName || '未知主宰' }}</strong><span>{{ factionLabel }} · {{ player.master.hp }}/{{ player.master.maxHp }}</span></dd>
      </div>
      <div class="identity-connection-row"><dt>连接</dt><dd class="connection-state" :class="{ online: connected }"><i/>{{ connectionLabel }}</dd></div>
    </dl>
  </article>
</template>

<style scoped>
.battle-player-identity{display:grid;min-width:0;gap:7px}.battle-player-identity__name{display:grid;min-width:0;grid-template-columns:max-content minmax(0,1fr);align-items:start;gap:6px}.battle-player-identity__name>b{color:#d2525b;font-size:12px;white-space:nowrap}.battle-player-identity.is-mine .battle-player-identity__name>b{color:#58bdc5}.battle-player-identity__name>strong{min-width:0;color:#f0eee7;font-size:14px;line-height:1.25;overflow-wrap:anywhere}.battle-player-identity__facts{display:grid;min-width:0;gap:5px;margin:0}.battle-player-identity__facts>div{display:grid;min-width:0;grid-template-columns:58px minmax(0,1fr);align-items:start;gap:6px}.battle-player-identity dt{color:#7f8c90;font-size:11px;font-weight:900;line-height:1.45}.battle-player-identity dd{display:flex;min-width:0;flex-wrap:wrap;align-items:center;gap:4px;margin:0;color:#dce2e0;font-size:12px;line-height:1.35}.battle-player-identity dd>span{min-width:0;overflow-wrap:anywhere}.identity-rank-row dd{font-weight:900}.identity-master-row dd{display:grid;gap:1px}.identity-master-row dd strong{overflow-wrap:anywhere}.identity-master-row dd span{color:#93a09d;font-size:10px}.connection-state{display:flex!important;width:max-content!important;max-width:100%!important;align-items:center;gap:4px;color:#b76570!important;font-weight:900;white-space:normal!important}.connection-state.online{color:#58c99a!important}.connection-state i{width:6px;height:6px;flex:0 0 6px;border-radius:50%;background:currentColor;box-shadow:0 0 6px currentColor}
</style>
