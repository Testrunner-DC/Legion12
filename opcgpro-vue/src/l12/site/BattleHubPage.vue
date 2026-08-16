<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { connect, createRoom, joinRoom, l12State, selectCustomDeck, selectDeck, setReady, spectateRoom, type RoomOptions } from '@/l12/net'
import { loadSavedDecks } from '@/l12/decks'

const router = useRouter()
const tab = ref<'match' | 'friendly' | 'sandbox'>('friendly')
const roomCode = ref('')
const roomOptions = ref<RoomOptions>({ spectating: 'public', handVisibility: 'request' })
const customDecks = ref(loadSavedDecks())
const me = computed(() => l12State.room?.players.find(player => player.playerIndex === l12State.room?.yourPlayerIndex))

async function ensureConnected() {
  if (!l12State.nickname.trim()) { l12State.notice = '请先输入昵称'; return false }
  if (l12State.status !== 'online') await connect()
  return true
}
async function onCreate() { try { if (await ensureConnected()) createRoom(roomOptions.value) } catch {} }
async function onJoin() { try { if (await ensureConnected()) joinRoom(roomCode.value.trim()) } catch {} }
async function onSpectate() { try { if (await ensureConnected()) spectateRoom(roomCode.value.trim()) } catch {} }
</script>

<template>
  <div class="battle-hub">
    <header class="page-head"><div><small>BATTLE LOBBY</small><h1>开始对战</h1><p>选择模式并确认当前牌库，准备后进入对局。</p></div><div class="server-state" :class="l12State.status"><i/><span>{{ l12State.status === 'online' ? '服务器在线' : '尚未连接' }}</span></div></header>

    <section v-if="l12State.room" class="room-stage panel">
      <header><div><small>FRIENDLY ROOM</small><h2>友谊战整备室</h2></div><code>{{ l12State.room.roomCode }}</code></header>
      <div class="versus">
        <article v-for="index in [0,1]" :key="index" :class="{ empty: !l12State.room.players[index] }"><span>PLAYER {{ index + 1 }}</span><b>{{ l12State.room.players[index]?.name || '等待玩家' }}</b><p>{{ l12State.room.players[index]?.deckName || '尚未选择牌库' }}</p><em>{{ l12State.room.players[index]?.ready ? '已准备' : '未准备' }}</em></article><strong>VS</strong>
      </div>
      <div class="room-decks"><button v-for="deck in l12State.room.decks" :key="deck.index" :class="{ active: me?.deckIndex === deck.index }" :disabled="me?.ready" @click="selectDeck(deck.index)"><b>{{ deck.name }}</b><span>{{ deck.masterName }}</span></button><button v-for="deck in customDecks" :key="deck.name" :class="{ active: me?.customDeck && me?.deckName === deck.name }" :disabled="me?.ready" @click="selectCustomDeck(deck)"><b>{{ deck.name }}</b><span>自定义 · {{ deck.cardIds.length }} 张</span></button></div>
      <footer><router-link to="/deck-editor">编辑我的牌库</router-link><button class="primary" :disabled="l12State.room.players.length < 2" @click="setReady(!me?.ready)">{{ me?.ready ? '取消准备' : '准备对战' }}</button></footer>
    </section>

    <template v-else>
      <section class="current-deck panel"><div class="deck-thumb">库</div><div><small>当前牌库</small><b>{{ Object.values(customDecks)[0]?.name || '尚未选择' }}</b><span>{{ Object.values(customDecks)[0] ? `${Object.values(customDecks)[0].cardIds.length} 张主牌` : '前往牌库页面建立或选择牌库' }}</span></div><router-link to="/decks">更换 →</router-link></section>
      <div class="mode-tabs"><button :class="{ active: tab === 'match' }" @click="tab = 'match'">匹配</button><button :class="{ active: tab === 'friendly' }" @click="tab = 'friendly'">好友房</button><button :class="{ active: tab === 'sandbox' }" @click="tab = 'sandbox'">单人</button></div>

      <section v-if="tab === 'match'" class="mode-panel panel"><small>PUBLIC MATCH</small><h2>公开匹配</h2><p>排位与休闲匹配的数据服务尚未接入。页面结构已预留，不会用测试数据伪造排行榜或匹配结果。</p><div class="match-options"><button disabled>排位匹配</button><button disabled>休闲匹配</button></div><button class="primary" disabled>匹配服务待接入</button></section>

      <section v-else-if="tab === 'friendly'" class="mode-panel panel friendly-panel"><small>FRIENDLY ROOM</small><h2>创建、加入或观战房间</h2><label>玩家昵称<input v-model="l12State.nickname" maxlength="16" placeholder="输入玩家昵称"/></label><div class="join-row"><button class="primary" @click="onCreate">创建新房间</button><span>房间码</span><input v-model="roomCode" maxlength="6" placeholder="输入 6 位房间码" @keyup.enter="onJoin"/><div class="join-actions"><button @click="onJoin">加入对战</button><button class="spectate-button" @click="onSpectate">直接观战</button></div></div><div class="room-settings"><div><b>观战权限</b><select v-model="roomOptions.spectating"><option value="public">允许所有玩家直接观战</option><option value="friends">仅限好友观战</option><option value="disabled">禁止观战</option></select></div><div><b>观战者查看手牌</b><select v-model="roomOptions.handVisibility"><option value="request">需要当局玩家同意</option><option value="public">默认公开</option></select></div></div></section>

      <section v-else class="mode-panel panel"><small>TEST SANDBOX</small><h2>单人测试沙盒</h2><p>用于验证牌库、卡效、阶段与交互，不计入玩家战绩和排行榜。</p><button class="primary" @click="router.push('/sandbox')">进入测试沙盒</button></section>
    </template>
    <p v-if="l12State.notice" class="battle-notice">{{ l12State.notice }}</p>
  </div>
