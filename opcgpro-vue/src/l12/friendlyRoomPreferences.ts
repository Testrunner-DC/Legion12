import type { RoomOptions } from './net'

export type FriendlyDisasterMode = RoomOptions['disasterMode']

export function isFriendlyDisasterMode(value: unknown): value is FriendlyDisasterMode {
  return value === 'all' || value === 'random' || value === 'season' || value === 'none'
}

function preferenceKey(accountId?: string | null) {
  return `l12-friendly-disaster-mode-v1:${accountId ? `account:${encodeURIComponent(accountId)}` : 'guest'}`
}

export function loadFriendlyDisasterMode(accountId?: string | null): FriendlyDisasterMode | undefined {
  try {
    const value = localStorage.getItem(preferenceKey(accountId))
    return isFriendlyDisasterMode(value) ? value : undefined
  } catch { return undefined }
}

// This remembers a local control choice, not a successful room operation.
export function saveFriendlyDisasterMode(accountId: string | null | undefined, value: unknown): boolean {
  if (!isFriendlyDisasterMode(value)) return false
  try {
    localStorage.setItem(preferenceKey(accountId), value)
    return true
  } catch { return false }
}
