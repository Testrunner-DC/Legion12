import type { RecordedCommand } from './replayModel'

export interface ReplayArchivePage {
  items: RecordedCommand[]
  nextCursor?: string | null
  totalCommands: number
  pageBytes: number
}
export interface ReplayReadIssue { message: string; terminal: boolean; denied: boolean; retryAfterMs: number }
export function classifyReplayReadFailure(error: unknown): ReplayReadIssue {
  const value = error as { status?: number; code?: string; message?: string; retryAfterMs?: number }
  const denied = value?.status === 401 || value?.status === 403 || value?.code === 'stale_session'
  const transient = !denied && (error instanceof TypeError || ['network_error', 'request_timeout'].includes(value?.code ?? '')
    || [408, 425, 500, 502, 503, 504].includes(value?.status ?? -1)
    || (value?.status === 409 && value.code === 'storage_conflict'))
  const rateLimited = value?.status === 429
  return { message: value?.message || '暂时无法读取回放', terminal: !transient && !rateLimited,
    denied, retryAfterMs: rateLimited ? Math.max(1_000, value.retryAfterMs || 60_000) : 0 }
}
interface PageEntry {
  start: number; count: number; cursor?: string; nextCursor?: string
  signature: string; lastSequence: number; bytes: number; items?: RecordedCommand[]
  disasterBefore: Record<string, unknown> | null
}
interface Options {
  matchId: string
  onPage?: (commands: RecordedCommand[], start: number) => void
  onChange?: () => void
  maxCachedPages?: number
  maxCachedBytes?: number
  maxAttempts?: number
  now?: () => number
  sleep?: (milliseconds: number, signal: AbortSignal) => Promise<void>
}
const aborted = () => new DOMException('Replay read cancelled', 'AbortError')
const readTimedOut = () => Object.assign(new Error('读取回放超时，可重试继续'), { code: 'request_timeout' })
function sleep(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) return reject(aborted())
    const cancel = () => { clearTimeout(timer); reject(aborted()) }
    const timer = setTimeout(() => { signal.removeEventListener('abort', cancel); resolve() }, milliseconds)
    signal.addEventListener('abort', cancel, { once: true })
  })
}
// This is a bounded cache of already authorized archive frames, never game authority.
// Descriptors and the public index survive eviction; private raw states do not.
export class ReplayArchiveLoader {
  readonly pages: PageEntry[] = []
  total = 0
  issue: ReplayReadIssue | null = null
  loading = 0
  private disposed = false
  private focusedStep = 0
  private retryAt = 0
  private lastDisaster: Record<string, unknown> | null = null
  private readonly lifetime = new AbortController()
  private readonly pending = new Map<string, Promise<boolean>>()
  private requestedStep: number | null = null
  constructor(private readonly read: (cursor: string | undefined, signal: AbortSignal) => Promise<ReplayArchivePage>,
    private readonly options: Options) {}
  get indexedSteps() { return this.pages.reduce((sum, page) => sum + page.count, 0) }
  get cachedBytes() { return this.pages.reduce((sum, page) => sum + (page.items ? page.bytes : 0), 0) }
  get cachedPages() { return this.pages.filter(page => page.items).length }
  get hasMore() { return Boolean(this.pages.at(-1)?.nextCursor) }
  get isDisposed() { return this.disposed }
  private changed() { if (!this.disposed) this.options.onChange?.() }
  seed(page: ReplayArchivePage) { this.accept(page, undefined); this.changed() }
  focus(step: number) { this.focusedStep = step; this.evict(); this.changed() }
  private entry(step: number) { return this.pages.find(page => step >= page.start && step < page.start + page.count) }
  commandAt(step: number) { const page = this.entry(step); return page?.items?.[step - page.start] }
  window(step: number) {
    const page = this.entry(step)
    if (!page?.items) return null
    const previous = step > 0 ? this.entry(step - 1) : undefined
    if (previous && !previous.items) return null
    const first = previous && previous !== page ? previous : page
    const commands = first === page ? page.items : [...first.items!, ...page.items]
    return { commands, step: step - first.start, disasterBefore: first.disasterBefore }
  }
  async ensure(step: number, retry = false): Promise<boolean> {
    if (this.disposed || this.lifetime.signal.aborted || this.issue?.terminal || step < 0 || step >= this.total) return false
    if (retry && this.now() < this.retryAt) return false
    if (retry) this.issue = null
    this.requestedStep = step
    try {
    while (!this.entry(step) && !this.disposed) {
      const last = this.pages.at(-1)
      if (!last?.nextCursor || !await this.fetch(last.nextCursor)) return false
    }
    const page = this.entry(step)
    if (!page || (!page.items && !await this.fetch(page.cursor))) return false
    const previous = step > 0 ? this.entry(step - 1) : undefined
    if (previous && !previous.items && !await this.fetch(previous.cursor)) return false
    return !this.disposed && Boolean(this.commandAt(step))
    } finally { if (this.requestedStep === step) this.requestedStep = null }
  }
  prefetch(step: number) {
    const page = this.entry(step)
    if (this.disposed || !page?.nextCursor || this.issue) return
    const next = this.entry(page.start + page.count)
    if (!next?.items) void this.fetch(page.nextCursor)
  }
  async loadMore(retry = false) {
    if (this.disposed || this.lifetime.signal.aborted || this.issue?.terminal) return false
    if (retry && this.now() < this.retryAt) return false
    if (retry) this.issue = null
    const cursor = this.pages.at(-1)?.nextCursor
    return cursor ? this.fetch(cursor) : true
  }
  dispose() {
    if (this.disposed) return
    this.disposed = true; this.lifetime.abort()
    this.pages.length = 0; this.pending.clear(); this.issue = null; this.lastDisaster = null
  }
  private fetch(cursor?: string): Promise<boolean> {
    const key = cursor ?? '<first>'
    const existing = this.pending.get(key)
    if (existing) return existing
    if (this.disposed || this.issue?.terminal || this.issue || this.now() < this.retryAt) return Promise.resolve(false)
    // Register synchronously before calling the provider, including a synchronous throw.
    const operation = Promise.resolve().then(() => this.fetchWithRecovery(cursor))
    this.pending.set(key, operation)
    void operation.finally(() => { this.pending.delete(key) })
    return operation
  }
  private now() { return this.options.now?.() ?? Date.now() }
  private async fetchWithRecovery(cursor?: string): Promise<boolean> {
    this.loading++; this.changed()
    const deadline = this.now() + 30_000
    try {
      for (let attempt = 0; attempt < (this.options.maxAttempts ?? 3); attempt++) {
        if (this.disposed) return false
        const remaining = deadline - this.now()
        if (remaining <= 0) break
        const controller = new AbortController()
        const cancel = () => controller.abort()
        this.lifetime.signal.addEventListener('abort', cancel, { once: true })
        const timer = setTimeout(cancel, remaining)
        try {
          const page = await this.read(cursor, controller.signal)
          if (this.disposed || this.lifetime.signal.aborted) return false
          if (controller.signal.aborted || this.now() >= deadline) throw readTimedOut()
          this.accept(page, cursor)
          this.issue = null; this.retryAt = 0; this.evict(); this.changed(); return true
        } catch (reason) {
          if (this.disposed || this.lifetime.signal.aborted) return false
          const issue = classifyReplayReadFailure(controller.signal.aborted || this.now() >= deadline ? readTimedOut() : reason)
          this.issue = issue; this.changed()
          if (issue.denied) {
            this.lifetime.abort(); this.pages.length = 0; this.lastDisaster = null; this.changed(); return false
          }
          if (issue.terminal || issue.retryAfterMs) {
            this.retryAt = this.now() + issue.retryAfterMs; return false
          }
          if (attempt + 1 >= (this.options.maxAttempts ?? 3)) return false
          const delay = Math.min(500 * 2 ** attempt, 2_000)
          if (this.now() + delay >= deadline) return false
          await (this.options.sleep ?? sleep)(delay, this.lifetime.signal)
        } finally {
          clearTimeout(timer); this.lifetime.signal.removeEventListener('abort', cancel)
        }
      }
      return false
    } catch (reason) {
      if (!this.disposed) { this.issue = classifyReplayReadFailure(reason); this.changed() }
      return false
    } finally { this.loading--; this.changed() }
  }
  private accept(page: ReplayArchivePage, cursor?: string) {
    const invalid = () => { throw Object.assign(new Error('回放数据不完整，请返回对局档案重新选择'), { code: 'invalid_replay_page' }) }
    if (!Array.isArray(page.items) || page.items.length > 100 || !Number.isSafeInteger(page.totalCommands)
      || page.totalCommands < 0 || (this.pages.length && page.totalCommands !== this.total)) invalid()
    const bytes = new TextEncoder().encode(JSON.stringify(page.items)).length
    // Server rows are <=4MiB; reserve bounded JSON envelope overhead as well.
    if (bytes > 5 * 1024 * 1024 || !page.items.length) invalid()
    const existing = this.pages.find(entry => entry.cursor === cursor)
    const prior = existing ? this.pages[this.pages.indexOf(existing) - 1] : this.pages.at(-1)
    if (!existing && ((this.pages.length === 0 && cursor !== undefined) || (prior && prior.nextCursor !== cursor))) invalid()
    let lastSequence = prior?.lastSequence ?? -1
    for (const item of page.items) {
      const rawMatch = item.state?.MatchId ?? item.state?.matchId
      if (!Number.isSafeInteger(item.sequence) || item.sequence <= lastSequence || !item.state
        || (rawMatch && rawMatch !== this.options.matchId)) invalid()
      lastSequence = item.sequence
    }
    const signature = page.items.map(item => `${item.sequence}:${item.revision}:${item.stateHash}`).join('|')
    const start = existing?.start ?? this.indexedSteps
    const nextCursor = page.nextCursor || undefined
    if (nextCursor === cursor && nextCursor !== undefined) invalid()
    if ((start + page.items.length < page.totalCommands) !== Boolean(nextCursor)
      || start + page.items.length > page.totalCommands) invalid()
    if (existing) {
      if (existing.signature !== signature || existing.nextCursor !== nextCursor) invalid()
      existing.items = page.items; return
    }
    this.options.onPage?.(page.items, start)
    const entry: PageEntry = { start, count: page.items.length, cursor, nextCursor, signature, lastSequence,
      bytes, items: page.items, disasterBefore: this.lastDisaster }
    for (const command of page.items) {
      const events = command.state.Events ?? command.state.events ?? []
      for (const event of events) {
        const type = event.Type ?? event.type
        if (type === 'disaster-removed') this.lastDisaster = null
        else if (type === 'disaster-reveal') this.lastDisaster = (event.Cards ?? event.cards ?? [])[0] ?? null
      }
    }
    this.pages.push(entry); this.total = page.totalCommands
  }
  private evict() {
    const current = this.entry(this.focusedStep)
    const previous = this.entry(this.focusedStep - 1)
    const maxPages = this.options.maxCachedPages ?? 4
    const maxBytes = this.options.maxCachedBytes ?? 20 * 1024 * 1024
    const protectedEntries = new Set(this.requestedStep === null ? [] : [this.entry(this.requestedStep), this.entry(this.requestedStep - 1)])
    const candidates = this.pages.filter(page => page.items && page !== current && page !== previous && !protectedEntries.has(page))
      .sort((a, b) => Math.abs(b.start - this.focusedStep) - Math.abs(a.start - this.focusedStep))
    for (const page of candidates) {
      if (this.cachedPages <= maxPages && this.cachedBytes <= maxBytes) break
      page.items = undefined
    }
  }
}
