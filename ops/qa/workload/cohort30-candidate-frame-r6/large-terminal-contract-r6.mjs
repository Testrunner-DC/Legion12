import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'

// Candidate identity is deliberately unbound until Main supplies a hash-pinned record.
export const FIXED = Object.seal({
  candidate: '', formalCandidate: '', formalPid: 0,
  manifestSha256: '', releaseSha256: '', testDllSha256: '', formalDllSha256: '',
  deployedNetSha256: '', cardFrameSha256: '', netSourcePath: '', cardFrameSourcePath: '',
  manifestPath: '', bindingSha256: '', syntheticOnly: true,
  clientBoundariesSha256: 'E62BF9BCEC47C06727CA75D3ADF5C278E4FB5ACD6F0302E40531262A1DED06AB',
  selectorHelperSha256: 'C431C0BF87072655487DDF6C103062D9DC46E20FCDC127AB435BF44F98C03838',
  typescriptSha256: '569177652966BD528C319171C7DD22860DBF72BDE116CBC4F644F1D02BB12E39',
  nodeSha256: '3602F2BB1A10F2CBAB4C36886218A33C1AB3DB87290E73B033C46C77147D0237',
  knownHostsSha256: '8D0F71B88C9488400B7157CC84B77774D68BFA4E15F79E259656C81C6C2FC0C9',
  diagnosticMode: 'terminal-bounds-v2',
  publicBase: 'https://legion-12.com/testrun/', formalBase: 'https://legion-12.com/',
  wsEndpoint: 'wss://legion-12.com/testrun/ws',
})
let candidateBindingKey = ''

export function validateCandidateBinding(value, { allowSynthetic = false } = {}) {
  const keys = ['schema','candidate','formalCandidate','formalPid','manifestPath','manifestSha256',
    'releaseSha256','testDllSha256','formalDllSha256','deployedNetSha256','cardFrameSha256',
    'netSourcePath','cardFrameSourcePath','syntheticOnly']
  requireContract(isPlainObject(value) && Object.keys(value).sort().join('|') === keys.sort().join('|')
    && value.schema === 1 && typeof value.syntheticOnly === 'boolean'
    && (!value.syntheticOnly || allowSynthetic), 'candidate_binding_shape_invalid')
  requireContract(/^[0-9a-f]{40}$/.test(value.candidate)
    && value.candidate !== '3b7b6ba0cb8715ae01c029ef6460fc1428472e68'
    && /^[0-9a-f]{40}$/.test(value.formalCandidate)
    && value.candidate !== value.formalCandidate && Number.isSafeInteger(value.formalPid)
    && value.formalPid > 0, 'candidate_identity_invalid')
  for (const key of ['manifestSha256','releaseSha256','testDllSha256','formalDllSha256',
    'deployedNetSha256','cardFrameSha256'])
    requireContract(typeof value[key] === 'string' && /^[0-9A-F]{64}$/.test(value[key]),
      'candidate_hash_invalid')
  for (const key of ['manifestPath','netSourcePath','cardFrameSourcePath'])
    requireContract(typeof value[key] === 'string' && path.isAbsolute(value[key]),
      'candidate_source_path_invalid')
  requireContract(sha256File(value.netSourcePath, 1024 * 1024) === value.deployedNetSha256
    && sha256File(value.cardFrameSourcePath, 65536) === value.cardFrameSha256,
  'candidate_module_hash_mismatch')
  return value
}

export function loadCandidateBinding(bindingPath, expectedSha256, { allowSynthetic = false } = {}) {
  requireContract(typeof expectedSha256 === 'string' && /^[0-9A-F]{64}$/.test(expectedSha256),
    'candidate_binding_hash_required')
  requireContract(sha256File(bindingPath, 65536) === expectedSha256, 'candidate_binding_hash_mismatch')
  const value = validateCandidateBinding(readJsonFile(bindingPath, 65536), { allowSynthetic })
  requireContract(!candidateBindingKey || candidateBindingKey === expectedSha256,
    'candidate_binding_reentry_changed')
  if (!candidateBindingKey) {
    for (const key of Object.keys(FIXED))
      if (Object.hasOwn(value, key)) FIXED[key] = value[key]
    FIXED.bindingSha256 = expectedSha256
    candidateBindingKey = expectedSha256
    Object.freeze(FIXED)
  }
  // A synthetic fixture may exercise local pure consumers, never an execution entry.
  if (!value.syntheticOnly) validateManifest(value.manifestPath)
  return Object.freeze({ ...value, bindingSha256: expectedSha256 })
}

