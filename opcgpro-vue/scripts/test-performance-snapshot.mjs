import assert from 'node:assert/strict'
import { evaluatePerformanceSnapshot } from './verify-performance-snapshot.mjs'

const acceptance = {
  windowSeconds: 60,
  minimumSamples: 20,
  minimumReadSamples: 10,
  minimumMutationSamples: 2,
  maximumSnapshotAgeSeconds: 75,
  slowRequestThresholdMilliseconds: 1000,
  maximumSlowRequestPercent: 5,
  maximumServerErrorPercent: 1,
  maximumRateLimitedPercent: 20,
}
const healthy = {
  observedAt: '2026-09-24T00:00:00.000Z',
  serviceVersion: '0123456789abcdef0123456789abcdef01234567',
  httpPerformance: {
    windowSeconds: 60, minimumSamples: 20, minimumReadSamples: 10, minimumMutationSamples: 2,
    slowRequestThresholdMilliseconds: 1000,
    sampleCount: 40, readSampleCount: 30, mutationSampleCount: 10,
    sampleSufficient: true, withinBudget: true,
    slowRequestPercent: 2.5, serverErrorPercent: 0, rateLimitedPercent: 5, budgetFailures: [],
  },
}
const options = { expectedCommit: healthy.serviceVersion, now: new Date(healthy.observedAt) }
assert.deepEqual(evaluatePerformanceSnapshot(healthy, acceptance, options), [])
assert(evaluatePerformanceSnapshot({ ...healthy, httpPerformance: { ...healthy.httpPerformance,
  sampleCount: 19, readSampleCount: 17, mutationSampleCount: 2,
  sampleSufficient: false, withinBudget: null } }, acceptance, options)
  .some(issue => issue.includes('insufficient samples')))
assert(evaluatePerformanceSnapshot({ ...healthy, httpPerformance: { ...healthy.httpPerformance,
  slowRequestPercent: 7.5, withinBudget: false, budgetFailures: ['slow_request_rate'] } }, acceptance, options)
  .some(issue => issue.includes('slow request')))
assert(evaluatePerformanceSnapshot({ ...healthy, httpPerformance: { ...healthy.httpPerformance,
  slowRequestThresholdMilliseconds: 5000 } }, acceptance, options).some(issue => issue.includes('threshold')))
assert(evaluatePerformanceSnapshot({ ...healthy, serviceVersion: 'f'.repeat(40) }, acceptance, options)
  .some(issue => issue.includes('does not match')))
assert(evaluatePerformanceSnapshot({ ...healthy, observedAt: '2026-09-23T23:00:00Z' }, acceptance, options)
  .some(issue => issue.includes('older')))
assert(evaluatePerformanceSnapshot({}, acceptance, options).some(issue => issue.includes('missing')))

console.log('Performance snapshot verifier passed: healthy, insufficient, degraded, drifted and missing snapshots covered')
