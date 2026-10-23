import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import net from 'node:net'
import {spawn, execFileSync} from 'node:child_process'
import {pathToFileURL} from 'node:url'
import {REPO, EVIDENCE_ROOT, CACHE_ROOT, assertTestrunTarget, assertGeneratedDirectories, digest} from './testrun-pressure-protocol.mjs'

const READER = path.join(REPO, 'ops/qa/Read-TestrunPressureLedgers.ps1')
const LEGACY = 'C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/cohort30-terminal-v2/ledger-r5'
const SINK_SHA = 'B74A6359DA15536248EE660F01B33DFD8A59376AF6D930DDC0F2BC54CC94DA81'
const CLIENT_SHA = '88BB6ACB68C3867CEC7A5E3DBA5A2B429A672FE4CDEF94C8D47D5921EA38B7F9'
const tokenPattern = /^[a-f0-9]{64}$/
const idPattern = /^[A-Za-z0-9_-]{8,128}$/
const exact = (value, names) => assert.deepEqual(Object.keys(value ?? {}).sort(), [...names].sort())
const iso = value => {assert.equal(typeof value, 'string'); const result = Date.parse(value); assert.ok(Number.isFinite(result)); return result}
const fingerprint = account => crypto.createHash('sha256').update(JSON.stringify({username: account.username, password: account.password})).digest('hex')
const failure = code => Object.assign(new Error(code), {code})
const safeCode = error => /^[A-Z][A-Z0-9_]{1,80}$/.test(error?.code ?? '') ? error.code : 'RECOVERY_UNPROVEN'
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))

export function assertRecoveryBinding(binding) {
  assert.equal(binding.kind, 'normal-testrun-pressure-v1'); assertTestrunTarget(binding.baseUrl)
  for (const identity of [binding.test, binding.formal]) {
    assert.ok(Number.isSafeInteger(identity.pid) && identity.pid > 0)
    assert.match(identity.commit, /^[a-f0-9]{40}$/); assert.match(identity.dllSha256, /^[A-F0-9]{64}$/)
  }
  assert.notEqual(binding.test.pid, binding.formal.pid)
  return binding
}
export function validateRecoveryControl(control, binding, bindingSha256) {
  assertRecoveryBinding(binding)
  exact(control, ['schema', 'kind', 'runId', 'baseUrl', 'test', 'formal', 'bindingSha256', 'registrationWindow', 'groups'])
  assert.equal(control.schema, 1); assert.equal(control.kind, 'normal-testrun-account-ledgers-v1')
  assert.match(control.runId, /^[a-f0-9]{32}$/); assert.equal(control.baseUrl, binding.baseUrl)
  assert.match(bindingSha256, /^[A-F0-9]{64}$/); assert.equal(control.bindingSha256, bindingSha256)
  for (const name of ['test', 'formal']) assert.deepEqual(control[name], {pid: binding[name].pid, commit: binding[name].commit, dllSha256: binding[name].dllSha256})
  exact(control.registrationWindow, ['openedUtc', 'notAfterUtc'])
  const opened = iso(control.registrationWindow.openedUtc), closed = iso(control.registrationWindow.notAfterUtc)
  assert.ok(closed > opened && closed - opened <= 900000)
  assert.equal(control.groups.length, 2)
  let parent
  control.groups.forEach((group, index) => {
    exact(group, ['index', 'runId', 'maxSlots', 'ledgerPath'])
    assert.equal(group.index, index); assert.equal(group.runId, `normal-${control.runId}-${index}`)
    assert.equal(group.maxSlots, [60, 9][index]); assert.ok(path.isAbsolute(group.ledgerPath))
    assert.equal(path.basename(group.ledgerPath), ['players60.dpapi', 'outer9.dpapi'][index])
    assert.equal(path.basename(path.dirname(group.ledgerPath)), control.runId)
    if (parent !== undefined) assert.equal(path.dirname(group.ledgerPath), parent)
    parent = path.dirname(group.ledgerPath)
  })
  return control
}
export function validatePrivateLedger(privateLedger, control, controlSha256) {
  exact(privateLedger, ['schema', 'kind', 'runId', 'controlSha256', 'bindingSha256', 'registrationWindow', 'accounts', 'ledgerBindings'])
  assert.equal(privateLedger.schema, 1); assert.equal(privateLedger.kind, 'normal-ledger-private-envelope')
  assert.equal(privateLedger.runId, control.runId); assert.equal(privateLedger.controlSha256, controlSha256)
  assert.equal(privateLedger.bindingSha256, control.bindingSha256); assert.deepEqual(privateLedger.registrationWindow, control.registrationWindow)
  assert.ok(Array.isArray(privateLedger.accounts) && privateLedger.accounts.length <= 69)
  assert.equal(privateLedger.ledgerBindings.length, 2)
  const names = new Set(), ids = new Set(), slots = new Set()
  for (const account of privateLedger.accounts) {
    exact(account, ['ledgerIndex', 'slot', 'stage', 'username', 'password', 'requestedAtUtc', 'preflightAbsent', 'requestFingerprint', 'id', 'createdAt'])
    assert.ok([0, 1].includes(account.ledgerIndex) && Number.isInteger(account.slot) && account.slot >= 0 && account.slot < [60, 9][account.ledgerIndex])
    assert.ok(!slots.has(`${account.ledgerIndex}:${account.slot}`)); slots.add(`${account.ledgerIndex}:${account.slot}`)
    assert.match(account.username, /^[A-Za-z0-9_]{2,11}$/); assert.ok(!names.has(account.username)); names.add(account.username)
    assert.equal(typeof account.password, 'string'); assert.ok(account.password.length >= 16 && account.password.length <= 128)
    assert.equal(account.preflightAbsent, true); assert.equal(account.requestFingerprint, fingerprint(account))
    const requested = iso(account.requestedAtUtc)
    assert.ok(requested >= iso(control.registrationWindow.openedUtc) && requested <= iso(control.registrationWindow.notAfterUtc))
    assert.ok(['intent', 'confirmed'].includes(account.stage))
    if (account.stage === 'intent') {assert.equal(account.id, null); assert.equal(account.createdAt, null)}
    else {
      assert.match(account.id, idPattern); assert.ok(!ids.has(account.id)); ids.add(account.id)
      const created = iso(account.createdAt)
      assert.ok(created >= iso(control.registrationWindow.openedUtc) && created <= iso(control.registrationWindow.notAfterUtc))
    }
  }
  privateLedger.ledgerBindings.forEach((group, index) => {
    assert.equal(group.index, index); assert.equal(group.runId, control.groups[index].runId)
    const accounts = privateLedger.accounts.filter(account => account.ledgerIndex === index)
    const confirmed = accounts.filter(account => account.stage === 'confirmed').length
    assert.equal(group.count, accounts.length); assert.equal(group.confirmed, confirmed)
    assert.equal(group.sequence, accounts.length + confirmed)
    if (group.missing) {assert.equal(accounts.length, 0); assert.equal(group.cipherSha256, null)}
    else {assert.match(group.cipherSha256, /^[A-F0-9]{64}$/); assert.equal(group.wrongRunRejected, true)}
  })
  return privateLedger
}

