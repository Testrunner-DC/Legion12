export class RequestDeadlineError extends Error {
  constructor() {
    super('Request deadline exceeded')
    this.name = 'RequestDeadlineError'
  }
}

interface CoordinatedRequest<T> {
  key?: string
  method: string
  signal?: AbortSignal | null
  timeoutMs: number
  maxAttempts: number
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
  const mutations = new Map<string, Promise<unknown>>()
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

  async function runAttempt<T>(request: CoordinatedRequest<T>, attempt: number) {
    const controller = new AbortController()
    let deadlineExpired = false
    let release: (() => void) | null = null
    const onAbort = () => controller.abort()
    request.signal?.addEventListener('abort', onAbort, { once: true })
    const timeout = setTimeout(() => {
      deadlineExpired = true
      controller.abort()
    }, Math.max(1, request.timeoutMs))
    try {
      release = await acquireSlot(controller.signal)
      return await request.run(controller.signal, attempt)
    } catch (error) {
      if (deadlineExpired) throw new RequestDeadlineError()
      throw error
    } finally {
      clearTimeout(timeout)
      request.signal?.removeEventListener('abort', onAbort)
      release?.()
    }
  }

  async function runWithRetries<T>(request: CoordinatedRequest<T>) {
    const attempts = Math.max(1, Math.floor(request.maxAttempts))
    for (let attempt = 1; ; attempt += 1) {
      try {
        return await runAttempt(request, attempt)
      } catch (error) {
        if (attempt >= attempts || !request.shouldRetry(error, attempt)) throw error
        await delay(Math.max(0, request.retryDelayMs(error, attempt)), request.signal)
      }
    }
  }

  function execute<T>(request: CoordinatedRequest<T>): Promise<T> {
    const method = request.method.toUpperCase()
    const flights = method === 'GET' || method === 'HEAD' ? reads : mutations
    if (request.key) {
      const current = flights.get(request.key)
      if (current) return current as Promise<T>
    }
    const pending = runWithRetries(request)
    if (!request.key) return pending
    flights.set(request.key, pending)
    void pending.finally(() => {
      if (flights.get(request.key!) === pending) flights.delete(request.key!)
    }).catch(() => undefined)
    return pending
  }

  return {
    execute,
    snapshot: () => ({ active, queued: queue.length, reads: reads.size, mutations: mutations.size }),
  }
}