export const LIMITS = Object.freeze({
  isolatedSamples: 1,
  burstSamples: 6,
  minimumTerminalBytes: 169 * 1024,
  preferredTerminalBytes: 180 * 1024,
  maximumTerminalBytes: 300 * 1024,
  maximumAcceptedCommandsPerRoom: 96,
  businessBudgetMs: 10 * 60 * 1000,
  cleanupBudgetMs: 60 * 1000,
  requestTimeoutMs: 10 * 1000,
  terminalObservationMs: 15 * 1000,
  originalSloMs: 3000,
  barrierTargetMs: 10,
  barrierMaximumMs: 25,
})

export class ContractError extends Error {
  constructor(code) {
    super(code)
    this.name = 'ContractError'
    this.code = /^[a-z][a-z0-9_]{0,79}$/.test(code) ? code : 'contract_failure'
  }
}

export function requireContract(condition, code) {
  if (!condition) throw new ContractError(code)
}

export const isPlainObject = value => value !== null && typeof value === 'object' && !Array.isArray(value)

export function armSamples(arm) {
  if (arm === 'isolated') return LIMITS.isolatedSamples
  if (arm === 'burst') return LIMITS.burstSamples
  throw new ContractError('arm_invalid')
}

export function sha256Text(value) {
  return crypto.createHash('sha256').update(value, 'utf8').digest('hex').toUpperCase()
}

export function sha256File(filePath, maximumBytes = 200 * 1024 * 1024) {
  let item
  try { item = fs.lstatSync(filePath) } catch { throw new ContractError('bound_file_missing') }
  requireContract(item.isFile() && !item.isSymbolicLink() && item.size > 0 && item.size <= maximumBytes,
    'bound_file_invalid')
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex').toUpperCase()
}

export function readJsonFile(filePath, maximumBytes = 2 * 1024 * 1024) {
  let item
  try { item = fs.lstatSync(filePath) } catch { throw new ContractError('json_file_missing') }
  requireContract(item.isFile() && !item.isSymbolicLink() && item.size > 0 && item.size <= maximumBytes,
    'json_file_invalid')
  try { return JSON.parse(fs.readFileSync(filePath, 'utf8')) }
  catch { throw new ContractError('json_file_invalid') }
}

export function validateOwnedReceipt(receipt, { count, candidate = FIXED.candidate, nonceSha256, testPid }) {
  requireContract(count === 1 || count === 6, 'sample_count_invalid')
  requireContract(isPlainObject(receipt) && receipt.schema === 3 && receipt.status === 'installed',
    'owned_receipt_invalid')
  requireContract(receipt.candidate === candidate && receipt.formalCandidate === FIXED.formalCandidate,
    'owned_candidate_mismatch')
  requireContract(receipt.mode === FIXED.diagnosticMode && receipt.expectedSamples === count,
    'owned_count_mismatch')
  requireContract(typeof nonceSha256 === 'string' && /^[0-9A-F]{64}$/.test(nonceSha256)
    && String(receipt.nonceSha256).toUpperCase() === nonceSha256, 'owned_nonce_mismatch')
  requireContract(receipt.ownedTestConfigInstalled === true && receipt.startupExecEnvReady === true
    && receipt.currentDiagnosticStillActive === true && receipt.ephemeralTestMatches === true,
  'owned_lifecycle_incomplete')
  requireContract(receipt.formalWrites === 0 && Number.isSafeInteger(receipt.testPidAfter)
    && receipt.testPidAfter > 0, 'owned_runtime_invalid')
  if (testPid !== undefined) requireContract(receipt.testPidAfter === testPid, 'owned_pid_mismatch')
  requireContract(/^[0-9a-f]{64}$/i.test(String(receipt.configSha256)), 'owned_config_hash_invalid')
  return true
}

