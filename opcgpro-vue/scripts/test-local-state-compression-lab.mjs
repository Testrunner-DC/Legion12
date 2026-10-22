import assert from 'node:assert/strict'
import fs from 'node:fs'
import {createHash} from 'node:crypto'
import {deflateRawSync} from 'node:zlib'
import {performance} from 'node:perf_hooks'
import {encodeSyntheticPacket, decodeSyntheticFrame, productAdmission, LAB_CAPABILITY, MAX_RAW_BYTES} from './local-state-compression-lab.mjs'

let cases = 0
const check = fn => { fn(); cases++ }
const ctx = {scope: 'synthetic-offline-only', authenticated: true, capability: LAB_CAPABILITY}
const hash = value => createHash('sha256').update(value).digest('hex')
const raw = Buffer.from(JSON.stringify({type: 'gameState', state: {you: 1,
  players: [{name: '公开名字'}, {hand: Array.from({length: 18}, (_, i) => ({cardId: `S01-${i.toString().padStart(4, '0')}`, effect: '完整字段，保留中文、引号"与换行\n😀'}))}]}}))
const encoded = encodeSyntheticPacket(raw, ctx)
check(() => assert.equal(encoded.compressed, true))
check(() => assert.deepEqual(decodeSyntheticFrame(encoded.bytes, ctx), raw))
for (const context of [{}, {...ctx, scope: 'network'}, {...ctx, authenticated: false}, {...ctx, capability: 'legacy'}]) {
  check(() => assert.equal(encodeSyntheticPacket(raw, context).bytes, raw))
  check(() => assert.throws(() => decodeSyntheticFrame(encoded.bytes, context)))
}
for (const type of ['hello', 'auth', 'chat', 'chatMessage', 'presence', 'requestAck', 'unknown']) {
  const bytes = Buffer.from(JSON.stringify({type, text: 'not compressed'.repeat(200)}))
  check(() => assert.equal(encodeSyntheticPacket(bytes, ctx).bytes, bytes))
}
for (const bytes of [Buffer.from('{}'), Buffer.from('{broken'), Buffer.from([0xff, 0xfe]), Buffer.alloc(MAX_RAW_BYTES + 1)])
  check(() => assert.equal(encodeSyntheticPacket(bytes, ctx).bytes, bytes))
check(() => assert.equal(encodeSyntheticPacket(Buffer.from(JSON.stringify({type: 'gameState', padding: 'x'.repeat(200000)})), ctx).reason, 'expansion-ratio'))
const noise = Buffer.from(JSON.stringify({type: 'gameState', data: Buffer.concat(Array.from({length: 32}, (_, i) => Buffer.from(hash(String(i)), 'hex'))).toString('base64')}))
check(() => assert.equal(encodeSyntheticPacket(noise, ctx).reason, 'no-material-wire-saving'))
encodeSyntheticPacket(Buffer.from(JSON.stringify({type: 'gameState', other: 'another packet'.repeat(200)})), ctx)
check(() => assert.deepEqual(encodeSyntheticPacket(raw, ctx).bytes, encoded.bytes))
check(() => assert.deepEqual(decodeSyntheticFrame(encoded.bytes, ctx), raw))
const edit = mutate => {const frame = JSON.parse(encoded.bytes); mutate(frame); return Buffer.from(JSON.stringify(frame))}
for (const wire of [
  encoded.bytes.subarray(0, encoded.bytes.length - 1), Buffer.from(' ' + encoded.bytes), Buffer.from([0xff]),
  Buffer.alloc(MAX_RAW_BYTES + 1), Buffer.from(encoded.bytes.toString().replace('"algorithm":', '"type":"duplicate","algorithm":')),
  edit(f => {f.extra = true}), edit(f => {f.algorithm = 'gzip'}), edit(f => {f.type = 'unknown'}),
  edit(f => {f.rawBytes = -1}), edit(f => {f.rawBytes = 1023}), edit(f => {f.rawBytes = 1024.5}),
  edit(f => {f.rawBytes = MAX_RAW_BYTES + 1}), edit(f => {f.rawBytes++}), edit(f => {f.rawBytes--}),
  edit(f => {f.sha256 = '0'.repeat(64)}), edit(f => {f.sha256 = 'unknown'}), edit(f => {f.data = ''}),
  edit(f => {f.data = '!!!!'}), edit(f => {f.data += ' '}),
  edit(f => {f.data = Buffer.from(f.data, 'base64').subarray(0, 8).toString('base64')}),
  edit(f => {f.data = Buffer.concat([Buffer.from(f.data, 'base64'), Buffer.from('trailing')]).toString('base64')}),
  edit(f => {const b = Buffer.from(f.data, 'base64'); f.data = Buffer.concat([b, b]).toString('base64')}),
  edit(f => {f.data = deflateRawSync(Buffer.alloc(200000, 120)).toString('base64'); f.rawBytes = 1024}),
]) check(() => assert.throws(() => decodeSyntheticFrame(wire, ctx)))
const forged = bytes => Buffer.from(JSON.stringify({type: LAB_CAPABILITY, algorithm: 'deflate-raw', rawBytes: bytes.length,
  sha256: hash(bytes), data: deflateRawSync(bytes).toString('base64')}))
for (const bytes of [Buffer.from(JSON.stringify({type: 'chat', text: 'chat'.repeat(400)})),
  Buffer.concat([Buffer.from('{"type":"gameState","text":"'), Buffer.alloc(1500, 0xff), Buffer.from('"}')])])
  check(() => assert.throws(() => decodeSyntheticFrame(forged(bytes), ctx)))
