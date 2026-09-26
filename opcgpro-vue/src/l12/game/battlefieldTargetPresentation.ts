import type { Card, GameState } from '../types'

export type BattlefieldRelativeSide = 'self' | 'opponent'

export interface BattlefieldTargetLocation {
  card: Card
  playerIndex: number
  row: number
  slot: number
  relativeSide: BattlefieldRelativeSide
  label: string
}

export function battlefieldSlotLabel(side: BattlefieldRelativeSide, row: number, slot: number) {
  return `${side === 'self' ? '我方' : '对方'}${row === 0 ? '前排' : '后排'}第${slot + 1}格`
}

export function findBattlefieldTarget(game: Pick<GameState, 'players'>, viewerPlayerIndex: number, instanceId: string) {
  for (const player of game.players) {
    for (let row = 0; row < player.field.length; row += 1) {
      for (let slot = 0; slot < player.field[row].length; slot += 1) {
        const card = player.field[row][slot]
        if (!card || card.instanceId !== instanceId) continue
        const relativeSide: BattlefieldRelativeSide = player.playerIndex === viewerPlayerIndex ? 'self' : 'opponent'
        return {
          card,
          playerIndex: player.playerIndex,
          row,
          slot,
          relativeSide,
          label: battlefieldSlotLabel(relativeSide, row, slot),
        } satisfies BattlefieldTargetLocation
      }
    }
  }
  return null
}

export function battlefieldTargetIds(game: Pick<GameState, 'players'>, candidateIds: Iterable<string>) {
  const candidates = new Set(candidateIds)
  return game.players.flatMap(player => player.field.flat())
    .filter((card): card is Card => Boolean(card?.instanceId && candidates.has(card.instanceId)))
    .map(card => card.instanceId)
}

export function battlefieldTargetLabel(
  game: Pick<GameState, 'players'>,
  viewerPlayerIndex: number,
  instanceId: string,
  visibleLabel?: string | null,
) {
  const location = findBattlefieldTarget(game, viewerPlayerIndex, instanceId)
  if (!location) return visibleLabel?.trim() || ''
  if (location.card.hidden && location.playerIndex !== viewerPlayerIndex) return location.label
  const identity = visibleLabel?.trim() || location.card.name?.trim()
  if (!identity || identity.includes(location.label)) return identity || location.label
  return `${identity} · ${location.label}`
}