export function validateRemovalReceipt(receipt, { count, candidate = FIXED.candidate, priorPid }) {
  requireContract(isPlainObject(receipt) && receipt.schema === 3 && receipt.status === 'removed',
    'removal_receipt_invalid')
  requireContract(receipt.candidate === candidate && receipt.formalCandidate === FIXED.formalCandidate
    && receipt.mode === FIXED.diagnosticMode && receipt.expectedSamples === count,
  'removal_binding_mismatch')
  requireContract(receipt.ownedTestConfigRemoved === true && receipt.startupExecEnvReady === true
    && receipt.currentDiagnosticStillActive === false && receipt.ephemeralTestMatches === true
    && receipt.formalWrites === 0, 'removal_lifecycle_incomplete')
  requireContract(receipt.testPidBefore === priorPid && Number.isSafeInteger(receipt.testPidAfter)
    && receipt.testPidAfter > 0 && receipt.testPidAfter !== priorPid, 'removal_pid_invalid')
  return true
}

export function validateManifest(manifestPath) {
  const manifest = readJsonFile(manifestPath, 64 * 1024)
  requireContract(sha256File(manifestPath, 64 * 1024) === FIXED.manifestSha256,
    'manifest_hash_mismatch')
  requireContract(manifest.schema === 3 && manifest.commit === FIXED.candidate
    && manifest.releaseBaseCommit === FIXED.formalCandidate
    && String(manifest.releaseSha256).toUpperCase() === FIXED.releaseSha256,
  'manifest_binding_mismatch')
  const archive = path.resolve(String(manifest.releaseArchive ?? ''))
  requireContract(path.basename(archive) === `l12-release-${FIXED.candidate}.tar.gz`,
    'release_archive_name_mismatch')
  requireContract(sha256File(archive) === FIXED.releaseSha256, 'release_archive_hash_mismatch')
  return { manifestSha256: FIXED.manifestSha256, releaseSha256: FIXED.releaseSha256 }
}

export function validateRuntimeInputs({ candidate, testPid, formalPid, testDllSha256, formalDllSha256 }) {
  requireContract(FIXED.bindingSha256 && !FIXED.syntheticOnly && candidate === FIXED.candidate, 'candidate_mismatch')
  requireContract(Number.isSafeInteger(testPid) && testPid > 0 && Number.isSafeInteger(formalPid)
    && formalPid > 0 && testPid !== formalPid, 'runtime_pid_invalid')
  requireContract(String(testDllSha256).toUpperCase() === FIXED.testDllSha256,
    'test_dll_mismatch')
  requireContract(String(formalDllSha256).toUpperCase() === FIXED.formalDllSha256,
    'formal_dll_mismatch')
  return true
}

// Retained workload-size bound is for the restored complete JSON, not compressed wire bytes.
export function wireSizeValid(bytes) {
  return Number.isSafeInteger(bytes) && bytes > 0 && bytes <= 2 * 1024 * 1024
}

export function frameSizeValid(bytes) {
  return Number.isSafeInteger(bytes) && bytes >= LIMITS.minimumTerminalBytes
    && bytes <= LIMITS.maximumTerminalBytes
}

export function validateBarrier(sendOffsetsMs, count) {
  requireContract(Array.isArray(sendOffsetsMs) && sendOffsetsMs.length === count
    && sendOffsetsMs.every(value => Number.isFinite(value) && value >= 0), 'barrier_samples_invalid')
  const spread = count === 1 ? 0 : Math.max(...sendOffsetsMs) - Math.min(...sendOffsetsMs)
  requireContract(spread <= LIMITS.barrierMaximumMs, 'barrier_spread_exceeded')
  return spread
}

