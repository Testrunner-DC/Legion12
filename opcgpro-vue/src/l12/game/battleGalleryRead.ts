import type { AlternateArt } from '../platform'

type DetailIdentity = { cardId: string; hidden?: boolean }

// The mobile detail drawer is independent of the folded desktop inspector.
// Hidden cards never create public metadata demand.
export function battleGalleryDetailDemand<T extends DetailIdentity>(mobile: boolean, opened: boolean,
  focused: T | null, inspector: T | null): T | null {
  const card = mobile ? (opened ? focused : null) : inspector
  return card && !card.hidden && card.cardId ? card : null
}

// Per-board public display data only, not an entitlement/authorization cache.
// Do not cancel a shared GET on retirement; only reject its late result here.
export function createBattleGalleryReader(load: () => Promise<AlternateArt[]>, accept: (arts: AlternateArt[]) => void) {
  let alive = true
  let loaded = false
  let inFlight: Promise<boolean> | null = null
  return {
    read(): Promise<boolean> {
      if (!alive) return Promise.resolve(false)
      if (loaded) return Promise.resolve(true)
      if (inFlight) return inFlight
      const request = Promise.resolve().then(() => alive ? load() : null).then(arts => {
        if (!alive || arts === null) return false
        accept(arts)
        loaded = true
        return true
      }).catch(() => false).finally(() => {
        if (inFlight === request) inFlight = null
      })
      inFlight = request
      return request
    },
    dispose() { alive = false },
  }
}
