import assert from 'node:assert/strict'
import {summarizeLocalPressureCost} from './local-pressure-cost.mjs'

const sample = (utcMs, allocatedBytes) => ({utcMs, allocatedBytes, cpuMs: utcMs / 10,
  workingSet: 300, heapBytes: 200 - utcMs / 100, threadPoolPending: 0, gen0: 0, gen1: 0, gen2: 0})
const samples = [sample(1000, 100), sample(2000, 300), sample(3000, 700)]
const pressure = {stages: [{name: 'inside', rooms: 30, startedUtcMs: 500, endedUtcMs: 3500}],
  websocket: {byteBasis: 'utf8-json-payload-excluding-websocket-framing', messageTypes: {
    gameStateDelta: {messages: 2, totalPayloadBytes: 40}}}}
const metric = (start, end, mean = '2.25', lost = 0) =>
  `[L12性能] 阶段=bytes.platform.operation.transactional-save 次数=4 平均=${mean} 最大=5.00 单位=legacy-bytes P95桶=(1,4] P99桶=(1,4] 溢出样本=0 累计争用丢样=${lost} 窗口开始=${start} 窗口结束=${end}`
const first = metric('2026-10-21T00:00:00Z', '2026-10-21T00:01:00Z')
const second = metric('2026-10-21T00:01:00Z', '2026-10-21T00:02:00Z', '3.00', 1)
const summarize = (overrides = {}) => summarizeLocalPressureCost({samples, pressure, hostLog: first + '\n' + second, ...overrides})
let cases = 0
const value = summarize()
assert.equal(value.overall.allocatedBytes, 600); cases++
assert.equal(value.stages[0].process.allocatedBytesPerObservedSecond, 300)
assert.equal(value.stages[0].process.excludedLeadingMs, 500)
assert.equal(value.stages[0].process.excludedTrailingMs, 500); cases++
assert.equal(value.highestAllocationIntervals[0].allocatedBytes, 400); cases++
assert.equal(value.metrics[0].approximateSum, 21)
assert.equal(value.metrics[0].roundingErrorBound, 0.04)
assert.equal(value.metrics[0].latestCumulativeContentionLoss, 1); cases++
assert.deepEqual(value.wireByType, [{type: 'gameStateDelta', messages: 2, payloadBytes: 40}]); cases++
for (const key of ['onlineCapacityAccepted', 'productFixBenefitProven', 'leakProven']) assert.equal(value[key], false)
assert.ok(value.overall.heapEndBytes < value.overall.heapStartBytes); cases++
assert.equal(summarize({pressure: {...pressure, stages: [{name: 'sparse', rooms: 1, startedUtcMs: 1100, endedUtcMs: 2100}]}}).stages[0].process.available, false); cases++
for (const invalid of [[], [samples[0]], [samples[1], samples[0]], [samples[0], samples[0]],
  [samples[0], {...samples[1], allocatedBytes: 99}], [samples[0], {...samples[1], cpuMs: NaN}],
  [samples[0], {...samples[1], gen0: -1}]]) {
  assert.throws(() => summarize({samples: invalid})); cases++
}
for (const invalid of [first + '\n' + first, first + '\n' + first.replace('00:00:00Z', '00:00:30Z'),
  first.replace('平均=2.25', '平均=99.00'), first.replace('次数=4', '次数=0'),
  '[L12性能] 阶段=unsupported', second + '\n' + first]) {
  assert.throws(() => summarize({hostLog: invalid})); cases++
}
assert.throws(() => summarize({pressure: {...pressure, stages: [{startedUtcMs: 1, endedUtcMs: 1}]}})); cases++
assert.throws(() => summarize({pressure: {...pressure, websocket: {messageTypes: {gameState: {messages: 1, totalPayloadBytes: -1}}}}})); cases++
console.log(JSON.stringify({status: 'passed', cases, networkCalls: 0, generatedFiles: 0}))
