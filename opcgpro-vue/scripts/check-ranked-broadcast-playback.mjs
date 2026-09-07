import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const playback = read('src/l12/site/rankedBroadcastPlayback.ts')
const ticker = read('src/l12/site/RankedBroadcastTicker.vue')
const platform = read('src/l12/platform.ts')
const store = read('../服务端WebSocket/TwelveLegions/L12PlatformStore.Ranked.cs')
const server = read('../服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')
const battleHub = read('src/l12/site/BattleHubPage.vue')
const gamePage = read('src/l12/GamePage.vue')

function requireContract(condition, message) {
  if (!condition) throw new Error(message)
}

requireContract(store.includes('DateTimeOffset? subscriptionStartedAt = null'),
  'server claim must keep an optional subscription time for old-client compatibility')
requireContract(store.includes('now - RankedBroadcastRealtimeWindow')
  && store.includes('if (requestedStart < freshFloor) requestedStart = freshFloor'),
  'server must bound client clock rollback with its realtime window')
requireContract(!store.includes('pending.ClaimToken = Guid.NewGuid()'),
  'an unfinished delivery must never receive a refreshed token or be shown again')
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

console.log('Ranked broadcast playback contracts passed: subscription freshness, claim-once, persistent background completion, multi-tab and route continuity.')
