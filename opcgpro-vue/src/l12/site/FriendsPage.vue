<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { connect, inviteFriend, l12State, spectateRoom } from '@/l12/net'
import { friendApi, type PlatformFriend, type PlatformPresence } from '@/l12/platform'

const tab = ref<'friends' | 'requests' | 'add' | 'blocked'>('friends')
const query = ref('')
const friends = ref<PlatformFriend[]>([])
const requests = ref<PlatformFriend[]>([])
const results = ref<PlatformFriend[]>([])
const blocked = ref<PlatformFriend[]>([])
const presence = ref<PlatformPresence[]>([])
const selectedId = ref('')
const busy = ref(false)
const notice = ref('')
let refreshTimer: number | null = null
const incoming = computed(() => requests.value.filter(item => item.direction === 'incoming'))
const outgoing = computed(() => requests.value.filter(item => item.direction === 'outgoing'))
const presenceById = computed(() => new Map(presence.value.map(item => [item.accountId, item])))
const onlineFriends = computed(() => friends.value.filter(item => presenceById.value.get(item.accountId)?.online ?? item.online))
const selected = computed(() => friends.value.find(item => item.accountId === selectedId.value) ?? friends.value[0])
const selectedPresence = computed(() => selected.value ? presenceById.value.get(selected.value.accountId) : undefined)

async function refresh() {
  try {
    ;[friends.value, requests.value, blocked.value, presence.value] = await Promise.all([friendApi.friends(), friendApi.requests(), friendApi.blocked(), friendApi.presence()])
    if (!friends.value.some(item => item.accountId === selectedId.value)) selectedId.value = friends.value[0]?.accountId ?? ''
  }
  catch (error) { notice.value = error instanceof Error ? error.message : '好友数据读取失败' }
}
async function search() {
  busy.value = true; notice.value = ''
  try { results.value = await friendApi.players(query.value.trim()) }
  catch (error) { notice.value = error instanceof Error ? error.message : '搜索失败' }
  finally { busy.value = false }
}
async function add(player: PlatformFriend) {
  try { notice.value = (await friendApi.request(player.accountId)).message; await refresh(); await search() }
  catch (error) { notice.value = error instanceof Error ? error.message : '申请发送失败' }
}
async function resolve(player: PlatformFriend, accept: boolean) {
  try { notice.value = (await friendApi.resolve(player.accountId, accept)).message; await refresh() }
  catch (error) { notice.value = error instanceof Error ? error.message : '申请处理失败' }
}
async function remove(player: PlatformFriend) { await friendApi.remove(player.accountId); notice.value = `已删除好友 ${player.username}`; await refresh() }
async function blockPlayer(player: PlatformFriend) {
  if (!window.confirm(`屏蔽后双方将解除好友关系，确定屏蔽「${player.username}」吗？`)) return
  notice.value = (await friendApi.block(player.accountId)).message; tab.value = 'blocked'; await refresh()
}
async function unblock(player: PlatformFriend) { await friendApi.unblock(player.accountId); notice.value = `已取消屏蔽 ${player.username}`; await refresh() }
async function invite(player: PlatformFriend) {
  const state = presenceById.value.get(player.accountId)
  if (!state?.online || !state.canInvite) { notice.value = state?.actionReason || '该好友当前无法接受邀请'; return }
  try { if (l12State.status !== 'online') await connect(); inviteFriend(player.accountId); notice.value = `正在向 ${player.username} 发送对战邀请…` }
  catch (error) { notice.value = error instanceof Error ? error.message : '邀请发送失败' }
}
async function spectate(player: PlatformFriend) {
  const state = presenceById.value.get(player.accountId)
  if (!state?.canSpectate || !state.roomCode) { notice.value = state?.actionReason || '当前房间不可观战'; return }
  try { if (l12State.status !== 'online') await connect(); spectateRoom(state.roomCode); notice.value = `正在进入 ${player.username} 的对局…` }
  catch (error) { notice.value = error instanceof Error ? error.message : '进入观战失败' }
}
onMounted(async () => { await refresh(); if (l12State.status === 'offline') void connect().catch(() => undefined); refreshTimer = window.setInterval(() => void refresh(), 15_000) })
onBeforeUnmount(() => { if (refreshTimer !== null) window.clearInterval(refreshTimer) })
</script>

