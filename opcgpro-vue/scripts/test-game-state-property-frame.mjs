import assert from 'node:assert/strict'
import fs from 'node:fs'
import { propertyFrameSourceModuleUrl } from './property-frame-source-module.mjs'
import { createTestrunProtocol, currentProtocolClientBinding } from './testrun-pressure-protocol.mjs'

const { restoreGameStatePropertyFrame: restore, GAME_STATE_PROPERTY_FRAME_CAPABILITY: capability } = await import(propertyFrameSourceModuleUrl())
const names = JSON.parse(fs.readFileSync(new URL('../src/l12/gameStatePropertyNamesV1.json', import.meta.url), 'utf8'))
const token = name => '@' + names.indexOf(name).toString(36)
const encode = value => {
  if (Array.isArray(value)) return value.map(encode)
  if (!value || typeof value !== 'object') return value
  const result = {}
  for (const [name, item] of Object.entries(value)) Object.defineProperty(result,
    names.includes(name) ? token(name) : /^[~@]/.test(name) ? '~' + name : name,
    { value: encode(item), enumerable: true })
  return result
}
const plain = { type: 'gameState', requestId: 'ack', state: { matchId: 'synthetic', revision: 2,
  players: [{ hand: [{ name: '只对本人可见', effectText: '"state": <>&😀', '@0': 1, '~unknown': null }] }],
  recentEvents: [], lastAction: null } }
const frame = value => ({ type: capability, version: 1,
  matchId: value.type === 'gameStateDelta' ? value.matchId
    : value.type === 'gameStateCardFrameV1' ? value.payload.state.matchId : value.state.matchId,
  payload: encode(value) })
const original = JSON.stringify(plain), packed = frame(plain)
let cases = 0
assert.deepEqual(restore(packed), plain); assert.equal(JSON.stringify(plain), original); cases++
const restored = restore(packed); restored.state.players[0].hand[0].name = '改动'; assert.equal(JSON.stringify(plain), original); cases++
const variants = [value => value.version = 2, value => value.extra = true, value => delete value.matchId,
  value => value.matchId = 'other', value => value.payload[token('type')] = 'chatMessage',
  value => value.payload['@zzzzzzz'] = 1, value => value.payload['@00'] = 1,
  value => value.payload['@A'] = 1, value => value.payload['@-1'] = 1,
  value => value.payload.type = 'gameState', value => value.payload = null]
for (const mutate of variants) { const value = structuredClone(packed); mutate(value); assert.equal(restore(value), null); cases++ }
for (const value of [
  { type: 'gameStateDelta', matchId: 'synthetic', baseRevision: 1, revision: 2, changes: [] },
  { type: 'gameStateCardFrameV1', payload: plain, cardSnapshots: [], eventCardRefs: [], lastActionCardRefs: null },
]) { assert.deepEqual(restore(frame(value)), value); cases++ }
const polluted = JSON.parse('{"type":"gameState","state":{"matchId":"synthetic","__proto__":{"injected":true},"constructor":"kept"}}')
assert.deepEqual(restore(frame(polluted)), polluted); assert.equal({}.injected, undefined); cases++
const big = frame({ type: 'gameState', state: { matchId: 'synthetic', text: 'x'.repeat(2 * 1024 * 1024) } })
assert.equal(restore(big), null); cases++
const nodes = frame({ type: 'gameState', state: { matchId: 'synthetic', nodes: Array(100001).fill(null) } })
assert.equal(restore(nodes), null); cases++
let deep = {}; for (let index = 0; index < 65; index++) deep = { child: deep }
assert.equal(restore(frame({ type: 'gameState', state: { matchId: 'synthetic', deep } })), null); cases++
const net = fs.readFileSync(new URL('../src/l12/net.ts', import.meta.url), 'utf8')
for (const raw of [JSON.stringify(packed).replace('"version":1', '"version":0,"version":1'),
  JSON.stringify(packed).replace(JSON.stringify(token('type')) + ':"gameState"',
    JSON.stringify(token('type')) + ':"gameState",' + JSON.stringify(token('type')) + ':"gameState"')]) {
  assert.equal(restore(JSON.parse(raw), raw), null); cases++
}
const keyToken = token('type')
const escapedDuplicate = JSON.stringify(packed).replace(JSON.stringify(keyToken) + ':"gameState"',
  JSON.stringify(keyToken) + ':"gameState","\\u0040' + keyToken.slice(1) + '":"gameState"')
