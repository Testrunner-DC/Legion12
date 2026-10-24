#!/usr/bin/env node

import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { performance } from 'node:perf_hooks'

import { ClientBoundaries } from '../client-boundaries.mjs'
import { isLegionBlockedByVisibleDisaster } from '../load-v2-r2/pressure-pure-selftest.mjs'
import { selectLegalCommandWithAllBottom as selectLegalCommand } from '../cohort30-terminal-v2/placement-selector-r1.mjs'
import {
  ContractError, FIXED, LIMITS, frameSizeValid, loadCandidateBinding, isPlainObject, readJsonFile,
  requireContract, sanitizedFailure, sha256File, sha256Text,
  validateManifest, validateOwnedReceipt, validateRuntimeInputs,
} from './large-terminal-contract-r6.mjs'
import {
  COHORT, diagnosticSlotForRoom, validateClientCohortReport,
} from './cohort30-contract-r6.mjs'

import { execFileSync } from 'node:child_process'
function assertGeneratedRoot() {
  execFileSync('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
    ['-NoProfile', '-Command', ". '"+fileURLToPath(new URL('../../../windows/L12GeneratedStorage.ps1',import.meta.url)).replaceAll("'","''")+"'; $null = Assert-L12GeneratedPath -Path 'D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6' -Purpose 'cohort r6 local tool output'"],
    { windowsHide: true, stdio: 'pipe', timeout: 15000 })
}

import { AccountLedgerClient, NamedPipeLedgerTransport, registrationFingerprint }
  from '../../ledger/account-ledger-client-r2.mjs'
const LEDGER_SOURCE = fileURLToPath(new URL('../../ledger/account-ledger-client-r2.mjs',import.meta.url))
requireContract(sha256File(LEDGER_SOURCE, 65536) === '88BB6ACB68C3867CEC7A5E3DBA5A2B429A672FE4CDEF94C8D47D5921EA38B7F9',
  'frozen_ledger_dependency_changed')

const TOOL_ROOT = path.dirname(fileURLToPath('file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/cohort30-terminal-v2-r6.mjs'))
const FROZEN_ROOT = path.resolve(fileURLToPath(new URL('..',import.meta.url)))
const CLIENT_BOUNDARIES_SOURCE = path.resolve(FROZEN_ROOT, 'client-boundaries.mjs')
const SELECTOR_HELPER_SOURCE = path.resolve(FROZEN_ROOT, 'load-v2-r2/pressure-pure-selftest.mjs')
const TYPESCRIPT_SOURCE = fileURLToPath(new URL('../../../../opcgpro-vue/node_modules/typescript/lib/typescript.js',import.meta.url))
const NONCE_ENV = 'L12_SYNTHETIC_TRANSPORT_DIAGNOSTIC_NONCE'
const ADMIN_ENV = 'L12_PRESSURE_ADMIN_TOKEN'
const STOP_ENV = 'L12_PUBLIC_PROBE_STOP_FILE'
const MAX_JSON_BYTES = 2 * 1024 * 1024

requireContract(sha256File(CLIENT_BOUNDARIES_SOURCE, 64 * 1024) === FIXED.clientBoundariesSha256
  && sha256File(SELECTOR_HELPER_SOURCE, 128 * 1024) === FIXED.selectorHelperSha256
  && sha256File(TYPESCRIPT_SOURCE, 16 * 1024 * 1024) === FIXED.typescriptSha256,
'frozen_projection_dependency_changed')
let reduceEnvelope = null
let restoreCardFrame = null
let CardFrameRecovery = null

export async function initializeCandidateProtocol() {
  requireContract(FIXED.bindingSha256, 'candidate_binding_required')
  const ts = createRequire(import.meta.url)(TYPESCRIPT_SOURCE)
  const netText = fs.readFileSync(FIXED.netSourcePath, 'utf8')
  const frameText = fs.readFileSync(FIXED.cardFrameSourcePath, 'utf8')
  requireContract(sha256Text(netText) === FIXED.deployedNetSha256
    && sha256Text(frameText) === FIXED.cardFrameSha256, 'candidate_protocol_source_changed')
  const compile = (source, fileName, module) => {
    const value = ts.transpileModule(source, { fileName, reportDiagnostics: true,
      compilerOptions: { target: ts.ScriptTarget.ES2022, module, isolatedModules: true } })
    requireContract(!(value.diagnostics ?? []).some(item => item.category === ts.DiagnosticCategory.Error),
      'candidate_protocol_transpile_failed')
    return value.outputText
  }
  const reducerBody = netText.slice(netText.indexOf('function rebuildDeltaGameState('),
    netText.indexOf('function clearReconnectTimer()'))
  requireContract(reducerBody.startsWith('function rebuildDeltaGameState('), 'candidate_reducer_missing')
  reduceEnvelope = new Function('lastGameStateEnvelope', 'delta', 'negotiatedEventWindowDelta',
    compile(reducerBody, FIXED.netSourcePath, ts.ModuleKind.ESNext) + ';return rebuildDeltaGameState(delta);')
  const code = compile(frameText, FIXED.cardFrameSourcePath, ts.ModuleKind.ESNext)
  const module = await import('data:text/javascript;base64,' + Buffer.from(code).toString('base64'))
  requireContract(typeof module.restoreGameStateCardFrame === 'function'
    && typeof module.GameStateCardFrameRecovery === 'function', 'candidate_card_frame_exports_missing')
  restoreCardFrame = module.restoreGameStateCardFrame
  CardFrameRecovery = module.GameStateCardFrameRecovery
  return module
}

export function reconstructCandidateDelta(previous, delta, eventWindow) {
  requireContract(typeof reduceEnvelope === 'function', 'candidate_protocol_not_initialized')
  return reduceEnvelope(previous, delta, eventWindow)
}

class RunError extends ContractError {}

// R4_SELECTOR_METADATA_BEGIN
const PROMPT_SUBTYPE_PHASES = new Set([
  'Preparation', 'Mulligan', 'DisasterPreparation', 'Initiative', 'Main', 'Defense', 'GameOver',
])
const PROMPT_SUBTYPE_KINDS = new Set([
  'disaster-ban', 'disaster-pick', 'disaster-reveal', 'initiative', 'option', 'response',
  'resource-payment', 'order',
])
const PROMPT_SUBTYPE_ACTIONS = new Set(['disaster-apocalypse-hand-order'])
const PROMPT_SUBTYPE_PLACEMENTS = new Set(['all-bottom', 'all-top-bottom'])
const promptSubtypeEnum = (value, allowed, optional = false) => {
  if (optional && (value === undefined || value === null)) return null
  return typeof value === 'string' && allowed.has(value) ? value : null
}
const promptSubtypeInteger = value => Number.isInteger(value) && value >= 0 && value <= 512
  ? value : null

export function sanitizeUnsupportedPromptSubtypes(snapshot, playerIndex) {
  const prompts = Array.isArray(snapshot?.prompts) ? snapshot.prompts : []
  return prompts.filter(prompt => prompt?.playerIndex === playerIndex).slice(0, 8).map(prompt => ({
    phase: promptSubtypeEnum(snapshot?.phase, PROMPT_SUBTYPE_PHASES),
    kind: promptSubtypeEnum(prompt?.kind, PROMPT_SUBTYPE_KINDS),
    action: promptSubtypeEnum(prompt?.data?.action, PROMPT_SUBTYPE_ACTIONS, true),
    placementMode: promptSubtypeEnum(prompt?.data?.placementMode, PROMPT_SUBTYPE_PLACEMENTS, true),
    minChoose: promptSubtypeInteger(prompt?.minChoose),
    maxChoose: promptSubtypeInteger(prompt?.maxChoose),
    choiceCount: Array.isArray(prompt?.validChoices) && prompt.validChoices.length <= 512
      ? prompt.validChoices.length : null,
  }))
}
// R4_SELECTOR_METADATA_END

