// Read-model scheduling only; resource/permission invalidation is never paused.
// No shared request is cancelled and no response/identity is cached here.
export function createVisibleResourceRead(options: {
  visible: () => boolean
  invalidate: () => void
  load: () => Promise<unknown>
}) {
  let alive = true
  let dirty = false
  let queued = false
  let running = false
  type Waiter = { current: () => boolean; resolve: (fresh: boolean) => void; reject: (error: unknown) => void }
  const waiters = new Set<Waiter>()
  function retireStaleWaiters() {
    for (const waiter of waiters) if (!waiter.current()) { waiters.delete(waiter); waiter.resolve(false) }
  }
  function mayRead() { retireStaleWaiters(); return options.visible() || waiters.size > 0 }
  function enqueue() {
    retireStaleWaiters()
    if (!alive || queued || running || !dirty || !mayRead()) return
    queued = true
    void Promise.resolve().then(async () => {
      queued = false
      if (!alive || running || !dirty || !mayRead()) return
      dirty = false
      running = true
      let failed = false, failure: unknown
      try { await options.load() }
      catch (error) { failed = true; failure = error } // Automatic errors remain page-owned.
      finally {
        running = false
        retireStaleWaiters()
        if (!dirty) {
          for (const waiter of waiters) { if (failed) waiter.reject(failure); else waiter.resolve(true) }
          waiters.clear()
        }
        enqueue()
      }
    })
  }
  return {
    wake() { enqueue() },
    request() {
      if (!alive) return
      options.invalidate()
      dirty = true
      // A pending read must settle before a dirty event starts a fresh GET.
      enqueue()
    },
    // Explicit user-write readback preserves its original awaited semantics,
    // but shares the exact same pending/fresh boundary as automatic demand.
    refresh(current: () => boolean = () => true): Promise<boolean> {
      if (!alive || !current()) return Promise.resolve(false)
      return new Promise((resolve, reject) => {
        waiters.add({ current, resolve, reject })
        options.invalidate()
        dirty = true
        enqueue()
      })
    },
    dispose() {
      alive = false; dirty = false
      for (const waiter of waiters) waiter.resolve(false)
      waiters.clear()
    },
  }
}
