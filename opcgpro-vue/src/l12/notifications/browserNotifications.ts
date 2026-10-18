import { reactive } from 'vue'
import { shouldDisplayBrowserNotification, type RealtimeNotificationKind } from './realtimeNotificationPolicy'

export type BrowserNotificationStatus = 'unsupported' | 'insecure' | NotificationPermission

const PREFERENCE_KEY = 'l12-browser-notifications-v1'
const COPY: Record<RealtimeNotificationKind, { title: string; body: string }> = {
  match_found: { title: '匹配成功', body: '对局已准备，请返回网页查看。' },
  friend_request: { title: '收到好友申请', body: '请返回网页查看并决定如何处理。' },
  friend_challenge: { title: '收到好友对战邀请', body: '请返回网页查看并决定如何处理。' },
  chat_message: { title: '收到新消息', body: '请返回网页查看。' },
}

function storedPreference() {
  if (typeof window === 'undefined') return false
  try { return window.localStorage.getItem(PREFERENCE_KEY) === 'enabled' }
  catch { return false }
}

let displayUnavailable = false

function capability() {
  const supported = !displayUnavailable && typeof window !== 'undefined' && 'Notification' in window
  const secure = typeof window !== 'undefined' && window.isSecureContext === true
  const permission: NotificationPermission = supported ? window.Notification.permission : 'default'
  const status: BrowserNotificationStatus = !supported ? 'unsupported' : !secure ? 'insecure' : permission
  return { supported, secure, permission, status }
}

const initial = capability()
export const browserNotificationState = reactive({
  enabled: storedPreference(),
  requesting: false,
  ...initial,
})

function savePreference(enabled: boolean) {
  browserNotificationState.enabled = enabled
  if (typeof window === 'undefined') return
  try {
    if (enabled) window.localStorage.setItem(PREFERENCE_KEY, 'enabled')
    else window.localStorage.removeItem(PREFERENCE_KEY)
  } catch { /* A blocked storage area keeps the setting for this page lifetime only. */ }
}

export function refreshBrowserNotificationState() {
  Object.assign(browserNotificationState, capability())
}

/** The only permission request path. Call directly from a user's click or key activation. */
export async function toggleBrowserNotificationsFromUserGesture() {
  refreshBrowserNotificationState()
  if (browserNotificationState.requesting) return
  if (browserNotificationState.enabled) { savePreference(false); return }
  if (!browserNotificationState.supported || !browserNotificationState.secure
    || browserNotificationState.permission === 'denied') return

  browserNotificationState.requesting = true
  try {
    if (browserNotificationState.permission === 'default') await window.Notification.requestPermission()
    refreshBrowserNotificationState()
    if (browserNotificationState.permission === 'granted') savePreference(true)
  } catch {
    refreshBrowserNotificationState()
  } finally {
    browserNotificationState.requesting = false
  }
}

export function browserNotificationCanDisplayNow() {
  refreshBrowserNotificationState()
  return shouldDisplayBrowserNotification({
    supported: browserNotificationState.supported,
    secure: browserNotificationState.secure,
    enabled: browserNotificationState.enabled,
    permission: browserNotificationState.permission,
    hidden: typeof document !== 'undefined' && document.hidden,
    focused: typeof document !== 'undefined' && typeof document.hasFocus === 'function' && document.hasFocus(),
  })
}

export function showBrowserNotification(kind: RealtimeNotificationKind, onClick: () => void) {
  if (!browserNotificationCanDisplayNow() || typeof window === 'undefined') return false
  const copy = COPY[kind]
  try {
    const options: NotificationOptions & { renotify?: boolean } = {
      body: copy.body,
      tag: `l12-realtime-${kind}`,
      renotify: false,
      silent: true,
    }
    const notification = new window.Notification(copy.title, options)
    notification.onclick = () => {
      window.focus()
      try { onClick() } finally { notification.close() }
    }
    return true
  } catch {
    // An exposed API is not proof that this browser can deliver page notifications.
    displayUnavailable = true
    savePreference(false)
    refreshBrowserNotificationState()
    return false
  }
}
