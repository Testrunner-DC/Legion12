type OutcomeSummary = { endedUtc?: string | null; winner?: number | null; error?: string | null }
export type RecordedMatchOutcome =
  | { kind: 'ongoing' | 'draw' | 'invalid' | 'unknown'; winner?: never }
  | { kind: 'decided'; winner: 0 | 1 }

const knownWinner = (value: unknown): value is 0 | 1 => value === 0 || value === 1

// Read only recorded facts. A missing winner is not affirmative evidence of a
// draw, and the last available file frame is not proof that the game ended.
export function recordedMatchOutcome(summary: OutcomeSummary, state?: Record<string, unknown> | null): RecordedMatchOutcome {
  if (state) {
    const phase = state.Phase ?? state.phase
    if (phase !== 'GameOver' && phase !== 10) return { kind: 'ongoing' }
  } else if (!summary.endedUtc) return { kind: 'ongoing' }
  const stateWinner = state?.Winner ?? state?.winner
  const events = state?.Events ?? state?.events
  const eventTypes = new Set(Array.isArray(events) ? events.map(event => event?.Type ?? event?.type) : [])
  const invalid = Boolean(summary.error) || eventTypes.has('game-invalid')
  const draw = state?.EndedByAgreedDraw === true || state?.endedByAgreedDraw === true || eventTypes.has('game-draw')
  if (knownWinner(summary.winner) && knownWinner(stateWinner) && summary.winner !== stateWinner)
    return { kind: 'unknown' }
  const winner = knownWinner(stateWinner) ? stateWinner : summary.winner
  if ((invalid && draw) || ((invalid || draw) && knownWinner(winner))) return { kind: 'unknown' }
  if (invalid) return { kind: 'invalid' }
  if (knownWinner(winner)) return { kind: 'decided', winner }
  return { kind: draw ? 'draw' : 'unknown' }
}
