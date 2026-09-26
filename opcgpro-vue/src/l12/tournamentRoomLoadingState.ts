export interface TournamentRoomLoadingInput {
  status: 'offline' | 'connecting' | 'online'
  recoveryPhase: string
  connectionIssue: string
  notice: string
  room: null | {
    tournamentId?: string
    tournamentMatchId?: string
    started: boolean
    yourPlayerIndex: number | null
    players: Array<{ name: string; playerIndex: number; connected: boolean }>
  }
}

export interface TournamentRoomLoadingView {
  kind: 'waiting' | 'loading' | 'failed'
  title: string
  detail: string
}

export function tournamentRoomLoadingState(input: TournamentRoomLoadingInput): TournamentRoomLoadingView {
  const room = input.room
  const opponent = room?.players.find(player => player.playerIndex !== room.yourPlayerIndex)
  const tournamentRoom = Boolean(room?.tournamentId && room.tournamentMatchId)
  if (input.connectionIssue === 'authentication' || input.connectionIssue === 'superseded') return {
    kind: 'failed',
    title: '赛事对局加载失败',
    detail: input.notice || '当前登录连接无法恢复这场赛事对局，请返回赛事详情后重试。',
  }
  if (input.status !== 'online' || !['snapshot-acknowledged', 'session-claimed'].includes(input.recoveryPhase)) return {
    kind: 'loading',
    title: input.recoveryPhase === 'disconnected' ? '连接中断，正在恢复赛事房间…' : '正在加载赛事对局…',
    detail: input.notice || '正在向服务器请求权威房间与对局快照。',
  }
  if (tournamentRoom && !room?.started && !opponent?.connected) return {
    kind: 'waiting',
    title: '已进入赛事房间，等待对手',
    detail: opponent?.name ? `正在等待 ${opponent.name} 进入本桌；对手进入后将自动开始。` : '对手进入后将自动开始，无需刷新页面。',
  }
  if (tournamentRoom) return {
    kind: 'loading',
    title: '对手已进入，正在加载对局…',
    detail: '服务器正在下发本桌的权威开局快照。',
  }
  return {
    kind: 'failed',
    title: '未找到可恢复的对局',
    detail: input.notice || '服务器已完成同步，但当前账号没有可进入的房间或对局。',
  }
}
