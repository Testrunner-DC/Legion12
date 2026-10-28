<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  counts: { registered: number; pendingCheckIn?: number; checkedIn: number; waitlisted: number }
  maxPlayers: number
}>()
const pending = computed(() => props.counts.pendingCheckIn
  ?? Math.max(0, props.counts.registered - props.counts.checkedIn))
</script>

<template>
  <span class="tournament-participant-counts">
    <strong>已报名 {{ counts.registered }} / {{ maxPlayers }}</strong>
    <span>待签到 {{ pending }} · 已签到 {{ counts.checkedIn }} · 候补 {{ counts.waitlisted }}</span>
  </span>
</template>

<style scoped>
.tournament-participant-counts{display:grid;min-width:0;gap:5px;line-height:1.5;font-family:inherit}.tournament-participant-counts strong,.tournament-participant-counts>span,.tournament-participant-counts small{overflow-wrap:anywhere}.tournament-participant-counts strong{font-size:14px}.tournament-participant-counts>span,.tournament-participant-counts small{font-size:12px;font-weight:400;color:var(--l12-ui-text-muted,#91a0a6)}
</style>
