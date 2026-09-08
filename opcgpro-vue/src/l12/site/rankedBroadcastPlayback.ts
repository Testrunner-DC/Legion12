import { reactive, watch } from 'vue'
import {
  authState,
  PlatformRequestError,
  platformState,
  rankedApi,
  DEFAULT_RANKED_BROADCAST_CONFIG,
  type RankedBroadcastConfig,
  type RankedBroadcastClaim,
} from '@/l12/platform'

const STANDARD_PLAYBACK_MS = 16_000
const REDUCED_PLAYBACK_MS = 4_000
const COMPLETION_RETRY_MS = 15_000
const COMPLETION_STORAGE_PREFIX = 'l12-ranked-broadcast-completion:'

type PendingCompletion = {
  broadcastId: string
  claimToken: string
  queuedAt: string
}

export const rankedBroadcastPlayback = reactive<{
  accountId: string
  subscriptionStartedAt: string
  claim: RankedBroadcastClaim | null
  playbackStartedAt: number
  playbackDurationMs: number
  loading: boolean
  settings: RankedBroadcastConfig
}>({
  accountId: '',
  subscriptionStartedAt: '',
  claim: null,
  playbackStartedAt: 0,
  playbackDurationMs: STANDARD_PLAYBACK_MS,
  loading: false,
  settings: { ...DEFAULT_RANKED_BROADCAST_CONFIG },
})

export function configureRankedBroadcastPlayback(settings: RankedBroadcastConfig) {
  rankedBroadcastPlayback.settings = { ...DEFAULT_RANKED_BROADCAST_CONFIG, ...settings }
}

export function rankedBroadcastIntervalMs() {
  return Math.max(3, rankedBroadcastPlayback.settings.intervalSeconds) * 1_000
}

export function rankedBroadcastLobbyDelayMs() {
  return Math.max(0, rankedBroadcastPlayback.settings.lobbyDelaySeconds) * 1_000
}

let pending: Promise<RankedBroadcastClaim | null> | null = null
let subscriptionGeneration = 0
let playbackTimer: ReturnType<typeof window.setTimeout> | undefined
const completionFlushes = new Map<string, Promise<void>>()
const completionRetryTimers = new Map<string, ReturnType<typeof window.setTimeout>>()
const volatileCompletions = new Map<string, Map<string, PendingCompletion>>()

function completionStoragePrefix(accountId: string) {
  return `${COMPLETION_STORAGE_PREFIX}${encodeURIComponent(accountId)}:`
}

function completionStorageKey(accountId: string, broadcastId: string) {
  return `${completionStoragePrefix(accountId)}${encodeURIComponent(broadcastId)}`
}

function validCompletion(value: unknown): value is PendingCompletion {
  if (!value || typeof value !== 'object') return false
  const row = value as Partial<PendingCompletion>
  return typeof row.broadcastId === 'string' && row.broadcastId.length > 0
    && row.broadcastId.length <= 120 && typeof row.claimToken === 'string'
    && row.claimToken.length > 0 && row.claimToken.length <= 160
    && typeof row.queuedAt === 'string'
}

function readPendingCompletions(accountId: string) {
  let stored: PendingCompletion[] = []
  try {
    const prefix = completionStoragePrefix(accountId)
    for (let index = 0; index < localStorage.length; index += 1) {
      const key = localStorage.key(index)
      if (!key?.startsWith(prefix)) continue
      const parsed = JSON.parse(localStorage.getItem(key) || 'null') as unknown
      if (validCompletion(parsed)) stored.push(parsed)
    }
  } catch { /* A damaged local queue must not restore a visible broadcast. */ }
  const merged = [...stored, ...(volatileCompletions.get(accountId)?.values() ?? [])]
  return merged.filter((row, index) => merged.findIndex(candidate =>
    candidate.broadcastId === row.broadcastId && candidate.claimToken === row.claimToken) === index)
}

function enqueueCompletion(accountId: string, claim: RankedBroadcastClaim) {
  const completion: PendingCompletion = {
    broadcastId: claim.broadcast.id,
    claimToken: claim.claimToken,
    queuedAt: new Date().toISOString(),
  }
  const memory = volatileCompletions.get(accountId) ?? new Map<string, PendingCompletion>()
  memory.set(completion.broadcastId, completion)
  volatileCompletions.set(accountId, memory)
  try {
    // One key per broadcast avoids cross-tab read/modify/write queue loss.
    localStorage.setItem(completionStorageKey(accountId, completion.broadcastId), JSON.stringify(completion))
    memory.delete(completion.broadcastId)
    if (!memory.size) volatileCompletions.delete(accountId)
  } catch { /* Keep the in-memory retry queue when storage is unavailable. */ }
}

function removeCompletion(accountId: string, completion: PendingCompletion) {
  const memory = volatileCompletions.get(accountId)
  if (memory?.get(completion.broadcastId)?.claimToken === completion.claimToken)
    memory.delete(completion.broadcastId)
  if (memory && !memory.size) volatileCompletions.delete(accountId)
  try {
    const key = completionStorageKey(accountId, completion.broadcastId)
    const stored = JSON.parse(localStorage.getItem(key) || 'null') as unknown
    // Never delete a newer token written by another tab between read and acknowledgement.
    if (validCompletion(stored) && stored.claimToken === completion.claimToken)
      localStorage.removeItem(key)
  } catch { /* A future storage event/login retry can clean up inaccessible storage. */ }
}

function scheduleCompletionRetry(accountId: string) {
  if (completionRetryTimers.has(accountId)) return
  const timer = window.setTimeout(() => {
    completionRetryTimers.delete(accountId)
    void flushRankedBroadcastCompletions(accountId)
  }, COMPLETION_RETRY_MS)
  completionRetryTimers.set(accountId, timer)
}

