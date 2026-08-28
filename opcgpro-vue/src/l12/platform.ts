import { computed, reactive } from 'vue'
import { disconnect, l12State } from './net'
import type { SavedL12Deck } from './decks'

export interface PlatformAccount {
  id: string; username: string; role: string; createdAt: string; publicHistory: boolean; permissions?: string[]
  disabled?: boolean; disabledAt?: string; disabledReason?: string
}
export interface PlatformSession {
  id: string; createdAt: string; expiresAt: string; current: boolean; authStrength: string; permissionVersion: number
}
export interface SessionRevocation { sessionId?: string; revokedCount: number; alreadyRevoked: boolean }
export interface PlatformFriend {
  accountId: string; username: string; status: 'none' | 'pending' | 'accepted'
  direction: 'none' | 'incoming' | 'outgoing'; createdAt: string; online?: boolean
}
export interface PlatformPresence { accountId: string; username: string; online: boolean }
export interface PublishedDeck {
  id: string; ownerId: string; author: string; deck: SavedL12Deck; likes: number; copies: number; liked: boolean
  createdAt: string; updatedAt: string; official?: boolean
}
export interface BugReport {
  id: string; reporterName: string; title: string; description: string; page: string; roomCode?: string; matchId?: string
  version: string; status: string; priority: string; assignee?: string; adminNotes?: string; history: BugAudit[]; createdAt: string; updatedAt: string
}
export interface BugAudit { id: string; actorName: string; action: string; fromValue?: string; toValue?: string; comment?: string; createdAt: string }
export interface EffectAtomDescriptor {
  kind: string; category: string; label: string; description: string; runtimeExecutable: boolean; kernelContract: string
}
export interface EffectAtom {
  atomId: string; kind: string; label: string; order: number; parameters: Record<string, string>; runtimeExecutable: boolean; source: string; stage: string
}
export interface AtomicAbility {
  abilityId: string; cardId: string; sequence: number; text: string; trigger: string; atoms: EffectAtom[]
  migrationStatus: string; hasLegacyFallback: boolean; mappingSource: string; confidence: number; executionModel: string
  reviewStatus: 'unreviewed' | 'human-assisted' | 'confirmed' | 'rejected'; reviewSource: string
}
export interface AtomicCardEffect {
  cardId: string; name: string; product: string; faction: string; cardType: string; imageUrl?: string; effectText: string
  abilities: AtomicAbility[]; migrationStatus: string; atomCount: number; executableAtomCount: number; legacyAtomCount: number; atomKinds: string[]
  reviewStatus: 'unreviewed' | 'human-assisted' | 'confirmed' | 'rejected'; reviewSource: string
}
export interface AtomicCoverage {
  totalCards: number; cardsWithText: number; totalAbilities: number; totalAtoms: number; declarativeReadyAbilities: number
  verifiedAbilities: number; legacyBackedAbilities: number; byStatus: Record<string, number>; byAtomKind: Record<string, number>
}
export interface AtomicEffectPage { items: AtomicCardEffect[]; total: number; page: number; pageSize: number; coverage: AtomicCoverage }
export interface ContentEntry { key: string; draftValue: string; publishedValue: string; status: 'draft' | 'published'; updatedBy?: string; updatedAt?: string; publishedBy?: string; publishedAt?: string; version: number; publishedVersionId?: string; rollbackVersionId?: string }
export interface EffectReview { cardId: string; abilityId?: string; status: 'unreviewed' | 'human-assisted' | 'confirmed' | 'rejected'; note: string; reviewer: string; updatedAt: string }
export interface AdminAudit {
  id: string; actorId: string; actorName: string; category: string; action: string; target: string; fromValue?: string; toValue?: string; comment?: string; createdAt: string
  correlationId?: string; outcome?: string; permission?: string; reason?: string; commandId?: string; idempotencyKey?: string; dryRun?: boolean; expectedVersion?: number
}
export interface AdminCommand {
  id: string; idempotencyKey?: string; type: string; actorId: string; actorName: string; requestedAt: string
  scope: string; reason?: string; dryRun: boolean; expectedVersion?: number; risk: string; status: string
  permission: string; payload: unknown; result?: unknown; resultCode?: string; resultMessage?: string
  resultStatusCode?: number; failureReason?: string; correlationId: string; resourceVersion: number; updatedAt: string
}
export interface AdminApproval {
  commandId: string; requesterId: string; requesterName: string; requestedAt: string; status: string
  reviewerId?: string; reviewerName?: string; decision?: string; reason?: string; reviewedAt?: string
}
export interface MfaCapability {
  credentialProtectionAvailable: boolean; enrollmentEnabled: boolean; mode: string
  secretsPersisted: boolean; requirement: string
}
export interface SecurityAlert { code: string; severity: 'critical' | 'warning' | string; count: number; message: string }
export interface SecurityStatus {
  platformVersion: number; activeApprovers: number; secondApproverReady: boolean
  offlineBootstrapEnabled: boolean; offlineBootstrapCredentialConfigured: boolean; offlineBootstrapUsed: boolean
  disabledAccounts: number; activeLoginLocks: number; pendingApprovals: number; oldestPendingApprovalAt?: string
  highRiskAuditAvailable: boolean; auditRetentionDays: number; auditArchiveSegments: number
  lastAuditArchiveAt?: string; mfa: MfaCapability; alerts: SecurityAlert[]
}
export interface AuditArchiveSegment {
  id: string; from: string; until: string; eventCount: number; sha256: string; createdAt: string
}
export interface AuditArchiveOperation {
  applied: boolean; archiveBefore: string; retentionDays: number; eligibleEvents: number
  segment?: AuditArchiveSegment; sourceEventsRetained: boolean
}
export interface AuditArchiveRecovery {
  success: boolean; segments: number; events: number; error?: string; rehearsedAt: string
}
export interface AdminCommandAccepted { commandId: string; status: 'requested'; message: string; command: AdminCommand }
export interface ContentBatchItem { key: string; previousValue: string; publishedValue: string; previousVersionId?: string; publishedVersionId: string }
export interface ContentBatch { id: string; action: 'publish' | 'rollback'; sourceBatchId?: string; status: string; actorId: string; actorName: string; createdAt: string; items: ContentBatchItem[] }
export interface ContentPreviewItem { key: string; draftValue: string; publishedValue: string; entryVersion: number; wouldChange: boolean }
export interface ContentBatchPreview { action: 'publish' | 'rollback'; sourceBatchId?: string; items: ContentPreviewItem[] }
export interface ContentBatchOperation { applied: boolean; batch?: ContentBatch; preview?: ContentBatchPreview }
export interface VerifiedReleaseArtifact {
  id: string; commit: string; releaseSha256: string; cardsHash?: string; cardsSha256?: string; verifiedAt: string
  verificationGates: string[]; environments: string[]
}
export interface ReleaseProbe { success: boolean; code: string; durationMs: number }
export interface ReleaseEnvironment {
  environment: string; version: number; state: string; adapterConfigured: boolean; activeArtifactId?: string
  activeCommit?: string; lastRunId?: string; health: ReleaseProbe; webSocket: ReleaseProbe; observedAt: string
}
export interface ReleaseCheck { kind: string; success: boolean; code: string; durationMs: number; checkedAt: string }
export interface ReleaseRun {
  id: string; commandId: string; action: 'deploy' | 'rollback'; environment: string; artifactId: string; commit: string
  releaseSha256: string; cardsHash?: string; status: string; actorId: string; actorName: string
  previousArtifactId?: string; rollbackTargetRunId?: string; rollbackAttempted: boolean; rollbackSucceeded: boolean
  resultCode: string; checks: ReleaseCheck[]; environmentVersion: number; startedAt: string; completedAt: string
}
export interface ReleasePlan {
  action: 'deploy' | 'rollback'; environment: string; environmentVersion: number; currentArtifactId?: string
  targetArtifact: VerifiedReleaseArtifact; rollbackTargetRunId?: string; steps: string[]; willExecute: boolean
}
export interface ReleaseOperation { applied: boolean; plan: ReleasePlan; run?: ReleaseRun }
export type TournamentStatus = 'registration' | 'running' | 'completed'
export type TournamentRoundStatus = 'pending' | 'checkin' | 'running' | 'completed'
export type TournamentDeckVisibility = 'always' | 'after' | 'private'
export type TournamentDisasterMode = 'all' | 'random' | 'season' | 'none'
export interface TournamentRulesSnapshot {
  ruleset: string; disasterMode: TournamentDisasterMode; banList: string
  deckVisibility: TournamentDeckVisibility; hash: string; capturedAt: string
}
export interface TournamentDeckSnapshot { name: string; code?: string; hash: string; submittedAt: string; lockedAt?: string }
export interface TournamentStaff { accountId: string; username: string }
export interface TournamentParticipant {
  accountId: string; username: string; checkedIn: boolean; dropped: boolean; deck?: TournamentDeckSnapshot
}
export interface TournamentRuling {
  id: string; matchId: string; kind: string; targetAccountId?: string; decision: string; minutes: number
  reason: string; actorId: string; actorName: string; createdAt: string
}
export interface TournamentMatch {
  id: string; table: number; playerAAccountId: string; playerAName: string; playerBAccountId?: string
  playerBName: string; roomCode: string; readyA: boolean; readyB: boolean; status: 'waiting' | 'running' | 'completed'
  result?: string; timeExtensionMinutes: number; startedAt?: string; deadline?: string; recordedMatchId?: string
  rulings: TournamentRuling[]
}
export interface TournamentRound {
  id: string; number: number; status: TournamentRoundStatus; paused: boolean; startedAt?: string; pausedAt?: string
  totalPausedSeconds: number; matches: TournamentMatch[]
}
export interface Tournament {
  id: string; code: string; name: string; organizerAccountId: string; organizerName: string; referees: TournamentStaff[]
  status: TournamentStatus; format: 'single' | 'swiss' | 'league'; visibility: 'public' | 'code'; maxPlayers: number
  startAt?: string; description: string; rules: TournamentRulesSnapshot; roundMinutes: number; checkInMinutes: number
  participants: TournamentParticipant[]; rounds: TournamentRound[]; version: number; legacyImported: boolean
  createdAt: string; updatedAt: string; completedAt?: string
}
export interface TournamentList { platformVersion: number; items: Tournament[] }
export interface TournamentCreateInput {
  name: string; format: Tournament['format']; visibility: Tournament['visibility']; maxPlayers: number; startAt?: string
  ruleset: string; description: string; deckVisibility: TournamentDeckVisibility; disasterMode: TournamentDisasterMode
  banList: string; roundMinutes: number; checkInMinutes: number; refereeAccountIds: string[]
}
export interface LegacyTournamentParticipantInput {
  name: string; deckName?: string; deckCode?: string; checkedIn?: boolean; dropped?: boolean
}
export interface LegacyTournamentMatchInput {
  id?: string; table: number; playerA: string; playerB: string; roomCode?: string; readyA: boolean; readyB: boolean
  status?: string; result?: string; ruling?: string; timeExtension: number; startedAt?: string; deadline?: string
}
export interface LegacyTournamentRoundInput {
  number: number; status?: string; paused: boolean; startedAt?: string; matches?: LegacyTournamentMatchInput[]
}
export interface LegacyTournamentInput {
  id: string; code?: string; name: string; organizer?: string; referees?: string[]; status?: string; format?: string
  visibility?: string; maxPlayers: number; startAt?: string; ruleset?: string; description?: string
  deckVisibility?: string; disasterMode?: string; banList?: string; roundMinutes: number; checkInMinutes: number
  participants?: LegacyTournamentParticipantInput[]; rounds?: LegacyTournamentRoundInput[]
  createdAt?: string; updatedAt?: string; completedAt?: string
}
export interface TournamentLegacyImport { previewHash: string; applied: boolean; tournaments: Tournament[] }
export type TournamentWriteResult = Tournament | AdminCommandAccepted

