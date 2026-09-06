<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { l12State } from '../net'
import { friendApi, platformState, type PlatformPresence } from '../platform'
defineEmits<{ settings: [] }>()
const players = ref<PlatformPresence[]>([])
const showOnline = ref(false)
const loaded = ref(false)
const stale = ref(false)
let timer = 0
let reading = false
let disposed = false
const connection = computed(() => {
  if (l12State.connectionIssue === 'authentication') return '登录状态失效'
  if (l12State.connectionIssue === 'superseded') return '已由其他页面接管'
  if (l12State.connectionIssue === 'maintenance') return '维护中'
  if (l12State.status === 'connecting') return l12State.recoveryPhase === 'snapshot-received' ? '快照确认中' : '连接恢复中'
  return l12State.status === 'online' ? '连接正常' : '对战连接中断'
})
async function refresh() {
  if (reading || !platformState.account) return
  const accountId = platformState.account.id
  reading = true
  try {
    const data = await friendApi.presence()
    if (disposed || platformState.account?.id !== accountId) return
    players.value = data; loaded.value = true; stale.value = false
  } catch { stale.value = true }
  finally { reading = false }
}
function focus() { void refresh() }
function keydown(event: KeyboardEvent) { if (event.key === 'Escape') showOnline.value = false }
onMounted(() => { void refresh(); timer = window.setInterval(refresh, 15000); window.addEventListener('focus', focus); window.addEventListener('keydown', keydown) })
onBeforeUnmount(() => { disposed = true; clearInterval(timer); window.removeEventListener('focus', focus); window.removeEventListener('keydown', keydown) })
</script>
<template>
  <aside class="battle-utility-dock" aria-label="对局通用功能">
    <button class="battle-settings-button" aria-label="打开对局设置" @click="$emit('settings')">⚙ 设置</button>
    <button @click="showOnline = true; refresh()">在线人数 {{ loaded ? players.length : '—' }}{{ stale ? '（待刷新）' : '' }}</button>
    <span :class="l12State.status" role="status"><i/>{{ connection }}</span>
  </aside>
  <Teleport to="body"><div v-if="showOnline" class="battle-online-mask" @click.self="showOnline = false">
    <section role="dialog" aria-modal="true" aria-labelledby="battle-online-title"><header><h2 id="battle-online-title">在线玩家</h2><button aria-label="关闭在线玩家" @click="showOnline = false">关闭</button></header>
      <p v-if="stale">在线名单暂未刷新，保留上次成功读取的结果。</p>
      <ul><li v-for="player in players" :key="player.accountId"><b>{{ player.username }}</b><span>{{ player.accountId === platformState.account?.id ? '我方' : ({ idle: '空闲', inRoom: '房间中', playing: '对局中', spectating: '观战中' }[player.activity]) }}</span></li></ul>
      <p v-if="!players.length">{{ loaded ? '暂无在线玩家' : '正在读取在线名单…' }}</p>
    </section>
  </div></Teleport>
</template>
<style scoped>
.battle-utility-dock{position:fixed;z-index:1600;left:10px;bottom:10px;display:flex;flex-direction:column;align-items:stretch;gap:4px;max-width:min(220px,calc(100vw - 20px));padding:6px;border:1px solid #46535b;background:#080d11ed;font-size:max(14px,var(--l12-board-readable,14px))}.battle-utility-dock button{min-height:34px;padding:5px 9px;border:1px solid #44525b;background:#101b23;color:#e9d285;text-align:left;font-size:max(14px,var(--l12-board-readable,14px))}.battle-utility-dock>span{display:flex;align-items:center;gap:7px;padding:5px;color:#df9da9;overflow-wrap:anywhere}.battle-utility-dock i{flex:none;width:7px;height:7px;border-radius:50%;background:currentColor}.battle-utility-dock .online{color:#76d7ad}.battle-utility-dock .connecting{color:#e9d285}.battle-online-mask{position:fixed;inset:0;z-index:4000;display:grid;place-items:center;padding:20px;background:#000b}.battle-online-mask section{width:min(560px,100%);max-height:85vh;overflow:auto;padding:22px;border:1px solid #596973;background:#101921;font-size:max(14px,var(--l12-board-readable,14px))}.battle-online-mask header{display:flex;align-items:center;justify-content:space-between;gap:16px}.battle-online-mask h2{font-size:max(22px,var(--l12-board-readable,14px));margin:0}.battle-online-mask button{padding:8px;border:1px solid #53636d;background:#172733;color:#fff;font-size:max(14px,var(--l12-board-readable,14px))}.battle-online-mask ul{padding:0;list-style:none}.battle-online-mask li{display:flex;justify-content:space-between;gap:16px;padding:12px 0;border-bottom:1px solid #35414a;overflow-wrap:anywhere}.battle-online-mask li span{flex:none;color:#aab9c1}
</style>