class Deadline {
  constructor(milliseconds) { this.ends = performance.now() + milliseconds }
  remainingOrZero() { return Math.max(0, Math.floor(this.ends - performance.now())) }
  remaining(cap = LIMITS.requestTimeoutMs) {
    const remaining = this.remainingOrZero()
    if (remaining <= 0) throw new RunError('business_budget_exhausted')
    return Math.max(1, Math.min(cap, remaining))
  }
}

const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds))

class BusinessAbort {
  constructor() {
    this.aborted = false
    this.firstFailure = null
    this.cancelError = new RunError('business_cancelled_after_failure')
    this.listeners = new Set()
  }

  abort(error) {
    if (this.aborted) return false
    this.aborted = true
    this.firstFailure = error
    for (const listener of [...this.listeners]) listener(this.cancelError)
    this.listeners.clear()
    return true
  }

  throwIfAborted() {
    if (this.aborted) throw this.cancelError
  }

  subscribe(listener) {
    if (this.aborted) {
      listener(this.cancelError)
      return () => {}
    }
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }
}

async function runBusinessStage(items, operation, businessAbort) {
  businessAbort.throwIfAborted()
  const tasks = items.map((item, index) => {
    let task
    try {
      businessAbort.throwIfAborted()
      task = Promise.resolve(operation(item, index))
    } catch (error) { task = Promise.reject(error) }
    return task.catch(error => {
      businessAbort.abort(error)
      throw error
    })
  })
  const results = await Promise.allSettled(tasks)
  const failed = results.find(result => result.status === 'rejected')
  if (failed) throw businessAbort.firstFailure ?? failed.reason
  return results.map(result => result.value)
}

function businessDelay(milliseconds, businessAbort) {
  businessAbort.throwIfAborted()
  if (milliseconds <= 0) return Promise.resolve()
  return new Promise((resolve, reject) => {
    let settled = false
    let unsubscribe = () => {}
    const finish = error => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      unsubscribe()
      if (error) reject(error); else resolve()
    }
    const timer = setTimeout(() => finish(), milliseconds)
    unsubscribe = businessAbort.subscribe(error => finish(error))
  })
}

function resolveStopFile(rawValue, { existsFn = fs.existsSync } = {}) {
  requireContract(typeof rawValue === 'string' && rawValue.length > 0, 'stop_file_required')
  const stopFile = path.resolve(rawValue)
  requireContract(path.dirname(stopFile).toLowerCase() === TOOL_ROOT.toLowerCase()
    && /^cohort30-terminal-v2-[a-f0-9]{16}\.flag$/.test(path.basename(stopFile)),
  'stop_file_path_refused')
  let exists
  try { exists = existsFn(stopFile) } catch { throw new RunError('stop_file_check_failed') }
  requireContract(exists === false, 'stop_file_not_new')
  return stopFile
}

function stopRequested(stopFile, { existsFn = fs.existsSync } = {}) {
  try { return Boolean(stopFile && existsFn(stopFile)) }
  catch { throw new RunError('stop_file_check_failed') }
}

function assertNotStopped(stopFile, dependencies) {
  if (stopRequested(stopFile, dependencies)) throw new RunError('resource_guard_stop_requested')
}

function parseCli(argv) {
  const values = { execute: false }
  for (let index = 0; index < argv.length; index += 1) {
    const key = argv[index]
    if (key === '--execute' && !values.execute) values.execute = true
    else if (['--candidate', '--manifest', '--config-receipt', '--test-pid', '--formal-pid',
      '--test-dll-sha', '--formal-dll-sha', '--output', '--ledger-run', '--ledger-pipe', '--binding', '--binding-sha'].includes(key)
      && values[key.slice(2)] === undefined) values[key.slice(2)] = argv[++index] ?? ''
    else throw new RunError('cli_invalid')
  }
  requireContract(values.execute, 'execute_required')
  const output = path.resolve(String(values.output ?? ''))
  requireContract(path.dirname(output).toLowerCase() === TOOL_ROOT.toLowerCase()
    && path.extname(output).toLowerCase() === '.json' && !fs.existsSync(output),
  'new_output_beside_tool_required')
  const candidateBinding = loadCandidateBinding(path.resolve(String(values.binding ?? '')), String(values['binding-sha'] ?? ''))
  const runtime = {
    candidate: values.candidate,
    testPid: Number(values['test-pid']),
    formalPid: Number(values['formal-pid']),
    testDllSha256: String(values['test-dll-sha'] ?? '').toUpperCase(),
    formalDllSha256: String(values['formal-dll-sha'] ?? '').toUpperCase(),
  }
  validateRuntimeInputs(runtime)
  requireContract(/^[A-Za-z0-9_-]{8,96}$/.test(values['ledger-run'] ?? '')
    && /^[A-Za-z0-9_-]{16,96}$/.test(values['ledger-pipe'] ?? ''), 'ledger_identity_required')
  return {
    ledgerRun: values['ledger-run'], ledgerPipe: values['ledger-pipe'], candidateBinding,
    output, runtime,
    manifestPath: path.resolve(String(values.manifest ?? '')),
    configReceiptPath: path.resolve(String(values['config-receipt'] ?? '')),
  }
}

async function boundedBody(response, retain = true) {
  if (!response.body) return retain ? '' : undefined
  const reader = response.body.getReader()
  const chunks = []
  let bytes = 0
  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    bytes += value.byteLength
    if (bytes > MAX_JSON_BYTES) {
      try { await reader.cancel() } catch {}
      throw new RunError('http_body_too_large')
    }
    if (retain) chunks.push(Buffer.from(value))
  }
  return retain ? Buffer.concat(chunks, bytes).toString('utf8') : undefined
}

async function request(base, endpoint, deadline, {
  method = 'GET', token, body, expected = [200], retain = true,
} = {}) {
  let response
  try {
    response = await fetch(new URL(endpoint, base), {
      method, redirect: 'error',
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      signal: AbortSignal.timeout(deadline.remaining()),
    })
  } catch { throw new RunError('http_transport_failed') }
  requireContract(expected.includes(response.status), `http_status_${response.status}`)
  const text = await boundedBody(response, retain)
  if (!retain || !text) return null
  try { return JSON.parse(text) } catch { throw new RunError('http_json_invalid') }
}

function exactDeltaValue(message, pathSegments) {
  if (!Array.isArray(message?.changes)) return undefined
  const change = message.changes.find(item => !item?.remove && Array.isArray(item.path)
    && item.path.length === pathSegments.length
    && item.path.every((segment, index) => segment === pathSegments[index]))
  return change?.value
}

function confirmationForRequest(message, requestId) {
  if (message?.type === 'gameState' && message.requestId === requestId) {
    return { accepted: true, revision: message.state?.revision, phase: message.state?.phase }
  }
  if (message?.type === 'gameStateDelta' && exactDeltaValue(message, ['requestId']) === requestId) {
    return { accepted: true, revision: message.revision,
      phase: exactDeltaValue(message, ['state', 'phase']) }
  }
  if (message?.requestId === requestId) return { accepted: false, revision: null, phase: null }
  return null
}

class Peer {
  constructor(token, deadline, stopFile, businessAbort) {
    this.token = token
    this.deadline = deadline
    this.stopFile = stopFile
    this.businessAbort = businessAbort
    this.socket = null
    this.waiters = new Set()
    this.queue = []
    this.closed = false
    this.fatal = null
    this.playerIndex = null
    this.envelope = null
    this.deltaInvalid = false
    this.negotiatedEventWindowDelta = false
    this.boundaryObserver = null
    this.boundaryConnectionSlot = 0
    this.terminalTrace = null
    this.terminalFrame = null
    requireContract(typeof CardFrameRecovery === 'function', 'candidate_protocol_not_initialized')
    this.cardFrameRecovery = new CardFrameRecovery()
    this.negotiatedCardFrame = false
    this.packedFrames = 0
    this.plainFrames = 0
    this.cardFrameDecodeFailures = 0
    this.plainRecoveryRequests = 0
  }

