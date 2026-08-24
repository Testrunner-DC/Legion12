<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { gmAction, l12State } from '@/l12/net'
import type { Card, GameState } from '@/l12/types'
import SandboxCardPicker, { type SandboxCatalogCard } from './SandboxCardPicker.vue'

const props = defineProps<{ game: GameState }>()
const emit = defineEmits<{
  armPlacement: [request: {
    type: 'placeCard' | 'playHandCard'
    targetPlayer: number
    cardId?: string
    cardInstanceId?: string
    cardName: string
    cardType: string
    triggerEffects: boolean
  }]
}>()
const open = ref(true)
const targetMode = ref<'self' | 'opponent'>('self')
const selectedCatalogCard = ref<SandboxCatalogCard | null>(null)
const pickerOpen = ref(false)
const destination = ref('hand')
const handDestination = ref('graveyard')

const triggerEffects = ref(true)
const selectedInstanceId = ref('')
const selectedHandInstanceId = ref('')
const attackerInstanceId = ref('')
const attackTargetInstanceId = ref('')
const count = ref(1)
const troops = ref(5000)
const life = ref(props.game.players[props.game.you]?.master.hp ?? 10)
const disaster = ref(props.game.disasterValue)
const phase = ref('Main')

const targetPlayer = computed(() => targetMode.value === 'self' ? props.game.you : 1 - props.game.you)
const target = computed(() => props.game.players[targetPlayer.value])
const targetHand = computed(() => target.value?.hand ?? [])
const selectedHandCard = computed(() => targetHand.value.find(card => card.instanceId === selectedHandInstanceId.value) ?? null)
const publicCards = computed(() => {
  const player = target.value
  if (!player) return [] as Card[]
  return [...player.field.flat().filter((card): card is Card => Boolean(card)), ...(player.relic ? [player.relic] : []), ...(player.extraRelics || [])]
})
const fieldLegions = computed(() => target.value?.field.flat()
  .filter((card): card is Card => card !== null && card.cardType === 'legion') || [])
const defendingLegions = computed(() => props.game.players[1 - targetPlayer.value]?.field.flat()
  .filter((card): card is Card => card !== null && card.cardType === 'legion' && !card.hidden) || [])

watch(targetPlayer, () => {
  selectedInstanceId.value = ''
  selectedHandInstanceId.value = ''
  attackerInstanceId.value = ''
  attackTargetInstanceId.value = ''
  life.value = target.value?.master.hp ?? 10
})
watch(() => props.game.disasterValue, value => { disaster.value = value })