export class PlatformRequestError extends Error {
  constructor(message: string, public readonly status: number, public readonly code: string, public readonly correlationId: string) { super(message) }
}

function loadAccount(): PlatformAccount | null {
  try { return JSON.parse(localStorage.getItem('l12-account') || 'null') as PlatformAccount | null } catch { return null }
}

export const platformState = reactive({
  token: localStorage.getItem('l12-auth-token') || '',
  account: loadAccount(),
})

export function hasPermission(permission: string) {
  const account = platformState.account
  if (!account) return false
  if (account.permissions?.length) return account.permissions.includes(permission)
  if (account.role === 'admin') return true
  if (account.role === 'editor') return ['admin.content.read', 'admin.content.draft', 'admin.content.publish', 'admin.content.rollback', 'admin.effects.read', 'admin.effects.review', 'admin.commands.read'].includes(permission)
  if (account.role === 'support') return ['admin.bugs.read', 'admin.bugs.write', 'admin.commands.read'].includes(permission)
  return account.role === 'release-manager' && ['admin.content.read', 'admin.commands.read', 'admin.approvals.read', 'admin.approvals.review', 'admin.security.read', 'releases.read', 'releases.execute', 'releases.approvals.review', 'releases.runtime.read'].includes(permission)
}

export const isAdmin = computed(() => platformState.account?.role === 'admin')
export const canAccessAdmin = computed(() => hasPermission('admin.bugs.read') || hasPermission('admin.content.read') || hasPermission('admin.effects.read') || hasPermission('admin.audit.read') || hasPermission('admin.commands.read') || hasPermission('admin.approvals.read') || hasPermission('admin.security.read') || hasPermission('releases.read') || hasPermission('releases.runtime.read'))

