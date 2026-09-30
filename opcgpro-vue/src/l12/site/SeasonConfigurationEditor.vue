<script setup lang="ts">
import { computed } from 'vue'
import { type DeckCard } from '@/l12/decks'
import {
  type RankedBroadcast,
  type RankedConfig,
  type SeasonDefinitionDraft,
} from '@/l12/platform'
import ConstructionRuleEditor from './ConstructionRuleEditor.vue'
import DisasterPoolPicker from './DisasterPoolPicker.vue'
import PagedCollection from './PagedCollection.vue'

const props = defineProps<{
  slot: 'current' | 'next'
  section: 'season' | 'ranked' | 'construction'
  cards: DeckCard[]
  broadcasts: RankedBroadcast[]
}>()
const emit = defineEmits<{ deleteBroadcast: [id: string] }>()
const draft = defineModel<SeasonDefinitionDraft>({ required: true })
const ranked = computed(() => draft.value.configuration.ranked)
const presetDecks = computed({
  get: () => draft.value.configuration.defaultPresetDeckIds.join('\n'),
  set: value => { draft.value.configuration.defaultPresetDeckIds = lines(value) },
})
const placementMaximumLimit = computed(() => Math.max(0, (ranked.value.factions[0]?.tiers[2]?.minimum ?? 30000) - 1))
const sharedTierFields = [
  { key: 'minimum', label: '七曜门槛' }, { key: 'baseDelta', label: '基础胜负' },
  { key: 'winStreakCap', label: '连胜上限' }, { key: 'lossProtectionCap', label: '连败保护' },
  { key: 'ratingGapCap', label: '差值修正' }, { key: 'streakTerminationReward', label: '终结连胜' },
] as const
type SharedTierKey = typeof sharedTierFields[number]['key']
const tierMismatch = computed(() => {
  const factions = ranked.value.factions
  const base = factions[0]?.tiers ?? []
  return factions.some(faction => faction.tiers.length !== base.length || faction.tiers.some((tier, index) =>
    sharedTierFields.some(field => tier[field.key] !== base[index]?.[field.key])))
})

function lines(value: string) {
  return value.split(/\r?\n/).map(item => item.trim()).filter(Boolean)
}
function updateTier(index: number, key: SharedTierKey, event: Event) {
  const value = (event.target as HTMLInputElement).valueAsNumber
  if (!Number.isSafeInteger(value) || value < 0) return
  for (const faction of ranked.value.factions) if (faction.tiers[index]) faction.tiers[index]![key] = value
}
function normalizeTiers(sourceIndex: number) {
  const source = ranked.value.factions[sourceIndex]?.tiers
  if (!source || ranked.value.factions.some(faction => faction.tiers.length !== source.length)) return
  for (const faction of ranked.value.factions) faction.tiers.forEach((tier, index) => {
    for (const field of sharedTierFields) tier[field.key] = source[index]![field.key]
  })
}
function toggleSeasonEnd(event: Event) {
  draft.value.endsAt = (event.target as HTMLInputElement).checked
    ? undefined
    : new Date(Date.now() + 90 * 24 * 60 * 60 * 1000).toISOString().slice(0, 16)
}
function rankedTimeError(config: RankedConfig) {
  const timing = config.timeControl
  if (Object.values(timing).some(value => !Number.isSafeInteger(value))) return '排位时限必须填写整数秒数'
  if (timing.totalTimeSeconds < 300 || timing.totalTimeSeconds > 7200) return '总操作时限需为 300–7200 秒'
  if (timing.operationTimeSeconds < 15 || timing.operationTimeSeconds > 900 || timing.operationTimeSeconds > timing.totalTimeSeconds) return '单次操作时限需为 15–900 秒，且不得超过总操作时限'
  if (timing.reconnectGraceSeconds < 15 || timing.reconnectGraceSeconds > 900) return '断线重连宽限需为 15–900 秒'
  if ([timing.disasterDecisionSeconds, timing.mulliganDecisionSeconds].some(value => value < 10 || value > 300)) return '天灾选择与手牌调度时限均需为 10–300 秒'
  return ''
}
const timingError = computed(() => rankedTimeError(ranked.value))
</script>

