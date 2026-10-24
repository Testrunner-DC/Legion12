#!/usr/bin/env node

import assert from 'node:assert/strict'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

class CleanupProtocolError extends Error {
  constructor(code) {
    super(code)
    this.name = 'CleanupProtocolError'
    this.code = code
  }
}

function failureCode(error) {
  if (typeof error?.code === 'string' && error.code) return error.code
  if (error?.name === 'TimeoutError') return 'deadline-exceeded'
  if (error?.name === 'AbortError') return 'aborted'
  if (error instanceof SyntaxError) return 'invalid-json'
  return 'unexpected-failure'
}

export function cleanupTerminalForRequest(message, requestId) {
  if (message?.requestId !== requestId) return null
  if (message?.type === 'gameState') {
    return {
      accepted: true,
      phase: typeof message.state?.phase === 'string' ? message.state.phase : null,
    }
  }
  if (message?.type === 'actionRejected' || message?.type === 'error') {
    return { accepted: false, phase: null }
  }
  return null
}

export function isLegionBlockedByVisibleDisaster(snapshot, ownPlayer, card) {
  return snapshot?.activeDisaster?.cardId === 'S02-DS01'
    && card?.cardType === 'legion'
    && typeof card.profession === 'string'
    && card.profession.length > 0
    && card.profession === ownPlayer?.libraryTop?.profession
}

async function sendAndWait(client, predicate, label, timeoutMs, payload) {
  const pending = client.waitFor(predicate, label, timeoutMs, true)
  pending.catch(() => {})
  client.send(payload)
  return await pending
}

export async function cleanupRoomProtocol(room, { timeoutMs, requestIdFactory }) {
  const outcome = { compliantSurrenders: 0, roomsLeft: 0, errors: [] }
  if (room.started && !room.ended) {
    const client = [room.host, room.guest].find(candidate => !candidate.closed)
    if (!client) {
      outcome.errors.push('cleanup-surrender-no-open-transport')
      return outcome
    }

    const requestId = requestIdFactory()
    try {
      const message = await sendAndWait(client,
        candidate => cleanupTerminalForRequest(candidate, requestId) !== null,
        'surrender-terminal', timeoutMs,
        { type: 'gameAction', command: { type: 'surrender' }, requestId })
      const terminal = cleanupTerminalForRequest(message, requestId)
      if (!terminal.accepted) throw new CleanupProtocolError('cleanup-surrender-rejected')
      if (terminal.phase !== 'GameOver')
        throw new CleanupProtocolError('cleanup-surrender-terminal-not-game-over')
      room.ended = true
      outcome.compliantSurrenders = 1
    } catch (error) {
      outcome.errors.push(failureCode(error))
      return outcome
    }
  }

  if (!room.started || room.ended) {
    const leaves = await Promise.allSettled([room.host, room.guest].map(async client => {
      if (client.closed) throw new CleanupProtocolError('cleanup-websocket-closed')
      await sendAndWait(client, message => message?.type === 'roomLeft',
        'room-leave', timeoutMs, { type: 'leaveRoom' })
    }))
    const failedLeaves = leaves.filter(result => result.status === 'rejected')
    if (failedLeaves.length === 0) {
      room.left = true
      outcome.roomsLeft = 1
    } else {
      outcome.errors.push(...failedLeaves.map(result => failureCode(result.reason)))
    }
  }
  return outcome
}

class MockClient {
  constructor(onSend = null) {
    this.onSend = onSend
    this.sent = []
    this.waiters = new Set()
    this.closed = false
  }

  waitFor(predicate, label, timeoutMs) {
    if (this.closed) return Promise.reject(new CleanupProtocolError('websocket-not-open'))
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => finish(new CleanupProtocolError(`websocket-${label}-timeout`)), timeoutMs)
      const finish = (error, value) => {
        clearTimeout(timer)
        this.waiters.delete(waiter)
        if (error) reject(error); else resolve(value)
      }
      const waiter = { predicate, finish }
      this.waiters.add(waiter)
    })
  }

  send(payload) {
    if (this.closed) throw new CleanupProtocolError('websocket-not-open')
    this.sent.push(structuredClone(payload))
    this.onSend?.(payload, this)
  }

  emit(message) {
    for (const waiter of [...this.waiters]) {
      if (waiter.predicate(message)) waiter.finish(null, structuredClone(message))
    }
  }
}

function mockRoom(host, guest) {
  return { host, guest, started: true, ended: false, left: false }
}

