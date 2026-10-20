import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'

const fail = code => { throw Object.assign(new Error(code), {code}) }
const exact = (value, keys) => value && Object.getPrototypeOf(value) === Object.prototype
  && Object.keys(value).sort().join('|') === [...keys].sort().join('|')
const ROOT_KEYS = ['schema', 'kind', 'phase', 'candidate', 'coreSourceSha256', 'originalSloMs',
  'maximumWallMs', 'pressureBudgetMs', 'cleanupPending', 'finalAccepted', 'metrics']
const METRIC_KEYS = ['elapsedMs', 'stages', 'terminalFrames', 'actionBounds', 'http', 'commands',
  'websocket', 'payloadFloor', 'reconstructionErrors', 'presenceReconstructionErrors']
const PRIVATE_KEY = /password|token|credential|nonce|account(?:id|name)?$|username|roomcode|matchid|responsebody|payloadbody|cardname|cardid/i

// Only the caller's controlled numeric aggregates are exported; string values
// are omitted. This is not a sanitizer for arbitrary keys or business objects.
export function pressureNumericOnly(value, state = {nodes: 0}, depth = 0) {
  if (++state.nodes > 50000 || depth > 14) fail('pressure_checkpoint_numeric_budget')
  if (value === null || typeof value === 'boolean') return value
  if (typeof value === 'number') {
    if (!Number.isFinite(value) || value < 0) fail('pressure_checkpoint_numeric_value')
    return value
  }
  if (typeof value === 'string' || value === undefined) return undefined
  if (Array.isArray(value)) {
    if (value.length > 4096) fail('pressure_checkpoint_array_budget')
    return value.map(item => pressureNumericOnly(item, state, depth + 1)).filter(item => item !== undefined)
  }
  if (!value || Object.getPrototypeOf(value) !== Object.prototype) fail('pressure_checkpoint_object')
  const result = {}
  for (const [key, item] of Object.entries(value)) {
    if (!/^[A-Za-z_0-9-]{1,64}$/.test(key) || PRIVATE_KEY.test(key)) fail('pressure_checkpoint_private_field')
    const numeric = pressureNumericOnly(item, state, depth + 1)
    if (numeric !== undefined) result[key] = numeric
  }
  return result
}

export function createPressureMeasurementCheckpoint({candidate, coreSourceSha256, metrics}) {
  if (!/^[a-f0-9]{40}$/.test(candidate || '') || !/^[A-F0-9]{64}$/.test(coreSourceSha256 || ''))
    fail('pressure_checkpoint_binding')
  if (!exact(metrics, METRIC_KEYS)) fail('pressure_checkpoint_metrics_shape')
  return {schema: 1, kind: 'l12-pressure-numeric-checkpoint', phase: 'before-account-cleanup',
    candidate, coreSourceSha256, originalSloMs: 3000, maximumWallMs: 900000, pressureBudgetMs: 720000,
    cleanupPending: true, finalAccepted: false, metrics: pressureNumericOnly(metrics)}
}

export function validatePressureMeasurementCheckpoint(value) {
  if (!exact(value, ROOT_KEYS) || value.schema !== 1 || value.kind !== 'l12-pressure-numeric-checkpoint'
    || value.phase !== 'before-account-cleanup' || value.originalSloMs !== 3000
    || value.maximumWallMs !== 900000 || value.pressureBudgetMs !== 720000
    || value.cleanupPending !== true || value.finalAccepted !== false
    || !/^[a-f0-9]{40}$/.test(value.candidate || '') || !/^[A-F0-9]{64}$/.test(value.coreSourceSha256 || '')
    || !exact(value.metrics, METRIC_KEYS)) fail('pressure_checkpoint_shape')
  if (JSON.stringify(value.metrics) !== JSON.stringify(pressureNumericOnly(value.metrics)))
    fail('pressure_checkpoint_non_numeric_metrics')
  if (!Array.isArray(value.metrics.stages) || value.metrics.stages.length > 3
    || !Array.isArray(value.metrics.terminalFrames) || value.metrics.terminalFrames.length > 60)
    fail('pressure_checkpoint_coverage_bounds')
  const slots = new Set()
  for (const frame of value.metrics.terminalFrames) {
    if (!Number.isSafeInteger(frame.numericClientSlot) || frame.numericClientSlot < 0 || frame.numericClientSlot >= 60
      || slots.has(frame.numericClientSlot) || !Number.isSafeInteger(frame.numericRoomSlot)
      || frame.numericRoomSlot !== Math.floor(frame.numericClientSlot / 2)
      || frame.receiver !== frame.numericClientSlot % 2 || !Number.isFinite(frame.sendToCallbackMs)
      || frame.sendToCallbackMs < 0 || frame.exceededOriginal3s !== Number(frame.sendToCallbackMs > 3000))
      fail('pressure_checkpoint_terminal_identity')
    slots.add(frame.numericClientSlot)
  }
  return value
}

export function persistPressureMeasurementCheckpoint(output, input) {
  // The invoking acceptance entry must also call Assert-L12GeneratedPath.
  // Re-resolve the physical parent here, rather than trusting a D-spelled alias.
  if (!path.isAbsolute(output)) fail('pressure_checkpoint_absolute_output')
  const parent = fs.realpathSync(path.dirname(output))
  if (!/^D:[\\/]/i.test(parent) || path.basename(output) !== 'measurement-before-account-cleanup.json')
    fail('pressure_checkpoint_output_scope')
  const value = validatePressureMeasurementCheckpoint(input)
  const bytes = Buffer.from(JSON.stringify(value) + '\n')
  if (bytes.length > 2 * 1024 * 1024) fail('pressure_checkpoint_byte_budget')
  const fd = fs.openSync(path.join(parent, path.basename(output)), 'wx', 0o600)
  try {
    fs.writeFileSync(fd, bytes)
    fs.fsyncSync(fd)
  } finally { fs.closeSync(fd) }
  return {bytes: bytes.length, sha256: crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase(),
    finalAccepted: false, cleanupPending: true}
}
