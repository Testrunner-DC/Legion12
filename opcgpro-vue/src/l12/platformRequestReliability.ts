export class RequestDeadlineError extends Error {
  constructor(public readonly correlationId = '', public readonly attempt = 1,
    public readonly queueWaitMs = 0,
    public readonly phase: 'network' | 'retry_budget' = 'network') {
    super('Request deadline exceeded')
    this.name = 'RequestDeadlineError'
  }
}

interface CoordinatedRequest<T> {
  key?: string
  /** Stable identity for one logical request across retries and deadline errors. */
  correlationId?: string
  method: string
  signal?: AbortSignal | null
  timeoutMs: number
  maxAttempts: number
  /** Replay timeout failures only for an explicitly idempotent command. */
  retryOnDeadline?: boolean
  run: (signal: AbortSignal, attempt: number) => Promise<T>
  shouldRetry: (error: unknown, attempt: number) => boolean
  retryDelayMs: (error: unknown, attempt: number) => number
}

interface QueueEntry {
  signal?: AbortSignal | null
  resolve: (release: () => void) => void
  reject: (error: unknown) => void
  onAbort?: () => void
}

function abortError() {
  if (typeof DOMException !== 'undefined') return new DOMException('The request was aborted', 'AbortError')
  const error = new Error('The request was aborted')
  error.name = 'AbortError'
  return error
}

function delay(milliseconds: number, signal?: AbortSignal | null) {
  if (milliseconds <= 0) return Promise.resolve()
  if (signal?.aborted) return Promise.reject(abortError())
  return new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, milliseconds)
    const onAbort = () => {
      clearTimeout(timer)
      signal?.removeEventListener('abort', onAbort)
      reject(abortError())
    }
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