export function flushRankedBroadcastCompletions(accountId = rankedBroadcastPlayback.accountId) {
  if (!accountId || !authState.verified || platformState.account?.id !== accountId)
    return Promise.resolve()
  const running = completionFlushes.get(accountId)
  if (running) return running
  const task = (async () => {
    for (const completion of readPendingCompletions(accountId)) {
      if (!authState.verified || platformState.account?.id !== accountId) break
      try {
        await rankedApi.completeBroadcast(completion.broadcastId, completion.claimToken)
        removeCompletion(accountId, completion)
      } catch (error) {
        // Deleted broadcasts and invalid legacy tokens are already consumed for display;
        // discard those terminal acknowledgements rather than retrying forever.
        if (error instanceof PlatformRequestError && error.status === 409) {
          removeCompletion(accountId, completion)
          continue
        }
        scheduleCompletionRetry(accountId)
        break
      }
    }
  })().finally(() => completionFlushes.delete(accountId))
  completionFlushes.set(accountId, task)
  return task
}

function finishVisibleClaim(queueCompletion: boolean) {
  const claim = rankedBroadcastPlayback.claim
  const accountId = rankedBroadcastPlayback.accountId
  // Visibility ends synchronously. Persistence is deliberately a background concern.
  rankedBroadcastPlayback.claim = null
  rankedBroadcastPlayback.playbackStartedAt = 0
  if (playbackTimer) window.clearTimeout(playbackTimer)
  playbackTimer = undefined
  if (!claim || !accountId || !queueCompletion) return false
  enqueueCompletion(accountId, claim)
  void flushRankedBroadcastCompletions(accountId)
  return true
}

function resetSubscription() {
  finishVisibleClaim(true)
  subscriptionGeneration += 1
  rankedBroadcastPlayback.accountId = ''
  rankedBroadcastPlayback.subscriptionStartedAt = ''
  rankedBroadcastPlayback.loading = false
}

function ensureSubscription(accountId: string) {
  if (rankedBroadcastPlayback.accountId === accountId
      && rankedBroadcastPlayback.subscriptionStartedAt) return
  resetSubscription()
  rankedBroadcastPlayback.accountId = accountId
  rankedBroadcastPlayback.subscriptionStartedAt = new Date().toISOString()
}

function beginPlayback(claim: RankedBroadcastClaim) {
  rankedBroadcastPlayback.claim = claim
  rankedBroadcastPlayback.playbackStartedAt = Date.now()
  rankedBroadcastPlayback.playbackDurationMs = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    ? Math.min(REDUCED_PLAYBACK_MS, rankedBroadcastPlayback.settings.displaySeconds * 1_000)
    : Math.max(5_000, rankedBroadcastPlayback.settings.displaySeconds * 1_000)
  if (playbackTimer) window.clearTimeout(playbackTimer)
  playbackTimer = window.setTimeout(() => { finishVisibleClaim(true) },
    rankedBroadcastPlayback.playbackDurationMs + 250)
}

export function rankedBroadcastAnimationStyle() {
  const duration = Math.max(1, rankedBroadcastPlayback.playbackDurationMs)
  const elapsed = Math.max(0, Date.now() - rankedBroadcastPlayback.playbackStartedAt)
  return {
    animationDuration: `${duration}ms`,
    animationDelay: `-${Math.min(elapsed, duration - 1)}ms`,
  }
}

export async function claimNextRankedBroadcast(): Promise<RankedBroadcastClaim | null> {
  const accountId = authState.verified ? platformState.account?.id ?? '' : ''
  if (!accountId) {
    if (rankedBroadcastPlayback.accountId) resetSubscription()
    return null
  }
  ensureSubscription(accountId)
  void flushRankedBroadcastCompletions(accountId)
  if (rankedBroadcastPlayback.claim) return rankedBroadcastPlayback.claim
  if (pending) {
    await pending
    return claimNextRankedBroadcast()
  }
  const generation = subscriptionGeneration
  const subscriptionStartedAt = rankedBroadcastPlayback.subscriptionStartedAt
  rankedBroadcastPlayback.loading = true
  const request = rankedApi.claimBroadcast(subscriptionStartedAt)
    .then(claim => {
      if (!claim) return null
      if (subscriptionGeneration === generation && rankedBroadcastPlayback.accountId === accountId)
        beginPlayback(claim)
      else {
        // A stale in-flight response has already consumed its server display right.
        // Persist its acknowledgement without surfacing it in a newer login session.
        enqueueCompletion(accountId, claim)
      }
      return claim
    })
    .catch(() => null)
    .finally(() => {
      if (pending === request) pending = null
      if (subscriptionGeneration === generation) rankedBroadcastPlayback.loading = false
    })
  pending = request
  return request
}

export function completeCurrentRankedBroadcast() {
  return finishVisibleClaim(true)
}

if (typeof window !== 'undefined') {
  watch(() => authState.verified ? platformState.account?.id ?? '' : '', accountId => {
    if (!accountId || (rankedBroadcastPlayback.accountId
        && rankedBroadcastPlayback.accountId !== accountId)) resetSubscription()
  }, { flush: 'sync' })
  window.addEventListener('pagehide', () => { finishVisibleClaim(true) })
  window.addEventListener('storage', event => {
    const accountId = rankedBroadcastPlayback.accountId
    if (accountId && event.key?.startsWith(completionStoragePrefix(accountId)))
      void flushRankedBroadcastCompletions(accountId)
  })
}
