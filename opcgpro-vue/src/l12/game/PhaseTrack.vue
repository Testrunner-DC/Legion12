<script setup lang="ts">
import type { Phase } from '../types'
withDefaults(defineProps<{ phase: Phase; round: number; activeSide: 'my' | 'opponent'; vertical?: boolean }>(), { vertical: false })
const phases: Array<{ key: Phase; lines: string[] }> = [
  { key: 'Disaster', lines: ['触发', '天灾'] }, { key: 'Reset', lines: ['重置', '阶段'] },
  { key: 'Draw', lines: ['抽牌', '阶段'] }, { key: 'Morale', lines: ['士气', '阶段'] },
  { key: 'Main', lines: ['主要', '阶段'] }, { key: 'End', lines: ['结束', '阶段'] },
]
</script>

<template>
  <div class="l12-phase-track" :class="[`active-${activeSide}`, { vertical }]">
    <span v-if="phase === 'Initiative'" class="active"><em>先后</em><em>手</em></span>
    <span v-else-if="phase === 'DisasterPreparation'" class="active"><em>天灾</em><em>准备</em></span>
    <span v-for="item in phases" :key="item.key" :class="{ active: phase === item.key }">
      <em v-for="line in item.lines" :key="line">{{ line }}</em>
    </span>
    <span class="round"><em>TURN</em><b>{{ round }}</b></span>
  </div>
</template>

<style scoped>
.l12-phase-track.vertical{position:relative;inset:auto;display:grid;min-width:0;align-content:center;align-items:stretch;gap:7px;padding:5px 3px;border-radius:2px;transform:none}
.l12-phase-track.vertical span{box-sizing:border-box;min-width:0;min-height:62px;flex-direction:column;justify-content:center;gap:1px;padding:6px 2px;line-height:1.28;text-align:center;white-space:normal;writing-mode:horizontal-tb;transform:none}
.l12-phase-track.vertical span em{display:block;font-style:normal;white-space:nowrap}
.l12-phase-track.vertical span.round{margin-top:3px;gap:2px}
.l12-phase-track.vertical span.round b{font-size:inherit;line-height:1}
</style>