// All HTTP and runtime observations are injected. This module has no fetch/SSH implementation.
export function createNormalSessionManager({binding, request, observeIdentity, acquireSession, deadlineMs, now = Date.now}) {
  return createProfileSessionManager({binding,request,observeIdentity,acquireSession,deadlineMs,now},{diagnosticsOff:true})
}

// Separate diagnostic entry: the normal factory above always requires OFF.
// Only the explicitly bounded, ephemeral two-recipient terminal probe is accepted.
export function createTerminalRecipientSessionManager(options) {
  assert.match(options.diagnosticSetting??'',/^terminal-recipients-v4:30:[a-f0-9]{32}$/,
    'EXACT_TERMINAL_RECIPIENT_PROFILE_REQUIRED')
  return createProfileSessionManager(options,{diagnosticsOff:false,diagnosticSetting:options.diagnosticSetting})
}

function createProfileSessionManager({binding, request, observeIdentity, acquireSession, deadlineMs, now = Date.now}, profile) {
  assertRecoveryBinding(binding)
  for (const callback of [request, observeIdentity, acquireSession, now]) assert.equal(typeof callback, 'function')
  assert.ok(Number.isFinite(deadlineMs))
  const records = new Map()
  const remaining = () => {const left = deadlineMs - now(); if (left < 250) throw failure('SESSION_DEADLINE_EXHAUSTED'); return Math.min(10000, left)}
  const call = async (relative, options = {}) => {
    assert.ok(typeof relative === 'string' && !relative.startsWith('/') && !relative.includes('://') && !relative.includes('..') && !relative.includes('\\'))
    const expected = options.expected ?? [200]
    const response = await request(binding.baseUrl, relative, {...options, timeoutMs: remaining(), redirect: 'error'})
    if (!response || !Number.isInteger(response.status) || !expected.includes(response.status)) throw failure('SESSION_HTTP_STATUS_UNEXPECTED')
    return response
  }
  const bound = async () => {
    remaining()
    const observed = await observeIdentity()
    for (const [key, value] of Object.entries({baseUrl: binding.baseUrl, testPid: binding.test.pid, testCommit: binding.test.commit,
      testDllSha256: binding.test.dllSha256, matchStorage: 'ephemeral',...profile}))
      if (observed?.[key] !== value) throw failure('SESSION_RUNTIME_BINDING_CHANGED')
    const health = (await call('health')).body
    if (health?.status !== 'ok' || health.serverVersion !== binding.test.commit || health.maintenance !== false) throw failure('SESSION_HEALTH_BINDING_CHANGED')
  }
  const recordFor = handle => {const record = records.get(handle?.privateSessionHandle); if (!record || record.handle !== handle) throw failure('SESSION_HANDLE_REJECTED'); return record}
  const prepare = async (kind, originalHandle) => {
    await bound()
    const acquired = await acquireSession({kind, baseUrl: binding.baseUrl})
    if (!tokenPattern.test(acquired?.token ?? '')) throw failure('SESSION_TOKEN_REJECTED')
    const tokenSha256 = digest(acquired.token)
    if ([...records.values()].some(record => record.tokenSha256 === tokenSha256)) throw failure('SESSION_NOT_INDEPENDENT')
    const handle = Object.freeze({kind, privateSessionHandle: crypto.randomUUID()})
    const record = {handle, token: acquired.token, tokenSha256, sessionId: null, accountId: null, prepared: false, revoked: false}
    records.set(handle.privateSessionHandle, record)
    try {
      const account = (await call('api/auth/me', {token: record.token})).body
      if (account?.role !== 'admin' || account.deleted !== false || typeof account.id !== 'string') throw failure('SESSION_ADMIN_IDENTITY_REJECTED')
      const sessions = (await call('api/auth/sessions', {token: record.token})).body
      const current = Array.isArray(sessions) ? sessions.filter(item => item.current === true) : []
      if (current.length !== 1 || !idPattern.test(current[0].id ?? '')) throw failure('SESSION_CURRENT_ID_REJECTED')
      if ([...records.values()].some(other => other !== record && other.sessionId === current[0].id)) throw failure('SESSION_NOT_INDEPENDENT')
      if (kind === 'recovery' && recordFor(originalHandle).accountId !== account.id) throw failure('SESSION_RECOVERY_ADMIN_CHANGED')
      record.accountId = account.id; record.sessionId = current[0].id
      const storage = (await call('api/admin/storage/private-deck-persistence', {token: record.token})).body
      if (storage?.configuredEnabled !== true || storage.effectiveMode !== 'object' || storage.writable !== true) throw failure('SESSION_F2_OBJECT_WRITABLE_REQUIRED')
      record.prepared = true
      return handle
    } catch (error) {throw failure(safeCode(error))}
  }
  const revoke = async handle => {
    const record = recordFor(handle)
    await bound()
    const me = await call('api/auth/me', {token: record.token, expected: [200, 401]})
    if (me.status === 401) {record.revoked = true; return {kind: handle.kind, status: 'ALREADY_REVOKED_401', afterStatus: 401}}
    if (record.accountId !== null && me.body?.id !== record.accountId) throw failure('SESSION_REVOKE_OWNER_CHANGED')
    const list = (await call('api/auth/sessions', {token: record.token})).body
    const current = Array.isArray(list) ? list.filter(item => item.current === true) : []
    if (current.length !== 1 || (record.sessionId !== null && current[0].id !== record.sessionId)) throw failure('SESSION_REVOKE_ID_CHANGED')
    await call('api/auth/sessions/current', {method: 'DELETE', token: record.token, expected: [200, 204]})
    await call('api/auth/me', {token: record.token, expected: [401]})
    record.revoked = true
    return {kind: handle.kind, status: 'EXACT_CURRENT_SESSION_REVOKED_401', afterStatus: 401}
  }
  return {
    prepareOriginal: () => prepare('original'),
    prepareRecovery: original => {recordFor(original); return prepare('recovery', original)},
    async withPrivateSession(handle, consumer) {
      const record = recordFor(handle)
      if (!record.prepared || record.revoked) throw failure('SESSION_NOT_USABLE')
      await bound()
      return consumer({token: record.token, accountId: record.accountId, sessionId: record.sessionId, baseUrl: binding.baseUrl})
    },
    async request(handle, relative, options = {}) {
      const record = recordFor(handle)
      if (!record.prepared || record.revoked) throw failure('SESSION_NOT_USABLE')
      if (['POST', 'DELETE', 'PUT', 'PATCH'].includes(options.method)) await bound()
      return call(relative, {...options, token: options.anonymous ? undefined : options.token ?? record.token})
    },
    isManagedToken: token => [...records.values()].some(record => record.tokenSha256 === digest(token)),
    revoke,
    async revokeAll() {
      const results = []
      for (const record of records.values()) {
        try {results.push(await revoke(record.handle))} catch (error) {results.push({kind: record.handle.kind, status: 'REVOCATION_UNPROVEN', code: safeCode(error)})}
      }
      return {complete: results.every(item => item.afterStatus === 401), results, credentialsEmitted: false}
    },
    publicStatus: () => ({sessions: [...records.values()].map(record => ({kind: record.handle.kind, prepared: record.prepared, revoked: record.revoked})), credentialsEmitted: false}),
  }
}

