import { computed, reactive } from 'vue'
import { disconnect, l12State } from './net'
import type { SavedL12Deck } from './decks'

export interface PlatformAccount {
  id: string; username: string; role: string; createdAt: string; publicHistory: boolean; permissions?: string[]
  permissionVersion?: number; disabled?: boolean; disabledAt?: string; disabledReason?: string
  mustChangePassword?: boolean; deleted?: boolean; deletedAt?: string; emailMasked?: string; emailVerified?: boolean
}
export interface RoleCommandResult { accountId: string; role: 'player' | 'admin'; changed: boolean }
export interface AccountStatusOperation { applied: boolean; account: PlatformAccount; revokedSessions: number; alreadyApplied: boolean }
export interface PlatformSession {
  id: string; createdAt: string; expiresAt: string; current: boolean; authStrength: string; permissionVersion: number
}
export interface SessionRevocation { sessionId?: string; revokedCount: number; alreadyRevoked: boolean }
export interface EmailStatus {
  bound: boolean; verified: boolean; maskedEmail?: string; pendingMaskedEmail?: string
  pendingExpiresAt?: string; featureEnabled: boolean; mailConfigured: boolean
}
export interface EmailCapability { enabled: boolean; mailConfigured: boolean }
export interface AuthOperation { code: string; message: string }
export interface AdminPasswordReset { applied: boolean; account: PlatformAccount; revokedSessions: number }
export interface AccountDeletion {
  applied: boolean; account: PlatformAccount; revokedSessions: number; removedPrivateRecords: number; cleanedMatchRecords?: number
}
export interface PlatformFriend {
  accountId: string; username: string; status: 'none' | 'pending' | 'accepted'
  direction: 'none' | 'incoming' | 'outgoing'; createdAt: string; online?: boolean
}
export interface PlatformPresence {
  accountId: string; username: string; online: boolean
  activity: 'idle' | 'inRoom' | 'playing' | 'spectating'; roomCode?: string
  canInvite: boolean; canSpectate: boolean; actionReason?: string
  friendStatus: 'self' | 'none' | 'pending' | 'accepted'; friendDirection: 'none' | 'incoming' | 'outgoing'
}
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
export interface OperationsSeasonConfig { id: string; name: string; status: string; startsAt?: string; endsAt?: string }
export interface OperationsDisasterPoolConfig { cardIds: string[]; annihilationLocked: boolean }
export interface OperationsCardRestriction { cardId: string; maxCopies: number; reason?: string; masterId?: string }
export interface OperationsMatchMode { id: string; name: string; enabled: boolean }
export interface OperationsMaintenanceConfig { enabled: boolean; message: string; startsAt?: string; endsAt?: string }
export interface OperationsDefaultRoomConfig {
  matchModeId: string
  spectating: 'public' | 'friends' | 'disabled'
  handVisibility: 'request' | 'public'
  disasterMode: 'all' | 'random' | 'season' | 'none'
}
export interface OperationsConfigPayload {
  season: OperationsSeasonConfig
  disasterPool: OperationsDisasterPoolConfig
  cardRestrictions: OperationsCardRestriction[]
  defaultPresetDeckIds: string[]
  matchModes: OperationsMatchMode[]
  defaultRoomConfig: OperationsDefaultRoomConfig
  featureFlags: Record<string, boolean>
  maintenance: OperationsMaintenanceConfig
}
export interface EffectiveOperationsPolicy {
  version: number
  season: OperationsSeasonConfig
  disasterCardIds: string[]
  matchModes: OperationsMatchMode[]
  defaultRoomConfig: OperationsDefaultRoomConfig
  seasonDisasterModeAvailable: boolean
  cardRestrictions: OperationsCardRestriction[]
  defaultPresetDeckIds: string[]
  maintenance: { active: boolean; message: string; startsAt?: string; endsAt?: string }
}
export interface RankedTierConfig { name: string; minimum: number; baseDelta: number; winStreakCap: number; lossProtectionCap: number; ratingGapCap: number; color: string; icon: string }
export interface RankedFactionConfig { id: 'order' | 'chaos' | 'fate'; name: string; color: string; icon: string; firstTitle: string; topFiveTitle: string; tiers: RankedTierConfig[] }
export interface RankedConfig { placementMatches: number; placementMaximum: number; broadcastEnabled: boolean; factions: RankedFactionConfig[] }
export interface RankedProfile { accountId: string; username: string; seasonId: string; faction?: string; sevenValue: number; displayValue: string; placementPlayed: number; placementWins: number; placed: boolean; wins: number; losses: number; winStreak: number; lossStreak: number; tier: string; tierIndex: number; factionRank: number; title?: string }
export interface RankedProfileHistory { seasonId: string; faction: string; sevenValue: number; placementPlayed: number; placementWins: number; wins: number; losses: number; winStreak: number; archivedAt: string }
export interface RankedOverview { profile: RankedProfile; factionTotals: Record<string, number>; config: RankedConfig; history: RankedProfileHistory[] }
export interface RankedSettlementComponent { kind: string; label: string; value: number }
export interface RankedSettlement { matchId: string; accountId: string; faction: string; won: boolean; placement: boolean; placementPlayed: number; placementRequired: number; before: number; after: number; delta: number; tierBefore: string; tierAfter: string; components: RankedSettlementComponent[]; settledAt: string }
export interface RankedBroadcast { id: string; matchId: string; eventType: string; message: string; createdAt: string }
export interface RankedLeaderboardEntry { rank: number; username: string; faction: string; sevenValue: number; displayValue: string; tier: string; title?: string; wins: number; losses: number; winStreak: number }
export interface OperationsConfigView {
  version: number; versionId: string; config: OperationsConfigPayload; updatedBy: string; updatedAt: string
}
export interface OperationsConfigVersion {
  id: string; version: number; action: string; config: OperationsConfigPayload
  actorId: string; actorName: string; reason: string; createdAt: string
}
export interface OperationsConfigPreview {
  valid: boolean; currentVersion: number; nextVersion: number; normalized: OperationsConfigPayload
  changes: string[]; warnings: string[]
}
export interface OperationsConfigOperation {
  applied: boolean; current: OperationsConfigView; historyEntry: OperationsConfigVersion; changes: string[]
}
export interface RuntimeDependencyStatus {
  name: string; configured: boolean; state: string; detail?: string; observedAt: string
}
export interface RuntimeStatus {
  observedAt: string; serviceVersion: string; cardCount: number; onlineAccountCount: number
  webSocketConnectionCount: number; roomCount: number; activeGameCount: number
  releaseEnvironments: ReleaseEnvironment[]; cdn: RuntimeDependencyStatus
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
  disasterCardIds: string[]; cardRestrictions: OperationsCardRestriction[]
  deckVisibility: TournamentDeckVisibility; hash: string; capturedAt: string
}
export interface TournamentDeckSnapshot {
  name: string; code?: string; hash: string; submittedAt: string; lockedAt?: string
  masterId: string; cardIds: string[]; moraleIds: string[]; specialIds: string[]
}
export interface TournamentStaff { accountId: string; username: string }
export interface TournamentParticipant {
  accountId: string; username: string; checkedIn: boolean; dropped: boolean; eliminated: boolean; seed: number
  deck?: TournamentDeckSnapshot
}
export interface TournamentStanding {
  roundNumber: number; rank: number; accountId: string; username: string; wins: number; losses: number
  draws: number; byes: number; opponentScore: number; opponentsOpponentScore: number; seed: number
}
export interface TournamentMatchEvent {
  id: string; kind: string; result?: string; recordedMatchId?: string; actorId: string; detail: string; createdAt: string
}
export interface TournamentRuling {
  id: string; matchId: string; kind: string; targetAccountId?: string; decision: string; minutes: number
  reason: string; actorId: string; actorName: string; createdAt: string
}
export interface TournamentMatch {
  id: string; table: number; playerAAccountId: string; playerAName: string; playerBAccountId?: string
  playerBName: string; roomCode: string; readyA: boolean; readyB: boolean; status: 'waiting' | 'running' | 'completed'
  result?: string; timeExtensionMinutes: number; startedAt?: string; deadline?: string; recordedMatchId?: string
  rulings: TournamentRuling[]; graceDeadline?: string; sourceMatchIds: string[]; rulesHash: string
  playerADeckHash?: string; playerBDeckHash?: string; replayNumber: number; canEnter: boolean; canSpectate: boolean
  events: TournamentMatchEvent[]
}
export interface TournamentRound {
  id: string; number: number; status: TournamentRoundStatus; paused: boolean; startedAt?: string; pausedAt?: string
  totalPausedSeconds: number; matches: TournamentMatch[]; stage: 'swiss' | 'elimination'; standingsCapturedAt?: string
  standings: TournamentStanding[]; pairingFailure?: string
}
export interface TournamentBracketMatch {
  id: string; table: number; playerAAccountId: string; playerAName: string; playerBAccountId?: string
  playerBName: string; result?: string; sourceMatchIds: string[]
}
export interface TournamentBracketRound { number: number; matches: TournamentBracketMatch[] }
export interface Tournament {
  id: string; code: string; name: string; organizerAccountId: string; organizerName: string; referees: TournamentStaff[]
  status: TournamentStatus; format: 'single' | 'swiss' | 'swiss-cut' | 'league'; visibility: 'public' | 'code'; maxPlayers: number
  startAt?: string; description: string; rules: TournamentRulesSnapshot; roundMinutes: number; checkInMinutes: number
  participants: TournamentParticipant[]; rounds: TournamentRound[]; version: number; legacyImported: boolean
  createdAt: string; updatedAt: string; completedAt?: string; swissRounds: number; cutSize?: number
  registrationVisibility: 'public' | 'staff'; lateGraceMinutes: number
  finalSwissStandings: TournamentStanding[]; eliminationBracket: TournamentBracketRound[]
}
export interface TournamentList { platformVersion: number; items: Tournament[] }
export interface TournamentCreateInput {
  name: string; format: Tournament['format']; visibility: Tournament['visibility']; maxPlayers: number; startAt?: string
  ruleset: string; description: string; deckVisibility: TournamentDeckVisibility; disasterMode: TournamentDisasterMode
  banList: string; disasterCardIds: string[]; cardRestrictions: OperationsCardRestriction[]
  roundMinutes: number; checkInMinutes: number; refereeAccountIds: string[]; swissRounds: number; cutSize?: number
  registrationVisibility: 'public' | 'staff'; lateGraceMinutes: number
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

export const authState = reactive({
  initialized: false,
  verified: false,
  refreshing: false,
})

let authRefreshPromise: Promise<PlatformAccount | null> | null = null

export function hasPermission(permission: string) {
  if (!authState.verified) return false
  const account = platformState.account
  if (!account) return false
  return account.permissions?.includes(permission) === true
}

export const isAdmin = computed(() => authState.verified && platformState.account?.role === 'admin')
export const canAccessAdmin = computed(() => authState.verified && platformState.account?.role === 'admin')

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
  // 登录和注册是匿名凭据交换；不能让旧会话的迟到 401 清掉一次新的登录。
  const anonymousCredentialRequest = path === '/api/auth/login' || path === '/api/auth/register'
    || path === '/api/auth/email/capability'
    || path === '/api/auth/email/verify' || path === '/api/auth/password/forgot'
    || path === '/api/auth/password/reset'
  const requestToken = anonymousCredentialRequest ? '' : platformState.token
  if (requestToken) headers.set('Authorization', `Bearer ${requestToken}`)
  const response = await fetch(`${apiBase()}${path}`, { ...init, headers })
  const payload = await response.json().catch(() => ({}))
  if (!response.ok) {
    const correlationId = String(payload.correlationId || response.headers.get('X-Correlation-ID') || '')
    const message = `${payload.message || `请求失败（${response.status}）`}${correlationId ? `（关联 ID：${correlationId}）` : ''}`
    // 某些旧端点返回无 JSON body 的裸 401；只要本次确实携带当前 token，就必须失效本机会话。
    if (response.status === 401 && requestToken && platformState.token === requestToken) forgetAccount(requestToken)
    // 403 代表会话仍可能有效但权限已变化。立即让权限 UI 失败关闭，并去重刷新权威账号。
    if (response.status === 403 && requestToken && platformState.token === requestToken && path !== '/api/auth/me') {
      authState.verified = false
      void refreshCurrentAccount({ force: true }).catch(() => undefined)
    }
    throw new PlatformRequestError(message, response.status, String(payload.code || 'request_failed'), correlationId)
  }
  return payload as T
}

