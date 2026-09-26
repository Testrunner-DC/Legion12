import type { OperationsConfigPayload, OperationsConfigPreview } from '@/l12/platform'

export interface FrozenOperationsConfigPreview {
  snapshot: OperationsConfigPayload
  fingerprint: string
  currentVersion: number
  nextVersion: number
}

function compareText(left: string, right: string) {
  return left < right ? -1 : left > right ? 1 : 0
}

function canonicalDate(value: string | null | undefined) {
  if (value == null || value.trim() === '') return undefined
  const instant = new Date(value)
  return Number.isNaN(instant.getTime()) ? value.trim() : instant.toISOString()
}

function optionalText(value: string | null | undefined) {
  return value == null || value === '' ? undefined : value
}

/**
 * Produces the client representation of the server's normalized operations payload.
 * Optional null/undefined values and equivalent ISO offsets intentionally collapse to
 * one form. Arrays whose order is normalized by the server use that same order here.
 */
export function normalizeOperationsConfigSnapshot(payload: OperationsConfigPayload): OperationsConfigPayload {
  const featureFlags = Object.fromEntries(Object.entries(payload.featureFlags)
    .sort(([left], [right]) => compareText(left.toLocaleLowerCase('en-US'), right.toLocaleLowerCase('en-US'))))
  return {
    season: {
      id: payload.season.id,
      name: payload.season.name,
      status: payload.season.status,
      startsAt: canonicalDate(payload.season.startsAt),
      endsAt: canonicalDate(payload.season.endsAt),
    },
    disasterPool: {
      cardIds: [...payload.disasterPool.cardIds],
      annihilationLocked: payload.disasterPool.annihilationLocked,
    },
    cardRestrictions: payload.cardRestrictions.map(item => ({
      cardId: item.cardId,
      maxCopies: item.maxCopies,
      reason: optionalText(item.reason),
      masterId: optionalText(item.masterId),
    })).sort((left, right) => compareText(left.cardId.toLocaleLowerCase('en-US'), right.cardId.toLocaleLowerCase('en-US'))),
    defaultPresetDeckIds: [...payload.defaultPresetDeckIds],
    matchModes: payload.matchModes.map(item => ({ ...item }))
      .sort((left, right) => compareText(left.id.toLocaleLowerCase('en-US'), right.id.toLocaleLowerCase('en-US'))),
    defaultRoomConfig: { ...payload.defaultRoomConfig },
    featureFlags,
    maintenance: {
      enabled: payload.maintenance.enabled,
      message: payload.maintenance.message,
      startsAt: canonicalDate(payload.maintenance.startsAt),
      endsAt: canonicalDate(payload.maintenance.endsAt),
      advanceBroadcastHours: payload.maintenance.advanceBroadcastHours,
      expectedDurationHours: payload.maintenance.expectedDurationHours,
    },
    announcements: (payload.announcements ?? []).map(item => ({
      id: item.id,
      content: item.content,
      enabled: item.enabled,
      sortOrder: item.sortOrder,
      startsAt: canonicalDate(item.startsAt),
      endsAt: canonicalDate(item.endsAt),
    })).sort((left, right) => left.sortOrder - right.sortOrder
      || compareText(left.id.toLocaleLowerCase('en-US'), right.id.toLocaleLowerCase('en-US'))),
  }
}

function stableValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stableValue)
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value)
    .filter(([, item]) => item !== undefined)
    .sort(([left], [right]) => compareText(left, right))
    .map(([key, item]) => [key, stableValue(item)]))
  return value
}

export function operationsConfigFingerprint(payload: OperationsConfigPayload) {
  return JSON.stringify(stableValue(normalizeOperationsConfigSnapshot(payload)))
}

function freezeDeep<T>(value: T): T {
  if (value && typeof value === 'object' && !Object.isFrozen(value)) {
    for (const item of Object.values(value)) freezeDeep(item)
    Object.freeze(value)
  }
  return value
}

export function freezeOperationsConfigPreview(preview: OperationsConfigPreview): FrozenOperationsConfigPreview {
  const snapshot = freezeDeep(normalizeOperationsConfigSnapshot(preview.normalized))
  return freezeDeep({
    snapshot,
    fingerprint: operationsConfigFingerprint(snapshot),
    currentVersion: preview.currentVersion,
    nextVersion: preview.nextVersion,
  })
}

export function operationsConfigPreviewMatches(guard: FrozenOperationsConfigPreview, current: OperationsConfigPayload) {
  return guard.fingerprint === operationsConfigFingerprint(current)
}

export function operationsConfigPreviewSubmission(guard: FrozenOperationsConfigPreview) {
  return {
    config: structuredClone(guard.snapshot),
    expectedVersion: guard.currentVersion,
  }
}
