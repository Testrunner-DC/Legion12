<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { connect, inviteFriend, l12State } from '@/l12/net'
import { friendApi, type PlatformFriend } from '@/l12/platform'

const tab = ref<'friends' | 'requests' | 'add'>('friends')
const query = ref('')
const friends = ref<PlatformFriend[]>([])
const requests = ref<PlatformFriend[]>([])
const results = ref<PlatformFriend[]>([])
const busy = ref(false)
const notice = ref('')
let refreshTimer: number | null = null
const incoming = computed(() => requests.value.filter(item => item.direction === 'incoming'))
const outgoing = computed(() => requests.value.filter(item => item.direction === 'outgoing'))
const onlineFriends = computed(() => friends.value.filter(item => item.online))

async function refresh() {
  try { [friends.value, requests.value] = await Promise.all([friendApi.friends(), friendApi.requests()]) }
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
async function invite(player: PlatformFriend) {
  if (!player.online) { notice.value = '该好友当前不在线'; return }
  try { if (l12State.status !== 'online') await connect(); inviteFriend(player.accountId); notice.value = `正在向 ${player.username} 发送对战邀请…` }
  catch (error) { notice.value = error instanceof Error ? error.message : '邀请发送失败' }
}
onMounted(async () => { await refresh(); if (l12State.status === 'offline') void connect().catch(() => undefined); refreshTimer = window.setInterval(() => void refresh(), 15_000) })
onBeforeUnmount(() => { if (refreshTimer !== null) window.clearInterval(refreshTimer) })
</script>

<template>
  <div class="friends-page">
    <header><small>FRIENDS & PRESENCE</small><h1>好友</h1><p>管理好友申请、查看在线状态并直接邀请对战。</p></header>
    <div class="tabs"><button :class="{ active: tab === 'friends' }" @click="tab = 'friends'">好友 {{ friends.length }} · 在线 {{ onlineFriends.length }}</button><button :class="{ active: tab === 'requests' }" @click="tab = 'requests'">申请 {{ incoming.length }}</button><button :class="{ active: tab === 'add' }" @click="tab = 'add'">添加好友</button></div>
    <p v-if="notice || l12State.notice" class="notice">{{ notice || l12State.notice }}</p>
    <section v-if="tab === 'friends'" class="panel">
      <div class="toolbar"><b>好友列表</b><span>邀请被接受后将直接创建房间，发起方为房主。</span></div>
      <div v-if="friends.length" class="friend-list"><article v-for="friend in friends" :key="friend.accountId"><div class="avatar">{{ friend.username.slice(0, 1) }}</div><div><b>{{ friend.username }}</b><span><i :class="{ online: friend.online }"/>{{ friend.online ? '在线' : '离线' }}</span></div><button :disabled="!friend.online" @click="invite(friend)">邀请对战</button><button class="quiet" @click="remove(friend)">删除</button></article></div>
      <div v-else class="empty">暂无好友<br/>前往“添加好友”按用户名搜索玩家。</div>
    </section>
    <section v-else-if="tab === 'requests'" class="panel request-panel">
      <h2>收到的申请</h2><article v-for="player in incoming" :key="player.accountId"><b>{{ player.username }}</b><div><button class="quiet" @click="resolve(player, false)">拒绝</button><button @click="resolve(player, true)">接受</button></div></article><p v-if="!incoming.length" class="empty compact">暂无待处理申请</p>
      <h2>已发送</h2><article v-for="player in outgoing" :key="player.accountId"><b>{{ player.username }}</b><span>等待对方处理</span></article><p v-if="!outgoing.length" class="empty compact">暂无已发送申请</p>
    </section>
    <section v-else class="panel add-panel">
      <label>搜索用户名<input v-model="query" placeholder="输入完整或部分用户名" @keyup.enter="search"/></label><button :disabled="busy || !query.trim()" @click="search">{{ busy ? '搜索中…' : '搜索玩家' }}</button>
      <div class="search-results"><article v-for="player in results" :key="player.accountId"><div><b>{{ player.username }}</b><span>{{ player.online ? '在线' : '离线' }}</span></div><button v-if="player.status === 'none'" @click="add(player)">申请好友</button><span v-else-if="player.status === 'accepted'">已是好友</span><span v-else>{{ player.direction === 'incoming' ? '对方已申请你' : '申请已发送' }}</span></article></div>
    </section>
  </div>
</template>

<style scoped>
.friends-page{width:min(1050px,calc(100% - 40px));min-height:100%;margin:auto;padding:34px 0 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.friends-page>header small{color:#52c3ca;font:900 9px monospace;letter-spacing:.18em}.friends-page h1{margin:5px 0;font-size:30px}.friends-page header p{margin:0;color:#77858c;font-size:11px}.tabs{display:grid;grid-template-columns:repeat(3,1fr);margin:22px 0 14px;border:1px solid #35424b;background:#0a1117}.tabs button{padding:13px;border:0;background:transparent;color:#78868d;font-weight:900}.tabs button.active{background:#16343a;color:#7bdddf}.panel{border:1px solid #35424b;background:#101821}.toolbar{display:flex;align-items:center;justify-content:space-between;gap:20px;padding:14px;border-bottom:1px solid #35424b}.toolbar span{color:#65737a;font-size:10px}.friend-list article,.request-panel article,.search-results article{display:grid;grid-template-columns:46px 1fr auto auto;align-items:center;gap:12px;padding:13px 16px;border-bottom:1px solid rgba(235,230,216,.09)}.avatar{display:grid;width:42px;height:42px;place-items:center;border:1px solid #d6b864;border-radius:50%;background:#18252d;color:#e4c777;font-weight:900}.friend-list b,.friend-list span{display:block}.friend-list span{margin-top:4px;color:#748087;font-size:9px}.friend-list span i{display:inline-block;width:7px;height:7px;margin-right:5px;border-radius:50%;background:#60696d}.friend-list span i.online{background:#54c69a;box-shadow:0 0 7px #54c69a}.friend-list button,.add-panel button,.request-panel button{padding:9px 12px;border:1px solid #54bdc5;background:#143b41;color:#fff;font-size:10px;font-weight:900}.friend-list button:disabled{opacity:.35}.friend-list button.quiet,.request-panel button.quiet{border-color:#4b565c;background:#0b1217;color:#8c969a}.empty{display:grid;min-height:260px;place-items:center;color:#748188;text-align:center;line-height:1.9}.empty.compact{min-height:90px}.add-panel{padding:24px}.add-panel label{display:block;color:#aab1b4;font-size:11px;font-weight:900}.add-panel input{display:block;width:100%;margin-top:8px;padding:11px;border:1px solid #48555e;background:#070d12;color:#fff}.add-panel>button{margin-top:12px}.search-results{margin-top:18px}.search-results article{grid-template-columns:1fr auto}.search-results span{color:#7d8a91;font-size:10px}.request-panel{padding:16px}.request-panel h2{margin:8px 0;padding:10px 0;border-bottom:1px solid #35424b;font-size:14px}.request-panel article{grid-template-columns:1fr auto}.request-panel article div{display:flex;gap:8px}.request-panel article span{color:#758188;font-size:10px}.notice{padding:10px;border-left:3px solid #d2b45f;background:#211b0e;color:#e6cc84;font-size:10px}@media(max-width:700px){.friends-page{width:auto;padding:20px 12px 48px}.toolbar{align-items:stretch;flex-direction:column}.friend-list article{grid-template-columns:42px 1fr}.friend-list button{width:100%}.add-panel{padding:18px}}
</style>
