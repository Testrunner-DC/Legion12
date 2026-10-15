import type { Card, PlayerView } from '../types'

/** The server supplies real Canopic membership; this projection only groups it for display. */
export function canopicRelicPresentation(player: Pick<PlayerView, 'relic' | 'extraRelics' | 'specialZones'>) {
  const presence = player.specialZones?.canopicProgress ?? []
  const canopicInstances = new Set(presence.map(card => card.instanceId))
  const primary = player.relic && !canopicInstances.has(player.relic.instanceId) ? player.relic : null
  const isisMarkers = Boolean(player.specialZones?.canopicTrack?.length)
  const seen = new Set<string>()
  const retained: Card[] = []
  for (const card of [...(player.extraRelics ?? []), ...(player.relic && !primary ? [player.relic] : []), ...presence]) {
    if (seen.has(card.instanceId) || card.instanceId === primary?.instanceId) continue
    seen.add(card.instanceId)
    if (!isisMarkers || !canopicInstances.has(card.instanceId)) retained.push(card)
  }
  return { primary, retained }
}
