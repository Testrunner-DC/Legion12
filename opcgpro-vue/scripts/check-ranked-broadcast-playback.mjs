import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const playback = read('src/l12/site/rankedBroadcastPlayback.ts')
const ticker = read('src/l12/site/RankedBroadcastTicker.vue')
const platform = read('src/l12/platform.ts')
const store = read('../服务端WebSocket/TwelveLegions/L12PlatformStore.Ranked.cs')
const broadcastObjects = read('../服务端WebSocket/TwelveLegions/L12PlatformStore.RankedBroadcastStorage.cs')
function subscriptionFreshnessContract(wrapper, objects) {
  const claimStart = objects.indexOf('private L12RankedBroadcastClaimView? ClaimRankedBroadcastObject(')
  const claimEnd = objects.indexOf('private bool CompleteRankedBroadcastObject(', claimStart)
  if (claimStart < 0 || claimEnd <= claimStart) return false
  const claim = objects.slice(claimStart, claimEnd)
  const sharedStart = objects.indexOf('private static RankedBroadcastRow? FindNextRankedBroadcast(')
  if (sharedStart < 0) return false
  const sharedEnd = objects.indexOf('\n    private ', sharedStart + 1)
  const shared = objects.slice(sharedStart, sharedEnd < 0 ? objects.length : sharedEnd)
  return wrapper.includes('ClaimRankedBroadcastAt(accountId, subscriptionStartedAt, DateTimeOffset.UtcNow)')
    && /ClaimRankedBroadcastObject\(accountId, subscriptionStartedAt, now,\s*RankedBroadcastRealtimeWindow\)/.test(wrapper)
    && /FindNextRankedBroadcast\(unchanged, accountId, subscriptionStartedAt,\s*now, realtimeWindow\)/.test(claim)
    && /FindNextRankedBroadcast\(latest, accountId, subscriptionStartedAt,\s*normalizedNow, realtimeWindow\)/.test(claim)
    && shared.includes('var freshFloor = normalizedNow - realtimeWindow')
    && shared.includes('if (requestedStart < freshFloor) requestedStart = freshFloor')
    && shared.includes('if (requestedStart > normalizedNow) requestedStart = normalizedNow')
    && !claim.includes('pending.ClaimToken = Guid.NewGuid()')
    && !shared.includes('pending.ClaimToken = Guid.NewGuid()')
}
const server = read('../服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')
const battleHub = read('src/l12/site/BattleHubPage.vue')
const gamePage = read('src/l12/GamePage.vue')

function requireContract(condition, message) {
  if (!condition) throw new Error(message)
}

requireContract(store.includes('DateTimeOffset? subscriptionStartedAt = null'),
  'server claim must keep an optional subscription time for old-client compatibility')
requireContract(subscriptionFreshnessContract(store, broadcastObjects),
  'server must bound client clock rollback with its realtime window')
for (const [label, wrapper, objects] of [
  ['non-authoritative public clock', store.replace('subscriptionStartedAt, DateTimeOffset.UtcNow)', 'subscriptionStartedAt, DateTimeOffset.MinValue)'), broadcastObjects],
  ['wrong window delegation', store.replace('RankedBroadcastRealtimeWindow);', 'TimeSpan.Zero);'), broadcastObjects],
  ['missing rollback clamp', store, broadcastObjects.replace('if (requestedStart < freshFloor) requestedStart = freshFloor', '')],
  ['missing future clamp', store, broadcastObjects.replace('if (requestedStart > normalizedNow) requestedStart = normalizedNow', '')],
  ['unfinished token refresh', store, broadcastObjects.replace('var freshFloor = normalizedNow - realtimeWindow', 'pending.ClaimToken = Guid.NewGuid(); var freshFloor = normalizedNow - realtimeWindow')],
  ['missing authoritative owner', store, broadcastObjects.replace('private L12RankedBroadcastClaimView? ClaimRankedBroadcastObject(', 'private L12RankedBroadcastClaimView? DetachedClaim(')],
  ['missing shared owner', store, broadcastObjects.replace('private static RankedBroadcastRow? FindNextRankedBroadcast(', 'private static RankedBroadcastRow? DetachedFreshness(')],
  ['fast path bypasses freshness', store, broadcastObjects.replace('FindNextRankedBroadcast(unchanged,', 'DetachedFreshness(unchanged,')],
  ['refreshed path bypasses freshness', store, broadcastObjects.replace('FindNextRankedBroadcast(latest,', 'DetachedFreshness(latest,')],
  ['fast path wrong window', store, broadcastObjects.replace('now, realtimeWindow) is null', 'now, TimeSpan.Zero) is null')],
]) requireContract(!subscriptionFreshnessContract(wrapper, objects), `subscription guard incorrectly accepted ${label}`)
requireContract(server.includes('ClaimRankedBroadcast(account.Id, subscriptionStartedAt)'),
  'HTTP claim endpoint must forward the optional subscription time')
requireContract(platform.includes('?subscriptionStartedAt=${encodeURIComponent(subscriptionStartedAt)}'),
  'current clients must send their foreground subscription start')

const hide = playback.indexOf('rankedBroadcastPlayback.claim = null')
const queue = playback.indexOf('enqueueCompletion(accountId, claim)', hide)
const background = playback.indexOf('void flushRankedBroadcastCompletions(accountId)', queue)
requireContract(hide >= 0 && queue > hide && background > queue,
  'animation completion must hide synchronously, persist the acknowledgement, then retry in background')
requireContract(playback.includes("COMPLETION_STORAGE_PREFIX = 'l12-ranked-broadcast-completion:'")
  && playback.includes('One key per broadcast avoids cross-tab read/modify/write queue loss.')
  && playback.includes("window.addEventListener('pagehide'")
  && playback.includes("window.addEventListener('storage'"),
  'completion retries must survive refresh and coordinate across tabs')
requireContract(playback.includes('animationDelay: `-${Math.min(elapsed, duration - 1)}ms`')
  && ticker.includes(':style="animationStyle"'),
  'route remounts must continue the current animation from elapsed time')
requireContract(ticker.includes('animation:ranked-message-once 16s linear 1 both')
  && !ticker.includes('animation-iteration-count:infinite'),
  'ranked broadcasts must remain one-shot animations')
requireContract(battleHub.includes('<RankedBroadcastTicker') && gamePage.includes('<RankedBroadcastTicker'),
  'BattleHub and GamePage must use the same module-level playback state')
requireContract(platform.includes('broadcastSettings: ()')
  && ticker.includes('rankedApi.broadcastSettings()')
  && ticker.includes("lobbyDelayInitialized = route.path !== '/lobby'")
  && ticker.includes('lobbyReadyAt = Date.now() + rankedBroadcastLobbyDelayMs()')
  && ticker.includes('schedule(remaining, generation, false)')
  && playback.includes('settings.displaySeconds')
  && playback.includes('settings.intervalSeconds'),
  'backend broadcast timing must drive future display duration, lobby delay and item interval')

await import('./test-ranked-broadcast-ticker-lifecycle.mjs')

console.log('Ranked broadcast playback contracts passed: subscription freshness, claim-once, persistent background completion, multi-tab and route continuity.')
