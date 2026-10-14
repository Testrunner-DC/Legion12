export const tournamentSelfRulingMessage = '当前桌次存在本人利益冲突或身份信息不完整，请由独立裁判处理。'

const knownId = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.trim() === value
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object'
type MatchIdentity = { id: string; playerAAccountId: string; playerBAccountId?: string | null }
function hasMatchIdentity(value: unknown): value is MatchIdentity {
  return record(value) && knownId(value.id) && knownId(value.playerAAccountId)
    && (value.playerBAccountId == null || knownId(value.playerBAccountId))
}

// Conflict eligibility only: callers must retain their existing staff/permission gates.
export function canDirectlyRuleTournamentMatch(accountId: unknown, match: unknown): boolean {
  return knownId(accountId) && hasMatchIdentity(match)
    && accountId !== match.playerAAccountId && accountId !== match.playerBAccountId
}

// Mirrors removal's result side effect: only unfinished rounds and opposed unfinished tables.
// No actor/match identity means unknown, never permission to submit a direct result.
export function canManageParticipantRemovalWithoutSelfRuling(tournament: unknown, accountId: unknown, targetAccountId: unknown): boolean {
  if (!knownId(accountId) || !knownId(targetAccountId) || !record(tournament)
    || !knownId(tournament.id) || !Array.isArray(tournament.rounds)) return false
  for (const round of tournament.rounds) {
    if (!record(round) || typeof round.status !== 'string' || !['pending', 'checkin', 'running', 'completed'].includes(round.status)) return false
    if (round.status === 'completed') continue
    if (!Array.isArray(round.matches)) return false
    for (const match of round.matches) {
      if (!record(match) || typeof match.status !== 'string' || !['waiting', 'running', 'completed'].includes(match.status)) return false
      if (match.status === 'completed') continue
      if (!hasMatchIdentity(match)) return false
      if (!match.playerBAccountId) continue
      const removesSide = targetAccountId === match.playerAAccountId || targetAccountId === match.playerBAccountId
      if (removesSide && !canDirectlyRuleTournamentMatch(accountId, match)) return false
    }
  }
  return true
}
