import {
  ContractError, FIXED, LIMITS, frameSizeValid, wireSizeValid, isPlainObject, requireContract,
  validateBarrier, validateOwnedReceipt, validateRemovalReceipt, validateServerEvidence,
} from './large-terminal-contract-r6.mjs'

export { ContractError, FIXED, requireContract }

export const COHORT = Object.freeze({
  probe: 'cohort30-terminal-v2-v1',
  roomCount: 30,
  connectionCount: 60,
  traceSampleCount: 6,
  traceRoomSlots: Object.freeze([0, 5, 11, 17, 23, 29]),
  ordinaryRoomCount: 24,
  maximumAcceptedCommandsPerRoom: LIMITS.maximumAcceptedCommandsPerRoom,
  actionIntervalMs: 2000,
  minimumTerminalBytes: LIMITS.minimumTerminalBytes,
  preferredTerminalBytes: LIMITS.preferredTerminalBytes,
  maximumTerminalBytes: LIMITS.maximumTerminalBytes,
  originalSloMs: LIMITS.originalSloMs,
  barrierMaximumMs: LIMITS.barrierMaximumMs,
  terminalObservationMs: LIMITS.terminalObservationMs,
  businessBudgetMs: LIMITS.businessBudgetMs,
  cleanupBudgetMs: 4 * 60 * 1000,
})

const exactIntegers = (actual, expected) => Array.isArray(actual)
  && actual.length === expected.length
  && actual.every((value, index) => value === expected[index])

export function diagnosticSlotForRoom(roomSlot) {
  const index = COHORT.traceRoomSlots.indexOf(roomSlot)
  return index < 0 ? null : index
}

export function validateTraceRoomSlots(value) {
  requireContract(exactIntegers(value, COHORT.traceRoomSlots), 'trace_room_slots_invalid')
  return true
}

function validateBoundaryRow(row, diagnosticSlot, roomSlot) {
  requireContract(isPlainObject(row) && row.slot === diagnosticSlot && row.roomSlot === roomSlot
    && row.connection_slot === 0 && row.command_kind === 3 && row.response_seen === 1
    && row.response_kind === 1 && row.action_accepted === 1 && row.terminal === 1
    && row.callback_valid === 1 && row.parse_valid === 1 && row.dispatch_valid === 1
    && row.receipt_send_valid === 1, 'client_boundary_incomplete')
  requireContract(Number.isFinite(row.send_to_callback_ms) && row.send_to_callback_ms >= 0
    && Number.isFinite(row.parse_ms) && row.parse_ms >= 0
    && Number.isFinite(row.dispatch_ms) && row.dispatch_ms >= 0
    && wireSizeValid(row.wire_bytes), 'client_boundary_value_invalid')
  requireContract(row.exceeded_original_3s === Number(row.send_to_callback_ms > COHORT.originalSloMs),
    'client_boundary_slo_flag_invalid')
}

function validateTerminalFrame(frame, receiver) {
  requireContract(isPlainObject(frame) && frame.receiver === receiver && frame.type === 'gameState'
    && frame.terminal === true && frame.delta === false && wireSizeValid(frame.wireBytes)
    && frameSizeValid(frame.logicalJsonBytes)
    && ['gameState', 'gameStateCardFrameV1'].includes(frame.wireType)
    && Number.isFinite(frame.restoreMs) && frame.restoreMs >= 0
    && Number.isFinite(frame.sendToCallbackMs) && frame.sendToCallbackMs >= 0
    && Number.isFinite(frame.parseMs) && frame.parseMs >= 0,
  'terminal_frame_invalid')
  requireContract(frame.exceededOriginal3s === Number(frame.sendToCallbackMs > COHORT.originalSloMs),
    'terminal_frame_slo_flag_invalid')
}

