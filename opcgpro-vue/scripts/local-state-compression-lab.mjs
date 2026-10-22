// Offline, synthetic-data laboratory only. Not a negotiated product protocol.
// Fresh streams and a checksum do NOT establish confidentiality of compressed lengths.
import {deflateRawSync, inflateRawSync} from 'node:zlib'
import {createHash} from 'node:crypto'

export const LAB_CAPABILITY = 'syntheticOfflineDeflateAuditV1'
export const MAX_RAW_BYTES = 2 * 1024 * 1024
const maxRatio = 128
const allowed = new Set(['gameState', 'gameStateDelta', 'gameStateCardFrameV1'])
const digest = bytes => createHash('sha256').update(bytes).digest('hex')
const utf8 = new TextDecoder('utf-8', {fatal: true})
const fail = () => { throw new Error('synthetic-compression-frame-rejected') }

export function encodeSyntheticPacket(raw, context = {}) {
  if (!Buffer.isBuffer(raw)) throw new TypeError('raw UTF8 Buffer required')
  const fallback = reason => ({compressed: false, bytes: raw, reason})
  if (context.scope !== 'synthetic-offline-only' || context.authenticated !== true
      || context.capability !== LAB_CAPABILITY) return fallback('not-negotiated-for-laboratory')
  if (raw.length < 1024 || raw.length > MAX_RAW_BYTES) return fallback('size')
  let message
  try { message = JSON.parse(utf8.decode(raw)) } catch { return fallback('invalid-json') }
  if (!allowed.has(message?.type)) return fallback('not-game-state')
  const packed = deflateRawSync(raw, {level: 1}) // No state/dictionary retained across calls.
  if (raw.length > packed.length * maxRatio) return fallback('expansion-ratio')
  const bytes = Buffer.from(JSON.stringify({type: LAB_CAPABILITY, algorithm: 'deflate-raw',
    rawBytes: raw.length, sha256: digest(raw), data: packed.toString('base64')}))
  if (bytes.length + 64 >= raw.length) return fallback('no-material-wire-saving')
  return {compressed: true, bytes, reason: 'laboratory-only-not-privacy-approved'}
}

export function decodeSyntheticFrame(wire, context = {}) {
  if (context.scope !== 'synthetic-offline-only' || context.authenticated !== true
      || context.capability !== LAB_CAPABILITY) return fail()
  if (!Buffer.isBuffer(wire) || wire.length > MAX_RAW_BYTES) return fail()
  try {
    const text = utf8.decode(wire), frame = JSON.parse(text)
    // Canonical laboratory wrapper also refuses duplicate keys, unknown fields and ambiguity.
    if (JSON.stringify(frame) !== text || Object.keys(frame).join(',') !== 'type,algorithm,rawBytes,sha256,data'
        || frame.type !== LAB_CAPABILITY || frame.algorithm !== 'deflate-raw'
        || !Number.isSafeInteger(frame.rawBytes) || frame.rawBytes < 1024 || frame.rawBytes > MAX_RAW_BYTES
        || typeof frame.sha256 !== 'string' || !/^[a-f0-9]{64}$/.test(frame.sha256)
        || typeof frame.data !== 'string' || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(frame.data)) return fail()
    const packed = Buffer.from(frame.data, 'base64')
    if (packed.length === 0 || packed.toString('base64') !== frame.data
        || frame.rawBytes > packed.length * maxRatio) return fail()
    const result = inflateRawSync(packed, {maxOutputLength: frame.rawBytes, info: true})
    if (result.engine.bytesWritten !== packed.length || result.buffer.length !== frame.rawBytes
        || digest(result.buffer) !== frame.sha256 || !allowed.has(JSON.parse(utf8.decode(result.buffer))?.type)) return fail()
    return result.buffer
  } catch { return fail() }
}

// Deliberately non-configurable: local roundtrips/cost cannot admit a live privacy-bearing stream.
export function productAdmission() {
  return {admitted: false, reason: 'same-packet-length-privacy-not-proven', productImportsAllowed: false}
}
