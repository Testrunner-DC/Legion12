import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {pathToFileURL} from 'node:url'
import {REPO,EVIDENCE_ROOT,CACHE_ROOT,assertGeneratedDirectories,validateBinding,digest} from './testrun-pressure-protocol.mjs'
import {observeIdentity,assertRuntimeIdentity,acquireTestSession,testRequest,installGuardFiles,startNormalGuard,bindOwnedControl,remoteJson,durableJson,pause,publicFailure} from './testrun-pressure-runtime.mjs'
import {openOwnedControl} from './testrun-pressure-control.mjs'
import {protectOwnedSession,planQualificationSessionRecovery,removeExitedGuardFiles} from './qualify-normal-testrun-lifecycle.mjs'
import {createNormalSessionManager,validateRecoveryControl,readColdLedgersPrivately,recoverOwnedLedgerAccounts} from './testrun-pressure-recovery.mjs'
import {createOwnedLedgerSinks,FROZEN_GUARD,readPressureDisk,assertPressureDisk} from './testrun-pressure-execution.mjs'
import {TRACE_LIMITS,createConfirmationTraceCore,createConfirmationObserver,runConfirmationWindow,snapshotDecodeCounters,OWNED_JOURNAL_CODE} from './testrun-confirmation-trace.mjs'

const SOURCE_FILES=['testrun-confirmation-trace.mjs','run-testrun-confirmation-trace.mjs','test-testrun-confirmation-trace.mjs',
 'testrun-pressure-protocol.mjs','testrun-pressure-runtime.mjs','testrun-pressure-recovery.mjs','testrun-pressure-execution.mjs','testrun-pressure-control.mjs','qualify-normal-testrun-lifecycle.mjs']
const sources=()=>SOURCE_FILES.map(file=>({file,sha256:digest(fs.readFileSync(path.join(import.meta.dirname,file)))}))
function inside(selected,root){const relative=path.relative(root,selected);assert.ok(relative&&!relative.startsWith('..')&&!path.isAbsolute(relative),'TRACE_OUTPUT_OUTSIDE_EXISTING_LEASE')}
export function assertTraceConfiguration(configuration){
 assert.deepEqual(Object.keys(configuration).sort(),['bindingPath','bindingSha256','cache','output'])
 for(const key of ['bindingPath','cache','output'])assert.ok(path.isAbsolute(configuration[key]),'TRACE_ABSOLUTE_INPUT_REQUIRED')
 assert.match(configuration.bindingSha256,/^[A-F0-9]{64}$/)
 inside(configuration.output,EVIDENCE_ROOT);inside(configuration.cache,CACHE_ROOT)
 assertGeneratedDirectories(configuration.output,configuration.cache)
 const bytes=fs.readFileSync(configuration.bindingPath);assert.ok(bytes.length<=65536);assert.equal(digest(bytes),configuration.bindingSha256)
 const binding=validateBinding(JSON.parse(bytes))
 for(const root of [configuration.output,configuration.cache]){
  if(fs.existsSync(root)){const info=fs.lstatSync(root);assert.ok(info.isDirectory()&&!info.isSymbolicLink());assert.deepEqual(fs.readdirSync(root),[],'TRACE_OUTPUT_NOT_EMPTY')}
 }
 return binding
}

// Existing fixed 60/9 *capacities* are retained for the unchanged cold reader.
// Only group 0 is started, and the registration call below is exactly 20.
export function createTraceLedgerControl({binding,bindingSha256,runId,cache,openedUtcMs,deadlineMs}){
 const control={schema:1,kind:'normal-testrun-account-ledgers-v1',runId,baseUrl:binding.baseUrl,
  test:Object.fromEntries(['pid','commit','dllSha256'].map(key=>[key,binding.test[key]])),
  formal:Object.fromEntries(['pid','commit','dllSha256'].map(key=>[key,binding.formal[key]])),bindingSha256,
  registrationWindow:{openedUtc:new Date(openedUtcMs).toISOString(),notAfterUtc:new Date(deadlineMs).toISOString()},
  groups:[60,9].map((maxSlots,index)=>({index,runId:`normal-${runId}-${index}`,maxSlots,ledgerPath:path.join(cache,runId,['players60.dpapi','outer9.dpapi'][index])}))}
 return validateRecoveryControl(control,binding,bindingSha256)
}

