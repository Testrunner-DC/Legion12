import assert from 'node:assert/strict'

// Diagnostics only: never a decoder, protocol gate, or retained payload recorder.
const fields = Object.freeze({
  gameStateDelta: ['handSplices', 'eventWindow', 'changes'],
  gameStateCardFrameV1: ['payload', 'cardSnapshots', 'eventCardRefs', 'lastActionCardRefs'],
})
const stages = new Set(['setup', 'setup-10', 'setup-20', 'setup-30', 'payload-preparation',
  'rooms-10', 'rooms-20', 'rooms-30', 'cleanup'])
const stageKey = value => stages.has(value) ? value : 'other'
const maximumBytes = 2 * 1024 * 1024
const blank = names => Object.fromEntries([...names, 'headerAndSyntax'].map(name => [name, 0]))

// Locate original value spans. JSON.parse only validates syntax; its values are
// discarded, never reserialized for byte accounting. Duplicate keys fail closed.
function rawValueBytes(raw, wireType) {
  assert.equal(typeof raw, 'string')
  assert.ok(Buffer.byteLength(raw, 'utf8') <= maximumBytes)
  const parsed = JSON.parse(raw)
  assert.ok(parsed && typeof parsed === 'object' && !Array.isArray(parsed) && parsed.type === wireType)
  let index = 0
  const whitespace = () => {while (/\s/u.test(raw[index] ?? '') && index < raw.length) index++}
  const stringEnd = () => {
    assert.equal(raw[index++], '"')
    while (index < raw.length) {
      const char = raw[index++]
      if (char === '\\') {index++; continue}
      if (char === '"') return
    }
    throw new Error('unterminated')
  }
  whitespace(); assert.equal(raw[index++], '{'); whitespace()
  const seen = new Set(), values = blank(fields[wireType])
  if (raw[index] !== '}') while (true) {
    const keyStart = index
    stringEnd()
    const key = JSON.parse(raw.slice(keyStart, index))
    assert.ok(!seen.has(key) && seen.size < 64)
    seen.add(key); whitespace(); assert.equal(raw[index++], ':'); whitespace()
    const start = index
    const stack = []
    while (index < raw.length) {
      const char = raw[index]
      if (char === '"') {stringEnd(); continue}
      if (char === '{' || char === '[') {stack.push(char); index++; continue}
      if (char === '}' || char === ']') {
        if (!stack.length) break
        assert.equal(stack.pop(), char === '}' ? '{' : '['); index++; continue
      }
      if (char === ',' && !stack.length) break
      index++
    }
    assert.equal(stack.length, 0)
    let end = index
    while (end > start && /\s/u.test(raw[end - 1])) end--
    assert.ok(end > start)
    if (fields[wireType].includes(key)) values[key] = Buffer.byteLength(raw.slice(start, end), 'utf8')
    whitespace()
    if (raw[index] === '}') break
    assert.equal(raw[index++], ','); whitespace()
  }
  assert.equal(raw[index++], '}'); whitespace(); assert.equal(index, raw.length)
  values.headerAndSyntax = Buffer.byteLength(raw, 'utf8') - Object.values(values).reduce((sum, value) => sum + value, 0)
  assert.ok(values.headerAndSyntax >= 0)
  return values
}

export function createLocalWireCostObserver({clock = () => performance.now()} = {}) {
  const rows = new Map()
  const bucket = (stage, type) => {
    const key = stageKey(stage) + ':' + type
    if (!rows.has(key)) rows.set(key, {stage: stageKey(stage), type, packets: 0, wireBytes: 0,
      attributedPackets: 0, failedPackets: 0, unattributedBytes: 0,
      parts: blank(fields[type]), diagnosticMs: 0, maximumDiagnosticMs: 0})
    return rows.get(key)
  }
  return {
    observe({stage, wireType, raw, wireBytes}) {
      if (!Object.hasOwn(fields, wireType)) return
      const row = bucket(stage, wireType), started = clock()
      // This runs only after original ACK/dispatch timestamps in the local adapter.
      // Failure never rejects a valid game state or requests business recovery.
      row.packets++
      const validCount = Number.isSafeInteger(wireBytes) && wireBytes >= 0
      row.wireBytes += validCount ? wireBytes : 0
      try {
        assert.ok(validCount && Buffer.byteLength(raw, 'utf8') === wireBytes)
        const parts = rawValueBytes(raw, wireType)
        for (const name of Object.keys(row.parts)) row.parts[name] += parts[name]
        row.attributedPackets++
      } catch {
        row.failedPackets++
        row.unattributedBytes += validCount ? wireBytes : 0
      }
      const elapsed = clock() - started
      if (Number.isFinite(elapsed) && elapsed >= 0) {
        row.diagnosticMs += elapsed; row.maximumDiagnosticMs = Math.max(row.maximumDiagnosticMs, elapsed)
      }
    },
    summarize(wsRows) {
      assert.ok(Array.isArray(wsRows))
      // Existing wsRows contain numeric counts only, not raw bodies.
      const expected = new Map()
      for (const row of wsRows) {
        if (!Object.hasOwn(fields, row.type)) continue
        const key = stageKey(row.stage) + ':' + row.type
        const value = expected.get(key) ?? {packets: 0, wireBytes: 0}
        assert.ok(Number.isSafeInteger(row.bytes) && row.bytes >= 0)
        value.packets++; value.wireBytes += row.bytes; expected.set(key, value)
      }
      const keys = new Set([...expected.keys(), ...rows.keys()])
      const output = [...keys].sort().map(key => {
        const actual = rows.get(key)
        const reference = expected.get(key) ?? {packets: 0, wireBytes: 0}
        if (!actual) return {stage: key.split(':')[0], type: key.split(':')[1],
          packets: 0, wireBytes: 0, missingObserverPackets: reference.packets,
          missingObserverBytes: reference.wireBytes, reconciled: false}
        const copy = {...actual, parts: {...actual.parts}}
        const accountedBytes = Object.values(copy.parts).reduce((sum, value) => sum + value, 0) + copy.unattributedBytes
        return {...copy, expectedPackets: reference.packets, expectedWireBytes: reference.wireBytes,
          accountedBytes, reconciled: copy.packets === reference.packets
            && copy.wireBytes === reference.wireBytes && accountedBytes === copy.wireBytes}
      })
      return {schema: 1, enabled: true, rawPayloadsRetained: false, diagnosticOnlyNoCapacityClaim: true,
        byteBasis: 'original received UTF8 JSON, selected top-level values plus header/syntax; no reserialization',
        observedTypes: Object.keys(fields), rows: output,
        reconciled: output.every(row => row.reconciled),
        failures: output.reduce((sum, row) => sum + (row.failedPackets ?? 0), 0)}
    },
  }
}

