<script setup lang="ts">
import { computed } from 'vue'
import type { GameState, PlayerView } from '../types'
import { l12State } from '../net'
import DefenseDecisionExplanation from './DefenseDecisionExplanation.vue'
import { mulliganCopy } from './promptPlayerCopy'

const props = defineProps<{
  game: GameState; me: PlayerView; mode: 'play' | 'attack' | 'move' | 'freeMove' | 'cavalryMove'; selectedId: string | null;
  mulliganCount: number; defenseIds: string[]; defenseTargetType: string | null;
  supportIds: string[]; canSupport: boolean; supportReady: boolean; busy?: boolean
}>()
const emit = defineEmits<{
  command: [type: string, extra?: Record<string, unknown>]
}>()
function rankedSetupLimitLabel() {
  const milliseconds = l12State.rankedClock?.operationLimitMs ?? 0
  const seconds = Math.max(0, Math.ceil(milliseconds / 1000))
  return seconds >= 60 && seconds % 60 === 0 ? `${seconds / 60} 分钟` : `${seconds} 秒`
}
const selectedBlockers = computed(() => (props.me.hand ?? []).filter(card => props.defenseIds.includes(card.instanceId)))
const selectedBlockTroops = computed(() => selectedBlockers.value.reduce((total, card) => total + card.troops, 0))
const attackValue = computed(() => props.game.pendingDefense?.attackValue ?? 0)
const blockReady = computed(() => selectedBlockers.value.length > 0 && (attackValue.value <= 0 || selectedBlockTroops.value >= attackValue.value))
</script>

<template>
  <div class="l12-actions" data-ui-contract="equal-combat-action-group">
    <template v-if="game.phase === 'Mulligan'">
      <small class="action-actor-badge" :class="me.mulliganDone ? 'opponent' : 'self'">{{ me.mulliganDone ? '对手操作' : '你的操作' }}</small>
      <p class="mulligan-role">你是{{ game.firstPlayer === me.playerIndex ? '先攻' : '后攻' }}玩家</p>
      <p>
        {{ mulliganCopy(mulliganCount, (l12State.rankedClock?.operationLimitMs ?? 0) > 0) }}<span v-if="l12State.rankedClock?.operationLimitMs && l12State.rankedClock.operationLimitMs > 0"> 限时 {{ rankedSetupLimitLabel() }}。</span>
      </p>
      <button class="primary" :disabled="me.mulliganDone || busy" @click="emit('command', 'mulligan')">
        {{ busy ? '处理中…' : me.mulliganDone ? '等待对方' : mulliganCount ? `换掉所选 ${mulliganCount} 张` : '保留全部手牌' }}
      </button>
    </template>
    <template v-else-if="(game.prompts?.[0]?.playerIndex ?? game.waitingPrompt?.playerIndex) != null">
      <small class="action-actor-badge" :class="(game.prompts?.[0]?.playerIndex ?? game.waitingPrompt?.playerIndex) === me.playerIndex ? 'self' : 'opponent'">{{ (game.prompts?.[0]?.playerIndex ?? game.waitingPrompt?.playerIndex) === me.playerIndex ? '你的操作' : '对手操作' }}</small>
      <p class="waiting">{{ (game.prompts?.[0]?.playerIndex ?? game.waitingPrompt?.playerIndex) === me.playerIndex ? '请完成当前选择…' : '等待对手操作…' }}</p>
    </template>
    <template v-else-if="game.phase === 'Defense' && game.pendingDefense?.stage === 'DefenseChoice' && me.playerIndex === 1 - game.pendingDefense.attackerPlayer && defenseTargetType === 'master'">
      <small class="action-actor-badge self">你在防守</small>
      <DefenseDecisionExplanation :game="game" :me="me" :defense-ids="defenseIds" :support-ids="supportIds" :defense-target-type="defenseTargetType" compact />
      <button class="primary" :disabled="!blockReady || busy" @click="emit('command', 'resolveDefense')">确认抵挡</button>
      <button class="danger" :disabled="busy" @click="emit('command', 'resolveDefense', { cardInstanceIds: [] })">不抵挡</button>
    </template>
    <template v-else-if="game.phase === 'Defense' && game.pendingDefense?.stage === 'DefenseChoice' && me.playerIndex === 1 - game.pendingDefense.attackerPlayer && defenseTargetType === 'legion'">
      <small class="action-actor-badge self">你在防守</small>
      <DefenseDecisionExplanation :game="game" :me="me" :defense-ids="defenseIds" :support-ids="supportIds" :defense-target-type="defenseTargetType" compact />
      <button class="primary" :disabled="!supportReady || busy" @click="emit('command', 'resolveDefense')">确认支援</button>
      <button class="danger" :disabled="busy" @click="emit('command', 'resolveDefense', { cardInstanceIds: [], supportInstanceId: null })">不支援</button>
    </template>
    <template v-else-if="game.activePlayer === me.playerIndex && ['Disaster','Reset','Draw','Morale','End'].includes(game.phase)">
      <small class="action-actor-badge neutral">我方阶段</small>
      <p>服务器正在依次执行阶段步骤…</p>
    </template>
    <template v-else-if="game.activePlayer === me.playerIndex && game.phase === 'Main'">
      <small class="action-actor-badge self">你的操作</small>
      <p v-if="me.nextLegionChargeMaxCost" class="pending-effect">全军出击：本回合下一张费用不高于 {{ me.nextLegionChargeMaxCost }} 的军团获得冲锋。</p>
      <p class="card-action-hint">点击主宰查看并发动主宰效果；点击手牌或战场军团后显示可执行操作。</p>
      <button class="danger" :disabled="busy" @click="emit('command','endTurn')">{{ busy ? '处理中…' : '结束回合' }}</button>
    </template>
    <p v-else class="waiting"><small class="action-actor-badge opponent">对手操作</small><span>等待对手操作…</span></p>
  </div>
</template>

<style scoped>
.mulligan-role{color:#f0d274;font-size:var(--l12-board-copy,13px);font-weight:900}
.action-actor-badge{display:inline-flex;width:max-content;padding:2px 6px;border:1px solid #5d6a66;background:#101718;color:#cbd4cf;font-size:var(--l12-board-micro,9px);font-weight:900;line-height:1.3}.action-actor-badge.self{border-color:#4d9e72;color:#79e4a3}.action-actor-badge.opponent{border-color:#8f454b;color:#ef9297}.waiting{display:flex;align-items:center;gap:7px}
.l12-actions>button{box-sizing:border-box;min-width:132px;height:48px;min-height:48px;max-height:48px;padding:7px 10px;line-height:1.25;text-align:center;white-space:normal;overflow:hidden;text-wrap:balance}
</style>