export async function registerTwentyOwned({hooks,adminToken,ledger,owned,privateValues,ledgerReceipt,request,wait,stopFile,assertFresh}){
 assert.equal(owned.length,0);let mutations=0
 const requestFn=async(base,relative,deadline,options={})=>{
  assertFresh()
  if((options.method??'GET')!=='GET'){
   assert.equal(relative,'api/auth/register','TRACE_REGISTRATION_ROUTE_REJECTED');assert.equal(options.method,'POST')
   assert.ok(mutations<TRACE_LIMITS.accounts,'TRACE_ACCOUNT_CAP');mutations++
  }
  return request(base,relative,deadline,options)
 }
 try{
  await hooks.registerAccounts(adminToken,TRACE_LIMITS.accounts,{},privateValues,owned,
   {ledger,ledgerReceipt,requestFn,delayFn:wait,stopFile,existsFn:fs.existsSync})
  assert.equal(owned.length,TRACE_LIMITS.accounts);assert.equal(mutations,TRACE_LIMITS.accounts)
  assert.equal(new Set(owned.map(account=>account.id)).size,TRACE_LIMITS.accounts)
 }finally{ledger.close()}
 return {plannedAccounts:20,attemptedRegistrations:mutations,confirmed:owned.filter(account=>account.registrationOutcome==='confirmed').length,
  outerAccountsCreated:0,group0Capacity:60,group1Started:false}
}