function validateBoundaryRow(row, slot) {
  requireContract(isPlainObject(row) && row.slot === slot && row.response_seen === 1
    && row.response_kind === 1 && row.action_accepted === 1 && row.terminal === 1
    && row.callback_valid === 1 && row.parse_valid === 1 && row.dispatch_valid === 1
    && row.receipt_send_valid === 1, 'client_boundary_incomplete')
  requireContract(Number.isFinite(row.send_to_callback_ms) && row.send_to_callback_ms >= 0
    && Number.isFinite(row.parse_ms) && row.parse_ms >= 0
    && Number.isFinite(row.dispatch_ms) && row.dispatch_ms >= 0
    && wireSizeValid(row.wire_bytes), 'client_boundary_value_invalid')
  requireContract(row.exceeded_original_3s === Number(row.send_to_callback_ms > LIMITS.originalSloMs),
    'client_slo_flag_invalid')
}

export function validateClientArmReport(report, expectedCount) {
  requireContract(expectedCount === 1 || expectedCount === 6, 'sample_count_invalid')
  requireContract(isPlainObject(report) && report.schema === 1
    && report.probe === 'large-terminal-public-v1' && report.candidate === FIXED.candidate
    && report.expectedSamples === expectedCount, 'client_report_binding_invalid')
  requireContract(report.productStateInjected === false && report.paddingUsed === false
    && report.terminalFullRequired === true && report.originalSloMs === LIMITS.originalSloMs,
  'client_report_semantics_invalid')
  requireContract(isPlainObject(report.runtime) && Number.isSafeInteger(report.runtime.testPid)
    && report.runtime.testPid > 0 && report.runtime.testDllSha256 === FIXED.testDllSha256,
  'client_runtime_invalid')
  requireContract(Array.isArray(report.boundaryRows) && report.boundaryRows.length === expectedCount,
    'client_sample_count_mismatch')
  report.boundaryRows.forEach((row, index) => validateBoundaryRow(row, index))
  requireContract(Array.isArray(report.rooms) && report.rooms.length === expectedCount,
    'client_room_count_mismatch')
  report.rooms.forEach((room, index) => {
    requireContract(room.slot === index && room.acceptedCommands >= 0
      && room.acceptedCommands <= LIMITS.maximumAcceptedCommandsPerRoom
      && Array.isArray(room.terminalFrames) && room.terminalFrames.length === 2,
    'client_room_invalid')
    room.terminalFrames.forEach((frame, receiver) => {
      requireContract(frame.receiver === receiver && frame.type === 'gameState'
        && frame.terminal === true && frame.delta === false && wireSizeValid(frame.wireBytes)
        && frameSizeValid(frame.logicalJsonBytes)
        && Number.isFinite(frame.sendToCallbackMs) && frame.sendToCallbackMs >= 0,
      'terminal_frame_invalid')
    })
  })
  const spread = validateBarrier(report.barrierSendOffsetsMs, expectedCount)
  requireContract(report.barrierSpreadMs === spread, 'barrier_spread_mismatch')
  requireContract(isPlainObject(report.terminalWindow)
    && Number.isFinite(Date.parse(report.terminalWindow.startUtc))
    && Number.isFinite(Date.parse(report.terminalWindow.endUtc))
    && Date.parse(report.terminalWindow.endUtc) >= Date.parse(report.terminalWindow.startUtc)
    && Date.parse(report.terminalWindow.endUtc) - Date.parse(report.terminalWindow.startUtc)
      <= LIMITS.terminalObservationMs, 'terminal_window_invalid')
  requireContract(isPlainObject(report.cleanup) && report.cleanup.failures === 0
    && report.cleanup.socketsClosed === expectedCount * 2
    && report.cleanup.playerSessionsRevoked === expectedCount * 2
    && report.cleanup.syntheticAccountsDeleted === expectedCount * 2
    && report.cleanup.adminSessionRevoked === 1, 'client_cleanup_incomplete')
  const breached = report.rooms.some(room => room.terminalFrames
    .some(frame => frame.sendToCallbackMs > LIMITS.originalSloMs))
  requireContract(report.sloBreached === breached, 'client_slo_summary_invalid')
  requireContract(report.status === (breached
    ? 'completed-with-slo-failure-awaiting-server-and-removal'
    : 'client-completed-awaiting-server-and-removal'), 'client_status_invalid')
  return { breached, spread }
}

