export type RealtimeNotificationKind = 'match_found' | 'friend_request' | 'friend_challenge' | 'chat_message'
export type NotificationChannel = 'sound' | 'desktop'

const MAX_ACCOUNTS = 8
const MAX_EVENTS_PER_KIND = 512

type AccountEvents = Map<RealtimeNotificationKind, Map<string, true>>

function validKey(value: string) {
  return value.length > 0 && value.length <= 512
}

/** Account-bound, bounded replay protection shared by every realtime notification source. */
export class RealtimeEventLedger {
  private readonly accounts = new Map<string, AccountEvents>()

  private account(accountId: string) {
    let events = this.accounts.get(accountId)
    if (!events) {
      events = new Map()
      this.accounts.set(accountId, events)
      while (this.accounts.size > MAX_ACCOUNTS) this.accounts.delete(this.accounts.keys().next().value!)
    } else {
      this.accounts.delete(accountId)
      this.accounts.set(accountId, events)
    }
    return events
  }

  remember(accountId: string, kind: RealtimeNotificationKind, eventId: string) {
    if (!validKey(accountId) || !validKey(eventId)) return false
    const account = this.account(accountId)
    let events = account.get(kind)
    if (!events) { events = new Map(); account.set(kind, events) }
    if (events.has(eventId)) return false
    events.set(eventId, true)
    while (events.size > MAX_EVENTS_PER_KIND) events.delete(events.keys().next().value!)
    return true
  }

  rememberBatch(accountId: string, kind: RealtimeNotificationKind, eventIds: readonly string[]) {
    let hasNewEvent = false
    for (const eventId of eventIds) hasNewEvent = this.remember(accountId, kind, eventId) || hasNewEvent
    return hasNewEvent
  }

  baseline(accountId: string, kind: RealtimeNotificationKind, eventIds: readonly string[]) {
    this.rememberBatch(accountId, kind, eventIds)
  }
}

export interface ChatNotificationEntrySnapshot {
  id: string
  seq: number
  incoming: boolean
}

export interface ChatNotificationThreadSnapshot {
  key: string
  instanceId: string
  scope: 'friend' | 'opponent'
  unread: number
  readSeq: number
  entries: ChatNotificationEntrySnapshot[]
}

export interface ChatNotificationCandidate {
  id: string
  threadKey: string
  scope: 'friend' | 'opponent'
}

export interface RealtimeNotificationOwner {
  accountId: string
  sessionEpoch: number
  connectionGeneration: number
}

export function notificationOwnerMatches(expected: RealtimeNotificationOwner, current: RealtimeNotificationOwner) {
  return expected.accountId.length > 0 && expected.accountId === current.accountId
    && expected.sessionEpoch === current.sessionEpoch
    && expected.connectionGeneration === current.connectionGeneration
}

interface ObservedChatThread {
  instanceId: string
  knownEntries: Map<string, true>
  lastUnread: number
  pendingLiveEntries: number
}

/**
 * Reconciles chat's synchronous `unread += 1` then `entries = ...` mutations.
 * History and the first snapshot are baselines; only an unread increase paired
 * with a newly observed incoming entry can produce a notification.
 */
export class ChatNotificationTracker {
  private accountId = ''
  private readonly threads = new Map<string, ObservedChatThread>()

  constructor(private readonly ledger: RealtimeEventLedger) {}

  beginSession(accountId: string, snapshots: readonly ChatNotificationThreadSnapshot[]) {
    this.accountId = accountId
    this.threads.clear()
    for (const snapshot of snapshots) this.baselineThread(accountId, snapshot)
  }

  endSession() {
    this.accountId = ''
    this.threads.clear()
  }

