export type PresentationReservation = {
  waitUntilGranted: () => Promise<(presented?: boolean) => void>
  setPaused: (paused: boolean) => void
  setMetadata: (metadata: PresentationReservationMetadata) => void
  cancel: () => void
  prepareVisualOwner: (owner: PreparedVisualOwner) => void
}

export type PresentationReservationRole = 'entry-movement' | 'entry-effect'

export type PresentationReservationQuery = {
  role?: PresentationReservationRole
  sourceInstanceId?: string
  transactionKey?: string
}

export type PresentationReservationOptions = {
  visualKey?: string
  role?: PresentationReservationRole
  sourceInstanceId?: string
  transactionKey?: string
}

export type PresentationReservationMetadata = Pick<PresentationReservationOptions,
  'role' | 'sourceInstanceId' | 'transactionKey'>

export type PreparedVisualOwner = {
  show: () => void
  hide: () => void
  dispose: () => void
}

type PendingPresentation = {
  sequence: number
  priority: number
  order: number
  ready: boolean
  paused: boolean
  cancelled: boolean
  released: boolean
  visualKey?: string
  role?: PresentationReservationRole
  sourceInstanceId?: string
  transactionKey?: string
  visualOwner?: PreparedVisualOwner
  resolve: (release: (presented?: boolean) => void) => void
}

export type PresentationSequenceCoordinator = ReturnType<typeof createPresentationSequenceCoordinator>