export function apiBase() {
  try {
    const url = new URL(l12State.endpoint)
    url.protocol = url.protocol === 'wss:' ? 'https:' : 'http:'
    url.pathname = ''
    return url.toString().replace(/\/$/, '')
  } catch { return `${location.protocol}//${location.hostname}:8080` }
}

export async function platformRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Content-Type', 'application/json')
  headers.set('X-Correlation-ID', globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(16)}${Math.random().toString(16).slice(2)}`)
  if (platformState.token) headers.set('Authorization', `Bearer ${platformState.token}`)
  const response = await fetch(`${apiBase()}${path}`, { ...init, headers })
  const payload = await response.json().catch(() => ({}))
  if (!response.ok) {
    const correlationId = String(payload.correlationId || response.headers.get('X-Correlation-ID') || '')
    const message = `${payload.message || `请求失败（${response.status}）`}${correlationId ? `（关联 ID：${correlationId}）` : ''}`
    if (response.status === 401 && payload.code === 'authentication_required' && platformState.token) forgetAccount()
    throw new PlatformRequestError(message, response.status, String(payload.code || 'request_failed'), correlationId)
  }
  return payload as T
}

function remember(account: PlatformAccount, token: string) {
  platformState.account = account
  platformState.token = token
  localStorage.setItem('l12-account', JSON.stringify(account))
  localStorage.setItem('l12-auth-token', token)
  l12State.nickname = account.username
  localStorage.setItem('l12-nickname', account.username)
}

export async function register(username: string, password: string) {
  const result = await platformRequest<{ account: PlatformAccount; token: string }>('/api/auth/register', { method: 'POST', body: JSON.stringify({ username, password }) })
  remember(result.account, result.token)
}

export async function login(username: string, password: string) {
  const result = await platformRequest<{ account: PlatformAccount; token: string }>('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) })
  remember(result.account, result.token)
}

function forgetAccount() {
  disconnect()
  platformState.account = null
  platformState.token = ''
  localStorage.removeItem('l12-account')
  localStorage.removeItem('l12-auth-token')
  l12State.nickname = ''
  localStorage.removeItem('l12-nickname')
}

export async function logout(options: { revokeServer?: boolean } = {}) {
  try {
    if (options.revokeServer !== false && platformState.token) {
      await platformRequest<SessionRevocation>('/api/auth/sessions/current', { method: 'DELETE' })
    }
  } finally { forgetAccount() }
}

export const sessionApi = {
  list: () => platformRequest<PlatformSession[]>('/api/auth/sessions'),
  revoke: (sessionId: string) => platformRequest<SessionRevocation>(`/api/auth/sessions/${encodeURIComponent(sessionId)}`, { method: 'DELETE' }),
  revokeAll: () => platformRequest<SessionRevocation>('/api/auth/sessions', { method: 'DELETE' }),
}

export async function changePassword(currentPassword: string, newPassword: string) {
  return platformRequest<{ message: string }>('/api/auth/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) })
}

export async function submitBug(input: { title: string; description: string; page: string; roomCode?: string; matchId?: string; version: string }) {
  return platformRequest<BugReport>('/api/bugs', { method: 'POST', body: JSON.stringify(input) })
}

export async function getPublicContent(key: string) {
  return platformRequest<{ key: string; value: string }>(`/api/content/${encodeURIComponent(key)}`)
}

export const mfaCapability = () => platformRequest<MfaCapability>('/api/auth/mfa/capability')

function commandKey(prefix: string) {
  const suffix = globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(16)}${Math.random().toString(16).slice(2)}`
  return `${prefix}-${suffix}`.replace(/[^a-zA-Z0-9_.-]/g, '-').slice(0, 128)
}

