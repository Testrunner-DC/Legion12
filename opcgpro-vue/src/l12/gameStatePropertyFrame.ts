import propertyNames from './gameStatePropertyNamesV1.json'

export const GAME_STATE_PROPERTY_FRAME_CAPABILITY = 'gameStatePropertyFrameV1'
export const GAME_STATE_PROPERTY_FRAME_TYPE = GAME_STATE_PROPERTY_FRAME_CAPABILITY
export const PROPERTY_FRAME_VOCABULARY_SHA256 = '9193B87AD3D50DD4B14D445A2B09FDCE62B2503746B2AA61D0B6F72E0FD9DC45'
const MAX_ORIGINAL_BYTES = 2 * 1024 * 1024
const MAX_TOKENS = 100000
// Same original limit and proved fixed-key/wrapper allowance as the server.
const MAX_ENCODED_BYTES = MAX_ORIGINAL_BYTES + 2 * MAX_TOKENS + 2048
const encoder = new TextEncoder()
const isObject = (value: unknown): value is Record<string, any> => value !== null && typeof value === 'object' && !Array.isArray(value)
const vocabulary = new Map(propertyNames.map((name, index) => ['@' + index.toString(36),
  { name, source: JSON.stringify(name) }] as const))

// Restore property lexemes only: values (including numeric spelling) remain
// byte-for-byte. Reject both original duplicates and decoded-key collisions
// before JSON.parse can collapse them; no value dictionary or cross-frame state.
function restoreOriginalSource(raw: string): string {
  const scopes: Array<{ source: Set<string>, decoded: Set<string> } | null> = []
  const changes: Array<{ from: number, to: number, source: string }> = []
  let payloadFrom = -1, payloadTo = -1, tokens = 0
  const inPayload = (at: number) => payloadFrom >= 0 && at >= payloadFrom && payloadTo < 0
  const token = (at: number) => {
    if (inPayload(at) && ++tokens > MAX_TOKENS) throw new Error('property frame token bound')
  }
  for (let at = 0; at < raw.length; at++) {
    const char = raw[at]
    if (char === '{' || char === '[') {
      token(at)
      scopes.push(char === '{' ? { source: new Set(), decoded: new Set() } : null)
      if (scopes.length > 65) throw new Error('property frame source depth')
    } else if (char === '}' || char === ']') {
      token(at)
      if (inPayload(at) && scopes.length === 2) payloadTo = at + 1
      scopes.pop()
    } else if (char === '"') {
      token(at)
      const from = at++
      for (; at < raw.length; at++) {
        if (raw[at] === '\\') at++
        else if (raw[at] === '"') break
      }
      const to = at + 1
      let next = to
      while (next < raw.length && /\s/.test(raw[next]!)) next++
      if (raw[next] !== ':') continue
      const lexeme = raw.slice(from, to), key: string = JSON.parse(lexeme)
      const scope = scopes.at(-1)
      if (!scope || scope.source.has(key)) throw new Error('duplicate property frame source key')
      scope.source.add(key)
      let name = key, source: string | null = null
      if (inPayload(from)) {
        if (key.startsWith('~')) {
          if (raw[from + 1] !== '~') throw new Error('noncanonical property escape marker')
          name = key.slice(1)
          if (!name.startsWith('@') && !name.startsWith('~')) throw new Error('unnecessary property escape marker')
          source = '"' + raw.slice(from + 2, to)
        } else if (key.startsWith('@')) {
          const entry = vocabulary.get(key)
          if (!entry || lexeme !== JSON.stringify(key)) throw new Error('unknown property token')
          name = entry.name; source = entry.source
        }
        if (source !== null) changes.push({ from, to, source })
      }
      if (scope.decoded.has(name)) throw new Error('duplicate decoded property')
      scope.decoded.add(name)
      if (scopes.length === 1 && key === 'payload') {
        next++
        while (next < raw.length && /\s/.test(raw[next]!)) next++
        if (raw[next] !== '{') throw new Error('property frame payload must be object')
        payloadFrom = next
      }
    } else if (char && /[0-9tfn-]/.test(char)) {
      token(at)
      while (at + 1 < raw.length && !/[\s,\]}]/.test(raw[at + 1]!)) at++
    }
  }
  if (scopes.length || payloadFrom < 0 || payloadTo <= payloadFrom) throw new Error('incomplete property frame source')
  const parts: string[] = []
  let copied = payloadFrom, restoredBytes = encoder.encode(raw.slice(payloadFrom, payloadTo)).byteLength
  for (const change of changes) {
    restoredBytes += encoder.encode(change.source).byteLength - encoder.encode(raw.slice(change.from, change.to)).byteLength
    parts.push(raw.slice(copied, change.from), change.source)
    copied = change.to
  }
  if (restoredBytes > MAX_ORIGINAL_BYTES) throw new Error('property frame original payload bound')
  parts.push(raw.slice(copied, payloadTo))
  return parts.join('')
}

/** Publish only after full validation. Nested card-frame checks receive the
 * original logical source, not the larger wrapper or reserialized values. */
export function restoreGameStatePropertyFrame(value: unknown, encoded?: string,
  onOriginalSource?: (source: string) => void): Record<string, any> | null {
  try {
    const raw = encoded ?? JSON.stringify(value)
    if (!isObject(value) || value.type !== GAME_STATE_PROPERTY_FRAME_TYPE || value.version !== 1
      || typeof value.matchId !== 'string' || !value.matchId || value.matchId.length > 256
      || Object.keys(value).sort().join(',') !== 'matchId,payload,type,version'
      || encoder.encode(raw).byteLength > MAX_ENCODED_BYTES) return null
    const source = restoreOriginalSource(raw)
    const payload = JSON.parse(source)
    const matchId = payload?.type === 'gameStateDelta' ? payload.matchId
      : payload?.type === 'gameStateCardFrameV1' ? payload.payload?.state?.matchId : payload?.state?.matchId
    if (!isObject(payload) || !['gameState', 'gameStateDelta', 'gameStateCardFrameV1'].includes(payload.type)
      || matchId !== value.matchId) return null
    onOriginalSource?.(source)
    return payload
  } catch { return null }
}