function assertOwnedView(view, account, control, {allowDeleted = false} = {}) {
  if (!view || !idPattern.test(view.id ?? '') || view.role !== 'player' || !Number.isInteger(view.permissionVersion) || view.permissionVersion < 1
    || (!allowDeleted && (view.deleted !== false || view.disabled === true)) || /^(?:admin|aimin)$/i.test(account.username)) throw failure('ACCOUNT_ROLE_OR_PERMISSION_REJECTED')
  const created = iso(view.createdAt), requested = iso(account.requestedAtUtc)
  if (created < iso(control.registrationWindow.openedUtc) || created > iso(control.registrationWindow.notAfterUtc)
    || created < requested - 5000 || created > requested + 15000) throw failure('ACCOUNT_CREATION_WINDOW_UNPROVEN')
  if (account.stage === 'confirmed' && (view.id !== account.id || view.createdAt !== account.createdAt)) throw failure('CONFIRMED_ACCOUNT_IDENTITY_CHANGED')
  if (!allowDeleted && view.username !== account.username) throw failure('ACCOUNT_USERNAME_CHANGED')
  return view
}
export async function recoverOwnedLedgerAccounts({binding, control, privateLedger, sessionManager, recoveryHandle, deadlineMs, now = Date.now, wait = delay}) {
  validateRecoveryControl(control, binding, control.bindingSha256)
  validatePrivateLedger(privateLedger, control, privateLedger.controlSha256)
  assert.equal(recoveryHandle.kind, 'recovery')
  const counts = {records: privateLedger.accounts.length, deletedNow: 0, alreadyDeleted: 0, unknown: 0, playerSessionsRevoked: 0}
  const pending = []
  const call = (route, options = {}) => {
    if (now() + 250 >= deadlineMs) throw failure('RECOVERY_DEADLINE_EXHAUSTED')
    return sessionManager.request(recoveryHandle, route, options)
  }
  const pace = async () => {if (now() + 1300 >= deadlineMs) throw failure('RECOVERY_DEADLINE_EXHAUSTED'); await wait(1300); if (now() >= deadlineMs) throw failure('RECOVERY_DEADLINE_EXHAUSTED')}
  for (const account of privateLedger.accounts) {
    let playerToken = null
    try {
      let selected
      if (account.stage === 'intent') {
        const page = (await call(`api/admin/accounts/query?page=1&pageSize=2&search=${encodeURIComponent(account.username)}&deleted=false`)).body
        if (!page || !Array.isArray(page.items) || page.total !== 1 || page.items.length !== 1) throw failure('INTENT_ACCOUNT_UNCERTAIN')
        selected = assertOwnedView(page.items[0], account, control)
        const login = await call('api/auth/login', {method: 'POST', anonymous: true, body: {username: account.username, password: account.password}, expected: [200, 401, 429]})
        if (login.status !== 200 || !tokenPattern.test(login.body?.token ?? '') || sessionManager.isManagedToken(login.body.token)) throw failure('INTENT_CREDENTIAL_OWNERSHIP_UNPROVEN')
        playerToken = login.body.token
        const authenticated = assertOwnedView(login.body.account, account, control)
        if (authenticated.id !== selected.id || authenticated.createdAt !== selected.createdAt) throw failure('INTENT_LOGIN_QUERY_MISMATCH')
      } else {
        const response = await call('api/admin/accounts/' + encodeURIComponent(account.id), {expected: [200, 404]})
        if (response.status !== 200) throw failure('CONFIRMED_ACCOUNT_MISSING')
        selected = assertOwnedView(response.body, account, control, {allowDeleted: response.body.deleted === true})
        if (selected.deleted === true) {counts.alreadyDeleted++; continue}
      }
      const route = 'api/admin/accounts/' + encodeURIComponent(selected.id)
      let detail = assertOwnedView((await call(route)).body, {...account, stage: 'confirmed', id: selected.id, createdAt: selected.createdAt}, control)
      await pace()
      await call(route + '/sessions', {method: 'DELETE', expected: [200, 204]})
      const sessions = (await call(route + '/sessions')).body
      if (!Array.isArray(sessions) || sessions.length !== 0) throw failure('PLAYER_SESSIONS_NOT_REVOKED')
      if (playerToken) await call('api/auth/me', {token: playerToken, expected: [401]})
      counts.playerSessionsRevoked++
      detail = assertOwnedView((await call(route)).body, {...account, stage: 'confirmed', id: selected.id, createdAt: selected.createdAt}, control)
      await pace()
      let mutationUncertain = false
      try {
        await call('api/admin/v1/accounts/' + encodeURIComponent(selected.id) + '/delete', {method: 'POST', expected: [200, 204],
          body: {reason: 'owned normal testrun ledger recovery', idempotencyKey: `normal-recovery-${control.runId}-${account.ledgerIndex}-${account.slot}`,
            expectedVersion: detail.permissionVersion, dryRun: false}})
      } catch {mutationUncertain = true}
      const after = (await call(route)).body
      if (after?.id !== selected.id || after.createdAt !== selected.createdAt || after.role !== 'player' || after.deleted !== true || after.username === selected.username)
        throw failure(mutationUncertain ? 'ACCOUNT_CAS_OR_READBACK_UNCERTAIN' : 'ACCOUNT_DELETE_READBACK_UNPROVEN')
      counts.deletedNow++
    } catch (error) {
      counts.unknown++; pending.push({ledgerIndex: account.ledgerIndex, slot: account.slot, code: safeCode(error)})
    } finally {
      if (playerToken) {
        // Only the exact newly obtained player token, never an admin's sessions.
        try {await call('api/auth/sessions/current', {method: 'DELETE', token: playerToken, expected: [200, 204, 401]}); await call('api/auth/me', {token: playerToken, expected: [401]})}
        catch {if (!pending.some(item => item.ledgerIndex === account.ledgerIndex && item.slot === account.slot)) {counts.unknown++; pending.push({ledgerIndex: account.ledgerIndex, slot: account.slot, code: 'RECOVERED_PLAYER_SESSION_REVOCATION_UNPROVEN'})}}
      }
      playerToken = null
    }
  }
  return {schema: 1, status: counts.unknown === 0 ? 'OWNED_LEDGER_RECORDS_ACCOUNTED_FOR' : 'RECOVERY_PENDING_OWNERSHIP_OR_READBACK',
    cleanupComplete: counts.unknown === 0, ...counts, pending, credentialsEmitted: false, formalWrites: 0,
    onlineCapacityAccepted: false, boundary: 'Only the protected ledger records were reconciled; this does not certify a full pressure run.'}
}

