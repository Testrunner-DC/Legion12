import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {pathToFileURL} from 'node:url'
import {REPO,assertGeneratedDirectories,validateBinding,digest,FROZEN_SHA,PROTOCOL_CLIENT_SOURCES} from './testrun-pressure-protocol.mjs'
import {observeIdentity,assertRuntimeIdentity,acquireTestSession,testRequest,installGuardFiles,startNormalGuard,bindOwnedControl,remoteJson,durableJson,pause,publicFailure} from './testrun-pressure-runtime.mjs'
import {openOwnedControl} from './testrun-pressure-control.mjs'
import {protectOwnedSession,planQualificationSessionRecovery,removeExitedGuardFiles} from './qualify-normal-testrun-lifecycle.mjs'
import {createNormalSessionManager,readColdLedgersPrivately,recoverOwnedLedgerAccounts,validateRecoveryControl} from './testrun-pressure-recovery.mjs'
import {createExecutableCore,createOwnedLedgerSinks,readPeripheralPreflight,readPressureDisk,assertPressureDisk,assertCapturedMeasurement,FROZEN_GUARD} from './testrun-pressure-execution.mjs'
import {inspectNormalPressureEvidence} from './testrun-pressure-finalizer.mjs'
import {FULL_TRACE_PROFILE,createConfirmationObserver,composeConfirmationMarkers,observeFailedSetupTail,readFullOwnedJournal} from './testrun-confirmation-trace.mjs'

const SOURCE_FILES=['run-testrun-representative-pressure.mjs','testrun-confirmation-trace.mjs','test-testrun-confirmation-trace.mjs','testrun-pressure-control.mjs','testrun-pressure-protocol.mjs','testrun-pressure-runtime.mjs','testrun-pressure-recovery.mjs','testrun-pressure-execution.mjs','testrun-pressure-finalizer.mjs','qualify-normal-testrun-lifecycle.mjs','property-frame-source-module.mjs','ws-burst-observer.mjs']
const sources=()=>[...SOURCE_FILES.map(file=>({file,sha256:digest(fs.readFileSync(path.join(import.meta.dirname,file)))})),
 ...PROTOCOL_CLIENT_SOURCES.map(([, , file])=>({file,sha256:digest(fs.readFileSync(path.join(REPO,file)))}))]
