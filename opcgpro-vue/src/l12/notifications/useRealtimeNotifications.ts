import { computed, onBeforeUnmount, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { chatResource } from '@/l12/chatResource'
import { friendResource } from '@/l12/friendResource'
import { playL12FriendRequestSound, primeL12ActionAudio } from '@/l12/game/useL12ActionAudio'
import { l12State } from '@/l12/net'
import { platformState } from '@/l12/platform'
import {
  browserNotificationCanDisplayNow,
  refreshBrowserNotificationState,
  showBrowserNotification,
} from './browserNotifications'
import {
  ChatNotificationTracker,
  RealtimeEventLedger,
  RealtimeNotificationRateLimiter,
  notificationOwnerMatches,
  type ChatNotificationThreadSnapshot,
  type RealtimeNotificationKind,
} from './realtimeNotificationPolicy'

const ledger = new RealtimeEventLedger()
const chatTracker = new ChatNotificationTracker(ledger)
const rateLimiter = new RealtimeNotificationRateLimiter()
const chatThreadInstances = new WeakMap<object, string>()
let nextChatThreadInstance = 0

function chatThreadInstance(thread: object) {
  let id = chatThreadInstances.get(thread)
  if (!id) { id = `thread-${++nextChatThreadInstance}`; chatThreadInstances.set(thread, id) }
  return id
}

function incomingFriendRequestKeys(accountId: string) {
  if (!accountId || friendResource.accountId !== accountId || !friendResource.loaded) return []
  return friendResource.requests
    .filter(request => request.direction === 'incoming')
    .map(request => `${request.accountId}\u0000${request.createdAt}`)
}

function chatSnapshots(accountId: string): ChatNotificationThreadSnapshot[] {
  if (!accountId || chatResource.state.accountId !== accountId) return []
  return Object.entries(chatResource.state.threads).map(([key, thread]) => ({
    key,
    instanceId: chatThreadInstance(thread),
    scope: thread.scope,
    unread: thread.unread,
    readSeq: thread.readSeq,
    entries: thread.entries.map(entry => ({
      id: entry.id,
      seq: entry.seq,
      incoming: entry.senderId !== accountId,
    })),
  }))
}

function matchEventId() {
  const match = l12State.matchFound
  return match ? match.matchId || `room:${match.roomCode}` : ''
}

export function useRealtimeNotifications() {
  const router = useRouter()
  const connectedAccountId = computed(() => {
    const accountId = platformState.account?.id ?? ''
    return accountId && l12State.status === 'online' && l12State.accountId === accountId ? accountId : ''
  })
  let friendBaselineAccount = ''
  let notificationSessionEpoch = 0

  function signal(kind: RealtimeNotificationKind, target: string, accountId: string) {
    const now = Date.now()
    if (rateLimiter.allow('sound', kind, now)) playL12FriendRequestSound()
    if (browserNotificationCanDisplayNow() && rateLimiter.allow('desktop', kind, now)) {
      const owner = { accountId, sessionEpoch: notificationSessionEpoch, connectionGeneration: l12State.connectionGeneration }
      showBrowserNotification(kind, () => {
        const current = {
          accountId: connectedAccountId.value,
          sessionEpoch: notificationSessionEpoch,
          connectionGeneration: l12State.connectionGeneration,
        }
        if (notificationOwnerMatches(owner, current)) void router.push(target).catch(() => undefined)
      })
    }
  }

  const stops = [
    watch(connectedAccountId, accountId => {
      notificationSessionEpoch += 1
      friendBaselineAccount = ''
      if (!accountId) { chatTracker.endSession(); return }

      const matchId = matchEventId()
      if (matchId) ledger.baseline(accountId, 'match_found', [matchId])
      const invitationId = l12State.friendInvitation?.invitationId ?? ''
      if (invitationId) ledger.baseline(accountId, 'friend_challenge', [invitationId])

      const friendKeys = incomingFriendRequestKeys(accountId)
      if (friendResource.loaded && friendResource.accountId === accountId) {
        ledger.baseline(accountId, 'friend_request', friendKeys)
        friendBaselineAccount = accountId
      }
      chatTracker.beginSession(accountId, chatSnapshots(accountId))
    }, { immediate: true, flush: 'sync' }),

    watch(() => [connectedAccountId.value, matchEventId()] as const, ([accountId, eventId]) => {
      if (accountId && eventId && ledger.remember(accountId, 'match_found', eventId)) signal('match_found', '/battle', accountId)
    }, { immediate: true, flush: 'sync' }),

    watch(() => [connectedAccountId.value, l12State.friendInvitation?.invitationId ?? ''] as const,
      ([accountId, invitationId]) => {
        if (accountId && invitationId && ledger.remember(accountId, 'friend_challenge', invitationId)) {
          signal('friend_challenge', '/battle', accountId)
        }
      }, { immediate: true, flush: 'sync' }),

    watch(() => ({
      connectedAccountId: connectedAccountId.value,
      resourceAccountId: friendResource.accountId,
      loaded: friendResource.loaded,
      keys: friendResource.requests
        .filter(request => request.direction === 'incoming')
        .map(request => `${request.accountId}\u0000${request.createdAt}`),
    }), snapshot => {
      const accountId = snapshot.connectedAccountId
      if (!accountId || !snapshot.loaded || snapshot.resourceAccountId !== accountId) return
      if (friendBaselineAccount !== accountId) {
        ledger.baseline(accountId, 'friend_request', snapshot.keys)
        friendBaselineAccount = accountId
        return
      }
      if (ledger.rememberBatch(accountId, 'friend_request', snapshot.keys)) {
        signal('friend_request', '/battle/friends', accountId)
      }
    }, { immediate: true, flush: 'sync' }),

    watch(() => ({
      connectedAccountId: connectedAccountId.value,
      resourceAccountId: chatResource.state.accountId,
      enabled: chatResource.state.enabled,
      threads: chatSnapshots(chatResource.state.accountId),
    }), snapshot => {
      const accountId = snapshot.connectedAccountId
      if (!accountId || !snapshot.enabled || snapshot.resourceAccountId !== accountId) return
      const candidates = chatTracker.observe(accountId, snapshot.threads)
      if (!candidates.length) return
      signal('chat_message', candidates.some(candidate => candidate.scope === 'opponent') ? '/game' : '/battle/friends', accountId)
    }, { immediate: true, flush: 'sync' }),
  ]

  function primeNotificationAudio() {
    primeL12ActionAudio()
    window.removeEventListener('pointerdown', primeNotificationAudio)
    window.removeEventListener('keydown', primeNotificationAudio)
  }
  function refreshPermission() { refreshBrowserNotificationState() }

  onMounted(() => {
    refreshBrowserNotificationState()
    window.addEventListener('pointerdown', primeNotificationAudio, { once: true })
    window.addEventListener('keydown', primeNotificationAudio, { once: true })
    window.addEventListener('focus', refreshPermission)
    document.addEventListener('visibilitychange', refreshPermission)
  })
  onBeforeUnmount(() => {
    for (const stop of stops) stop()
    chatTracker.endSession()
    window.removeEventListener('pointerdown', primeNotificationAudio)
    window.removeEventListener('keydown', primeNotificationAudio)
    window.removeEventListener('focus', refreshPermission)
    document.removeEventListener('visibilitychange', refreshPermission)
  })
}
