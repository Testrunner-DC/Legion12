<script setup lang="ts">
import { computed } from 'vue'
import type { AdminDisasterSetupSummary } from '@/l12/platform'
import type { DeckCard } from '@/l12/decks'

const props = withDefaults(defineProps<{ summary?: AdminDisasterSetupSummary | null; cards?: DeckCard[] }>(), { cards: () => [] })
const byId = computed(() => new Map(props.cards.map(card => [card.id, card.nameZh])))
const visible = computed(() => props.summary && props.summary.coverage !== 'private' && props.summary.status !== 'active')
const mode = (value?: string | null) => (({ all: '全部天灾', season: '赛季天灾', random: '随机天灾', none: '不使用天灾' } as Record<string, string>)[value || ''] || '未记录')
const name = (id?: string | null) => id ? byId.value.get(id) || id : '尚未决定'
const source = (value?: string | null) => (({ player: '玩家选择', 'server-setup-timeout': '服务器超时代选',
  'player-pick': '玩家选入', 'random-public': '随机公开', 'random-mode': '随机模式', fixed: '固定牌' } as Record<string, string>)[value || ''] || '来源未知')
const step = (value: number) => (({ 0: '先手第 1 禁', 1: '后手禁用', 2: '先手第 2 禁', 4: '先手选择', 5: '后手选择' } as Record<number, string>)[value] || '未记录步骤')
</script>

<template>
  <section class="disaster-summary" aria-label="天灾禁选摘要">
    <header><h3>天灾禁选摘要</h3><span>{{ mode(summary?.mode) }}</span></header>
    <p v-if="!summary" class="coverage-note">此后端版本尚未提供禁选摘要，不代表本局没有禁选。</p>
    <p v-else-if="!visible" class="coverage-note">对局进行中，不展示私密天灾候选或选用明细。</p>
    <template v-else>
      <p v-if="summary.coverage === 'unknown'" class="coverage-note">历史禁选事实未知，不能按“未禁用／未选择”理解。</p>
      <p v-else-if="summary.coverage === 'partial'" class="coverage-note">准备未完成或记录不完整；只展示可证明的部分，未决不等于放弃选择。</p>
      <p v-else-if="summary.mode === 'none' || summary.mode === 'random'" class="coverage-note">本模式没有玩家自主禁选，不纳入禁选偏好分母。</p>
      <p v-else class="coverage-note">已记录当时实际候选与成功决定；摘要不依赖录像留存。</p>
      <p v-if="summary.firstPlayer === 0 || summary.firstPlayer === 1">玩家 {{ summary.firstPlayer + 1 }} 先手 · 玩家 {{ 2 - summary.firstPlayer }} 后手</p>
      <ol class="setup-decisions" v-if="summary.coverage !== 'unknown' && summary.decisions?.length">
        <li v-for="decision in summary.decisions" :key="decision.step">
          <div><b>{{ step(decision.step) }}</b><small>玩家 {{ decision.actorIndex + 1 }} · {{ source(decision.origin) }}</small></div>
          <strong>{{ name(decision.chosenCardId) }}</strong>
          <details><summary>本步合法候选 · {{ decision.candidateCardIds.length }} 张</summary><p>{{ decision.candidateCardIds.map(name).join('、') }}</p></details>
        </li>
      </ol>
      <div v-if="summary.coverage !== 'unknown' && summary.composition?.length" class="composition"><b>最终天灾构成（不是实际触发记录）</b><span v-for="card in summary.composition" :key="card.cardId">{{ name(card.cardId) }} <small>{{ source(card.source) }}<template v-if="card.ownerIndex === 0 || card.ownerIndex === 1"> · 玩家 {{ card.ownerIndex + 1 }}</template></small></span></div>
    </template>
  </section>
</template>

<style scoped>
.disaster-summary{min-width:0;padding:16px;border:1px solid #3b4a53;background:#101820;color:#e8e5db}.disaster-summary header{display:flex;flex-wrap:wrap;justify-content:space-between;gap:8px}.disaster-summary h3{margin:0}.coverage-note,.disaster-summary small{color:#a7b4b8}.coverage-note{line-height:1.65}.setup-decisions{display:grid;gap:8px;list-style:none;padding:0}.setup-decisions li{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:8px;padding:12px;border:1px solid #34434d}.setup-decisions li div{display:grid;gap:4px}.setup-decisions strong,.setup-decisions p,.composition span{overflow-wrap:anywhere}.setup-decisions details{grid-column:1/-1;min-width:0}.setup-decisions summary{cursor:pointer;padding:5px 0}.setup-decisions details p{line-height:1.6}.composition{display:grid;gap:8px}.composition span{padding:8px;background:#15212a}.composition small{display:block;margin-top:4px}@media(max-width:440px){.disaster-summary{padding:12px}.setup-decisions li{grid-template-columns:minmax(0,1fr)}}
</style>