export async function executeConfirmationTrace(configuration){
 const binding=assertTraceConfiguration(configuration),{output,cache,bindingSha256}=configuration
 for(const root of [output,cache])if(!fs.existsSync(root))fs.mkdirSync(root,{recursive:true})
 process.env.TEMP=cache;process.env.TMP=cache;process.env.TMPDIR=cache
 const runId=crypto.randomUUID().replaceAll('-',''),started=Date.now(),deadlineMs=started+TRACE_LIMITS.wallMs
 const stopFile=path.join(cache,'stop.json'),owned=[],privateValues=new Set(),rooms=[],failures=[],sessionAttempts=[],protectedSessions=[],immediatelyRetired=new Set()
 const sourceBefore=sources(),generated=createConfirmationTraceCore(pathToFileURL(path.join(import.meta.dirname,'testrun-pressure-protocol.mjs')).href)
 const coreFile=path.join(cache,'owned-confirmation-core.mjs');fs.writeFileSync(coreFile,generated.source,{flag:'wx'})
 durableJson(path.join(output,'trace-plan.json'),{schema:1,runId,bindingSha256,limits:TRACE_LIMITS,sources:sourceBefore,core:generated.evidence,onlineCapacityAccepted:false})
 let transport,unbind,before,after,installed,guard,guardProof,removed,manager,original,recovery,control,controlPath,controlSha256,sinks,sinkProof,coldProof,recoveryProof,revocation,controlExit,observer,business,registration,journal,capture,core,metrics,decodeCountersBeforeTeardown
 const rootStop=new AbortController(),stop=source=>{observer?.setStopSource(source);if(!rootStop.signal.aborted)rootStop.abort(new Error('TRACE_STOP_REQUESTED'))}
 const reserveTimer=setTimeout(()=>stop('overall-deadline'),TRACE_LIMITS.wallMs-TRACE_LIMITS.recoveryReserveMs)
 const context={local:false,baseUrl:binding.baseUrl,expectedCommit:binding.test.commit,requestTimeoutMs:10000,
  signal:rootStop.signal,cleanupDeadline:deadlineMs,stopFile,measuredCommand:false}
 let sessionPlan=null
 try{
  transport=openOwnedControl({binding});await transport.waitReady();unbind=bindOwnedControl(transport)
  before=assertRuntimeIdentity(await observeIdentity(binding),binding);durableJson(path.join(output,'identity-before.json'),before)
  assertPressureDisk(await readPressureDisk('/tmp/l12-normal-pressure-'+runId))
  installed=await installGuardFiles(runId,path.join(REPO,'ops/qa/testrun-normal-resource-guard.py'),FROZEN_GUARD)
  durableJson(path.join(output,'owned-guard-install.json'),installed)
  guard=startNormalGuard(binding,installed,{stopFile,logPath:path.join(output,'guard.jsonl')});await guard.waitReady();guard.assertFresh()
  manager=createNormalSessionManager({binding,request:testRequest,deadlineMs,
   observeIdentity:async()=>{guard.assertFresh();const value=assertRuntimeIdentity(await observeIdentity(binding),binding);return {baseUrl:binding.baseUrl,testPid:value.test.pid,testCommit:value.test.commit,testDllSha256:value.test.dllSha256,diagnosticsOff:true,matchStorage:'ephemeral'}},
   acquireSession:async({kind})=>{
    guard.assertFresh();const intent={kind,phase:'acquire-requested'};sessionAttempts.push(intent)
    durableJson(path.join(cache,kind+'-acquire-intent.json'),{schema:1,runId,...intent})
    const acquired=await acquireTestSession(binding)
    try{protectedSessions.push(await protectOwnedSession({file:path.join(cache,kind+'-session.dpapi'),runId,kind,token:acquired.token}));return acquired}
    catch(error){const retired=await testRequest(binding.baseUrl,'api/auth/sessions/current',{method:'DELETE',token:acquired.token});const verified=await testRequest(binding.baseUrl,'api/auth/me',{token:acquired.token});if([200,204].includes(retired.status)&&verified.status===401)immediatelyRetired.add(kind);throw error}
   }})
  original=await manager.prepareOriginal();recovery=await manager.prepareRecovery(original)
  control=createTraceLedgerControl({binding,bindingSha256,runId,cache,openedUtcMs:Date.now(),deadlineMs})
  fs.mkdirSync(path.dirname(control.groups[0].ledgerPath));controlPath=path.join(output,'ledger-control.json');durableJson(controlPath,control);controlSha256=digest(fs.readFileSync(controlPath))
  sinks=await createOwnedLedgerSinks(control,{cache:path.dirname(control.groups[0].ledgerPath),output})
  core=await import(pathToFileURL(coreFile).href);await core.initializeNormalTestrunProtocol(binding)
  metrics=new core.workloadTestHooks.Metrics();metrics.currentHandArrayDelta={negotiatedSessions:0,packets:0,wireBytes:0,applied:0,rejected:0}
  metrics.requestStop=()=>stop('trace-fault')
  observer=createConfirmationObserver({confirmationForRequest:core.payloadFloorTestHooks.confirmationForRequest,onFault:()=>stop('trace-fault')});context.actionMarkers=observer.markers
  await sinks.start(0)
  try{
   await manager.withPrivateSession(original,async session=>{
    registration=await registerTwentyOwned({hooks:core.confirmationTraceHooks,adminToken:session.token,ledger:sinks.clients[0],owned,privateValues,ledgerReceipt:{},stopFile,
     wait:pause,assertFresh:()=>{guard.assertFresh();assert.ok(!rootStop.signal.aborted&&!fs.existsSync(stopFile),'TRACE_SETUP_STOPPED')},
     request:async(_base,relative,_deadline,options)=>{
      assert.ok(Date.now()+10000+TRACE_LIMITS.recoveryReserveMs<deadlineMs,'TRACE_SETUP_RECOVERY_RESERVE')
      const response=await testRequest(binding.baseUrl,relative,{...options,timeoutMs:10000})
      assert.ok((options.expected??[200]).includes(response.status),'TRACE_REGISTRATION_HTTP_STATUS')
      return options.retain===false?undefined:response.body
     }})
   })
  }finally{sinkProof=await sinks.finish();durableJson(path.join(output,'ledger-sinks-final.json'),{schema:1,exits:sinkProof,group1Started:false})}
  durableJson(path.join(output,'registration-counts.json'),registration)
  for(let slot=0;slot<TRACE_LIMITS.rooms;slot++){
   guard.assertFresh();assert.ok(!rootStop.signal.aborted&&!fs.existsSync(stopFile),'TRACE_SETUP_STOPPED')
   assert.ok(Date.now()+TRACE_LIMITS.recoveryReserveMs+TRACE_LIMITS.windowMs+TRACE_LIMITS.passiveTailMs+30000<deadlineMs,'TRACE_ROOM_SETUP_RECOVERY_RESERVE')
   const identities=owned.slice(slot*2,slot*2+2).map(account=>({token:account.token,accountId:account.id}))
   const room=await core.payloadFloorTestHooks.createRoomPair(context,...identities,metrics,rooms)
   const accountIds=[];accountIds[room.host.playerIndex]=identities[0].accountId;accountIds[room.guest.playerIndex]=identities[1].accountId
   observer.attachRoom(room,accountIds)
  }
  business=await runConfirmationWindow({rooms,hooks:{...core.confirmationTraceHooks,...core.payloadFloorTestHooks},observer,context,metrics,
   assertFresh:()=>guard.assertFresh(),existsStop:()=>fs.existsSync(stopFile)})
 }catch(error){failures.push({phase:rooms.length===10?'trace':'admission-or-setup',code:publicFailure(error)})}
 finally{
  clearTimeout(reserveTimer)
  if(sinks&&!sinkProof)try{sinkProof=await sinks.finish();durableJson(path.join(output,'ledger-sinks-final.json'),{schema:1,exits:sinkProof,group1Started:false})}catch(error){failures.push({phase:'ledger-close',code:publicFailure(error)})}
  // This capture always precedes both room cleanup and account anonymization.
  // A failure here does not suppress mandatory cleanup or pretend capture passed.
  if(observer){
   observer.setPhase('capture')
   try{
    const client=observer.report();decodeCountersBeforeTeardown=snapshotDecodeCounters(metrics,rooms)
    durableJson(path.join(output,'client-before-teardown.json'),{...client,decodeCountersBeforeTeardown})
    guard.assertFresh()
    journal=await observer.withPrivateRequests(async requests=>remoteJson(OWNED_JOURNAL_CODE,{...binding,requests},{timeoutMs:30000}))
    assert.equal(journal.capturedBeforeAnonymization,true);assert.equal(journal.readOnly,true);assert.equal(journal.rows.length,client.commands.filter(command=>command.nativeSendMs!==null).length)
    const requests=new Map(client.commands.map(command=>[command.ordinal,command.requestHash]))
    for(const row of journal.rows){assert.ok(requests.has(row.ordinal),'TRACE_JOURNAL_ORDINAL_UNBOUND');row.requestHash=requests.get(row.ordinal)}
    durableJson(path.join(output,'journal-before-teardown.json'),journal)
    capture={schema:1,capturedBeforeAnonymization:true,utcMs:Date.now(),clientSha256:digest(fs.readFileSync(path.join(output,'client-before-teardown.json'))),journalSha256:digest(fs.readFileSync(path.join(output,'journal-before-teardown.json'))),onlineCapacityAccepted:false}
    durableJson(path.join(output,'capture-before-account-teardown.json'),capture)
   }catch(error){failures.push({phase:'capture-before-anonymization',code:publicFailure(error)})}
   observer.setPhase('cleanup')
  }
  const roomCleanup={compliantSurrenders:0,roomsLeft:0,roomErrors:[],sessionsRevoked:0,sessionErrors:[]}
  if(core&&metrics){metrics.currentStage='cleanup';await Promise.allSettled(rooms.map((room,slot)=>core.workloadTestHooks.cleanupRoom(room,context,roomCleanup,slot)))}
  if(roomCleanup.roomErrors.length||roomCleanup.roomsLeft!==rooms.length)failures.push({phase:'room-cleanup',code:'TRACE_OWNED_ROOMS_PENDING'})
  try{durableJson(path.join(output,'owned-room-cleanup.json'),{schema:1,createdRooms:rooms.length,roomsLeft:roomCleanup.roomsLeft,compliantSurrenders:roomCleanup.compliantSurrenders,errorCount:roomCleanup.roomErrors.length})}catch(error){failures.push({phase:'room-cleanup-receipt',code:publicFailure(error)})}
  if(controlPath&&recovery)try{
   coldProof=await readColdLedgersPrivately({controlPath,controlSha256,bindingSha256,allowPartial:true,consume:async privateLedger=>{
    assert.ok(privateLedger.accounts.length<=20&&privateLedger.accounts.every(account=>account.ledgerIndex===0),'TRACE_LEDGER_SCOPE_EXCEEDED')
    // Durable intent may exist even when its ACK never reached this process.
    // Recover every validated cold row, not only the in-memory owned list.
    recoveryProof=await recoverOwnedLedgerAccounts({binding,control,privateLedger,sessionManager:manager,recoveryHandle:recovery,deadlineMs})
   }})
   durableJson(path.join(output,'cold-partial-ledger-proof.json'),coldProof);durableJson(path.join(output,'independent-owned-account-recovery.json'),recoveryProof)
   if(!recoveryProof.cleanupComplete)failures.push({phase:'owned-account-recovery',code:'TRACE_OWNED_ACCOUNTS_PENDING'})
  }catch(error){failures.push({phase:'cold-owned-recovery',code:publicFailure(error)})}
  if(manager)try{revocation=await manager.revokeAll();durableJson(path.join(output,'owned-session-revocation.json'),revocation);if(!revocation.complete)failures.push({phase:'session-revocation',code:'TRACE_SESSION_PENDING'})}catch(error){failures.push({phase:'session-revocation',code:publicFailure(error)})}
  try{
   const slots={}
   for(const kind of ['original','recovery']){const file=path.join(cache,kind+'-session.dpapi');slots[kind]={intent:fs.existsSync(path.join(cache,kind+'-acquire-intent.json')),cipherSha256:fs.existsSync(file)?digest(fs.readFileSync(file)):null}}
   sessionPlan=planQualificationSessionRecovery({protectedSessions,sessionAttempts,revocation},slots)
  }catch(error){failures.push({phase:'session-intent-readback',code:publicFailure(error)})}
  for(const attempt of sessionAttempts)if(!manager?.publicStatus().sessions.some(item=>item.kind===attempt.kind&&item.revoked)&&!immediatelyRetired.has(attempt.kind))failures.push({phase:'session-acquisition',code:'TRACE_ACQUISITION_OUTCOME_UNPROVEN',kind:attempt.kind})
  try{after=assertRuntimeIdentity(await observeIdentity(binding),binding);for(const name of ['test','formal'])assert.equal(after[name].restartCount,before[name].restartCount);durableJson(path.join(output,'identity-after.json'),after)}catch(error){failures.push({phase:'identity-after',code:publicFailure(error)})}
  if(guard)try{const rows=guard.state().rows,until=Date.now()+5000;while(guard.state().rows<=rows&&Date.now()<until)await pause(25);guardProof=await guard.finish();durableJson(path.join(output,'guard-supervision.json'),guardProof);removed=await removeExitedGuardFiles(installed,guardProof);durableJson(path.join(output,'owned-guard-files-removed.json'),removed)}catch(error){failures.push({phase:'guard-close',code:publicFailure(error)})}
  unbind?.();if(transport)try{controlExit=await transport.close();durableJson(path.join(output,'owned-control-exit.json'),controlExit)}catch(error){failures.push({phase:'control-close',code:publicFailure(error)})}
  for(const account of owned)for(const key of ['id','username','password','token','createdAt'])account[key]=''
  privateValues.clear()
 }
 const sourceAfter=sources();if(JSON.stringify(sourceAfter)!==JSON.stringify(sourceBefore))failures.push({phase:'source-final',code:'TRACE_SOURCE_CHANGED'})
 const client=observer?.report(),traceComplete=Boolean(business?.tailComplete&&capture&&client&&client.faults.length===0&&client.commands.length>0&&client.commands.every(command=>command.originalOutcome!==null))
 const lifecycleComplete=Boolean(recoveryProof?.cleanupComplete&&revocation?.complete&&sinkProof?.length===1&&sinkProof[0].code===0&&guardProof?.completion?.allOwnedChildrenReaped&&removed?.ownedFilesRemoved===2&&controlExit?.ownControlExited&&Date.now()<=deadlineMs)
 const proof={schema:1,status:!failures.length&&traceComplete&&lifecycleComplete?'BOUNDED_CONFIRMATION_TRACE_CAPTURED':'CONFIRMATION_TRACE_INCOMPLETE',runId,bindingSha256,
  onlineCapacityAccepted:false,originalRepresentativeWorkloadAccepted:false,originalRunPressureInvoked:false,
  durationMs:Date.now()-started,limits:TRACE_LIMITS,business,registration,traceComplete,lifecycleComplete,failures,sessionAttempts,protectedSessions,sessionPlan,
  capture,decodeCountersBeforeTeardown,sourceBefore,sourceAfter,formalWrites:0,serviceRestarts:0,maintenanceWrites:0,journalProbeDatabaseWrites:0,
  boundary:'One owned ten-room confirmation trace only. Original 3s failures remain failed; no 30+10 capacity, large-payload floor or compression benefit is certified.'}
 durableJson(path.join(output,'confirmation-trace-result.json'),proof)
 console.log(JSON.stringify({status:proof.status,onlineCapacityAccepted:false,output,failures}));return proof
}
if(process.argv[1]&&pathToFileURL(path.resolve(process.argv[1])).href===import.meta.url){
 const args=process.argv.slice(2)
 try{assert.equal(args.length,9);assert.equal(args[0],'--execute');assert.equal(args[1],'--binding');assert.equal(args[3],'--binding-sha');assert.equal(args[5],'--output');assert.equal(args[7],'--cache')
  const proof=await executeConfirmationTrace({bindingPath:args[2],bindingSha256:args[4],output:args[6],cache:args[8]});if(proof.status!=='BOUNDED_CONFIRMATION_TRACE_CAPTURED')process.exitCode=2
 }catch(error){console.log(JSON.stringify({status:'TRACE_REFUSED_OR_INCOMPLETE',code:publicFailure(error),onlineCapacityAccepted:false}));process.exitCode=2}
}