// Optional full Journal diagnosis consumes its own reserve. It is not an
// additional normal capacity gate or a reason to shorten the original workload.
export function normalObservationPolicy(confirmationDiagnostics=false){
 assert.equal(typeof confirmationDiagnostics,'boolean','NORMAL_DIAGNOSTICS_MODE_MUST_BE_BOOLEAN')
 return {confirmationTrace:confirmationDiagnostics,pressureStopMs:confirmationDiagnostics?680000:720000,
  businessReserveMs:confirmationDiagnostics?220000:180000,recoveryReserveMs:180000}
}
export function assertRunConfiguration(configuration){
 assert.equal(typeof configuration,'object');assert.match(configuration.bindingSha256,/^[A-F0-9]{64}$/)
 for(const key of ['bindingPath','output','cache'])assert.ok(path.isAbsolute(configuration[key]),'NORMAL_RUN_ABSOLUTE_INPUT')
 assertGeneratedDirectories(configuration.output,configuration.cache)
 const bytes=fs.readFileSync(configuration.bindingPath);assert.ok(bytes.length<65536);assert.equal(digest(bytes),configuration.bindingSha256)
 return validateBinding(JSON.parse(bytes))
}
export async function executeNormalPressure(configuration){
 const observationPolicy=normalObservationPolicy(configuration.confirmationDiagnostics)
 const binding=assertRunConfiguration(configuration),{output,cache,bindingSha256}=configuration
 for(const root of [output,cache]){if(!fs.existsSync(root))fs.mkdirSync(root,{recursive:true});assert.deepEqual(fs.readdirSync(root),[],'NORMAL_RUN_SELECTED_DIRECTORY_NOT_EMPTY')}
 process.env.TEMP=cache;process.env.TMP=cache;process.env.TMPDIR=cache
 const runId=crypto.randomUUID().replaceAll('-',''),startedUtcMs=Date.now(),deadlineMs=startedUtcMs+900000
 const sourceAtAdmission=sources(),stopFile=path.join(cache,'stop.json'),measurementPath=path.join(output,'measurement-before-account-cleanup.json')
 const ledgerRoot=path.join(cache,runId);fs.mkdirSync(ledgerRoot)
 const coreBuild=createExecutableCore(pathToFileURL(path.join(import.meta.dirname,'testrun-pressure-protocol.mjs')).href,{confirmationTrace:observationPolicy.confirmationTrace})
 const coreFile=path.join(cache,'normal-original-workload.mjs');fs.writeFileSync(coreFile,coreBuild.source,{flag:'wx'})
 durableJson(path.join(output,'execution-adaptation.json'),coreBuild.evidence)
 durableJson(path.join(output,'sources-before.json'),{schema:1,sources:sourceAtAdmission,bindingSha256})
 let transport,unbind,before,after,installed,guard,guardProof,removed,manager,original,recovery,preflight,coreReport,controlPath,control,controlSha256,sinks,sinkProof,coldProof,recoveryProof,revocation,capture,transportExit,finalDisk
 const sessionAttempts=[],protectedSessions=[],immediatelyRetired=new Set(),failures=[]
 const mark=name=>console.log(JSON.stringify({stage:name,elapsedMs:Date.now()-startedUtcMs}))
 const stop=name=>{if(!fs.existsSync(stopFile))durableJson(stopFile,{schema:1,reason:name,utc:new Date().toISOString()})}
 const stopTimer=setTimeout(()=>stop('NORMAL_OWNED_PRESSURE_DEADLINE'),observationPolicy.pressureStopMs)
 const traceFaults=[],traceBudget={remainingMs:observationPolicy.confirmationTrace?FULL_TRACE_PROFILE.captureMs:0}
 let observer,traceCapture=null,failedSetupTail=null
 const traceFault=code=>{if(!traceFaults.includes(code))traceFaults.push(code)}
 let businessStarted=false,cleanupFinishedUtcMs=null
 try{
  transport=openOwnedControl({binding});await transport.waitReady();unbind=bindOwnedControl(transport)
  before=assertRuntimeIdentity(await observeIdentity(binding),binding)
  durableJson(path.join(output,'identity-before.json'),before);mark('actual-pinned-control-and-dual-identity')
  installed=await installGuardFiles(runId,path.join(REPO,'ops/qa/testrun-normal-resource-guard.py'),FROZEN_GUARD)
  durableJson(path.join(output,'owned-guard-install.json'),installed)
  guard=startNormalGuard(binding,installed,{stopFile,logPath:path.join(output,'guard.jsonl')});await guard.waitReady();guard.assertFresh()
  manager=createNormalSessionManager({binding,request:testRequest,deadlineMs,
   observeIdentity:async()=>{const value=assertRuntimeIdentity(await observeIdentity(binding),binding);return {baseUrl:binding.baseUrl,testPid:value.test.pid,testCommit:value.test.commit,testDllSha256:value.test.dllSha256,diagnosticsOff:true,matchStorage:'ephemeral'}},
   acquireSession:async({kind})=>{
    guard.assertFresh();const intent={kind,phase:'acquire-requested'};sessionAttempts.push(intent)
    durableJson(path.join(cache,kind+'-acquire-intent.json'),{schema:1,runId,...intent})
    const acquired=await acquireTestSession(binding)
    try{protectedSessions.push(await protectOwnedSession({file:path.join(cache,kind+'-session.dpapi'),runId,kind,token:acquired.token}));return acquired}
    catch(error){
     const retired=await testRequest(binding.baseUrl,'api/auth/sessions/current',{method:'DELETE',token:acquired.token})
     const verified=await testRequest(binding.baseUrl,'api/auth/me',{token:acquired.token})
     if([200,204].includes(retired.status)&&verified.status===401)immediatelyRetired.add(kind)
     throw error
    }
   }})
  original=await manager.prepareOriginal();recovery=await manager.prepareRecovery(original)
  const peripheral=await readPeripheralPreflight(manager,original),disk=assertPressureDisk(await readPressureDisk(installed.root))
  preflight={schema:1,baseUrl:binding.baseUrl,testPid:binding.test.pid,formalPid:binding.formal.pid,diagnosticsOff:true,matchStorage:'ephemeral',...peripheral,resourcePolicy:before.resourcePolicy,disk,sharedWindowExclusive:true}
  durableJson(path.join(output,'preflight.json'),preflight);mark('actual-F2-data-disk-and-independent-recovery-admitted')
  const now=new Date().toISOString()
  control={schema:1,kind:'normal-testrun-account-ledgers-v1',runId,baseUrl:binding.baseUrl,
   test:Object.fromEntries(['pid','commit','dllSha256'].map(key=>[key,binding.test[key]])),formal:Object.fromEntries(['pid','commit','dllSha256'].map(key=>[key,binding.formal[key]])),bindingSha256,
   registrationWindow:{openedUtc:now,notAfterUtc:new Date(deadlineMs).toISOString()},
   groups:[60,9].map((maxSlots,index)=>({index,runId:`normal-${runId}-${index}`,maxSlots,ledgerPath:path.join(ledgerRoot,['players60.dpapi','outer9.dpapi'][index])}))}
  validateRecoveryControl(control,binding,bindingSha256);controlPath=path.join(output,'ledger-control.json');durableJson(controlPath,control);controlSha256=digest(fs.readFileSync(controlPath))
  sinks=await createOwnedLedgerSinks(control,{cache:ledgerRoot,output})
  const core=await import(pathToFileURL(coreFile).href);await core.initializeNormalTestrunProtocol(binding)
  if(observationPolicy.confirmationTrace)observer=createConfirmationObserver({profile:FULL_TRACE_PROFILE.name,confirmationForRequest:core.payloadFloorTestHooks.confirmationForRequest,onFault:traceFault})
  const captureTrace=async label=>{
   assert.equal(observationPolicy.confirmationTrace,true,'NORMAL_RUN_MUST_NOT_CAPTURE_EXTRA_JOURNAL')
   try{
    const journal=await readFullOwnedJournal({observer,binding,deadlineMs,remote:remoteJson,assertFresh:()=>guard.assertFresh(),budget:traceBudget})
    const client=observer.report(),clientPath=path.join(output,label+'-client.json'),journalPath=path.join(output,label+'-journal.json')
    durableJson(clientPath,client);durableJson(journalPath,journal)
    const byOrdinal=new Map(journal.rows.map(row=>[row.ordinal,row]))
    const consistent=client.commands.every(command=>command.originalOutcome!=='accepted'||(byOrdinal.get(command.ordinal)?.present===true
     &&byOrdinal.get(command.ordinal).accepted===1&&byOrdinal.get(command.ordinal).revision===command.ackRevision))
    if(!consistent)traceFault('FULL_TRACE_JOURNAL_ACK_MISMATCH')
    const result={schema:1,capturedBeforeAnonymization:true,profile:FULL_TRACE_PROFILE.name,commands:client.commands.length,journalRows:journal.rows.length,
     clientSha256:digest(fs.readFileSync(clientPath)),journalSha256:digest(fs.readFileSync(journalPath)),originalOutcomesNeverUpgraded:true,acceptedAckJournalConsistent:consistent}
    durableJson(path.join(output,label+'-capture.json'),result);return result
   }catch{traceFault('FULL_TRACE_CAPTURE_INCOMPLETE');return null}
  }
  // Observation faults fail acceptance but must never prevent original
  // heartbeat/terminal/room exit, or independent account/session recovery.
  const observeSafely=method=>(...args)=>{try{return observer[method](...args)}catch{traceFault('FULL_TRACE_OBSERVER_INCOMPLETE')}}
  const confirmationTrace=observationPolicy.confirmationTrace?{
   compose(originalMarkers,metrics){observer.setStageReader(()=>metrics.currentStage);return composeConfirmationMarkers(originalMarkers,observer)},
   bindRoom:observeSafely('bindRoom'),sealRequest:observeSafely('sealRequest'),sealTerminal:observeSafely('sealTerminal'),
   async beforeTerminal({failed,workersFrozen,originalFailure,originalStopReason,stage}){
    try{
     failedSetupTail=await observeFailedSetupTail({observer,failed,workersFrozen,deadlineMs,assertFresh:()=>guard.assertFresh()})
     failedSetupTail={...failedSetupTail,originalFailure,originalStopReason,stage}
     if(failed)await captureTrace('failed-setup-before-terminal')
    }catch{traceFault('FULL_TRACE_SETUP_CAPTURE_INCOMPLETE')}
    finally{observer.setPhase('cleanup')}
   },
  }:undefined
  const artifactBinding={candidateBindingSha256:bindingSha256,manifestSha256:binding.test.manifestSha256,releaseSha256:binding.test.releaseSha256,testPid:binding.test.pid,formalPid:binding.formal.pid,deployedNetSha256:binding.client.netSha256,cardFrameSha256:binding.client.cardFrameSha256,handArraySha256:binding.client.handArraySha256,propertyFrameSha256:binding.client.propertyFrameSha256,propertyNamesSha256:binding.client.propertyNamesSha256}
  await manager.withPrivateSession(original,async privateSession=>{
   coreReport=await core.workloadTestHooks.runPressure({local:false,baseUrl:binding.baseUrl,expectedCommit:binding.test.commit,expectedCommitSource:'exact-normal-binding-and-actual-running-process',
    credentials:{players:null,admin:{token:privateSession.token},tournamentReader:null},provisionPlayers:true,diagnosticNonce:crypto.randomBytes(16).toString('hex'),artifactBinding,
    runId,stopFile,stageSeconds:60,httpIntervalMs:2000,wsIntervalMs:2000,requestTimeoutMs:10000,measurementCheckpointPath:measurementPath,executionDeadlineMs:deadlineMs,confirmationTrace,
    ledgerClients:sinks.clients,beforeLedgerGroup:async index=>{guard.assertFresh();await sinks.start(index)},
    assertNormalExecution:async()=>{guard.assertFresh();assert.equal(manager.publicStatus().sessions.filter(item=>item.prepared&&!item.revoked).length,2);assert.ok(deadlineMs-Date.now()>600000);assert.deepEqual(sources(),sourceAtAdmission);businessStarted=true;mark('original-30-plus-10-workload-starting')},
    onBeforeAccountTeardown:async()=>{
     try{capture=assertCapturedMeasurement(measurementPath,{candidate:binding.test.commit,coreSha256:coreBuild.evidence.adaptedSha256});durableJson(path.join(output,'capture-before-account-teardown.json'),capture)}catch{failures.push({phase:'measurement-capture',code:'MEASUREMENT_CAPTURE_INCOMPLETE'})}
     if(observationPolicy.confirmationTrace)traceCapture=await captureTrace('terminal-before-account-teardown')
    },
    progressFn:row=>{if(Number.isInteger(row?.stage))console.log(JSON.stringify({stage:row.stage,minimumActiveRooms:row.minimumActiveRooms,maximumActiveRooms:row.maximumActiveRooms,outsideUsers:row.outsideUsers}))},
   })
  })
  durableJson(path.join(output,'pressure-report.json'),coreReport);mark('pressure-observation-captured')
 }catch(error){failures.push({phase:businessStarted?'business':'admission',code:publicFailure(error)});stop('NORMAL_RUN_FAILED')}
 finally{
  clearTimeout(stopTimer)
  if(sinks)try{sinkProof=await sinks.finish();durableJson(path.join(output,'ledger-sinks-final.json'),{schema:1,exits:sinkProof})}catch(error){failures.push({phase:'ledger-writer-close',code:publicFailure(error)})}
  if(controlPath&&recovery)try{
   coldProof=await readColdLedgersPrivately({controlPath,controlSha256,bindingSha256,allowPartial:true,consume:async privateLedger=>{
    recoveryProof=await recoverOwnedLedgerAccounts({binding,control,privateLedger,sessionManager:manager,recoveryHandle:recovery,deadlineMs})
   }})
   durableJson(path.join(output,'cold-ledger-proof.json'),coldProof);durableJson(path.join(output,'independent-owned-account-recovery.json'),recoveryProof)
   if(!recoveryProof.cleanupComplete)failures.push({phase:'account-recovery',code:'OWNED_ACCOUNTS_PENDING'})
  }catch(error){failures.push({phase:'cold-recovery',code:publicFailure(error)})}
  if(manager)try{revocation=await manager.revokeAll();durableJson(path.join(output,'owned-session-revocation.json'),revocation);if(!revocation.complete)failures.push({phase:'session-revocation',code:'OWNED_SESSION_REVOCATION_PENDING'})}catch(error){failures.push({phase:'session-revocation',code:publicFailure(error)})}
  for(const attempt of sessionAttempts){if(!manager?.publicStatus().sessions.some(item=>item.kind===attempt.kind&&item.revoked)&&!immediatelyRetired.has(attempt.kind))failures.push({phase:'session-acquisition',code:'ACQUISITION_OUTCOME_UNPROVEN',kind:attempt.kind})}
  cleanupFinishedUtcMs=Date.now()
  try{after=assertRuntimeIdentity(await observeIdentity(binding),binding);finalDisk=assertPressureDisk(await readPressureDisk(installed?.root??'/tmp/l12-normal-pressure-'+runId));durableJson(path.join(output,'identity-after.json'),{...after,disk:finalDisk})}catch(error){failures.push({phase:'postflight',code:publicFailure(error)})}
  if(guard)try{
   const rows=guard.state().rows,until=Date.now()+5000;while(guard.state().rows<=rows&&Date.now()<until)await pause(25)
   guardProof=await guard.finish();durableJson(path.join(output,'guard-supervision.json'),guardProof)
   removed=await removeExitedGuardFiles(installed,guardProof);durableJson(path.join(output,'owned-guard-files-removed.json'),removed)
  }catch(error){failures.push({phase:'guard-close',code:publicFailure(error)})}
  unbind?.()
  if(transport)try{transportExit=await transport.close();durableJson(path.join(output,'owned-control-exit.json'),transportExit)}catch(error){failures.push({phase:'control-close',code:publicFailure(error)})}
 }
 const sourceAfter=sources();if(JSON.stringify(sourceAfter)!==JSON.stringify(sourceAtAdmission))failures.push({phase:'source-final',code:'NORMAL_TOOL_SOURCE_CHANGED'})
 if(observationPolicy.confirmationTrace&&(!traceCapture||!observer||observer.report().commands.some(command=>command.originalOutcome===null)))traceFault('FULL_TRACE_COMMAND_COVERAGE_INCOMPLETE')
 for(const code of traceFaults)failures.push({phase:'full-confirmation-trace',code})
 const postflight={}
 if(after&&before)for(const name of ['test','formal'])postflight[name]={...after[name],restartDelta:after[name].restartCount-before[name].restartCount}
 const lifecycle={businessStartedUtcMs:Date.parse(coreReport?.startedAt),cleanupFinishedUtcMs,businessSucceeded:coreReport?.status==='completed',captureSucceeded:Boolean(capture),captureBeforeTeardown:capture?.capturedBeforeTeardown===true,
  teardownSucceeded:recoveryProof?.cleanupComplete===true&&revocation?.complete===true,independentRecoverySessionVerified:Boolean(recovery),allOwnedHelpersExited:transportExit?.ownControlExited===true&&guardProof?.completion?.allOwnedChildrenReaped===true&&sinkProof?.length===2&&sinkProof.every(item=>item.code===0),originalSessionRevoked:manager?.publicStatus().sessions.some(item=>item.kind==='original'&&item.revoked)===true,recoverySessionRevoked:manager?.publicStatus().sessions.some(item=>item.kind==='recovery'&&item.revoked)===true,formalWrites:0,serviceRestarts:0,maintenanceWrites:0}
 const evidence={schema:1,bindingSha256,frozenCoreSha256:FROZEN_SHA,preflight,report:coreReport,guard:guardProof,
  ledgers:coldProof?.groups.map(item=>({verified:true,coldReadVerified:item.wrongRunRejected===true,count:item.count,seq:item.sequence,cipherSha256:item.cipherSha256})),lifecycle,postflight}
 let validated=false
 if(!failures.length)try{assert.equal(coldProof.complete,true);assert.ok(Date.now()<=deadlineMs);inspectNormalPressureEvidence(evidence,binding);validated=true}catch(error){failures.push({phase:'final-original-contract',code:publicFailure(error)})}
 const proof={schema:1,status:validated?'REPRESENTATIVE_NORMAL_30_PLUS_10_ACCEPTED':'NOT_ACCEPTED',onlineCapacityAccepted:validated,runId,bindingSha256,coreAdaptation:coreBuild.evidence,failures,
  actualFrozenWorkloadInvoked:businessStarted,durationMs:Date.now()-startedUtcMs,lifecycle,sourceAtAdmission,sourceAfter,sessionAttempts,protectedSessions,evidence,
  confirmationTrace:{enabled:observationPolicy.confirmationTrace,profile:observationPolicy.confirmationTrace?FULL_TRACE_PROFILE.name:null,capture:traceCapture,failedSetupTail,faults:traceFaults,captureBudgetRemainingMs:traceBudget.remainingMs,businessReserveMs:observationPolicy.businessReserveMs,recoveryReserveMs:observationPolicy.recoveryReserveMs,
   boundary:observationPolicy.confirmationTrace?'Optional client callback/dispatch and exact committed Journal facts only; server lock wait and cross-host latency are not established. Original timeout/cancelled outcomes are never upgraded.':'No extra Journal diagnosis requested. Original numeric checkpoint, action markers, sixty terminal recipients and full normal acceptance remain mandatory.'},
  formalWrites:0,serviceRestarts:0,maintenanceWrites:0,actualBrowserFpsMeasured:false,operatingSystemNotificationsVerified:false,
  boundary:'Normal Linux isolated test-service HTTP/WebSocket and original workload only. This does not certify browser animation FPS, all card effects, operating-system notifications or production capacity.'}
 durableJson(path.join(output,'normal-pressure-acceptance.json'),proof)
 console.log(JSON.stringify({status:proof.status,output,failures,onlineCapacityAccepted:validated}));return proof
}
if(process.argv[1]&&pathToFileURL(path.resolve(process.argv[1])).href===import.meta.url){
 const args=process.argv.slice(2);assert.equal(args.length,9);assert.equal(args[0],'--execute');assert.equal(args[1],'--binding');assert.equal(args[3],'--binding-sha');assert.equal(args[5],'--output');assert.equal(args[7],'--cache')
 const proof=await executeNormalPressure({bindingPath:args[2],bindingSha256:args[4],output:args[6],cache:args[8]});if(!proof.onlineCapacityAccepted)process.exitCode=2
}