function remember(account: PlatformAccount, token: string) {
  platformState.account = account
  platformState.token = token
  authState.initialized = true
  authState.verified = true
  authState.refreshing = false
  localStorage.setItem('l12-account', JSON.stringify(account))
  localStorage.setItem('l12-auth-token', token)
  l12State.nickname = account.username
  localStorage.setItem('l12-nickname', account.username)
}

export function refreshCurrentAccount(options: { force?: boolean } = {}): Promise<PlatformAccount | null> {
  if (authRefreshPromise) return authRefreshPromise
  if (!platformState.token) {
    forgetAccount()
    return Promise.resolve(null)
  }
  if (!options.force && authState.verified) return Promise.resolve(platformState.account)

  const requestToken = platformState.token
  authState.refreshing = true
  authState.verified = false
  const pending = (async () => {
    try {
      const account = await platformRequest<PlatformAccount>('/api/auth/me')
      if (platformState.token !== requestToken) return platformState.account
      remember(account, requestToken)
      return account
    } catch (error) {
      if (platformState.token === requestToken) {
        // 网络与 5xx 不销毁可重试的 token，但绝不能继续把缓存身份当成已验证权限。
        authState.initialized = true
        authState.verified = false
      }
      if (error instanceof PlatformRequestError && error.status === 401)
        return platformState.token === requestToken ? null : platformState.account
      throw error
    } finally {
      if (platformState.token === requestToken) authState.refreshing = false
      authRefreshPromise = null
    }
  })()
  authRefreshPromise = pending
  return pending
}

