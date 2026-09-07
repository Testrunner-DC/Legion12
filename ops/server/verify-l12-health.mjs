#!/usr/bin/env node
import process from 'node:process'

const expectedCommit = String(process.argv[2] ?? '').trim()
if (!/^[0-9a-f]{40}$/.test(expectedCommit)) {
  process.stderr.write('expected commit must be a 40-character lowercase Git SHA\n')
  process.exit(2)
}
const mode = String(process.argv[3] ?? '').trim()
if (mode !== '' && mode !== '--allow-maintenance') {
  process.stderr.write('health verification mode must be --allow-maintenance when provided\n')
  process.exit(2)
}
const allowMaintenance = mode === '--allow-maintenance'

const chunks = []
let bytes = 0
for await (const chunk of process.stdin) {
  bytes += chunk.length
  if (bytes > 1024 * 1024) {
    process.stderr.write('health response exceeds 1 MiB\n')
    process.exit(2)
  }
  chunks.push(chunk)
}

let health
try {
  health = JSON.parse(Buffer.concat(chunks).toString('utf8'))
} catch {
  process.stderr.write('health response is not valid JSON\n')
  process.exit(1)
}

const expectedEngine = `l12-engine/${expectedCommit}`
const statusIsConsistent = (health?.status === 'ok' && health?.maintenance === false)
  || (allowMaintenance && health?.status === 'maintenance' && health?.maintenance === true)
if (!statusIsConsistent
    || health?.service !== 'twelve-legions'
    || health?.serverVersion !== expectedCommit
    || health?.engineVersion !== expectedEngine) {
  const expectedStatus = allowMaintenance ? 'ok(false) or maintenance(true)' : 'ok(false)'
  process.stderr.write(`health readiness or identity mismatch: expected ${expectedStatus} / ${expectedCommit} / ${expectedEngine}\n`)
  process.exit(1)
}

process.stdout.write(`${expectedCommit}\n`)