assert.equal(restore(JSON.parse(escapedDuplicate), escapedDuplicate), null); cases++
assert.match(net, /\[GAME_STATE_PROPERTY_FRAME_CAPABILITY\]: true/)
assert.match(net, /negotiatedGameStatePropertyFrame \? restoreGameStatePropertyFrame/)
const propertyAt = net.indexOf('restoreGameStatePropertyFrame(message, encodedMessage,')
assert(propertyAt > 0 && propertyAt < net.indexOf("if (message.type === 'gameStateCardFrameV1')"))
assert.match(net, /restoreGameStateCardFrame\(message, snapshotEncodedMessage\)/)
assert.match(net, /negotiatedGameStatePropertyFrame = false[\s\S]*?plainFull: true/); cases++
for (const publicLength of [2096934, 2096974]) for (const privateLength of [0, 40]) {
  const value = { type: 'gameState', state: { matchId: 'synthetic', publicText: 'a'.repeat(publicLength),
    privateText: 's'.repeat(privateLength) } }
  assert(Buffer.byteLength(JSON.stringify(value)) <= 2 * 1024 * 1024)
  const restored = restore(frame(value))
  assert(restored !== null, 'Legal original input cannot fail only because fixed framing adds bytes')
  assert.deepEqual(restored, value)
  cases++
}
const { codec } = await createTestrunProtocol(currentProtocolClientBinding())
const nested = { type: 'gameStateCardFrameV1', payload: { type: 'gameState', state: {
  matchId: 's'.repeat(200), recentEvents: [], lastAction: null, syntheticPadding: '' } },
  cardSnapshots: [], eventCardRefs: [], lastActionCardRefs: null }
nested.payload.state.syntheticPadding = 'p'.repeat(2 * 1024 * 1024 - 16 - Buffer.byteLength(JSON.stringify(nested)))
const nestedPacket = frame(nested), nestedRaw = JSON.stringify(nestedPacket)
assert(Buffer.byteLength(nestedRaw) > 2 * 1024 * 1024)
let originalSource = ''
const nestedRestored = restore(nestedPacket, nestedRaw, source => { originalSource = source })
assert.deepEqual(nestedRestored, nested)
assert.equal(originalSource, JSON.stringify(nested))
assert.deepEqual(codec.restoreGameStateCardFrame(nestedRestored, originalSource), nested.payload); cases++
for (const count of [99988, 99989]) {
  const value = { type: 'gameState', state: { matchId: 'synthetic', nodes: Array(count).fill(null) } }
  assert.equal(restore(frame(value)) !== null, count === 99988); cases++
}
for (const count of [61, 62]) {
  let value = {}; for (let index = 0; index < count; index++) value = { child: value }
  assert.equal(restore(frame({ type: 'gameState', state: { matchId: 'synthetic', deep: value } })) !== null,
    count === 61); cases++
}
for (const raw of [JSON.stringify(packed).replace(JSON.stringify(token('type')), '"\\u0040' + token('type').slice(1) + '"'),
  JSON.stringify(packed).replace('"~@0"', '"\\u007e@0"'),
  JSON.stringify(packed).replace('"~@0"', '"~unnecessary"')]) {
  assert.equal(restore(JSON.parse(raw), raw), null); cases++
}
const numeric = frame({ type: 'gameState', state: { matchId: 'synthetic', syntheticNumber: 1000000000 } })
const numericRaw = JSON.stringify(numeric).replace('1000000000', '1e9')
let numericSource = ''
assert(restore(JSON.parse(numericRaw), numericRaw, source => { numericSource = source }))
assert(numericSource.includes('1e9')); cases++
const encodedLimit = 2 * 1024 * 1024 + 2 * 100000 + 2048
const baseRaw = JSON.stringify(packed), paddedRaw = baseRaw + ' '.repeat(encodedLimit - Buffer.byteLength(baseRaw))
assert.deepEqual(restore(packed, paddedRaw), plain)
assert.equal(restore(packed, paddedRaw + ' '), null); cases++
console.log(JSON.stringify({ status: 'passed', cases, actualClientDecoder: true, networkCalls: 0, onlineCapacityAccepted: false }))