export function validateServerEvidence(evidence, {
  count, testPid, nonceSha256, configReceiptSha256, clientReportSha256,
}) {
  requireContract(isPlainObject(evidence) && evidence.schema === 2
    && evidence.commit === FIXED.candidate && evidence.pid === testPid
    && String(evidence.dllSha256).toUpperCase() === FIXED.testDllSha256,
  'server_evidence_binding_invalid')
  requireContract(Array.isArray(evidence.rows) && evidence.rows.length === count,
    'server_sample_count_mismatch')
  requireContract(evidence.expectedSamples === count
    && evidence.nonceSha256 === nonceSha256
    && evidence.configReceiptSha256 === configReceiptSha256
    && evidence.clientReportSha256 === clientReportSha256,
  'server_safe_binding_invalid')
  evidence.rows.forEach((row, index) => {
    requireContract(row.slot === index && row.status === 1 && row.receive_valid === 1
      && row.receipt_valid === 1 && row.receipt_after_send_valid === 1
      && row.wire_delta === 0 && wireSizeValid(row.wire_bytes), 'server_row_invalid')
    requireContract(row.lease_valid === 0 && row.lease_start_rel_us === 0
      && row.lease_end_rel_us === 0, 'lease_na_not_proven')
  })
  const summary = evidence.summary
  requireContract(isPlainObject(summary) && summary.expected === count && summary.accepted === count
    && summary.completed === count && summary.missing === 0 && summary.deadline === 0
    && summary.duplicate === 0 && summary.invalid === 0 && summary.receipts === count
    && summary.receipt_missing === 0 && summary.receipt_duplicate === 0,
  'server_summary_incomplete')
  requireContract(evidence.observationComplete === true && evidence.leaseStageApplicable === 0
    && evidence.missingIsInconclusive === true && evidence.formalWrites === 0
    && evidence.payloadsExported === 0 && evidence.identifiersExported === 0,
  'server_evidence_semantics_invalid')
  return true
}

export function validateHealthPair(value) {
  requireContract(isPlainObject(value) && value.test?.status === 'ok'
    && value.test?.serverVersion === FIXED.candidate && value.formal?.status === 'ok'
    && value.formal?.serverVersion === FIXED.formalCandidate, 'final_health_invalid')
  return true
}

export function finalizeArm({ client, server, install, removal, clientReportSha256, configReceiptSha256 }) {
  const count = client.expectedSamples
  const clientResult = validateClientArmReport(client, count)
  const nonceHash = String(client.bindings?.nonceSha256 ?? '').toUpperCase()
  requireContract(/^[0-9A-F]{64}$/.test(String(clientReportSha256))
    && /^[0-9A-F]{64}$/.test(String(configReceiptSha256))
    && String(client.bindings?.configReceiptSha256 ?? '').toUpperCase() === configReceiptSha256,
  'finalize_source_hash_invalid')
  validateOwnedReceipt(install, { count, nonceSha256: nonceHash, testPid: client.runtime.testPid })
  validateServerEvidence(server, {
    count, testPid: client.runtime.testPid,
    nonceSha256: nonceHash,
    configReceiptSha256: configReceiptSha256 ?? client.bindings.configReceiptSha256,
    clientReportSha256,
  })
  validateRemovalReceipt(removal, { count, priorPid: client.runtime.testPid })
  return { count, sloBreached: clientResult.breached, zeroMissing: true, cleanupComplete: true }
}