  async connect() {
    this.businessAbort.throwIfAborted()
    requireContract(typeof WebSocket === 'function', 'native_websocket_missing')
    this.socket = new WebSocket(FIXED.wsEndpoint)
    await new Promise((resolve, reject) => {
      let settled = false
      let unsubscribe = () => {}
      const finish = error => {
        if (settled) return
        settled = true
        clearTimeout(timer)
        unsubscribe()
        this.socket.removeEventListener('open', opened)
        this.socket.removeEventListener('error', failed)
        if (error) reject(error); else resolve()
      }
      const opened = () => finish()
      const failed = () => finish(new RunError('ws_open_failed'))
      const timer = setTimeout(() => finish(new RunError('ws_open_timeout')), this.deadline.remaining())
      this.socket.addEventListener('open', opened, { once: true })
      this.socket.addEventListener('error', failed, { once: true })
      unsubscribe = this.businessAbort.subscribe(error => finish(error))
    })
    this.socket.addEventListener('message', event => this.onMessage(event))
    this.socket.addEventListener('close', () => this.fail(new RunError('ws_closed_early')))
    this.socket.addEventListener('error', () => this.fail(new RunError('ws_transport_failed')))
    const session = this.waitFor(message => message?.type === 'session', 'session')
    const recovery = this.waitFor(message => message?.type === 'recoveryComplete', 'recovery')
    const handshake = Promise.all([session, recovery])
    handshake.catch(() => {})
    this.send({ type: 'hello', authToken: this.token, capabilities: {
      requestIds: true, deltaGameState: true, eventWindowDeltaV1: true, presenceDeltaV1: true,
      gameStateCardFrameV1: true,
    } })
    const [sessionMessage] = await handshake
    requireContract(sessionMessage.capabilities?.requestIds === true
      && sessionMessage.capabilities?.deltaGameState === true
      && sessionMessage.capabilities?.eventWindowDeltaV1 === true
      && sessionMessage.capabilities?.gameStateCardFrameV1 === true,
    'required_capability_missing')
    this.negotiatedEventWindowDelta = true
  }

  onMessage(event) {
    const callbackAt = performance.now()
    const raw = typeof event.data === 'string' ? event.data : String(event.data)
    const parseStartedAt = performance.now()
    let message
    try { message = JSON.parse(raw) } catch { this.fail(new RunError('ws_json_invalid')); return }
    const parsedAt = performance.now()
    const wireType = message?.type
    const wireWasPlainGameState = wireType === 'gameState'
    const wireBytes = Buffer.byteLength(raw, 'utf8')
    let restoreMs = 0
    if (wireType === 'session') {
      // Set capabilities at session receipt, before a queued recovery frame can arrive.
      this.negotiatedEventWindowDelta = message.capabilities?.deltaGameState === true
        && message.capabilities?.eventWindowDeltaV1 === true
      this.negotiatedCardFrame = message.capabilities?.gameStateCardFrameV1 === true
    }
    if (wireType === 'gameStateCardFrameV1') {
      const restoreStartedAt = performance.now()
      const rebuilt = this.negotiatedCardFrame && !this.cardFrameRecovery.disabled
        ? restoreCardFrame(message, raw) : null
      restoreMs = performance.now() - restoreStartedAt
      if (!rebuilt) {
        this.cardFrameDecodeFailures++
        if (this.cardFrameRecovery.begin(this.terminalTrace?.requestId ?? '')) {
          this.negotiatedCardFrame = false
          this.plainRecoveryRequests++
          try { this.send({ type: 'syncState', plainFull: true }, { ignoreBusinessAbort: true }) }
          catch { /* The measurement remains failed; cleanup keeps its existing bounded budget. */ }
        }
        this.businessAbort.abort(new RunError('card_frame_reconstruction_failed'))
        return
      }
      message = rebuilt
      this.packedFrames++
    } else if (wireWasPlainGameState) this.plainFrames++
    let marker = null
    if (this.boundaryObserver) {
      marker = this.boundaryObserver.observeParsed({
        connectionSlot: this.boundaryConnectionSlot, message, callbackAt, parseStartedAt, parsedAt,
        bytes: Buffer.byteLength(raw, 'utf8'),
      })
    }
    if (message?.type === 'gameState') { this.envelope = message; this.deltaInvalid = false }
    else if (message?.type === 'gameStateDelta') {
      const rebuilt = reduceEnvelope(this.envelope, message, this.negotiatedEventWindowDelta)
      if (rebuilt) this.envelope = rebuilt
      else { this.deltaInvalid = true; this.fail(new RunError('delta_reconstruction_failed')) }
    }
    if (this.terminalTrace && !this.terminalFrame && message?.type === 'gameState'
        && message.state?.matchId === this.terminalTrace.matchId
        && message.state?.phase === 'GameOver') {
      const sendToCallbackMs = callbackAt - this.terminalTrace.sentAt
      this.terminalFrame = {
        receiver: this.terminalTrace.receiver, type: 'gameState', terminal: true, delta: false,
        wireBytes, wireType, logicalJsonBytes: Buffer.byteLength(JSON.stringify(message), 'utf8'),
        restoreMs, sendToCallbackMs,
        parseMs: parsedAt - parseStartedAt,
        exceededOriginal3s: Number(sendToCallbackMs > COHORT.originalSloMs),
      }
    }
    if (wireWasPlainGameState && this.cardFrameRecovery.active)
      this.cardFrameRecovery.recordPublishedPlainFrame(message)
    if (wireType === 'recoveryComplete' && this.cardFrameRecovery.active) {
      if (!this.cardFrameRecovery.finish().ready)
        this.businessAbort.abort(new RunError('plain_recovery_without_published_plain_frame'))
    }
    let delivered = false
    for (const waiter of [...this.waiters]) {
      let matches = false
      try { matches = waiter.predicate(message) } catch {}
      if (matches) { delivered = true; waiter.finish(null, message) }
    }
    if (!delivered && ['session', 'recoveryComplete', 'roomState', 'gameState', 'gameStateDelta',
      'actionRejected', 'error', 'pong'].includes(message?.type)) {
      this.queue.push(message)
      if (this.queue.length > 256) this.fail(new RunError('ws_queue_bound'))
    }
    if (marker) this.boundaryObserver.afterDispatch(marker, performance.now())
  }

  fail(error) {
    if (this.fatal) return
    this.fatal = error
    for (const waiter of [...this.waiters]) waiter.finish(error)
  }

  send(payload, { ignoreStop = false, ignoreBusinessAbort = false } = {}) {
    if (!ignoreBusinessAbort) this.businessAbort.throwIfAborted()
    if (!ignoreStop) assertNotStopped(this.stopFile)
    requireContract(this.socket?.readyState === WebSocket.OPEN, 'ws_not_open')
    this.socket.send(JSON.stringify(payload))
  }

  waitFor(predicate, label, maximum = COHORT.terminalObservationMs, deadline = this.deadline,
    { ignoreBusinessAbort = false } = {}) {
    requireContract(!this.closed, 'ws_closed')
    if (this.fatal) return Promise.reject(this.fatal)
    if (!ignoreBusinessAbort) this.businessAbort.throwIfAborted()
    const queuedIndex = this.queue.findIndex(message => predicate(message))
    if (queuedIndex >= 0) return Promise.resolve(this.queue.splice(queuedIndex, 1)[0])
    return new Promise((resolve, reject) => {
      let settled = false
      let waiter = null
      let unsubscribe = () => {}
      const timer = setTimeout(() => finish(new RunError(`ws_${label}_timeout`)),
        deadline.remaining(maximum))
      const finish = (error, value) => {
        if (settled) return
        settled = true
        clearTimeout(timer)
        unsubscribe()
        if (waiter) this.waiters.delete(waiter)
        if (error) reject(error); else resolve(value)
      }
      waiter = { predicate, finish }
      if (!ignoreBusinessAbort)
        unsubscribe = this.businessAbort.subscribe(error => finish(error))
      if (!settled) this.waiters.add(waiter)
    })
  }

  async pingDrain() {
    const pong = this.waitFor(message => message?.type === 'pong', 'pong')
    pong.catch(() => {})
    this.send({ type: 'ping' })
    await pong
  }

