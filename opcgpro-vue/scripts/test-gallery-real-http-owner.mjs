import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { closeNotificationProbeResources as closeProbeResources } from './verify-notification-real-chain.mjs'

const source = fs.readFileSync(new URL('./verify-gallery-real-http.mjs', import.meta.url), 'utf8')
const start = source.lastIndexOf('} finally {') + '} finally {'.length
const end = source.lastIndexOf('\n}')
assert.ok(start > 11 && end > start)
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor
const finalize = new AsyncFunction('browser', 'vite', 'stopBackend', 'report', 'backend', 'errors', 'requests', 'backendPids', 'path', 'fs', 'output', 'closeProbeResources', 'process', 'console', source.slice(start, end))
let cases = 0
for (const rejected of [[], ['browser'], ['vite'], ['backend'], ['browser', 'vite', 'backend']]) {
  const called = [], written = [], processState = { exitCode: 0 }
  const report = { status: 'passed' }
  const backend = { exitCode: null, signalCode: null }
  const browser = { isConnected: () => false, close: () => {
    called.push('browser'); if (rejected.includes('browser')) throw new Error('synthetic browser close')
  } }
  const vite = { httpServer: { listening: false }, close: async () => {
    called.push('vite'); if (rejected.includes('vite')) throw new Error('synthetic Vite close')
  } }
  const stopBackend = async () => {
    called.push('backend'); if (rejected.includes('backend')) throw new Error('synthetic backend close')
    backend.exitCode = 0
  }
  await finalize(browser, vite, stopBackend, report, backend, [], [], [], path,
    { writeFileSync: (file, value) => written.push({ file, value: JSON.parse(value) }) },
    'synthetic-no-disk-output', closeProbeResources, processState, { log() {} })
  assert.deepEqual(called.toSorted(), ['backend', 'browser', 'vite'])
  assert.deepEqual(report.cleanupResourceFailures.toSorted(), rejected.toSorted())
  assert.equal(report.status, rejected.length ? 'failed' : 'passed')
  assert.equal(processState.exitCode, rejected.length ? 1 : 0)
  assert.equal(written.length, 1)
  assert.equal(written[0].value.status, report.status)
  cases++
}
console.log(JSON.stringify({ status: 'passed', cases, independentResources: 3, browsersLaunched: 0, filesWritten: 0 }))
