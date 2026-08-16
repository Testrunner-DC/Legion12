<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import GameBoard from './game/GameBoard.vue'
import { l12State } from './net'

const router = useRouter()
const game = computed(() => l12State.game)
</script>

<template>
  <div v-if="game" class="game-page">
    <GameBoard :game="game" :read-only="l12State.spectating" />

    <Transition name="fade">
      <button v-if="l12State.notice" class="toast" @click="l12State.notice = ''">{{ l12State.notice }}</button>
    </Transition>

    <Transition name="fade">
      <div v-if="game.phase === 'GameOver'" class="game-over">
        <p>{{ game.winner === game.you ? '胜利' : '败北' }}</p>
        <small>MATCH {{ game.matchId.slice(0, 12) }} · REV {{ game.revision }}</small>
        <button @click="router.push('/lobby')">返回大厅</button>
      </div>
    </Transition>
  </div>
  <main v-else class="missing-game">
    <h1>对局状态尚未加载</h1>
    <button @click="router.push('/lobby')">返回大厅</button>
  </main>
</template>