export function initializeAuth() {
  if (authState.initialized) return Promise.resolve(platformState.account)
  return refreshCurrentAccount({ force: true })
}

export async function register(username: string, password: string) {
  const result = await platformRequest<{ account: PlatformAccount; token: string }>('/api/auth/register', { method: 'POST', body: JSON.stringify({ username, password }) })
  remember(result.account, result.token)
}

export async function login(username: string, password: string) {
  const result = await platformRequest<{ account: PlatformAccount; token: string }>('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) })
  remember(result.account, result.token)
}

function forgetAccount(expectedToken?: string) {
  if (expectedToken !== undefined && platformState.token !== expectedToken) return
  disconnect()
  platformState.account = null
  platformState.token = ''
  authState.initialized = true
  authState.verified = false
  authState.refreshing = false
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
  const result = await platformRequest<{ message: string }>('/api/auth/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) })
  await refreshCurrentAccount({ force: true })
  return result
}

export const emailApi = {
  capability: () => platformRequest<EmailCapability>('/api/auth/email/capability'),
  status: () => platformRequest<EmailStatus>('/api/auth/email'),
  bind: (email: string, currentPassword: string) => platformRequest<AuthOperation>('/api/auth/email/bind', {
    method: 'POST', body: JSON.stringify({ email, currentPassword }),
  }),
  verify: (token: string) => platformRequest<AuthOperation>('/api/auth/email/verify', {
    method: 'POST', body: JSON.stringify({ token }),
  }),
  unbind: (currentPassword: string) => platformRequest<AuthOperation>('/api/auth/email/unbind', {
    method: 'POST', body: JSON.stringify({ currentPassword }),
  }),
}

export const requestPasswordReset = (email: string) => platformRequest<AuthOperation>('/api/auth/password/forgot', {
  method: 'POST', body: JSON.stringify({ email }),
})

export const resetPassword = (token: string, newPassword: string) => platformRequest<AuthOperation>('/api/auth/password/reset', {
  method: 'POST', body: JSON.stringify({ token, newPassword }),
})

export async function submitBug(input: { title: string; description: string; page: string; roomCode?: string; matchId?: string; version: string }) {
  return platformRequest<BugReport>('/api/bugs', { method: 'POST', body: JSON.stringify(input) })
}

export async function getPublicContent(key: string) {
  return platformRequest<{ key: string; value: string }>(`/api/content/${encodeURIComponent(key)}`)
}

export async function getPublicContentBatch(keys: string[]) {
  const params = new URLSearchParams()
  keys.forEach(key => params.append('key', key))
  return platformRequest<{ values: Record<string, string> }>(`/api/content?${params}`)
}

export const getEffectiveOperationsPolicy = () =>
  platformRequest<EffectiveOperationsPolicy>('/api/operations/effective-policy')

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
  setRole: (id: string, role: 'player' | 'admin', expectedVersion?: number) => platformRequest<RoleCommandResult>(`/api/admin/accounts/${encodeURIComponent(id)}/role`, { method: 'PUT', body: JSON.stringify(commandBody('role', { role, expectedVersion })) }),
  setAccountStatus: (id: string, disabled: boolean, reason: string, expectedVersion?: number) => platformRequest<AccountStatusOperation>(`/api/admin/accounts/${encodeURIComponent(id)}/status`, {
    method: 'PUT', body: JSON.stringify(commandBody('account-status', { disabled, reason, expectedVersion })),
  }),
  resetAccountPassword: (id: string, reason: string, expectedVersion?: number, dryRun = false) => platformRequest<AdminPasswordReset>(`/api/admin/accounts/${encodeURIComponent(id)}/reset-password`, {
    method: 'POST', body: JSON.stringify(commandBody('account-password-reset', { reason, expectedVersion, dryRun })),
  }),
  deleteAccount: (id: string, reason: string, expectedVersion?: number, dryRun = false) => platformRequest<AccountDeletion>(`/api/admin/accounts/${encodeURIComponent(id)}/delete`, {
    method: 'POST', body: JSON.stringify(commandBody('account-delete', { reason, expectedVersion, dryRun })),
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
  operationsConfig: () => platformRequest<OperationsConfigView>('/api/admin/operations/config'),
  operationsHistory: (limit = 50) => platformRequest<OperationsConfigVersion[]>(`/api/admin/operations/config/history?limit=${Math.max(1, Math.min(200, limit))}`),
  previewOperationsConfig: (config: OperationsConfigPayload, expectedVersion?: number) => platformRequest<OperationsConfigPreview>('/api/admin/operations/config/preview', {
    method: 'POST', body: JSON.stringify({ config, expectedVersion }),
  }),
  applyOperationsConfig: (config: OperationsConfigPayload, reason: string, expectedVersion?: number) => platformRequest<OperationsConfigOperation>('/api/admin/operations/config', {
    method: 'PUT', body: JSON.stringify(commandBody('operations-config', { config, reason, expectedVersion })),
  }),
  rollbackOperationsConfig: (versionId: string, reason: string, expectedVersion?: number) => platformRequest<OperationsConfigOperation>('/api/admin/operations/config/rollback', {
    method: 'POST', body: JSON.stringify(commandBody('operations-rollback', { versionId, reason, expectedVersion })),
  }),
  runtimeStatus: () => platformRequest<RuntimeStatus>('/api/admin/runtime/status'),
  rankedConfig: () => platformRequest<RankedConfig>('/api/admin/ranked/config'),
  saveRankedConfig: (config: RankedConfig, reason: string) => platformRequest<RankedConfig>('/api/admin/ranked/config', { method: 'PUT', body: JSON.stringify({ config, reason }) }),
  deleteRankedBroadcast: (id: string) => platformRequest<void>(`/api/admin/ranked/broadcasts/${encodeURIComponent(id)}`, { method: 'DELETE' }),
}

export const rankedApi = {
  overview: () => platformRequest<RankedOverview>('/api/ranked/me'),
  selectFaction: (faction: 'order' | 'chaos' | 'fate') => platformRequest<RankedProfile>('/api/ranked/faction', { method: 'POST', body: JSON.stringify({ faction }) }),
  leaderboard: (faction = '') => platformRequest<{ players: RankedLeaderboardEntry[]; matches: Array<Record<string, unknown>> }>(`/api/rankings${faction ? `?faction=${encodeURIComponent(faction)}` : ''}`),
  broadcasts: (limit = 30) => platformRequest<RankedBroadcast[]>(`/api/ranked/broadcasts?limit=${limit}`),
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
  setStaff: (id: string, expectedVersion: number, refereeAccountIds: string[], reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/staff`, {
    method: 'PUT', body: JSON.stringify(commandBody('tournament-staff', { expectedVersion, refereeAccountIds, reason })),
  }),
  start: (id: string, expectedVersion: number, reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/start`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-start', { expectedVersion, reason })),
  }),
  nextRound: (id: string, expectedVersion: number, reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/rounds`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-round', { expectedVersion, reason })),
  }),
  checkIn: (id: string, roundNumber: number, expectedVersion: number, accountId: string | undefined, ready: boolean) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/rounds/${roundNumber}/check-in`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-check-in', { expectedVersion, accountId, ready })),
  }),
  startRound: (id: string, roundNumber: number, expectedVersion: number, reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/rounds/${roundNumber}/start`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-round-start', { expectedVersion, reason })),
  }),
  pauseRound: (id: string, roundNumber: number, expectedVersion: number, paused: boolean, reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/rounds/${roundNumber}/pause`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-round-pause', { expectedVersion, paused, reason })),
  }),
  extendMatch: (id: string, matchId: string, expectedVersion: number, minutes: number, reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/matches/${encodeURIComponent(matchId)}/time-extension`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-extension', { expectedVersion, minutes, reason })),
  }),
  ruleMatch: (id: string, matchId: string, expectedVersion: number, body: { kind: 'result' | 'penalty' | 'no-show'; targetAccountId?: string; decision: string; reason: string }) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/matches/${encodeURIComponent(matchId)}/rulings`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-ruling', { expectedVersion, ...body })),
  }),
  rematch: (id: string, matchId: string, expectedVersion: number, reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/matches/${encodeURIComponent(matchId)}/rematch`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-rematch', { expectedVersion, reason })),
  }),
  linkMatch: (id: string, matchId: string, expectedVersion: number, recordedMatchId: string, reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/matches/${encodeURIComponent(matchId)}/reference`, {
    method: 'PUT', body: JSON.stringify(commandBody('tournament-reference', { expectedVersion, recordedMatchId, reason })),
  }),
  complete: (id: string, expectedVersion: number, reason: string) => platformRequest<Tournament>(`/api/tournaments/${encodeURIComponent(id)}/complete`, {
    method: 'POST', body: JSON.stringify(commandBody('tournament-complete', { expectedVersion, reason })),
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
