import fs from 'node:fs'
import path from 'node:path'
import {fileURLToPath} from 'node:url'
import {execFileSync} from 'node:child_process'
import {FIXED,loadCandidateBinding,sha256File,readJsonFile,requireContract,frameSizeValid,wireSizeValid,validateOwnedReceipt,
 validateServerEvidence,validateRemovalReceipt,validateHealthPair}
 from './cohort30-candidate-frame-r6/large-terminal-contract-r6.mjs'
import {validateSessionCompletion}
 from './cohort30-candidate-frame-r6/cohort30-contract-r6.mjs'
export const ORIGINAL_WORKLOAD = Object.freeze({stages:Object.freeze([10,20,30]),stageSeconds:60,
 outsideUsers:10,playerAccounts:60,outerSyntheticAccounts:9,totalAccounts:69,totalSessions:70,
 httpIntervalMs:2000,wsIntervalMs:2000,actionIntervalMs:2000,originalSloMs:3000,
 expectedMarkers:6,terminalFrames:60,ledgerMaxSlots:60})
export function validateOriginalWorkloadPlan(value) {
 requireContract(value?.stageSeconds===60&&value?.httpIntervalMs===2000&&value?.wsIntervalMs===2000,
  'original_workload_cadence_or_duration_changed')
 return true
}
export function validateFullCollectionSafetyInput(report) {
 requireContract(report?.schemaVersion===1&&report.expectedCommit?.value===FIXED.candidate
  &&report.artifactBinding?.candidateBindingSha256===FIXED.bindingSha256
  &&report.artifactBinding?.cardFrameSha256===FIXED.cardFrameSha256
  &&report.artifactBinding?.deployedNetSha256===FIXED.deployedNetSha256
  &&Number.isSafeInteger(report.artifactBinding.configTestPid)&&report.artifactBinding.configTestPid>0
  &&/^[0-9a-f]{64}$/i.test(report.artifactBinding.nonceSha256??'')
  &&/^[0-9a-f]{64}$/i.test(report.artifactBinding.configReceiptSha256??''),
  'collection_safe_binding_invalid')
 const start=Date.parse(report.terminalWindow?.startUtc),end=Date.parse(report.terminalWindow?.endUtc)
 requireContract(Number.isFinite(start)&&Number.isFinite(end)&&end>=start&&end-start<=270000,
  'collection_terminal_window_invalid')
 // This authorizes only bounded numeric collection. Missing client terminal
 // frames or cleanup failures remain failures and never suppress safe capture.
 return {startUtc:new Date(start-15000).toISOString(),endUtc:new Date(end+15000).toISOString()}
}
export function validateFullCollectionInput(report) {
 requireContract(report?.schemaVersion===1&&report.expectedCommit?.value===FIXED.candidate
  &&report.artifactBinding?.candidateBindingSha256===FIXED.bindingSha256
  &&report.artifactBinding?.cardFrameSha256===FIXED.cardFrameSha256
  &&report.artifactBinding?.deployedNetSha256===FIXED.deployedNetSha256,'full_report_candidate_binding_invalid')
 const markers=report.actionBoundsClient
 requireContract(markers?.expected_samples===6&&markers.begun_samples===6
  &&markers.play_card_begun===0&&markers.end_turn_begun===0&&markers.surrender_begun===6
  &&markers.invalid_records===0&&markers.expired_begins===0&&markers.late_callbacks===0
  &&markers.client?.invalid_records===0&&markers.client.rows.length===6,'six_terminal_markers_required')
 markers.client.rows.forEach((row,index)=>requireContract(row.slot===index&&row.command_kind===3
  &&row.response_kind===1&&row.terminal===1&&row.response_seen===1&&row.callback_valid===1
  &&row.parse_valid===1&&row.dispatch_valid===1&&row.receipt_send_valid===1
  &&Number.isFinite(row.send_to_callback_ms)&&row.send_to_callback_ms>=0
  &&wireSizeValid(row.wire_bytes),'terminal_marker_incomplete'))
 requireContract(Array.isArray(report.terminalFrames)&&report.terminalFrames.length===60
  &&new Set(report.terminalFrames.map(frame=>frame.numericClientSlot)).size===60,'sixty_terminal_frames_required')
 report.terminalFrames.forEach(frame=>requireContract(Number.isInteger(frame.numericClientSlot)
  &&frame.numericClientSlot>=0&&frame.numericClientSlot<60
  &&frame.numericRoomSlot===Math.floor(frame.numericClientSlot/2)&&frame.receiver===frame.numericClientSlot%2
  &&frame.type==='gameState'&&frame.terminal===true&&frame.delta===false
  &&['gameState','gameStateCardFrameV1'].includes(frame.wireType)
  &&wireSizeValid(frame.wireBytes)&&frameSizeValid(frame.logicalJsonBytes)
  &&Number.isFinite(frame.sendToCallbackMs)&&frame.sendToCallbackMs>=0
  &&frame.exceededOriginal3s===Number(frame.sendToCallbackMs>3000)
  &&Number.isFinite(frame.parseMs)&&frame.parseMs>=0&&Number.isFinite(frame.restoreMs)&&frame.restoreMs>=0,
  'terminal_frame_incomplete'))
 markers.client.rows.forEach((row,index)=>{
  const actor=report.terminalFrames.find(frame=>frame.numericClientSlot===[0,5,11,17,23,29][index]*2)
  requireContract(actor&&row.wire_bytes===actor.wireBytes&&row.send_to_callback_ms===actor.sendToCallbackMs
   &&row.parse_ms===actor.parseMs,'terminal_marker_actor_frame_mismatch')
 })
 return true
}
export function validateFullWorkloadFinal({report,server,install,removal,health,sessionCompletion,ledgers,lifecycle,
 clientReportSha256,configReceiptSha256}) {
 validateFullCollectionSafetyInput(report)
 validateFullCollectionInput(report)
 requireContract(report.status==='completed'&&report.failure===null&&report.stopReason===null
  &&report.frameProtocol?.decoderFailures===0&&report.deltaReconstruction?.errors===0
  &&report.presenceDeltaReconstruction?.errors===0,'full_workload_client_failed')
 requireContract(report.observedCommit===FIXED.candidate
  &&report.artifactBinding.manifestSha256===FIXED.manifestSha256
  &&String(report.artifactBinding.releaseSha256).toUpperCase()===FIXED.releaseSha256
  &&report.artifactBinding.configFormalPid===FIXED.formalPid
  &&report.frameProtocol.capability==='gameStateCardFrameV1','full_runtime_artifact_binding_invalid')
 validateOriginalWorkloadPlan({stageSeconds:report.budget?.stageSeconds,
  httpIntervalMs:report.budget?.httpIntervalMs,wsIntervalMs:report.budget?.wsPingIntervalMs})
 requireContract(report.budget?.actionInspectionIntervalMs===2000&&report.budget?.maximumRooms===30
  &&report.budget?.syntheticIdentities===69&&report.budget?.maximumIdentities===70
  &&report.budget?.distinctOutsideUsers===10
  &&JSON.stringify(report.budget?.roomStages)==='[10,20,30]'
  &&Array.isArray(report.stages)&&report.stages.length===3,'original_full_budget_changed')
 report.stages.forEach((stage,index)=>requireContract(stage.rooms===[10,20,30][index]
  &&stage.durationMs>=60000&&stage.minimumActiveRooms===stage.rooms&&stage.maximumActiveRooms===stage.rooms
  &&stage.outsideUsers===10&&stage.measuredDistinctOutsideUsers===10,'original_stage_incomplete'))
 requireContract(report.terminalFrames.every(frame=>frame.sendToCallbackMs<=3000)
  &&report.actionBoundsClient.client.rows.every(row=>row.exceeded_original_3s===0)
  &&report.actionBoundsClient.client_complete===1,'original_3s_failed')
 requireContract(Object.keys(report.http??{}).length>0
  &&Object.values(report.http).filter(value=>!value.skipped).every(value=>value.requests>0&&value.errors===0)
  &&report.commands?.overall?.attempted>0
  &&['rejected','timed-out','transport-error','advanced-without-ack'].every(kind=>report.commands.overall.outcomes[kind]===0),
  'original_operations_or_http_failed')
 requireContract(report.wsBurstClient?.captureComplete===1&&report.wsBurstClient.terminalCoverageComplete===1
  &&report.wsBurstClient.connectionsObserved===60&&report.wsBurstClient.droppedFrames===0
  &&report.wsBurstClient.invalidRecords===0,'complete_terminal_observation_required')
 const cleanup=report.cleanup
 requireContract(cleanup?.roomsCreated===30&&cleanup.compliantSurrenders===30&&cleanup.roomsLeft===30
  &&cleanup.sessionsRevoked===70&&cleanup.roomErrors.length===0&&cleanup.sessionErrors.length===0
  &&cleanup.syntheticAccounts?.planned===69&&cleanup.syntheticAccounts.registrationAttempts===69
  &&cleanup.syntheticAccounts.confirmed===69&&cleanup.syntheticAccounts.uncertainRegistrationResponses===0
  &&cleanup.syntheticAccounts.logicalDeletesConfirmed===69&&cleanup.syntheticAccounts.errors.length===0,
  'seventy_sessions_sixtynine_accounts_cleanup_required')
 requireContract(ledgers?.length===2&&ledgers[0]!==ledgers[1]
  &&ledgers.every(item=>item.verified===true&&/^[0-9a-f]{64}$/.test(item.cipherSha256??''))
  &&ledgers[0].count===60&&ledgers[0].seq===120&&ledgers[1].count===9&&ledgers[1].seq===18
  &&ledgers[0].cipherSha256!==ledgers[1].cipherSha256,'two_durable_ledgers_incomplete')
 requireContract(report.ledgerReceipts?.length===2&&report.ledgerReceipts.every((item,index)=>
  item.seq===ledgers[index].seq&&item.count===ledgers[index].count
  &&item.cipherSha256===ledgers[index].cipherSha256),'client_dual_ledger_binding_mismatch')
 requireContract(lifecycle?.captureSucceeded===true&&lifecycle.teardownSucceeded===true
  &&lifecycle.businessSucceeded===true&&lifecycle.captureBeforeTeardown===true
  &&lifecycle.guardStopped===false,'full_lifecycle_or_guard_incomplete')
 validateOwnedReceipt(install,{count:6,nonceSha256:report.artifactBinding.nonceSha256,
  testPid:report.artifactBinding.configTestPid})
 validateServerEvidence(server,{count:6,testPid:report.artifactBinding.configTestPid,
  nonceSha256:report.artifactBinding.nonceSha256,configReceiptSha256,clientReportSha256})
 // The frozen numeric core has no command_kind column. Do not invent one:
 // command kind is proven by the six client terminal markers; server numeric
 // slots/receipts/wire bytes are independently bound to those exact frames.
 requireContract(server.rows.every((row,index)=>(!Object.hasOwn(row,'command_kind')||row.command_kind===3)
  &&row.wire_bytes===report.actionBoundsClient.client.rows[index].wire_bytes),'server_terminal_rows_mismatch')
 validateRemovalReceipt(removal,{count:6,priorPid:report.artifactBinding.configTestPid})
 validateSessionCompletion(sessionCompletion)
 validateHealthPair(health)
 requireContract(health.test.maintenance===false&&health.formal.maintenance===false,
  'final_maintenance_not_open')
 // Measurement classification cannot certify that the external execution
 // chain is implemented, authorized, or independently verified.
 return {status:'passed',measurementAccepted:true,originalSloMs:3000,terminalFrames:60,serverRows:6,accounts:69,sessions:70,
  remoteApplyReady:false,completeOriginalWorkloadToolReady:false}
}



