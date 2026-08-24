<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { connect, createRoom, joinRoom, l12State, selectCustomDeck, selectDeck, setReady } from './net'
import { ensureOfficialPrebuiltDecks, loadSavedDecks } from './decks'
import CardArchive from './CardArchive.vue'
import MatchRecords from './MatchRecords.vue'
import { platformState } from './platform'

const roomCode = ref('')
const router = useRouter()
const customDecks = ref(loadSavedDecks())
const view = ref<'home' | 'room' | 'cards' | 'replay'>('home')
const me = computed(() => l12State.room?.players.find(player => player.playerIndex === l12State.room?.yourPlayerIndex))

onMounted(async () => { customDecks.value = await ensureOfficialPrebuiltDecks() })

async function ensureConnected() {
  if (!platformState.account || !platformState.token) { l12State.notice = '请先登录账号'; return false }
  if (l12State.status !== 'online') await connect()
  return true
}
async function onCreate() { try { if (await ensureConnected()) { createRoom(); view.value = 'room' } } catch { } }
async function onJoin() { try { if (await ensureConnected()) { joinRoom(roomCode.value); view.value = 'room' } } catch { } }
</script>

<template>
  <div class="grand-shell">
    <header class="grand-topbar">
      <div class="top-brand"><b>XII</b><span>十二军团<small>ONLINE BATTLE</small></span></div>
      <div class="top-center">服务器权威制 · S01 卡效持续验证</div>
      <div class="top-user"><i :class="l12State.status"/><span>{{ l12State.nickname || '游客' }}</span><small>{{ l12State.status }}</small></div>
    </header>

    <nav class="grand-sidebar">
      <button :class="{ active: view === 'home' }" @click="view = 'home'"><b>⌂</b><span>主页</span></button>
      <button :class="{ active: view === 'room' }" @click="view = 'room'"><b>⚔</b><span>房间</span></button>
      <button :class="{ active: view === 'cards' }" @click="view = 'cards'"><b>▦</b><span>卡牌</span></button>
      <button @click="router.push('/deck-editor')"><b>▤</b><span>牌库</span></button>
      <button :class="{ active: view === 'replay' }" @click="view = 'replay'"><b>↺</b><span>记录</span></button>
      <div class="side-spacer"/><button disabled><b>⚙</b><span>设置</span></button>
    </nav>

    <main class="grand-main">
      <template v-if="l12State.room">
        <section class="friendly-room grand-panel">
          <i class="corner tl"/><i class="corner tr"/><i class="corner bl"/><i class="corner br"/>
          <p class="kicker">FRIENDLY MATCH · {{ l12State.room.roomCode }}</p>
          <h1>友谊战整备室</h1>
          <div class="versus-grid">
            <article v-for="index in [0,1]" :key="index" :class="{ vacant: !l12State.room.players[index] }">
              <small>PLAYER {{ index + 1 }}</small>
              <div class="commander-glyph">{{ l12State.room.players[index]?.deckName?.slice(0, 1) || '待' }}</div>
              <h2>{{ l12State.room.players[index]?.name || '等待玩家' }}</h2>
              <p>{{ l12State.room.players[index]?.deckName }} · {{ l12State.room.players[index]?.masterName }}</p>
              <b :class="{ ready: l12State.room.players[index]?.ready }">{{ l12State.room.players[index]?.ready ? '已准备' : '未准备' }}</b>
            </article>
            <em>VS</em>
          </div>
          <div class="deck-picker">
            <small>选择预组或自定义牌库</small>
            <div>
              <button v-for="deck in l12State.room.decks" :key="deck.index"
                :class="{ selected: me?.deckIndex === deck.index }" :disabled="me?.ready"
                @click="selectDeck(deck.index)">
                <b>{{ deck.name }}</b><span>{{ deck.masterName }}</span>
              </button>
              <button v-for="deck in customDecks" :key="`custom-${deck.name}`"
                :class="{ selected: me?.customDeck && me?.deckName === deck.name }" :disabled="me?.ready"
                @click="selectCustomDeck(deck)">
                <b>{{ deck.name }}</b><span>自定义 · {{ deck.cardIds.length }} 张</span>
              </button>
            </div>
            <button class="deck-editor-link" :disabled="me?.ready" @click="router.push('/deck-editor')">＋ 编辑我的牌库</button>
          </div>
          <div class="room-actions">
            <span>房间代码 <strong>{{ l12State.room.roomCode }}</strong></span>
            <button class="primary" :disabled="l12State.room.players.length < 2" @click="setReady(!me?.ready)">{{ me?.ready ? '取消准备' : '准备对战' }}</button>
          </div>
        </section>
      </template>

      <template v-else-if="view === 'home'">
        <section class="command-hero grand-panel">
          <i class="corner tl"/><i class="corner tr"/><i class="corner bl"/><i class="corner br"/>
          <div><p class="kicker">COMMAND QUARTERS</p><h1>欢迎来到<br/><span>十二军团</span></h1><p>选择预组、建立房间，并向另一位军团长发出挑战。</p></div>
          <div class="hero-seal">XII<small>LEGIONS</small></div>
        </section>
        <section class="home-actions">
          <button class="mode-card primary-mode" @click="view = 'room'"><i>⚔</i><div><small>FRIENDLY MATCH</small><h2>友谊对战</h2><p>使用六位房间码建立 1v1 对局</p></div><b>→</b></button>
          <button class="mode-card" @click="view = 'cards'"><i>▦</i><div><small>CARD ARCHIVE</small><h2>卡牌档案</h2><p>已收录 248 张 S01–S02 卡牌数据</p></div><b>→</b></button>
          <button class="mode-card" @click="router.push('/deck-editor')"><i>▤</i><div><small>DECK BUILDER</small><h2>牌库编辑器</h2><p>建立、校验并保存 40–50 张自定义牌库</p></div><b>→</b></button>
          <button class="mode-card" @click="view = 'replay'"><i>↺</i><div><small>MATCH RECORDS</small><h2>对局记录</h2><p>SQLite 完整记录 · 逐操作快照回看</p></div><b>→</b></button>
        </section>
      </template>

      <template v-else-if="view === 'room'">
        <section class="room-workspace grand-panel">
          <i class="corner tl"/><i class="corner tr"/><i class="corner bl"/><i class="corner br"/>
          <p class="kicker">FRIENDLY ROOM</p><h1>建立友谊战</h1>
          <div class="terminal-form">
            <label>当前账号<input :value="platformState.account?.username || '尚未登录'" disabled/></label>
            <label>服务器地址<input v-model="l12State.endpoint" spellcheck="false"/></label>
            <button class="primary" @click="onCreate">创建新房间</button>
            <div class="or"><span>或使用房间代码</span></div>
            <div class="join-line"><input v-model="roomCode" maxlength="6" placeholder="六位房间码" @keyup.enter="onJoin"/><button @click="onJoin">加入房间</button></div>
          </div>
        </section>
      </template>

      <CardArchive v-else-if="view === 'cards'"/>

      <MatchRecords v-else/>
    </main>

    <aside class="grand-info-rail">
      <section class="grand-panel"><p class="kicker">SERVER</p><h3>连接状态</h3><div class="server-line"><i :class="l12State.status"/><span>{{ l12State.status }}</span></div><small>{{ l12State.endpoint }}</small></section>
      <section class="grand-panel"><p class="kicker">TEST DECKS</p><h3>预设军团</h3><article><b>天廷</b><span>杨戬 · 40+8</span></article><article><b>高天原</b><span>须佐之男 · 40+8</span></article><article><b>太阳城</b><span>梅杰德 · 40+8</span></article><article><b>阿斯加德</b><span>洛基 · 40+8</span></article></section>
      <section class="grand-panel rail-grow"><p class="kicker">DEVELOPMENT</p><h3>当前范围</h3><ul><li>房间码 1v1</li><li>四套 S01 预组选择</li><li>SQLite 快照回看</li><li>S01 卡效持续回归</li></ul></section>
      <p v-if="l12State.notice" class="notice">{{ l12State.notice }}</p>
    </aside>
  </div>
</template>
