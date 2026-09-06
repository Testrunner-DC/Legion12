import { send } from './net'
import { platformRequest } from './platform'

export type MatchDrawStatus = 'pending' | 'accepting' | 'accepted' | 'rejected' | 'expired' | 'cancelled'

export interface MatchDrawClientState {
  id: string
  matchId: string
  requesterName: string
  responderName: string
  reason: string
  status: MatchDrawStatus
  viewerIsRequester: boolean
  viewerCanRespond: boolean
  requestedAt: string
  expiresAt: string
  respondedAt?: string
}

export interface MatchGovernanceClientState {
  supported: boolean
  opponentAccountId?: string
  opponentName?: string
  canRequestDraw: boolean
  drawUnavailableReason?: string
  canReportOpponent: boolean
  reportUnavailableReason?: string
  drawRequest?: MatchDrawClientState | null
}

export interface MatchGovernanceResult {
  action: 'request-draw' | 'resolve-draw' | 'report-opponent'
  clientRequestId: string
  status: string
  message: string
  recordId?: string
}

export interface MatchGovernanceAudit {
  id: string
  actorId: string
  actorName: string
  action: string
  fromValue?: string
  toValue?: string
  comment?: string
  createdAt: string
}

export interface MatchDrawRecord {
  id: string
  matchId: string
  roomCode: string
  modeId: string
  requesterId: string
  requesterName: string
  responderId: string
  responderName: string
  reason: string
  status: MatchDrawStatus
  adminStatus: 'new' | 'reviewing' | 'resolved' | 'closed'
  adminNotes?: string
  requestedAt: string
  expiresAt: string
  respondedAt?: string
  history: MatchGovernanceAudit[]
}

export interface PlayerMatchReport {
  id: string
  matchId: string
  roomCode: string
  modeId: string
  reporterId: string
  reporterName: string
  reportedId: string
  reportedName: string
  description: string
  status: 'new' | 'reviewing' | 'resolved' | 'closed'
  adminNotes?: string
  createdAt: string
  updatedAt: string
  history: MatchGovernanceAudit[]
}

function governanceId(prefix: string) {
  const suffix = globalThis.crypto?.randomUUID?.()
    ?? `${Date.now().toString(16)}${Math.random().toString(16).slice(2)}`
  return `${prefix}-${suffix}`.replace(/[^a-zA-Z0-9_-]/g, '-').slice(0, 80)
}

export function requestMatchDraw(reason: string, requestId = governanceId('draw')) {
  send({ type: 'requestMatchDraw', requestId, reason })
  return requestId
}

export function resolveMatchDraw(requestId: string, accept: boolean) {
  send({ type: 'resolveMatchDraw', requestId, accept })
}

export function reportOpponent(description: string, reportId = governanceId('report')) {
  send({ type: 'reportOpponent', reportId, description })
  return reportId
}

function queryPath(path: string, query: { status?: string; search?: string }) {
  const params = new URLSearchParams()
  if (query.status) params.set('status', query.status)
  if (query.search) params.set('search', query.search)
  return `${path}${params.size ? `?${params}` : ''}`
}

export const matchGovernanceAdminApi = {
  drawRequests: (query: { status?: string; search?: string } = {}) =>
    platformRequest<MatchDrawRecord[]>(queryPath('/api/admin/match-governance/draw-requests', query)),
  playerReports: (query: { status?: string; search?: string } = {}) =>
    platformRequest<PlayerMatchReport[]>(queryPath('/api/admin/match-governance/player-reports', query)),
  updateDrawRequest: (id: string, body: { status: string; adminNotes?: string; comment?: string }) =>
    platformRequest<MatchDrawRecord>(`/api/admin/match-governance/draw-requests/${encodeURIComponent(id)}`, {
      method: 'PATCH', body: JSON.stringify(body),
    }),
  updatePlayerReport: (id: string, body: { status: string; adminNotes?: string; comment?: string }) =>
    platformRequest<PlayerMatchReport>(`/api/admin/match-governance/player-reports/${encodeURIComponent(id)}`, {
      method: 'PATCH', body: JSON.stringify(body),
    }),
}
