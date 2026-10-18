import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const app = read('src/App.vue')
const bridge = read('src/l12/notifications/useRealtimeNotifications.ts')
const browser = read('src/l12/notifications/browserNotifications.ts')
const settings = read('src/l12/site/L12SettingsModal.vue')
const friendRequests = read('src/l12/site/FriendRequestNotifications.vue')
const chat = read('src/l12/chatResource.ts')
const net = read('src/l12/net.ts')

assert.match(app, /import \{ useRealtimeNotifications \} from '@\/l12\/notifications\/useRealtimeNotifications'/)
assert.equal((app.match(/useRealtimeNotifications\(\)/g) ?? []).length, 1, 'global bridge must mount exactly once')
assert.doesNotMatch(friendRequests, /playL12FriendRequestSound|primeL12ActionAudio|const sounded/)
assert.match(friendRequests, /refreshFriendResource\(\)/, 'friend refresh/dialog ownership must remain')

for (const marker of ['matchFound', 'friendInvitation', 'friendResource.requests', 'chatResource.state.threads']) assert.match(bridge, new RegExp(marker.replace('.', '\\.')))
assert.match(bridge, /playL12FriendRequestSound\(\)/, 'all short cues must reuse the existing SFX controller')
assert.doesNotMatch(bridge, /setInterval\s*\(|fetch\s*\(|new Audio\s*\(|requestPermission|resolveFriendInvitation|friendApi\./)
assert.match(bridge, /entry\.senderId !== accountId/, 'self-authored chat entries must not notify')
assert.match(bridge, /candidate\.scope === 'opponent'/)
assert.match(bridge, /ledger\.rememberBatch\(accountId, 'friend_request', snapshot\.keys\)/, 'friend batches must be fully registered before one merged signal')
assert.doesNotMatch(bridge, /snapshot\.keys\.some\(/)
assert.match(bridge, /notificationSessionEpoch \+= 1/)
assert.match(bridge, /sessionEpoch: notificationSessionEpoch/)
assert.match(bridge, /connectionGeneration: l12State\.connectionGeneration/)
assert.match(bridge, /notificationOwnerMatches\(owner, current\)/, 'stale notification clicks must not navigate after account ABA')

assert.match(settings, /网页通知/)
assert.match(settings, /权限：/)
assert.match(settings, /仅限此浏览器/)
assert.match(settings, /页面关闭后普通网页不能继续提醒/)
assert.match(settings, /不会显示聊天正文等私人内容/)
assert.match(settings, /不会跨设备同步/)
assert.match(settings, /@click="toggleBrowserNotificationsFromUserGesture\(\)"/)

assert.equal((browser.match(/Notification\.requestPermission\(\)/g) ?? []).length, 1, 'permission may only be requested by the explicit settings action')
assert.match(browser, /if \(browserNotificationState\.permission === 'default'\) await window\.Notification\.requestPermission\(\)/)
assert.match(browser, /silent: true/, 'browser-native sound must not bypass the game SFX controls')
assert.match(browser, /document\.hidden/)
assert.match(browser, /document\.hasFocus/)
assert.match(browser, /window\.focus\(\)/)
assert.doesNotMatch(browser, /fromName|senderName|\.text|roomCode|deck|credential|token/)
for (const text of ['对局已准备，请返回网页查看。', '请返回网页查看并决定如何处理。', '请返回网页查看。']) assert.match(browser, new RegExp(text))

assert.match(chat, /export type ChatScope = 'friend' \| 'opponent'/, 'there is no public/lobby chat scope to amplify')
assert.match(chat, /!entries\.has\(entry\.id\) && incoming && entry\.senderId !== state\.accountId/)
assert.match(chat, /merge\(item, entries as ChatEntry\[\], false\)/, 'history must not raise live unread')
assert.match(net, /\['chatAck', 'chatMessage', 'chatHistory', 'chatReadAck'\]\.includes\(message\.type\)\) chatResource\.receive\(message\)/)

const sourceRoot = path.join(root, 'src')
const sourceFiles = []
const visit = directory => {
  for (const item of fs.readdirSync(directory, { withFileTypes: true })) {
    const absolute = path.join(directory, item.name)
    if (item.isDirectory()) visit(absolute)
    else if (/\.(?:ts|vue)$/.test(item.name)) sourceFiles.push(absolute)
  }
}
visit(sourceRoot)
const permissionOwners = sourceFiles.filter(file => fs.readFileSync(file, 'utf8').includes('Notification.requestPermission()'))
assert.deepEqual(permissionOwners, [path.join(root, 'src/l12/notifications/browserNotifications.ts')])

console.log('notification contract: PASS')
