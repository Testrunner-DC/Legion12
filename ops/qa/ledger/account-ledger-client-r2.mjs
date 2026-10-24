import crypto from 'node:crypto'
import net from 'node:net'

const MAX_FRAME_BYTES = 16_384
const MAX_ACK_BYTES = 1_024
const SHA256_HEX = /^[0-9a-f]{64}$/
const RUN_ID = /^[A-Za-z0-9_-]{8,96}$/
const PIPE_NAME = /^[A-Za-z0-9_-]{16,96}$/

class LedgerClientError extends Error {
  constructor(code) {
    super(code)
    this.name = 'LedgerClientError'
    this.code = code
  }
}

function fail(code) {
  throw new LedgerClientError(code)
}

function withTimeout(promise, timeoutMs, code) {
  let timer
  return Promise.race([
    promise,
    new Promise((_, reject) => {
      timer = setTimeout(() => reject(new LedgerClientError(code)), timeoutMs)
      timer.unref?.()
    }),
  ]).finally(() => clearTimeout(timer))
}

function encodeFrame(value, maxBytes = MAX_FRAME_BYTES) {
  const payload = Buffer.from(JSON.stringify(value), 'utf8')
  if (payload.length < 2 || payload.length > maxBytes) fail('ledger_frame_rejected')
  const frame = Buffer.allocUnsafe(payload.length + 4)
  frame.writeUInt32LE(payload.length, 0)
  payload.copy(frame, 4)
  payload.fill(0)
  return frame
}

class NamedPipeLedgerTransport {
  constructor({
    pipeName,
    connectTimeoutMs = 10_000,
    ackTimeoutMs = 10_000,
    connectionFactory = net.createConnection,
  } = {}) {
    if (typeof pipeName !== 'string' || !PIPE_NAME.test(pipeName)) fail('ledger_pipe_name_rejected')
    if (!Number.isInteger(connectTimeoutMs) || connectTimeoutMs < 1000 || connectTimeoutMs > 60_000 ||
        !Number.isInteger(ackTimeoutMs) || ackTimeoutMs < 1000 || ackTimeoutMs > 60_000 ||
        typeof connectionFactory !== 'function') {
      fail('ledger_timeout_rejected')
    }
    this.pipeName = pipeName
    this.connectTimeoutMs = connectTimeoutMs
    this.ackTimeoutMs = ackTimeoutMs
    this.connectionFactory = connectionFactory
    this.socket = null
    this.buffer = Buffer.alloc(0)
    this.pending = null
    this.failed = false
  }

  async connect() {
    if (this.failed) fail('ledger_transport_failed')
    if (this.socket) return
    const socket = this.connectionFactory(`\\\\.\\pipe\\${this.pipeName}`)
    socket.on('data', chunk => this.#onData(chunk))
    socket.on('error', () => this.#failPending('ledger_transport_failed'))
    socket.on('close', () => this.#failPending('ledger_transport_closed'))
    this.socket = socket
    try {
      await withTimeout(new Promise((resolve, reject) => {
        socket.once('connect', resolve)
        socket.once('error', () => reject(new LedgerClientError('ledger_connect_failed')))
      }), this.connectTimeoutMs, 'ledger_connect_timeout')
      if (this.socket !== socket || socket.destroyed) fail('ledger_transport_failed')
    } catch (error) {
      if (this.socket === socket) this.socket = null
      if (!socket.destroyed) socket.destroy()
      this.failed = true
      this.#failPending('ledger_transport_failed')
      throw error instanceof LedgerClientError ? error : new LedgerClientError('ledger_connect_failed')
    }
  }

  async request(value) {
    if (this.failed) fail('ledger_transport_failed')
    await this.connect()
    if (this.pending) fail('ledger_concurrent_request_rejected')
    const frame = encodeFrame(value)
    const response = new Promise((resolve, reject) => { this.pending = { resolve, reject } })
    try {
      await new Promise((resolve, reject) => {
        this.socket.write(frame, error => error ? reject(new LedgerClientError('ledger_write_failed')) : resolve())
      })
      return await withTimeout(response, this.ackTimeoutMs, 'ledger_ack_timeout')
    } catch (error) {
      this.abort()
      throw error instanceof LedgerClientError ? error : new LedgerClientError('ledger_transport_failed')
    } finally {
      frame.fill(0)
      this.pending = null
    }
  }

  close() {
    if (this.socket && !this.socket.destroyed) this.socket.end()
    this.socket = null
  }

  abort() {
    this.failed = true
    if (this.socket && !this.socket.destroyed) this.socket.destroy()
    this.socket = null
    this.#failPending('ledger_transport_failed')
  }

  #failPending(code) {
    if (!this.pending) return
    const { reject } = this.pending
    this.pending = null
    reject(new LedgerClientError(code))
  }

  #onData(chunk) {
    if (this.failed) return
    if (this.buffer.length + chunk.length > MAX_ACK_BYTES + 4) {
      this.abort()
      return
    }
    this.buffer = Buffer.concat([this.buffer, chunk])
    if (this.buffer.length < 4) return
    const length = this.buffer.readUInt32LE(0)
    if (length < 2 || length > MAX_ACK_BYTES) {
      this.abort()
      return
    }
    if (this.buffer.length < length + 4) return
    if (!this.pending || this.buffer.length !== length + 4) {
      this.abort()
      return
    }
    const payload = this.buffer.subarray(4, length + 4)
    this.buffer = Buffer.alloc(0)
    try {
      const value = JSON.parse(payload.toString('utf8'))
      const { resolve } = this.pending
      this.pending = null
      resolve(value)
    } catch {
      this.abort()
    }
  }
}

class AccountLedgerClient {
  constructor({ runId, maxSlots = 60, transport } = {}) {
    if (typeof runId !== 'string' || !RUN_ID.test(runId)) fail('ledger_run_rejected')
    if (!Number.isInteger(maxSlots) || maxSlots < 1 || maxSlots > 60) fail('ledger_slot_rejected')
    if (!transport || typeof transport.request !== 'function') fail('ledger_transport_rejected')
    this.runId = runId
    this.maxSlots = maxSlots
    this.transport = transport
    this.sequence = 0
    this.states = new Map()
    this.failed = false
  }

