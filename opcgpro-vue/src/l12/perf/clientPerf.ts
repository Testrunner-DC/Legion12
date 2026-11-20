// 客户端性能采样（方案 §7）：只在被抽中的会话里聚合少量指标，靠消息/tick 驱动，
// 30 秒一次用 sendBeacon 上报聚合值；服务端只打印一条 [L12性能] 日志且不落库。
// 不引入自有定时器与 raw fetch（性能架构锁 P0-P4）；任何异常都必须静默，不能影响对局。
export const CLIENT_PERF_ENDPOINT = '/api/perf/client'
export const CLIENT_PERF_SAMPLE_DENOMINATOR = 10
export const CLIENT_PERF_SESSION_CAP = 200
export const CLIENT_PERF_MAX_BODY_BYTES = 512
export const CLIENT_PERF_FLUSH_MS = 30_000
export const CLIENT_PERF_LONG_FRAME_MS = 50

type Aggregates = {
  messages: number
  parseTotalMs: number
  parseMaxMs: number
  handlerTotalMs: number
  handlerMaxMs: number
  bytesMax: number
  longFrames: number
  frameMaxMs: number
  pendingCount: number
  pendingTotalMs: number
  pendingMaxMs: number
  readyMs: number
}

const emptyAggregates = (): Aggregates => ({
  messages: 0, parseTotalMs: 0, parseMaxMs: 0, handlerTotalMs: 0, handlerMaxMs: 0, bytesMax: 0,
  longFrames: 0, frameMaxMs: 0, pendingCount: 0, pendingTotalMs: 0, pendingMaxMs: 0, readyMs: 0,
})

let aggregates = emptyAggregates()
let sentBatches = 0
let enabled = false
let started = false
let pendingSince = 0
let lastFrameAt = 0
let lastFlushAt = 0
let frameHandle: number | undefined
let sessionStart = 0

const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now())

const readFlag = (storage: 'localStorage' | 'sessionStorage', key: string): string | null => {
  try {
    return typeof globalThis[storage] === 'undefined' ? null : globalThis[storage].getItem(key)
  } catch {
    return null
  }
}

const writeFlag = (storage: 'localStorage' | 'sessionStorage', key: string, value: string) => {
  try {
    if (typeof globalThis[storage] !== 'undefined') globalThis[storage].setItem(key, value)
  } catch {
    // 开关写不进去就当作未抽中，静默放弃。
  }
}

export const clientPerfDisabled = () =>
  readFlag('localStorage', 'l12-perf-off') === '1' || String(import.meta.env?.VITE_L12_PERF ?? '') === 'off'

const shouldSampleSession = () => {
  const existing = readFlag('sessionStorage', 'l12-perf-sample')
  if (existing === '1') return true
  if (existing === '0') return false
  const sampled = Math.random() < 1 / CLIENT_PERF_SAMPLE_DENOMINATOR
  writeFlag('sessionStorage', 'l12-perf-sample', sampled ? '1' : '0')
  return sampled
}

const serialize = (): string | null => {
  if (aggregates.messages === 0 && aggregates.longFrames === 0 && aggregates.pendingCount === 0 && aggregates.readyMs === 0) return null
  const round = (value: number) => Math.round(value * 10) / 10
  const payload = {
    v: 1,
    msg: aggregates.messages,
    parse: [round(aggregates.parseTotalMs), round(aggregates.parseMaxMs)],
    handler: [round(aggregates.handlerTotalMs), round(aggregates.handlerMaxMs)],
    bytes: aggregates.bytesMax,
    long: aggregates.longFrames,
    frameMax: round(aggregates.frameMaxMs),
    pending: [aggregates.pendingCount, round(aggregates.pendingTotalMs), round(aggregates.pendingMaxMs)],
    ready: round(aggregates.readyMs),
    elapsed: Math.round(now() - sessionStart),
  }
  const body = JSON.stringify(payload)
  return new TextEncoder().encode(body).length <= CLIENT_PERF_MAX_BODY_BYTES ? body : null
}

const stopClientPerf = () => {
  enabled = false
  if (frameHandle !== undefined && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(frameHandle)
  frameHandle = undefined
}

export const flushClientPerf = () => {
  if (!enabled || sentBatches >= CLIENT_PERF_SESSION_CAP) return
  const body = serialize()
  if (!body) return
  try {
    if (typeof navigator !== 'undefined' && typeof navigator.sendBeacon === 'function') {
      if (!navigator.sendBeacon(CLIENT_PERF_ENDPOINT, new Blob([body], { type: 'application/json' }))) return
    } else {
      return
    }
    sentBatches += 1
  } catch {
    return
  }
  aggregates = emptyAggregates()
  lastFlushAt = now()
  if (sentBatches >= CLIENT_PERF_SESSION_CAP) stopClientPerf()
}

const maybeFlushClientPerf = () => {
  if (!enabled || sentBatches >= CLIENT_PERF_SESSION_CAP) return
  if (now() - lastFlushAt >= CLIENT_PERF_FLUSH_MS) flushClientPerf()
}

export const noteClientMessageParsed = (parseMs: number) => {
  if (!enabled) return
  aggregates.parseTotalMs += parseMs
  aggregates.parseMaxMs = Math.max(aggregates.parseMaxMs, parseMs)
}

export const noteClientMessageHandled = (startedAt: number, bytes: number) => {
  if (!enabled) return
  const total = now() - startedAt
  aggregates.messages += 1
  aggregates.handlerTotalMs += total
  aggregates.handlerMaxMs = Math.max(aggregates.handlerMaxMs, total)
  aggregates.bytesMax = Math.max(aggregates.bytesMax, bytes)
  maybeFlushClientPerf()
}

export const notePendingLockStart = () => {
  if (!enabled || pendingSince !== 0) return
  pendingSince = now()
}

export const notePendingLockEnd = () => {
  if (!enabled || pendingSince === 0) return
  const held = now() - pendingSince
  pendingSince = 0
  aggregates.pendingCount += 1
  aggregates.pendingTotalMs += held
  aggregates.pendingMaxMs = Math.max(aggregates.pendingMaxMs, held)
  maybeFlushClientPerf()
}

export const markClientReady = () => {
  if (!enabled || aggregates.readyMs !== 0) return
  aggregates.readyMs = now() - sessionStart
}

const trackFrames = () => {
  if (!enabled || typeof requestAnimationFrame !== 'function') return
  const step = (timestamp: number) => {
    if (!enabled) return
    if (lastFrameAt > 0) {
      const gap = timestamp - lastFrameAt
      if (gap > CLIENT_PERF_LONG_FRAME_MS) aggregates.longFrames += 1
      aggregates.frameMaxMs = Math.max(aggregates.frameMaxMs, gap)
    }
    lastFrameAt = timestamp
    frameHandle = requestAnimationFrame(step)
  }
  frameHandle = requestAnimationFrame(step)
}

export const initClientPerf = () => {
  if (started || typeof window === 'undefined') return
  started = true
  if (clientPerfDisabled() || !shouldSampleSession()) return
  enabled = true
  sessionStart = now()
  lastFlushAt = sessionStart
  trackFrames()
  if (typeof window.addEventListener === 'function') {
    window.addEventListener('pagehide', flushClientPerf)
    window.addEventListener('visibilitychange', () => {
      if (typeof document !== 'undefined' && document.visibilityState === 'hidden') flushClientPerf()
    })
  }
}

export const clientPerfState = () => ({ enabled, sentBatches, aggregates: { ...aggregates } })
