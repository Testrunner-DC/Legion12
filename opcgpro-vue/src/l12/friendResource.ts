import { reactive } from 'vue'
import { friendApi, platformState, type PlatformFriend } from './platform'

export const friendResource = reactive({
  accountId: '',
  friends: [] as PlatformFriend[],
  requests: [] as PlatformFriend[],
  blocked: [] as PlatformFriend[],
  loaded: false,
})

let generation = 0
let pending: Promise<void> | null = null
let dirty = false

export function resetFriendResource(accountId = platformState.account?.id ?? '') {
  generation += 1
  friendResource.accountId = accountId
  friendResource.friends = []
  friendResource.requests = []
  friendResource.blocked = []
  friendResource.loaded = false
  pending = null
  dirty = false
}

export function refreshFriendResource() {
  const accountId = platformState.account?.id ?? ''
  if (!accountId) {
    resetFriendResource('')
    return Promise.resolve()
  }
  if (friendResource.accountId !== accountId) resetFriendResource(accountId)
  if (pending) return pending
  const expected = generation
  dirty = false
  let request: Promise<void>
  request = friendApi.overview().then(overview => {
    if (expected !== generation || accountId !== platformState.account?.id) return
    friendResource.friends = overview.friends
    friendResource.requests = overview.requests
    friendResource.blocked = overview.blocked
    friendResource.loaded = true
  }).finally(() => {
    if (pending !== request) return
    pending = null
    if (dirty && expected === generation && accountId === platformState.account?.id && !document.hidden)
      void refreshFriendResource()
  })
  pending = request
  return request
}

export function invalidateFriendResource() {
  dirty = true
  if (!document.hidden) void refreshFriendResource().catch(() => undefined)
}
