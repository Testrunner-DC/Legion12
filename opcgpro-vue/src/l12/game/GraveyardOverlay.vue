<script setup lang="ts">
import { computed } from 'vue'
import CardTile from '../CardTile.vue'
import type { Card, PlayerView } from '../types'

const props = defineProps<{ players: PlayerView[]; initialPlayer: number; ownPlayerIndex: number; canActivateOsiris?: boolean }>()
const emit = defineEmits<{ close: []; focus: [card: Card]; ability: [card: Card, ability: string] }>()
const player = computed(() => props.players.find(item => item.playerIndex === props.initialPlayer) ?? props.players[0])
</script>

<template>
  <Teleport to="body">
    <div class="graveyard-overlay" @click.self="emit('close')">
      <section class="graveyard-window">
        <header>
          <div><small>PUBLIC ZONE</small><h2>{{ player.name }}的墓地</h2></div>
          <button aria-label="关闭墓地" @click="emit('close')">×</button>
        </header>
        <div class="graveyard-columns single">
          <article>
            <h3>{{ player.name }} <span>{{ player.graveyard?.length ?? player.graveyardCount ?? 0 }} 张</span></h3>
            <div class="graveyard-cards">
              <div v-for="card in [...(player.graveyard || [])].reverse()" :key="card.instanceId" class="graveyard-card-entry">
                <CardTile :card="card" @mouseenter="emit('focus', card)" @select="emit('focus', card)" />
                <button v-if="player.playerIndex === ownPlayerIndex && canActivateOsiris && card.cardId === 'S01-02M2'"
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
.graveyard-card-entry{position:relative}.osiris-victory{position:absolute;z-index:4;left:50%;bottom:5px;transform:translateX(-50%);padding:4px 7px;border:1px solid #79e2a2;background:#0a2f20;color:#ddffea;font-size:9px;font-weight:900;white-space:nowrap;box-shadow:0 0 12px rgba(80,220,132,.6)}
</style>