  close() {
    this.closed = true
    for (const waiter of [...this.waiters]) waiter.finish(new RunError('ws_closed'))
    if (this.socket?.readyState === WebSocket.CONNECTING) {
      this.socket.addEventListener('open', () => {
        try { this.socket.close(1000, 'cohort30 terminal v2 complete') } catch {}
      }, { once: true })
    } else if (this.socket && this.socket.readyState < WebSocket.CLOSING) {
      try { this.socket.close(1000, 'cohort30 terminal v2 complete') } catch {}
    }
  }
}

async function registerAccounts(adminToken, count, deadline, privateValues, ownedAccounts, {
  requestFn = request, delayFn = delay, stopFile, existsFn = fs.existsSync,
  ledger, ledgerReceipt = {},
} = {}) {
  requireContract(Array.isArray(ownedAccounts) && ledger instanceof AccountLedgerClient,
    'owned_account_ledger_invalid')
  const runKey = crypto.randomBytes(4).toString('hex')
  const namePrefix = `c${runKey}`
  for (const deleted of [false, true]) {
    const query = await requestFn(FIXED.publicBase,
      `api/admin/accounts/query?page=1&pageSize=1&search=${encodeURIComponent(namePrefix)}&deleted=${deleted}`,
      deadline, { token: adminToken })
    requireContract(query?.total === 0 && Array.isArray(query?.items) && query.items.length === 0,
      'account_name_prefix_not_unique')
  }
  for (let index = 0; index < count; index += 1) {
    const username = `c${runKey}${index.toString(16).padStart(2, '0')}`
    const password = crypto.randomBytes(24).toString('base64url')
    requireContract(username.length <= 11, 'account_name_invalid')
    privateValues.add(username); privateValues.add(password)
    const intent = {
      username, password, requestedAtUtc: new Date().toISOString(), preflightAbsent: true,
      registrationOutcome: 'pending', id: null, createdAt: null, token: null,
    }
    if (index > 0) await delayFn(2300)
    assertNotStopped(stopFile, { existsFn })
    try {
      // The two real empty-prefix queries above are the ownership preflight. No mutating
      // request can run before the actual durable intent ACK.
      Object.assign(ledgerReceipt, await ledger.persistIntent({
        slot: index, username, password, requestedAtUtc: intent.requestedAtUtc,
        preflightAbsent: true, requestFingerprint: registrationFingerprint(username, password),
      }))
      ownedAccounts.push(intent)
      const created = await requestFn(FIXED.publicBase, 'api/auth/register', deadline, {
        method: 'POST', body: { username, password },
      })
      requireContract(typeof created?.token === 'string' && typeof created?.account?.id === 'string'
        && created.account.username === username && typeof created.account.createdAt === 'string',
      'account_registration_invalid')
      Object.assign(intent, { id: created.account.id, createdAt: created.account.createdAt,
        token: created.token, registrationOutcome: 'confirmed' })
      privateValues.add(intent.id); privateValues.add(intent.token)
      // Keep the in-memory identity for cleanup if the confirmation ACK is lost, but do
      // not return it to room creation until its ID is durably committed.
      Object.assign(ledgerReceipt, await ledger.persistConfirmed({
        slot: index, id: intent.id, createdAtUtc: intent.createdAt,
      }))
    } catch (error) {
      ledger.stop()
      if (intent.registrationOutcome !== 'confirmed') intent.registrationOutcome = 'unknown'
      throw error
    }
  }
  return ownedAccounts
}

async function startRoom(room, businessAbort) {
  await runBusinessStage([room.host, room.guest], peer => peer.connect(), businessAbort)
  room.creationAttempted = true
  room.host.send({ type: 'createRoom', options: { matchModeId: 'friendly' } })
  const created = await room.host.waitFor(message => message?.type === 'roomState'
    && typeof message.roomCode === 'string', 'create_room')
  room.roomCode = created.roomCode
  room.roomCreated = true
  room.privateValues.add(room.roomCode)
  room.guest.send({ type: 'joinRoom', roomCode: room.roomCode })
  await runBusinessStage([room.host, room.guest], (peer, index) => peer.waitFor(
    message => message?.type === 'roomState' && message.roomCode === room.roomCode,
    index === 0 ? 'host_joined_room' : 'guest_joined_room'), businessAbort)
  room.host.send({ type: 'ready', ready: true })
  await room.host.waitFor(message => message?.type === 'roomState' && message.roomCode === room.roomCode,
    'host_ready')
  room.gameStartAttempted = true
  room.guest.send({ type: 'ready', ready: true })
  const states = await runBusinessStage([room.host, room.guest], (peer, index) => peer.waitFor(
    message => message?.type === 'gameState' && typeof message.state?.matchId === 'string',
    index === 0 ? 'host_game' : 'guest_game'), businessAbort)
  const matchId = states[0].state.matchId
  requireContract(matchId && states[1].state.matchId === matchId, 'match_identity_mismatch')
  room.matchId = matchId
  room.privateValues.add(matchId)
  room.host.playerIndex = states[0].state.you
  room.guest.playerIndex = states[1].state.you
  requireContract(Number.isInteger(room.host.playerIndex) && Number.isInteger(room.guest.playerIndex)
    && room.host.playerIndex !== room.guest.playerIndex, 'player_projection_invalid')
  room.active = true
}

function projectionBytes(peer) {
  requireContract(isPlainObject(peer.envelope) && isPlainObject(peer.envelope.state)
    && !peer.deltaInvalid, 'authoritative_projection_missing')
  return Buffer.byteLength(JSON.stringify(peer.envelope), 'utf8')
}

async function sendLegalCommand(room, peer, snapshot, selected, businessAbort) {
  requireContract(!room.attemptedRevisionKeys.has(selected.revisionKey), 'duplicate_revision_command')
  const elapsed = performance.now() - room.lastCommandAt
  if (elapsed < COHORT.actionIntervalMs)
    await businessDelay(COHORT.actionIntervalMs - elapsed, businessAbort)
  const requestId = crypto.randomUUID()
  room.privateValues.add(requestId)
  const confirmation = peer.waitFor(message => confirmationForRequest(message, requestId) !== null,
    'command_confirmation')
  confirmation.catch(() => {})
  room.attemptedRevisionKeys.add(selected.revisionKey)
  if (selected.turnKey) room.playedTurns.add(selected.turnKey)
  room.lastCommandAt = performance.now()
  peer.send({ type: 'gameAction', command: selected.command, requestId })
  const message = await confirmation
  const result = confirmationForRequest(message, requestId)
  requireContract(result?.accepted === true && Number.isInteger(result.revision)
    && result.revision > snapshot.revision, 'command_not_authoritatively_accepted')
  const waitRevision = async target => {
    if (Number.isInteger(target.envelope?.state?.revision)
        && target.envelope.state.revision >= result.revision) return
    await target.waitFor(message => (message?.type === 'gameState'
        && Number.isInteger(message.state?.revision) && message.state.revision >= result.revision)
      || (message?.type === 'gameStateDelta' && Number.isInteger(message.revision)
        && message.revision >= result.revision), 'projection_convergence')
  }
  await runBusinessStage([room.host, room.guest], waitRevision, businessAbort)
  requireContract(room.host.envelope?.state?.revision === room.guest.envelope?.state?.revision,
    'projection_revision_diverged')
  room.acceptedCommands += 1
  room.commandCounts[selected.command.type] = (room.commandCounts[selected.command.type] ?? 0) + 1
}

