<script setup lang="ts">
import { computed } from 'vue'
import CardTile from '../CardTile.vue'
import type { Card, PlayerView } from '../types'

const props = defineProps<{ players: PlayerView[]; initialPlayer: number }>()
const emit = defineEmits<{ close: []; focus: [card: Card] }>()
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
              <CardTile v-for="card in [...(player.graveyard || [])].reverse()" :key="card.instanceId" :card="card"
                @mouseenter="emit('focus', card)" @select="emit('focus', card)" />
              <p v-if="!player.graveyard?.length">墓地为空</p>
            </div>
          </article>
        </div>
      </section>
    </div>
  </Teleport>
</template>