<template>
  <div class="season-editor" :data-ui-contract="slot === 'current' ? 'current-season' : 'next-season'" data-current-contract="current-season" data-next-contract="next-season">
    <template v-if="section === 'season'">
      <fieldset>
        <legend>{{ slot === 'current' ? '当前赛季' : '下赛季草稿' }}</legend>
        <label>赛季 ID<input v-model="draft.seasonId" :readonly="slot === 'current'"/></label>
        <label>名称<input v-model="draft.name"/></label>
        <label>开始时间<input v-model="draft.startsAt" type="datetime-local" :readonly="slot === 'current'"/></label>
        <label>结束时间<input v-model="draft.endsAt" type="datetime-local" :disabled="!draft.endsAt"/></label>
        <label class="toggle-row wide"><span><b>不设置结束时间</b><small>赛季持续有效，直到管理员设置结束时间。</small></span><input type="checkbox" :checked="!draft.endsAt" @change="toggleSeasonEnd"/></label>
      </fieldset>
      <fieldset><legend>赛季天灾池</legend><DisasterPoolPicker v-model="draft.configuration.disasterPool.cardIds" class="wide" :cards="cards" locked-id="S01-DS10"/><span class="locked-note wide">堙灭固定公开并锁定在最后一张。</span></fieldset>
    </template>

    <template v-else-if="section === 'construction'">
      <fieldset class="wide"><legend>构筑规则</legend><p class="wide field-help">筛选卡牌后设置全局构筑上限，或指定某位主宰的专属上限；主宰专属规则优先于全局规则。</p><ConstructionRuleEditor v-model="draft.configuration.cardRestrictions" class="wide" :cards="cards"/></fieldset>
      <fieldset><legend>新账号默认预组</legend><label class="wide">每行一个官方预组的主宰卡号；仅用于新账号初始化。<textarea v-model="presetDecks" rows="14"/></label></fieldset>
    </template>

    <template v-else>
      <fieldset><legend>定级与广播</legend><label>定级场次<input v-model.number="ranked.placementMatches" type="number" min="1" max="20"/></label><label>定级七曜上限<input v-model.number="ranked.placementMaximum" type="number" min="0" :max="placementMaximumLimit"/><small>须低于第三段门槛，当前最多 {{ placementMaximumLimit }}</small></label><label class="toggle-row wide"><span><b>启用排位快讯</b><small>总开关关闭后不再生成新的排位广播。</small></span><input v-model="ranked.broadcastEnabled" type="checkbox"/></label></fieldset>
      <fieldset class="wide" data-ui-contract="ranked-broadcast-config"><legend>排位广播规则</legend><p class="wide field-help">设置只影响下一条广播；当前正在播放的广播不会被中断。</p><label>单条显示时长（秒）<input v-model.number="ranked.broadcast.displaySeconds" type="number" min="5" max="120"/></label><label>进入大厅后延迟（秒）<input v-model.number="ranked.broadcast.lobbyDelaySeconds" type="number" min="0" max="120"/></label><label>两条广播间隔（秒）<input v-model.number="ranked.broadcast.intervalSeconds" type="number" min="3" max="600"/></label><label>连胜广播门槛<input v-model.number="ranked.broadcast.winStreakThreshold" type="number" min="2" max="100"/></label><label>终结连胜门槛<input v-model.number="ranked.broadcast.streakEndedThreshold" type="number" min="2" max="100"/></label><label>最低段位<select v-model.number="ranked.broadcast.minimumTierIndex"><option v-for="(tier,index) in ranked.factions[0]?.tiers" :key="index" :value="index">第 {{ index + 1 }} 段 · {{ tier.name }}</option></select></label><label class="toggle-row"><span><b>连胜</b></span><input v-model="ranked.broadcast.winStreakEnabled" type="checkbox"/></label><label class="toggle-row"><span><b>终结连胜</b></span><input v-model="ranked.broadcast.streakEndedEnabled" type="checkbox"/></label><label class="toggle-row"><span><b>最高段位晋升</b></span><input v-model="ranked.broadcast.highestTierEnabled" type="checkbox"/></label><label class="toggle-row"><span><b>派名称号</b></span><input v-model="ranked.broadcast.factionTitleEnabled" type="checkbox"/></label><label class="toggle-row"><span><b>最强主宰称号</b></span><input v-model="ranked.broadcast.masterTitleEnabled" type="checkbox"/></label></fieldset>
      <fieldset class="wide ranked-time-control" data-ui-contract="ranked-time-control-config"><legend>排位用时</legend><p class="wide field-help">单位均为秒；当前槽保存后只影响新对局，下赛季槽仅保存草稿。</p><label>总操作时限<input v-model.number="ranked.timeControl.totalTimeSeconds" type="number" min="300" max="7200"/></label><label>单次操作时限<input v-model.number="ranked.timeControl.operationTimeSeconds" type="number" min="15" max="900"/></label><label>断线重连宽限<input v-model.number="ranked.timeControl.reconnectGraceSeconds" type="number" min="15" max="900"/></label><label>天灾禁选／选择<input v-model.number="ranked.timeControl.disasterDecisionSeconds" type="number" min="10" max="300"/></label><label>手牌调度<input v-model.number="ranked.timeControl.mulliganDecisionSeconds" type="number" min="10" max="300"/></label><p v-if="timingError" class="wide notice" role="alert">{{ timingError }}</p></fieldset>
      <fieldset v-for="faction in ranked.factions" :key="faction.id" class="wide"><legend>{{ faction.name }}派系</legend><label>显示名称<input v-model="faction.name"/></label><label>主题色<input v-model="faction.color" type="color"/></label><label>最高段位第一名称号<input v-model="faction.firstTitle"/></label><label>最高段位第二至五名称号<input v-model="faction.topFiveTitle"/></label></fieldset>
      <fieldset class="wide"><legend>五段位共同规则</legend><p v-if="tierMismatch" class="wide notice" role="alert">历史配置存在不同派系的段位数值差异。请确认来源后统一，系统不会静默覆盖。</p><div v-if="tierMismatch" class="wide"><button v-for="(faction,index) in ranked.factions" :key="faction.id" type="button" @click="normalizeTiers(index)">以{{ faction.name }}的数值统一</button></div><div class="wide ranked-shared-tiers"><article v-for="(tier,index) in ranked.factions[0]?.tiers" :key="index"><h3>第 {{ index + 1 }} 段位</h3><div class="tier-name-fields"><label v-for="faction in ranked.factions" :key="faction.id">{{ faction.name }}名称<input v-if="faction.tiers[index]" v-model="faction.tiers[index].name"/></label></div><div class="tier-number-fields"><label v-for="field in sharedTierFields" :key="field.key">{{ field.label }}<input :value="tier[field.key]" type="number" min="0" :disabled="tierMismatch || Boolean(ranked.pendingGradient)" @input="updateTier(index,field.key,$event)"/></label></div></article></div></fieldset>
      <fieldset v-if="ranked.pendingGradient" class="wide" data-ui-contract="pending-ranked-gradient"><legend>下赛季待生效梯度</legend><p class="wide field-help">离开赛季 {{ ranked.pendingGradient.afterSeasonId }} 时一次性生效；隐藏匹配分不会重置。</p></fieldset>
      <fieldset class="wide"><legend>主宰最强玩家称号</legend><div class="wide ranked-master-title-grid"><PagedCollection :items="ranked.masterTitles" v-slot="{items}"><label v-for="master in items" :key="master.masterId"><span>{{ master.masterName }}<small>{{ master.masterId }}</small></span><input v-model="master.title" :aria-label="`${master.masterName}最强玩家称号`"/></label></PagedCollection></div></fieldset>
      <fieldset v-if="slot === 'current'" class="wide runtime-broadcasts"><legend>近期排位快讯</legend><p class="wide field-help">这是当前运行态数据，不属于赛季定义草稿。</p><PagedCollection :items="broadcasts" v-slot="{items}"><article v-for="item in items" :key="item.id" class="broadcast-row"><span>{{ item.message }}<small>{{ new Date(item.createdAt).toLocaleString() }}</small></span><button @click="emit('deleteBroadcast',item.id)">删除</button></article></PagedCollection><span v-if="!broadcasts.length">暂无排位快讯</span></fieldset>
    </template>
  </div>