async function prepareLargeRoom(room, businessAbort) {
  while (room.acceptedCommands < COHORT.maximumAcceptedCommandsPerRoom) {
    requireContract(room.host.envelope?.state?.phase !== 'GameOver'
      && room.guest.envelope?.state?.phase !== 'GameOver', 'natural_terminal_before_probe')
    const sizes = [projectionBytes(room.host), projectionBytes(room.guest)]
    requireContract(Math.max(...sizes) <= COHORT.maximumTerminalBytes, 'preterminal_projection_too_large')
    if (Math.min(...sizes) >= COHORT.preferredTerminalBytes) {
      room.preTerminalBytes = sizes
      return
    }
    let selected = null
    let unsupported = false
    // R4_SELECTOR_UNSUPPORTED_DECLARATION_BEGIN
    let unsupportedPromptSubtypes = []
    // R4_SELECTOR_UNSUPPORTED_DECLARATION_END
    for (const peer of [room.host, room.guest]) {
      const snapshot = structuredClone(peer.envelope.state)
      const candidate = selectLegalCommand(peer, snapshot, room, isLegionBlockedByVisibleDisaster)
      if (candidate.command) { selected = { peer, snapshot, ...candidate }; break }
      // R4_SELECTOR_UNSUPPORTED_BRANCH_BEGIN
      if (candidate.skip === 'unsupported_authoritative_prompt') {
        unsupported = true
        if (unsupportedPromptSubtypes.length === 0)
          unsupportedPromptSubtypes = sanitizeUnsupportedPromptSubtypes(snapshot, peer.playerIndex)
      }
      // R4_SELECTOR_UNSUPPORTED_BRANCH_END
    }
    // R4_SELECTOR_CAPTURE_BEGIN
    if (selected === null && unsupported && room.firstUnsupportedPromptSubtypes.length === 0)
      room.firstUnsupportedPromptSubtypes = unsupportedPromptSubtypes
    // R4_SELECTOR_CAPTURE_END
    requireContract(selected !== null, unsupported
      ? 'unsupported_authoritative_prompt' : 'no_legal_progress_command')
    await sendLegalCommand(room, selected.peer, selected.snapshot, selected, businessAbort)
  }
  throw new RunError('large_state_not_reached_within_96')
}

async function quiesceRoom(room, businessAbort) {
  await runBusinessStage([room.host, room.guest], peer => peer.pingDrain(), businessAbort)
  await businessDelay(500, businessAbort)
  await runBusinessStage([room.host, room.guest], peer => peer.pingDrain(), businessAbort)
  const host = room.host.envelope?.state
  const guest = room.guest.envelope?.state
  requireContract(host?.matchId === room.matchId && guest?.matchId === room.matchId
    && host.revision === guest.revision && host.phase !== 'GameOver' && guest.phase !== 'GameOver',
  'quiescent_projection_mismatch')
}

function attemptTerminalSend(room, payload) {
  requireContract(room.terminalSent !== true, 'terminal_already_attempted')
  room.terminalSent = true
  room.host.send(payload)
}

async function terminalBarrier(rooms, nonce, businessAbort) {
  assertNotStopped(rooms[0]?.host?.stopFile)
  requireContract(rooms.length === COHORT.roomCount, 'terminal_room_count_invalid')
  const waits = []
  for (const room of rooms) {
    const diagnosticSlot = diagnosticSlotForRoom(room.slot)
    room.diagnosticSlot = diagnosticSlot
    if (diagnosticSlot !== null) {
      room.observer = new ClientBoundaries({
        nonce,
        sendReceipt: (_connectionSlot, payload) => room.host.send(payload, { ignoreStop: true }),
      })
      room.host.boundaryObserver = room.observer
      room.host.boundaryConnectionSlot = 0
      room.terminalRequestId = `l12diag:${nonce}:${diagnosticSlot}:${crypto.randomUUID().replaceAll('-', '')}`
    } else room.terminalRequestId = crypto.randomUUID()
    room.privateValues.add(room.terminalRequestId)
    const wait = Promise.all([
      room.host.waitFor(message => message?.type === 'gameState'
        && message.requestId === room.terminalRequestId && message.state?.matchId === room.matchId
        && message.state?.phase === 'GameOver', 'actor_terminal'),
      room.guest.waitFor(message => message?.type === 'gameState'
        && message.state?.matchId === room.matchId && message.state?.phase === 'GameOver',
      'peer_terminal'),
    ])
    wait.catch(() => {})
    waits.push(wait)
  }
  const startedUtc = new Date().toISOString()
  const barrierStart = performance.now()
  const sends = []
  let sendFailure = null
  try {
    assertNotStopped(rooms[0]?.host?.stopFile)
    for (const room of rooms) {
      const sentAt = performance.now()
      sends.push(sentAt - barrierStart)
      room.host.terminalTrace = { sentAt, matchId: room.matchId, requestId: room.terminalRequestId, receiver: 0 }
      room.guest.terminalTrace = { sentAt, matchId: room.matchId, requestId: room.terminalRequestId, receiver: 1 }
      if (room.observer) {
        requireContract(room.observer.begin({
          slot: room.diagnosticSlot, connectionSlot: 0, requestId: room.terminalRequestId,
          commandType: 'surrender', sentAt,
        }), 'client_observer_begin_failed')
      }
      attemptTerminalSend(room, { type: 'gameAction', requestId: room.terminalRequestId,
        command: { type: 'surrender' } })
    }
  } catch (error) {
    sendFailure = error
    businessAbort.abort(error)
  }
  if (sendFailure) {
    await Promise.allSettled(waits)
    throw sendFailure
  }
  const spread = Math.max(...sends) - Math.min(...sends)
  try {
    requireContract(spread <= COHORT.barrierMaximumMs, 'barrier_spread_exceeded')
  } catch (error) {
    businessAbort.abort(error)
    await Promise.allSettled(waits)
    throw error
  }
  await runBusinessStage(waits, wait => wait, businessAbort)
  for (const room of rooms) {
    requireContract(room.host.terminalFrame && room.guest.terminalFrame, 'terminal_frame_missing')
    room.terminalConfirmed = true
    room.active = false
  }
  return { sends, spread, startedUtc, completedUtc: new Date().toISOString() }
}

function cleanupRemaining(deadline) {
  if (typeof deadline?.remainingOrZero === 'function') return deadline.remainingOrZero()
  try { return deadline.remaining(COHORT.cleanupBudgetMs) } catch { return 0 }
}

function markResidual(summary, kind, count = 1) {
  summary.residuals[kind] += count
  summary.failures += count
}

function markCleanupDeadline(summary) {
  if (summary.residuals.cleanupDeadlineExceeded === 0)
    markResidual(summary, 'cleanupDeadlineExceeded')
}

function sampleCleanupStop(summary, stopFile, existsFn) {
  try { summary.guardStopObserved ||= stopRequested(stopFile, { existsFn }) }
  catch {
    if (summary.residuals.guardCheckUnknown === 0) markResidual(summary, 'guardCheckUnknown')
    summary.guardStopObserved = true
  }
}

function authoritativeGameOver(room) {
  const host = room.host?.envelope?.state
  const guest = room.guest?.envelope?.state
  const matchId = room.matchId ?? host?.matchId ?? guest?.matchId
  return typeof matchId === 'string' && matchId.length > 0
    && host?.matchId === matchId && guest?.matchId === matchId
    && host.phase === 'GameOver' && guest.phase === 'GameOver'
}

async function waitForAuthoritativeGameOver(room, deadline) {
  if (authoritativeGameOver(room)) return true
  const remaining = cleanupRemaining(deadline)
  if (remaining <= 0) return false
  const knownMatchId = room.matchId ?? room.host?.envelope?.state?.matchId
    ?? room.guest?.envelope?.state?.matchId ?? null
  const terminal = message => message?.type === 'gameState'
    && message.state?.phase === 'GameOver'
    && (knownMatchId === null || message.state?.matchId === knownMatchId)
  try {
    await Promise.all([room.host, room.guest].map(peer => {
      const state = peer.envelope?.state
      if (state?.phase === 'GameOver'
          && (knownMatchId === null || state.matchId === knownMatchId)) return Promise.resolve()
      return peer.waitFor(terminal, 'cleanup_terminal',
        Math.min(COHORT.terminalObservationMs, remaining), deadline,
        { ignoreBusinessAbort: true })
    }))
    const observed = room.host.envelope?.state?.matchId ?? room.guest.envelope?.state?.matchId
    if (room.matchId === null && typeof observed === 'string') room.matchId = observed
    return authoritativeGameOver(room)
  } catch { return false }
}

