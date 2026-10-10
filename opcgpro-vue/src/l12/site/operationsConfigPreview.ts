import type {
  OperationsConfigPayload,
  OperationsConfigPreview,
  OperationsConfigSection,
  OperationsSectionPayload,
  OperationsSectionPreview,
} from '@/l12/platform'

export interface FrozenOperationsConfigPreview {
  snapshot: OperationsConfigPayload
  fingerprint: string
  currentVersion: number
  nextVersion: number
}

export interface FrozenOperationsSectionPreview {
  section: OperationsConfigSection
  snapshot: OperationsSectionPayload
  fingerprint: string
  currentRevision: number
  nextRevision: number
  expectedFieldRevisions: Readonly<Record<string, number>>
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

export function operationsSectionSnapshot(payload: OperationsConfigPayload,
  section: OperationsConfigSection): OperationsSectionPayload {
  const normalized = normalizeOperationsConfigSnapshot(payload)
  switch (section) {
    case 'room': return {
      defaultRoomConfig: structuredClone(normalized.defaultRoomConfig),
      matchModes: structuredClone(normalized.matchModes),
    }
    case 'features': return { featureFlags: structuredClone(normalized.featureFlags) }
    case 'announcements': return { announcements: structuredClone(normalized.announcements ?? []) }
    case 'maintenance': return { maintenance: structuredClone(normalized.maintenance) }
  }
}

export function normalizeOperationsSectionSnapshot(section: OperationsConfigSection,
  payload: OperationsSectionPayload): OperationsSectionPayload {
  switch (section) {
    case 'room': return {
      defaultRoomConfig: payload.defaultRoomConfig ? { ...payload.defaultRoomConfig } : undefined,
      matchModes: (payload.matchModes ?? []).map(item => ({ ...item }))
        .sort((left, right) => compareText(left.id.toLocaleLowerCase('en-US'), right.id.toLocaleLowerCase('en-US'))),
    }
    case 'features': return {
      featureFlags: Object.fromEntries(Object.entries(payload.featureFlags ?? {})
        .sort(([left], [right]) => compareText(left.toLocaleLowerCase('en-US'), right.toLocaleLowerCase('en-US')))),
    }
    case 'announcements': return {
      announcements: (payload.announcements ?? []).map(item => ({
        ...item,
        startsAt: canonicalDate(item.startsAt),
        endsAt: canonicalDate(item.endsAt),
      })).sort((left, right) => left.sortOrder - right.sortOrder
        || compareText(left.id.toLocaleLowerCase('en-US'), right.id.toLocaleLowerCase('en-US'))),
    }
    case 'maintenance': return {
      maintenance: payload.maintenance ? {
        ...payload.maintenance,
        startsAt: canonicalDate(payload.maintenance.startsAt),
        endsAt: canonicalDate(payload.maintenance.endsAt),
      } : undefined,
    }
  }
}

function equalValue(left: unknown, right: unknown) {
  return JSON.stringify(stableValue(left)) === JSON.stringify(stableValue(right))
}

function keyedById<T extends { id: string }>(items: T[]) {
  return new Map(items.map(item => [item.id.toLocaleLowerCase('en-US'), item] as const))
}

function keyedFlags(flags: Record<string, boolean>) {
  return new Map(Object.entries(flags).map(([key, value]) =>
    [key.toLocaleLowerCase('en-US'), { key, value }] as const))
}

export function operationsSectionChangedFields(section: OperationsConfigSection,
  baseline: OperationsConfigPayload, current: OperationsConfigPayload) {
  const before = normalizeOperationsConfigSnapshot(baseline)
  const after = normalizeOperationsConfigSnapshot(current)
  const fields: string[] = []
  if (section === 'room') {
    const keys = ['matchModeId', 'spectating', 'handVisibility', 'disasterMode'] as const
    for (const key of keys) {
      if (before.defaultRoomConfig[key] !== after.defaultRoomConfig[key])
        fields.push(`room/defaultRoomConfig/${key}`)
    }
    const beforeModes = keyedById(before.matchModes)
    const afterModes = keyedById(after.matchModes)
    for (const id of new Set([...beforeModes.keys(), ...afterModes.keys()])) {
      if (!equalValue(beforeModes.get(id), afterModes.get(id)))
        fields.push(`room/matchModes/${afterModes.get(id)?.id ?? beforeModes.get(id)!.id}`)
    }
  } else if (section === 'features') {
    const beforeFlags = keyedFlags(before.featureFlags)
    const afterFlags = keyedFlags(after.featureFlags)
    for (const id of new Set([...beforeFlags.keys(), ...afterFlags.keys()])) {
      if (!equalValue(beforeFlags.get(id)?.value, afterFlags.get(id)?.value))
        fields.push(`features/featureFlags/${afterFlags.get(id)?.key ?? beforeFlags.get(id)!.key}`)
    }
  } else if (section === 'announcements') {
    const beforeItems = keyedById(before.announcements ?? [])
    const afterItems = keyedById(after.announcements ?? [])
    for (const id of new Set([...beforeItems.keys(), ...afterItems.keys()])) {
      if (!equalValue(beforeItems.get(id), afterItems.get(id)))
        fields.push(`announcements/items/${afterItems.get(id)?.id ?? beforeItems.get(id)!.id}`)
    }
  } else {
    const keys = ['enabled', 'message', 'startsAt', 'endsAt',
      'advanceBroadcastHours', 'expectedDurationHours'] as const
    for (const key of keys) {
      if (!equalValue(before.maintenance[key], after.maintenance[key]))
        fields.push(`maintenance/${key}`)
    }
  }
  return fields.sort(compareText)
}

function fieldRevision(revisions: Record<string, number>, field: string) {
  const exact = Object.entries(revisions).find(([key]) =>
    key.toLocaleLowerCase('en-US') === field.toLocaleLowerCase('en-US'))
  return exact?.[1] ?? 0
}

export function operationsSectionPreviewRequest(section: OperationsConfigSection,
  baseline: OperationsConfigPayload, current: OperationsConfigPayload,
  expectedRevision: number, fieldRevisions: Record<string, number>) {
  const changes = operationsSectionChangedFields(section, baseline, current)
  return {
    config: operationsSectionSnapshot(current, section),
    expectedRevision,
    expectedFieldRevisions: Object.fromEntries(changes.map(field =>
      [field, fieldRevision(fieldRevisions, field)])),
    changes,
  }
}

export function operationsSectionFingerprint(section: OperationsConfigSection,
  payload: OperationsSectionPayload) {
  return JSON.stringify(stableValue(normalizeOperationsSectionSnapshot(section, payload)))
}

export function freezeOperationsSectionPreview(preview: OperationsSectionPreview): FrozenOperationsSectionPreview {
  const snapshot = freezeDeep(normalizeOperationsSectionSnapshot(preview.section, preview.normalized))
  return freezeDeep({
    section: preview.section,
    snapshot,
    fingerprint: operationsSectionFingerprint(preview.section, snapshot),
    currentRevision: preview.currentRevision,
    nextRevision: preview.nextRevision,
    expectedFieldRevisions: { ...preview.expectedFieldRevisions },
  })
}

export function operationsSectionPreviewMatches(guard: FrozenOperationsSectionPreview,
  current: OperationsConfigPayload) {
  return guard.fingerprint === operationsSectionFingerprint(guard.section,
    operationsSectionSnapshot(current, guard.section))
}

export function operationsSectionPreviewSubmission(guard: FrozenOperationsSectionPreview) {
  return {
    config: structuredClone(guard.snapshot),
    expectedRevision: guard.currentRevision,
    expectedFieldRevisions: structuredClone(guard.expectedFieldRevisions) as Record<string, number>,
  }
}

export function mergeOperationsSection(payload: OperationsConfigPayload,
  section: OperationsConfigSection, sectionPayload: OperationsSectionPayload): OperationsConfigPayload {
  const merged = JSON.parse(JSON.stringify(payload)) as OperationsConfigPayload
  if (section === 'room') {
    if (sectionPayload.defaultRoomConfig) merged.defaultRoomConfig = structuredClone(sectionPayload.defaultRoomConfig)
    if (sectionPayload.matchModes) merged.matchModes = structuredClone(sectionPayload.matchModes)
  } else if (section === 'features') {
    if (sectionPayload.featureFlags) merged.featureFlags = structuredClone(sectionPayload.featureFlags)
  } else if (section === 'announcements') {
    if (sectionPayload.announcements) merged.announcements = structuredClone(sectionPayload.announcements)
  } else if (sectionPayload.maintenance) {
    merged.maintenance = structuredClone(sectionPayload.maintenance)
  }
  return merged
}
