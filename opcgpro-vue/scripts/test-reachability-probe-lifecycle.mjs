import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { closeNotificationProbeResources as closeProbeResources } from './verify-notification-real-chain.mjs'

const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor
const scripts = ['verify-editor-replay-mobile-reachability.mjs', 'verify-site-detail-reachability.mjs']
let cases = 0
for (const script of scripts) {
  const source = fs.readFileSync(new URL(script, import.meta.url), 'utf8')
  assert.ok(source.indexOf('Assert-L12GeneratedPath') < source.indexOf('fs.mkdirSync'), 'Shared storage guard must precede output creation')
  assert.match(source, /\$ErrorActionPreference="Stop"; \. \$env:L12_STORAGE_GUARD/)
  assert.match(source, /L12GeneratedStorage\.ps1/)
  const start = source.lastIndexOf('} finally {') + '} finally {'.length
  const end = source.indexOf('\n}\n', start)
  assert.ok(start > 11 && end > start, 'The real owner finalizer must be found')
  const body = source.slice(start, end)
  assert.doesNotMatch(body, /await browser\??\.close|await server\.close/)
  const finalize = new AsyncFunction('browser', 'server', 'report', 'closeProbeResources', 'recordFailure', 'path', 'fs', 'output', 'appendProgress', body)
  for (const rejected of [[], ['browser'], ['vite'], ['browser', 'vite'], null]) {
    const called = [], written = [], progress = []
    const report = { status: 'passed', matrix: [], failures: [] }
    const resource = name => rejected === null ? undefined : { close: async () => {
      called.push(name)
      if (rejected.includes(name)) throw new Error('synthetic close failure')
    } }
    await finalize(resource('browser'), resource('vite'), report, closeProbeResources,
      (test, error) => report.failures.push({ test, message: error.message }), path,
      { writeFileSync: (file, value) => written.push({ file, value: JSON.parse(value) }) },
      'synthetic-no-disk-output', value => progress.push(value))
    assert.deepEqual(called.toSorted(), rejected === null ? [] : ['browser', 'vite'])
    assert.deepEqual(report.cleanupResourceFailures.toSorted(), (rejected ?? []).toSorted())
    assert.equal(report.status, rejected?.length ? 'failed' : 'passed')
    assert.equal(written.length, 1, 'A close rejection must not suppress the real report writer')
    assert.equal(written[0].value.status, report.status)
    assert.equal(progress.at(-1).stage, 'completed')
    cases++
  }
}
console.log(JSON.stringify({ status: 'passed', cases, scripts: scripts.length, browsersLaunched: 0, filesWritten: 0 }))