function commandBody<T extends Record<string, unknown>>(prefix: string, body: T) {
  return { ...body, idempotencyKey: body.idempotencyKey || commandKey(prefix) }
}

export const adminApi = {
  accounts: () => platformRequest<PlatformAccount[]>('/api/admin/accounts'),
  setRole: (id: string, role: string) => platformRequest<AdminCommandAccepted>(`/api/admin/v1/accounts/${encodeURIComponent(id)}/role`, { method: 'PUT', body: JSON.stringify(commandBody('role', { role })) }),
  setAccountStatus: (id: string, disabled: boolean, reason: string, expectedVersion: number, dryRun = false) => platformRequest<AdminCommandAccepted | { applied: boolean; account: PlatformAccount; revokedSessions: number; alreadyApplied: boolean }>(`/api/admin/v1/accounts/${encodeURIComponent(id)}/status`, {
    method: 'PUT', body: JSON.stringify(commandBody('account-status', { disabled, reason, expectedVersion, dryRun })),
  }),
  sessions: (id: string) => platformRequest<PlatformSession[]>(`/api/admin/accounts/${encodeURIComponent(id)}/sessions`),
  revokeSession: (id: string, sessionId: string) => platformRequest<SessionRevocation>(`/api/admin/accounts/${encodeURIComponent(id)}/sessions/${encodeURIComponent(sessionId)}`, { method: 'DELETE' }),
  revokeSessions: (id: string) => platformRequest<SessionRevocation>(`/api/admin/accounts/${encodeURIComponent(id)}/sessions`, { method: 'DELETE' }),
  bugs: (query: { status?: string; priority?: string; assignee?: string; search?: string } = {}) => {
    const params = new URLSearchParams()
    Object.entries(query).forEach(([key, value]) => { if (value) params.set(key, value) })
    return platformRequest<BugReport[]>(`/api/admin/bugs${params.size ? `?${params}` : ''}`)
  },
  updateBug: (id: string, body: Partial<Pick<BugReport, 'status' | 'priority' | 'assignee' | 'adminNotes'>> & { comment?: string }) => platformRequest<BugReport>(`/api/admin/v1/bugs/${encodeURIComponent(id)}`, { method: 'PATCH', body: JSON.stringify(commandBody('bug', body)) }),
  getContent: (key: string) => platformRequest<ContentEntry>(`/api/admin/content/${encodeURIComponent(key)}`),
  saveContentDraft: (key: string, value: string) => platformRequest<ContentEntry>(`/api/admin/v1/content/${encodeURIComponent(key)}/draft`, { method: 'PUT', body: JSON.stringify(commandBody('draft', { value })) }),
  previewContent: (keys: string[]) => platformRequest<ContentBatchPreview>('/api/admin/v1/content/preview', { method: 'POST', body: JSON.stringify({ keys }) }),
  publishContent: (keys: string[], dryRun = false) => platformRequest<AdminCommandAccepted | ContentBatchOperation>('/api/admin/v1/content/publish', { method: 'POST', body: JSON.stringify(commandBody('content-publish', { keys, dryRun })) }),
  contentBatches: () => platformRequest<ContentBatch[]>('/api/admin/v1/content/batches'),
  rollbackContent: (batchId: string, dryRun = false) => platformRequest<AdminCommandAccepted | ContentBatchOperation>('/api/admin/v1/content/rollback', { method: 'POST', body: JSON.stringify(commandBody('content-rollback', { batchId, dryRun })) }),
  effectAtoms: () => platformRequest<EffectAtomDescriptor[]>('/api/admin/effect-atoms'),
  effectCoverage: () => platformRequest<AtomicCoverage>('/api/admin/effects/coverage'),
  effects: (query: { search?: string; status?: string; product?: string; atomKind?: string; page?: number; pageSize?: number } = {}) => {
    const params = new URLSearchParams()
    Object.entries(query).forEach(([key, value]) => { if (value !== undefined && value !== '') params.set(key, String(value)) })
    return platformRequest<AtomicEffectPage>(`/api/admin/effects${params.size ? `?${params}` : ''}`)
  },
  effect: (cardId: string) => platformRequest<AtomicCardEffect>(`/api/admin/effects/${encodeURIComponent(cardId)}`),
  reviewEffect: (cardId: string, body: { abilityId?: string; status: string; note?: string }) => platformRequest<EffectReview>(`/api/admin/v1/effects/${encodeURIComponent(cardId)}/review`, { method: 'PUT', body: JSON.stringify(commandBody('effect-review', body)) }),
  releaseArtifacts: () => platformRequest<VerifiedReleaseArtifact[]>('/api/admin/v1/releases/artifacts'),
  releaseEnvironments: () => platformRequest<ReleaseEnvironment[]>('/api/admin/v1/releases/environments'),
  releaseRuns: (query: { environment?: string; status?: string } = {}) => {
    const params = new URLSearchParams()
    Object.entries(query).forEach(([key, value]) => { if (value) params.set(key, value) })
    return platformRequest<ReleaseRun[]>(`/api/admin/v1/releases/runs${params.size ? `?${params}` : ''}`)
  },
  releaseRun: (id: string) => platformRequest<ReleaseRun>(`/api/admin/v1/releases/runs/${encodeURIComponent(id)}`),
  deployRelease: (artifactId: string, environment: string, expectedVersion: number, dryRun: boolean, reason: string) => platformRequest<AdminCommandAccepted | ReleaseOperation>('/api/admin/v1/releases/deploy', {
    method: 'POST', body: JSON.stringify(commandBody('release-deploy', { artifactId, environment, expectedVersion, dryRun, reason })),
  }),
  rollbackRelease: (targetRunId: string, expectedVersion: number, dryRun: boolean, reason: string) => platformRequest<AdminCommandAccepted | ReleaseOperation>('/api/admin/v1/releases/rollback', {
    method: 'POST', body: JSON.stringify(commandBody('release-rollback', { targetRunId, expectedVersion, dryRun, reason })),
  }),
  commands: (query: { status?: string; type?: string; actorId?: string } = {}) => {
    const params = new URLSearchParams()
    Object.entries(query).forEach(([key, value]) => { if (value) params.set(key, value) })
    return platformRequest<AdminCommand[]>(`/api/admin/v1/commands${params.size ? `?${params}` : ''}`)
  },
  command: (id: string) => platformRequest<AdminCommand>(`/api/admin/v1/commands/${encodeURIComponent(id)}`),
  approvals: (status = 'requested') => platformRequest<AdminApproval[]>(`/api/admin/v1/approvals?status=${encodeURIComponent(status)}`),
  reviewApproval: (commandId: string, decision: 'approve' | 'reject', reason = '') => platformRequest<AdminCommand>(`/api/admin/v1/approvals/${encodeURIComponent(commandId)}`, { method: 'POST', body: JSON.stringify({ decision, reason }) }),
  securityStatus: () => platformRequest<SecurityStatus>('/api/admin/v1/security/status'),
  auditArchives: () => platformRequest<AuditArchiveSegment[]>('/api/admin/v1/security/audit-archives'),
  archiveAudit: (retentionDays: number, expectedVersion: number, dryRun: boolean, reason: string) => platformRequest<AdminCommandAccepted | AuditArchiveOperation>('/api/admin/v1/security/audit-archives', {
    method: 'POST', body: JSON.stringify(commandBody('audit-archive', { retentionDays, expectedVersion, dryRun, reason })),
  }),
  rehearseAuditRecovery: () => platformRequest<AuditArchiveRecovery>('/api/admin/v1/security/audit-recovery-rehearsal'),
  audit: (query: string | { category?: string; outcome?: string; actorId?: string; commandId?: string; correlationId?: string } = '') => {
    const filters = typeof query === 'string' ? { category: query } : query
    const params = new URLSearchParams()
    Object.entries(filters).forEach(([key, value]) => { if (value) params.set(key, value) })
    return platformRequest<AdminAudit[]>(`/api/admin/v1/audit${params.size ? `?${params}` : ''}`)
  },
}

