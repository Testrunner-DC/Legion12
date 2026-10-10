import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repositoryRoot = path.resolve(frontendRoot, '..')
const read = relative => fs.readFileSync(path.join(repositoryRoot, relative), 'utf8')
const budgets = JSON.parse(read('ops/performance-budgets.json'))
const reliability = read('服务端WebSocket/TwelveLegions/L12HttpReliability.cs')
const server = read('服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')
const runtimeModel = read('服务端WebSocket/TwelveLegions/L12RuntimeStatus.cs')
const platform = read('opcgpro-vue/src/l12/platform.ts')
const operationsPanel = read('opcgpro-vue/src/l12/site/AdminOperationsPanel.vue')
const packageDocument = JSON.parse(read('opcgpro-vue/package.json'))

let checks = 0
const acceptance = budgets.runtimeAcceptance
assert.equal(acceptance.windowSeconds, 60); checks += 1
assert.equal(acceptance.minimumSamples, 20); checks += 1
assert.equal(acceptance.minimumReadSamples, 10); checks += 1
assert.equal(acceptance.minimumMutationSamples, 2); checks += 1
assert.equal(acceptance.maximumSnapshotAgeSeconds, 75); checks += 1
assert.equal(acceptance.slowRequestThresholdMilliseconds, 1000); checks += 1
assert.equal(acceptance.maximumSlowRequestPercent, 5); checks += 1
assert.equal(acceptance.maximumServerErrorPercent, 1); checks += 1
assert.equal(acceptance.maximumRateLimitedPercent, 20); checks += 1

const monitorStart = reliability.indexOf('internal sealed class L12HttpPerformanceMonitor')
const monitorEnd = reliability.indexOf('internal static class L12HttpExceptionBoundary')
assert(monitorStart >= 0 && monitorEnd > monitorStart); checks += 1
const monitor = reliability.slice(monitorStart, monitorEnd)
assert(monitor.includes('Bucket?[] _buckets') && monitor.includes('BucketCount = WindowSeconds / BucketSeconds')); checks += 1
assert(monitor.includes('Interlocked.CompareExchange') && monitor.includes('lock (bucket.Gate)')); checks += 1
assert(!monitor.includes('Dictionary<') && !monitor.includes('Console.') && !monitor.includes('File.')); checks += 1
for (const contract of [
  'WindowSeconds = 60', 'MinimumSamples = 20', 'MinimumReadSamples = 10', 'MinimumMutationSamples = 2',
  'SlowRequestThresholdMilliseconds = 1_000',
  'MaximumSlowRequestPercent = 5', 'MaximumServerErrorPercent = 1', 'MaximumRateLimitedPercent = 20',
]) assert(monitor.includes(contract), `server runtime budget drifted: ${contract}`)
checks += 1
assert(monitor.includes('Server-Timing') && monitor.includes('StartsWithSegments("/api")')); checks += 1
assert(monitor.includes('slow_request_rate') && monitor.includes('server_error_rate')
  && monitor.includes('rate_limited_rate')); checks += 1
assert(monitor.includes('DiagnosticRequests') && monitor.includes('ExpectedUnavailable')
  && monitor.includes('ClientCancelled')); checks += 1

const monitorMiddleware = server.indexOf('_httpPerformance.InvokeAsync')
const exceptionMiddleware = server.indexOf('L12HttpExceptionBoundary.InvokeAsync')
assert(monitorMiddleware >= 0 && exceptionMiddleware > monitorMiddleware); checks += 1
assert(server.includes('RateLimit-Remaining, Server-Timing')); checks += 1
assert(server.includes('_httpPerformance.Snapshot()')); checks += 1
assert(server.includes('build.ServerRelease') && server.includes('ExpectedUnavailableItemName')); checks += 1
assert(runtimeModel.includes('record L12HttpPerformanceView') && runtimeModel.includes('bool? WithinBudget')); checks += 1
assert(platform.includes('interface HttpPerformanceStatus') && platform.includes('httpPerformance: HttpPerformanceStatus')); checks += 1
assert(operationsPanel.includes('HTTP 低卡顿预算') && operationsPanel.includes('httpPerformance.p95LatencyBand')
  && operationsPanel.includes('httpPerformance.serverErrorCount')); checks += 1
assert(packageDocument.scripts['check:performance-architecture'].includes('check-performance-observability.mjs')); checks += 1
assert(packageDocument.scripts['check:performance-architecture'].includes('test-performance-snapshot.mjs')); checks += 1
const verifier = read('opcgpro-vue/scripts/verify-performance-snapshot.mjs')
assert(verifier.includes('insufficient samples') && verifier.includes('--expected-commit')
  && verifier.includes('snapshot is older than')); checks += 1
const testrunVerifier = read('ops/windows/verify-l12-testrun-performance.ps1')
assert(testrunVerifier.includes('ExpectedCommit') && testrunVerifier.includes('--receipt')
  && testrunVerifier.includes('Refusing to overwrite')); checks += 1
const releaseVerifier = read('ops/windows/verify-l12.ps1')
assert(releaseVerifier.includes('@{ Source = "ops\\windows\\verify-l12-testrun-performance.ps1"; Target = "ops\\windows\\verify-l12-testrun-performance.ps1" }')); checks += 1
assert(!releaseVerifier.includes('[IO.Path]::GetRelativePath')
  && releaseVerifier.includes('$testrunFile.FullName.Substring($testrunRootPrefix.Length)'),
  'release packaging must remain compatible with Windows PowerShell 5.1'); checks += 1

console.log(`Performance observability architecture passed: ${checks}/${checks}`)
