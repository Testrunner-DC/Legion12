export function homeAccountVerified(accountId: string | undefined, token: string, verified: boolean) {
  return verified && Boolean(accountId && token)
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
