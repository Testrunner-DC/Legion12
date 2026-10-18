<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { cancelFriendInvitation, l12State, resolveFriendInvitation } from '@/l12/net'
import { platformState } from '@/l12/platform'
import { landscapeTeleportTarget } from '@/l12/mobileViewport'
import { usesSiteUiStates } from './siteUiStateScope'

type InvitationOwner = { invitationId: string; accountId: string }

const route = useRoute()
const router = useRouter()
const invitationMinimized = ref(false)
const outgoingInvitationMinimized = ref(false)
const incomingOwner = ref<InvitationOwner | null>(null)
const outgoingOwner = ref<InvitationOwner | null>(null)
const acceptedIncoming = ref<InvitationOwner | null>(null)

const connectedAccountId = computed(() => {
  const accountId = platformState.account?.id ?? ''
  return accountId && l12State.status === 'online' && l12State.accountId === accountId ? accountId : ''
})
const incomingInvitation = computed(() => {
  const invitation = l12State.friendInvitation
  return invitation && incomingOwner.value?.invitationId === invitation.invitationId
    && incomingOwner.value.accountId === connectedAccountId.value ? invitation : null
})
const outgoingInvitation = computed(() => {
  const invitation = l12State.outgoingFriendInvitation
  return invitation && outgoingOwner.value?.invitationId === invitation.invitationId
    && outgoingOwner.value.accountId === connectedAccountId.value ? invitation : null
})

watch(() => l12State.friendInvitation, invitation => {
  if (!invitation) {
    incomingOwner.value = null
    invitationMinimized.value = false
    return
  }
  const accountId = connectedAccountId.value
  if (!accountId) return
  if (incomingOwner.value?.invitationId === invitation.invitationId && incomingOwner.value.accountId === accountId) return
  incomingOwner.value = { invitationId: invitation.invitationId, accountId }
  invitationMinimized.value = false
}, { immediate: true })
watch(() => l12State.outgoingFriendInvitation, invitation => {
  if (!invitation) {
    outgoingOwner.value = null
    outgoingInvitationMinimized.value = false
    return
  }
  const accountId = connectedAccountId.value
  if (!accountId) return
  if (outgoingOwner.value?.invitationId === invitation.invitationId && outgoingOwner.value.accountId === accountId) return
  outgoingOwner.value = { invitationId: invitation.invitationId, accountId }
  outgoingInvitationMinimized.value = false
}, { immediate: true })
watch(() => platformState.account?.id, () => {
  invitationMinimized.value = false
  outgoingInvitationMinimized.value = false
  incomingOwner.value = null
  outgoingOwner.value = null
  acceptedIncoming.value = null
}, { flush: 'sync' })

