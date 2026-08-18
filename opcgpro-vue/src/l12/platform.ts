import { computed, reactive } from 'vue'
import { l12State } from './net'

export interface PlatformAccount { id: string; username: string; role: string; createdAt: string; publicHistory: boolean }
export interface BugReport {
  id: string; reporterName: string; title: string; description: string; page: string; roomCode?: string; matchId?: string
  version: string; status: string; priority: string; assignee?: string; adminNotes?: string; createdAt: string; updatedAt: string
}

function loadAccount(): PlatformAccount | null {
  try { return JSON.parse(localStorage.getItem('l12-account') || 'null') as PlatformAccount | null } catch { return null }
}

export const platformState = reactive({
  token: localStorage.getItem('l12-auth-token') || '',
  account: loadAccount(),
})

export const isAdmin = computed(() => platformState.account?.role === 'admin')

export function apiBase() {
  try {
    const url = new URL(l12State.endpoint)
    url.protocol = url.protocol === 'wss:' ? 'https:' : 'http:'
    url.pathname = ''
    return url.toString().replace(/\/$/, '')
  } catch { return `${location.protocol}//${location.hostname}:8080` }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
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
  const result = await request<{ account: PlatformAccount; token: string }>('/api/auth/register', { method: 'POST', body: JSON.stringify({ username, password }) })
  remember(result.account, result.token)
}

export async function login(username: string, password: string) {
  const result = await request<{ account: PlatformAccount; token: string }>('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) })
  remember(result.account, result.token)
}

export function logout() {
  platformState.account = null
  platformState.token = ''
  localStorage.removeItem('l12-account')
  localStorage.removeItem('l12-auth-token')
}

export async function changePassword(currentPassword: string, newPassword: string) {
  return request<{ message: string }>('/api/auth/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) })
}

export async function submitBug(input: { title: string; description: string; page: string; roomCode?: string; matchId?: string; version: string }) {
  return request<BugReport>('/api/bugs', { method: 'POST', body: JSON.stringify(input) })
}

export async function getPublicContent(key: string) {
  return request<{ key: string; value: string }>(`/api/content/${encodeURIComponent(key)}`)
}

export const adminApi = {
  accounts: () => request<PlatformAccount[]>('/api/admin/accounts'),
  setRole: (id: string, role: string) => request<void>(`/api/admin/accounts/${encodeURIComponent(id)}/role`, { method: 'PUT', body: JSON.stringify({ role }) }),
  bugs: (status = '') => request<BugReport[]>(`/api/admin/bugs${status ? `?status=${encodeURIComponent(status)}` : ''}`),
  updateBug: (id: string, body: Partial<Pick<BugReport, 'status' | 'priority' | 'assignee' | 'adminNotes'>>) => request<BugReport>(`/api/admin/bugs/${encodeURIComponent(id)}`, { method: 'PATCH', body: JSON.stringify(body) }),
  getContent: getPublicContent,
  setContent: (key: string, value: string) => request<void>(`/api/admin/content/${encodeURIComponent(key)}`, { method: 'PUT', body: JSON.stringify({ value }) }),
}