async function closeKnownPregameRoom(room, deadline) {
  if (!room.roomCode || cleanupRemaining(deadline) <= 0) return false
  try {
    const result = room.host.waitFor(message => message?.type === 'roomLeft'
      || message?.type === 'error', 'cleanup_leave_room',
    Math.min(COHORT.terminalObservationMs, cleanupRemaining(deadline)), deadline,
    { ignoreBusinessAbort: true })
    result.catch(() => {})
    room.host.send({ type: 'leaveRoom' },
      { ignoreStop: true, ignoreBusinessAbort: true })
    const response = await result
    return response?.type === 'roomLeft'
  } catch { return false }
}

async function cleanupRoom(room, deadline, summary, {
  waitForGameOverFn = waitForAuthoritativeGameOver,
  closePregameFn = closeKnownPregameRoom,
} = {}) {
  if (cleanupRemaining(deadline) <= 0) {
    markCleanupDeadline(summary)
    return
  }
  if (room.terminalSent) {
    if (await waitForGameOverFn(room, deadline)) {
      room.terminalConfirmed = true
      room.active = false
      summary.terminalRoomsConfirmed += 1
    } else markResidual(summary, 'terminalOutcomeUnknown')
    return
  }
  const projectedMatch = room.matchId ?? room.host?.envelope?.state?.matchId
    ?? room.guest?.envelope?.state?.matchId
  if (room.active || room.gameStartAttempted || typeof projectedMatch === 'string') {
    try {
      room.terminalSent = true
      const requestId = crypto.randomUUID()
      room.host.send({ type: 'gameAction', requestId, command: { type: 'surrender' } },
        { ignoreStop: true, ignoreBusinessAbort: true })
      summary.ordinarySurrenders += 1
    } catch {
      markResidual(summary, 'terminalOutcomeUnknown')
      return
    }
    if (await waitForGameOverFn(room, deadline)) {
      room.terminalConfirmed = true
      room.active = false
      summary.terminalRoomsConfirmed += 1
    } else markResidual(summary, 'terminalOutcomeUnknown')
    return
  }
  if (room.roomCode) {
    if (await closePregameFn(room, deadline)) {
      room.roomClosed = true
      summary.pregameRoomsClosed += 1
    } else markResidual(summary, 'roomLifecycleUnknown')
  } else if (room.creationAttempted) markResidual(summary, 'roomLifecycleUnknown')
}

function exactIntentAccount(view, intent) {
  const createdAt = Date.parse(view?.createdAt)
  const requestedAt = Date.parse(intent.requestedAtUtc)
  return view?.username === intent.username && view?.role === 'player'
    && typeof view?.id === 'string' && view.id.length > 0
    && Number.isFinite(createdAt) && Number.isFinite(requestedAt)
    && createdAt >= requestedAt - 5 * 60 * 1000
    && createdAt <= Date.now() + 5 * 60 * 1000
}

async function queryExactIntent(intent, deleted, adminToken, deadline, requestFn) {
  const page = await requestFn(FIXED.publicBase,
    `api/admin/accounts/query?page=1&pageSize=2&search=${encodeURIComponent(intent.username)}&deleted=${deleted}`,
    deadline, { token: adminToken })
  requireContract(Array.isArray(page?.items) && Number.isInteger(page?.total)
    && page.total >= 0 && page.total <= 2, 'intent_query_invalid')
  if (page.total === 0 && page.items.length === 0) return null
  requireContract(page.total === 1 && page.items.length === 1
    && exactIntentAccount(page.items[0], intent), 'intent_query_not_exact')
  return page.items[0]
}

async function reconcileRegistrationIntent(intent, adminToken, deadline, privateValues, requestFn = request) {
  if (typeof intent.id === 'string' && typeof intent.createdAt === 'string'
      && typeof intent.token === 'string' && intent.registrationOutcome === 'confirmed') {
    return { status: 'present', account: intent }
  }
  requireContract(intent.preflightAbsent === true && typeof intent.username === 'string'
    && typeof intent.password === 'string', 'registration_intent_invalid')
  let loginFailureCode = null
  try {
    const login = await requestFn(FIXED.publicBase, 'api/auth/login', deadline, {
      method: 'POST', body: { username: intent.username, password: intent.password },
    })
    requireContract(typeof login?.token === 'string' && exactIntentAccount(login?.account, intent),
      'intent_login_not_exact')
    Object.assign(intent, { id: login.account.id, createdAt: login.account.createdAt,
      token: login.token, registrationOutcome: 'recovered-login' })
    privateValues.add(intent.id); privateValues.add(intent.token)
    return { status: 'present', account: intent }
  } catch (error) { loginFailureCode = error?.code ?? 'unknown' }
  const active = await queryExactIntent(intent, false, adminToken, deadline, requestFn)
  if (active) {
    // A definitive credential rejection means the exact-name account is not proven to be ours;
    // never delete it merely because its public fields resemble this intent.
    requireContract(loginFailureCode !== 'http_status_401', 'intent_credentials_rejected')
    Object.assign(intent, { id: active.id, createdAt: active.createdAt,
      token: null, registrationOutcome: 'recovered-admin' })
    privateValues.add(intent.id)
    return { status: 'present', account: intent }
  }
  const deleted = await queryExactIntent(intent, true, adminToken, deadline, requestFn)
  if (deleted) return { status: 'already-deleted', account: deleted }
  intent.registrationOutcome = 'not-committed'
  return { status: 'absent', account: null }
}

