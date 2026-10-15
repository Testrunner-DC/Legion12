<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { chatResource, type ChatScope } from '../chatResource'
const props = defineProps<{ peerAccountId: string; peerName: string; scope?: ChatScope; matchId?: string; readOnly?: boolean }>()
const composing = ref(false)
const visible = ref(!document.hidden)
const conversation = computed(() => ({ peerAccountId: props.peerAccountId, scope: props.scope ?? 'friend', matchId: props.matchId }))
const thread = computed(() => props.readOnly ? chatResource.peek(conversation.value) : chatResource.thread(conversation.value))
const available = computed(() => chatResource.state.enabled && !props.readOnly && !thread.value?.retired && chatResource.canUse(conversation.value))
function refresh() { if (visible.value && !props.readOnly) chatResource.sync(conversation.value) }
function send() { if (!composing.value && available.value) chatResource.send(conversation.value) }
function keydown(event: KeyboardEvent) {
  if (event.key !== 'Enter' || event.shiftKey || event.isComposing || composing.value || event.keyCode === 229) return
  event.preventDefault(); send()
}
function visibility() { visible.value = !document.hidden; if (visible.value) markRead() }
function markRead() { if (visible.value && !props.readOnly) chatResource.read(conversation.value) }
watch(() => [props.peerAccountId, props.scope, props.matchId, chatResource.state.connection, chatResource.state.privacyRevision], refresh, { immediate: true })
watch(() => [thread.value?.entries.at(-1)?.seq, thread.value?.readSeq], markRead)
onMounted(() => document.addEventListener('visibilitychange', visibility))
onBeforeUnmount(() => document.removeEventListener('visibilitychange', visibility))
</script>

<template>
  <section class="chat-panel" aria-label="聊天">
    <header><b>{{ peerName }}</b><span>{{ (scope ?? 'friend') === 'opponent' ? '本局对话' : '好友消息' }}</span><button type="button" :disabled="readOnly || !chatResource.state.enabled || !chatResource.canUse(conversation) || thread?.retired || thread?.loading" @click="refresh">刷新</button></header>
    <p v-if="!chatResource.state.enabled" class="chat-notice" role="status">聊天尚未启用或连接未就绪。</p>
    <p v-else-if="!chatResource.canUse(conversation)" class="chat-notice" role="status">当前会话不可用；请先确认好友或屏蔽状态。</p>
    <p v-if="readOnly" class="chat-notice">本局对话已结束，仅可查看已接收消息。</p>
    <div v-if="thread" class="chat-messages" role="log" aria-label="消息记录" :aria-busy="thread.loading">
      <button v-if="thread.hasMore" type="button" :disabled="readOnly || thread.loading" @click="chatResource.sync(conversation, true)">较早消息</button>
      <p v-if="thread.loaded && !thread.entries.length" class="chat-empty">还没有消息</p>
      <article v-for="entry in thread.entries" :key="entry.id" :class="{ own: entry.senderId === chatResource.state.accountId }">
        <small>{{ entry.senderName }} · {{ new Date(entry.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) }}</small><p>{{ entry.text }}</p>
      </article>
    </div>
    <p v-if="thread?.notice" class="chat-notice" role="status">{{ thread.notice }}</p>
    <form v-if="thread" @submit.prevent="send">
      <label class="chat-input-label">发送消息<textarea v-model="thread.draft" :disabled="!available" rows="2" maxlength="512" placeholder="输入消息（最多100字）" @compositionstart="composing = true" @compositionend="composing = false" @keydown="keydown" /></label>
      <footer><span v-if="thread.pending">{{ thread.pending.status === 'sending' ? '正在确认…' : '发送结果未确认' }}</span><button v-if="thread.pending?.status === 'uncertain'" type="button" :disabled="!available" @click="chatResource.send(conversation, true)">重试原消息</button><button type="submit" :disabled="!available || !!thread.pending || !thread.draft.trim() || composing">发送</button></footer>
    </form>
  </section>
</template>

<style scoped>
.chat-panel{display:flex;box-sizing:border-box;width:100%;min-width:0;min-height:260px;flex:1;flex-direction:column;gap:10px;color:#ecede7;font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif;text-align:left}.chat-panel header{display:flex;align-items:center;gap:10px;flex-wrap:wrap}.chat-panel header b{overflow-wrap:anywhere}.chat-panel header span{flex:1;color:#9babaf;font-size:12px}.chat-panel button{min-height:36px;padding:7px 11px;border:1px solid #52746f;background:#102b29;color:#e9f0e9;font-family:inherit;font-size:13px;font-weight:700}.chat-panel button:disabled{opacity:.45;cursor:not-allowed}.chat-panel button:focus-visible,.chat-panel textarea:focus-visible{outline:2px solid #e7c668;outline-offset:2px}.chat-messages{flex:1;min-height:100px;max-height:360px;overflow:auto;overscroll-behavior:contain;padding:8px;border:1px solid #35464c;background:#081116}.chat-messages article{margin:8px 0;max-width:90%;width:fit-content;padding:8px 10px;border:1px solid #344c56;background:#14222c;overflow-wrap:anywhere}.chat-messages article.own{margin-left:auto;border-color:#35675d;background:#102d29}.chat-messages small{font-size:11px;color:#9aafb4}.chat-messages article p{margin:4px 0 0;white-space:pre-wrap;font-size:14px;line-height:1.6}.chat-input-label{display:block;font-size:12px;color:#aebbbb}.chat-panel textarea{box-sizing:border-box;display:block;width:100%;min-height:60px;max-height:140px;padding:8px;border:1px solid #485e67;background:#081116;color:#fff;resize:vertical;font:400 14px/1.5 'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.chat-panel footer{display:flex;align-items:center;justify-content:flex-end;flex-wrap:wrap;gap:8px;margin-top:7px}.chat-panel footer span{flex:1;color:#c8ae75;font-size:12px}.chat-notice,.chat-empty{margin:0;color:#b8b8a3;font-size:12px;line-height:1.5}@media(max-height:450px){.chat-panel{min-height:180px}.chat-messages{max-height:160px}.chat-panel textarea{min-height:44px}}
</style>