  observe(accountId: string, snapshots: readonly ChatNotificationThreadSnapshot[]) {
    if (!accountId || accountId !== this.accountId) return [] as ChatNotificationCandidate[]
    const active = new Set(snapshots.map(snapshot => snapshot.key))
    for (const key of this.threads.keys()) if (!active.has(key)) this.threads.delete(key)

    const candidates: ChatNotificationCandidate[] = []
    for (const snapshot of snapshots) {
      let observed = this.threads.get(snapshot.key)
      if (!observed || observed.instanceId !== snapshot.instanceId) {
        this.baselineThread(accountId, snapshot)
        continue
      }

      const pendingBeforeObservation = observed.pendingLiveEntries
      const unreadDelta = Math.max(-MAX_EVENTS_PER_KIND, Math.min(MAX_EVENTS_PER_KIND, snapshot.unread - observed.lastUnread))
      if (unreadDelta > 0) observed.pendingLiveEntries = Math.min(MAX_EVENTS_PER_KIND, observed.pendingLiveEntries + unreadDelta)
      else if (unreadDelta < 0) observed.pendingLiveEntries = Math.max(0, observed.pendingLiveEntries + unreadDelta)

      const unseen = snapshot.entries
        .filter(entry => validKey(entry.id) && !observed!.knownEntries.has(entry.id))
        .sort((left, right) => right.seq - left.seq)
      for (const entry of unseen) {
        observed.knownEntries.set(entry.id, true)
        while (observed.knownEntries.size > MAX_EVENTS_PER_KIND) {
          observed.knownEntries.delete(observed.knownEntries.keys().next().value!)
        }
      }

      for (const entry of unseen) {
        const eligible = entry.incoming && entry.seq > snapshot.readSeq && observed.pendingLiveEntries > 0
        if (eligible) {
          observed.pendingLiveEntries -= 1
          if (this.ledger.remember(accountId, 'chat_message', `${snapshot.key}\u0000${entry.id}`)) {
            candidates.push({ id: entry.id, threadKey: snapshot.key, scope: snapshot.scope })
          }
        } else {
          this.ledger.remember(accountId, 'chat_message', `${snapshot.key}\u0000${entry.id}`)
        }
      }
      // A live merge replaces `entries` immediately after increasing `unread`.
      // If that next observation has no new window entry, the increase came from
      // an item already outside the bounded chat window and must not leak into a
      // later message/history observation.
      if (pendingBeforeObservation > 0 && unreadDelta <= 0 && unseen.length === 0) {
        observed.pendingLiveEntries = 0
      }
      observed.lastUnread = snapshot.unread
    }
    return candidates
  }

  private baselineThread(accountId: string, snapshot: ChatNotificationThreadSnapshot) {
    const recentEntries = snapshot.entries
      .filter(entry => validKey(entry.id))
      .sort((left, right) => left.seq - right.seq)
      .slice(-MAX_EVENTS_PER_KIND)
    const knownEntries = new Map(recentEntries.map(entry => [entry.id, true] as const))
    this.ledger.baseline(accountId, 'chat_message', [...knownEntries.keys()].map(id => `${snapshot.key}\u0000${id}`))
    this.threads.set(snapshot.key, {
      instanceId: snapshot.instanceId,
      knownEntries,
      lastUnread: snapshot.unread,
      pendingLiveEntries: 0,
    })
  }
}

const CHANNEL_LIMITS: Record<NotificationChannel, { global: number; kind: number }> = {
  sound: { global: 350, kind: 800 },
  desktop: { global: 1_000, kind: 3_500 },
}

/** Collapses bursts without timers, polling, or replaying suppressed events later. */
export class RealtimeNotificationRateLimiter {
  private readonly global = new Map<NotificationChannel, number>()
  private readonly kinds = new Map<string, number>()

  allow(channel: NotificationChannel, kind: RealtimeNotificationKind, now = Date.now()) {
    if (!Number.isFinite(now)) return false
    const lastGlobal = this.global.get(channel)
    const kindKey = `${channel}:${kind}`
    const lastKind = this.kinds.get(kindKey)
    const limits = CHANNEL_LIMITS[channel]
    if ((lastGlobal !== undefined && now >= lastGlobal && now - lastGlobal < limits.global)
      || (lastKind !== undefined && now >= lastKind && now - lastKind < limits.kind)) return false
    this.global.set(channel, now)
    this.kinds.set(kindKey, now)
    return true
  }
}

export interface BrowserNotificationPolicyInput {
  supported: boolean
  secure: boolean
  enabled: boolean
  permission: NotificationPermission
  hidden: boolean
  focused: boolean
}

export function shouldDisplayBrowserNotification(input: BrowserNotificationPolicyInput) {
  return input.supported && input.secure && input.enabled && input.permission === 'granted'
    && (input.hidden || !input.focused)
}
