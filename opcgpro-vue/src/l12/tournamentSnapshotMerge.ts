export interface VersionedTournamentSnapshot {
  id: string
  version: number
}

/**
 * Applies a list snapshot without allowing a response that started before a local write to
 * roll that write back. An item absent from an authoritative snapshot is removed unless it was
 * created or advanced after that snapshot request began.
 */
export function mergeTournamentSnapshot<T extends VersionedTournamentSnapshot>(
  incoming: readonly T[], current: readonly T[], baselineVersions: ReadonlyMap<string, number>,
): T[] {
  const merged = new Map(incoming.map(item => [item.id, item]))
  for (const item of current) {
    const replacement = merged.get(item.id)
    const baselineVersion = baselineVersions.get(item.id)
    if (replacement && item.version > replacement.version) merged.set(item.id, item)
    else if (!replacement && (baselineVersion === undefined || item.version > baselineVersion))
      merged.set(item.id, item)
  }
  return [...merged.values()]
}