export function createPresentationSequenceCoordinator(
  onBusyChange?: (busy: boolean) => void,
  onVisualOwnershipChange?: (visualKey: string, owned: boolean) => void,
  onReservationChange?: () => void,
) {
  const pending = new Set<PendingPresentation>()
  const entryMovementTransactions = new Set<string>()
  const presentationFacts = new Set<number>()
  const activeVisualOwners = new Map<string, { item: PendingPresentation; owner: PreparedVisualOwner }>()
  let active: PendingPresentation | null = null
  let nextOrder = 0
  let scheduled = false
  let lastBusy = false

  const comparePresentations = (left: PendingPresentation, right: PendingPresentation) => {
    return left.sequence - right.sequence || left.priority - right.priority || left.order - right.order
  }

  // Keep a stable total order. A pair-only role comparator would form cycles
  // with unrelated priorities (entry20 < effect10 < unrelated15 < entry20).
  // An entry effect is simply ineligible while its exact predecessor exists;
  // this derives from the original reservation set, not a second queue.
  const blockedByEntry = (item: PendingPresentation) => item.role === 'entry-effect'
    && [...pending, ...(active ? [active] : [])].some(candidate => candidate !== item
      && !candidate.cancelled && !candidate.released && candidate.role === 'entry-movement'
      && candidate.sequence === item.sequence && candidate.sourceInstanceId === item.sourceInstanceId
      && candidate.transactionKey === item.transactionKey)

  const orderedForVisualKey = (visualKey: string) => [...pending, ...(active ? [active] : [])]
    .filter(item => !item.cancelled && !item.released && item.visualKey === visualKey && !blockedByEntry(item))
    .sort(comparePresentations)

  const matchesReservation = (item: PendingPresentation, query: PresentationReservationQuery) =>
    !item.cancelled && !item.released
    && (!query.role || item.role === query.role)
    && (!query.sourceInstanceId || item.sourceInstanceId === query.sourceInstanceId)
    && (!query.transactionKey || item.transactionKey === query.transactionKey)

  const reservationState = (query: PresentationReservationQuery): 'pending' | 'active' | null => {
    if (active && matchesReservation(active, query)) return 'active'
    return [...pending].some(item => matchesReservation(item, query)) ? 'pending' : null
  }

  const handoffVisualOwner = (item: PendingPresentation, disposePrevious: boolean) => {
    const visualKey = item.visualKey
    const owner = item.visualOwner
    if (!visualKey || !owner) return
    const previous = activeVisualOwners.get(visualKey)
    if (previous?.item === item) {
      owner.show()
      return
    }
    // The successor acquires its lease and becomes visible first. The previous
    // owner then releases/removes in the same stack, so no paint can observe a
    // blank or the final authority between two ordered actions.
    owner.show()
    if (previous) {
      if (disposePrevious) previous.owner.dispose()
      else previous.owner.hide()
    }
    activeVisualOwners.set(visualKey, { item, owner })
    if (!previous) onVisualOwnershipChange?.(visualKey, true)
  }

  const settleReleasedVisualOwner = (visualKey: string) => {
    const current = activeVisualOwners.get(visualKey)
    if (!current?.item.released) return
    const successor = orderedForVisualKey(visualKey).find(candidate => candidate !== current.item)
    // Only the actual next reservation may inherit the pixels. Skipping an
    // unprepared predecessor would reorder two real authority actions merely
    // because their image/DOM preparation finished differently. Keep the
    // completed final holder until that successor can take the lease.
    if (successor?.visualOwner) handoffVisualOwner(successor, true)
    else if (!successor) {
      activeVisualOwners.delete(visualKey)
      onVisualOwnershipChange?.(visualKey, false)
      current.owner.dispose()
    }
  }

  const releaseVisualOwner = (item: PendingPresentation) => {
    item.released = true
    const visualKey = item.visualKey
    const owner = item.visualOwner
    if (!visualKey) return
    const current = activeVisualOwners.get(visualKey)
    if (current?.item !== item) owner?.dispose()
    settleReleasedVisualOwner(visualKey)
  }

  const prepareVisualOwner = (item: PendingPresentation, owner: PreparedVisualOwner) => {
    if (item.visualOwner === owner) {
      const visualKey = item.visualKey
      const current = visualKey ? activeVisualOwners.get(visualKey) : undefined
      if (current?.item === item) owner.show()
      else if (visualKey && (!current || current.item.released)
        && orderedForVisualKey(visualKey)[0] === item) handoffVisualOwner(item, Boolean(current))
      return
    }
    item.visualOwner?.dispose()
    item.visualOwner = owner
    if (item.cancelled || !item.visualKey) {
      owner.dispose()
      return
    }
    const current = activeVisualOwners.get(item.visualKey)
    if (current) {
      if (current.item.released && orderedForVisualKey(item.visualKey)[0] === item)
        handoffVisualOwner(item, true)
      else owner.hide()
      return
    }
    if (orderedForVisualKey(item.visualKey)[0] === item) handoffVisualOwner(item, false)
    else owner.hide()
  }

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
      .filter(item => !item.cancelled && !blockedByEntry(item))
      .sort(comparePresentations)[0]
    // A lower authoritative sequence is a reservation, not merely a ready
    // animation.  It intentionally blocks later work while its image/DOM
    // anchors are being prepared or while a modal is covering the board.
    if (!next || !next.ready || next.paused) return
    pending.delete(next)
    active = next
    onReservationChange?.()
    if (next.visualOwner) handoffVisualOwner(next, false)
    let released = false
    next.resolve((_presented = true) => {
      if (released) return
      released = true
      releaseVisualOwner(next)
      if (active === next) active = null
      onReservationChange?.()
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

  const reserve = (sequence: number, priority = 50,
    options: PresentationReservationOptions = {}): PresentationReservation => {
    let resolveGrant!: (release: (presented?: boolean) => void) => void
    const granted = new Promise<(presented?: boolean) => void>(resolve => { resolveGrant = resolve })
    const item: PendingPresentation = {
      sequence,
      priority,
      order: nextOrder++,
      ready: false,
      paused: false,
      cancelled: false,
      released: false,
      visualKey: options.visualKey,
      role: options.role,
      sourceInstanceId: options.sourceInstanceId?.trim() || undefined,
      transactionKey: options.transactionKey?.trim() || undefined,
      resolve: resolveGrant,
    }
    if (!item.sourceInstanceId || !item.transactionKey) item.role = undefined
    pending.add(item)
    onReservationChange?.()
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
        if (item.visualKey && item.visualOwner) {
          const current = activeVisualOwners.get(item.visualKey)
          // A paused head keeps a static, pointer-transparent holder over the
          // authoritative card. The real slot stays hit-testable underneath,
          // while the final class cannot appear and then visibly rewind when
          // the prompt releases.
          if (current?.item === item) item.visualOwner.show()
          else if ((!current || current.item.released)
            && orderedForVisualKey(item.visualKey)[0] === item)
            handoffVisualOwner(item, Boolean(current))
        }
        schedulePump()
      },
      setMetadata: metadata => {
        if (item.cancelled || item.released) return
        item.role = metadata.role
        item.sourceInstanceId = metadata.sourceInstanceId?.trim() || undefined
        item.transactionKey = metadata.transactionKey?.trim() || undefined
        if (!item.sourceInstanceId || !item.transactionKey) item.role = undefined
        onReservationChange?.()
        schedulePump()
      },
      cancel: () => {
        if (item.cancelled) return
        item.cancelled = true
        pending.delete(item)
        releaseVisualOwner(item)
        onReservationChange?.()
        // An already granted presentation owns its release callback; callers
        // cancel the visual and release it through that normal path.
        notifyBusy()
        schedulePump()
      },
      prepareVisualOwner: owner => prepareVisualOwner(item, owner),
    }
  }

  const registerEntryMovementTransaction = (transactionKey: string) => {
    entryMovementTransactions.add(transactionKey)
  }

  const ownsEntryMovementTransaction = (transactionKey: string) => entryMovementTransactions.has(transactionKey)

  const clearEntryMovementTransactions = () => entryMovementTransactions.clear()

  const registerPresentationFact = (sequence: number) => {
    if (Number.isSafeInteger(sequence) && sequence > 0) presentationFacts.add(sequence)
  }

  const ownsPresentationFact = (sequence: number) => presentationFacts.has(sequence)

  const ownsVisualKey = (visualKey: string) => activeVisualOwners.has(visualKey)

  const clearPresentationFacts = () => presentationFacts.clear()

  const reset = () => {
    const owners = new Set<PreparedVisualOwner>()
    for (const item of pending) if (item.visualOwner) owners.add(item.visualOwner)
    if (active?.visualOwner) owners.add(active.visualOwner)
    for (const { owner } of activeVisualOwners.values()) owners.add(owner)
    const visualKeys = [...activeVisualOwners.keys()]
    for (const item of pending) item.cancelled = true
    pending.clear()
    entryMovementTransactions.clear()
    presentationFacts.clear()
    for (const owner of owners) owner.dispose()
    activeVisualOwners.clear()
    for (const visualKey of visualKeys) onVisualOwnershipChange?.(visualKey, false)
    active = null
    onReservationChange?.()
    notifyBusy()
  }

  return {
    reserve,
    reservationState,
    registerEntryMovementTransaction,
    ownsEntryMovementTransaction,
    clearEntryMovementTransactions,
    registerPresentationFact,
    ownsPresentationFact,
    ownsVisualKey,
    clearPresentationFacts,
    reset,
  }
}