// Local file classifier. It opens no network connections and handles no credentials.
export function readFinalContext(contextPath) {
 const context=readJsonFile(contextPath,65536)
 const expected=['schema','report','server','install','removal','health','sessionCompletion','ledgers','lifecycle']
 requireContract(Object.keys(context).sort().join('|')===expected.sort().join('|')&&context.schema===1,
  'final_context_shape_invalid')
 const values={}
 for(const name of expected.filter(name=>name!=='schema')) {
  requireContract(typeof context[name]==='string'&&path.isAbsolute(context[name]),'final_context_absolute_paths_required')
  values[name]=readJsonFile(context[name],name==='report'?8388608:262144)
 }
 const clientReportSha256=sha256File(context.report),configReceiptSha256=sha256File(context.install)
 requireContract(String(values.report.artifactBinding.configReceiptSha256).toUpperCase()===configReceiptSha256,
  'final_real_install_hash_mismatch')
 requireContract(values.lifecycle.bindings?.clientSha256===clientReportSha256
  &&values.lifecycle.bindings.serverSha256===sha256File(context.server)
  &&values.lifecycle.bindings.installSha256===configReceiptSha256
  &&values.lifecycle.bindings.removalSha256===sha256File(context.removal)
  &&values.lifecycle.bindings.sessionCompletionSha256===sha256File(context.sessionCompletion)
  &&values.lifecycle.bindings.healthSha256===sha256File(context.health)
  &&values.lifecycle.bindings.ledgersSha256===sha256File(context.ledgers),'final_real_file_hashes_mismatch')
 return {...values,clientReportSha256,configReceiptSha256}
}
async function contractMain(argv) {
 requireContract(argv.length%2===0,'classifier_arguments_invalid')
 const args=Object.fromEntries(Array.from({length:argv.length/2},(_,index)=>[argv[index*2],argv[index*2+1]]))
 requireContract(Object.keys(args).every(key=>['--mode','--report','--context','--binding','--binding-sha',
  '--synthetic-validation-only'].includes(key)),'classifier_arguments_unknown')
 const selected=args['--context']??args['--report']
 requireContract(typeof selected==='string'&&path.isAbsolute(selected),'classifier_selected_input_required')
 execFileSync('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
  ['-NoProfile','-Command',". '"+fileURLToPath(new URL('../../windows/L12GeneratedStorage.ps1',import.meta.url)).replaceAll("'","''")+"'; $null=Assert-L12GeneratedPath -Path '"+path.dirname(selected).replaceAll("'","''")+"' -Purpose 'r7 local file classifier'"],
  {windowsHide:true,stdio:'pipe',timeout:15000})
 loadCandidateBinding(args['--binding'],args['--binding-sha'],
  {allowSynthetic:args['--synthetic-validation-only']==='true'})
 let result
 if(args['--mode']==='collection-safety')result={status:'COLLECTION_INPUT_VALIDATED',
  ...validateFullCollectionSafetyInput(readJsonFile(args['--report'],8388608)),finalAccepted:false}
 else if(args['--mode']==='final')result={...validateFullWorkloadFinal(readFinalContext(args['--context'])),finalAccepted:true}
 else throw Error('CLASSIFIER_MODE_REQUIRED')
 process.stdout.write(JSON.stringify(result)+'\n')
}
if(process.argv[1]&&path.resolve(process.argv[1]).toLowerCase()===fileURLToPath(import.meta.url).toLowerCase())
 contractMain(process.argv.slice(2)).catch(error=>{
  process.stderr.write(JSON.stringify({status:'rejected',code:error.code??'classifier_failed'})+'\n');process.exitCode=1
 })
