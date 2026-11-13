import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const repo = path.resolve(fileURLToPath(new URL('../..', import.meta.url)))
const read = relative => fs.readFileSync(path.join(repo, relative), 'utf8')
const json = relative => JSON.parse(read(relative))
let checks = 0
const check = (label, value) => {
  checks++
  assert.ok(value, label)
}

// B0: one bounded budget/telemetry baseline for site-wide and battle traffic.
const budgets = json('ops/performance-budgets.json')
check('B0 steady-state and hidden-tab budgets are zero', budgets.defaults?.steadyStateHttpRequestsPerMinute === 0
  && budgets.defaults?.hiddenTabRequestsPerMinute === 0)
check('B0 mutation and initial-load budgets are bounded', budgets.defaults?.maximumMutationRequestsPerAction === 1
  && budgets.defaults?.maximumParallelPageLoads === 3)
check('B0 runtime acceptance thresholds are present', budgets.runtimeAcceptance?.windowSeconds === 60
  && budgets.runtimeAcceptance?.slowRequestThresholdMilliseconds === 1000
  && budgets.runtimeAcceptance?.maximumServerErrorPercent === 1)
const perf = read('服务端WebSocket/TwelveLegions/L12PerformanceMetrics.cs')
check('B0 fixed performance stages and queue depth are exposed', perf.includes('FixedStageNames')
  && perf.includes('websocket.queue-depth') && perf.includes('persistence.total'))

// B1: read-heavy catalog/deck paths retain projection boundaries and avoid wire inflation.
const deckSummary = read('服务端WebSocket/TwelveLegions/L12PlatformStore.PublicDeckSummaryQueries.cs')
const deckQueries = read('服务端WebSocket/TwelveLegions/L12PlatformStore.DeckQueries.cs')
check('B1 public deck reads use a summary projection', deckSummary.includes('PublicDeckSummary')
  && deckSummary.includes('includeCover'))
check('B1 deck search is bounded behind the store gate', deckQueries.includes('lock (_gate)')
  && deckQueries.includes('Take('))

// B2: battle commands remain serialized by room ownership while persistence and transport are observable.
const room = read('服务端WebSocket/TwelveLegions/L12RoomManager.cs')
const outbound = read('服务端WebSocket/TwelveLegions/L12OutboundConnection.cs')
const metrics = read('服务端WebSocket/TwelveLegions/L12PerformanceMetrics.cs')
check('B2 battle commands use a per-room gate', room.includes('await room.Gate.WaitAsync()')
  && room.includes('room.Gate.Release()'))
check('B2 action and persistence stages are measured', metrics.includes('action.engine')
  && metrics.includes('persistence.transaction') && metrics.includes('persistence.total'))
check('B2 outbound queue is bounded and replaceable', outbound.includes('MaximumQueuedMessages = 32')
  && outbound.includes('ReplaceableGameState'))

// B3: HTTP pressure is admitted through a bounded guard and sampled by a fixed window.
const http = read('服务端WebSocket/TwelveLegions/L12HttpReliability.cs')
check('B3 HTTP traffic guard is present', http.includes('class L12HttpTrafficGuard')
  && http.includes('RateLimited'))
check('B3 HTTP performance sampling has slow/error thresholds', http.includes('SlowRequestThresholdMilliseconds')
  && http.includes('MaximumServerErrorPercent') && http.includes('Server-Timing'))

// B4: expensive administrative/season operations expose phase-level timings and failures.
const platformMetrics = read('服务端WebSocket/TwelveLegions/L12PlatformOperationMetrics.cs')
check('B4 platform operations expose bounded phase metrics', platformMetrics.includes('SnapshotSerializeStage')
  && platformMetrics.includes('HydrationStage') && platformMetrics.includes('CommittedIndexPrepareStage'))
check('B4 platform failures are separated from durations', metrics.includes('AdminTransactionFailureStage')
  && metrics.includes('SeasonFinalizationFailureStage'))

// B5: chat and outbound lanes have item/byte/account caps instead of unbounded fan-out.
const chat = read('服务端WebSocket/TwelveLegions/L12ChatRuntime.cs')
check('B5 chat queue has global and per-account caps', chat.includes('MaximumQueuedWorkItems = 256')
  && chat.includes('MaximumQueuedWorkItemsPerAccount = 8')
  && chat.includes('MaximumQueuedWorkBytesPerAccount'))
check('B5 terminal/critical outbound messages bypass replaceable merging', outbound.includes('EnqueueTerminalAndCompleteAsync')
  && outbound.includes('replaceableGameState: false'))

// B6: client delta reconstruction is clone-on-write rather than a full JSON deep clone.
const net = read('opcgpro-vue/src/l12/net.ts')
const deltaStart = net.indexOf('function rebuildDeltaGameState(')
const deltaEnd = net.indexOf('function clearReconnectTimer(')
const delta = net.slice(deltaStart, deltaEnd)
check('B6 delta reconstruction exists', deltaStart >= 0 && deltaEnd > deltaStart)
check('B6 delta reconstruction uses path cloning', net.includes('function cloneDeltaContainer')
  && net.includes('function cloneDeltaPath') && delta.includes('cloneDeltaPath(rebuilt, change.path)'))
check('B6 event windows copy only the append array', delta.includes('window.append.slice()'))
check('B6 full-baseline JSON clone is absent', !delta.includes('JSON.parse(JSON.stringify(baseline))'))

console.log(`B0-B6 latency specialist contract passed: ${checks}/${checks} checks`)
