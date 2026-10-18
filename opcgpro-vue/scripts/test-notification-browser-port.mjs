import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'

function transpile(url) {
  return ts.transpileModule(fs.readFileSync(url, 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  }).outputText
}

const policySource = transpile(new URL('../src/l12/notifications/realtimeNotificationPolicy.ts', import.meta.url))
globalThis.__l12NotificationPolicy = await import(`data:text/javascript;base64,${Buffer.from(policySource).toString('base64')}`)

let browserSource = transpile(new URL('../src/l12/notifications/browserNotifications.ts', import.meta.url))
browserSource = browserSource
  .replace(/import\s+\{\s*reactive\s*\}\s+from\s+['"]vue['"];?/, 'const reactive = value => value;')
  .replace(/import\s+\{\s*shouldDisplayBrowserNotification\s*\}\s+from\s+['"]\.\/realtimeNotificationPolicy['"];?/, 'const { shouldDisplayBrowserNotification } = globalThis.__l12NotificationPolicy;')
assert.doesNotMatch(browserSource, /^import\s/m, 'browser module test port must replace every runtime import')

const stored = new Map()
const notifications = []
let permissionRequests = 0
let requestMode = 'pending'
let pendingPermission = null
let focused = true
let focusCalls = 0

class MockNotification {
  static permission = 'default'
  static throwOnConstruct = false

  static requestPermission() {
    permissionRequests += 1
    if (requestMode === 'throw') return Promise.reject(new Error('permission port failure'))
    if (requestMode === 'pending') {
      return new Promise(resolve => {
        pendingPermission = permission => {
          MockNotification.permission = permission
          resolve(permission)
        }
      })
    }
    MockNotification.permission = requestMode
    return Promise.resolve(requestMode)
  }

  constructor(title, options) {
    if (MockNotification.throwOnConstruct) throw new Error('constructor port failure')
    this.title = title
    this.options = options
    this.closed = false
    this.onclick = null
    notifications.push(this)
  }

  close() { this.closed = true }
}

globalThis.window = {
  Notification: MockNotification,
  isSecureContext: true,
  localStorage: {
    getItem: key => stored.get(key) ?? null,
    setItem: (key, value) => stored.set(key, value),
    removeItem: key => stored.delete(key),
  },
  focus: () => { focusCalls += 1 },
}
globalThis.document = {
  hidden: false,
  hasFocus: () => focused,
}

const browser = await import(`data:text/javascript;base64,${Buffer.from(browserSource).toString('base64')}`)
const {
  browserNotificationCanDisplayNow,
  browserNotificationState,
  refreshBrowserNotificationState,
  showBrowserNotification,
  toggleBrowserNotificationsFromUserGesture,
} = browser

assert.equal(permissionRequests, 0, 'module import must never request permission')
refreshBrowserNotificationState()
refreshBrowserNotificationState()
assert.equal(permissionRequests, 0, 'state refresh must never request permission')
assert.equal(browserNotificationState.enabled, false, 'desktop notifications default off')
assert.equal(browserNotificationState.status, 'default')

const firstClick = toggleBrowserNotificationsFromUserGesture()
const duplicateClick = toggleBrowserNotificationsFromUserGesture()
assert.equal(permissionRequests, 1, 'an explicit click starts one permission request')
assert.equal(browserNotificationState.requesting, true)
pendingPermission('granted')
await Promise.all([firstClick, duplicateClick])
assert.equal(permissionRequests, 1, 'a concurrent repeated click cannot create another prompt')
assert.equal(browserNotificationState.enabled, true)
assert.equal(stored.get('l12-browser-notifications-v1'), 'enabled')

await toggleBrowserNotificationsFromUserGesture()
assert.equal(browserNotificationState.enabled, false, 'a later explicit click disables the local preference')
assert.equal(permissionRequests, 1, 'disabling never requests permission')

MockNotification.permission = 'default'
requestMode = 'denied'
await toggleBrowserNotificationsFromUserGesture()
assert.equal(permissionRequests, 2)
assert.equal(browserNotificationState.status, 'denied')
assert.equal(browserNotificationState.enabled, false)
await toggleBrowserNotificationsFromUserGesture()
assert.equal(permissionRequests, 2, 'a denied state must not be bypassed or re-prompted')

MockNotification.permission = 'default'
requestMode = 'throw'
refreshBrowserNotificationState()
await assert.doesNotReject(toggleBrowserNotificationsFromUserGesture())
assert.equal(permissionRequests, 3)
assert.equal(browserNotificationState.requesting, false, 'permission failures must release the busy state')
assert.equal(browserNotificationState.enabled, false)

delete window.Notification
refreshBrowserNotificationState()
assert.equal(browserNotificationState.status, 'unsupported')
await toggleBrowserNotificationsFromUserGesture()
assert.equal(permissionRequests, 3, 'unsupported browsers must not request permission')

window.Notification = MockNotification
window.isSecureContext = false
MockNotification.permission = 'default'
requestMode = 'granted'
refreshBrowserNotificationState()
assert.equal(browserNotificationState.status, 'insecure')
await toggleBrowserNotificationsFromUserGesture()
assert.equal(permissionRequests, 3, 'insecure contexts must not request permission')

window.isSecureContext = true
MockNotification.permission = 'granted'
refreshBrowserNotificationState()
await toggleBrowserNotificationsFromUserGesture()
assert.equal(browserNotificationState.enabled, true, 'an already granted permission still needs an explicit local opt-in')
assert.equal(permissionRequests, 3)

document.hidden = false
focused = true
assert.equal(browserNotificationCanDisplayNow(), false)
assert.equal(showBrowserNotification('match_found', () => assert.fail('foreground click callback should not exist')), false)
assert.equal(notifications.length, 0, 'foreground pages use only the existing short cue')

document.hidden = true
const expectedCopy = {
  match_found: ['匹配成功', '对局已准备，请返回网页查看。'],
  friend_request: ['收到好友申请', '请返回网页查看并决定如何处理。'],
  friend_challenge: ['收到好友对战邀请', '请返回网页查看并决定如何处理。'],
  chat_message: ['收到新消息', '请返回网页查看。'],
}
for (const [kind, [title, body]] of Object.entries(expectedCopy)) {
  assert.equal(showBrowserNotification(kind, () => undefined), true)
  const shown = notifications.at(-1)
  assert.equal(shown.title, title)
  assert.equal(shown.options.body, body)
  assert.equal(shown.options.silent, true, 'browser-native sound cannot bypass game SFX settings')
  assert.equal(shown.options.tag, `l12-realtime-${kind}`)
  assert.doesNotMatch(`${shown.title} ${shown.options.body}`, /sender|account|room|deck|token|secret|正文/i)
}
assert.equal(permissionRequests, 3, 'displaying notifications never requests permission')

let clickCallbacks = 0
assert.equal(showBrowserNotification('chat_message', () => { clickCallbacks += 1 }), true)
const clickable = notifications.at(-1)
clickable.onclick()
assert.equal(focusCalls, 1)
assert.equal(clickCallbacks, 1)
assert.equal(clickable.closed, true)

document.hidden = false
focused = false
assert.equal(showBrowserNotification('friend_request', () => undefined), true, 'a visible but unfocused page may show desktop notification')

MockNotification.throwOnConstruct = true
document.hidden = true
assert.equal(showBrowserNotification('match_found', () => undefined), false, 'constructor failures are contained')
MockNotification.throwOnConstruct = false
assert.equal(browserNotificationState.enabled, false, 'rejected delivery must clear the enabled preference')
assert.equal(browserNotificationState.status, 'unsupported')
refreshBrowserNotificationState()
assert.equal(browserNotificationState.status, 'unsupported', 'API presence must not re-enable rejected delivery')
assert.equal(showBrowserNotification('chat_message', () => undefined), false)

MockNotification.permission = 'denied'
refreshBrowserNotificationState()
assert.equal(showBrowserNotification('chat_message', () => undefined), false)

delete globalThis.window
delete globalThis.document
delete globalThis.__l12NotificationPolicy
console.log('notification browser port: PASS')