export function selectLegalCommand(client, snapshot, room, blockedByVisibleDisaster = () => false) {
  const prompts = Array.isArray(snapshot?.prompts) ? snapshot.prompts : []
  const ownPrompts = prompts.filter(prompt => prompt?.playerIndex === client.playerIndex)
  for (const prompt of ownPrompts) {
    const choices = prompt?.validChoices
    const minimum = prompt?.minChoose
    const maximum = prompt?.maxChoose
    if (typeof prompt?.promptId !== 'string' || !prompt.promptId
        || !Array.isArray(choices) || choices.some(choice => typeof choice !== 'string' || !choice)
        || new Set(choices).size !== choices.length || !Number.isInteger(minimum)
        || !Number.isInteger(maximum) || minimum < 0 || maximum < minimum
        || minimum > choices.length
        || (prompt.data?.placementMode !== undefined && prompt.data?.placementMode !== null)) continue
    return {
      command: {
        type: 'resolvePrompt', promptId: prompt.promptId, cardInstanceIds: choices.slice(0, minimum),
        ...(prompt.activationId ? {
          activationId: prompt.activationId, sourceInstanceId: prompt.sourceInstanceId,
          sourceCardId: prompt.sourceCardId, step: prompt.step,
          createdRevision: prompt.createdRevision, controller: prompt.controller,
        } : {}),
      },
      revisionKey: `${client.playerIndex}:${snapshot.revision}:resolvePrompt:${prompt.promptId}`,
    }
  }
  if (ownPrompts.length > 0) return { skip: 'unsupported_authoritative_prompt' }
  if (snapshot?.phase === 'Defense' && snapshot.pendingDefense?.stage === 'DefenseChoice'
      && snapshot.pendingDefense.attackerPlayer !== client.playerIndex
      && (snapshot.effectStack?.length ?? 0) === 0 && !snapshot.waitingPrompt) {
    return { command: { type: 'resolveDefense', cardInstanceIds: [] },
      revisionKey: `${client.playerIndex}:${snapshot.revision}:resolveDefense` }
  }
  const own = snapshot?.players?.find(player => player?.playerIndex === client.playerIndex)
  if (snapshot?.phase === 'Mulligan' && own?.mulliganDone === false) {
    return { command: { type: 'mulligan', cardInstanceIds: [] },
      revisionKey: `${client.playerIndex}:${snapshot.revision}:mulligan` }
  }
  if (snapshot?.phase === 'Main' && snapshot.activePlayer === client.playerIndex
      && prompts.length === 0 && (snapshot.effectStack?.length ?? 0) === 0
      && snapshot.pendingDefense == null && snapshot.waitingPrompt == null) {
    const attack = Object.entries(snapshot.legalAttackTargets ?? {})
      .find(([id, targets]) => typeof id === 'string' && Array.isArray(targets)
        && targets.some(value => typeof value === 'string' && value !== 'master'))
    if (attack) return {
      command: { type: 'attack', cardInstanceId: attack[0],
        target: { type: 'legion', instanceId: attack[1].find(value => value !== 'master') } },
      revisionKey: `${client.playerIndex}:${snapshot.revision}:attack:${attack[0]}`,
    }
    const turnKey = `${client.playerIndex}:${snapshot.turnSerial ?? snapshot.round ?? snapshot.revision}`
    if (!room.playedTurns.has(turnKey) && Array.isArray(own?.field?.[0])) {
      const slot = own.field[0].findIndex(value => value === null)
      const budget = own.spendableResourceCount
      const card = own.hand?.find(value => value?.cardType === 'legion' && !value.playBlockedReason
        && !blockedByVisibleDisaster(snapshot, own, value) && typeof value.instanceId === 'string'
        && Number.isInteger(value.playCost) && Number.isInteger(budget)
        && value.playCost >= 0 && value.playCost <= budget)
      if (slot >= 0 && card) return {
        command: { type: 'playCard', cardInstanceId: card.instanceId, row: 0, slot,
          targetPlayerIndex: client.playerIndex, choice: 'normal-entry' },
        turnKey,
        revisionKey: `${client.playerIndex}:${snapshot.revision}:playCard:${card.instanceId}`,
      }
    }
    return { command: { type: 'endTurn' },
      revisionKey: `${client.playerIndex}:${snapshot.revision}:endTurn` }
  }
  return { skip: 'no_authoritative_command' }
}

export function sanitizedFailure(error) {
  return error instanceof ContractError ? error.code : 'unexpected_failure'
}
