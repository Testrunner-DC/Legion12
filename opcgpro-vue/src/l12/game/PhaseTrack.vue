<script setup lang="ts">
import type { Phase } from '../types'
defineProps<{ phase: Phase; round: number; activeSide: 'my' | 'opponent' }>()
const phases: Array<{ key: Phase; label: string }> = [
  { key: 'Disaster', label: '触发天灾' }, { key: 'Reset', label: '重置阶段' },
  { key: 'Draw', label: '抽牌阶段' }, { key: 'Morale', label: '士气阶段' },
  { key: 'Main', label: '主要阶段' }, { key: 'End', label: '结束阶段' },
]
</script>

<template>
  <div class="l12-phase-track" :class="`active-${activeSide}`">
    <span v-if="phase === 'Initiative'" class="active"><i>准</i>选择先后手</span>
    <span v-else-if="phase === 'DisasterPreparation'" class="active"><i>灾</i>天灾准备</span>
    <span class="round">TURN {{ round }}</span>
    <span v-for="(item, index) in phases" :key="item.key" :class="{ active: phase === item.key }">
      <i>{{ index + 1 }}</i>{{ item.label }}
    </span>
  </div>
</template>
