import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..')

export function evaluatePerformanceSnapshot(payload, acceptance, options = {}) {
  const performance = payload?.httpPerformance ?? payload
  const issues = []
  if (!performance || typeof performance !== 'object') return ['missing httpPerformance snapshot']
  if (performance.windowSeconds !== acceptance.windowSeconds)
    issues.push(`unexpected observation window: ${performance.windowSeconds}`)
  if (performance.minimumSamples !== acceptance.minimumSamples)
    issues.push(`unexpected minimum sample contract: ${performance.minimumSamples}`)
  if (performance.minimumReadSamples !== acceptance.minimumReadSamples)
    issues.push(`unexpected minimum read sample contract: ${performance.minimumReadSamples}`)
  if (performance.minimumMutationSamples !== acceptance.minimumMutationSamples)
    issues.push(`unexpected minimum mutation sample contract: ${performance.minimumMutationSamples}`)
  if (performance.slowRequestThresholdMilliseconds !== acceptance.slowRequestThresholdMilliseconds)
    issues.push(`unexpected slow-request threshold: ${performance.slowRequestThresholdMilliseconds}`)
  if (!Number.isFinite(performance.sampleCount) || performance.sampleCount < acceptance.minimumSamples)
    issues.push(`insufficient samples: ${performance.sampleCount ?? 0}/${acceptance.minimumSamples}`)
  if (!Number.isFinite(performance.readSampleCount) || performance.readSampleCount < acceptance.minimumReadSamples)
    issues.push(`insufficient read samples: ${performance.readSampleCount ?? 0}/${acceptance.minimumReadSamples}`)
  if (!Number.isFinite(performance.mutationSampleCount) || performance.mutationSampleCount < acceptance.minimumMutationSamples)
    issues.push(`insufficient mutation samples: ${performance.mutationSampleCount ?? 0}/${acceptance.minimumMutationSamples}`)
  if (performance.readSampleCount + performance.mutationSampleCount !== performance.sampleCount)
    issues.push('sample composition does not add up')
  if (options.expectedCommit && payload?.serviceVersion !== options.expectedCommit)
    issues.push(`snapshot commit ${payload?.serviceVersion ?? 'missing'} does not match ${options.expectedCommit}`)
  const observedAt = Date.parse(payload?.observedAt ?? '')
  const now = options.now instanceof Date ? options.now.getTime() : Date.now()
  if (!Number.isFinite(observedAt)) issues.push('missing or invalid snapshot observedAt')
  else if (Math.abs(now - observedAt) > acceptance.maximumSnapshotAgeSeconds * 1000)
    issues.push(`snapshot is older than ${acceptance.maximumSnapshotAgeSeconds} seconds`)
  if (performance.sampleSufficient !== true) issues.push('server marked the sample insufficient')
  if (performance.withinBudget !== true) issues.push('server marked the runtime outside the low-lag budget')
  const checks = [
    ['slow request', performance.slowRequestPercent, acceptance.maximumSlowRequestPercent],
    ['server error', performance.serverErrorPercent, acceptance.maximumServerErrorPercent],
    ['rate limited', performance.rateLimitedPercent, acceptance.maximumRateLimitedPercent],
  ]
  for (const [label, actual, maximum] of checks) {
    if (!Number.isFinite(actual) || actual > maximum)
      issues.push(`${label} percentage ${actual ?? 'missing'} exceeds ${maximum}`)
  }
  if (Array.isArray(performance.budgetFailures) && performance.budgetFailures.length)
    issues.push(`server budget failures: ${performance.budgetFailures.join(',')}`)
  return issues
}

async function readStdin() {
  let text = ''
  for await (const chunk of process.stdin) text += chunk
  return text
}

export async function runPerformanceSnapshotVerifier() {
  const acceptance = JSON.parse(fs.readFileSync(path.join(repositoryRoot, 'ops', 'performance-budgets.json'), 'utf8'))
    .runtimeAcceptance
  try {
    const expectedIndex = process.argv.indexOf('--expected-commit')
    const receiptIndex = process.argv.indexOf('--receipt')
    const expectedCommit = expectedIndex >= 0 ? process.argv[expectedIndex + 1] : ''
    const receiptPath = receiptIndex >= 0 ? process.argv[receiptIndex + 1] : ''
    if (!/^[0-9a-f]{40}$/.test(expectedCommit)) throw new Error('--expected-commit requires a full 40-character commit')
    const payload = JSON.parse(await readStdin())
    const issues = evaluatePerformanceSnapshot(payload, acceptance, { expectedCommit })
    if (issues.length) {
      for (const issue of issues) console.error(`PERF SNAPSHOT FAIL: ${issue}`)
      return 1
    }
    if (receiptPath) {
      const resolvedReceipt = path.resolve(receiptPath)
      fs.mkdirSync(path.dirname(resolvedReceipt), { recursive: true })
      fs.writeFileSync(resolvedReceipt, JSON.stringify({ schema: 1, expectedCommit,
        verifiedAt: new Date().toISOString(), observedAt: payload.observedAt,
        httpPerformance: payload.httpPerformance }, null, 2) + '\n', { flag: 'wx' })
    }
    console.log(`Performance snapshot passed: ${payload.httpPerformance?.sampleCount ?? payload.sampleCount} samples`)
    return 0
  } catch (error) {
    console.error(`PERF SNAPSHOT FAIL: ${error instanceof Error ? error.message : String(error)}`)
    return 1
  }
}

if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url))
  process.exitCode = await runPerformanceSnapshotVerifier()
