import assert from 'node:assert/strict'
import {PLAN, FROZEN_SHA, EXECUTION_GAPS} from './testrun-pressure-protocol.mjs'

const integer = (value, minimum = 0) => assert.ok(Number.isSafeInteger(value) && value >= minimum)
const nonnegative = value => assert.ok(Number.isFinite(value) && value >= 0)
const sha = value => assert.match(value, /^[0-9A-F]{64}$/)
const zero = value => assert.equal(value, 0)
const empty = value => assert.deepEqual(value, [])
const requiredHttp = ['authMe', 'deckSummaries', 'rankedProfile', 'rankings', 'tournamentSummaries',
  'publicDecks', 'matchHistory', 'cardAnalytics', 'audit', 'tournamentDetail']

// This validates prospective input contracts only. No CLI accepts real run files,
// and no function can currently emit a live capacity acceptance receipt.
export function inspectNormalPressureEvidence(evidence, binding) {
  for (const forbidden of ['install', 'removal', 'serverRows', 'legacyServerEvidence'])
    assert.ok(!Object.hasOwn(evidence, forbidden), 'normal evidence must not fabricate old recorder receipts')
  const {preflight, report, guard, ledgers, lifecycle, postflight} = evidence
  assert.equal(evidence.schema, 1)
  assert.equal(evidence.frozenCoreSha256, FROZEN_SHA)
  sha(evidence.bindingSha256)
  assert.equal(preflight.baseUrl, 'https://legion-12.com/testrun/')
  assert.equal(preflight.testPid, binding.test.pid); assert.equal(preflight.formalPid, binding.formal.pid)
  assert.notEqual(preflight.testPid, preflight.formalPid)
  assert.equal(preflight.diagnosticsOff, true); assert.equal(preflight.matchStorage, 'ephemeral')
  assert.deepEqual(preflight.privateDeck, {configuredEnabled: true, effectiveMode: 'object', writable: true})
  assert.equal(preflight.sharedWindowExclusive, true)
  for (const name of ['analyticsItems', 'auditItems', 'tournamentItems']) integer(preflight[name], 1)
  assert.deepEqual(preflight.resourcePolicy, {cpuWeight: 10, ioWeight: 10, nice: 10, oomScoreAdjust: 750,
    memoryHighBytes: 768 * 1024 ** 2, memoryMaxBytes: 896 * 1024 ** 2})
  for (const [name, cap] of [['programBytes', 1.5 * 1024 ** 3], ['dataBytes', 512 * 1024 ** 2],
    ['temporaryPeakBytes', 1024 ** 3], ['totalPeakBytes', 3 * 1024 ** 3]]) {
    integer(preflight.disk[name]); assert.ok(preflight.disk[name] <= cap, 'original logical-byte disk cap exceeded')
  }
  assert.ok(preflight.disk.formalFreeBytes >= 8 * 1024 ** 3)
  assert.equal(report.schemaVersion, 1); assert.equal(report.status, 'completed')
  assert.equal(report.failure, null); assert.equal(report.stopReason, null)
  assert.equal(report.expectedCommit.value, binding.test.commit); assert.equal(report.observedCommit, binding.test.commit)
  assert.deepEqual(report.artifactBinding, {candidateBindingSha256: evidence.bindingSha256,
    manifestSha256: binding.test.manifestSha256, releaseSha256: binding.test.releaseSha256,
    testPid: binding.test.pid, formalPid: binding.formal.pid, deployedNetSha256: binding.client.netSha256,
    cardFrameSha256: binding.client.cardFrameSha256, handArraySha256: binding.client.handArraySha256})
  const startedUtcMs = Date.parse(report.startedAt), terminalStart = Date.parse(report.terminalWindow.startUtc)
  const terminalEnd = Date.parse(report.terminalWindow.endUtc)
  assert.ok(Number.isFinite(startedUtcMs) && Number.isFinite(terminalStart) && Number.isFinite(terminalEnd)
    && terminalStart >= startedUtcMs && terminalEnd >= terminalStart && terminalEnd - terminalStart <= 270000)
  assert.equal(lifecycle.businessStartedUtcMs, startedUtcMs)
  assert.ok(lifecycle.cleanupFinishedUtcMs >= terminalEnd && lifecycle.cleanupFinishedUtcMs - startedUtcMs >= 180000)
  assert.equal(report.deltaReconstruction.sourceSha256, binding.client.netSha256)
  zero(report.deltaReconstruction.errors); zero(report.presenceDeltaReconstruction.errors)
  assert.equal(report.presenceDeltaReconstruction.sourceSha256, binding.client.netSha256)
  const budget = report.budget
  assert.deepEqual(budget.roomStages, PLAN.stages)
  for (const [name, value] of Object.entries({stageSeconds: 60, maximumRooms: 30, playerIdentities: 60,
    distinctOutsideUsers: 10, syntheticIdentities: 69, maximumIdentities: 70,
    httpIntervalMs: 2000, wsPingIntervalMs: 2000, actionInspectionIntervalMs: 2000,
    maximumCommandsPerRoomPerInterval: 1, requestTimeoutMs: 10000})) assert.equal(budget[name], value)
  assert.equal(budget.disjointMatchAndOutsideAccountPools, true)
  assert.equal(report.stages.length, 3)
  report.stages.forEach((stage, index) => {
    assert.equal(stage.rooms, PLAN.stages[index]); assert.ok(stage.durationMs >= 60000)
    assert.equal(stage.minimumActiveRooms, stage.rooms); assert.equal(stage.maximumActiveRooms, stage.rooms)
    assert.equal(stage.outsideUsers, 10); assert.equal(stage.measuredDistinctOutsideUsers, 10)
    const measured = report.outsideHttpByStage[stage.name]
    for (const name of requiredHttp) {integer(measured[name]?.requests, 1); zero(measured[name].errors)}
  })
  for (const name of requiredHttp) {
    const row = report.http[name]
    assert.ok(row && !row.skipped); integer(row.requests, 1); zero(row.errors)
  }
  integer(report.commands.overall.attempted, 1)
  for (const outcome of ['rejected', 'timed-out', 'transport-error', 'advanced-without-ack']) zero(report.commands.overall.outcomes[outcome])
  assert.equal(report.frameProtocol.capability, 'gameStateCardFrameV1'); zero(report.frameProtocol.decoderFailures)
  const hand = report.currentHandArrayDelta
  integer(hand.negotiatedSessions, 60); integer(hand.packets, 1); integer(hand.wireBytes, 1)
  assert.equal(hand.applied, hand.packets); zero(hand.rejected)
  const floor = report.payloadFloorPrecheck
  assert.equal(floor.status, 'ready'); assert.equal(floor.workersFrozen, true)
  assert.equal(floor.expectedRooms, 30); assert.equal(floor.observedRooms, 30); empty(floor.issues)
  assert.equal(floor.minimumLogicalBytes, PLAN.minimumLogicalBytes); assert.equal(floor.maximumLogicalBytes, PLAN.maximumLogicalBytes)
  assert.equal(floor.rows.length, 30)
  floor.rows.forEach((row, index) => {
    assert.equal(row.numericRoomSlot, index)
    for (const key of ['sameMatch', 'sameRoom', 'sameRevision']) assert.equal(row[key], true)
    integer(row.exactAckMatches, 1); assert.equal(row.peerConvergenceMatches, row.exactAckMatches)
    assert.equal(row.receivers.length, 2)
    row.receivers.forEach((peer, receiver) => {
      assert.equal(peer.receiver, receiver); integer(peer.revision)
      assert.ok(peer.logicalJsonBytes >= PLAN.minimumLogicalBytes && peer.logicalJsonBytes <= PLAN.maximumLogicalBytes)
    })
    assert.equal(row.receivers[0].revision, row.receivers[1].revision)
  })
  const burst = report.wsBurstClient
  assert.equal(burst.captureComplete, 1); assert.equal(burst.terminalCoverageComplete, 1)
  assert.equal(burst.connectionsObserved, 60); zero(burst.droppedFrames); zero(burst.invalidRecords)
  assert.equal(report.terminalFrames.length, 60)
  assert.equal(new Set(report.terminalFrames.map(row => row.numericClientSlot)).size, 60)
  report.terminalFrames.forEach(frame => {
    integer(frame.numericClientSlot); assert.ok(frame.numericClientSlot < 60)
    assert.equal(frame.numericRoomSlot, Math.floor(frame.numericClientSlot / 2)); assert.equal(frame.receiver, frame.numericClientSlot % 2)
    assert.equal(frame.type, 'gameState'); assert.equal(frame.terminal, true); assert.equal(frame.delta, false)
    assert.ok(['gameState', 'gameStateCardFrameV1'].includes(frame.wireType))
    integer(frame.wireBytes, 1)
    assert.ok(frame.wireBytes <= 2 * 1024 * 1024)
    integer(frame.logicalJsonBytes)
    assert.ok(frame.logicalJsonBytes >= 169 * 1024 && frame.logicalJsonBytes <= 300 * 1024)
    nonnegative(frame.sendToCallbackMs); assert.ok(frame.sendToCallbackMs <= 3000)
    zero(frame.exceededOriginal3s); nonnegative(frame.parseMs); nonnegative(frame.restoreMs)
  })
  const markers = report.actionBoundsClient
  assert.equal(markers.expected_samples, 6); assert.equal(markers.begun_samples, 6)
  assert.equal(markers.surrender_begun, 6); zero(markers.play_card_begun); zero(markers.end_turn_begun)
  for (const key of ['invalid_records', 'expired_begins', 'late_callbacks']) zero(markers[key])
  zero(markers.client.invalid_records); assert.equal(markers.client.rows.length, 6); assert.equal(markers.client_complete, 1)
  markers.client.rows.forEach((row, index) => {
    assert.equal(row.slot, index); assert.equal(row.command_kind, 3)
    for (const key of ['response_kind', 'terminal', 'response_seen', 'callback_valid', 'parse_valid', 'dispatch_valid', 'receipt_send_valid']) assert.equal(row[key], 1)
    const frame = report.terminalFrames.find(item => item.numericClientSlot === [0, 5, 11, 17, 23, 29][index] * 2)
    assert.equal(row.wire_bytes, frame.wireBytes); assert.equal(row.send_to_callback_ms, frame.sendToCallbackMs)
    assert.equal(row.parse_ms, frame.parseMs); zero(row.exceeded_original_3s)
  })
  assert.equal(report.cleanupPassiveTail.originalFailureThresholdMs, 3000)
  assert.equal(report.cleanupPassiveTail.passiveOnlyMs, 10000); zero(report.cleanupPassiveTail.retries)
  const cleanup = report.cleanup
  for (const [name, value] of Object.entries({roomsCreated: 30, compliantSurrenders: 30, roomsLeft: 30, sessionsRevoked: 70})) assert.equal(cleanup[name], value)
  empty(cleanup.roomErrors); empty(cleanup.sessionErrors)
  for (const name of ['planned', 'registrationAttempts', 'confirmed', 'logicalDeletesConfirmed']) assert.equal(cleanup.syntheticAccounts[name], 69)
  zero(cleanup.syntheticAccounts.uncertainRegistrationResponses); empty(cleanup.syntheticAccounts.errors)
  assert.equal(ledgers.length, 2)
  assert.equal(report.ledgerReceipts.length, 2)
  ledgers.forEach((ledger, index) => {
    assert.equal(ledger.verified, true); assert.equal(ledger.coldReadVerified, true)
    assert.equal(ledger.count, [60, 9][index]); assert.equal(ledger.seq, [120, 18][index]); sha(ledger.cipherSha256)
    assert.equal(report.ledgerReceipts[index].count, ledger.count); assert.equal(report.ledgerReceipts[index].seq, ledger.seq)
    assert.equal(report.ledgerReceipts[index].cipherSha256.toUpperCase(), ledger.cipherSha256)
  })
  assert.notEqual(ledgers[0].cipherSha256, ledgers[1].cipherSha256)
  for (const name of ['businessSucceeded', 'captureSucceeded', 'captureBeforeTeardown', 'teardownSucceeded',
    'independentRecoverySessionVerified', 'allOwnedHelpersExited', 'originalSessionRevoked', 'recoverySessionRevoked']) assert.equal(lifecycle[name], true)
  zero(lifecycle.formalWrites); zero(lifecycle.serviceRestarts); zero(lifecycle.maintenanceWrites)
  assert.equal(guard.stopRequested, false); assert.equal(guard.staleLimitMs, 5000)
  assert.ok(guard.rows.length >= 2)
  guard.rows.forEach((row, index) => {
    assert.equal(row.readOnly, true); assert.equal(row.stop, false); assert.equal(row.phase, 'complete')
    assert.equal(row.testPid, binding.test.pid); assert.equal(row.formalPid, binding.formal.pid)
    nonnegative(row.roundElapsedMs); assert.ok(row.roundElapsedMs < 3750)
    integer(row.testMemoryBytes); assert.ok(row.testMemoryBytes < 850 * 1024 ** 2)
    integer(row.hostAvailableBytes); assert.ok(row.hostAvailableBytes >= 512 * 1024 ** 2)
    zero(row.oomDelta); zero(row.restartDelta); integer(row.observedUtcMs)
    if (index) assert.ok(row.observedUtcMs > guard.rows[index - 1].observedUtcMs && row.observedUtcMs - guard.rows[index - 1].observedUtcMs < 5000)
  })
  assert.ok(guard.rows[0].observedUtcMs <= lifecycle.businessStartedUtcMs)
  assert.ok(guard.rows.at(-1).observedUtcMs >= lifecycle.cleanupFinishedUtcMs)
  for (const [environment, identity] of [['test', binding.test], ['formal', binding.formal]]) {
    const end = postflight[environment]
    assert.equal(end.pid, identity.pid); assert.equal(end.commit, identity.commit); assert.equal(end.dllSha256, identity.dllSha256)
    assert.equal(end.status, 'ok'); assert.equal(end.maintenance, false); assert.equal(end.diagnosticsOff, true); zero(end.restartDelta)
  }
  return {schema: 1, status: 'prospective-input-contract-valid', originalSloMs: 3000,
    terminalFrames: 60, legacyRecorderEvidenceClaimed: false, onlineCapacityAccepted: false,
    remoteApplyReady: false, executionEnabled: false, missingExecutionComponents: [...EXECUTION_GAPS],
    boundary: 'Pure validation of supplied data only; this is not a real run, independent review or execution admission.'}
}
