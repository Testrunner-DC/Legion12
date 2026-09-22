<script setup lang="ts">
import { computed, ref } from 'vue'
import CardTile from '../CardTile.vue'
import type { Card, PlayerView } from '../types'
import { landscapeTeleportTarget } from '../mobileViewport'

const props = defineProps<{ players: PlayerView[]; initialPlayer: number; ownPlayerIndex: number; canActivateOsiris?: boolean; inspectionOnly?: boolean; mobileLayout?: boolean }>()
const emit = defineEmits<{ close: []; focus: [card: Card]; inspect: [card: Card]; ability: [card: Card, ability: string] }>()
const minimized = ref(false)
const player = computed(() => props.players.find(item => item.playerIndex === props.initialPlayer) ?? props.players[0])

function selectCard(card: Card) {
  emit('focus', card)
  if (!props.mobileLayout) emit('inspect', card)
  if (props.inspectionOnly) return
  if (player.value.playerIndex !== props.ownPlayerIndex) return
  const enabledAbilities = card.abilities?.filter(ability => ability.enabled !== false) ?? []
  if (enabledAbilities.length === 1) emit('ability', card, enabledAbilities[0].id)
}
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()">
    <div class="graveyard-overlay" :class="{ minimized, 'mobile-safe-overlay': mobileLayout }" @click.self="emit('close')">
      <section v-if="minimized" class="graveyard-minimized"><button type="button" @click="minimized = false">恢复墓地</button></section>
      <section v-else class="graveyard-window">
        <header>
          <div><small>PUBLIC ZONE</small><h2>{{ player.name }}的墓地</h2></div>
          <button aria-label="最小化墓地" title="最小化以查看场面" @click="minimized = true">—</button>
          <button aria-label="关闭墓地" @click="emit('close')">×</button>
        </header>
        <div class="graveyard-columns single">
          <article>
            <h3>{{ player.name }} <span>{{ player.graveyard?.length ?? player.graveyardCount ?? 0 }} 张</span></h3>
            <div class="graveyard-cards">
              <div v-for="card in [...(player.graveyard || [])].reverse()" :key="card.instanceId" class="graveyard-card-entry">
                <CardTile :card="card" @mouseenter="emit('focus', card)" @select="selectCard(card)" />
                <span class="graveyard-card-name">{{ card.name }}</span>
                <button v-if="!inspectionOnly && player.playerIndex === ownPlayerIndex && canActivateOsiris && card.cardId === 'S01-02M2'"
                  class="osiris-victory" @mouseenter="emit('focus', card)" @click.stop="emit('ability', card, 'isisVictory')">特殊胜利</button>
              </div>
              <p v-if="!player.graveyard?.length">墓地为空</p>
            </div>
          </article>
        </div>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.graveyard-card-entry{position:relative}.graveyard-card-name{display:block;color:#f1f0e9;font-size:var(--l12-board-copy,13px);font-weight:900;line-height:1.3;text-align:center;overflow-wrap:anywhere}.osiris-victory{position:absolute;z-index:4;left:50%;bottom:30px;transform:translateX(-50%);padding:4px 7px;border:1px solid #79e2a2;background:#0a2f20;color:#ddffea;font-size:var(--l12-board-copy,13px);font-weight:900;white-space:nowrap;box-shadow:0 0 12px rgba(80,220,132,.6)}
.graveyard-overlay.mobile-safe-overlay{z-index:2147483605;inset:var(--l12-viewport-top,0px) auto auto var(--l12-viewport-left,0px);box-sizing:border-box;width:var(--l12-viewport-width,100vw);height:var(--l12-viewport-height,100vh);padding:8px}.graveyard-overlay.mobile-safe-overlay .graveyard-window{box-sizing:border-box;width:100%;max-height:100%;overflow:auto}
.graveyard-overlay.mobile-safe-overlay.minimized{inset:auto calc(100vw - var(--l12-viewport-left,0px) - var(--l12-viewport-width,100vw) + 110px) calc(100vh - var(--l12-viewport-top,0px) - var(--l12-viewport-height,100vh) + var(--l12-mobile-hand-h,64px) + 5px) auto;width:auto;height:auto;padding:0;background:transparent;pointer-events:none}.graveyard-minimized{pointer-events:auto}.graveyard-minimized button{min-height:32px;padding:5px 10px;border:1px solid #70d7df;background:#174e54;color:#fff;font-weight:900;box-shadow:0 8px 24px #000}
</style>
