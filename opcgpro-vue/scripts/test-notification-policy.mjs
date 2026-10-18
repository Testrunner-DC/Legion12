import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'

const sourceUrl = new URL('../src/l12/notifications/realtimeNotificationPolicy.ts', import.meta.url)
const compiled = ts.transpileModule(fs.readFileSync(sourceUrl, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const policy = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`)

const {
  ChatNotificationTracker,
  RealtimeEventLedger,
  RealtimeNotificationRateLimiter,
  notificationOwnerMatches,
  shouldDisplayBrowserNotification,
} = policy

const ledger = new RealtimeEventLedger()
assert.equal(ledger.remember('account-a', 'match_found', 'match-1'), true)
assert.equal(ledger.remember('account-a', 'match_found', 'match-1'), false, 'duplicate packet must not notify')
assert.equal(ledger.remember('account-b', 'match_found', 'match-1'), true, 'dedupe must be account-bound')
assert.equal(ledger.remember('account-a', 'match_found', 'match-1'), false, 'account ABA must retain the A ledger')
ledger.baseline('account-a', 'friend_request', ['old-request'])
assert.equal(ledger.remember('account-a', 'friend_request', 'old-request'), false, 'initial snapshot must be a baseline')
assert.equal(ledger.remember('account-a', 'friend_request', 'new-request'), true)
assert.equal(ledger.rememberBatch('account-a', 'friend_request', ['batch-1', 'batch-2', 'batch-3']), true)
assert.equal(ledger.remember('account-a', 'friend_request', 'batch-1'), false)
assert.equal(ledger.remember('account-a', 'friend_request', 'batch-2'), false, 'the whole batch must be recorded despite the first new key')
assert.equal(ledger.remember('account-a', 'friend_request', 'batch-3'), false)

const owner = { accountId: 'account-a', sessionEpoch: 3, connectionGeneration: 7 }
assert.equal(notificationOwnerMatches(owner, owner), true)
assert.equal(notificationOwnerMatches(owner, { accountId: 'account-b', sessionEpoch: 4, connectionGeneration: 7 }), false, 'account switch invalidates an old click')
assert.equal(notificationOwnerMatches(owner, { accountId: 'account-a', sessionEpoch: 5, connectionGeneration: 7 }), false, 'account ABA invalidates an old click even if a server generation repeats')
assert.equal(notificationOwnerMatches(owner, { accountId: 'account-a', sessionEpoch: 3, connectionGeneration: 9 }), false, 'reconnect invalidates an old click')

const entry = (id, seq, incoming = true) => ({ id, seq, incoming })
const thread = ({
  key = 'friend-thread', instanceId = 'thread-1', scope = 'friend', unread = 0, readSeq = 0, entries = [],
} = {}) => ({ key, instanceId, scope, unread, readSeq, entries })

const chatLedger = new RealtimeEventLedger()
const chat = new ChatNotificationTracker(chatLedger)
const old = entry('old-history', 1)
chat.beginSession('account-a', [thread({ unread: 1, entries: [old] })])
assert.deepEqual(chat.observe('account-a', [thread({ unread: 1, entries: [old] })]), [], 'first snapshot must stay silent')

const olderHistory = entry('older-history', 2)
assert.deepEqual(chat.observe('account-a', [thread({ unread: 1, entries: [old, olderHistory] })]), [], 'history append without unread growth must stay silent')

assert.deepEqual(chat.observe('account-a', [thread({ unread: 2, entries: [old, olderHistory] })]), [], 'unread mutation precedes the live entry')
const live = entry('live-1', 3)
assert.deepEqual(chat.observe('account-a', [thread({ unread: 2, entries: [old, olderHistory, live] })]), [
  { id: 'live-1', threadKey: 'friend-thread', scope: 'friend' },
])
assert.deepEqual(chat.observe('account-a', [thread({ unread: 2, entries: [old, olderHistory, live] })]), [], 'same message and unread replay must not repeat')

assert.deepEqual(chat.observe('account-a', [thread({ unread: 0, readSeq: 3, entries: [old, olderHistory, live] })]), [], 'read acknowledgement must stay silent')
const postReadHistory = entry('post-read-history', 4)
assert.deepEqual(chat.observe('account-a', [thread({ unread: 0, readSeq: 3, entries: [old, olderHistory, live, postReadHistory] })]), [], 'history above readSeq is not live without unread growth')

assert.deepEqual(chat.observe('account-a', [thread({ unread: 1, readSeq: 3, entries: [old, olderHistory, live, postReadHistory] })]), [])
const liveAfterHistory = entry('live-2', 5)
assert.deepEqual(chat.observe('account-a', [thread({ unread: 1, readSeq: 3, entries: [old, olderHistory, live, postReadHistory, liveAfterHistory] })]).map(item => item.id), ['live-2'])

const reconstructed = thread({ instanceId: 'thread-2', unread: 1, readSeq: 3, entries: [old, olderHistory, live, postReadHistory, liveAfterHistory, entry('restored-history', 6)] })
assert.deepEqual(chat.observe('account-a', [reconstructed]), [], 'thread reconstruction/history replay must become a baseline')
chat.beginSession('account-b', [reconstructed])
assert.deepEqual(chat.observe('account-b', [reconstructed]), [], 'account switch must baseline the new owner')
chat.beginSession('account-a', [reconstructed])
assert.deepEqual(chat.observe('account-a', [reconstructed]), [], 'account ABA must not replay restored entries')

const existing = reconstructed
const emptyOpponent = thread({ key: 'opponent-thread', instanceId: 'thread-opponent', scope: 'opponent' })
assert.deepEqual(chat.observe('account-a', [existing, emptyOpponent]), [], 'new empty thread is only initialized')
assert.deepEqual(chat.observe('account-a', [existing, thread({ ...emptyOpponent, unread: 1 })]), [], 'new live unread arrives before its entry')
const opponentLive = thread({ ...emptyOpponent, unread: 1, entries: [entry('opponent-live', 1)] })
assert.deepEqual(chat.observe('account-a', [existing, opponentLive]), [
  { id: 'opponent-live', threadKey: 'opponent-thread', scope: 'opponent' },
])
assert.deepEqual(chat.observe('account-a', [existing, opponentLive]), [], 'opponent duplicate must stay silent')

const windowEntries = Array.from({ length: 200 }, (_, index) => entry(`window-${index}`, index + 100))
const boundedChat = new ChatNotificationTracker(new RealtimeEventLedger())
const boundedThread = thread({ key: 'bounded', instanceId: 'bounded-1', unread: 0, entries: windowEntries })
boundedChat.beginSession('account-a', [boundedThread])
assert.deepEqual(boundedChat.observe('account-a', [thread({ ...boundedThread, unread: 1 })]), [], 'an unread increment is paired with the next entry replacement')
assert.deepEqual(boundedChat.observe('account-a', [thread({ ...boundedThread, unread: 1, entries: [...windowEntries] })]), [], 'a replay outside the current bounded window must clear its unmatched live budget')
const backfilled = entry('old-backfill', 1)
assert.deepEqual(boundedChat.observe('account-a', [thread({ ...boundedThread, unread: 1, entries: [backfilled, ...windowEntries] })]), [], 'history backfill must never consume a stale live budget')
assert.deepEqual(boundedChat.observe('account-a', [thread({ ...boundedThread, unread: 2, entries: [backfilled, ...windowEntries] })]), [])
const newest = entry('bounded-live', 1_000)
assert.deepEqual(boundedChat.observe('account-a', [thread({ ...boundedThread, unread: 2, entries: [backfilled, ...windowEntries.slice(1), newest] })]).map(item => item.id), ['bounded-live'])

const longChat = new ChatNotificationTracker(new RealtimeEventLedger())
let longWindow = []
let longUnread = 0
const longThread = (entries = longWindow) => thread({ key: 'long-running', instanceId: 'long-1', unread: longUnread, entries })
longChat.beginSession('account-a', [longThread()])
for (let seq = 1; seq <= 600; seq += 1) {
  longUnread += 1
  assert.deepEqual(longChat.observe('account-a', [longThread()]), [])
  longWindow = [...longWindow, entry(`long-live-${seq}`, seq)].slice(-200)
  assert.deepEqual(longChat.observe('account-a', [longThread()]).map(item => item.id), [`long-live-${seq}`])
}
assert.equal(longWindow.length, 200, 'the real chat resource keeps only its current 200-entry window')
assert.equal(longWindow[0].seq, 401)
longUnread += 1
assert.deepEqual(longChat.observe('account-a', [longThread()]), [])
const replayOutsideWindow = [...longWindow, entry('long-live-1', 1)].sort((left, right) => left.seq - right.seq).slice(-200)
assert.deepEqual(replayOutsideWindow, longWindow, 'chatResource merge drops the old replay from its bounded window')
assert.deepEqual(longChat.observe('account-a', [longThread(replayOutsideWindow)]), [], 'a forgotten seq=1 live replay cannot notify after both 512-entry dedupe bounds roll over')
longUnread += 1
assert.deepEqual(longChat.observe('account-a', [longThread()]), [])
longWindow = [...longWindow, entry('long-live-601', 601)].slice(-200)
assert.deepEqual(longChat.observe('account-a', [longThread()]).map(item => item.id), ['long-live-601'], 'clearing the unmatched replay budget must not hide the next genuine live entry')

const limiter = new RealtimeNotificationRateLimiter()
assert.equal(limiter.allow('sound', 'match_found', 1_000), true)
assert.equal(limiter.allow('sound', 'friend_request', 1_100), false, 'global sound burst must collapse')
assert.equal(limiter.allow('sound', 'friend_request', 1_400), true)
assert.equal(limiter.allow('sound', 'match_found', 1_500), false, 'per-kind sound burst must collapse')
assert.equal(limiter.allow('sound', 'match_found', 1_900), true)
assert.equal(limiter.allow('desktop', 'chat_message', 2_000), true)
assert.equal(limiter.allow('desktop', 'friend_challenge', 2_500), false, 'global desktop burst must collapse')
assert.equal(limiter.allow('desktop', 'chat_message', 100), true, 'clock rollback must not lock notifications forever')

const eligible = { supported: true, secure: true, enabled: true, permission: 'granted', hidden: true, focused: false }
assert.equal(shouldDisplayBrowserNotification(eligible), true)
assert.equal(shouldDisplayBrowserNotification({ ...eligible, hidden: false, focused: true }), false, 'foreground desktop notification must stay quiet')
assert.equal(shouldDisplayBrowserNotification({ ...eligible, enabled: false }), false, 'explicit opt-in is required')
assert.equal(shouldDisplayBrowserNotification({ ...eligible, permission: 'default' }), false, 'default permission is not consent')
assert.equal(shouldDisplayBrowserNotification({ ...eligible, permission: 'denied' }), false)
assert.equal(shouldDisplayBrowserNotification({ ...eligible, supported: false }), false)
assert.equal(shouldDisplayBrowserNotification({ ...eligible, secure: false }), false)

console.log('notification policy: PASS')