export function validateClientCohortMeasurement(report) {
  requireContract(isPlainObject(report) && report.schema === 1 && report.probe === COHORT.probe
    && report.candidate === FIXED.candidate && report.formalCandidate === FIXED.formalCandidate,
  'client_report_binding_invalid')
  requireContract(report.roomCount === COHORT.roomCount
    && report.connectionCount === COHORT.connectionCount
    && report.expectedSamples === COHORT.traceSampleCount
    && report.traceSampleCount === COHORT.traceSampleCount
    && report.ordinaryRoomCount === COHORT.ordinaryRoomCount,
  'cohort_shape_invalid')
  validateTraceRoomSlots(report.traceRoomSlots)
  requireContract(report.productStateInjected === false && report.paddingUsed === false
    && report.terminalFullRequired === true && report.nativeFirstByteMeasured === false
    && report.oneWayNetworkMeasured === false && report.requesterOnlyServerTrace === true
    && report.peerServerTraceMeasured === false, 'client_report_semantics_invalid')
  requireContract(report.maximumAcceptedCommandsPerRoom === COHORT.maximumAcceptedCommandsPerRoom
    && report.actionIntervalMs === COHORT.actionIntervalMs
    && report.originalSloMs === COHORT.originalSloMs
    && report.businessBudgetMs === COHORT.businessBudgetMs,
  'client_limit_binding_invalid')
  requireContract(isPlainObject(report.runtime) && Number.isSafeInteger(report.runtime.testPid)
    && report.runtime.testPid > 0 && report.runtime.testDllSha256 === FIXED.testDllSha256
    && report.runtime.formalPid === FIXED.formalPid
    && report.runtime.formalDllSha256 === FIXED.formalDllSha256,
  'client_runtime_invalid')
  requireContract(Array.isArray(report.boundaryRows)
    && report.boundaryRows.length === COHORT.traceSampleCount, 'client_trace_count_invalid')
  report.boundaryRows.forEach((row, diagnosticSlot) =>
    validateBoundaryRow(row, diagnosticSlot, COHORT.traceRoomSlots[diagnosticSlot]))
  requireContract(Array.isArray(report.rooms) && report.rooms.length === COHORT.roomCount,
    'client_room_count_invalid')
  let terminalFrames = 0
  report.rooms.forEach((room, roomSlot) => {
    const diagnosticSlot = diagnosticSlotForRoom(roomSlot)
    requireContract(isPlainObject(room) && room.slot === roomSlot
      && room.diagnosticSlot === diagnosticSlot && room.terminalAttempted === true
      && room.terminalResends === 0 && Number.isSafeInteger(room.acceptedCommands)
      && room.acceptedCommands >= 0
      && room.acceptedCommands <= COHORT.maximumAcceptedCommandsPerRoom
      && Array.isArray(room.preTerminalBytes) && room.preTerminalBytes.length === 2
      && room.preTerminalBytes.every(frameSizeValid)
      && Array.isArray(room.terminalFrames) && room.terminalFrames.length === 2,
    'client_room_invalid')
    room.terminalFrames.forEach((frame, receiver) => validateTerminalFrame(frame, receiver))
    terminalFrames += room.terminalFrames.length
  })
  report.boundaryRows.forEach((row, diagnosticSlot) => {
    const actorFrame = report.rooms[COHORT.traceRoomSlots[diagnosticSlot]].terminalFrames[0]
    requireContract(row.wire_bytes === actorFrame.wireBytes
      && row.send_to_callback_ms === actorFrame.sendToCallbackMs
      && row.parse_ms === actorFrame.parseMs, 'client_boundary_frame_mismatch')
  })
  requireContract(report.terminalAttemptCount === COHORT.roomCount
    && report.terminalResendCount === 0 && report.diagnosticRequestCount === COHORT.traceSampleCount
    && report.ordinaryRequestCount === COHORT.ordinaryRoomCount
    && report.terminalFrameCount === COHORT.connectionCount
    && terminalFrames === COHORT.connectionCount, 'terminal_coverage_invalid')
  const spread = validateBarrier(report.barrierSendOffsetsMs, COHORT.roomCount)
  requireContract(report.barrierSpreadMs === spread, 'barrier_spread_mismatch')
  requireContract(isPlainObject(report.terminalWindow)
    && Number.isFinite(Date.parse(report.terminalWindow.startUtc))
    && Number.isFinite(Date.parse(report.terminalWindow.endUtc))
    && Date.parse(report.terminalWindow.endUtc) >= Date.parse(report.terminalWindow.startUtc)
    && Date.parse(report.terminalWindow.endUtc) - Date.parse(report.terminalWindow.startUtc)
      <= COHORT.terminalObservationMs, 'terminal_window_invalid')
  requireContract(report.resourceGuardStopFileConfigured === true
    && report.guardStopObserved === false, 'client_guard_incomplete')
  requireContract(report.payloadsExported === 0 && report.identifiersExported === 0,
    'private_evidence_exported')
  requireContract(report.frameProtocol?.capability === 'gameStateCardFrameV1'
    && report.frameProtocol.decoderFailures === 0
    && report.bindings?.candidateBindingSha256 === FIXED.bindingSha256
    && report.bindings?.cardFrameSha256 === FIXED.cardFrameSha256,
  'client_frame_protocol_incomplete')
  const breached = report.rooms.some(room => room.terminalFrames
    .some(frame => frame.sendToCallbackMs > COHORT.originalSloMs))
  requireContract(report.sloBreached === breached, 'client_slo_summary_invalid')
  return { breached, spread, terminalFrames }
}

// Collection is not final acceptance. It preserves complete terminal facts even when
// an independently reported account cleanup fails; no public status is rewritten.
export function validateClientCohortReport(report) {
  const result = validateClientCohortMeasurement(report)
  requireContract(isPlainObject(report.cleanup) && report.cleanup.failures === 0
    && report.cleanup.socketsClosed === COHORT.connectionCount
    && report.cleanup.playerSessionsRevoked === COHORT.connectionCount
    && report.cleanup.syntheticAccountsDeleted === COHORT.connectionCount
    && report.cleanup.adminSessionRevoked === 1, 'client_cleanup_incomplete')
  requireContract(report.status === (result.breached
    ? 'completed-with-slo-failure-awaiting-server-and-removal'
    : 'client-completed-awaiting-server-and-removal'), 'client_status_invalid')
  requireContract(report.ledger?.seq === 120 && report.ledger?.count === 60
    && /^[0-9a-f]{64}$/.test(report.ledger?.cipherSha256 ?? ''), 'client_ledger_incomplete')
  return result
}

