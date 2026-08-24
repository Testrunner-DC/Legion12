<script setup lang="ts">
import type { GameState, PlayerView } from '../types'

defineProps<{
  game: GameState; me: PlayerView; mode: 'play' | 'attack' | 'move' | 'freeMove' | 'cavalryMove'; selectedId: string | null;
  mulliganCount: number; defenseCount: number; defenseTargetType: string | null;
  supportId: string | null; canSupport: boolean; busy?: boolean
}>()
const emit = defineEmits<{
  command: [type: string, extra?: Record<string, unknown>]
}>()
</script>

<template>
  <div class="l12-actions">
    <template v-if="game.phase === 'Mulligan'">
      <p>选择起始手牌后确认调度</p>
      <button class="primary" :disabled="me.mulliganDone || busy" @click="emit('command', 'mulligan')">
        {{ busy ? '处理中…' : me.mulliganDone ? '等待对手' : `确认调度 (${mulliganCount})` }}
      </button>
    </template>
    <template v-else-if="game.phase === 'Defense' && game.activePlayer !== me.playerIndex && defenseTargetType === 'master'">
      <p>从手牌选择军团弃置抵挡；合计兵力须不低于进攻军团。</p>
      <button class="primary" :disabled="defenseCount === 0 || busy" @click="emit('command', 'resolveDefense')">弃置抵挡 ({{ defenseCount }})</button>
      <button class="danger" :disabled="busy" @click="emit('command', 'resolveDefense')">不抵挡 · 主宰承受伤害</button>
    </template>
    <template v-else-if="game.phase === 'Defense' && game.activePlayer !== me.playerIndex && defenseTargetType === 'legion'">
      <p v-if="canSupport">可点击被进攻军团同列的后排军团，将其选为支援军团。</p>
      <p v-else>当前没有符合兵力条件的同列后排支援军团。</p>
      <button class="primary" :disabled="!supportId || busy" @click="emit('command', 'resolveDefense')">确认支援</button>
      <button class="danger" :disabled="busy" @click="emit('command', 'resolveDefense', { supportInstanceId: null })">不支援 · 结算双方兵力</button>
    </template>
    <template v-else-if="game.activePlayer === me.playerIndex && ['Disaster','Reset','Draw','Morale','End'].includes(game.phase)">
      <p>服务器正在依次执行阶段步骤…</p>
    </template>
    <template v-else-if="game.activePlayer === me.playerIndex && game.phase === 'Main'">
      <p v-if="me.nextLegionChargeMaxCost" class="pending-effect">全军出击：本回合下一张费用不高于 {{ me.nextLegionChargeMaxCost }} 的军团获得冲锋。</p>
      <p class="card-action-hint">点击主宰查看并发动主宰效果；点击手牌或战场军团后，操作按钮会显示在卡牌上方。</p>
      <button class="danger" :disabled="busy" @click="emit('command','endTurn')">{{ busy ? '处理中…' : '结束回合' }}</button>
    </template>
    <p v-else class="waiting">等待对手操作…</p>
  </div>
</template>