<template>
  <div class="friends-page">
    <header><small>FRIENDS & PRESENCE</small><h1>好友</h1><p>管理好友申请、查看在线状态并直接邀请对战。</p></header>
    <div class="tabs"><button :class="{ active: tab === 'friends' }" @click="tab = 'friends'">好友 ({{ friends.length }})</button><button :class="{ active: tab === 'requests' }" @click="tab = 'requests'">申请 {{ incoming.length }}</button><button :class="{ active: tab === 'add' }" @click="tab = 'add'">添加好友</button><button :class="{ active: tab === 'blocked' }" @click="tab = 'blocked'">屏蔽</button></div>
    <p v-if="notice || l12State.notice" class="notice">{{ notice || l12State.notice }}</p>
    <section v-if="tab === 'friends'" class="panel friend-center">
      <aside><div class="toolbar"><b>好友消息</b><span>{{ onlineFriends.length }} 位在线</span></div><button v-for="friend in friends" :key="friend.accountId" :class="{ active: selected?.accountId === friend.accountId }" @click="selectedId = friend.accountId"><div class="avatar">{{ friend.username.slice(0, 1) }}</div><span><b>{{ friend.username }}</b><small><i :class="{ online: presenceById.get(friend.accountId)?.online }"/>{{ presenceById.get(friend.accountId)?.activity === 'playing' ? '对局中' : presenceById.get(friend.accountId)?.online ? '在线' : '离线' }}</small></span></button><p v-if="!friends.length">暂无好友</p></aside>
      <main v-if="selected"><div class="hero-avatar">{{ selected.username.slice(0, 1) }}</div><h2>{{ selected.username }}</h2><p>{{ selectedPresence?.online ? selectedPresence.activity === 'playing' ? '正在对局' : '当前在线' : '当前离线' }}</p><div><button v-if="selectedPresence?.canInvite" @click="invite(selected)">邀请对战</button><button v-else-if="selectedPresence?.canSpectate" @click="spectate(selected)">进入观战</button><button v-else disabled>{{ selectedPresence?.actionReason || '暂不可操作' }}</button><button class="quiet" @click="remove(selected)">删除好友</button><button class="danger" @click="blockPlayer(selected)">屏蔽</button></div></main>
      <div v-else class="empty">选择一位好友<br/>查看状态并发起对战。</div>
    </section>
    <section v-else-if="tab === 'requests'" class="panel request-panel">
      <h2>收到的申请</h2><article v-for="player in incoming" :key="player.accountId"><b>{{ player.username }}</b><div><button class="quiet" @click="resolve(player, false)">拒绝</button><button @click="resolve(player, true)">接受</button></div></article><p v-if="!incoming.length" class="empty compact">暂无待处理申请</p>
      <h2>已发送</h2><article v-for="player in outgoing" :key="player.accountId"><b>{{ player.username }}</b><span>等待对方处理</span></article><p v-if="!outgoing.length" class="empty compact">暂无已发送申请</p>
    </section>
    <section v-else-if="tab === 'add'" class="panel add-panel">
      <label>搜索用户名<input v-model="query" placeholder="输入完整或部分用户名" @keyup.enter="search"/></label><button :disabled="busy || !query.trim()" @click="search">{{ busy ? '搜索中…' : '搜索玩家' }}</button>
      <div class="search-results"><article v-for="player in results" :key="player.accountId"><div><b>{{ player.username }}</b><span>{{ player.online ? '在线' : '离线' }}</span></div><button v-if="player.status === 'none'" @click="add(player)">申请好友</button><span v-else-if="player.status === 'accepted'">已是好友</span><span v-else>{{ player.direction === 'incoming' ? '对方已申请你' : '申请已发送' }}</span></article></div>
    </section>
    <section v-else class="panel blocked-panel"><header><div><h2>已屏蔽玩家</h2><p>屏蔽后双方不能发送好友申请。</p></div><b>{{ blocked.length }}</b></header><article v-for="player in blocked" :key="player.accountId"><div><b>{{ player.username }}</b><span>已屏蔽</span></div><button class="quiet" @click="unblock(player)">取消屏蔽</button></article><div v-if="!blocked.length" class="empty">暂无已屏蔽玩家</div></section>
  </div>
</template>

