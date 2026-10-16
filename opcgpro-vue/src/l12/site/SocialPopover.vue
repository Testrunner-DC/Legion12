<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { chatResource } from '../chatResource'
import { friendResource, refreshFriendResource } from '../friendResource'
import { friendApi } from '../platform'
import ChatPanel from './ChatPanel.vue'
const props = defineProps<{ anchor?: HTMLElement | null; positionAnchor?: HTMLElement | null; initialTab?: 'friends' | 'online' | 'opponent'; initialPeerId?: string; opponentId?: string; opponentName?: string; matchId?: string; opponentReadOnly?: boolean }>()
const emit = defineEmits<{ close: [] }>()
const tab = ref(props.initialTab ?? 'friends')
const selectedId = ref(props.initialPeerId ?? '')
const panel = ref<HTMLElement>()
const position = ref<Record<string, string>>({ left: '12px', bottom: '12px' })
const notice = ref('')
const selected = computed(() => friendResource.accountId === chatResource.state.accountId
  ? friendResource.friends.find(friend => friend.accountId === selectedId.value)
  : undefined)
const unread = (id: string) => chatResource.state.threads[JSON.stringify(['friend', id, ''])]?.unread ?? 0
function close(restore = false) { emit('close'); if (restore && props.anchor?.isConnected) props.anchor.focus() }
function keydown(event: KeyboardEvent) { if (event.key === 'Escape') { event.stopPropagation(); close(true) } }
function outside(event: PointerEvent) { if (!panel.value?.contains(event.target as Node) && !props.anchor?.contains(event.target as Node)) close() }
function place() {
  const width = Math.min(370, Math.max(180, innerWidth - 24)), height = Math.min(570, Math.max(160, innerHeight - 24))
  const anchor = (props.positionAnchor ?? props.anchor)?.getBoundingClientRect()
  const left = Math.max(12, Math.min((props.positionAnchor ? anchor?.left : anchor?.right) ?? 12, innerWidth - width - 12))
  const bottom = anchor && anchor.top > 180 ? Math.min(innerHeight - 172, innerHeight - anchor.top + 8) : 12
  position.value = { left: `${left}px`, bottom: `${Math.max(12, bottom)}px`, width: `${width}px`, maxHeight: `${Math.min(height, innerHeight - Math.max(12, bottom) - 12)}px` }
}
async function blockSelected() {
  if (!selected.value || !confirm(`屏蔽「${selected.value.username}」并解除好友关系？`)) return
  try { await friendApi.block(selected.value.accountId); selectedId.value = ''; await refreshFriendResource() }
  catch { notice.value = '屏蔽失败，请稍后重试。' }
}
onMounted(async () => {
  place(); document.addEventListener('pointerdown', outside); document.addEventListener('keydown', keydown)
  window.addEventListener('resize', place); window.addEventListener('scroll', place, true)
  try { await refreshFriendResource() } catch { notice.value = '好友列表暂不可用。' }
  if (!props.initialPeerId || !friendResource.friends.some(friend => friend.accountId === props.initialPeerId)) selectedId.value = ''
  await nextTick(); place()
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', outside); document.removeEventListener('keydown', keydown)
  window.removeEventListener('resize', place); window.removeEventListener('scroll', place, true)
})
</script>
<template>
  <Teleport to="body">
    <section ref="panel" class="social-popover ui-state-scope" data-l12-social-popover :style="position" role="dialog" aria-modal="false" aria-label="好友与聊天">
      <header><b>好友与聊天</b><button type="button" aria-label="关闭好友与聊天" @click="close(true)">×</button></header>
      <nav aria-label="社交功能"><button type="button" :aria-pressed="tab === 'friends'" @click="tab = 'friends'">好友</button><button v-if="$slots.online" type="button" :aria-pressed="tab === 'online'" @click="tab = 'online'">在线</button><button v-if="opponentId && matchId" type="button" :aria-pressed="tab === 'opponent'" @click="tab = 'opponent'">本局对手</button></nav>
      <p v-if="notice" role="status">{{ notice }}</p>
      <div v-if="tab === 'online'" class="social-content"><slot name="online"/></div>
      <div v-else-if="tab === 'opponent' && opponentId && matchId" class="social-content"><ChatPanel :key="matchId + opponentId" :peer-account-id="opponentId" :peer-name="opponentName || '本局对手'" scope="opponent" :match-id="matchId" :read-only="opponentReadOnly"/></div>
      <div v-else class="social-content">
        <template v-if="selected"><div class="social-person"><button type="button" @click="selectedId = ''">返回好友</button><button type="button" @click="blockSelected">屏蔽</button></div><ChatPanel :key="selected.accountId" :peer-account-id="selected.accountId" :peer-name="selected.username"/></template>
        <template v-else><button v-for="friend in friendResource.friends" :key="friend.accountId" class="social-friend" type="button" @click="selectedId = friend.accountId"><span>{{ friend.username }}</span><b v-if="unread(friend.accountId)">{{ unread(friend.accountId) }}</b></button><p v-if="!friendResource.friends.length">暂无好友，可在好友页面添加。</p><router-link to="/friends" @click="close()">好友管理</router-link></template>
      </div>
    </section>
  </Teleport>
</template>
<style scoped>
.social-popover{position:fixed;z-index:4750;box-sizing:border-box;display:flex;min-width:0;flex-direction:column;overflow:hidden;padding:12px;border:1px solid #586d74;background:#101b23;color:#ebeee9;box-shadow:0 12px 36px #0009;font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif;font-size:14px}.social-popover>header{display:flex;align-items:center;justify-content:space-between;gap:10px}.social-popover button{min-height:36px;padding:6px 10px;border:1px solid #4a636a;background:#10252d;color:#edf0e7;font:700 13px 'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.social-popover button:focus-visible,.social-popover a:focus-visible{outline:2px solid #e2c368;outline-offset:2px}.social-popover>header>button{width:36px;font-size:20px}.social-popover>nav{display:flex;gap:6px;margin:10px 0}.social-popover>nav>button{flex:1}.social-popover button[aria-pressed=true]{border-color:#d7b661;background:#332c16}.social-content{min-width:0;min-height:0;overflow:auto;overscroll-behavior:contain}.social-friend{display:flex;width:100%;align-items:center;justify-content:space-between;gap:8px;margin:6px 0;text-align:left;overflow-wrap:anywhere}.social-friend b{color:#f1d180}.social-popover p{color:#a9b7b9;font-size:13px;line-height:1.6}.social-popover a{display:block;margin-top:12px;color:#b9d8d8}.social-person{display:flex;justify-content:space-between;gap:8px;margin-bottom:10px}.social-content :deep(.chat-panel){min-height:220px}.social-content :deep(.chat-messages){max-height:240px}@media(max-height:450px){.social-popover{padding:8px}.social-popover>nav{margin:6px 0}.social-content :deep(.chat-panel){min-height:170px}.social-content :deep(.chat-messages){max-height:110px}}
</style>