function run(type: string, extra: Record<string, unknown> = {}) {
  gmAction({ type, targetPlayer: targetPlayer.value, ...extra })
}
function addCard() { if (selectedCatalogCard.value) run('addCard', { cardId: selectedCatalogCard.value.id, destination: destination.value, value: count.value }) }
function placeCard() {
  const card = selectedCatalogCard.value
  if (!card) return
  if (card.cardType === 'legion') {
    emit('armPlacement', { type: 'placeCard', targetPlayer: targetPlayer.value, cardId: card.id, cardName: card.nameZh, cardType: card.cardType, triggerEffects: triggerEffects.value })
    open.value = false
    return
  }
  run('placeCard', { cardId: card.id, triggerEffects: triggerEffects.value })
}
function moveHandCard() { if (selectedHandCard.value) run('moveHandCard', { cardInstanceId: selectedHandCard.value.instanceId, destination: handDestination.value }) }
function playHandCard() {
  const card = selectedHandCard.value
  if (!card) return
  if (card.cardType === 'legion') {
    emit('armPlacement', { type: 'playHandCard', targetPlayer: targetPlayer.value, cardInstanceId: card.instanceId, cardName: card.name, cardType: card.cardType, triggerEffects: triggerEffects.value })
    open.value = false
    return
  }
  run('playHandCard', { cardInstanceId: card.instanceId, triggerEffects: triggerEffects.value })
}
function selectCatalogCard(card: SandboxCatalogCard) { selectedCatalogCard.value = card; pickerOpen.value = false }
function onKeydown(event: KeyboardEvent) {
  if (event.key.toLowerCase() !== 't' || event.ctrlKey || event.metaKey || event.altKey) return
  if ((event.target as HTMLElement | null)?.closest('input,select,textarea,button')) return
  open.value = !open.value
}
async function exportRecord() {
  try {
    const ws = new URL(l12State.endpoint)
    const url = `${ws.protocol === 'wss:' ? 'https:' : 'http:'}//${ws.host}/api/matches/${props.game.matchId}`
    const response = await fetch(url)
    if (!response.ok) throw new Error('读取对局记录失败')
    const data = await response.json()
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
    const href = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = href
    anchor.download = `L12-sandbox-${props.game.matchId.slice(0, 12)}.json`
    anchor.click()
    URL.revokeObjectURL(href)
  } catch (error) {
    l12State.notice = error instanceof Error ? error.message : '导出失败'
  }
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <button v-if="!open" class="gm-open" title="打开 GM 面板（T）" @click="open = true">GM</button>
  <aside v-else class="gm-panel">
    <header><div><small>TEST AUTHORITY</small><b>GM 调试面板</b></div><button @click="open = false">×</button></header>
    <p class="security">仅本次单人沙盒有效 · 所有操作由服务端校验并记录</p>

    <div class="target-tabs"><button :class="{ active: targetMode === 'self' }" @click="targetMode = 'self'">我方</button><button :class="{ active: targetMode === 'opponent' }" @click="targetMode = 'opponent'">对方</button></div>

    <section><h3>主宰、天灾与阶段</h3><div class="number-row"><input v-model.number="life" type="number" min="0" max="99"/><button @click="run('setLife', { value: life })">设置主宰血量</button></div><div class="number-row"><input v-model.number="disaster" type="number" min="0" max="99"/><button :disabled="game.disasterMode === 'none'" @click="run('setDisaster', { value: disaster })">设置天灾值</button></div><button class="wide" :disabled="game.disasterMode === 'none'" @click="run('triggerDisaster')">立即触发下一张天灾</button><select v-model="phase"><option value="Disaster">触发天灾</option><option value="Reset">重置阶段</option><option value="Draw">抽牌阶段</option><option value="Morale">士气阶段</option><option value="Main">主要阶段</option><option value="End">结束阶段</option></select><button class="primary wide" @click="run('setPhase', { phase })">将该方设为回合玩家并跳转</button><button class="wide" @click="run('nextPhase')">进入下一阶段</button></section>

    <section><h3>卡牌与区域</h3><button class="card-choice" @click="pickerOpen = true"><img v-if="selectedCatalogCard?.imageUrl" :src="selectedCatalogCard.imageUrl" :alt="selectedCatalogCard.nameZh"/><span><b>{{ selectedCatalogCard?.nameZh || '选择卡片' }}</b><small>{{ selectedCatalogCard?.number || '从完整卡牌档案筛选' }}</small></span></button><select v-model="destination"><option value="hand">加入手牌</option><option value="library-top">置于牌库顶部</option><option value="library-bottom">置于牌库底部</option><option value="graveyard">置入墓地</option><option value="removed">移出游戏</option></select><div class="number-row"><input v-model.number="count" type="number" min="1" max="20"/><button class="primary" :disabled="!selectedCatalogCard" @click="addCard">连续放置</button></div></section>

    <section><h3>无视费用打出</h3><label class="check"><input v-model="triggerEffects" type="checkbox"/>执行登场时/战术效果</label><button class="primary" :disabled="!selectedCatalogCard" @click="placeCard">打出所选卡片</button></section>

    <section><h3>{{ targetMode === 'self' ? '我方' : '对方' }}手牌</h3><div class="gm-hand"><button v-for="card in targetHand" :key="card.instanceId" :class="{ active: selectedHandInstanceId === card.instanceId }" @click="selectedHandInstanceId = card.instanceId"><img v-if="card.imageUrl" :src="card.imageUrl" :alt="card.name"/><span>{{ card.name }}</span></button><p v-if="!targetHand.length">该方没有手牌</p></div><select v-model="handDestination"><option value="graveyard">置入墓地</option><option value="library-top">置于牌库顶部</option><option value="library-bottom">置于牌库底部</option><option value="removed">移出游戏</option></select><button :disabled="!selectedHandCard" @click="moveHandCard">移动所选手牌</button><button class="primary" :disabled="!selectedHandCard" @click="playHandCard">无视费用打出所选手牌</button></section>

    <section><h3>场上卡牌</h3><select v-model="selectedInstanceId"><option value="">选择目标卡牌</option><option v-for="card in publicCards" :key="card.instanceId" :value="card.instanceId">{{ card.name }} · {{ card.instanceId }}</option></select><div class="two"><button :disabled="!selectedInstanceId" @click="run('setCardState', { cardInstanceId: selectedInstanceId, destination: 'ready' })">活跃</button><button :disabled="!selectedInstanceId" @click="run('setCardState', { cardInstanceId: selectedInstanceId, destination: 'rested' })">休整</button><button :disabled="!selectedInstanceId" @click="run('returnCardToHand', { cardInstanceId: selectedInstanceId })">返回手牌</button><button :disabled="!selectedInstanceId" @click="run('resetCardEffects', { cardInstanceId: selectedInstanceId })">重置效果</button><button class="danger" :disabled="!selectedInstanceId" @click="run('destroyCard', { cardInstanceId: selectedInstanceId })">击杀/弃置</button></div><div class="two"><button @click="run('readyAll')">全场活跃</button><button @click="run('restAll')">全场休整</button></div><button class="danger wide" @click="run('destroyAll')">击杀该方全部军团</button></section>

    <section><h3>兵力与测试进攻</h3><select v-model="attackerInstanceId"><option value="">选择该方军团</option><option v-for="card in fieldLegions" :key="card.instanceId" :value="card.instanceId">{{ card.name }} · 当前 {{ card.troops }}</option></select><div class="number-row"><input v-model.number="troops" type="number" min="0" max="99999" step="1000"/><button :disabled="!attackerInstanceId" @click="run('setTroops', { cardInstanceId: attackerInstanceId, value: troops })">设置当前兵力</button></div><select v-model="attackTargetInstanceId"><option value="">对方主宰</option><option v-for="card in defendingLegions" :key="card.instanceId" :value="card.instanceId">{{ card.name }} · 当前 {{ card.troops }}</option></select><button class="primary wide" :disabled="!attackerInstanceId" @click="run('startAttack', { cardInstanceId: attackerInstanceId, targetInstanceId: attackTargetInstanceId || null })">发起规则内测试进攻</button></section>

    <section><h3>士气、抽牌与牌库</h3><div class="number-row"><input v-model.number="count" type="number" min="1" max="20"/><button @click="run('addMorale', { value: count })">追加活跃士气</button></div><div class="two"><button @click="run('readyMorale')">全部士气活跃</button><button @click="run('restMorale')">全部士气休整</button><button @click="run('draw', { value: count })">抽取 {{ count }} 张</button><button @click="run('mill', { value: count })">弃置牌库顶 {{ count }} 张</button></div><button class="wide" @click="run('shuffleLibrary')">洗切牌库</button></section>

    <footer><button @click="exportRecord">导出可复现 JSON</button><span>快捷键 T</span></footer>
  </aside>
  <SandboxCardPicker v-if="pickerOpen" title="选择要执行 GM 操作的卡片" @select="selectCatalogCard" @close="pickerOpen = false"/>
</template>

<style scoped>
.gm-panel,.gm-open{position:fixed;z-index:2100;right:12px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.gm-open{top:62px;width:46px;height:40px;border:1px solid #dfb746;background:#171208;color:#f2cf6d;font-weight:900}.gm-panel{top:60px;bottom:14px;width:320px;overflow-y:auto;border:1px solid #675825;background:#090d11f5;color:#edf0ed;box-shadow:0 22px 70px #000b}.gm-panel>header{position:sticky;z-index:2;top:0;display:flex;align-items:center;justify-content:space-between;padding:13px 14px;border-bottom:1px solid #665724;background:#0c1115}.gm-panel header small,.gm-panel header b{display:block}.gm-panel header small{color:#c9a945;font:900 8px monospace;letter-spacing:.17em}.gm-panel header b{margin-top:3px;font-size:14px}.gm-panel header button{border:0;background:transparent;color:#d9dedb;font-size:20px}.security{margin:0;padding:9px 13px;background:#1f1909;color:#bda861;font-size:9px;font-weight:800}.target-tabs{display:grid;grid-template-columns:1fr 1fr;padding:12px 12px 0}.target-tabs button{padding:9px;border:1px solid #3d4850;background:#111820;color:#89959a;font-weight:900}.target-tabs button.active{border-color:#d6ad3b;background:#d6ad3b;color:#111}.gm-panel section{padding:12px;border-bottom:1px solid #283139}.gm-panel h3{margin:0 0 9px;color:#d8c073;font-size:11px}.gm-panel input,.gm-panel select,.gm-panel section button{box-sizing:border-box;width:100%;min-height:34px;margin-top:6px;border:1px solid #44515a;background:#101820;color:#eef1ee;font-size:10px;font-weight:800}.gm-panel input,.gm-panel select{padding:7px}.gm-panel button:disabled{cursor:not-allowed;opacity:.35}.gm-panel .primary{border-color:#d5ad3c;background:#d5ad3c;color:#111}.gm-panel .danger{border-color:#89313e;background:#3a1119;color:#f2b9c0}.compact,.two,.three,.number-row{display:grid;gap:6px}.compact,.two{grid-template-columns:1fr 1fr}.three{grid-template-columns:repeat(3,1fr)}.number-row{grid-template-columns:68px 1fr}.compact label{font-size:9px}.check{display:flex;align-items:center;gap:7px;margin-top:8px;color:#aab3b4;font-size:9px}.check input{width:14px;min-height:14px;margin:0}.gm-panel .wide{width:100%}.gm-panel footer{display:flex;align-items:center;justify-content:space-between;padding:12px}.gm-panel footer button{padding:8px 10px;border:1px solid #4e5b62;background:#111920;color:#fff;font-size:9px;font-weight:900}.gm-panel footer span{color:#7c898e;font:800 8px monospace}@media(max-width:700px){.gm-panel{top:48px;right:5px;bottom:5px;width:min(320px,calc(100vw - 10px))}}
.hint{margin:7px 0 0;color:#859197;font-size:9px;line-height:1.55}
.card-choice{display:flex!important;align-items:center;gap:9px;padding:6px!important;text-align:left}.card-choice img{width:38px;height:52px;object-fit:contain;background:#050708}.card-choice span,.card-choice b,.card-choice small{display:block;min-width:0}.card-choice span{flex:1}.card-choice small{margin-top:3px;color:#849196;font-size:8px}.gm-hand{display:flex;gap:5px;overflow-x:auto;padding:5px 0 8px}.gm-hand button{flex:0 0 62px!important;width:62px!important;padding:3px!important;border-color:#354148!important;background:#080d11!important}.gm-hand button.active{border-color:#e2bc49!important;box-shadow:0 0 9px #c7963a66}.gm-hand img{display:block;width:54px;height:75px;object-fit:contain}.gm-hand span{display:block;overflow:hidden;margin-top:3px;color:#fff;font-size:7px;text-overflow:ellipsis;white-space:nowrap}.gm-hand p{margin:4px;color:#758187;font-size:9px}
</style>