export function validateCohortServerEvidence(evidence, bindings) {
  const { clientBoundaryRows, ...serverBindings } = bindings
  validateServerEvidence(evidence, { count: COHORT.traceSampleCount, ...serverBindings })
  requireContract(evidence.scope === 'one-shot synthetic numeric v2, cohort30 terminal-only six markers'
    && evidence.roomCount === COHORT.roomCount
    && evidence.traceSampleCount === COHORT.traceSampleCount
    && exactIntegers(evidence.traceRoomSlots, COHORT.traceRoomSlots),
  'server_cohort_scope_invalid')
  requireContract(Array.isArray(clientBoundaryRows)
    && clientBoundaryRows.length === COHORT.traceSampleCount
    && evidence.rows.every((row, diagnosticSlot) =>
      row.wire_bytes === clientBoundaryRows[diagnosticSlot].wire_bytes),
  'server_client_frame_mismatch')
  return true
}

export function validateLifecycle(lifecycle, hashes) {
  requireContract(isPlainObject(lifecycle) && lifecycle.schema === 1
    && lifecycle.probe === COHORT.probe && lifecycle.roomCount === COHORT.roomCount
    && lifecycle.expectedSamples === COHORT.traceSampleCount && lifecycle.formalWrites === 0
    && lifecycle.deployment === 0 && lifecycle.guardStopped === false
    && lifecycle.cleanupComplete === true && lifecycle.evidenceComplete === true
    && lifecycle.completed === true && lifecycle.failure === null
    && lifecycle.cleanupFailure === null && [0, 2].includes(lifecycle.businessExit),
  'lifecycle_incomplete_or_unknown')
  requireContract(isPlainObject(lifecycle.bindings)
    && lifecycle.bindings.clientSha256 === hashes.clientReportSha256
    && lifecycle.bindings.serverSha256 === hashes.serverEvidenceSha256
    && lifecycle.bindings.installSha256 === hashes.configReceiptSha256
    && lifecycle.bindings.removalSha256 === hashes.removalReceiptSha256
    && lifecycle.bindings.sessionCompletionSha256 === hashes.sessionCompletionSha256,
  'lifecycle_hash_binding_invalid')
  return true
}

export function validateSessionCompletion(sessionCompletion) {
  requireContract(isPlainObject(sessionCompletion) && sessionCompletion.schema === 1
    && sessionCompletion.status === 'EXACT_TEST_SESSION_REVOCATION_VERIFIED'
    && sessionCompletion.revoked === true && sessionCompletion.afterStatus === 401
    && sessionCompletion.formalWrites === 0
    && sessionCompletion.credentialsDisplayed === false,
  'session_revocation_unknown')
  return true
}

export function finalizeCohort({
  client, server, install, removal, lifecycle, sessionCompletion, hashes,
}) {
  const clientResult = validateClientCohortReport(client)
  requireContract(lifecycle.ledger?.verified === true
    && lifecycle.ledger.seq === client.ledger.seq && lifecycle.ledger.count === client.ledger.count
    && lifecycle.ledger.cipherSha256 === client.ledger.cipherSha256,
  'final_ledger_binding_invalid')
  const nonceSha256 = String(client.bindings?.nonceSha256 ?? '').toUpperCase()
  for (const key of ['clientReportSha256', 'serverEvidenceSha256', 'configReceiptSha256',
    'removalReceiptSha256', 'sessionCompletionSha256']) {
    requireContract(/^[0-9A-F]{64}$/.test(String(hashes?.[key] ?? '')), 'final_hash_invalid')
  }
  requireContract(String(client.bindings?.configReceiptSha256 ?? '').toUpperCase()
    === hashes.configReceiptSha256, 'client_config_hash_mismatch')
  validateOwnedReceipt(install, { count: COHORT.traceSampleCount, nonceSha256,
    testPid: client.runtime.testPid })
  requireContract(install.formalPid === client.runtime.formalPid
    && client.runtime.formalPid === FIXED.formalPid
    && client.runtime.formalDllSha256 === FIXED.formalDllSha256,
  'install_formal_runtime_mismatch')
  validateCohortServerEvidence(server, {
    testPid: client.runtime.testPid, nonceSha256,
    configReceiptSha256: hashes.configReceiptSha256,
    clientReportSha256: hashes.clientReportSha256,
    clientBoundaryRows: client.boundaryRows,
  })
  validateRemovalReceipt(removal, { count: COHORT.traceSampleCount,
    priorPid: client.runtime.testPid })
  validateSessionCompletion(sessionCompletion)
  validateLifecycle(lifecycle, hashes)
  requireContract(lifecycle.businessExit === (clientResult.breached ? 2 : 0),
    'lifecycle_exit_slo_mismatch')
  return {
    roomCount: COHORT.roomCount,
    traceSampleCount: COHORT.traceSampleCount,
    terminalFrameCount: clientResult.terminalFrames,
    sloBreached: clientResult.breached,
    zeroMissing: true,
    cleanupComplete: true,
  }
}