</template>

<style scoped>
.season-editor{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;min-width:0;max-width:100%}.season-editor fieldset{box-sizing:border-box;display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;align-content:start;min-width:0;max-width:100%;border:1px solid #334049;padding:14px}.season-editor legend{max-width:100%;padding:0 6px;color:#e0c36e;font-weight:900;white-space:normal;overflow-wrap:anywhere}.season-editor label{display:flex;min-width:0;flex-direction:column;gap:6px;color:#b5bfc3;font-size:14px}.season-editor input,.season-editor select,.season-editor textarea,.season-editor button{box-sizing:border-box;max-width:100%;min-height:44px;border:1px solid #4c5961;background:#080e13;color:#fff;font:700 14px 'Microsoft YaHei';padding:9px}.season-editor input,.season-editor select,.season-editor textarea{width:100%;min-width:0}.wide{grid-column:1/-1}.field-help{margin:0;color:#8f9da3!important;overflow-wrap:anywhere}.locked-note{color:#e3c76e!important}.toggle-row{display:flex!important;min-width:0;flex-direction:row!important;align-items:center;justify-content:space-between;padding:8px;border:1px solid #2f3b42}.toggle-row span{display:flex;min-width:0;flex-direction:column}.toggle-row input{width:auto}.notice{color:#ffc8ce!important}.ranked-shared-tiers{display:grid;grid-template-columns:1fr;gap:16px}.ranked-shared-tiers article{min-width:0;padding:18px;border:1px solid #44515b;background:#0a1117}.ranked-shared-tiers h3{margin:0 0 14px}.tier-name-fields,.tier-number-fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:12px;margin-top:12px}.ranked-master-title-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px}.ranked-master-title-grid label{display:grid;grid-template-columns:minmax(0,1fr) minmax(150px,1fr);align-items:center;gap:8px;padding:9px;border:1px solid #303c43;background:#0a1117}.ranked-master-title-grid small{display:block;overflow-wrap:anywhere}.broadcast-row{display:grid;grid-template-columns:minmax(0,1fr) auto;align-items:center;gap:8px;padding:8px;border-bottom:1px solid #303c43}.broadcast-row span,.broadcast-row small{display:block;min-width:0;overflow-wrap:anywhere}
@media(max-width:1100px){.season-editor{grid-template-columns:1fr}.ranked-master-title-grid{grid-template-columns:1fr 1fr}}@media(max-width:650px){.season-editor,.season-editor fieldset{grid-template-columns:1fr}.wide{grid-column:auto}.season-editor fieldset{padding:10px}.ranked-master-title-grid{grid-template-columns:1fr}.ranked-master-title-grid label,.broadcast-row{grid-template-columns:1fr}.toggle-row{align-items:flex-start;gap:8px}}
@media(max-width:650px){.season-editor :deep(.pool-picker){box-sizing:border-box;width:100%;min-width:0;max-width:100%}.season-editor :deep(.pool-picker>header){min-width:0;max-width:100%;flex-wrap:wrap}.season-editor :deep(.pool-picker>header input){width:100%;min-width:0;flex-basis:100%}.season-editor :deep(.pool-picker>header span){max-width:100%;white-space:normal;overflow-wrap:anywhere}.season-editor :deep(.pool-grid){min-width:0;max-width:100%;grid-template-columns:repeat(auto-fill,minmax(min(100%,132px),1fr))}}
</style>