export const tournamentApi = {
  list: (query: { status?: string; search?: string; mine?: boolean } = {}) => {
    const params = new URLSearchParams()
    Object.entries(query).forEach(([key, value]) => { if (value !== undefined && value !== '') params.set(key, String(value)) })
    return platformRequest<TournamentList>(`/api/tournaments${params.size ? `?${params}` : ''}`)
  },
  get: (id: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}`),
  getByCode: (code: string) => platformRequest<Tournament>(`/api/tournaments/code/${encodeURIComponent(code)}`),
  create: (tournament: TournamentCreateInput, expectedVersion: number, dryRun = false) => platformRequest<Tournament>('/api/tournaments', {
    method: 'POST', body: JSON.stringify(commandBody('tournament-create', { tournament, expectedVersion, dryRun })),
  }),
  importLegacy: (tournaments: LegacyTournamentInput[], expectedVersion: number, previewHash?: string, dryRun = true) => platformRequest<TournamentLegacyImport>('/api/tournaments/import-legacy', {
    method: 'POST', body: JSON.stringify(commandBody('tournament-import', { tournaments, expectedVersion, previewHash, dryRun })),
  }),
  register: (id: string, expectedVersion: number, deckName: string, deckCode: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/registrations`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-register', { expectedVersion, deckName, deckCode })),
  }),
  updateRegistration: (id: string, expectedVersion: number, deckName: string, deckCode: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/registration`, {
    method: 'PUT', body: JSON.stringify(commandBody('tournament-registration', { expectedVersion, deckName, deckCode })),
  }),
  drop: (id: string, expectedVersion: number) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/registration`, {
    method: 'DELETE', body: JSON.stringify(commandBody('tournament-drop', { expectedVersion })),
  }),
  setStaff: (id: string, expectedVersion: number, refereeAccountIds: string[], reason: string) => platformRequest<TournamentWriteResult>(`/api/tournaments/${encodeURIComponent(id)}/staff`, {
    method: 'PUT', body: JSON.stringify(commandBody('tournament-staff', { expectedVersion, refereeAccountIds, reason })),
  }),
  start: (id: string, expectedVersion: number, reason: string) => platformRequest<TournamentWriteResult>(`/api/tournaments/${encodeURIComponent(id)}/start`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-start', { expectedVersion, reason })),
  }),
  nextRound: (id: string, expectedVersion: number, reason: string) => platformRequest<TournamentWriteResult>(`/api/tournaments/${encodeURIComponent(id)}/rounds`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-round', { expectedVersion, reason })),
  }),
  checkIn: (id: string, roundNumber: number, expectedVersion: number, accountId: string | undefined, ready: boolean) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/rounds/${roundNumber}/check-in`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-check-in', { expectedVersion, accountId, ready })),
  }),
  startRound: (id: string, roundNumber: number, expectedVersion: number, reason: string) => platformRequest<TournamentWriteResult>(`/api/tournaments/${encodeURIComponent(id)}/rounds/${roundNumber}/start`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-round-start', { expectedVersion, reason })),
  }),
  pauseRound: (id: string, roundNumber: number, expectedVersion: number, paused: boolean, reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/rounds/${roundNumber}/pause`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-round-pause', { expectedVersion, paused, reason })),
  }),
  extendMatch: (id: string, matchId: string, expectedVersion: number, minutes: number, reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/matches/${encodeURIComponent(matchId)}/time-extension`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-extension', { expectedVersion, minutes, reason })),
  }),
  ruleMatch: (id: string, matchId: string, expectedVersion: number, body: { kind: 'result' | 'penalty' | 'no-show'; targetAccountId?: string; decision: string; reason: string }) => platformRequest<TournamentWriteResult>(`/api/tournaments/${encodeURIComponent(id)}/matches/${encodeURIComponent(matchId)}/rulings`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-ruling', { expectedVersion, ...body })),
  }),
  linkMatch: (id: string, matchId: string, expectedVersion: number, recordedMatchId: string, reason: string) => platformRequest<TournamentWriteResult>(`/api/tournaments/${encodeURIComponent(id)}/matches/${encodeURIComponent(matchId)}/reference`, {
    method: 'PUT', body: JSON.stringify(commandBody('tournament-reference', { expectedVersion, recordedMatchId, reason })),
  }),
  complete: (id: string, expectedVersion: number, reason: string) => platformRequest<TournamentWriteResult>(`/api/tournaments/${encodeURIComponent(id)}/complete`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-complete', { expectedVersion, reason })),
  }),
  approvals: (id: string, status = 'requested') => platformRequest<AdminApproval[]>(`/api/tournaments/${encodeURIComponent(id)}/approvals?status=${encodeURIComponent(status)}`),
  reviewApproval: (commandId: string, decision: 'approve' | 'reject', reason: string) => platformRequest<AdminCommand>(`/api/admin/v1/approvals/${encodeURIComponent(commandId)}`, {
    method: 'POST', body: JSON.stringify({ decision, reason }),
  }),
}

export const friendApi = {
  presence: () => platformRequest<PlatformPresence[]>('/api/presence'),
  players: (search = '') => platformRequest<PlatformFriend[]>(`/api/players${search ? `?search=${encodeURIComponent(search)}` : ''}`),
  friends: () => platformRequest<PlatformFriend[]>('/api/friends'),
  requests: () => platformRequest<PlatformFriend[]>('/api/friends/requests'),
  request: (accountId: string) => platformRequest<{ message: string }>('/api/friends/requests', {
    method: 'POST', body: JSON.stringify({ accountId }),
  }),
  resolve: (requesterId: string, accept: boolean) => platformRequest<{ message: string }>(`/api/friends/requests/${encodeURIComponent(requesterId)}/resolve`, {
    method: 'POST', body: JSON.stringify({ accept }),
  }),
  remove: (accountId: string) => platformRequest<void>(`/api/friends/${encodeURIComponent(accountId)}`, { method: 'DELETE' }),
}

export const publicDeckApi = {
  list: () => platformRequest<PublishedDeck[]>('/api/public-decks'),
  publish: (deck: SavedL12Deck, publicationId?: string) => platformRequest<PublishedDeck>('/api/public-decks', {
    method: 'POST', body: JSON.stringify({ publicationId: publicationId || null, deck }),
  }),
  delete: (id: string) => platformRequest<void>(`/api/public-decks/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  toggleLike: (id: string) => platformRequest<PublishedDeck>(`/api/public-decks/${encodeURIComponent(id)}/like`, { method: 'POST' }),
  recordCopy: (id: string) => platformRequest<PublishedDeck>(`/api/public-decks/${encodeURIComponent(id)}/copy`, { method: 'POST' }),
}
