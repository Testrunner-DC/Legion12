<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { friendApi, platformState, type PlatformFriend } from '../platform'
import { playL12FriendRequestSound, primeL12ActionAudio } from '../game/useL12ActionAudio'

const requests = ref<PlatformFriend[]>([])
const current = computed(() => requests.value[0])
const busy = ref(false)
const notice = ref('')
const sounded = new Set<string>()
let generation = 0
let timer = 0
let reading = false
async function refresh() {
  const accountId = platformState.account?.id
  if (!accountId || reading || busy.value) return
  const expected = generation
  reading = true
  try {
    const next = (await friendApi.requests()).filter(item => item.direction === 'incoming')
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt))
    if (expected !== generation || accountId !== platformState.account?.id) return
    requests.value = next
    let newRequest = false
    for (const item of next) {
      const key = `${accountId}:${item.accountId}:${item.createdAt}`
      if (!sounded.has(key)) { sounded.add(key); newRequest = true }
    }
    if (newRequest) playL12FriendRequestSound()
  } catch { /* Retry preserves the pending dialog without inventing a rejection. */ }
  finally { reading = false }
}
async function resolve(action: 'accept' | 'reject' | 'block') {
  const request = current.value
  if (!request || busy.value) return
  const accountId = platformState.account?.id
  const expected = ++generation
  busy.value = true
  notice.value = ''
  try {
    if (action === 'block') await friendApi.block(request.accountId)
    else await friendApi.resolve(request.accountId, action === 'accept')
    if (expected !== generation || accountId !== platformState.account?.id) return
    requests.value = requests.value.filter(item => item.accountId !== request.accountId)
    window.dispatchEvent(new Event('l12-friends-changed'))
  } catch (error) { if (expected === generation) notice.value = error instanceof Error ? error.message : '处理失败，请重试' }
  finally { busy.value = false; void refresh() }
}
watch(() => platformState.account?.id, () => { generation++; requests.value = []; notice.value = ''; void refresh() })
function focus() { void refresh() }
onMounted(() => {
  void refresh()
  timer = window.setInterval(() => void refresh(), 4000)
  window.addEventListener('focus', focus)
  window.addEventListener('l12-friends-changed', focus)
  window.addEventListener('pointerdown', primeL12ActionAudio, { once: true })
})
onBeforeUnmount(() => {
  generation++; window.clearInterval(timer)
  window.removeEventListener('focus', focus)
  window.removeEventListener('l12-friends-changed', focus)
  window.removeEventListener('pointerdown', primeL12ActionAudio)
})
</script>
<template>
  <Teleport to="body"><section v-if="current" class="friend-request-dialog" role="dialog" aria-modal="false" aria-labelledby="friend-request-title" aria-live="polite">
    <h2 id="friend-request-title">好友申请 <small v-if="requests.length > 1">{{ requests.length }}条待处理</small></h2>
    <p><b>{{ current.username }}</b> 希望添加你为好友。</p>
    <p v-if="notice" role="alert">{{ notice }}</p>
    <div><button :disabled="busy" @click="resolve('block')">屏蔽</button><button :disabled="busy" @click="resolve('reject')">拒绝</button><button class="accept" :disabled="busy" @click="resolve('accept')">通过</button></div>
  </section></Teleport>
</template>
<style scoped>
.friend-request-dialog{position:fixed;z-index:4600;right:24px;bottom:24px;width:min(400px,calc(100vw - 48px));padding:20px;border:1px solid #d2b45f;background:#101a22;box-shadow:0 12px 48px #000a;color:#eef1ed;font-size:14px;overflow-wrap:anywhere}.friend-request-dialog h2{display:flex;justify-content:space-between;gap:12px;margin:0;font-size:18px}.friend-request-dialog small{font-size:14px;color:#b3c0c5}.friend-request-dialog p{line-height:1.65}.friend-request-dialog>div{display:flex;gap:8px}.friend-request-dialog button{flex:1;min-height:42px;border:1px solid #586872;background:#18232c;color:#fff;font-size:14px}.friend-request-dialog .accept{background:#08694e;border-color:#40ac89}.friend-request-dialog button:disabled{opacity:.5}
</style>