<style scoped>
.friends-page{width:min(1050px,calc(100% - 40px));min-height:100%;margin:auto;padding:34px 0 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.friends-page>header small{color:#52c3ca;font:900 9px monospace;letter-spacing:.18em}.friends-page h1{margin:5px 0;font-size:30px}.friends-page header p{margin:0;color:#77858c;font-size:11px}.tabs{display:grid;grid-template-columns:repeat(4,1fr);margin:22px 0 14px;border:1px solid #35424b;background:#0a1117}.tabs button{padding:13px;border:0;background:transparent;color:#78868d;font-weight:900}.tabs button.active{background:#0b745b;color:#fff}.panel{border:1px solid #35424b;background:#101821}.friend-center{display:grid;min-height:500px;grid-template-columns:280px 1fr}.friend-center aside{border-right:1px solid #35424b;background:#0c151b}.toolbar{display:flex;align-items:center;justify-content:space-between;gap:20px;padding:14px;border-bottom:1px solid #35424b}.toolbar span{color:#65737a;font-size:10px}.friend-center aside>button{display:grid;width:100%;grid-template-columns:46px 1fr;align-items:center;gap:10px;padding:11px 14px;border:0;border-bottom:1px solid #26343c;background:transparent;color:#fff;text-align:left}.friend-center aside>button.active,.friend-center aside>button:hover{background:#172932}.friend-center aside>button span,.friend-center aside>button b,.friend-center aside>button small{display:block}.friend-center aside>button small{margin-top:4px;color:#748087;font-size:9px}.friend-center aside>p{padding:16px;color:#748087;font-size:10px}.avatar,.hero-avatar{display:grid;place-items:center;border:1px solid #45caa3;background:#087f66;color:#fff;font-weight:900}.avatar{width:42px;height:42px;border-radius:12px}.hero-avatar{width:76px;height:76px;border-radius:18px;font-size:25px}.friend-center small i{display:inline-block;width:7px;height:7px;margin-right:5px;border-radius:50%;background:#60696d}.friend-center small i.online{background:#54c69a;box-shadow:0 0 7px #54c69a}.friend-center main{display:flex;flex-direction:column;align-items:center;justify-content:center;padding:28px;text-align:center}.friend-center main h2{margin:14px 0 4px}.friend-center main>p{margin:0;color:#748087;font-size:10px}.friend-center main>div:last-child{display:flex;gap:8px;margin-top:22px}.friend-center main button,.add-panel button,.request-panel button,.blocked-panel button{padding:9px 12px;border:1px solid #3bc09a;background:#0b755c;color:#fff;font-size:10px;font-weight:900}.friend-center main button:disabled{opacity:.4}.quiet{border-color:#4b565c!important;background:#0b1217!important;color:#8c969a!important}.danger{border-color:#8a3644!important;background:#261218!important;color:#e8a8b2!important}.empty{display:grid;min-height:260px;place-items:center;color:#748188;text-align:center;line-height:1.9}.empty.compact{min-height:90px}.add-panel,.blocked-panel{padding:24px}.add-panel label{display:block;color:#aab1b4;font-size:11px;font-weight:900}.add-panel input{display:block;width:100%;margin-top:8px;padding:11px;border:1px solid #48555e;background:#070d12;color:#fff}.add-panel>button{margin-top:12px}.search-results{margin-top:18px}.search-results article,.request-panel article,.blocked-panel article{display:grid;grid-template-columns:1fr auto;align-items:center;gap:12px;padding:13px 16px;border-bottom:1px solid rgba(235,230,216,.09)}.search-results span,.blocked-panel span{color:#7d8a91;font-size:10px}.request-panel{padding:16px}.request-panel h2{margin:8px 0;padding:10px 0;border-bottom:1px solid #35424b;font-size:14px}.request-panel article div{display:flex;gap:8px}.request-panel article span{color:#758188;font-size:10px}.blocked-panel>header{display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #35424b}.blocked-panel>header p{color:#748087;font-size:10px}.blocked-panel>header>b{color:#e2c36f;font-size:24px}.blocked-panel article div b,.blocked-panel article div span{display:block}.notice{padding:10px;border-left:3px solid #d2b45f;background:#211b0e;color:#e6cc84;font-size:10px}@media(max-width:700px){.friends-page{width:auto;padding:20px 12px 48px}.tabs{grid-template-columns:1fr 1fr}.friend-center{grid-template-columns:1fr}.friend-center aside{max-height:280px;overflow:auto;border-right:0;border-bottom:1px solid #35424b}.friend-center main{min-height:280px}.friend-center main>div:last-child{width:100%;flex-direction:column}.friend-center main button{width:100%}.toolbar{align-items:stretch;flex-direction:column}.add-panel{padding:18px}}
</style>
