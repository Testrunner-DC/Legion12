import { computed, reactive } from 'vue'
import { disconnect, l12State } from './net'

export interface PlatformAccount { id: string; username: string; role: string; createdAt: string; publicHistory: boolean }
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
export interface ContentEntry { key: string; draftValue: string; publishedValue: string; status: 'draft' | 'published'; updatedBy?: string; updatedAt?: string; publishedBy?: string; publishedAt?: string }
export interface EffectReview { cardId: string; abilityId?: string; status: 'unreviewed' | 'human-assisted' | 'confirmed' | 'rejected'; note: string; reviewer: string; updatedAt: string }
export interface AdminAudit { id: string; actorName: string; category: string; action: string; target: string; fromValue?: string; toValue?: string; comment?: string; createdAt: string }

function loadAccount(): PlatformAccount | null {
  try { return JSON.parse(localStorage.getItem('l12-account') || 'null') as PlatformAccount | null } catch { return null }
}

export const platformState = reactive({
  token: localStorage.getItem('l12-auth-token') || '',
  account: loadAccount(),
})

export const isAdmin = computed(() => platformState.account?.role === 'admin')
export const canAccessAdmin = computed(() => platformState.account?.role === 'admin' || platformState.account?.role === 'editor')

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
  if (platformState.token) headers.set('Authorization', `Bearer ${platformState.token}`)
  const response = await fetch(`${apiBase()}${path}`, { ...init, headers })
  const payload = await response.json().catch(() => ({}))
  if (!response.ok) throw new Error(payload.message || `请求失败（${response.status}）`)
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

export function logout() {
  disconnect()
  platformState.account = null
  platformState.token = ''
  localStorage.removeItem('l12-account')
  localStorage.removeItem('l12-auth-token')
  l12State.nickname = ''
  localStorage.removeItem('l12-nickname')
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

export const adminApi = {
  accounts: () => platformRequest<PlatformAccount[]>('/api/admin/accounts'),
  setRole: (id: string, role: string) => platformRequest<void>(`/api/admin/accounts/${encodeURIComponent(id)}/role`, { method: 'PUT', body: JSON.stringify({ role }) }),
  bugs: (query: { status?: string; priority?: string; assignee?: string; search?: string } = {}) => {
    const params = new URLSearchParams()
    Object.entries(query).forEach(([key, value]) => { if (value) params.set(key, value) })
    return platformRequest<BugReport[]>(`/api/admin/bugs${params.size ? `?${params}` : ''}`)
  },
  updateBug: (id: string, body: Partial<Pick<BugReport, 'status' | 'priority' | 'assignee' | 'adminNotes'>> & { comment?: string }) => platformRequest<BugReport>(`/api/admin/bugs/${encodeURIComponent(id)}`, { method: 'PATCH', body: JSON.stringify(body) }),
  getContent: (key: string) => platformRequest<ContentEntry>(`/api/admin/content/${encodeURIComponent(key)}`),
  saveContentDraft: (key: string, value: string) => platformRequest<ContentEntry>(`/api/admin/content/${encodeURIComponent(key)}/draft`, { method: 'PUT', body: JSON.stringify({ value }) }),
  publishContent: (key: string) => platformRequest<ContentEntry>(`/api/admin/content/${encodeURIComponent(key)}/publish`, { method: 'POST' }),
  effectAtoms: () => platformRequest<EffectAtomDescriptor[]>('/api/admin/effect-atoms'),
  effectCoverage: () => platformRequest<AtomicCoverage>('/api/admin/effects/coverage'),
  effects: (query: { search?: string; status?: string; product?: string; atomKind?: string; page?: number; pageSize?: number } = {}) => {
    const params = new URLSearchParams()
    Object.entries(query).forEach(([key, value]) => { if (value !== undefined && value !== '') params.set(key, String(value)) })
    return platformRequest<AtomicEffectPage>(`/api/admin/effects${params.size ? `?${params}` : ''}`)
  },
  effect: (cardId: string) => platformRequest<AtomicCardEffect>(`/api/admin/effects/${encodeURIComponent(cardId)}`),
  reviewEffect: (cardId: string, body: { abilityId?: string; status: string; note?: string }) => platformRequest<EffectReview>(`/api/admin/effects/${encodeURIComponent(cardId)}/review`, { method: 'PUT', body: JSON.stringify(body) }),
  audit: (category = '') => platformRequest<AdminAudit[]>(`/api/admin/audit${category ? `?category=${encodeURIComponent(category)}` : ''}`),
}
