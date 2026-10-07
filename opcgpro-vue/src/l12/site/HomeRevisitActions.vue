<script setup lang="ts">
import { computed } from 'vue'
import { authState, platformState } from '@/l12/platform'
import { l12State } from '@/l12/net'
import { homeAccountVerified, homeCanContinueGame } from './homeRevisit'

const verifiedAccount = computed(() => homeAccountVerified(platformState.account?.id,
  platformState.token, authState.verified) && !platformState.account?.disabled && !platformState.account?.deleted)
const canContinue = computed(() => homeCanContinueGame(platformState.account?.id, verifiedAccount.value, l12State))
</script>

<template>
  <nav v-if="canContinue" class="home-revisit-actions" aria-label="继续你的旅程">
    <router-link to="/game" class="home-revisit-action continue-game">
      <strong>{{ l12State.spectating ? '继续观战' : '继续对局' }}</strong><span>返回正在进行的对局</span>
    </router-link>
  </nav>
</template>

<style scoped>
.home-revisit-actions{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(260px,100%),1fr));gap:12px;padding:22px clamp(14px,5vw,78px);border-bottom:1px solid #293941;background:#0e171d;font-family:var(--l12-ui-font,'Microsoft YaHei','微软雅黑',sans-serif)}
.home-revisit-action{display:flex;min-width:0;min-height:74px;box-sizing:border-box;flex-direction:column;justify-content:center;gap:6px;padding:12px 16px;border:1px solid #34454e;color:#eef1ed;text-decoration:none;background:#121e25}
.home-revisit-action strong{font-size:17px;line-height:1.3}.home-revisit-action span{min-width:0;max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#a8b8bf;font-size:13px;line-height:1.45}
.home-revisit-action:hover,.home-revisit-action:focus-visible{border-color:#64c8ce;background:#182b33}.home-revisit-action:focus-visible{outline:2px solid #64c8ce;outline-offset:3px}.continue-game{border-color:#64c8ce}
@media(max-width:520px){.home-revisit-actions{gap:8px;padding:16px 14px}.home-revisit-action{min-height:68px}}
</style>
