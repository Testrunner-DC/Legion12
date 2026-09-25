export type PresentationReservation = {
  waitUntilGranted: () => Promise<() => void>
  setPaused: (paused: boolean) => void
  cancel: () => void
}

type PendingPresentation = {
  sequence: number
  priority: number
  order: number
  ready: boolean
  paused: boolean
  cancelled: boolean
  resolve: (release: () => void) => void
}

export type PresentationSequenceCoordinator = ReturnType<typeof createPresentationSequenceCoordinator>

export function createPresentationSequenceCoordinator(onBusyChange?: (busy: boolean) => void) {
  const pending = new Set<PendingPresentation>()
  let active: PendingPresentation | null = null
  let nextOrder = 0
  let scheduled = false
  let lastBusy = false

  const notifyBusy = () => {
    const busy = Boolean(active || pending.size)
    if (busy === lastBusy) return
    lastBusy = busy
    onBusyChange?.(busy)
  }

  const pump = () => {
    scheduled = false
    if (active) return
    const next = [...pending]
      .filter(item => !item.cancelled)
      .sort((left, right) => left.sequence - right.sequence || left.priority - right.priority || left.order - right.order)[0]
    // A lower authoritative sequence is a reservation, not merely a ready
    // animation.  It intentionally blocks later work while its image/DOM
    // anchors are being prepared or while a modal is covering the board.
    if (!next || !next.ready || next.paused) return
    pending.delete(next)
    active = next
    let released = false
    next.resolve(() => {
      if (released) return
      released = true
      if (active === next) active = null
      notifyBusy()
      schedulePump()
    })
    notifyBusy()
  }

  const schedulePump = () => {
    if (scheduled) return
    scheduled = true
    // Let every watcher reserve the current authoritative batch before the
    // first animation starts.  This removes component mount/watch ordering
    // from the visible event order.
    setTimeout(pump, 0)
  }

  const reserve = (sequence: number, priority = 50): PresentationReservation => {
    let resolveGrant!: (release: () => void) => void
    const granted = new Promise<() => void>(resolve => { resolveGrant = resolve })
    const item: PendingPresentation = {
      sequence,
      priority,
      order: nextOrder++,
      ready: false,
      paused: false,
      cancelled: false,
      resolve: resolveGrant,
    }
    pending.add(item)
    notifyBusy()
    schedulePump()
    return {
      waitUntilGranted: () => {
        item.ready = true
        schedulePump()
        return granted
      },
      setPaused: paused => {
        item.paused = paused
        schedulePump()
      },
      cancel: () => {
        if (item.cancelled) return
        item.cancelled = true
        pending.delete(item)
        // An already granted presentation owns its release callback; callers
        // cancel the visual and release it through that normal path.
        notifyBusy()
        schedulePump()
      },
    }
  }

  const reset = () => {
    for (const item of pending) item.cancelled = true
    pending.clear()
    active = null
    notifyBusy()
  }

  return { reserve, reset }
}