function clearPrivate(envelope) {
  for (const account of envelope?.accounts ?? []) {account.username = ''; account.password = ''; account.id = ''; account.createdAt = ''; account.requestFingerprint = ''}
}
function encodeFrame(value) {
  const bytes = Buffer.from(JSON.stringify(value)), frame = Buffer.alloc(bytes.length + 4)
  frame.writeUInt32LE(bytes.length); bytes.copy(frame, 4); bytes.fill(0); return frame
}
async function connectPrivatePipe(name, deadline, cancelled = () => false) {
  while (Date.now() < deadline && !cancelled()) {
    try {
      return await new Promise((resolve, reject) => {
        const socket = net.createConnection('\\\\.\\pipe\\' + name)
        socket.once('connect', () => {socket.removeListener('error', reject); resolve(socket)})
        socket.once('error', error => {socket.destroy(); reject(error)})
      })
    } catch {await delay(50)}
  }
  throw failure('PRIVATE_PIPE_CONNECT_TIMEOUT')
}
export async function readColdLedgersPrivately({controlPath, controlSha256, bindingSha256, allowPartial = false, consume, timeoutMs = 15000,
  pwsh = process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'}) {
  assert.equal(typeof consume, 'function'); assert.ok(Number.isInteger(timeoutMs) && timeoutMs >= 1000 && timeoutMs <= 30000)
  const controlBytes = fs.readFileSync(controlPath); assert.ok(controlBytes.length <= 16384); assert.equal(digest(controlBytes), controlSha256)
  const control = JSON.parse(controlBytes); assert.equal(control.bindingSha256, bindingSha256)
  assertGeneratedDirectories(path.dirname(controlPath), path.dirname(control.groups[0].ledgerPath))
  const pipeName = 'l12-normal-recovery-' + crypto.randomBytes(16).toString('hex'), nonce = crypto.randomBytes(32).toString('hex')
  const args = ['-NoProfile', '-File', READER, '-ControlPath', controlPath, '-ExpectedControlSha256', controlSha256,
    '-ExpectedBindingSha256', bindingSha256, '-Mode', 'PrivatePipe', '-PrivatePipeName', pipeName, '-PrivateConsumerNonceSha256', digest(nonce)]
  if (allowPartial) args.push('-AllowPartial')
  const child = spawn(pwsh, args, {cwd: path.dirname(control.groups[0].ledgerPath), windowsHide: true, stdio: ['ignore', 'pipe', 'pipe']})
  let stdout = Buffer.alloc(0), stderr = Buffer.alloc(0), processFault = null, socket, privateEnvelope, timer, cancelled = false
  const exited = new Promise(resolve => {child.once('error', () => resolve({code: null})); child.once('close', code => resolve({code}))})
  for (const [stream, kind] of [[child.stdout, 'stdout'], [child.stderr, 'stderr']]) stream.on('data', chunk => {
    if (kind === 'stdout') stdout = Buffer.concat([stdout, chunk]); else stderr = Buffer.concat([stderr, chunk])
    if (stdout.length > 16384 || stderr.length > 4096) {processFault = failure('COLD_READER_OUTPUT_BOUND'); child.kill()}
  })
  const timeout = new Promise((_, reject) => {timer = setTimeout(() => {processFault = failure('COLD_READER_TIMEOUT'); child.kill(); reject(processFault)}, timeoutMs)})
  try {
    const proof = await Promise.race([(async () => {
      socket = await connectPrivatePipe(pipeName, Date.now() + Math.min(10000, timeoutMs), () => cancelled)
      const framePromise = new Promise((resolve, reject) => {
        let buffer = Buffer.alloc(0)
        const onData = chunk => {
          buffer = Buffer.concat([buffer, chunk])
          if (buffer.length > 131076) {buffer.fill(0); reject(failure('PRIVATE_LEDGER_FRAME_BOUND')); return}
          if (buffer.length < 4) return
          const length = buffer.readUInt32LE(0)
          if (length < 2 || length > 131072) {buffer.fill(0); reject(failure('PRIVATE_LEDGER_FRAME_BOUND')); return}
          if (buffer.length < length + 4) return
          if (buffer.length !== length + 4) {buffer.fill(0); reject(failure('PRIVATE_LEDGER_FRAME_BOUND')); return}
          try {resolve(JSON.parse(buffer.subarray(4).toString('utf8')))} catch {reject(failure('PRIVATE_LEDGER_JSON_REJECTED'))}
          finally {buffer.fill(0); socket.removeListener('data', onData)}
        }
        socket.on('data', onData); socket.once('error', () => reject(failure('PRIVATE_LEDGER_PIPE_FAILED')))
        socket.once('close', () => reject(failure('PRIVATE_LEDGER_PIPE_CLOSED')))
      })
      const hello = encodeFrame({schema: 1, kind: 'normal-ledger-private-consumer', runId: control.runId, controlSha256, nonce})
      socket.write(hello, () => hello.fill(0))
      privateEnvelope = await framePromise
      validatePrivateLedger(privateEnvelope, control, controlSha256)
      const ack = encodeFrame({schema: 1, consumed: true, controlSha256})
      await new Promise((resolve, reject) => socket.write(ack, error => {ack.fill(0); error ? reject(failure('PRIVATE_LEDGER_ACK_FAILED')) : resolve()}))
      const result = await exited
      if (processFault || result.code !== 0 || stderr.length) throw processFault ?? failure('COLD_READER_FAILED')
      const publicProof = JSON.parse(stdout)
      assert.equal(publicProof.controlSha256, controlSha256); assert.equal(publicProof.privateConsumerCompleted, true)
      return publicProof
    })(), timeout])
    clearTimeout(timer); socket?.destroy()
    // The pipe only hands data off. The async private consumer may perform bounded
    // recovery after the reader exited; its deadline belongs to the recovery API.
    await consume(privateEnvelope)
    return {...proof, privateConsumerCompleted: true}
  } catch (error) {
    throw failure(safeCode(error) === 'RECOVERY_UNPROVEN' ? 'PRIVATE_COLD_READ_OR_CONSUMER_FAILED' : safeCode(error))
  } finally {
    cancelled = true; clearTimeout(timer); socket?.destroy(); if (child.exitCode === null) child.kill(); await exited
    clearPrivate(privateEnvelope); stdout.fill(0); stderr.fill(0)
  }
}

export async function runRecoverySelfTests() {
  let networkCalls = 0
  globalThis.fetch = () => {networkCalls++; throw failure('NETWORK_FORBIDDEN_IN_OFFLINE_TEST')}
  const runId = crypto.randomUUID().replaceAll('-', '')
  const output = path.join(EVIDENCE_ROOT, 'recovery-offline-' + runId), cache = path.join(CACHE_ROOT, 'recovery-offline-' + runId)
  const ledgerRoot = path.join(cache, runId)
  assertGeneratedDirectories(output, ledgerRoot)
  fs.mkdirSync(output, {recursive: true}); fs.mkdirSync(ledgerRoot, {recursive: true})
  process.env.TEMP = cache; process.env.TMP = cache; process.env.TMPDIR = cache
  const pwsh = process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
  const results = [], owned = []
  const check = async (name, work) => {await work(); results.push({name, status: 'passed'})}
  const binding = {kind: 'normal-testrun-pressure-v1', baseUrl: 'https://legion-12.com/testrun/',
    test: {pid: 101, commit: '131989a65ef3b26a22e58e5fe5c480c061aae9cb', dllSha256: 'B'.repeat(64)},
    formal: {pid: 102, commit: 'a'.repeat(40), dllSha256: 'C'.repeat(64)}}
  const windowClock = Date.now(), opened = new Date(windowClock - 1000).toISOString(), notAfter = new Date(windowClock + 899000).toISOString()
  const control = {schema: 1, kind: 'normal-testrun-account-ledgers-v1', runId, baseUrl: binding.baseUrl,
    test: binding.test, formal: binding.formal, bindingSha256: 'A'.repeat(64), registrationWindow: {openedUtc: opened, notAfterUtc: notAfter},
    groups: [60, 9].map((maxSlots, index) => ({index, runId: `normal-${runId}-${index}`, maxSlots,
      ledgerPath: path.join(ledgerRoot, ['players60.dpapi', 'outer9.dpapi'][index])}))}
  const controlPath = path.join(output, 'ledger-control.json')
  const controlFd = fs.openSync(controlPath, 'wx')
  try {fs.writeFileSync(controlFd, JSON.stringify(control)); fs.fsyncSync(controlFd)} finally {fs.closeSync(controlFd)}
  const controlSha256 = digest(fs.readFileSync(controlPath))
  const privateCopies = [], secrets = []
  let partial, full
  try {
    await check('normal-control-not-loopback-and-dual-identity', () => validateRecoveryControl(control, binding, control.bindingSha256))
    for (const [name, mutate] of [['formal-target', c => {c.baseUrl = 'https://legion-12.com/'}],
      ['same-pid', c => {c.formal.pid = c.test.pid}], ['wrong-group-run', c => {c.groups[1].runId += '-wrong'}],
      ['wrong-binding', c => {c.bindingSha256 = '0'.repeat(64)}]])
      await check('control-reject-' + name, () => {const bad = structuredClone(control); mutate(bad); assert.throws(() => validateRecoveryControl(bad, binding, control.bindingSha256))})
    const sinkPath = path.join(LEGACY, 'account-ledger-sink-r2.ps1'), clientPath = path.join(LEGACY, 'account-ledger-client-r2.mjs')
    assert.equal(digest(fs.readFileSync(sinkPath)), SINK_SHA); assert.equal(digest(fs.readFileSync(clientPath)), CLIENT_SHA)
    const {AccountLedgerClient, NamedPipeLedgerTransport, registrationFingerprint} = await import(pathToFileURL(clientPath).href)
    const startSink = async index => {
      const pipeName = 'l12-normal-ledger-' + crypto.randomBytes(16).toString('hex'), group = control.groups[index]
      const child = spawn(pwsh, ['-NoProfile', '-File', sinkPath, '-Apply', '-RunId', group.runId, '-PipeName', pipeName,
        '-LedgerPath', group.ledgerPath, '-MaxSlots', String(group.maxSlots), '-ConnectionTimeoutMs', '15000', '-FrameTimeoutMs', '30000'],
      {cwd: ledgerRoot, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe']})
      let text = '', errors = ''
      child.stdout.on('data', chunk => {text += chunk.toString(); if (text.length > 4096) child.kill()})
      child.stderr.on('data', chunk => {errors += chunk.toString(); if (errors.length > 4096) child.kill()})
      const done = new Promise(resolve => {child.once('error', () => resolve({code: null})); child.once('close', code => resolve({code, text, errors}))})
      const entry = {child, done, client: null}; owned.push(entry)
      execFileSync(pwsh, ['-NoProfile', '-Command',
        String.raw`$ErrorActionPreference='Stop';$until=[DateTime]::UtcNow.AddSeconds(10);while([DateTime]::UtcNow -lt $until){if([IO.Directory]::EnumerateFiles('\\.\pipe\') -contains ('\\.\pipe\'+$env:L12_NORMAL_PIPE)){exit 0};Start-Sleep -Milliseconds 25};exit 2`],
      {env: {...process.env, L12_NORMAL_PIPE: pipeName}, windowsHide: true, stdio: 'pipe', timeout: 12000, maxBuffer: 4096})
      entry.client = new AccountLedgerClient({runId: group.runId, maxSlots: group.maxSlots, transport: new NamedPipeLedgerTransport({pipeName})})
      return entry
    }
    const rows = []
    const addAccount = async (entry, index, slot, confirmed) => {
      const username = `R${runId.slice(0, 5)}${index}${String(slot).padStart(2, '0')}`, password = crypto.randomBytes(24).toString('hex')
      const requestedAtUtc = new Date().toISOString(), createdAtUtc = new Date(Date.now() + 1).toISOString()
      const account = {username, password, requestedAtUtc, id: `synthetic-${index}-${String(slot).padStart(2, '0')}`, createdAtUtc}
      secrets.push(username, password)
      await entry.client.persistIntent({slot, username, password, requestedAtUtc, preflightAbsent: true, requestFingerprint: registrationFingerprint(username, password)})
      if (confirmed) await entry.client.persistConfirmed({slot, id: account.id, createdAtUtc})
      rows.push({...account, ledgerIndex: index, slot})
      return account
    }
    const first = await startSink(0)
    for (let slot = 0; slot < 60; slot++) await addAccount(first, 0, slot, true)
    first.client.close(); assert.equal((await first.done).code, 0)
    const second = await startSink(1)
    let last
    for (let slot = 0; slot < 9; slot++) last = await addAccount(second, 1, slot, slot !== 8)
    await check('actual-frozen-sink-dual-DPAPI-partial-private-coldread', async () => {
      const proof = await readColdLedgersPrivately({controlPath, controlSha256, bindingSha256: control.bindingSha256, allowPartial: true,
        consume: envelope => {partial = structuredClone(envelope); privateCopies.push(partial)}})
      assert.equal(proof.complete, false); assert.equal(proof.privateConsumerCompleted, true)
      assert.deepEqual(proof.groups.map(group => [group.count, group.sequence, group.confirmed]), [[60, 120, 60], [9, 17, 8]])
      assert.ok(proof.groups.every(group => group.wrongRunRejected))
      for (const secret of secrets) assert.equal(JSON.stringify(proof).includes(secret), false)
      fs.writeFileSync(path.join(output, 'partial-cold-proof.json'), JSON.stringify(proof, null, 2), {flag: 'wx'})
    })
    await check('actual-cold-reader-refuses-incomplete-acceptance', () => {
      const result = spawnSyncCold(pwsh, controlPath, controlSha256, control.bindingSha256)
      assert.equal(result.status, 2); assert.ok(result.stderr.includes('NORMAL_LEDGER_INCOMPLETE')); assert.equal(result.stdout, '')
    })
    await second.client.persistConfirmed({slot: 8, id: last.id, createdAtUtc: last.createdAtUtc})
    second.client.close(); assert.equal((await second.done).code, 0)
    await check('actual-frozen-reader-complete-60-plus-9-with-wrong-run-rejection', async () => {
      const proof = await readColdLedgersPrivately({controlPath, controlSha256, bindingSha256: control.bindingSha256,
        consume: envelope => {full = structuredClone(envelope); privateCopies.push(full)}})
      assert.equal(proof.complete, true); assert.equal(proof.privateConsumerCompleted, true)
      assert.deepEqual(proof.groups.map(group => [group.count, group.sequence, group.confirmed]), [[60, 120, 60], [9, 18, 9]])
      assert.ok(proof.groups.every(group => group.wrongRunRejected))
      for (const secret of secrets) assert.equal(JSON.stringify(proof).includes(secret), false)
      for (const group of control.groups) {const cipher = fs.readFileSync(group.ledgerPath); for (const secret of secrets) assert.equal(cipher.includes(Buffer.from(secret)), false)}
      fs.writeFileSync(path.join(output, 'complete-cold-proof.json'), JSON.stringify(proof, null, 2), {flag: 'wx'})
    })
    await check('actual-reader-rejects-wrong-control-sha', () => {
      const result = spawnSyncCold(pwsh, controlPath, '0'.repeat(64), control.bindingSha256)
      assert.equal(result.status, 2); assert.ok(result.stderr.includes('NORMAL_LEDGER_CONTROL_SHA_CHANGED')); assert.equal(result.stdout, '')
    })
    const makeSimulation = selectedAccounts => {
      const state = {deleted: 0, casAttempts: 0, ownRevokes: 0, logins: 0, acquireCount: 0, loginStatus: 200, storageMode: 'object',
        runtimeChanged: false, casConflict: false, queryExtra: false, wrongRole: false, loginMismatch: false}
      const tokens = new Map(), accounts = new Map()
      for (const account of selectedAccounts) {
        const row = rows.find(item => item.ledgerIndex === account.ledgerIndex && item.slot === account.slot)
        accounts.set(row.id, {id: row.id, username: account.username, password: account.password, role: 'player', createdAt: row.createdAtUtc,
          permissionVersion: 3, deleted: false, disabled: false})
      }
      const adminAccount = {id: 'normal-admin-0001', username: 'Admin', role: 'admin', deleted: false}
      const view = account => {const copy = {...account}; delete copy.password; if (state.wrongRole) copy.role = 'admin'; return copy}
      const request = async (base, route, options) => {
        assert.equal(base, binding.baseUrl); assert.equal(options.redirect, 'error'); assert.ok(options.timeoutMs <= 10000)
        const selectedToken = tokens.get(options.token)
        if (route === 'health') return {status: 200, body: {status: 'ok', serverVersion: binding.test.commit, maintenance: false}}
        if (route === 'api/auth/login') {
          state.logins++
          if (state.loginStatus !== 200) return {status: state.loginStatus, body: {}}
          const account = [...accounts.values()].find(item => item.username === options.body.username && item.password === options.body.password && !item.deleted)
          if (!account) return {status: 401, body: {}}
          const token = crypto.randomBytes(32).toString('hex'); tokens.set(token, {accountId: account.id, sessionId: 'player-session-' + state.logins, revoked: false})
          return {status: 200, body: {token, account: {...view(account), ...(state.loginMismatch ? {id: 'different-account'} : {})}}}
        }
        if (route === 'api/auth/me') return !selectedToken || selectedToken.revoked ? {status: 401, body: null}
          : {status: 200, body: selectedToken.accountId === adminAccount.id ? adminAccount : view(accounts.get(selectedToken.accountId))}
        if (!selectedToken || selectedToken.revoked) return {status: 401, body: null}
        if (route === 'api/auth/sessions/current' && options.method === 'DELETE') {selectedToken.revoked = true; state.ownRevokes++; return {status: 204, body: null}}
        if (route === 'api/auth/sessions') return {status: 200, body: [{id: selectedToken.sessionId, current: true}]}
        if (route === 'api/admin/storage/private-deck-persistence') return {status: 200, body: {configuredEnabled: true, effectiveMode: state.storageMode, writable: true}}
        if (route.startsWith('api/admin/accounts/query?')) {
          const url = new URL(route, base), search = url.searchParams.get('search')
          const items = [...accounts.values()].filter(item => item.username === search && !item.deleted).map(view)
          if (state.queryExtra && items.length) items.push({...items[0], id: 'unrelated-account'})
          return {status: 200, body: {items, total: items.length}}
        }
        const match = /^api\/admin\/accounts\/([^/]+)(\/sessions)?$/.exec(route)
        if (match) {
          const account = accounts.get(decodeURIComponent(match[1]))
          if (!account) return {status: 404, body: null}
          if (match[2] && options.method === 'DELETE') {for (const token of tokens.values()) if (token.accountId === account.id) token.revoked = true; return {status: 204, body: null}}
          if (match[2]) return {status: 200, body: [...tokens.values()].filter(token => token.accountId === account.id && !token.revoked).map(token => ({id: token.sessionId}))}
          return {status: 200, body: view(account)}
        }
        const deletion = /^api\/admin\/v1\/accounts\/([^/]+)\/delete$/.exec(route)
        if (deletion) {
          state.casAttempts++; const account = accounts.get(decodeURIComponent(deletion[1]))
          assert.equal(options.body.dryRun, false)
          if (state.casConflict || options.body.expectedVersion !== account.permissionVersion) return {status: 409, body: {}}
          account.deleted = true; account.username = 'deleted-' + account.id; account.disabled = true; account.permissionVersion++
          state.deleted++; return {status: 204, body: null}
        }
        throw failure('SYNTHETIC_ROUTE_UNEXPECTED')
      }
      const manager = createNormalSessionManager({binding, request, deadlineMs: Date.now() + 900000,
        observeIdentity: async () => ({baseUrl: binding.baseUrl, testPid: state.runtimeChanged ? 999 : binding.test.pid,
          testCommit: binding.test.commit, testDllSha256: binding.test.dllSha256, diagnosticsOff: true, matchStorage: 'ephemeral'}),
        acquireSession: async ({kind}) => {
          state.acquireCount++; const token = kind === 'original' ? 'a'.repeat(64) : state.duplicateToken ? 'a'.repeat(64) : 'b'.repeat(64)
          if (!tokens.has(token)) tokens.set(token, {accountId: adminAccount.id, sessionId: `admin-session-${kind}`, revoked: false})
          return {token}
        }})
      return {manager, state, tokens, accounts}
    }
    await check('normal-session-independent-recovery-and-idempotent-original-401', async () => {
      const sim = makeSimulation(partial.accounts), original = await sim.manager.prepareOriginal(), recovery = await sim.manager.prepareRecovery(original)
      assert.notEqual(original.privateSessionHandle, recovery.privateSessionHandle)
      sim.tokens.get('a'.repeat(64)).revoked = true // The original frozen core already revoked its exact token.
      assert.equal((await sim.manager.revoke(original)).status, 'ALREADY_REVOKED_401')
      const reconciled = await recoverOwnedLedgerAccounts({binding, control, privateLedger: partial, sessionManager: sim.manager,
        recoveryHandle: recovery, deadlineMs: Date.now() + 900000, wait: async () => {}})
      assert.equal(reconciled.cleanupComplete, true); assert.equal(reconciled.deletedNow, 69); assert.equal(reconciled.unknown, 0)
      assert.equal(sim.state.casAttempts, 69); assert.equal(sim.state.logins, 1)
      assert.equal((await sim.manager.revokeAll()).complete, true)
      assert.equal(sim.state.ownRevokes, 1) // Recovery admin only; original/player tokens were already 401.
      for (const secret of [...secrets, 'a'.repeat(64), 'b'.repeat(64)]) assert.equal(JSON.stringify(reconciled).includes(secret), false)
      fs.writeFileSync(path.join(output, 'injected-recovery-proof.json'), JSON.stringify(reconciled, null, 2), {flag: 'wx'})
    })
    await check('duplicate-original-token-is-not-independent', async () => {
      const sim = makeSimulation([]), original = await sim.manager.prepareOriginal(); sim.state.duplicateToken = true
      await assert.rejects(() => sim.manager.prepareRecovery(original), /SESSION_NOT_INDEPENDENT/)
      assert.equal(sim.state.ownRevokes, 0); assert.equal((await sim.manager.revokeAll()).complete, true)
    })
    await check('runtime-drift-before-acquire-no-new-session', async () => {
      const sim = makeSimulation([]); sim.state.runtimeChanged = true
      await assert.rejects(() => sim.manager.prepareOriginal(), /SESSION_RUNTIME_BINDING_CHANGED/); assert.equal(sim.state.acquireCount, 0)
    })
    await check('F2-snapshot-refused-and-owned-session-revoked', async () => {
      const sim = makeSimulation([]); sim.state.storageMode = 'snapshot'
      await assert.rejects(() => sim.manager.prepareOriginal(), /SESSION_F2_OBJECT_WRITABLE_REQUIRED/)
      assert.equal((await sim.manager.revokeAll()).complete, true)
    })
    const oneIntent = structuredClone(partial.accounts.find(account => account.stage === 'intent'))
    const smallEnvelope = account => ({...structuredClone(partial), accounts: [structuredClone(account)],
      ledgerBindings: [0, 1].map(index => ({index, runId: control.groups[index].runId, missing: index !== account.ledgerIndex,
        count: index === account.ledgerIndex ? 1 : 0, sequence: index === account.ledgerIndex ? (account.stage === 'confirmed' ? 2 : 1) : 0,
        confirmed: index === account.ledgerIndex && account.stage === 'confirmed' ? 1 : 0,
        intent: index === account.ledgerIndex && account.stage === 'intent' ? 1 : 0,
        cipherSha256: index === account.ledgerIndex ? 'D'.repeat(64) : null, wrongRunRejected: index === account.ledgerIndex}))})
    for (const [name, configure] of [['credential-401', sim => {sim.state.loginStatus = 401}], ['credential-429', sim => {sim.state.loginStatus = 429}],
      ['ambiguous-query', sim => {sim.state.queryExtra = true}], ['admin-role', sim => {sim.state.wrongRole = true}],
      ['login-query-mismatch', sim => {sim.state.loginMismatch = true}], ['CAS-conflict', sim => {sim.state.casConflict = true}]])
      await check('recovery-pends-' + name, async () => {
        const ledger = smallEnvelope(oneIntent), sim = makeSimulation(ledger.accounts), original = await sim.manager.prepareOriginal(), recovery = await sim.manager.prepareRecovery(original)
        configure(sim)
        const result = await recoverOwnedLedgerAccounts({binding, control, privateLedger: ledger, sessionManager: sim.manager, recoveryHandle: recovery,
          deadlineMs: Date.now() + 900000, wait: async () => {}})
        assert.equal(result.cleanupComplete, false); assert.equal(result.unknown, 1); assert.equal(sim.state.deleted, 0)
        assert.equal(sim.state.casAttempts, name === 'CAS-conflict' ? 1 : 0)
        await sim.manager.revokeAll()
      })
    for (const [name, mutate] of [['fingerprint', ledger => {ledger.accounts[0].requestFingerprint = '0'.repeat(64)}],
      ['wrong-run', ledger => {ledger.runId = '0'.repeat(32)}], ['preexisting', ledger => {ledger.accounts[0].preflightAbsent = false}],
      ['request-window', ledger => {ledger.accounts[0].requestedAtUtc = '2000-01-01T00:00:00Z'}]])
      await check('private-ledger-reject-' + name, () => {const ledger = smallEnvelope(oneIntent); mutate(ledger); assert.throws(() => validatePrivateLedger(ledger, control, controlSha256))})
    await check('private-consumer-failure-is-not-public-success', async () => {
      await assert.rejects(() => readColdLedgersPrivately({controlPath, controlSha256, bindingSha256: control.bindingSha256,
        consume: () => {throw failure('SYNTHETIC_CONSUMER_FAILED')}}), /SYNTHETIC_CONSUMER_FAILED/)
    })
    assert.equal(networkCalls, 0)
    const proof = {schema: 1, status: 'offline-tests-passed', cases: results.length, results, networkCalls,
      realDpapiLedgers: 2, ledgerGroups: [60, 9], actualColdReaderExecuted: true, wrongRunRejectedByActualFrozenReader: true,
      frozenReaderSha256: 'C991435C5E18FC8A26D9F57264E25BC38F4D1B4D4CC79DAF395D1C0EFCC6BECE',
      sourceSha256: digest(fs.readFileSync(import.meta.filename)), readerSha256: digest(fs.readFileSync(READER)),
      credentialsEmittedToStdout: false, onlineCapacityAccepted: false, liveLinuxSessionsVerified: false,
      boundary: 'Actual Windows DPAPI/sink/CurrentUserOnly private-pipe cold reads; injected HTTP/session/recovery only.'}
    fs.writeFileSync(path.join(output, 'offline-recovery-tests.json'), JSON.stringify(proof, null, 2), {flag: 'wx'})
    return {status: proof.status, cases: proof.cases, output, networkCalls: 0, liveLinuxSessionsVerified: false}
  } catch (error) {
    fs.writeFileSync(path.join(output, 'failure.json'), JSON.stringify({schema: 1, status: 'failed', code: safeCode(error),
      message: error instanceof assert.AssertionError ? 'OFFLINE_ASSERTION_FAILED' : safeCode(error), networkCalls: 0}, null, 2), {flag: 'wx'})
    throw error
  } finally {
    for (const item of owned) {item.client?.close(); if (item.child.exitCode === null) item.child.kill(); await item.done}
    for (const envelope of privateCopies) clearPrivate(envelope)
    secrets.fill('')
  }
}
function spawnSyncCold(pwsh, controlPath, controlSha256, bindingSha256) {
  // Only the new cold reader, never a remote command or a historical test runner.
  return execCold(pwsh, ['-NoProfile', '-File', READER, '-ControlPath', controlPath, '-ExpectedControlSha256', controlSha256,
    '-ExpectedBindingSha256', bindingSha256])
}
function execCold(executable, args) {
  try {return {status: 0, stdout: execFileSync(executable, args, {windowsHide: true, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], timeout: 15000}), stderr: ''}}
  catch (error) {return {status: error.status, stdout: String(error.stdout ?? ''), stderr: String(error.stderr ?? '')}}
}
if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(import.meta.filename)) {
  assert.deepEqual(process.argv.slice(2), ['--self-test'], 'only offline --self-test is an execution entry')
  const result = await runRecoverySelfTests()
  console.log(JSON.stringify(result))
}
