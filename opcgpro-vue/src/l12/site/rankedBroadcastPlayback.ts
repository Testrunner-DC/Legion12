import { reactive } from 'vue'
import {
  authState,
  platformState,
  rankedApi,
  type RankedBroadcastClaim,
} from '@/l12/platform'

export const rankedBroadcastPlayback = reactive<{
  accountId: string
  claim: RankedBroadcastClaim | null
  loading: boolean
}>({ accountId: '', claim: null, loading: false })

let pending: Promise<RankedBroadcastClaim | null> | null = null

export async function claimNextRankedBroadcast() {
  const accountId = authState.verified ? platformState.account?.id ?? '' : ''
  if (!accountId) return null
  if (rankedBroadcastPlayback.accountId !== accountId) {
    rankedBroadcastPlayback.accountId = accountId
    rankedBroadcastPlayback.claim = null
  }
  if (rankedBroadcastPlayback.claim) return rankedBroadcastPlayback.claim
  if (pending) return pending
  rankedBroadcastPlayback.loading = true
  pending = rankedApi.claimBroadcast()
    .then(claim => {
      if (rankedBroadcastPlayback.accountId === accountId) rankedBroadcastPlayback.claim = claim
      return claim
    })
    .catch(() => null)
    .finally(() => {
      rankedBroadcastPlayback.loading = false
      pending = null
    })
  return pending
}

export async function completeCurrentRankedBroadcast() {
  const claim = rankedBroadcastPlayback.claim
  const accountId = rankedBroadcastPlayback.accountId
  if (!claim || !accountId || platformState.account?.id !== accountId) return false
  try {
    await rankedApi.completeBroadcast(claim.broadcast.id, claim.claimToken)
    if (rankedBroadcastPlayback.claim?.broadcast.id === claim.broadcast.id)
      rankedBroadcastPlayback.claim = null
    return true
  } catch {
    // The lease may have expired or an administrator may have removed the message.
    // Release the local claim so another tab cannot be held on a stale broadcast.
    if (rankedBroadcastPlayback.claim?.broadcast.id === claim.broadcast.id)
      rankedBroadcastPlayback.claim = null
    return false
  }
}
