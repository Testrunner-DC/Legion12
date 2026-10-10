import type { SeasonDefinitionDraft, SeasonDefinitionPreview } from '@/l12/platform'

function stable(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stable)
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value as Record<string, unknown>)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, item]) => [key, stable(item)]))
  }
  return value
}

export function seasonDefinitionFingerprint(draft: SeasonDefinitionDraft) {
  return JSON.stringify(stable(draft))
}

export interface FrozenSeasonDefinitionPreview {
  slot: 'current' | 'next'
  expectedRevision: number
  nextRevision: number
  operationsVersion: number
  previewToken: string
  snapshot: SeasonDefinitionDraft
  fingerprint: string
}

export function freezeSeasonDefinitionPreview(preview: SeasonDefinitionPreview): FrozenSeasonDefinitionPreview {
  const snapshot = structuredClone(preview.normalized)
  return Object.freeze({
    slot: preview.slot,
    expectedRevision: preview.currentRevision,
    nextRevision: preview.nextRevision,
    operationsVersion: preview.operationsVersion,
    previewToken: preview.previewToken,
    snapshot,
    fingerprint: seasonDefinitionFingerprint(snapshot),
  })
}

export function seasonDefinitionPreviewMatches(guard: FrozenSeasonDefinitionPreview, draft: SeasonDefinitionDraft) {
  return guard.fingerprint === seasonDefinitionFingerprint(draft)
}

export function seasonDefinitionPreviewSubmission(guard: FrozenSeasonDefinitionPreview) {
  return {
    draft: structuredClone(guard.snapshot),
    expectedRevision: guard.expectedRevision,
    expectedVersion: guard.operationsVersion,
    previewToken: guard.previewToken,
  }
}