function answerInvitation(accept: boolean) {
  const invitation = incomingInvitation.value
  const accountId = connectedAccountId.value
  if (!invitation || !accountId) return
  acceptedIncoming.value = accept ? { invitationId: invitation.invitationId, accountId } : null
  l12State.friendInvitation = null
  resolveFriendInvitation(invitation.invitationId, accept)
}
function outgoingInvitationTarget(targetAccountId: string) {
  return l12State.presence.find(player => player.accountId === targetAccountId)?.username || targetAccountId
}
function cancelOutgoingInvitation() {
  const invitation = outgoingInvitation.value
  if (invitation) cancelFriendInvitation(invitation.invitationId)
}
function enterFriendRoom(event: Event) {
  const invitationId = String((event as CustomEvent).detail?.invitationId ?? '')
  const accountId = connectedAccountId.value
  if (!invitationId || !accountId) return
  const incomingMatch = acceptedIncoming.value?.invitationId === invitationId && acceptedIncoming.value.accountId === accountId
  const outgoingMatch = outgoingOwner.value?.invitationId === invitationId && outgoingOwner.value.accountId === accountId
  if (!incomingMatch && !outgoingMatch) return
  acceptedIncoming.value = null
  outgoingOwner.value = null
  void router.push('/battle')
}
onMounted(() => window.addEventListener('l12-friend-room-created', enterFriendRoom))
onBeforeUnmount(() => window.removeEventListener('l12-friend-room-created', enterFriendRoom))
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()">
    <div v-if="incomingInvitation || outgoingInvitation" class="invitation-stack" :class="{ 'ui-state-scope': usesSiteUiStates(route.meta) }">
      <div v-if="outgoingInvitation" class="outgoing-invitation-gate" :class="{ minimized: outgoingInvitationMinimized }">
        <button v-if="outgoingInvitationMinimized" class="invitation-minimized" @click="outgoingInvitationMinimized = false">已发送对战邀请 · 展开</button>
        <section v-else class="site-modal invitation-modal outgoing-invitation-modal" role="status" aria-label="已发送好友对战邀请">
          <header><div><small>FRIEND BATTLE</small><h2>对战邀请已发送</h2></div><button aria-label="最小化已发送对战邀请" @click="outgoingInvitationMinimized = true">—</button></header>
          <p>正在等待 <b>{{ outgoingInvitationTarget(outgoingInvitation.targetAccountId) }}</b> 回应。</p>
          <div class="invite-code"><span>预留房间码</span><strong>{{ outgoingInvitation.roomCode }}</strong></div>
          <div class="invite-actions outgoing-invite-actions"><button class="quiet" @click="cancelOutgoingInvitation">撤回邀请</button></div>
        </section>
      </div>

      <div v-if="incomingInvitation" class="invitation-gate" :class="{ minimized: invitationMinimized }">
        <button v-if="invitationMinimized" class="invitation-minimized" @click="invitationMinimized = false">好友对战邀请 · 展开</button>
        <section v-else class="site-modal invitation-modal" role="dialog" aria-modal="false" aria-label="好友对战邀请">
          <header><div><small>FRIEND BATTLE</small><h2>好友对战邀请</h2></div><button aria-label="最小化好友对战邀请" @click="invitationMinimized = true">—</button></header>
          <p><b>{{ incomingInvitation.fromName }}</b> 邀请你进行友谊战。</p>
          <div class="invite-code"><span>预留房间码</span><strong>{{ incomingInvitation.roomCode }}</strong></div>
          <p class="invite-note">接受后将直接创建房间：发起方成为房主，你将自动进入整备室，无需再输入房间码。</p>
          <div class="invite-actions"><button class="quiet" @click="answerInvitation(false)">拒绝</button><button @click="answerInvitation(true)">接受并进入房间</button></div>
        </section>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.invitation-stack{position:fixed;z-index:160;right:18px;bottom:18px;display:flex;width:min(430px,calc(100vw - 36px));max-height:calc(100vh - 36px);flex-direction:column;gap:10px;overflow:auto;pointer-events:none;color:#f2f0e9;font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.invitation-gate,.outgoing-invitation-gate{width:100%;flex:0 0 auto;pointer-events:none}.site-modal{overflow:auto;border:1px solid rgba(235,230,216,.28);background:#111923;box-shadow:0 28px 90px #000}.site-modal>header{display:flex;align-items:center;justify-content:space-between;padding-bottom:15px;border-bottom:1px solid rgba(235,230,216,.14)}.site-modal header small{color:#51c5cc;font-size:14px;font-weight:900;letter-spacing:.18em}.site-modal h2{margin:4px 0 0;font-size:24px}.site-modal header button{width:34px;height:34px;border:1px solid #48545c;background:#0a1016;color:#fff}.invitation-modal{box-sizing:border-box;width:100%;max-height:min(620px,calc(100vh - 36px));padding:20px;pointer-events:auto}.invitation-modal>p{color:#aeb6ba;line-height:1.7}.invitation-minimized{padding:10px 14px;border:1px solid #e1c16c;background:#231c0d;color:#f0d478;box-shadow:0 12px 34px #000;font-weight:900;pointer-events:auto}.invite-code{display:flex;align-items:center;justify-content:space-between;margin:18px 0;padding:14px;border:1px solid #4e5b63;background:#080e13}.invite-code span{color:#79868d;font-size:14px}.invite-code strong{color:#f0d478;font-size:22px;font-weight:900;letter-spacing:.18em}.invite-note{font-size:14px}.invite-actions{display:grid;grid-template-columns:1fr 1.7fr;gap:10px;margin-top:20px}.invite-actions button{padding:12px;border:1px solid #e1c16c;background:#e1c16c;color:#080b0d;font-weight:900}.invite-actions button.quiet{border-color:#4a565e;background:#0a1117;color:#929da2}.outgoing-invitation-modal{border-color:rgba(81,197,204,.42)}.outgoing-invite-actions{grid-template-columns:1fr}
</style>

