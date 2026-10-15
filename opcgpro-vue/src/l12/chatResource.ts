import { reactive } from 'vue'

export type ChatScope = 'friend' | 'opponent'
export interface ChatConversation { scope: ChatScope; peerAccountId: string; matchId?: string }
export interface ChatEntry {
  id: string; seq: number; scope: ChatScope; matchId?: string
  senderId: string; senderName: string; recipientId: string; text: string
  createdAt: string; moderated: boolean; policyVersion: string
}
export interface ChatThread extends ChatConversation {
  entries: ChatEntry[]; draft: string; unread: number; loaded: boolean; loading: boolean
  hasMore: boolean; notice: string; readSeq: number; retired: boolean
  pending: null | { id: string; text: string; status: 'sending' | 'uncertain' }
}
type ChatWire = Record<string, unknown>
const MAX_THREADS = 16
const MAX_ENTRIES = 200
const validId = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.length <= 128
export const chatKey = (value: ChatConversation) => JSON.stringify([value.scope, value.peerAccountId, value.scope === 'opponent' ? value.matchId : ''])

// Account-owned, bounded, memory-only resource. It owns no socket and never polls.
export function createChatResource() {
  const state = reactive({ accountId: '', enabled: false, connection: 0, privacyRevision: 0, threads: {} as Record<string, ChatThread> })
  let transport: ((payload: ChatWire) => boolean) | null = null
  const requests = new Map<string, { key: string; kind: 'sync' | 'read'; account: string; connection: number; seq?: number; timer: ReturnType<typeof setTimeout> }>()
  const timers = new Map<string, ReturnType<typeof setTimeout>>()
  const opponentExpiry = new Map<string, ReturnType<typeof setTimeout>>()
  const suspended = new Map<string, number>()
  const blocked = new Set<string>()
  const removedFriends = new Set<string>()
  const unknownFriendMutations = new Set<string>()
  const privacyDrafts = new Map<string, Map<string, ChatThread>>()
  let accountEpoch = 0
  let serverUtcAtConnection = Date.now(), monotonicAtConnection = performance.now()
  const requestId = () => `c1.${Math.floor(serverUtcAtConnection + performance.now() - monotonicAtConnection)}.${crypto.randomUUID()}`
  function conversationKey(value: ChatConversation): string | null {
    void state.privacyRevision
    if (!validId(value.peerAccountId) || value.peerAccountId === state.accountId
      || (value.scope !== 'friend' && value.scope !== 'opponent')
      || (value.scope === 'opponent' && !validId(value.matchId)) || !allowed(value.peerAccountId, value.scope)) return null
    const key = chatKey(value)
    return key
  }
  function peek(value: ChatConversation): ChatThread | null {
    const key = conversationKey(value)
    return key === null ? null : state.threads[key] ?? null
  }
  function thread(value: ChatConversation): ChatThread | null {
    const key = conversationKey(value)
    if (key === null) return null
    if (state.threads[key]) return state.threads[key]!
    if (Object.keys(state.threads).length >= MAX_THREADS) {
      const idle = Object.keys(state.threads).find(id => {
        const item = state.threads[id]!
        return !item.pending && !item.draft && !item.unread && !item.loading
      })
      if (!idle) return null
      forget(idle)
    }
    state.threads[key] = { ...value, entries: [], draft: '', unread: 0, loaded: false, loading: false, hasMore: false, notice: '', readSeq: 0, retired: false, pending: null }
    return state.threads[key]!
  }
  const mutationKey = (peer: string, allScopes: boolean) => JSON.stringify([peer, allScopes ? 'all' : 'friend'])
  function forget(key: string) {
    const item = state.threads[key]
    if (item?.pending) { const timer = timers.get(item.pending.id); if (timer) clearTimeout(timer); timers.delete(item.pending.id) }
    for (const [id, request] of requests) if (request.key === key) { clearTimeout(request.timer); requests.delete(id) }
    const expiry = opponentExpiry.get(key); if (expiry) clearTimeout(expiry)
    opponentExpiry.delete(key); delete state.threads[key]
  }
  function allowed(peer: string, scope: ChatScope) {
    return !blocked.has(peer) && !suspended.has(mutationKey(peer, true))
      && !(scope === 'friend' && (removedFriends.has(peer) || suspended.has(mutationKey(peer, false))))
  }
  function clearRequests() {
    for (const item of requests.values()) clearTimeout(item.timer)
    requests.clear()
    for (const timer of timers.values()) clearTimeout(timer)
    timers.clear()
    for (const item of Object.values(state.threads)) {
      item.loading = false
      if (item.pending) item.pending.status = 'uncertain'
    }
  }
  function reset(accountId = '') {
    for (const timer of opponentExpiry.values()) clearTimeout(timer)
    opponentExpiry.clear()
    accountEpoch += 1
    clearRequests(); state.accountId = accountId; state.enabled = false; state.connection += 1
    state.threads = {}; transport = null; suspended.clear(); blocked.clear(); removedFriends.clear(); unknownFriendMutations.clear(); privacyDrafts.clear()
  }
  function connect(accountId: string, enabled: boolean, send: ((payload: ChatWire) => boolean) | null, serverUtcMs?: number) {
    if (state.accountId !== accountId) reset(accountId)
    clearRequests(); state.connection += 1; state.enabled = enabled; transport = send
    serverUtcAtConnection = typeof serverUtcMs === 'number' && Number.isSafeInteger(serverUtcMs) && serverUtcMs > 0 ? serverUtcMs : Date.now()
    monotonicAtConnection = performance.now()
  }
  function disconnected() { clearRequests(); state.enabled = false; transport = null }
  function sendWire(payload: ChatWire) { return state.enabled && Boolean(transport?.(payload)) }
  function beginRequest(item: ChatThread, kind: 'sync' | 'read', payload: ChatWire) {
    if (requests.size >= 16 || !allowed(item.peerAccountId, item.scope)) return false
    const id = requestId(), key = chatKey(item), account = state.accountId, connection = state.connection
    const timer = setTimeout(() => {
      const current = requests.get(id)
      if (!current) return
      requests.delete(id)
      if (kind === 'sync') item.loading = false
      item.notice = '暂未收到聊天结果，可以重试。'
    }, 10_000)
    requests.set(id, { key, kind, account, connection, seq: typeof payload.seq === 'number' ? payload.seq : undefined, timer })
    if (sendWire({ ...payload, requestId: id })) return true
    clearTimeout(timer); requests.delete(id); return false
  }
  function sync(value: ChatConversation, older = false) {
    const item = thread(value)
    if (!item || item.retired || item.loading || !state.enabled || !allowed(item.peerAccountId, item.scope)) return
    item.loading = true; item.notice = ''
    const beforeSeq = older ? item.entries[0]?.seq : undefined
    if (!beginRequest(item, 'sync', { type: 'chatSync', scope: item.scope, peerAccountId: item.peerAccountId, matchId: item.matchId, beforeSeq })) {
      item.loading = false; item.notice = '聊天连接暂不可用。'
    }
  }
  function send(value: ChatConversation, retry = false) {
    const item = thread(value)
    if (!item || item.retired || !allowed(item.peerAccountId, item.scope) || !state.enabled) return
    if (item.pending?.status === 'sending' || (item.pending && !retry)) return
    const text = retry ? item.pending?.text ?? '' : item.draft.trim()
    const length = typeof Intl.Segmenter === 'function' ? [...new Intl.Segmenter('zh', { granularity: 'grapheme' }).segment(text)].length : [...text].length
    if (!text || new TextEncoder().encode(text).length > 512 || length > 100) {
      item.notice = '消息最多100字且不超过512字节。'; return
    }
    const id = retry ? item.pending?.id ?? requestId() : requestId()
    item.pending = { id, text, status: 'sending' }; item.notice = ''
    if (!sendWire({ type: 'chatSend', requestId: id, scope: item.scope, peerAccountId: item.peerAccountId, matchId: item.matchId, text })) {
      item.pending.status = 'uncertain'; item.notice = '尚未确认发送，可使用原请求重试。'; return
    }
    const old = timers.get(id); if (old) clearTimeout(old)
    timers.set(id, setTimeout(() => {
      timers.delete(id)
      if (item.pending?.id !== id) return
      item.pending.status = 'uncertain'; item.notice = '尚未确认发送；重试会沿用原请求，不会重复发送。'
    }, 10_000))
  }
  function parseEntry(value: unknown): ChatEntry | null {
    if (!value || typeof value !== 'object') return null
    const e = value as ChatEntry
    if (!validId(e.id) || !validId(e.senderId) || !validId(e.recipientId) || e.senderId === e.recipientId
      || !Number.isSafeInteger(e.seq) || e.seq <= 0 || !['friend', 'opponent'].includes(e.scope)
      || (e.scope === 'opponent' && !validId(e.matchId))
      || typeof e.text !== 'string' || new TextEncoder().encode(e.text).length > 512
      || typeof e.senderName !== 'string' || e.senderName.length > 128
      || typeof e.createdAt !== 'string' || !Number.isFinite(Date.parse(e.createdAt))
      || typeof e.policyVersion !== 'string' || e.policyVersion.length > 64
      || (e.senderId !== state.accountId && e.recipientId !== state.accountId)) return null
    return { id: e.id, seq: e.seq, scope: e.scope, matchId: e.matchId, senderId: e.senderId, senderName: e.senderName, recipientId: e.recipientId, text: e.text, createdAt: e.createdAt, moderated: e.moderated === true, policyVersion: e.policyVersion }
  }
  function entryConversation(entry: ChatEntry): ChatConversation {
    return { scope: entry.scope, peerAccountId: entry.senderId === state.accountId ? entry.recipientId : entry.senderId, matchId: entry.matchId }
  }
  function merge(item: ChatThread, values: ChatEntry[], incoming: boolean) {
    const entries = new Map(item.entries.map(entry => [entry.id, entry]))
    for (const entry of values) {
      if (chatKey(entryConversation(entry)) !== chatKey(item)) continue
      if (!entries.has(entry.id) && incoming && entry.senderId !== state.accountId && entry.seq > item.readSeq) item.unread += 1
      entries.set(entry.id, entry)
    }
    item.entries = [...entries.values()].sort((a, b) => a.seq - b.seq).slice(-MAX_ENTRIES)
    item.unread = Math.min(item.unread, MAX_ENTRIES)
  }
  function receive(message: ChatWire) {
    if (!state.accountId || !state.enabled) return
    const type = message.type
    if (type === 'chatMessage') {
      const entry = parseEntry(message.entry)
      if (!entry) return
      const conversation = entryConversation(entry)
      if (!allowed(conversation.peerAccountId, conversation.scope)) return
      const item = thread(conversation); if (item && !item.retired) merge(item, [entry], true)
      return
    }
    if (type === 'chatAck') {
      const found = Object.values(state.threads).find(item => item.pending?.id === message.requestId)
      if (!found || !allowed(found.peerAccountId, found.scope)) return
      if (message.success === true) {
        const entry = parseEntry(message.entry)
        const expiredReceipt = message.entry == null && message.duplicate === true && validId(message.messageId)
          && Number.isSafeInteger(message.seq) && Number(message.seq) > 0
        if (!expiredReceipt && (!entry || chatKey(entryConversation(entry)) !== chatKey(found) || entry.senderId !== state.accountId)) return
        if (entry) merge(found, [entry], false)
        if (found.draft.trim() === found.pending?.text) found.draft = ''
        found.pending = null; found.notice = expiredReceipt ? '原消息已确认，已不在保留记录中。' : entry?.moderated ? '部分内容已按聊天规则处理。' : ''
      } else {
        const retryable = message.code === 'busy' || message.code === 'unavailable'
        if (retryable && found.pending) found.pending.status = 'uncertain'
        else found.pending = null
        found.notice = typeof message.message === 'string' ? message.message.slice(0, 200) : '消息未发送，请稍后重试。'
      }
      const timer = timers.get(String(message.requestId)); if (timer) clearTimeout(timer)
      timers.delete(String(message.requestId))
      return
    }
    const id = String(message.requestId ?? ''), request = requests.get(id)
    if (!request || request.account !== state.accountId || request.connection !== state.connection) return
    if ((request.kind === 'sync' && type !== 'chatHistory') || (request.kind === 'read' && type !== 'chatReadAck')) return
    const item = state.threads[request.key]
    requests.delete(id); clearTimeout(request.timer)
    if (!item || item.retired || !allowed(item.peerAccountId, item.scope)) return
    if (request.kind === 'sync') {
      item.loading = false
      if (message.success === false) { item.notice = '当前会话暂不可用。'; return }
      if (!Array.isArray(message.entries) || message.entries.length > MAX_ENTRIES) return
      const entries = message.entries.map(parseEntry)
      if (entries.some(entry => !entry || chatKey(entryConversation(entry)) !== request.key)) return
      merge(item, entries as ChatEntry[], false); item.loaded = true; item.hasMore = message.hasMore === true
    } else if (message.success === true && Number.isSafeInteger(message.seq) && Number(message.seq) >= 0 && Number(message.seq) <= (request.seq ?? 0)) {
      item.readSeq = Math.max(item.readSeq, Number(message.seq))
      item.unread = item.entries.filter(entry => entry.senderId !== state.accountId && entry.seq > item.readSeq).length
    }
  }
  function read(value: ChatConversation) {
    const item = thread(value), seq = item?.entries.at(-1)?.seq ?? 0
    if (!item || item.retired || !seq || seq <= item.readSeq || !allowed(item.peerAccountId, item.scope) || [...requests.values()].some(r => r.kind === 'read' && r.key === chatKey(item))) return
    beginRequest(item, 'read', { type: 'chatRead', scope: item.scope, peerAccountId: item.peerAccountId, matchId: item.matchId, seq })
  }
  function suspend(peer: string, allScopes = true) {
    state.privacyRevision += 1
    const owner = state.accountId
    const expectedEpoch = accountEpoch
    const ownKey = mutationKey(peer, allScopes)
    suspended.set(ownKey, (suspended.get(ownKey) ?? 0) + 1)
    const before = Object.entries(state.threads).filter(([, item]) => item.peerAccountId === peer && (allScopes || item.scope === 'friend'))
    const drafts = privacyDrafts.get(peer) ?? new Map<string, ChatThread>()
    for (const [key, item] of before) drafts.set(key, item)
    privacyDrafts.set(peer, drafts)
    for (const [key, item] of before) {
      if (item.pending) { const timer = timers.get(item.pending.id); if (timer) clearTimeout(timer); timers.delete(item.pending.id) }
      for (const [id, request] of requests) if (request.key === key) { clearTimeout(request.timer); requests.delete(id) }
      forget(key)
    }
    let completed = false
    return (committed: boolean | 'unknown') => {
      if (completed || owner !== state.accountId || expectedEpoch !== accountEpoch) return
      completed = true
      const remainingOwn = (suspended.get(ownKey) ?? 1) - 1
      if (remainingOwn > 0) suspended.set(ownKey, remainingOwn); else suspended.delete(ownKey)
      const remaining = (suspended.get(mutationKey(peer, true)) ?? 0) + (suspended.get(mutationKey(peer, false)) ?? 0)
      if (committed) {
        if (allScopes) blocked.add(peer)
        else { removedFriends.add(peer); if (committed === 'unknown') unknownFriendMutations.add(peer); else unknownFriendMutations.delete(peer) }
      }
      if (!remaining && !blocked.has(peer)) for (const [key, item] of drafts) {
        if (item.scope === 'friend' && removedFriends.has(peer)) continue
        // Old server replies/history cannot repopulate a retired privacy generation. Keep
        // only the owner's draft; a fresh authenticated history request must repopulate it.
        item.entries = []; item.unread = 0; item.loading = false; item.pending = null; item.loaded = false
        state.threads[key] = item
      }
      if (!remaining) privacyDrafts.delete(peer)
      state.privacyRevision += 1
    }
  }
  function unblock(peer: string) { blocked.delete(peer); state.privacyRevision += 1 }
  function retireOpponent(matchId: string) {
    const expectedEpoch = accountEpoch
    for (const [key, item] of Object.entries(state.threads)) {
      if (item.scope !== 'opponent' || item.matchId !== matchId || item.retired) continue
      item.retired = true; item.notice = '本局对话已结束，仅临时保留已接收消息。'
      if (item.pending) { const timer = timers.get(item.pending.id); if (timer) clearTimeout(timer); timers.delete(item.pending.id); item.pending = null }
      opponentExpiry.set(key, setTimeout(() => {
        opponentExpiry.delete(key)
        if (expectedEpoch === accountEpoch && state.threads[key] === item) forget(key)
      }, 10 * 60_000))
    }
  }
  function acceptFriend(peer: string) { if (!unknownFriendMutations.has(peer) && removedFriends.delete(peer)) state.privacyRevision += 1 }
  function canUse(value: ChatConversation) { return state.privacyRevision >= 0 && allowed(value.peerAccountId, value.scope) }
  return { state, thread, peek, reset, connect, disconnected, sync, send, receive, read, suspend, unblock, acceptFriend, canUse, retireOpponent }
}
export const chatResource = createChatResource()
