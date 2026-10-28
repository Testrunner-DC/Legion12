<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { friendApi, platformState, type FriendHistory, type PlayerStatisticsRange } from '../platform'
import { friendResource } from '../friendResource'

const props = defineProps<{ accountId: string; friendName: string }>()
const range = ref<PlayerStatisticsRange>('season')
const data = ref<FriendHistory | null>(null)
const loading = ref(false)
const error = ref('')
let generation = 0
let controller: AbortController | undefined
const validFriend = computed(() => !!platformState.account?.id && friendResource.accountId === platformState.account.id
  && friendResource.friends.some(friend => friend.accountId === props.accountId))
const ranges: Array<{ id: PlayerStatisticsRange; label: string }> = [
  { id: 'season', label: '本赛季' }, { id: '7d', label: '近 7 天' }, { id: '30d', label: '近 30 天' },
]
async function load() {
  const ticket = ++generation
  controller?.abort()
  data.value = null; error.value = ''; loading.value = false
  if (!validFriend.value) { error.value = '暂时无法查看该好友的战绩'; return }
  const accountId = props.accountId, ownerId = platformState.account?.id, token = platformState.token
  controller = new AbortController()
  loading.value = true
  try {
    const result = await friendApi.history(accountId, range.value, controller.signal)
    if (ticket !== generation || ownerId !== platformState.account?.id || token !== platformState.token || !validFriend.value) return
    if (result.accountId !== accountId || result.statistics.range !== range.value) throw new Error('战绩信息已变化，请重新查看')
    data.value = result
  } catch (cause) {
    if (ticket === generation) error.value = cause instanceof Error ? cause.message : '战绩读取失败，请重试'
  } finally { if (ticket === generation) loading.value = false }
}
watch(() => [props.accountId, platformState.account?.id, platformState.token, range.value, validFriend.value], () => { void load() }, { immediate: true })
onBeforeUnmount(() => { generation++; controller?.abort(); data.value = null })
const winRate = (games: number, wins: number) => games ? `${(wins / games * 100).toFixed(1)}%` : '—'
const date = (value: string) => new Date(value).toLocaleString('zh-CN', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' })
const mode = (id: string) => (({ ranked: '排位', casual: '休闲', friendly: '好友对战', tournament: '赛事' } as Record<string, string>)[id] ?? '对战')
const resultText = (result: string) => (({ win: '胜利', loss: '落败', draw: '平局' } as Record<string, string>)[result] ?? '已结束')
</script>

<template>
  <section class="friend-history" :aria-busy="loading" aria-label="好友战绩">
    <div class="history-ranges" aria-label="战绩时间范围"><button v-for="item in ranges" :key="item.id" type="button" :aria-pressed="range === item.id" @click="range = item.id">{{ item.label }}</button></div>
    <p v-if="loading" role="status">正在读取战绩…</p>
    <div v-else-if="error" role="status" class="history-error"><p>{{ error }}</p><button type="button" :disabled="!validFriend" @click="load">重新读取</button></div>
    <template v-else-if="data">
      <h3>{{ friendName }}的战绩</h3>
      <div class="history-totals"><article><b>全部对战</b><strong>{{ data.statistics.overall.games }} 场</strong><span>胜率 {{ winRate(data.statistics.overall.games, data.statistics.overall.wins) }}</span><small>{{ data.statistics.overall.wins }} 胜 / {{ data.statistics.overall.losses }} 负 / {{ data.statistics.overall.draws }} 平</small></article><article><b>排位对战</b><strong>{{ data.statistics.ranked.games }} 场</strong><span>胜率 {{ winRate(data.statistics.ranked.games, data.statistics.ranked.wins) }}</span><small>{{ data.statistics.ranked.wins }} 胜 / {{ data.statistics.ranked.losses }} 负 / {{ data.statistics.ranked.draws }} 平</small></article></div>
      <h3>最近 {{ data.recentLimit }} 场<span> · 当前时间范围内</span></h3>
      <p v-if="!data.recentMatches.length">该时间范围内暂无对局。</p>
      <ol v-else class="history-matches"><li v-for="(match, index) in data.recentMatches" :key="index"><div><b :class="match.result">{{ resultText(match.result) }}</b><small>{{ mode(match.modeId) }} · {{ date(match.endedUtc) }}</small></div><strong>对阵 {{ match.opponentName }}</strong><span>{{ match.masterName || '未知主宰' }} / {{ match.opponentMasterName || '未知主宰' }}</span></li></ol>
    </template>
  </section>
</template>

<style scoped>
.friend-history{min-width:0;color:#e9eee9;text-align:left}.history-ranges{display:flex;flex-wrap:wrap;gap:6px}.history-ranges button,.history-error button{min-height:38px;padding:8px 10px;border:1px solid #4a636a;background:#10252d;color:#edf0e7;font:inherit}.history-ranges button[aria-pressed=true]{border-color:#d7b661;background:#332c16}.friend-history h3{margin:18px 0 10px;font-size:15px}.friend-history h3 span,.friend-history p,.friend-history small,.history-matches span{color:#a9b7b9;font-size:12px;line-height:1.6}.history-totals{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px}.history-totals article{display:flex;min-width:0;flex-direction:column;gap:6px;padding:10px;border:1px solid #35424b;background:#0b151c}.history-totals b{font-size:13px}.history-totals strong{font-size:20px;color:#f1d180}.history-totals span{font-size:13px}.history-matches{display:grid;gap:8px;padding:0;list-style:none}.history-matches li{min-width:0;padding:10px;border-bottom:1px solid #35424b;overflow-wrap:anywhere}.history-matches li>div{display:flex;justify-content:space-between;flex-wrap:wrap;gap:6px}.history-matches li>strong,.history-matches li>span{display:block;margin-top:5px}.history-matches strong{font-size:13px}.win{color:#83d6b4}.loss{color:#e0a9af}.draw{color:#e5d18c}.history-error{padding:10px;border-left:2px solid #d7b661}.friend-history button:focus-visible{outline:2px solid #e2c368;outline-offset:2px}@media(max-width:380px){.history-totals{gap:5px}.history-totals article{padding:7px}}
</style>