</template>

<style scoped>
.battle-hub{width:min(980px,calc(100% - 40px));min-height:100%;margin:0 auto;padding:34px 0 60px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.page-head{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:22px}.page-head small,.panel>small,.panel header small{color:#50c4cc;font:900 9px monospace;letter-spacing:.2em}.page-head h1{margin:5px 0;font-size:30px}.page-head p,.mode-panel>p{margin:0;color:#7c8990;font-size:12px;line-height:1.7}.server-state{display:flex;align-items:center;gap:8px;color:#7b858a;font-size:11px;font-weight:900}.server-state i{width:8px;height:8px;border-radius:50%;background:#687177}.server-state.online i{background:#54c695;box-shadow:0 0 8px #54c695}.panel{border:1px solid rgba(235,230,216,.17);background:#101821;box-shadow:0 18px 50px rgba(0,0,0,.18)}.current-deck{display:grid;grid-template-columns:58px 1fr auto;align-items:center;gap:16px;padding:18px}.deck-thumb{display:grid;width:52px;height:70px;place-items:center;border:1px solid #d2b76f;background:linear-gradient(145deg,#6e1825,#13252a);font-size:20px;font-weight:900}.current-deck small,.current-deck b,.current-deck span{display:block}.current-deck small{color:#728089;font-size:10px}.current-deck b{margin:4px 0;font-size:17px}.current-deck span{color:#7f8b91;font-size:10px}.current-deck a{color:#e5c573;font-size:12px;font-weight:900;text-decoration:none}.mode-tabs{display:grid;grid-template-columns:repeat(3,1fr);margin:18px 0;border:1px solid rgba(235,230,216,.17);background:#0b1117}.mode-tabs button{padding:14px;border:0;background:transparent;color:#738089;font-weight:900}.mode-tabs button.active{background:linear-gradient(135deg,#8b1c2a,#ad2d38);color:#fff}.mode-panel{padding:28px}.mode-panel h2{margin:6px 0 8px;font-size:23px}.mode-panel>p{max-width:650px}.mode-panel label{display:block;margin:20px 0 12px;color:#aab2b4;font-size:11px;font-weight:900}.mode-panel input,.mode-panel select{width:100%;padding:12px;border:1px solid #45535c;background:#080e14;color:#fff;outline:none}.mode-panel input:focus,.mode-panel select:focus{border-color:#50c4cc}.primary{border-color:#e2c473!important;background:#e2c473!important;color:#0a0d0f!important;font-weight:900}.mode-panel>button.primary{min-width:220px;margin-top:22px;padding:13px;border:1px solid}.match-options{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:20px}.match-options button{padding:15px;border:1px solid #3e4a52;background:#0b1117;color:#7b868b}.join-row{display:grid;grid-template-columns:1fr auto 1fr auto;align-items:center;gap:9px}.join-row button{height:42px;padding:0 18px;border:1px solid #52606a;background:#121c24;color:#fff}.join-row span{color:#68757c;font-size:10px}.room-settings{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:18px}.room-settings>div{padding:14px;border:1px solid #354149;background:#0b1218}.room-settings b{display:block;margin-bottom:8px;font-size:11px}.room-stage{padding:26px}.room-stage>header{display:flex;align-items:center;justify-content:space-between}.room-stage h2{margin:4px 0}.room-stage code{padding:9px 12px;border:1px solid #d9bc6d;color:#f0d889;font-size:17px;letter-spacing:.16em}.versus{position:relative;display:grid;grid-template-columns:1fr 1fr;gap:60px;margin:24px 0}.versus article{display:flex;min-height:150px;flex-direction:column;align-items:center;justify-content:center;border:1px solid #37434a;background:#0a1117}.versus article.empty{opacity:.55}.versus span{color:#66747c;font:900 9px monospace;letter-spacing:.15em}.versus article>b{margin:10px 0 4px;font-size:20px}.versus p{margin:0;color:#78858b;font-size:10px}.versus em{margin-top:12px;color:#d9bb68;font-size:10px;font-style:normal;font-weight:900}.versus>strong{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);color:#a52b38}.room-decks{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}.room-decks button{padding:11px;border:1px solid #3f4c54;background:#0b1218;color:#fff;text-align:left}.room-decks button.active{border-color:#e0c16d;background:#202017}.room-decks b,.room-decks span{display:block}.room-decks span{margin-top:4px;color:#77848a;font-size:9px}.room-stage footer{display:flex;align-items:center;justify-content:space-between;margin-top:20px}.room-stage footer a{color:#55c4ca;font-size:11px;text-decoration:none}.room-stage footer button{min-width:220px;padding:12px;border:1px solid}.battle-notice{padding:11px;border-left:3px solid #a52b38;background:#211016;color:#e6a8ad;font-size:11px}
@media(max-width:700px){.battle-hub{width:auto;padding:20px 12px 50px}.page-head{gap:12px}.page-head h1{font-size:25px}.current-deck{grid-template-columns:48px 1fr}.current-deck a{grid-column:1/-1;text-align:right}.mode-panel{padding:20px}.join-row{grid-template-columns:1fr}.join-row span{text-align:center}.room-settings,.match-options,.versus,.room-decks{grid-template-columns:1fr}.versus{gap:10px}.versus>strong{display:none}.room-stage footer{align-items:stretch;flex-direction:column;gap:12px}.room-stage footer button{width:100%}}
.join-actions{display:flex;gap:6px}.join-actions .spectate-button{border-color:#4faeb5;color:#80dce2}
@media(max-width:700px){.join-actions{display:grid;grid-template-columns:1fr 1fr}}
</style>
