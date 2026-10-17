import { computed, onBeforeUnmount, ref, watch, type Ref } from 'vue'
import { authState, localDateBoundary, platformRequest, platformState, type AdminDisasterAnalyticsReport } from './platform'

export interface VisibleDisasterSetup {
  coverage: string; status: string; mode?: string | null; firstPlayer?: number | null; visibility: 'self' | 'public'
  decisions: Array<{ step: number; action: string; actorIndex: number; chosenCardId?: string | null
    origin?: string | null; ownCandidateCardIds: string[] }>
  visibleCompositionCardIds: string[]
}
export interface PublicDisasterStatistics {
  status: string; usableMatches: number; recentDays: number; minimumMatches: number
  items: Array<{ cardId: string; opportunities: number; bans: number; banRate?: number | null }>
}
export type DisasterAnalyticsScope = Record<string, string | number | undefined>
export function scopedDisasterPath(scope: DisasterAnalyticsScope, cardId?: string, signature?: string) {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(scope)) {
    if (value === undefined || value === '') continue
    if (key === 'from' || key === 'to') query.set(key === 'from' ? 'fromUtc' : 'toUtc', localDateBoundary(String(value), key === 'to')!)
    else query.set(key === 'minimumSample' ? 'minimumSampleSize' : key, String(value))
  }
  if (cardId) query.set('includedCardId', cardId)
  if (signature) query.set('deckSignature', signature)
  return `/api/admin/analytics/disasters?${query}`
}

// One explicit request at a time. Clearing/changing context, identity, permissions or token
// retires its result before rendering; a delayed reply never repopulates a different context.
export function useDisasterRead<T>(path: () => string) {
  const value = ref<T | null>(null) as Ref<T | null>
  const pending = ref(false)
  const error = ref('')
  let controller: AbortController | undefined
  let revision = 0
  let disposed = false
  const identity = computed(() => `${platformState.account?.id || ''}:${platformState.account?.permissionVersion || ''}:${platformState.token}:${authState.verified}`)
  const reset = () => { ++revision; controller?.abort(); controller = undefined; value.value = null; pending.value = false; error.value = '' }
  watch([path, identity], reset, { flush: 'sync' })
  async function load() {
    reset()
    const selected = path()
    if (!selected || disposed) return
    const version = revision
    const actor = identity.value
    controller = new AbortController()
    pending.value = true
    try {
      const result = await platformRequest<T>(selected, { signal: controller.signal })
      if (!disposed && version === revision && actor === identity.value && selected === path()) value.value = result
    } catch (reason) {
      if (!disposed && version === revision && actor === identity.value)
        error.value = reason instanceof Error ? reason.message : '天灾记录暂不可用，请稍后重试'
    } finally { if (!disposed && version === revision) pending.value = false }
  }
  onBeforeUnmount(() => { disposed = true; reset() })
  return { value, pending, error, load, reset }
}
export type ScopedDisasterReport = AdminDisasterAnalyticsReport
