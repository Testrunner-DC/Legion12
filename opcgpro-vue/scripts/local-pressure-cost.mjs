import assert from 'node:assert/strict'

const finite = value => typeof value === 'number' && Number.isFinite(value) && value >= 0
const counterFields = ['allocatedBytes', 'cpuMs', 'gen0', 'gen1', 'gen2']

// Offline numeric analysis only. Never infer a production SLO, net allocation
// attribution, or a leak from local process counters or inclusive metric scopes.
export function summarizeLocalPressureCost({samples, pressure, hostLog}) {
  assert.ok(Array.isArray(samples) && samples.length >= 2, 'at least two process samples required')
  assert.ok(Array.isArray(pressure?.stages) && pressure.stages.length > 0)
  assert.equal(typeof hostLog, 'string')
  for (const [index, sample] of samples.entries()) {
    for (const field of ['utcMs', 'workingSet', 'heapBytes', 'threadPoolPending', ...counterFields])
      assert.ok(finite(sample[field]), 'invalid numeric process sample: ' + field)
    if (!index) continue
    assert.ok(sample.utcMs > samples[index - 1].utcMs, 'sample clock must strictly increase')
    for (const field of counterFields)
      assert.ok(sample[field] >= samples[index - 1][field], 'process counter reset: ' + field)
  }
  const window = (start, end) => {
    assert.ok(finite(start) && finite(end) && start < end, 'invalid measurement window')
    const contained = samples.filter(row => row.utcMs >= start && row.utcMs <= end)
    if (contained.length < 2) return {available: false, sampleCount: contained.length,
      requestedStartUtcMs: start, requestedEndUtcMs: end, reason: 'fewer-than-two-contained-samples'}
    const first = contained[0], last = contained.at(-1)
    const elapsedMs = last.utcMs - first.utcMs
    const allocatedBytes = last.allocatedBytes - first.allocatedBytes
    return {available: true, sampleCount: contained.length, requestedStartUtcMs: start,
      requestedEndUtcMs: end, observedStartUtcMs: first.utcMs, observedEndUtcMs: last.utcMs,
      observedMs: elapsedMs, excludedLeadingMs: first.utcMs - start, excludedTrailingMs: end - last.utcMs,
      maxSampleGapMs: Math.max(...contained.slice(1).map((row, index) => row.utcMs - contained[index].utcMs)),
      allocatedBytes, allocatedBytesPerObservedSecond: allocatedBytes * 1000 / elapsedMs,
      cpuMs: last.cpuMs - first.cpuMs, heapStartBytes: first.heapBytes, heapEndBytes: last.heapBytes,
      heapPeakBytes: Math.max(...contained.map(row => row.heapBytes)),
      workingSetPeakBytes: Math.max(...contained.map(row => row.workingSet)),
      maxThreadPoolPending: Math.max(...contained.map(row => row.threadPoolPending)),
      collections: Object.fromEntries(['gen0', 'gen1', 'gen2'].map(key => [key, last[key] - first[key]]))}
  }
  const metrics = new Map()
  for (const line of hostLog.split(/\r?\n/)) {
    if (!line.startsWith('[L12性能] 阶段=')) continue
    const match = line.match(/^\[L12性能\] 阶段=(\S+) 次数=(\d+) 平均=([\d.]+) 最大=([\d.]+) 单位=(\S+).*累计争用丢样=(\d+) 窗口开始=(\S+) 窗口结束=(\S+)$/)
    assert.ok(match, 'unsupported metric row; do not silently omit evidence')
    const [, label, countText, meanText, maxText, unit, lostText, startText, endText] = match
    const count = Number(countText), mean = Number(meanText), maximum = Number(maxText), lost = Number(lostText)
    const start = Date.parse(startText), end = Date.parse(endText)
    assert.ok([count, mean, maximum, lost, start, end].every(finite) && count > 0 && mean <= maximum && start < end)
    const row = metrics.get(label) ?? {label, unit, count: 0, approximateSum: 0,
      roundingErrorBound: 0, maximum: 0, latestCumulativeContentionLoss: 0, windows: 0}
    assert.equal(row.unit, unit, 'metric unit changed')
    if (row.lastWindowEndUtcMs !== undefined)
      assert.ok(start >= row.lastWindowEndUtcMs, 'metric windows overlap or repeat')
    assert.ok(lost >= row.latestCumulativeContentionLoss, 'metric loss counter reset')
    row.count += count
    row.approximateSum += count * mean
    // Existing logger prints the mean to two decimals; keep its uncertainty.
    row.roundingErrorBound += count * 0.005
    row.maximum = Math.max(row.maximum, maximum)
    row.latestCumulativeContentionLoss = lost
    row.firstWindowStartUtcMs ??= start
    row.lastWindowEndUtcMs = end
    row.windows++
    metrics.set(label, row)
  }
  const wire = pressure.websocket?.messageTypes ?? {}
  const wireByType = Object.entries(wire).map(([type, value]) => {
    assert.ok(Number.isSafeInteger(value.messages) && value.messages >= 0
      && Number.isSafeInteger(value.totalPayloadBytes) && value.totalPayloadBytes >= 0)
    return {type, messages: value.messages, payloadBytes: value.totalPayloadBytes}
  })
  const intervals = samples.slice(1).map((last, index) => {
    const first = samples[index]
    return {startUtcMs: first.utcMs, endUtcMs: last.utcMs, elapsedMs: last.utcMs - first.utcMs,
      allocatedBytes: last.allocatedBytes - first.allocatedBytes, cpuMs: last.cpuMs - first.cpuMs}
  })
  return {schema: 1, scope: 'offline-local-process-cost-only', onlineCapacityAccepted: false,
    productFixBenefitProven: false, leakProven: false,
    overall: window(samples[0].utcMs, samples.at(-1).utcMs),
    stages: pressure.stages.map(stage => ({name: stage.name, rooms: stage.rooms,
      process: window(stage.startedUtcMs, stage.endedUtcMs)})),
    highestAllocationIntervals: intervals.toSorted((a, b) => b.allocatedBytes - a.allocatedBytes).slice(0, 8),
    metrics: [...metrics.values()], wireByType,
    wireByteBasis: pressure.websocket?.byteBasis ?? 'not-recorded',
    caveats: ['Stage counters cover contained sample pairs only; boundary intervals are not extrapolated.',
      'Metric sums use rounded means, may omit contended samples, and inclusive operations/phases overlap.',
      'Metrics cover the complete host log, including possible startup/history/shutdown; process counters cover their separate recorded windows, so their totals cannot yield attribution percentages.',
      'JSON/wire byte counts are not allocation counters; metric scopes must not be added or subtracted as net allocation.',
      'Local Windows measurements do not establish Linux/shared-host capacity or production traffic distribution.']}
}
