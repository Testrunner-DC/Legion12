export type TournamentHubSection = 'discover' | 'mine' | 'history' | 'host' | 'create'

export function tournamentHubSection(value: unknown): TournamentHubSection {
  return typeof value === 'string' && ['discover', 'mine', 'history', 'host', 'create'].includes(value)
    ? value as TournamentHubSection : 'discover'
}