async function cleanup({ rooms, accounts, adminToken, deadline, stopFile, privateValues }, {
  requestFn = request, delayFn = delay, existsFn = fs.existsSync,
  waitForGameOverFn = waitForAuthoritativeGameOver,
  closePregameFn = closeKnownPregameRoom,
  reconcileIntentFn = reconcileRegistrationIntent,
} = {}) {
  const cleanupStartedAt = performance.now()
  const summary = { ordinarySurrenders: 0, pregameRoomsClosed: 0,
    terminalRoomsConfirmed: 0, socketsClosed: 0, playerSessionsRevoked: 0,
    adminSessionRevoked: 0, syntheticAccountsDeleted: 0, failures: 0,
    guardStopObserved: false, residuals: {
      terminalOutcomeUnknown: 0, roomLifecycleUnknown: 0,
      registrationOutcomeUnknown: 0, accountOwnershipUnknown: 0,
      accountDeletionUnconfirmed: 0, cleanupDeadlineExceeded: 0,
      guardCheckUnknown: 0,
    } }
  sampleCleanupStop(summary, stopFile, existsFn)
  await Promise.all(rooms.map(room => cleanupRoom(room, deadline, summary,
    { waitForGameOverFn, closePregameFn })))
  sampleCleanupStop(summary, stopFile, existsFn)
  for (const room of rooms) {
    for (const peer of [room.host, room.guest]) {
      try { peer.close(); summary.socketsClosed += 1 } catch { summary.failures += 1 }
    }
  }

  const resolved = []
  for (let index = 0; index < accounts.length; index += 1) {
    if (cleanupRemaining(deadline) <= 0) {
      markCleanupDeadline(summary)
      break
    }
    try {
      const result = await reconcileIntentFn(accounts[index], adminToken, deadline,
        privateValues, requestFn)
      if (result.status === 'present') resolved.push(result.account)
      else if (result.status === 'already-deleted') summary.syntheticAccountsDeleted += 1
    } catch {
      markResidual(summary, 'registrationOutcomeUnknown')
      summary.residuals.accountOwnershipUnknown += 1
    }
  }

  for (let index = 0; index < resolved.length; index += 1) {
    const account = resolved[index]
    if (cleanupRemaining(deadline) <= 0) {
      markCleanupDeadline(summary)
      break
    }
    try {
      await requestFn(FIXED.publicBase,
        `api/admin/accounts/${encodeURIComponent(account.id)}/sessions`, deadline,
        { method: 'DELETE', token: adminToken, expected: [200, 204], retain: false })
      summary.playerSessionsRevoked += 1
    } catch { summary.failures += 1 }
  }

  for (let index = 0; index < resolved.length; index += 1) {
    const account = resolved[index]
    const remaining = cleanupRemaining(deadline)
    if (remaining <= 1300) {
      markCleanupDeadline(summary)
      break
    }
    try {
      await delayFn(1300)
      if (cleanupRemaining(deadline) <= 0) throw new RunError('cleanup_deadline_exhausted')
      const health = await requestFn(FIXED.publicBase, 'health', deadline)
      requireContract(health?.status === 'ok' && health?.serverVersion === FIXED.candidate,
        'cleanup_candidate_changed')
      const detail = await requestFn(FIXED.publicBase,
        `api/admin/accounts/${encodeURIComponent(account.id)}`, deadline, { token: adminToken })
      requireContract(detail?.id === account.id && detail?.username === account.username
        && detail?.createdAt === account.createdAt && detail?.role === 'player'
        && detail?.deleted !== true && Number.isInteger(detail?.permissionVersion),
      'cleanup_ownership_invalid')
      try {
        await requestFn(FIXED.publicBase,
          `api/admin/v1/accounts/${encodeURIComponent(account.id)}/delete`, deadline, {
            method: 'POST', token: adminToken, body: {
              reason: 'testrun bounded cohort30 terminal v2 cleanup',
              idempotencyKey: `cohort30-delete-${crypto.randomUUID()}`,
              expectedVersion: detail.permissionVersion, dryRun: false,
            }, expected: [200, 204], retain: false,
          })
      } catch {}
      const verified = await requestFn(FIXED.publicBase,
        `api/admin/accounts/${encodeURIComponent(account.id)}`, deadline, { token: adminToken })
      requireContract(verified?.id === account.id && verified?.deleted === true
        && verified?.username !== account.username, 'cleanup_delete_unconfirmed')
      summary.syntheticAccountsDeleted += 1
    } catch { markResidual(summary, 'accountDeletionUnconfirmed') }
    sampleCleanupStop(summary, stopFile, existsFn)
  }
  if (cleanupRemaining(deadline) > 0) {
    try {
      await requestFn(FIXED.publicBase, 'api/auth/sessions/current', deadline,
        { method: 'DELETE', token: adminToken, expected: [200, 204], retain: false })
      summary.adminSessionRevoked = 1
    } catch { summary.failures += 1 }
  } else markCleanupDeadline(summary)
  sampleCleanupStop(summary, stopFile, existsFn)
  if (cleanupRemaining(deadline) <= 0) markCleanupDeadline(summary)
  summary.durationMs = performance.now() - cleanupStartedAt
  return summary
}

function ensurePrivateValuesAbsent(report, values) {
  const serialized = JSON.stringify(report)
  for (const value of values) {
    if (typeof value === 'string' && value.length > 0)
      requireContract(!serialized.includes(value), 'private_value_in_report')
  }
  const banned = new Set(['accountId', 'username', 'password', 'token', 'authToken', 'roomCode',
    'matchId', 'nonce', 'diagnosticNonce', 'requestId'])
  const visit = value => {
    if (Array.isArray(value)) { value.forEach(visit); return }
    if (!isPlainObject(value)) return
    for (const [key, child] of Object.entries(value)) {
      requireContract(!banned.has(key), 'private_key_in_report')
      visit(child)
    }
  }
  visit(report)
}