check(() => assert.deepEqual(productAdmission(), {admitted: false, reason: 'same-packet-length-privacy-not-proven', productImportsAllowed: false}))
for (const file of ['../src/l12/net.ts', '../../服务端WebSocket/TwelveLegions/L12WebSocketServer.cs',
  '../../服务端WebSocket/TwelveLegions/L12OutboundConnection.cs', '../package.json']) {
  const source = fs.readFileSync(new URL(file, import.meta.url), 'utf8')
  check(() => assert.equal(source.includes(LAB_CAPABILITY) || source.includes('local-state-compression-lab'), false))
}

// A controlled compression unit vector, NOT a claim of a legal complete game or a live attack.
// Equal-length public names and private IDs isolate an interaction hidden by uncompressed lengths.
const ids = ['S01-0015', 'S01-0418']
const vector = (name, secret) => Buffer.from(JSON.stringify({type: 'gameState', state: {
  players: [{name, deckName: name.repeat(3)}, {hand: [{cardId: secret, instanceId: 'private-only'}]}],
  ordinary: Array.from({length: 45}, (_, i) => ({revision: i, text: `公开固定内容${i}`}))}}))
const matrix = ids.map(secret => ids.map(name => {
  const bytes = vector(name, secret), packed = encodeSyntheticPacket(bytes, ctx)
  assert.equal(packed.compressed, true)
  return {rawBytes: bytes.length, frameBytes: packed.bytes.length}
}))
check(() => assert.equal(new Set(matrix.flat().map(v => v.rawBytes)).size, 1))
const interaction = matrix[0][0].frameBytes - matrix[0][1].frameBytes - matrix[1][0].frameBytes + matrix[1][1].frameBytes
check(() => assert.notEqual(interaction, 0, 'Fresh streams do not establish same-packet length noninterference'))

const result = {schema: 1, status: 'LOCAL_LAB_CHECKS_PASSED_PRODUCT_ADMISSION_REJECTED', cases,
  controlledLengthVector: {syntheticUnitVectorNotLegalGame: true, matrix, interactionBytes: interaction},
  productAdmission: productAdmission(), networkCalls: 0, deployment: false,
  limitations: ['Length observer must see victim connection lengths; opponent own projection has no victim hand',
    'Names are chosen before play; no claim of unlimited adaptive renaming or practical card recovery',
    'Checksum is corruption detection, not authentication or confidentiality',
    'Bounded native synchronous inflate is not preemptive time isolation or a browser/reliable-queue implementation']}

if (process.argv[2]) {
  const source = fs.readFileSync(process.argv[2])
  const fixture = JSON.parse(source)
  assert.equal(fixture.syntheticOnly, true); assert.equal(fixture.rows.length, 52)
  assert.equal(fixture.acceptedServerSha256, '818441D28D5DFBE95C052117B22AB234C55BBE7459E8C653F71800F6411581ED')
  const rows = [], packets = []
  for (const row of fixture.rows) {
    assert.equal(row.attackerOwnProjectionHidden, true)
    for (const kind of ['full', 'delta']) {
      const bytes = Buffer.from(row[kind], 'base64'), packed = encodeSyntheticPacket(bytes, ctx)
      check(() => assert.equal(packed.compressed, true))
      check(() => assert.deepEqual(decodeSyntheticFrame(packed.bytes, ctx), bytes))
      const parsed = JSON.parse(bytes)
      if (kind === 'delta') check(() => assert.equal(parsed.type, 'gameStateDelta'))
      packets.push(bytes); rows.push({seed: row.seed, guess: row.guess, privateHandHash: row.privateHandHash, kind,
        rawBytes: bytes.length, frameBytes: packed.bytes.length})
    }
  }
  for (const seed of fixture.seeds) for (const kind of ['full', 'delta']) {
    const group = rows.filter(r => r.seed === seed && r.kind === kind)
    check(() => assert.equal(new Set(group.map(r => r.rawBytes)).size, 1))
    check(() => assert.equal(new Set(group.map(r => r.privateHandHash)).size, 1))
  }
  // Real worlds contain other public differences; do not mislabel their length variation
  // as proof of card recovery. The controlled unit vector above isolates that property.
  const variation = fixture.seeds.map(seed => Object.fromEntries(['seed', 'full', 'delta'].map(kind => kind === 'seed' ? [kind, seed]
    : [kind, [...new Set(rows.filter(r => r.seed === seed && r.kind === kind).map(r => r.frameBytes))].sort((a,b) => a-b)])))
  let encodeMs = 0, decodeMs = 0, originalBytes = 0, framedBytes = 0
  for (const bytes of packets) decodeSyntheticFrame(encodeSyntheticPacket(bytes, ctx).bytes, ctx)
  const before = process.memoryUsage()
  for (let round = 0; round < 8; round++) for (const bytes of packets) {
    let start = performance.now(); const packed = encodeSyntheticPacket(bytes, ctx); encodeMs += performance.now() - start
    start = performance.now(); const restored = decodeSyntheticFrame(packed.bytes, ctx); decodeMs += performance.now() - start
    assert.deepEqual(restored, bytes); originalBytes += bytes.length; framedBytes += packed.bytes.length
  }
  result.actualEngine = {fixtureSha256: hash(source), frames: packets.length, realCommands: fixture.realCommands,
    rounds: 8, exactRoundTrips: packets.length * 8, originalBytes, framedBytes,
    framedReductionPercent: 100 * (1 - framedBytes / originalBytes), encodeMs, decodeMs,
    memoryBefore: before, memoryAfter: process.memoryUsage(), memoryScope: 'Instant process samples, NOT per-packet allocations or peak/native total',
    variationNotCardRecoveryProof: variation, networkCalls: 0, cases}
}
result.cases = cases
console.log(JSON.stringify(result))
