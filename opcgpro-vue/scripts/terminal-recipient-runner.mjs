import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {pathToFileURL} from 'node:url'
import {digest} from './testrun-pressure-protocol.mjs'

// A separately named diagnostic entry, derived reversibly from the frozen
// original workload lifecycle. Normal admission and normal capacity remain OFF.
export function createTerminalRecipientRunner() {
 const file=path.join(import.meta.dirname,'run-testrun-representative-pressure.mjs')
 const original=fs.readFileSync(file,'utf8'), changes=[]
 const replace=(before,after,count=1)=>{assert.equal(source.split(before).length,count+1,'DIAGNOSTIC_RUNNER_ANCHOR_CHANGED');source=source.replaceAll(before,after);changes.push([before,after])}
 let source=original
 replace("import {observeIdentity,assertRuntimeIdentity,acquireTestSession,testRequest,installGuardFiles,startNormalGuard,bindOwnedControl,remoteJson,durableJson,pause,publicFailure} from './testrun-pressure-runtime.mjs'",
  "import {observeTerminalIdentity as observeIdentity,assertTerminalIdentity as assertRuntimeIdentity,acquireTerminalSession as acquireTestSession,testRequest,installTerminalGuardFiles as installGuardFiles,startTerminalResourceGuard as startNormalGuard,bindOwnedControl,remoteJson,durableJson,pause,publicFailure,configureTerminalRuntime,terminalSetting,terminalJournalRemote,readTerminalServerRows,removeTerminalGuardFiles as removeExitedGuardFiles} from './terminal-recipient-runtime.mjs'")
 replace("import {openOwnedControl} from './testrun-pressure-control.mjs'", "import {openTerminalControl as openOwnedControl} from './terminal-recipient-runtime.mjs'\nimport {createTerminalRecipientTransportAdapter} from './terminal-recipient-transport-adapter.mjs'\nimport {assertTerminalServerTrace} from './terminal-recipient-trace-contract.mjs'")
 replace('protectOwnedSession,planQualificationSessionRecovery,removeExitedGuardFiles','protectOwnedSession,planQualificationSessionRecovery')
 replace('createNormalSessionManager,readColdLedgersPrivately','createTerminalRecipientSessionManager as createNormalSessionManager,readColdLedgersPrivately')
 replace('const sources=()=>[...SOURCE_FILES.map',"SOURCE_FILES.push('terminal-recipient-runner.mjs','terminal-recipient-runtime.mjs','terminal-recipient-transport-adapter.mjs','terminal-recipient-markers.mjs','terminal-recipient-trace-contract.mjs')\nconst sources=()=>[...SOURCE_FILES.map")
 replace('export async function executeNormalPressure(configuration){','export async function executeNormalPressure(configuration){\n configureTerminalRuntime(configuration.diagnosticSetting);assert.match(configuration.runId,/^[a-f0-9]{32}$/)')
 replace('const observationPolicy=normalObservationPolicy(configuration.confirmationDiagnostics)','const observationPolicy=normalObservationPolicy(true)')
 replace("const runId=crypto.randomUUID().replaceAll('-',''),startedUtcMs=Date.now(),deadlineMs=startedUtcMs+900000",
  'const runId=configuration.runId,startedUtcMs=Date.now(),workloadDeadlineMs=startedUtcMs+900000,deadlineMs=startedUtcMs+1260000')
 replace("let observer,traceCapture=null,failedSetupTail=null",'let observer,recipientAdapter,serverTrace=null,traceCapture=null,failedSetupTail=null')
 replace("ops/qa/testrun-normal-resource-guard.py","ops/qa/testrun-terminal-resource-guard.py")
 replace('manager=createNormalSessionManager({binding,request:testRequest,deadlineMs,','manager=createNormalSessionManager({binding,request:testRequest,deadlineMs,diagnosticSetting:terminalSetting(),')
 replace("diagnosticsOff:true,matchStorage:'ephemeral'}},","diagnosticsOff:false,diagnosticSetting:terminalSetting(),matchStorage:'ephemeral'}},")
 replace("diagnosticsOff:true,matchStorage:'ephemeral',...peripheral","diagnosticsOff:false,diagnosticOnly:true,onlineCapacityAccepted:false,matchStorage:'ephemeral',...peripheral")
 replace('notAfterUtc:new Date(deadlineMs).toISOString()','notAfterUtc:new Date(workloadDeadlineMs).toISOString()')
 replace('deadlineMs,remote:remoteJson,assertFresh:','deadlineMs,remote:terminalJournalRemote,assertFresh:')
 replace('return composeConfirmationMarkers(originalMarkers,observer)',"recipientAdapter=createTerminalRecipientTransportAdapter({nonce:terminalSetting().split(':')[2],originalMarkers:composeConfirmationMarkers(originalMarkers,observer),onFault:traceFault});return recipientAdapter.markers")
 replace("bindRoom:observeSafely('bindRoom'),sealRequest:","bindRoom(...args){observeSafely('bindRoom')(...args);recipientAdapter.bindRoom(args[0])},sealRequest:")
 replace("diagnosticNonce:crypto.randomBytes(16).toString('hex')","diagnosticNonce:terminalSetting().split(':')[2]")
 replace('executionDeadlineMs:deadlineMs,confirmationTrace','executionDeadlineMs:workloadDeadlineMs,confirmationTrace')
 replace("durableJson(path.join(output,'pressure-report.json'),coreReport);mark('pressure-observation-captured')",
  "durableJson(path.join(output,'pressure-report.json'),coreReport);mark('pressure-observation-captured')\n  serverTrace=await readTerminalServerRows(binding);durableJson(path.join(output,'server-recipients.json'),serverTrace)")
 const closeBlock=""+original.slice(original.indexOf('  if(guard)try{'),original.indexOf('  unbind?.()'))
 replace(closeBlock,'  // Diagnostic guard was retired before independent low-pressure cleanup.\n')
 replace('  if(controlPath&&recovery)try{',closeBlock+"  if(controlPath&&recovery)try{")
 replace(" const sourceAfter=sources();", " const recipientReport=recipientAdapter?.diagnosticReport();if(recipientReport)durableJson(path.join(output,'client-recipients.json'),recipientReport)\n const retired=recipientAdapter?.retire();if(retired)durableJson(path.join(output,'recipient-adapter-retired.json'),retired)\n const sourceAfter=sources();")
 replace("if(!failures.length)try{assert.equal(coldProof.complete,true);assert.ok(Date.now()<=deadlineMs);inspectNormalPressureEvidence(evidence,binding);validated=true}catch(error){failures.push({phase:'final-original-contract',code:publicFailure(error)})}",
  "try{\n  assert.equal(recipientReport?.trace_complete,1);assertTerminalServerTrace(serverTrace)\n  assert.equal(coldProof?.complete,true);assert.equal(recoveryProof?.cleanupComplete,true);assert.equal(revocation?.complete,true);assert.equal(transportExit?.ownControlExited,true)\n  validated=true\n }catch(error){failures.push({phase:'diagnostic-trace-completeness',code:publicFailure(error)})}")
 replace("status:validated?'REPRESENTATIVE_NORMAL_30_PLUS_10_ACCEPTED':'NOT_ACCEPTED',onlineCapacityAccepted:validated,", "status:validated?'TERMINAL_RECIPIENT_TRACE_CAPTURED':'DIAGNOSTIC_INCOMPLETE',diagnosticOnly:true,onlineCapacityAccepted:false,traceCaptured:validated,")
 replace("normal-pressure-acceptance.json","terminal-recipients-diagnostic.json")
 replace("onlineCapacityAccepted:validated}));return proof","onlineCapacityAccepted:false,traceCaptured:validated}));return proof")
 // Generated output lives outside the checkout; imports and source pins still
 // refer to the exact authoritative leaves rather than copied application code.
 const imports=[...source.matchAll(/from '(\.\/[^']+)'/g)].map(match=>match[1])
 for(const relative of new Set(imports))replace("from '"+relative+"'","from '"+pathToFileURL(path.resolve(import.meta.dirname,relative)).href+"'",imports.filter(item=>item===relative).length)
 replace('path.join(import.meta.dirname,file)', 'path.join('+JSON.stringify(import.meta.dirname)+',file)')
 replace("path.join(import.meta.dirname,'testrun-pressure-protocol.mjs')","path.join("+JSON.stringify(import.meta.dirname)+",'testrun-pressure-protocol.mjs')")
 // The caller invokes the export, never the original normal CLI/finalizer.
 replace('if(process.argv[1]&&pathToFileURL(path.resolve(process.argv[1])).href===import.meta.url){','if(false){')
 let reverse=source
 for(const [before,after] of changes.toReversed())reverse=reverse.replaceAll(after,before)
 assert.equal(reverse,original,'DIAGNOSTIC_DERIVATION_NOT_REVERSIBLE')
 return {source,evidence:{schema:1,originalSourceSha256:digest(original),diagnosticSourceSha256:digest(source),exactChanges:changes.map(([before,after])=>({beforeSha256:digest(before),afterSha256:digest(after)})),reverseExact:true,originalWorkloadUnchanged:true,normalDeadlineMs:900000,diagnosticCleanupDeadlineMs:1260000,onlineCapacityAccepted:false}}
}
