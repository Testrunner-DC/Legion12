<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { connect, createSandbox, l12State } from '@/l12/net'
import { ensureOfficialPrebuiltDecks, loadDeckCatalog, loadSelectedDeckName, saveSelectedDeckName,
  validateDeck, type DeckCard, type SavedL12Deck } from '@/l12/decks'
import { platformState } from '@/l12/platform'
import DeckProfile from '@/l12/DeckProfile.vue'
import SavedDeckSelector from '@/l12/SavedDeckSelector.vue'

const decks = ref<SavedL12Deck[]>([])
const catalog = ref<DeckCard[]>([])
const playerDeckName = ref('')
const opponentDeckName = ref('')
const selectorTarget = ref<'sandbox-player' | 'sandbox-opponent' | null>(null)
const disasterMode = ref<'all' | 'random' | 'none' | 'custom'>('none')
const creating = ref(false)
const playerDeck = computed(() => decks.value.find(deck => deck.name === playerDeckName.value))
const opponentDeck = computed(() => decks.value.find(deck => deck.name === opponentDeckName.value))
const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const playerDeckError = computed(() => playerDeck.value ? validateDeck(playerDeck.value, catalog.value) : '请选择我方牌库')
const opponentDeckError = computed(() => opponentDeck.value ? validateDeck(opponentDeck.value, catalog.value) : '请选择对手牌库')
const selectorCurrentName = computed(() => selectorTarget.value === 'sandbox-opponent'
  ? opponentDeckName.value : playerDeckName.value)

onMounted(async () => {
  const [savedDecks, cards] = await Promise.all([ensureOfficialPrebuiltDecks(), loadDeckCatalog()])
  decks.value = Object.values(savedDecks)
  catalog.value = cards
  const record = Object.fromEntries(decks.value.map(deck => [deck.name, deck]))
  playerDeckName.value = loadSelectedDeckName('sandbox-player', record)
  opponentDeckName.value = loadSelectedDeckName('sandbox-opponent', record)
  if (platformState.account && platformState.token && l12State.status === 'offline') {
    try { await connect() } catch { /* 页面保留服务端提示。 */ }
  }
})

function confirmDeckSelection(deck: SavedL12Deck) {
  const scope = selectorTarget.value
  if (!scope) return
  if (scope === 'sandbox-player') playerDeckName.value = deck.name
  else opponentDeckName.value = deck.name
  saveSelectedDeckName(scope, deck.name)
  selectorTarget.value = null
}

async function startSandbox() {
  if (!platformState.account || !platformState.token) { l12State.notice = '请先登录账号'; return }
  if (!playerDeck.value || !opponentDeck.value || playerDeckError.value || opponentDeckError.value) {
    l12State.notice = playerDeckError.value || opponentDeckError.value || '请选择双方牌库'
    return
  }
  creating.value = true
  try {
    if (l12State.status !== 'online') await connect()
    createSandbox(playerDeck.value, opponentDeck.value, disasterMode.value)
  } catch {
    creating.value = false
  }
}
</script>

<template>
  <div class="sandbox-page">
    <section>
      <header><div><small>TEST SANDBOX</small><h1>单人测试沙盒</h1><p>复用正式规则内核，GM 指令仅对本沙盒生效；沙盒不会进入个人对局记录或排位统计。</p></div><span :class="l12State.status"><i/>{{ l12State.status === 'online' ? '服务器在线' : '服务器离线' }}</span></header>
      <div class="sandbox-grid">
        <div class="sandbox-account"><b>测试账号</b><span>{{ platformState.account?.username || '尚未登录' }}</span><router-link v-if="!platformState.account" to="/profile">前往登录</router-link></div>
        <section class="sandbox-deck"><b>我方牌库</b><DeckProfile v-if="playerDeck" compact :master-id="playerDeck.masterId" :master-name="byId.get(playerDeck.masterId)?.nameZh" :name="playerDeck.name" context="我方"/><p v-else>没有已保存牌库</p><span :class="{ invalid: !!playerDeckError }">{{ playerDeckError || '符合沙盒构筑规则' }}</span><button type="button" @click="selectorTarget = 'sandbox-player'">更换牌库</button></section>
        <section class="sandbox-deck"><b>对手牌库</b><DeckProfile v-if="opponentDeck" compact :master-id="opponentDeck.masterId" :master-name="byId.get(opponentDeck.masterId)?.nameZh" :name="opponentDeck.name" context="对手"/><p v-else>没有已保存牌库</p><span :class="{ invalid: !!opponentDeckError }">{{ opponentDeckError || '符合沙盒构筑规则' }}</span><button type="button" @click="selectorTarget = 'sandbox-opponent'">更换牌库</button></section>
        <label><b>天灾模式</b><select v-model="disasterMode"><option value="none">不使用天灾</option><option value="random">随机天灾</option><option value="all">全部天灾</option><option value="custom">自定天灾（四张始终公开）</option></select></label>
      </div>
      <div class="capabilities"><article><b>卡牌与区域</b><span>加牌、置顶/置底、墓地、无视费用打出、击杀与状态切换。</span></article><article><b>阶段与数值</b><span>切换回合玩家和阶段，调整血量、天灾值、士气并触发天灾。</span></article><article><b>可复现记录</b><span>每条 GM 指令由服务端校验，并写入与实战相同的状态快照。</span></article></div>
      <p v-if="l12State.notice" class="notice">{{ l12State.notice }}</p>
      <footer><router-link to="/lobby">← 返回对战大厅</router-link><button :disabled="creating || !decks.length || !!playerDeckError || !!opponentDeckError" @click="startSandbox">{{ creating ? '正在建立…' : '建立测试沙盒' }}</button></footer>
    </section>
    <SavedDeckSelector :open="!!selectorTarget" :mode="selectorTarget || 'sandbox-player'" :decks="decks"
      :catalog="catalog" :current-deck-name="selectorCurrentName" :loading="!catalog.length"
      @cancel="selectorTarget = null" @confirm="confirmDeckSelection"/>
  </div>
