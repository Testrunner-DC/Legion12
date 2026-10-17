import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import { reactive } from 'vue'

const filename = new URL('../src/l12/chatResource.ts', import.meta.url)
const source = fs.readFileSync(filename, 'utf8').replace("import { reactive } from 'vue'", 'const reactive = globalThis.__l12ChatReactive')
globalThis.__l12ChatReactive = reactive
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 }, reportDiagnostics: true })
assert.equal(compiled.diagnostics?.filter(d => d.category === ts.DiagnosticCategory.Error).length, 0)
const { createChatResource, chatKey } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputText).toString('base64')}`)
const realSet = globalThis.setTimeout, realClear = globalThis.clearTimeout
const timers = new Map(); let timerId = 0, checks = 0
globalThis.setTimeout = callback => { const id = ++timerId; timers.set(id, callback); return id }
globalThis.clearTimeout = id => timers.delete(id)
const timeout = () => { const callbacks = [...timers.values()]; timers.clear(); callbacks.forEach(callback => callback()) }
const resource = createChatResource(), sent = []
const friend = { scope: 'friend', peerAccountId: 'bob' }, opponent = { scope: 'opponent', peerAccountId: 'bob', matchId: 'match-1' }
const entry = (seq = 1, overrides = {}) => ({ id: `message-${seq}`, seq, scope: 'friend', senderId: 'alice', senderName: '甲', recipientId: 'bob', text: '你好', createdAt: '2026-10-16T00:00:00Z', moderated: false, policyVersion: 'chat-v1', ...overrides })
const connect = (account = 'alice') => resource.connect(account, true, payload => { sent.push(payload); return true })
const last = type => sent.filter(item => item.type === type).at(-1)
try {
  resource.connect('alice', false, payload => { sent.push(payload); return true })
  resource.thread(friend).draft = '你好'; resource.send(friend); resource.sync(friend)
  assert.equal(sent.length, 0); checks++
  connect(); resource.send(friend)
  const original = last('chatSend')
  assert.equal(original.text, '你好'); assert.equal(original.senderId, undefined); checks += 2
  resource.thread(friend).draft = '正在写下一条'
  resource.receive({ type: 'chatAck', requestId: original.requestId, success: true, entry: entry() })
  assert.equal(resource.thread(friend).draft, '正在写下一条'); assert.equal(resource.thread(friend).pending, null); checks += 2
  resource.receive({ type: 'chatAck', requestId: original.requestId, success: true, entry: entry() })
  assert.equal(resource.thread(friend).entries.length, 1); checks++
  resource.thread(friend).draft = '待确认'; resource.send(friend)
  const retry = last('chatSend'); timeout()
  assert.equal(resource.thread(friend).pending.status, 'uncertain'); checks++
  resource.send(friend, true); assert.equal(last('chatSend').requestId, retry.requestId); checks++
  resource.receive({ type: 'chatAck', requestId: retry.requestId, success: true, entry: entry(2, { senderId: 'other', recipientId: 'not-alice' }) })
  timeout(); assert.equal(resource.thread(friend).pending.status, 'uncertain'); checks++
  resource.receive({ type: 'chatAck', requestId: retry.requestId, success: false, code: 'busy' })
  assert.equal(resource.thread(friend).pending.status, 'uncertain'); checks++
  resource.send(friend, true)
  resource.receive({ type: 'chatAck', requestId: retry.requestId, success: true, entry: entry(2, { text: '待确认' }) })
  assert.equal(resource.thread(friend).draft, ''); checks++
  resource.thread(friend).draft = '保留草稿'; resource.send(friend)
  resource.receive({ type: 'chatAck', requestId: last('chatSend').requestId, success: false, code: 'denied' })
  assert.equal(resource.thread(friend).draft, '保留草稿'); assert.equal(resource.thread(friend).pending, null); checks += 2
  const beforeHistory = sent.length; resource.sync(friend); resource.sync(friend)
  assert.equal(sent.length, beforeHistory + 1); checks++
  const history = last('chatSync')
  resource.receive({ type: 'chatHistory', requestId: history.requestId, entries: [entry(3, { senderId: 'bob', recipientId: 'alice' })], hasMore: true })
  assert.equal(resource.thread(friend).entries.length, 3); assert.equal(resource.thread(friend).unread, 0); checks += 2
  resource.sync(friend, true); assert.equal(last('chatSync').beforeSeq, 1); checks++
  resource.receive({ type: 'chatHistory', requestId: last('chatSync').requestId, entries: [entry(4, { senderId: 'mallory', recipientId: 'alice' })] })
  assert.equal(resource.thread(friend).entries.length, 3); checks++
  resource.receive({ type: 'chatMessage', entry: entry(4, { senderId: 'bob', recipientId: 'alice' }) })
  resource.receive({ type: 'chatMessage', entry: entry(4, { senderId: 'bob', recipientId: 'alice' }) })
  assert.equal(resource.thread(friend).unread, 1); checks++
  resource.read(friend); const read = last('chatRead')
  resource.receive({ type: 'chatReadAck', requestId: read.requestId, success: true, seq: 4 })
  assert.equal(resource.thread(friend).unread, 0); checks++
  resource.thread(opponent).draft = '本局草稿'
  assert.notEqual(chatKey(friend), chatKey(opponent)); checks++
  const cancelDelete = resource.suspend('bob', false); cancelDelete(true)
  resource.receive({ type: 'chatMessage', entry: entry(1, { scope: 'opponent', matchId: 'match-1', senderId: 'bob', recipientId: 'alice' }) })
  assert.equal(resource.thread(opponent).entries.length, 1); checks++
  const beforeDeleted = sent.length; resource.send(friend); resource.send(opponent)
  assert.equal(sent.length, beforeDeleted + 1); assert.equal(last('chatSend').scope, 'opponent'); checks += 2
  resource.acceptFriend('bob')
  resource.thread(friend).draft = '新朋友'; resource.send(friend)
  resource.sync(friend); const staleHistory = last('chatSync')
  const cancelBlock = resource.suspend('bob', true)
  resource.receive({ type: 'chatMessage', entry: entry(5, { senderId: 'bob', recipientId: 'alice' }) })
  resource.receive({ type: 'chatHistory', requestId: staleHistory.requestId, entries: [entry(5)] })
  assert.equal(resource.state.threads[chatKey(friend)], undefined); checks++
  cancelBlock('unknown')
  const beforeBlocked = sent.length; resource.send(friend); resource.send(opponent)
  assert.equal(sent.length, beforeBlocked); checks++
  resource.unblock('bob'); resource.acceptFriend('bob')
  resource.thread(friend).draft = '并发草稿'
  const rollback1 = resource.suspend('bob'), rollback2 = resource.suspend('bob')
  rollback1(false); assert.equal(resource.state.threads[chatKey(friend)], undefined); checks++
  rollback2(false); assert.equal(resource.thread(friend).draft, '并发草稿'); assert.deepEqual([...resource.thread(friend).entries], []); checks += 2
  const staleBlock = resource.suspend('bob')
  resource.reset('other'); connect('other'); resource.reset('alice'); connect()
  staleBlock(false)
  assert.equal(Object.keys(resource.state.threads).length, 0); checks++
  resource.sync(friend); const oldConnectionHistory = last('chatSync'); resource.disconnected(); connect()
  resource.receive({ type: 'chatHistory', requestId: oldConnectionHistory.requestId, entries: [entry(7)] })
  assert.equal(resource.thread(friend).entries.length, 0); checks++
  resource.receive({ type: 'chatMessage', entry: entry(8, { senderId: 'other', recipientId: 'stranger' }) })
  assert.equal(Object.keys(resource.state.threads).length, 1); checks++
  for (let i = 1; i <= 220; i++) resource.receive({ type: 'chatMessage', entry: entry(i, { senderId: 'bob', recipientId: 'alice' }) })
  assert.equal(resource.thread(friend).entries.length, 200); assert.equal(resource.thread(friend).entries[0].seq, 21); checks += 2
  resource.thread(friend).draft = 'a'.repeat(101); const beforeLength = sent.length; resource.send(friend)
  assert.equal(sent.length, beforeLength); checks++
  resource.thread(friend).draft = '👨‍👩‍👧‍👦'.repeat(25); resource.send(friend)
  assert.equal(sent.length, beforeLength); checks++
  resource.thread(friend).draft = '正常卡牌讨论：管理员、GM、雷神之锤'; resource.send(friend)
  assert.equal(sent.length, beforeLength + 1); checks++
  resource.reset('alice'); connect()
  const unknownRemove = resource.suspend('bob', false); unknownRemove('unknown'); resource.acceptFriend('bob')
  assert.equal(resource.thread(friend), null, 'an ordinary overview cannot clear an unresolved remove tombstone'); checks++
  resource.reset('alice'); connect()
  resource.thread(friend).draft = '已删除正文的原消息'; resource.send(friend)
  resource.receive({ type: 'chatAck', requestId: last('chatSend').requestId, success: true, duplicate: true, entry: null, messageId: 'previous-message', seq: 42 })
  assert.equal(resource.thread(friend).draft, ''); assert.equal(resource.thread(friend).entries.length, 0); assert.equal(resource.thread(friend).pending, null); checks += 3
  resource.reset('alice'); resource.connect('alice', true, payload => { sent.push(payload); return true }, 1_800_000_000_000)
  resource.thread(friend).draft = '采用服务器时钟'; resource.send(friend)
  assert.match(last('chatSend').requestId, /^c1\.180000000\d{4}\.[a-f0-9-]+$/); checks++
  resource.reset('alice'); connect()
  for (let i = 0; i < 20; i++) { const item = resource.thread({ scope: 'friend', peerAccountId: `peer-${i}` }); if (item) item.draft = '保留' }
  assert.equal(Object.keys(resource.state.threads).length, 16); checks++
  resource.reset('alice'); connect()
  resource.thread(opponent).draft = '临时对话'; resource.send(opponent); resource.retireOpponent('match-1')
  const endedSends = sent.length
  resource.send(opponent); resource.sync(opponent)
  resource.receive({ type: 'chatMessage', entry: entry(9, { scope: 'opponent', matchId: 'match-1', senderId: 'bob', recipientId: 'alice' }) })
  assert.equal(sent.length, endedSends); assert.equal(resource.thread(opponent).entries.length, 0); assert.equal(resource.thread(opponent).retired, true); checks += 3
  timeout(); assert.equal(resource.state.threads[chatKey(opponent)], undefined); checks++
  assert.equal(resource.peek(opponent), null, 'an expired read-only panel must not recreate its retired thread')
  assert.equal(resource.peek(opponent), null, 'reopening an expired read-only panel must remain non-creating'); checks += 2

  resource.reset('alice'); connect()
  const evictedOpponent = { scope: 'opponent', peerAccountId: 'bob', matchId: 'match-evicted' }
  resource.thread(evictedOpponent); resource.retireOpponent('match-evicted')
  for (let i = 0; i < 16; i++) resource.thread({ scope: 'friend', peerAccountId: `eviction-peer-${i}` })
  assert.equal(resource.peek(evictedOpponent), null, 'bounded thread eviction must not reactivate a retired panel'); checks++
  const nextOpponent = { scope: 'opponent', peerAccountId: 'bob', matchId: 'match-2' }
  const beforeNextMatch = sent.length
  resource.thread(nextOpponent).draft = '新对局可发送'; resource.send(nextOpponent)
  assert.equal(sent.length, beforeNextMatch + 1); assert.equal(last('chatSend').matchId, 'match-2'); checks += 2

  resource.reset('alice'); connect()
  const abaOpponent = { scope: 'opponent', peerAccountId: 'bob', matchId: 'match-account-aba' }
  resource.thread(abaOpponent); resource.retireOpponent('match-account-aba')
  const staleOpponentExpiry = [...timers.values()][0]
  resource.reset('carol'); connect('carol')
  const nextAccountOpponent = resource.thread(abaOpponent)
  staleOpponentExpiry()
  assert.equal(resource.peek(abaOpponent), nextAccountOpponent, 'a stale account expiry must not delete the next account thread')
  assert.equal(resource.peek(abaOpponent)?.retired, false); checks += 2
  resource.reset(); assert.equal(timers.size, 0); checks++
  const net = fs.readFileSync(new URL('../src/l12/net.ts', import.meta.url), 'utf8')
  assert(net.includes('chatResource.receive(message)')); assert(net.includes('chatV1: true')); checks += 2
  const panel = fs.readFileSync(new URL('../src/l12/site/ChatPanel.vue', import.meta.url), 'utf8')
  assert(panel.includes('event.isComposing') && panel.includes('event.keyCode === 229')); assert(!panel.includes('v-html')); checks += 2
  assert(panel.includes('props.readOnly ? chatResource.peek(conversation.value) : chatResource.thread(conversation.value)'))
  assert(panel.includes('if (visible.value && !props.readOnly) chatResource.sync(conversation.value)'))
  assert(panel.includes('if (visible.value && !props.readOnly) chatResource.read(conversation.value)'))
  assert(panel.includes(':disabled="readOnly || !chatResource.state.enabled'))
  assert(panel.includes(':disabled="readOnly || thread.loading"')); checks += 5
  const popover = fs.readFileSync(new URL('../src/l12/site/SocialPopover.vue', import.meta.url), 'utf8')
  assert(popover.includes('aria-modal="false"')); assert(!popover.includes('inert') && !popover.includes('body.style') && !popover.includes('setInterval')); checks += 2
  assert(popover.includes('initialPeerId?: string') && popover.includes("friend.accountId === props.initialPeerId"))
  assert(popover.includes('friendResource.accountId === chatResource.state.accountId')); checks += 2
  const shell = fs.readFileSync(new URL('../src/l12/site/SiteShell.vue', import.meta.url), 'utf8')
  assert(shell.includes(".filter(item => item.scope === 'friend' && item.unread > 0)"))
  assert(shell.includes('candidate.seq > item.readSeq') && shell.includes('Date.parse(entry.createdAt)'))
  assert(shell.includes("socialInitialTab.value = socialInitialPeerId.value ? 'friends' : 'online'"))
  assert(shell.includes(':initial-peer-id="socialInitialPeerId"')); checks += 4
  const utilities = shell.match(/<div class="site-utilities">([\s\S]*?)<\/div>/)?.[1] ?? ''
  assert.equal((utilities.match(/<button\b/g) ?? []).length, 4)
  assert(!utilities.includes('<span>设置</span>') && !utilities.includes('<span>更新日志</span>') && !utilities.includes('<span>在线 / 消息</span>') && !utilities.includes('<span>{{ connectionLabel }}</span>'))
  assert(utilities.includes('aria-label="设置"') && utilities.includes('aria-label="更新日志"')
    && utilities.includes(':aria-label="socialLabel"') && utilities.includes(':aria-label="connectionLabel"')); checks += 3
  const utilityOrder = ['aria-label="设置"', 'aria-label="更新日志"', ':aria-label="socialLabel"', ':aria-label="connectionLabel"']
  const utilityOffsets = utilityOrder.map(marker => utilities.indexOf(marker))
  assert(utilityOffsets.every(offset => offset >= 0)); assert.deepEqual([...utilityOffsets].sort((a, b) => a - b), utilityOffsets)
  assert(utilities.includes('<span class="utility-icon"><SiteIcon name="online"/><i>{{ onlineCount }}</i><b v-if="socialPendingCount" class="utility-unread"'))
  assert(shell.includes('socialAnchor.value = mobileOpen.value ? mobileMenuButton.value : (event.currentTarget as HTMLElement)')); checks += 4
  console.log(`Chat resource: ${checks} account, protocol, privacy, bounded memory and IME checks passed.`)
} finally {
  resource.reset(); globalThis.setTimeout = realSet; globalThis.clearTimeout = realClear; delete globalThis.__l12ChatReactive
}
