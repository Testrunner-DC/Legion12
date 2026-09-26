<script setup lang="ts">
import { computed } from 'vue'

export interface MasterMatchupMatrixMaster {
  id: string
  name: string
  imageUrl?: string
  rank?: number
  winRate?: number | null
}
export interface MasterMatchupMatrixCell {
  masterId: string
  opponentMasterId: string
  samples: number
  winRate?: number | null
  firstWins?: number
  firstSamples?: number
  secondWins?: number
  secondSamples?: number
}

const props = withDefaults(defineProps<{
  masters: MasterMatchupMatrixMaster[]
  cells: MasterMatchupMatrixCell[]
  minimumSample?: number
  emptyText?: string
}>(), { minimumSample: 0, emptyText: '当前范围暂无对阵数据' })

const columnWidth = '银臂努阿达'.length * 14 + 44
const cellIndex = computed(() => new Map(props.cells.map(cell => [`${cell.masterId}|${cell.opponentMasterId}`, cell])))
function cell(masterId: string, opponentMasterId: string) { return cellIndex.value.get(`${masterId}|${opponentMasterId}`) }
function rate(value?: number | null) { return typeof value === 'number' ? `${(value * 100).toFixed(1)}%` : '—' }
function cellTone(masterId: string, opponentMasterId: string) {
  if (masterId === opponentMasterId) return 'mirror'
  const matchup = cell(masterId, opponentMasterId)
  if (!matchup || matchup.samples < props.minimumSample) return 'no-data'
  const value = matchup.winRate
  if (typeof value !== 'number') return 'no-data'
  return value > .5 ? 'advantage' : value < .5 ? 'disadvantage' : 'even'
}
function cellTitle(masterId: string, opponentMasterId: string) {
  if (masterId === opponentMasterId) return '同主宰镜像'
  const value = cell(masterId, opponentMasterId)
  if (!value) return '暂无对局'
  const split = typeof value.firstSamples === 'number' && typeof value.secondSamples === 'number'
    ? `；先手 ${value.firstWins ?? 0}/${value.firstSamples}；后手 ${value.secondWins ?? 0}/${value.secondSamples}` : ''
  return `共 ${value.samples} 场${split}`
}
</script>

<template>
  <div v-if="masters.length" class="master-matchup-matrix" data-ui-contract="shared-master-matchup-matrix">
    <div class="matrix-grid" :style="{ gridTemplateColumns: `64px repeat(${masters.length + 1}, ${columnWidth}px)` }">
      <div class="matrix-rank-head">排名</div>
      <div class="matrix-corner">我方 ↓<br>对方 →</div>
      <div v-for="master in masters" :key="`head-${master.id}`" class="matrix-head">
        <img class="matrix-master-avatar" data-ui-contract="ranking-master-avatar" :src="master.imageUrl" :alt="`${master.name}头像`"/>
        <span>{{ master.name }}</span>
      </div>
      <template v-for="(master, index) in masters" :key="`row-${master.id}`">
        <div class="matrix-rank-cell"><b>#{{ master.rank ?? index + 1 }}</b><span>{{ rate(master.winRate) }}</span></div>
        <div class="matrix-row-head"><img class="matrix-master-avatar" data-ui-contract="ranking-master-avatar" :src="master.imageUrl" :alt="`${master.name}头像`"/><b>{{ master.name }}</b></div>
        <div v-for="opponent in masters" :key="`${master.id}-${opponent.id}`" class="matrix-cell" :class="cellTone(master.id, opponent.id)" :title="cellTitle(master.id, opponent.id)">
          <template v-if="master.id === opponent.id"><b>镜像</b></template>
          <template v-else-if="cell(master.id, opponent.id)">
            <b>{{ cell(master.id, opponent.id)!.samples >= minimumSample ? rate(cell(master.id, opponent.id)!.winRate) : '—' }}</b>
            <span>{{ cell(master.id, opponent.id)!.samples }} 场</span>
          </template>
          <template v-else><b>等待</b><span>更多对局</span></template>
        </div>
      </template>
    </div>
  </div>
  <div v-else class="matrix-empty">{{ emptyText }}</div>
</template>

<style scoped>
.master-matchup-matrix{max-height:68vh;overflow:auto}.matrix-grid{display:grid;grid-auto-rows:76px;width:max-content;min-width:100%}.matrix-rank-head,.matrix-rank-cell,.matrix-corner,.matrix-head,.matrix-row-head,.matrix-cell{box-sizing:border-box;height:76px;min-height:76px;max-height:76px;overflow:hidden;border-right:1px solid #27343e;border-bottom:1px solid #27343e}.matrix-rank-head{position:sticky;z-index:7;top:0;left:0;display:grid;place-items:center;background:#101b27;color:#758994;font-size:14px}.matrix-corner{position:sticky;z-index:6;top:0;left:64px;display:grid;place-items:center;background:#101b27;color:#758994;font-size:14px}.matrix-head{position:sticky;z-index:4;top:0;display:flex;align-items:center;flex-direction:column;justify-content:center;gap:5px;background:#101b27}.matrix-master-avatar{box-sizing:border-box;width:44px;height:44px;min-width:44px;max-width:44px;flex:0 0 44px;border:1px solid #58666e;border-radius:0;background:#080d11;object-fit:cover}.matrix-head span,.matrix-row-head b{max-width:calc(100% - 8px);overflow:hidden;color:#c3ccd0;font-size:14px;text-overflow:ellipsis;white-space:nowrap}.matrix-rank-cell{position:sticky;z-index:5;left:0;display:flex;align-items:center;flex-direction:column;justify-content:center;gap:3px;background:#0d1720}.matrix-rank-cell b{color:#e7c864}.matrix-rank-cell span{color:#829098}.matrix-row-head{position:sticky;z-index:3;left:64px;display:flex;align-items:center;flex-direction:column;justify-content:center;gap:5px;padding:3px;background:#101b27}.matrix-cell{display:flex;align-items:center;flex-direction:column;justify-content:center;gap:3px;background:#101923}.matrix-cell b{font-size:14px}.matrix-cell span{color:#8a989e;font-size:14px}.matrix-cell.advantage{background:#0b352d}.matrix-cell.advantage b{color:#62e6b4}.matrix-cell.disadvantage{background:#36131e}.matrix-cell.disadvantage b{color:#ff8494}.matrix-cell.even{background:#2d2b17}.matrix-cell.even b{color:#ead56e}.matrix-cell.mirror{background:#121923;color:#53636c}.matrix-cell span,.matrix-cell b{max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.matrix-empty{display:grid;min-height:280px;place-items:center;color:#738088}
@media(max-width:700px){.master-matchup-matrix{max-height:72vh}}
@media(max-width:520px){.master-matchup-matrix{max-height:64vh}.matrix-grid{grid-auto-rows:52px}.matrix-rank-head,.matrix-rank-cell,.matrix-corner,.matrix-head,.matrix-row-head,.matrix-cell{height:52px;min-height:52px;max-height:52px}.matrix-master-avatar{width:28px;height:28px;min-width:28px;max-width:28px;flex-basis:28px}.matrix-rank-head,.matrix-corner,.matrix-head span,.matrix-row-head b,.matrix-cell b,.matrix-cell span{font-size:11px}.matrix-head span,.matrix-row-head b{max-width:78px}}
</style>