async function run(options) {
  assertGeneratedRoot()
  requireContract(FIXED.bindingSha256 && !FIXED.syntheticOnly, 'real_candidate_binding_required')
  throw new RunError('public_execution_disabled_local_slice')
  await initializeCandidateProtocol()
  const ledger = new AccountLedgerClient({ runId: options.ledgerRun, maxSlots: COHORT.connectionCount,
    transport: new NamedPipeLedgerTransport({ pipeName: options.ledgerPipe }) })
  const ledgerReceipt = { seq: 0, count: 0, cipherSha256: null }
  const startedAtUtc = new Date().toISOString()
  const operationStart = performance.now()
  const privateValues = new Set()
  const nonce = process.env[NONCE_ENV] ?? ''
  const adminToken = process.env[ADMIN_ENV] ?? ''
  const stopFileRaw = process.env[STOP_ENV] ?? ''
  delete process.env[NONCE_ENV]
  delete process.env[ADMIN_ENV]
  delete process.env[STOP_ENV]
  privateValues.add(nonce); privateValues.add(adminToken)
  requireContract(/^[a-f0-9]{32}$/.test(nonce), 'nonce_invalid')
  requireContract(/^[a-f0-9]{64}$/.test(adminToken), 'admin_session_invalid')
  const stopFile = resolveStopFile(stopFileRaw)
  const nonceSha256 = sha256Text(nonce)
  const artifactBinding = validateManifest(options.manifestPath)
  const install = readJsonFile(options.configReceiptPath, 64 * 1024)
  validateOwnedReceipt(install, { count: COHORT.traceSampleCount, nonceSha256,
    testPid: options.runtime.testPid })
  const configReceiptSha256 = sha256File(options.configReceiptPath, 64 * 1024)
  const deadline = new Deadline(COHORT.businessBudgetMs)
  const businessAbort = new BusinessAbort()
  const rooms = []
  const accounts = []
  let cleanupSummary = { failures: 1 }
  let failure = null
  let stage = 'health'
  let barrier = { sends: [], spread: 0 }
  let guardStopObserved = false
  try {
    const [testHealth, formalHealth] = await Promise.all([
      request(FIXED.publicBase, 'health', deadline), request(FIXED.formalBase, 'health', deadline),
    ])
    requireContract(testHealth?.status === 'ok' && testHealth?.serverVersion === FIXED.candidate,
      'test_health_mismatch')
    requireContract(formalHealth?.status === 'ok'
      && formalHealth?.serverVersion === FIXED.formalCandidate, 'formal_health_mismatch')
    stage = 'admin_identity'
    const admin = await request(FIXED.publicBase, 'api/auth/me', deadline, { token: adminToken })
    requireContract(admin?.role === 'admin' && typeof admin?.id === 'string', 'admin_identity_invalid')
    privateValues.add(admin.id)
    stage = 'register_accounts'
    try {
      await registerAccounts(adminToken, COHORT.connectionCount, deadline, privateValues, accounts,
        { stopFile, ledger, ledgerReceipt })
    } finally { ledger.close() }
    stage = 'start_rooms'
    for (let slot = 0; slot < COHORT.roomCount; slot += 1) {
      rooms.push({
        slot, host: new Peer(accounts[slot * 2].token, deadline, stopFile, businessAbort),
        guest: new Peer(accounts[slot * 2 + 1].token, deadline, stopFile, businessAbort),
        privateValues, playedTurns: new Set(), attemptedRevisionKeys: new Set(),
        acceptedCommands: 0, commandCounts: {}, lastCommandAt: 0,
        roomCode: null, matchId: null, active: false, terminalSent: false,
        terminalConfirmed: false, creationAttempted: false, roomCreated: false,
        gameStartAttempted: false, roomClosed: false,
        terminalResends: 0, diagnosticSlot: diagnosticSlotForRoom(slot), preTerminalBytes: null,
        firstUnsupportedPromptSubtypes: [],
      })
    }
    await runBusinessStage(rooms, room => startRoom(room, businessAbort), businessAbort)
    stage = 'legal_large_state'
    await runBusinessStage(rooms, room => prepareLargeRoom(room, businessAbort), businessAbort)
    stage = 'quiesce'
    await runBusinessStage(rooms, room => quiesceRoom(room, businessAbort), businessAbort)
    stage = 'terminal_barrier'
    barrier = await runBusinessStage([rooms], value => terminalBarrier(value, nonce, businessAbort),
      businessAbort).then(values => values[0])
    stage = 'client_validation'
  } catch (error) {
    failure = error instanceof ContractError ? error : new RunError('unexpected_failure')
    businessAbort.abort(failure)
    guardStopObserved = failure.code === 'resource_guard_stop_requested'
  } finally {
    ledger.close()
    try { guardStopObserved ||= stopRequested(stopFile) } catch { guardStopObserved = true }
    const cleanupDeadline = new Deadline(COHORT.cleanupBudgetMs)
    cleanupSummary = await cleanup({ rooms, accounts, adminToken, deadline: cleanupDeadline,
      stopFile, privateValues })
    guardStopObserved ||= cleanupSummary.guardStopObserved
    try { guardStopObserved ||= stopRequested(stopFile) } catch { guardStopObserved = true }
  }

  const boundaryRows = rooms.flatMap(room => (room.observer?.report().rows ?? [])
    .map(row => ({ ...row, roomSlot: room.slot }))).sort((left, right) => left.slot - right.slot)
  // R4_SELECTOR_REPORT_BEGIN
  const firstUnsupportedPromptSubtypes = []
  const unsupportedKeys = new Set()
  for (const room of [...rooms].sort((left, right) => left.slot - right.slot)) {
    for (const row of room.firstUnsupportedPromptSubtypes ?? []) {
      const key = JSON.stringify(row)
      if (!unsupportedKeys.has(key) && firstUnsupportedPromptSubtypes.length < 8) {
        unsupportedKeys.add(key)
        firstUnsupportedPromptSubtypes.push(row)
      }
    }
  }
  // R4_SELECTOR_REPORT_END
  const reportRooms = rooms.map(room => ({
    slot: room.slot, diagnosticSlot: room.diagnosticSlot,
    acceptedCommands: room.acceptedCommands, commandCounts: room.commandCounts,
    preTerminalBytes: room.preTerminalBytes, terminalAttempted: room.terminalSent === true,
    terminalResends: room.terminalResends,
    terminalFrames: [room.host.terminalFrame, room.guest.terminalFrame].filter(Boolean),
  }))
  const sloBreached = reportRooms.some(room => room.terminalFrames
    .some(frame => frame.sendToCallbackMs > COHORT.originalSloMs))
  const frameProtocol = rooms.reduce((total, room) => {
    for (const peer of [room.host, room.guest]) {
      total.packedFrames += peer.packedFrames; total.plainFrames += peer.plainFrames
      total.decoderFailures += peer.cardFrameDecodeFailures
      total.plainRecoveryRequests += peer.plainRecoveryRequests
    }
    return total
  }, { capability: 'gameStateCardFrameV1', packedFrames: 0, plainFrames: 0,
    decoderFailures: 0, plainRecoveryRequests: 0 })
  if (!failure && frameProtocol.decoderFailures > 0) failure = new RunError('card_frame_reconstruction_failed')
  if (!failure && cleanupSummary.failures > 0) failure = new RunError('cleanup_incomplete')
  if (!failure && guardStopObserved) failure = new RunError('resource_guard_stop_requested')
  let status = failure ? 'inconclusive' : sloBreached
    ? 'completed-with-slo-failure-awaiting-server-and-removal'
    : 'client-completed-awaiting-server-and-removal'
  const report = {
    schema: 1, probe: COHORT.probe, status,
    candidate: FIXED.candidate, formalCandidate: FIXED.formalCandidate,
    roomCount: COHORT.roomCount, connectionCount: COHORT.connectionCount,
    expectedSamples: COHORT.traceSampleCount, traceSampleCount: COHORT.traceSampleCount,
    traceRoomSlots: [...COHORT.traceRoomSlots], ordinaryRoomCount: COHORT.ordinaryRoomCount,
    productStateInjected: false, paddingUsed: false, terminalFullRequired: true,
    nativeFirstByteMeasured: false, oneWayNetworkMeasured: false,
    requesterOnlyServerTrace: true, peerServerTraceMeasured: false,
    maximumAcceptedCommandsPerRoom: COHORT.maximumAcceptedCommandsPerRoom,
    actionIntervalMs: COHORT.actionIntervalMs, originalSloMs: COHORT.originalSloMs,
    businessBudgetMs: COHORT.businessBudgetMs,
    runtime: {
      testPid: options.runtime.testPid, formalPid: options.runtime.formalPid,
      testDllSha256: options.runtime.testDllSha256,
      formalDllSha256: options.runtime.formalDllSha256,
    },
    bindings: {
      nonceSha256, configReceiptSha256, ...artifactBinding,
      candidateBindingSha256: FIXED.bindingSha256, cardFrameSha256: FIXED.cardFrameSha256,
      deployedNetSha256: FIXED.deployedNetSha256,
    },
    firstUnsupportedPromptSubtypes,
    boundaryRows, rooms: reportRooms,
    terminalAttemptCount: reportRooms.filter(room => room.terminalAttempted).length,
    terminalResendCount: reportRooms.reduce((sum, room) => sum + room.terminalResends, 0),
    diagnosticRequestCount: reportRooms.filter(room => room.diagnosticSlot !== null).length,
    ordinaryRequestCount: reportRooms.filter(room => room.diagnosticSlot === null).length,
    terminalFrameCount: reportRooms.reduce((sum, room) => sum + room.terminalFrames.length, 0),
    barrierSendOffsetsMs: barrier.sends, barrierSpreadMs: barrier.spread,
    terminalWindow: { startUtc: barrier.startedUtc ?? startedAtUtc,
      endUtc: barrier.completedUtc ?? new Date().toISOString() },
    sloBreached, cleanup: cleanupSummary, ledger: { ...ledgerReceipt }, frameProtocol,
    completeOriginalWorkloadToolReady: false,
    resourceGuardStopFileConfigured: true, guardStopObserved,
    serverEvidenceRequired: true, removalReceiptRequired: true,
    aggregateMetrics: { available: false, reason: 'not_collected_by_business_runner' },
    payloadsExported: 0, identifiersExported: 0,
    startedAtUtc, durationMs: performance.now() - operationStart,
    ...(failure ? { failure: { stage, code: failure.code } } : {}),
  }
  if (!failure) {
    try { validateClientCohortReport(report) }
    catch (error) {
      failure = error instanceof ContractError ? error : new RunError('client_contract_invalid')
      status = report.status = 'inconclusive'
      report.failure = { stage: 'client_validation', code: failure.code }
    }
  }
  ensurePrivateValuesAbsent(report, privateValues)
  const descriptor = fs.openSync(options.output, 'wx', 0o600)
  try {
    fs.writeFileSync(descriptor, `${JSON.stringify(report, null, 2)}\n`, 'utf8')
    fs.fsyncSync(descriptor)
  } finally { fs.closeSync(descriptor) }
  for (const account of accounts) {
    account.id = ''; account.username = ''; account.password = ''; account.token = ''
  }
  privateValues.clear()
  return { report, exitCode: failure ? 1 : sloBreached ? 2 : 0 }
}

export { parseCli, run }
export const toolTestHooks = Object.freeze({
  registerAccounts, cleanup, cleanupRoom, reconcileRegistrationIntent,
  waitForAuthoritativeGameOver, closeKnownPregameRoom,
  attemptTerminalSend, resolveStopFile, assertNotStopped,
  BusinessAbort, runBusinessStage, businessDelay, Peer,
})

const direct = process.argv[1]
  && path.resolve(process.argv[1]).toLowerCase() === fileURLToPath(import.meta.url).toLowerCase()
if (direct) {
  let options
  try { options = parseCli(process.argv.slice(2)) }
  catch (error) {
    process.stderr.write(`${JSON.stringify({ status: 'refused', error: sanitizedFailure(error) })}\n`)
    process.exitCode = 1
  }
  if (options) {
    run(options).then(({ report, exitCode }) => {
      process.stdout.write(`${JSON.stringify({ status: report.status, probe: report.probe,
        roomCount: report.roomCount, traceSampleCount: report.traceSampleCount,
        output: path.basename(options.output) })}\n`)
      process.exitCode = exitCode
    }).catch(error => {
      process.stderr.write(`${JSON.stringify({ status: 'failed-before-report',
        error: sanitizedFailure(error) })}\n`)
      process.exitCode = 1
    })
  }
}