export function createRequestCoordinator(maximumConcurrentRequests: number) {
  const maximum = Math.max(1, Math.floor(maximumConcurrentRequests))
  const reads = new Map<string, Promise<unknown>>()
  const queue: QueueEntry[] = []
  let active = 0

  function releaseSlot() {
    active = Math.max(0, active - 1)
    while (queue.length > 0) {
      const next = queue.shift()!
      next.signal?.removeEventListener('abort', next.onAbort!)
      if (next.signal?.aborted) {
        next.reject(abortError())
        continue
      }
      active += 1
      next.resolve(releaseSlot)
      break
    }
  }

  function acquireSlot(signal?: AbortSignal | null) {
    if (signal?.aborted) return Promise.reject(abortError())
    if (active < maximum) {
      active += 1
      return Promise.resolve(releaseSlot)
    }
    return new Promise<() => void>((resolve, reject) => {
      const entry: QueueEntry = { signal, resolve, reject }
      entry.onAbort = () => {
        const index = queue.indexOf(entry)
        if (index >= 0) queue.splice(index, 1)
        reject(abortError())
      }
      signal?.addEventListener('abort', entry.onAbort, { once: true })
      queue.push(entry)
    })
  }

  async function runWithRetryableDeadlines<T>(request: CoordinatedRequest<T>) {
    const requestController = new AbortController()
    let attemptController: AbortController | null = null
    let queueWaitMs = 0
    const onAbort = () => {
      requestController.abort()
      attemptController?.abort()
    }
    if (request.signal?.aborted) requestController.abort()
    else request.signal?.addEventListener('abort', onAbort, { once: true })
    try {
      const attempts = Math.max(1, Math.floor(request.maxAttempts))
      for (let attempt = 1; ; attempt += 1) {
        let release: (() => void) | null = null
        let timeout: ReturnType<typeof setTimeout> | undefined
        let attemptTimedOut = false
        let relayAbort: (() => void) | undefined
        try {
          const queuedAt = Date.now()
          release = await acquireSlot(requestController.signal)
          queueWaitMs += Math.max(0, Date.now() - queuedAt)
          attemptController = new AbortController()
          relayAbort = () => attemptController?.abort()
          requestController.signal.addEventListener('abort', relayAbort, { once: true })
          timeout = setTimeout(() => {
            attemptTimedOut = true
            attemptController?.abort()
          }, Math.max(1, request.timeoutMs))
          try {
            return await request.run(attemptController.signal, attempt)
          } catch (error) {
            if (attemptTimedOut) {
              if (attempt >= attempts)
                throw new RequestDeadlineError(request.correlationId, attempt, queueWaitMs,
                  attempt >= attempts ? 'retry_budget' : 'network')
              await delay(Math.max(0, request.retryDelayMs(
                new RequestDeadlineError(request.correlationId, attempt, queueWaitMs,
                  attempt >= attempts ? 'retry_budget' : 'network'), attempt)), requestController.signal)
              continue
            }
            if (requestController.signal.aborted || attempt >= attempts || !request.shouldRetry(error, attempt))
              throw error
            await delay(Math.max(0, request.retryDelayMs(error, attempt)), requestController.signal)
          }
        } finally {
          if (timeout) clearTimeout(timeout)
          if (relayAbort) requestController.signal.removeEventListener('abort', relayAbort)
          attemptController = null
          release?.()
        }
      }
    } finally {
      request.signal?.removeEventListener('abort', onAbort)
    }
  }

  async function runWithRetries<T>(request: CoordinatedRequest<T>) {
    if (request.retryOnDeadline) return runWithRetryableDeadlines(request)
    const controller = new AbortController()
    let deadlineExpired = false
    let timeout: ReturnType<typeof setTimeout> | undefined
    let queueWaitMs = 0
    let lastAttempt = 1
    const onAbort = () => controller.abort()
    if (request.signal?.aborted) controller.abort()
    else request.signal?.addEventListener('abort', onAbort, { once: true })
    try {
      const attempts = Math.max(1, Math.floor(request.maxAttempts))
      for (let attempt = 1; ; attempt += 1) {
        lastAttempt = attempt
        let release: (() => void) | null = null
        try {
          const queuedAt = Date.now()
          release = await acquireSlot(controller.signal)
          queueWaitMs += Math.max(0, Date.now() - queuedAt)
          // Queue wait is scheduler back-pressure, not network execution. Starting
          // the deadline before acquireSlot made a burst of >4 reads time out while
          // still queued, then all surfaced as the generic retry message. The
          // execution/retry budget starts once this request owns a slot; retries
          // still share the same budget because the timer is only created once.
          if (!timeout) {
            timeout = setTimeout(() => {
              deadlineExpired = true
              controller.abort()
            }, Math.max(1, request.timeoutMs))
          }
          return await request.run(controller.signal, attempt)
        } catch (error) {
          if (deadlineExpired) throw new RequestDeadlineError(request.correlationId, attempt, queueWaitMs,
            'retry_budget')
          if (controller.signal.aborted || attempt >= attempts || !request.shouldRetry(error, attempt)) throw error
          await delay(Math.max(0, request.retryDelayMs(error, attempt)), controller.signal)
        } finally {
          release?.()
        }
      }
    } catch (error) {
      if (deadlineExpired) throw new RequestDeadlineError(request.correlationId, lastAttempt, queueWaitMs,
        'retry_budget')
      throw error
    } finally {
      clearTimeout(timeout)
      request.signal?.removeEventListener('abort', onAbort)
    }
  }

  function execute<T>(request: CoordinatedRequest<T>): Promise<T> {
    const method = request.method.toUpperCase()
    const safeRead = method === 'GET' || method === 'HEAD'
    if (safeRead && request.key) {
      const current = reads.get(request.key)
      if (current) return current as Promise<T>
    }
    const pending = runWithRetries(request)
    // 写操作即使内容完全相同也可能代表用户的两次真实意图，默认绝不合并。
    if (!safeRead || !request.key) return pending
    reads.set(request.key, pending)
    void pending.finally(() => {
      if (reads.get(request.key!) === pending) reads.delete(request.key!)
    }).catch(() => undefined)
    return pending
  }

  return {
    execute,
    snapshot: () => ({ active, queued: queue.length, reads: reads.size, mutations: 0 }),
  }
}
