import { platformRequest } from './platform'

export type IntegrityDisposition = 'normal' | 'insufficient' | 'system-error' | 'confirmed' | 'review' | 'revoked'
export type IntegrityScope = 'match' | 'account'
export const integrityOutcomeLabels: Record<string, string> = {
  normal: '复核正常', insufficient: '证据不足，不予处罚', 'system-error': '系统异常',
  confirmed: '确认违规', review: '待复核', revoked: '处置已撤销', held: '排位收益待审核',
  released: '排位收益已补发', voided: '所选对局收益已作废', restored: '所选对局收益已恢复',
  unchanged: '排位收益不变', 'voided-profile-preserved': '所选对局已作废；为保护后续正常结算，当前排位档案保持不变',
  'appeal-reply': '申诉处理回复', partial: '部分账号已处置，其他账号未作结论',
}
export const integrityLabel = (value: string) => integrityOutcomeLabels[value] || value
export function integrityNotificationMessage(accountIds: readonly string[] | undefined, confirmed = false) {
  if (!accountIds) return '仅在实际影响排位资格或收益时通知相关玩家。'
  if (!accountIds.length) return '本次仅保存后台复核记录，不通知玩家。'
  return `${confirmed ? '已为' : '确认后将为'} ${accountIds.length} 名受影响玩家生成结果通知。`
}
export interface IntegrityAction {
  requestId: string; disposition: IntegrityDisposition; matchIds: string[]; restrictedAccountIds: string[]
  restrictionDays: number | null; evidence: string; reason: string; revokesDecisionId?: string | null
  scope?: IntegrityScope; targetAccountIds?: string[]
}
export interface IntegrityEffect {
  accountId: string; username: string; scoreDelta: number; restrictionUntil?: string | null
  rewardOutcome: string; blockedReason?: string | null
}
export interface IntegrityPreview {
  revision: number; requestId: string; disposition: string; canConfirm: boolean; blockingReasons: string[]
  matchIds: string[]; accountEffects: IntegrityEffect[]; playerNotificationAccountIds: string[]
  scope?: IntegrityScope; targetAccountIds?: string[]
}
export interface IntegrityDecision {
  decisionId: string; requestId: string; revision: number; disposition: string; effectiveDisposition: string
  matchIds: string[]; restrictedAccountIds: string[]; restrictionDays?: number | null; evidence: string; reason: string
  actorName: string; createdAt: string; revokesDecisionId?: string | null; revokedByDecisionId?: string | null
  accountEffects: IntegrityEffect[]; playerNotificationAccountIds: string[]
  scope?: IntegrityScope; targetAccountIds?: string[]
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
function normalizedTargets(value: unknown): string[] | null {
  if (!Array.isArray(value) || value.some(id => typeof id !== 'string' || !id.trim() || id !== id.trim())) return null
  const ids = value.map(id => id.toLowerCase()).sort()
  return new Set(ids).size === ids.length ? ids : null
}
function accountOperation(input: IntegrityAction) {
  if (input.scope === 'account') {
    if (!normalizedTargets(input.targetAccountIds)?.length) throw new Error('个人处置必须明确选择目标账号，请重新预览。')
    return true
  }
  if ((input.scope !== undefined && input.scope !== 'match')
    || (input.targetAccountIds !== undefined && (!Array.isArray(input.targetAccountIds) || input.targetAccountIds.length)))
    throw new Error('处置范围与目标账号不一致，请重新预览。')
  return false
}
function requireAccountEcho(input: IntegrityAction, result: IntegrityPreview | IntegrityDecision) {
  const wanted = normalizedTargets(input.targetAccountIds)
  const echoed = normalizedTargets(result?.targetAccountIds)
  if (result?.scope !== 'account' || !wanted?.length || !echoed || wanted.length !== echoed.length
    || wanted.some((id, index) => id !== echoed[index]))
    throw new Error('服务器返回的个人处置范围或目标不匹配，请刷新并核对结果，不要改用整场处置重试。')
}
export const integrityApi = {
  async preview(input: IntegrityAction) {
    const account = accountOperation(input)
    const result = await post<IntegrityPreview>(account ? '/api/admin/ranked/integrity/account/preview' : '/api/admin/ranked/integrity/preview', input)
    if (account) requireAccountEcho(input, result)
    return result
  },
  async confirm(input: IntegrityAction, expectedRevision: number) {
    const account = accountOperation(input)
    const result = await post<IntegrityDecision>(account ? '/api/admin/ranked/integrity/account/decisions' : '/api/admin/ranked/integrity/decisions', { input, expectedRevision })
    if (account) requireAccountEcho(input, result)
    return result
  },
  decisions: (cursor?: string) => platformRequest<IntegrityPage<IntegrityDecision>>(`/api/admin/ranked/integrity/decisions?${query({ cursor, limit: 20 })}`),
  notifications: (cursor?: string, unreadOnly = false) => platformRequest<IntegrityPage<IntegrityNotification>>(`/api/ranked/integrity/notifications?${query({ cursor, unreadOnly, limit: 20 })}`),
  acknowledge: (id: string) => post(`/api/ranked/integrity/notifications/${encodeURIComponent(id)}/ack`, {}),
  appeal: (decisionId: string, requestId: string, statement: string) => post<IntegrityAppeal>('/api/ranked/integrity/appeals', { decisionId, requestId, statement }),
  appeals: (cursor?: string) => platformRequest<IntegrityPage<IntegrityAppeal>>(`/api/ranked/integrity/appeals?${query({ cursor, limit: 20 })}`),
  adminAppeals: (cursor?: string, status?: string) => platformRequest<IntegrityPage<IntegrityAppeal>>(`/api/admin/ranked/integrity/appeals?${query({ cursor, status, limit: 20 })}`),
  reply: (id: string, requestId: string, expectedRevision: number, status: string, reply: string) => post<IntegrityAppeal>(`/api/admin/ranked/integrity/appeals/${encodeURIComponent(id)}/review`, { requestId, expectedRevision, status, reply }),
}