  async persistIntent({ slot, username, password, requestedAtUtc, preflightAbsent, requestFingerprint }) {
    this.#assertUsable()
    if (!Number.isInteger(slot) || slot < 0 || slot >= this.maxSlots || this.states.has(slot)) {
      fail('ledger_slot_rejected')
    }
    if (typeof username !== 'string' || !/^[A-Za-z0-9_]{2,11}$/.test(username) ||
        typeof password !== 'string' || password.length < 16 || password.length > 128 ||
        typeof requestedAtUtc !== 'string' || Number.isNaN(Date.parse(requestedAtUtc)) ||
        preflightAbsent !== true || typeof requestFingerprint !== 'string' || !SHA256_HEX.test(requestFingerprint)) {
      fail('ledger_intent_rejected')
    }
    const ack = await this.#persist({
      type: 'intent', runId: this.runId, sequence: this.sequence + 1, slot,
      username, password, requestedAtUtc, preflightAbsent, requestFingerprint,
    }, this.states.size + 1)
    this.states.set(slot, 'intent')
    return ack
  }

  async persistConfirmed({ slot, id, createdAtUtc }) {
    this.#assertUsable()
    if (!Number.isInteger(slot) || this.states.get(slot) !== 'intent' ||
        typeof id !== 'string' || id.length < 8 || id.length > 128 || /\s/.test(id) ||
        typeof createdAtUtc !== 'string' || Number.isNaN(Date.parse(createdAtUtc))) {
      fail('ledger_confirmation_rejected')
    }
    const ack = await this.#persist({
      type: 'confirmed', runId: this.runId, sequence: this.sequence + 1, slot, id, createdAtUtc,
    }, this.states.size)
    this.states.set(slot, 'confirmed')
    return ack
  }

  stop() {
    this.failed = true
    this.transport.abort?.()
  }

  close() {
    this.transport.close?.()
  }

  #assertUsable() {
    if (this.failed) fail('ledger_client_stopped')
  }

  async #persist(event, expectedCount) {
    try {
      const ack = await this.transport.request(event)
      if (!ack || ack.ok !== true || ack.seq !== event.sequence || ack.count !== expectedCount ||
          typeof ack.cipherSha256 !== 'string' || !SHA256_HEX.test(ack.cipherSha256)) {
        fail('ledger_ack_rejected')
      }
      this.sequence = event.sequence
      return Object.freeze({ seq: ack.seq, count: ack.count, cipherSha256: ack.cipherSha256 })
    } catch (error) {
      this.failed = true
      this.transport.abort?.()
      throw error instanceof LedgerClientError ? error : new LedgerClientError('ledger_persist_failed')
    }
  }
}

function registrationFingerprint(username, password) {
  return crypto.createHash('sha256')
    .update(JSON.stringify({ username, password }), 'utf8')
    .digest('hex')
}

async function registerAccountWithLedger({
  ledger, slot, username, password, requestedAtUtc = new Date().toISOString(), registerFn,
}) {
  if (!(ledger instanceof AccountLedgerClient) || typeof registerFn !== 'function') {
    fail('ledger_registration_arguments_rejected')
  }
  try {
    await ledger.persistIntent({
      slot, username, password, requestedAtUtc, preflightAbsent: true,
      requestFingerprint: registrationFingerprint(username, password),
    })
    const created = await registerFn({ username, password })
    const id = created?.account?.id
    const createdAtUtc = created?.account?.createdAt
    if (typeof id !== 'string' || typeof createdAtUtc !== 'string' || created?.account?.username !== username) {
      fail('ledger_registration_response_rejected')
    }
    await ledger.persistConfirmed({ slot, id, createdAtUtc })
    return created
  } catch (error) {
    ledger.stop()
    throw error instanceof LedgerClientError ? error : new LedgerClientError('ledger_registration_failed')
  }
}

export {
  AccountLedgerClient,
  LedgerClientError,
  NamedPipeLedgerTransport,
  registerAccountWithLedger,
  registrationFingerprint,
}
