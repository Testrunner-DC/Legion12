import { platformRequest } from './platform'

export type IntegrityDisposition = 'normal' | 'insufficient' | 'system-error' | 'confirmed' | 'review' | 'revoked'
export const integrityOutcomeLabels: Record<string, string> = {
  normal: '复核正常', insufficient: '证据不足，不予处罚', 'system-error': '系统异常',
  confirmed: '确认违规', review: '待复核', revoked: '处置已撤销', held: '排位收益待审核',
  released: '排位收益已补发', 'appeal-reply': '申诉处理回复',
}
export const integrityLabel = (value: string) => integrityOutcomeLabels[value] || value
export interface IntegrityAction {
  requestId: string; disposition: IntegrityDisposition; matchIds: string[]; restrictedAccountIds: string[]
  restrictionDays: number | null; evidence: string; reason: string; revokesDecisionId?: string | null
}
export interface IntegrityEffect {
  accountId: string; username: string; scoreDelta: number; restrictionUntil?: string | null
  rewardOutcome: string; blockedReason?: string | null
}
export interface IntegrityPreview {
  revision: number; requestId: string; disposition: string; canConfirm: boolean; blockingReasons: string[]
  matchIds: string[]; accountEffects: IntegrityEffect[]
}
export interface IntegrityDecision {
  decisionId: string; requestId: string; revision: number; disposition: string; effectiveDisposition: string
  matchIds: string[]; restrictedAccountIds: string[]; restrictionDays?: number | null; evidence: string; reason: string
  actorName: string; createdAt: string; revokesDecisionId?: string | null; revokedByDecisionId?: string | null
  accountEffects: IntegrityEffect[]
}
export interface IntegrityNotification {
  id: string; decisionId: string; matchIds: string[]; outcome: string; reason: string; decidedAt: string
  scoreDelta: number; restrictionUntil?: string | null; appealGuidance: string; acknowledged: boolean
  acknowledgedAt?: string | null; relatedDecisionId?: string | null
}
export interface IntegrityAppeal {
  id: string; decisionId: string; accountId?: string; username?: string; statement: string; status: string
  reply?: string | null; revision: number; createdAt: string; updatedAt: string
}
export interface IntegrityPage<T> { items: T[]; nextCursor?: string | null; unreadCount?: number }
export const integrityRequestId = () => globalThis.crypto.randomUUID()
function query(values: Record<string, string | number | boolean | undefined>) {
  const params = new URLSearchParams()
  Object.entries(values).forEach(([key, value]) => { if (value !== undefined && value !== '') params.set(key, String(value)) })
  return params.toString()
}
const post = <T>(path: string, body: unknown) => platformRequest<T>(path, { method: 'POST', body: JSON.stringify(body) })
export const integrityApi = {
  preview: (input: IntegrityAction) => post<IntegrityPreview>('/api/admin/ranked/integrity/preview', input),
  confirm: (input: IntegrityAction, expectedRevision: number) => post<IntegrityDecision>('/api/admin/ranked/integrity/decisions', { input, expectedRevision }),
  decisions: (cursor?: string) => platformRequest<IntegrityPage<IntegrityDecision>>(`/api/admin/ranked/integrity/decisions?${query({ cursor, limit: 20 })}`),
  notifications: (cursor?: string, unreadOnly = false) => platformRequest<IntegrityPage<IntegrityNotification>>(`/api/ranked/integrity/notifications?${query({ cursor, unreadOnly, limit: 20 })}`),
  acknowledge: (id: string) => post(`/api/ranked/integrity/notifications/${encodeURIComponent(id)}/ack`, {}),
  appeal: (decisionId: string, requestId: string, statement: string) => post<IntegrityAppeal>('/api/ranked/integrity/appeals', { decisionId, requestId, statement }),
  appeals: (cursor?: string) => platformRequest<IntegrityPage<IntegrityAppeal>>(`/api/ranked/integrity/appeals?${query({ cursor, limit: 20 })}`),
  adminAppeals: (cursor?: string, status?: string) => platformRequest<IntegrityPage<IntegrityAppeal>>(`/api/admin/ranked/integrity/appeals?${query({ cursor, status, limit: 20 })}`),
  reply: (id: string, requestId: string, expectedRevision: number, status: string, reply: string) => post<IntegrityAppeal>(`/api/admin/ranked/integrity/appeals/${encodeURIComponent(id)}/review`, { requestId, expectedRevision, status, reply }),
}
