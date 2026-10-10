import type { RankedTimeControlConfig } from './platform'

export interface TournamentTimeControlMinutes {
  total: number
  operation: number
  reconnect: number
  disaster: number
  mulligan: number
}

function preciseMinutes(seconds: number) {
  return Math.round(Math.max(0, seconds) / 60 * 100) / 100
}

export function tournamentTimeControlMinutes(value: RankedTimeControlConfig): TournamentTimeControlMinutes {
  return {
    total: preciseMinutes(value.totalTimeSeconds),
    operation: preciseMinutes(value.operationTimeSeconds),
    reconnect: preciseMinutes(value.reconnectGraceSeconds),
    disaster: preciseMinutes(value.disasterDecisionSeconds),
    mulligan: preciseMinutes(value.mulliganDecisionSeconds),
  }
}

export function tournamentTimeControlSeconds(value: TournamentTimeControlMinutes): RankedTimeControlConfig {
  const seconds = (minutes: number) => Math.max(1, Math.round((Number(minutes) || 0) * 60))
  return {
    totalTimeSeconds: seconds(value.total),
    operationTimeSeconds: seconds(value.operation),
    reconnectGraceSeconds: seconds(value.reconnect),
    disasterDecisionSeconds: seconds(value.disaster),
    mulliganDecisionSeconds: seconds(value.mulligan),
  }
}

export function tournamentMinutesLabel(seconds: number) {
  const minutes = preciseMinutes(seconds)
  return `${Number.isInteger(minutes) ? minutes : minutes.toFixed(2).replace(/0+$/, '').replace(/\.$/, '')} 分钟`
}

export function tournamentTimeControlLabel(value: RankedTimeControlConfig) {
  return `总时限 ${tournamentMinutesLabel(value.totalTimeSeconds)}；单次操作 ${tournamentMinutesLabel(value.operationTimeSeconds)}；断线宽限 ${tournamentMinutesLabel(value.reconnectGraceSeconds)}；天灾/调度各 ${tournamentMinutesLabel(value.disasterDecisionSeconds)}/${tournamentMinutesLabel(value.mulliganDecisionSeconds)}`
}