async function runPureSelfTest() {
  const ackHost = new MockClient((payload, client) => {
    if (payload.type === 'gameAction') {
      client.emit({ type: 'gameState', requestId: payload.requestId, state: { phase: 'GameOver' } })
      client.emit({ type: 'actionRejected', requestId: payload.requestId })
    } else if (payload.type === 'leaveRoom') client.emit({ type: 'roomLeft' })
  })
  const ackGuest = new MockClient((payload, client) => {
    if (payload.type === 'leaveRoom') client.emit({ type: 'roomLeft' })
  })
  const acknowledged = await cleanupRoomProtocol(mockRoom(ackHost, ackGuest), {
    timeoutMs: 20, requestIdFactory: () => 'request-first-ack',
  })
  assert.deepEqual(acknowledged, { compliantSurrenders: 1, roomsLeft: 1, errors: [] })
  assert.deepEqual(ackHost.sent.map(message => message.type), ['gameAction', 'leaveRoom'])
  assert.deepEqual(ackGuest.sent.map(message => message.type), ['leaveRoom'])
  assert.equal([...ackHost.sent, ...ackGuest.sent].some(message => message.type === 'syncState'), false)

  const wrongIdHost = new MockClient((payload, client) => {
    if (payload.type === 'gameAction') {
      client.emit({ type: 'gameState', requestId: 'another-request', state: { phase: 'GameOver' } })
      client.emit({ type: 'gameState', requestId: payload.requestId, state: { phase: 'GameOver' } })
    } else if (payload.type === 'leaveRoom') client.emit({ type: 'roomLeft' })
  })
  const wrongIdGuest = new MockClient((payload, client) => {
    if (payload.type === 'leaveRoom') client.emit({ type: 'roomLeft' })
  })
  const afterWrongId = await cleanupRoomProtocol(mockRoom(wrongIdHost, wrongIdGuest), {
    timeoutMs: 20, requestIdFactory: () => 'request-after-wrong-id',
  })
  assert.deepEqual(afterWrongId, { compliantSurrenders: 1, roomsLeft: 1, errors: [] })

  const rejectionHost = new MockClient((payload, client) => {
    if (payload.type === 'gameAction') client.emit({ type: 'actionRejected', requestId: payload.requestId })
  })
  const rejectionGuest = new MockClient()
  const rejected = await cleanupRoomProtocol(mockRoom(rejectionHost, rejectionGuest), {
    timeoutMs: 20, requestIdFactory: () => 'request-rejected',
  })
  assert.deepEqual(rejected, {
    compliantSurrenders: 0, roomsLeft: 0, errors: ['cleanup-surrender-rejected'],
  })
  assert.deepEqual(rejectionHost.sent.map(message => message.type), ['gameAction'])
  assert.equal(rejectionGuest.sent.length, 0)

  const timeoutHost = new MockClient((payload, client) => {
    if (payload.type === 'gameAction')
      client.emit({ type: 'gameState', requestId: 'wrong-only', state: { phase: 'GameOver' } })
  })
  const timeoutGuest = new MockClient()
  const timedOut = await cleanupRoomProtocol(mockRoom(timeoutHost, timeoutGuest), {
    timeoutMs: 10, requestIdFactory: () => 'request-timeout',
  })
  assert.deepEqual(timedOut, {
    compliantSurrenders: 0, roomsLeft: 0, errors: ['websocket-surrender-terminal-timeout'],
  })
  assert.deepEqual(timeoutHost.sent.map(message => message.type), ['gameAction'])
  assert.equal(timeoutGuest.sent.length, 0)

  const wrongPhaseHost = new MockClient((payload, client) => {
    if (payload.type === 'gameAction')
      client.emit({ type: 'gameState', requestId: payload.requestId, state: { phase: 'Main' } })
  })
  const wrongPhase = await cleanupRoomProtocol(mockRoom(wrongPhaseHost, new MockClient()), {
    timeoutMs: 20, requestIdFactory: () => 'request-wrong-phase',
  })
  assert.deepEqual(wrongPhase, {
    compliantSurrenders: 0, roomsLeft: 0,
    errors: ['cleanup-surrender-terminal-not-game-over'],
  })

  const visibleState = { activeDisaster: { cardId: 'S02-DS01' } }
  const ownPlayer = { libraryTop: { profession: 'infantry' } }
  const legions = [
    { instanceId: 'blocked', cardType: 'legion', profession: 'infantry' },
    { instanceId: 'legal', cardType: 'legion', profession: 'cavalry' },
  ]
  assert.equal(legions.find(card => !isLegionBlockedByVisibleDisaster(visibleState, ownPlayer, card)).instanceId,
    'legal')

  process.stdout.write(`${JSON.stringify({
    status: 'pure-self-test-passed',
    cases: ['first-ack', 'different-request-id', 'rejection', 'timeout', 'both-leave', 'wrong-phase',
      'visible-disaster-legion'],
    networkRequests: 0,
  })}\n`)
}

const directlyExecuted = process.argv[1]
  && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url))
if (directlyExecuted) {
  runPureSelfTest().catch(error => {
    process.stderr.write(`${JSON.stringify({ status: 'pure-self-test-failed', error: failureCode(error) })}\n`)
    process.exitCode = 1
  })
}
