<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { l12State } from '@/l12/net'

interface Friend { id: string; name: string; addedAt: string }
const router = useRouter()
const tab = ref<'friends' | 'requests' | 'add'>('friends')
const query = ref('')
const notice = ref('')
const friends = ref<Friend[]>(load())

function load(): Friend[] { try { return JSON.parse(localStorage.getItem('l12-friends-v1') || '[]') } catch { return [] } }
function persist() { localStorage.setItem('l12-friends-v1', JSON.stringify(friends.value)) }
function addFriend() {
  const name = query.value.trim()
  if (!name) return
  if (friends.value.some(friend => friend.name === name)) { notice.value = '该玩家已经在好友列表中'; return }
  friends.value.push({ id: crypto.randomUUID(), name, addedAt: new Date().toISOString() }); persist(); query.value = ''; tab.value = 'friends'; notice.value = '好友已添加到当前设备'
}
function removeFriend(id: string) { friends.value = friends.value.filter(friend => friend.id !== id); persist() }
function invite(friend: Friend) { l12State.notice = `请先在好友房创建房间，再向 ${friend.name} 分享房间码`; router.push('/lobby') }
const filtered = computed(() => friends.value.filter(friend => friend.name.toLocaleLowerCase('zh-CN').includes(query.value.trim().toLocaleLowerCase('zh-CN'))))
</script>

<template>
  <div class="friends-page">
    <header><div><small>FRIENDS</small><h1>好友中心</h1><p>管理好友与申请，并从好友房发起对战邀请。</p></div></header>
    <div class="tabs"><button :class="{ active: tab === 'friends' }" @click="tab = 'friends'">好友 {{ friends.length }}</button><button :class="{ active: tab === 'requests' }" @click="tab = 'requests'">申请</button><button :class="{ active: tab === 'add' }" @click="tab = 'add'">添加好友</button></div>
    <section v-if="tab === 'friends'" class="panel"><div class="toolbar"><input v-model="query" placeholder="搜索好友"/><span>不提供私聊；对战中使用当局对话。</span></div><div v-if="filtered.length" class="friend-list"><article v-for="friend in filtered" :key="friend.id"><div class="avatar">{{ friend.name.slice(0,1) }}</div><div><b>{{ friend.name }}</b><span>好友 · 当前状态不可用</span></div><button @click="invite(friend)">邀请对战</button><button class="quiet" @click="removeFriend(friend.id)">删除</button></article></div><div v-else class="empty">暂无好友</div></section>
    <section v-else-if="tab === 'requests'" class="panel empty"><div><b>暂无好友申请</b><p>服务端账户与好友申请同步尚未接入。</p></div></section>
    <section v-else class="panel add-panel"><label>玩家昵称或账号<input v-model="query" maxlength="32" placeholder="输入完整昵称或账号" @keyup.enter="addFriend"/></label><button :disabled="!query.trim()" @click="addFriend">发送好友申请</button><p>当前开发版会将玩家直接加入本机好友列表；正式账户服务接入后改为申请与接受流程。</p></section>
    <p v-if="notice" class="notice">{{ notice }}</p>
  </div>
</template>

<style scoped>
.friends-page{width:min(1050px,calc(100% - 40px));min-height:100%;margin:auto;padding:34px 0 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.friends-page>header small{color:#52c3ca;font:900 9px monospace;letter-spacing:.18em}.friends-page h1{margin:5px 0;font-size:30px}.friends-page header p{margin:0;color:#77858c;font-size:11px}.tabs{display:grid;grid-template-columns:repeat(3,1fr);margin:22px 0 14px;border:1px solid #35424b;background:#0a1117}.tabs button{padding:13px;border:0;background:transparent;color:#78868d;font-weight:900}.tabs button.active{background:#16343a;color:#7bdddf}.panel{border:1px solid #35424b;background:#101821}.toolbar{display:flex;align-items:center;justify-content:space-between;gap:20px;padding:14px;border-bottom:1px solid #35424b}.toolbar input,.add-panel input{min-width:280px;padding:11px;border:1px solid #48555e;background:#070d12;color:#fff}.toolbar span{color:#65737a;font-size:10px}.friend-list article{display:grid;grid-template-columns:46px 1fr auto auto;align-items:center;gap:12px;padding:13px 16px;border-bottom:1px solid rgba(235,230,216,.09)}.avatar{display:grid;width:42px;height:42px;place-items:center;border:1px solid #d6b864;border-radius:50%;background:#18252d;color:#e4c777;font-weight:900}.friend-list b,.friend-list span{display:block}.friend-list span{margin-top:4px;color:#748087;font-size:9px}.friend-list button,.add-panel button{padding:9px 12px;border:1px solid #54bdc5;background:#143b41;color:#fff;font-size:10px;font-weight:900}.friend-list button.quiet{border-color:#4b565c;background:#0b1217;color:#8c969a}.empty{display:grid;min-height:330px;place-items:center;color:#748188;text-align:center}.empty p{font-size:11px}.add-panel{padding:24px}.add-panel label{display:block;color:#aab1b4;font-size:11px;font-weight:900}.add-panel input{display:block;width:100%;margin-top:8px}.add-panel button{margin-top:12px}.add-panel p{color:#6d7980;font-size:10px;line-height:1.7}.notice{padding:10px;border-left:3px solid #d2b45f;background:#211b0e;color:#e6cc84;font-size:10px}
@media(max-width:700px){.friends-page{width:auto;padding:20px 12px 48px}.toolbar{align-items:stretch;flex-direction:column}.toolbar input{min-width:0;width:100%}.friend-list article{grid-template-columns:42px 1fr}.friend-list button{width:100%}.add-panel{padding:18px}}
</style>
