export function homeAccountVerified(accountId: string | undefined, token: string, verified: boolean) {
  return verified && Boolean(accountId && token)
}

export function recentHomeDeckName(decks: Record<string, { name?: string; updatedAt?: string }>) {
  return Object.entries(decks)
    .filter(([, deck]) => deck && Number.isFinite(Date.parse(deck.updatedAt ?? '')))
    .sort((left, right) => Date.parse(right[1].updatedAt!) - Date.parse(left[1].updatedAt!))
    .map(([name, deck]) => deck.name?.trim() || name.trim())
    .find(Boolean) ?? ''
}

export function homeCanContinueGame(accountId: string | undefined, verified: boolean, connection: {
  accountId: string; status: string; recoveryPhase: string; leavingRoom: boolean;
  game: null | { matchId: string; phase: string };
}) {
  return verified && Boolean(accountId) && connection.accountId === accountId
    && connection.status === 'online' && connection.recoveryPhase === 'snapshot-acknowledged'
    && !connection.leavingRoom && Boolean(connection.game?.matchId)
    && connection.game?.phase !== 'GameOver'
}