</template>

<style scoped>
.sandbox-page{display:grid;min-height:100%;place-items:center;padding:30px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.sandbox-page>section{width:min(960px,100%);padding:34px;border:1px solid #35424a;background:#101821;box-shadow:0 26px 70px #0007}.sandbox-page header{display:flex;align-items:flex-start;justify-content:space-between;gap:20px}.sandbox-page small{color:#53c4cb;font:900 9px monospace;letter-spacing:.18em}.sandbox-page h1{margin:7px 0;font-size:31px}.sandbox-page p{color:#839097;font-size:12px;line-height:1.7}.sandbox-page header>span{display:flex;align-items:center;gap:7px;color:#8a555d;font-size:10px;font-weight:900}.sandbox-page header>span.online{color:#55c795}.sandbox-page header i{width:7px;height:7px;border-radius:50%;background:currentColor;box-shadow:0 0 8px currentColor}.sandbox-grid{display:grid;grid-template-columns:repeat(2,1fr);gap:12px;margin:24px 0}.sandbox-grid label{padding:14px;border:1px solid #344149;background:#0a1117}.sandbox-grid b{display:block;margin-bottom:8px;font-size:10px}.sandbox-grid input,.sandbox-grid select{width:100%;padding:11px;border:1px solid #46545c;background:#070c10;color:#fff;font-weight:700;outline:none}.sandbox-grid input:focus,.sandbox-grid select:focus{border-color:#53c4cb}.capabilities{display:grid;grid-template-columns:repeat(3,1fr);gap:10px}.capabilities article{padding:17px;border-left:2px solid #c5aa5f;background:#0a1117}.capabilities b,.capabilities span{display:block}.capabilities span{margin-top:7px;color:#718087;font-size:10px;line-height:1.6}.notice{padding:10px;border-left:3px solid #a52b38;background:#211016;color:#e6a8ad!important}.sandbox-page footer{display:flex;align-items:center;justify-content:space-between;margin-top:24px}.sandbox-page a{color:#68d1d7;font-size:11px;text-decoration:none}.sandbox-page footer button{min-width:220px;padding:13px;border:1px solid #dfc26d;background:#dfc26d;color:#090d10;font-weight:900}.sandbox-page footer button:disabled{opacity:.45}@media(max-width:700px){.sandbox-page{padding:12px}.sandbox-page>section{padding:22px}.sandbox-page header,.sandbox-page footer{align-items:stretch;flex-direction:column}.sandbox-grid,.capabilities{grid-template-columns:1fr}}
.sandbox-account{padding:14px;border:1px solid #344149;background:#0a1117}.sandbox-account b,.sandbox-account span{display:block}.sandbox-account span{padding:11px;border:1px solid #46545c;background:#070c10;color:#fff;font-weight:700}.sandbox-account a{display:inline-block;margin-top:8px}
.sandbox-grid label :deep(.deck-profile){margin-bottom:10px}
.sandbox-deck{padding:14px;border:1px solid #344149;background:#0a1117}.sandbox-deck>b,.sandbox-deck>span{display:block}.sandbox-deck :deep(.deck-profile){margin-bottom:10px}.sandbox-deck>span{min-height:28px;color:#70cda3;font-size:10px}.sandbox-deck>span.invalid{color:#e89aa2}.sandbox-deck>button{width:100%;padding:10px;border:1px solid #d8bb68;background:#151b1d;color:#ecd282;font-weight:900}.sandbox-deck>p{min-height:64px;margin:0 0 10px;color:#7d8b92}
</style>
