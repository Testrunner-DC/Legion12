import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import {createExecutableCore} from './testrun-pressure-execution.mjs'
import {IDENTITY_CODE} from './testrun-pressure-runtime.mjs'

export const TRACE_LIMITS=Object.freeze({accounts:20,rooms:10,commandsPerRoom:16,cadenceMs:2000,
 confirmationMs:3000,windowMs:45000,passiveTailMs:10000,wallMs:900000,recoveryReserveMs:180000,maximumRows:32768})
// The conservative command bound is 96*(10+20+30+30) preparation commands,
// 30*(10+20+30) measured commands and 30 existing terminal surrenders.
export const FULL_TRACE_PROFILE=Object.freeze({name:'representative-30-plus-10-f737',rooms:30,
 maximumCommands:10470,commandsPerRoom:475,maximumRows:65536,maximumDrift:50000,
 journalBatch:160,captureMs:30000,passiveTailMs:10000,recoveryReserveMs:180000})
const commandTypes=new Set(['resolvePrompt','mulligan','playCard','attack','resolveDefense','endTurn'])
const messageTypes=new Set(['gameState','gameStateDelta','actionRejected','error','session','recoveryComplete','roomState','roomLeft','roomClosed','pong','presenceSnapshot','presenceDelta','resourceVersions','resourceChanged'])
const outcomes=new Set(['accepted','advanced-without-ack','rejected','timed-out','cancelled','transport-error'])
const stopSources=new Set(['none','primary-command-failure','guard-or-stop-file','window-complete','overall-deadline','trace-fault'])
const number=value=>Number.isFinite(value)?value:null
const revision=value=>Number.isSafeInteger(value)&&value>=0?value:null
const suffix='\nexport const confirmationTraceHooks=Object.freeze({authoritativeFullSnapshot,waitForPreparationCadence,cohortRequest,registerAccounts:cohort.registerAccounts});\n'

// Only expose existing private QA helpers. No body, selector, codec or timeout
// replacement; removing this suffix restores the exact executable core bytes.
export function createConfirmationTraceCore(adapterUrl,{profile='bounded-10-room'}={}){
 assert.ok(['bounded-10-room',FULL_TRACE_PROFILE.name].includes(profile))
 const base=createExecutableCore(adapterUrl,{confirmationTrace:profile===FULL_TRACE_PROFILE.name}),source=base.source+suffix
 assert.equal(source.slice(0,-suffix.length),base.source)
 return {source,evidence:{...base.evidence,traceOnly:true,originalRunPressureInvoked:false,
  traceSourceSha256:crypto.createHash('sha256').update(source).digest('hex').toUpperCase(),exportOnlySuffixSha256:crypto.createHash('sha256').update(suffix).digest('hex').toUpperCase()}}
}

export function snapshotDecodeCounters(metrics,rooms){
 const count=value=>{assert.ok(Number.isSafeInteger(value)&&value>=0,'TRACE_DECODE_COUNTER_INVALID');return value}
 const hand=metrics.currentHandArrayDelta
 const peers=rooms.flatMap(room=>[room.host,room.guest]).filter(Boolean)
 return {reconstructionErrors:count(metrics.reconstructionErrors),presenceReconstructionErrors:count(metrics.presenceReconstructionErrors),
  presenceRecoveries:count(metrics.presenceRecoveries),presenceDeltaApplied:count(metrics.presenceDeltaApplied),
  invalidJsonFrames:metrics.ws.filter(row=>row.type==='invalidJson').length,
  handNegotiatedSessions:count(hand.negotiatedSessions),handPackets:count(hand.packets),handApplied:count(hand.applied),handRejected:count(hand.rejected),handWireBytes:count(hand.wireBytes),
  cardFrameDecodeFailures:peers.reduce((sum,peer)=>sum+count(peer.cardFrameDecodeFailures),0),
  plainRecoveryRequests:peers.reduce((sum,peer)=>sum+count(peer.plainRecoveryRequests),0),
  packedFrames:peers.reduce((sum,peer)=>sum+count(peer.packedFrames),0),plainFrames:peers.reduce((sum,peer)=>sum+count(peer.plainFrames),0)}
}

export function createConfirmationObserver({confirmationForRequest,now=()=>performance.now(),salt=crypto.randomBytes(32),onFault=()=>{},profile='bounded-10-room'}){
 assert.equal(typeof confirmationForRequest,'function');assert.ok(Buffer.isBuffer(salt)&&salt.length===32)
 assert.ok(['bounded-10-room',FULL_TRACE_PROFILE.name].includes(profile),'TRACE_PROFILE_REJECTED')
 const full=profile===FULL_TRACE_PROFILE.name,limits=full?FULL_TRACE_PROFILE:TRACE_LIMITS
 const zero=now(),entries=[],frames=[],drift=[],clients=new Map(),byRoom=new Map(),byRequest=new Map(),byOrdinal=new Map(),seenRequests=new Set(),faults=new Set()
 let phase='setup',stopSource='none',primaryOrdinal=null,blockedPassiveSends=0,tailStarted=null,tailEnded=null
 let stageReader=()=>phase
 const at=value=>number(value-zero)
 const fault=code=>{faults.add(code);onFault(code)}
 const append=(rows,row)=>{if(rows.length>=(rows===drift&&full?limits.maximumDrift:limits.maximumRows)){fault('TRACE_NUMERIC_OUTPUT_BOUND');return false}rows.push(row);return true}
 const hash=(kind,value)=>crypto.createHmac('sha256',salt).update(kind+':'+value).digest('hex').toUpperCase()
 const markers={
  tryBegin(){return null},
  observeParsed(client,observation){
   if((!full&&!['actions','passive'].includes(phase))||!clients.has(client))return null
   const room=clients.get(client),list=byRoom.get(client.numericRoomSlot)??[]
   const message=observation.message
   const changes=message?.type==='gameStateDelta'&&Array.isArray(message.changes)?message.changes.filter(change=>Array.isArray(change?.path)&&change.path.length===1&&change.path[0]==='requestId'&&change.remove!==true):[]
   const requestId=message?.type==='gameStateDelta'?(changes.length===1?changes[0].value:null):message?.requestId
   let entry=byRequest.get(requestId)
   if(entry&&entry.room!==client.numericRoomSlot)entry=null
   const exact=Boolean(entry);entry??=list.at(-1)
   const result=exact?confirmationForRequest(observation.message,entry.requestId):null
   const row={ordinal:entry?.ordinal??null,room:client.numericRoomSlot,client:client.numericClientSlot,
    phase,stage:full?stageReader():phase,type:messageTypes.has(observation.message?.type)?observation.message.type:'other',
    callbackMs:at(observation.callbackAt),parseStartedMs:at(observation.parseStartedAt),parsedMs:at(observation.parsedAt),
    bytes:revision(observation.bytes),exactRequest:exact,ackAccepted:result?.accepted??null,
    ackRevision:revision(result?.revision),revisionBeforeDispatch:revision(client.envelope?.state?.revision),
    wireRevision:revision(observation.message?.revision??observation.message?.state?.revision),
    wireBaseRevision:revision(observation.message?.baseRevision),dispatchMs:null,revisionAfterDispatch:null,sameOwnedMatch:false}
   if(!append(frames,row))return null
   if(entry)byOrdinal.get(entry.ordinal).push(row)
   if(exact&&client===entry.actor&&entry.ack===null)entry.ack=row
   return {row,client,room}
  },
  afterDispatch(marker,completedAt){
   if(!marker)return false
   marker.row.dispatchMs=at(completedAt)
   marker.row.revisionAfterDispatch=revision(marker.client.envelope?.state?.revision)
   marker.row.sameOwnedMatch=marker.client.envelope?.state?.matchId===marker.room.expectedMatchId
    &&marker.client.envelope?.state?.you===marker.client.playerIndex
   return true
  },
  recordLoopDrift(lag){if(full||['actions','passive'].includes(phase))append(drift,{atMs:at(now()),lagMs:number(lag)})},
 }
 function attachRoom(room,accountIdsByPlayer){
  if(!full)assert.equal(phase,'setup');assert.ok(Array.isArray(accountIdsByPlayer)&&accountIdsByPlayer.length===2&&new Set(accountIdsByPlayer).size===2)
  assert.ok(accountIdsByPlayer.every(value=>typeof value==='string'&&value.length>=8))
  const slot=room.host.numericRoomSlot;assert.ok(Number.isInteger(slot)&&slot>=0&&slot<limits.rooms&&!byRoom.has(slot))
  assert.equal(room.guest.numericRoomSlot,slot);byRoom.set(slot,[])
  for(const client of [room.host,room.guest]){
   assert.ok([0,1].includes(client.playerIndex));assert.equal(client.numericClientSlot,slot*2+([room.host,room.guest].indexOf(client)))
   assert.ok(client.socket&&typeof client.socket.send==='function'&&!clients.has(client))
   clients.set(client,{expectedMatchId:room.expectedMatchId,accountIdsByPlayer})
   const originalSend=client.send.bind(client),nativeSend=client.socket.send.bind(client.socket)
   let sending=null
   client.socket.send=raw=>{
    // Full tracing observes native traffic. It must not suppress heartbeat,
    // recovery, diagnostic receipts, later surrender or ordinary room exit.
    if(!full&&(phase==='passive'||phase==='capture')){blockedPassiveSends++;fault('TRACE_PASSIVE_SEND_ATTEMPT');return}
    if(sending)sending.nativeSendMs=at(now())
    try{return nativeSend(raw)}finally{if(sending)sending.nativeReturnMs=at(now())}
   }
   client.send=payload=>{
    if((!full&&phase!=='actions')||payload?.type!=='gameAction')return originalSend(payload)
    const list=byRoom.get(slot)
    if(full&&(list.length>=limits.commandsPerRoom||entries.length>=limits.maximumCommands||seenRequests.has(payload.requestId)
      ||typeof payload.requestId!=='string'||!payload.requestId.length||payload.requestId.length>128
      ||(!commandTypes.has(payload.command?.type)&&payload.command?.type!=='surrender'))){fault('TRACE_COMMAND_BOUND_OR_IDENTITY');return originalSend(payload)}
    assert.ok(list.length<limits.commandsPerRoom&&entries.length<(full?limits.maximumCommands:160),'TRACE_COMMAND_CAP')
    assert.ok(typeof payload.requestId==='string'&&payload.requestId.length>0&&payload.requestId.length<=128&&!seenRequests.has(payload.requestId),'TRACE_REQUEST_ID_REUSED')
    assert.ok(commandTypes.has(payload.command?.type)||(full&&payload.command?.type==='surrender'),'TRACE_COMMAND_TYPE_REJECTED')
    seenRequests.add(payload.requestId)
    const entry={ordinal:entries.length,room:slot,actor:client,requestId:payload.requestId,type:payload.command.type,
     stage:full?stageReader():phase,beforeRevision:revision(client.envelope?.state?.revision),nativeSendMs:null,nativeReturnMs:null,
     ack:null,outcome:null,sealedMs:null,originalDurationMs:null,cancelSource:'none'}
    entries.push(entry);list.push(entry);byRequest.set(entry.requestId,entry);byOrdinal.set(entry.ordinal,[]);sending=entry
    try{return originalSend(payload)}finally{sending=null}
   }
  }
 }
 function sealRoom(room,args){
  const entry=byRoom.get(room)?.at(-1)
  assert.ok(entry&&entry.outcome===null&&outcomes.has(args[1]),'TRACE_OUTCOME_UNBOUND')
  entry.outcome=args[1];entry.originalDurationMs=number(args[2]);entry.sealedMs=at(now());entry.cancelSource=stopSource
  if(entry.outcome!=='accepted'&&stopSource==='none'&&primaryOrdinal===null)primaryOrdinal=entry.ordinal
 }
 function sealRequest(room,requestId,args,signal){
  const entry=byRequest.get(requestId)
  assert.ok(entry&&entry.room===room.host.numericRoomSlot&&entry.outcome===null&&outcomes.has(args[1]),'TRACE_OUTCOME_UNBOUND')
  entry.outcome=args[1];entry.originalDurationMs=number(args[2]);entry.sealedMs=at(now())
  // A failed full-workload command does not abort its siblings. Only the
  // original signal/deadline can explain this command's cancellation.
  const code=typeof signal?.reason?.code==='string'?signal.reason.code:signal?.reason?.message
  entry.workerStopCode=['preparation-workers-stopped','preparation-time-budget-exhausted','payload-floor-stage-workers-stopped','hard-deadline','stop-file'].includes(code)?code:null
  const originalStops={'preparation-workers-stopped':'original-preparation-stop','preparation-time-budget-exhausted':'original-preparation-deadline',
   'payload-floor-stage-workers-stopped':'original-stage-stop','hard-deadline':'original-hard-stop','stop-file':'original-stop-file'}
  entry.cancelSource=args[1]!=='cancelled'?'none':!signal?.aborted?'original-stage-deadline':originalStops[entry.workerStopCode]??'original-worker-abort'
  if(args[1]!=='accepted'&&args[1]!=='cancelled'&&primaryOrdinal===null)primaryOrdinal=entry.ordinal
 }
 function report(){
  const publicEntries=entries.map(entry=>{
   const related=byOrdinal.get(entry.ordinal)
   const ack=entry.ack,ackRevision=ack?.ackAccepted?ack.ackRevision:null
   const convergence=[entry.room*2,entry.room*2+1].map(client=>{
    const frame=ackRevision===null?null:related.find(row=>row.client===client&&row.sameOwnedMatch
     &&row.revisionAfterDispatch===ackRevision&&row.dispatchMs!==null)
    return {client,revision:frame?.revisionAfterDispatch??null,dispatchMs:frame?.dispatchMs??null}
   })
   const send=entry.nativeSendMs
   return {ordinal:entry.ordinal,room:entry.room,actorClient:entry.actor.numericClientSlot,type:entry.type,stage:entry.stage,
    requestHash:hash('request',entry.requestId),beforeRevision:entry.beforeRevision,nativeSendMs:send,nativeReturnMs:entry.nativeReturnMs,
    originalOutcome:entry.outcome,sealedMs:entry.sealedMs,originalDurationMs:entry.originalDurationMs,cancelSource:entry.cancelSource,workerStopCode:entry.workerStopCode??null,
    exactActorAck:ack!==null,ackAccepted:ack?.ackAccepted??null,ackRevision,ackCallbackMs:ack?.callbackMs??null,
    ackAfterOriginal3s:send!==null&&ack!==null&&ack.callbackMs-send>TRACE_LIMITS.confirmationMs,
    convergence,originalOutcomeNeverUpgraded:true}
  })
  return {schema:1,kind:'owned-command-confirmation-trace',profile,limits,onlineCapacityAccepted:false,
   primaryFailureOrdinal:primaryOrdinal,primaryFailureDoesNotImplySiblingAbort:full,stopSource,blockedPassiveSends,faults:[...faults].sort(),
   passiveTail:{startedMs:tailStarted,endedMs:tailEnded,durationMs:tailStarted!==null&&tailEnded!==null?tailEnded-tailStarted:null},
   commands:publicEntries,frames:structuredClone(frames),loopDrift:structuredClone(drift),
   privacy:{rawRequests:0,accounts:0,credentials:0,roomCodes:0,cardOrPlayerData:0},
   boundary:'Send to native JavaScript message callback includes remote/network/local scheduling; receiver projections need not have identical hashes. Late observations never change original outcomes.'}
 }
 return {markers,attachRoom,sealRoom,sealRequest,report,
  bindRoom(room,host,guest){const accounts=[];accounts[room.host.playerIndex]=host.accountId;accounts[room.guest.playerIndex]=guest.accountId;attachRoom(room,accounts)},
  setStageReader(read){assert.equal(typeof read,'function');stageReader=read},
  sealTerminal(room,outcome){const entry=byRoom.get(room.host.numericRoomSlot)?.at(-1);if(!entry||entry.type!=='surrender'||entry.outcome!==null)return
   const errors=outcome.errors??[],status=outcome.compliantSurrenders===1?'accepted':errors.some(value=>value.includes('timeout'))?'timed-out':errors.some(value=>value.includes('rejected'))?'rejected':'transport-error'
   entry.outcome=status;entry.sealedMs=at(now());entry.originalDurationMs=entry.nativeSendMs===null?null:entry.sealedMs-entry.nativeSendMs;entry.cancelSource='none'
   if(status!=='accepted'&&primaryOrdinal===null)primaryOrdinal=entry.ordinal},
  setPhase(value){assert.ok(['setup','actions','passive','capture','cleanup'].includes(value));if(phase===value)return;phase=value;if(value==='passive')tailStarted=at(now());if((value==='capture'||(full&&value==='cleanup'))&&tailStarted!==null&&tailEnded===null)tailEnded=at(now())},
  setStopSource(value){assert.ok(stopSources.has(value));if(stopSource==='none')stopSource=value},
  getStopSource(){return stopSource},
  async withPrivateRequests(consume){
   const requests=entries.filter(entry=>entry.nativeSendMs!==null).map(entry=>{
    const room=clients.get(entry.actor)
    return {ordinal:entry.ordinal,room:entry.room,matchId:room.expectedMatchId,
     account0:room.accountIdsByPlayer[0],account1:room.accountIdsByPlayer[1],playerIndex:entry.actor.playerIndex,requestId:entry.requestId}
   })
   try{return await consume(requests)}finally{for(const request of requests){request.matchId='';request.account0='';request.account1='';request.requestId=''}}
  },
 }
}

export function composeConfirmationMarkers(original,observer){
 return {tryBegin:args=>original.tryBegin(args),
  observeParsed:(client,value)=>({original:original.observeParsed(client,value),extra:observer.markers.observeParsed(client,value)}),
  afterDispatch:(marker,at)=>{original.afterDispatch(marker?.original,at);observer.markers.afterDispatch(marker?.extra,at)},
  recordLoopDrift:lag=>{original.recordLoopDrift(lag);observer.markers.recordLoopDrift(lag)},report:(...args)=>original.report(...args)}
}

export async function observeFailedSetupTail({observer,failed,workersFrozen,deadlineMs,assertFresh,now=Date.now,wait=ms=>new Promise(resolve=>setTimeout(resolve,ms))}){
 if(!failed)return {observed:false,durationMs:0}
 assert.equal(workersFrozen,true,'TRACE_WORKERS_MUST_BE_FROZEN')
 assert.equal(observer.report().passiveTail.startedMs,null,'TRACE_FAILURE_TAIL_ALREADY_OBSERVED')
 const start=now(),until=Math.min(start+FULL_TRACE_PROFILE.passiveTailMs,deadlineMs-FULL_TRACE_PROFILE.recoveryReserveMs-FULL_TRACE_PROFILE.captureMs)
 assert.ok(until>=start,'TRACE_RECOVERY_RESERVE_REQUIRED');observer.setPhase('passive')
 try{while(now()<until){assertFresh();await wait(Math.min(100,until-now()))}}
 finally{observer.setPhase('cleanup')}
 return {observed:true,durationMs:now()-start,originalOutcomeNeverUpgraded:true}
}

export async function readFullOwnedJournal({observer,binding,deadlineMs,remote,now=Date.now,assertFresh,budget={remainingMs:FULL_TRACE_PROFILE.captureMs}}){
 assert.ok(Number.isFinite(budget.remainingMs)&&budget.remainingMs>=0&&budget.remainingMs<=FULL_TRACE_PROFILE.captureMs)
 const started=now(),until=Math.min(started+budget.remainingMs,deadlineMs-FULL_TRACE_PROFILE.recoveryReserveMs)
 assert.ok(until-now()>=1000,'TRACE_RECOVERY_RESERVE_REQUIRED')
 const rows=[],hashes=new Map(observer.report().commands.map(row=>[row.ordinal,row.requestHash]))
 try{return await observer.withPrivateRequests(async requests=>{
  for(let offset=0;offset<requests.length;offset+=FULL_TRACE_PROFILE.journalBatch){
   assertFresh();const remaining=until-now();assert.ok(remaining>=1000,'TRACE_CAPTURE_TOTAL_DEADLINE')
   const batch=requests.slice(offset,offset+FULL_TRACE_PROFILE.journalBatch)
   const result=await remote(FULL_OWNED_JOURNAL_CODE,{...binding,requests:batch,profile:FULL_TRACE_PROFILE.name},{timeoutMs:Math.min(remaining,15000)})
   assert.ok(now()<=until,'TRACE_CAPTURE_TOTAL_DEADLINE');assert.equal(result.readOnly,true);assert.equal(result.capturedBeforeAnonymization,true);assert.equal(result.rows.length,batch.length)
   const expected=new Map(batch.map(row=>[row.ordinal,row.room]))
   for(const row of result.rows){
    assert.ok(expected.has(row.ordinal)&&hashes.has(row.ordinal),'TRACE_JOURNAL_ORDINAL_UNBOUND')
    assert.ok(row.room===expected.get(row.ordinal)&&row.ownedScopeVerified===true&&typeof row.present==='boolean','TRACE_JOURNAL_ROW_INVALID');expected.delete(row.ordinal)
    if(row.present)assert.ok([0,1].includes(row.accepted)&&Number.isSafeInteger(row.commandSequence)&&row.commandSequence>=0&&Number.isSafeInteger(row.revision)&&row.revision>=0
     &&/^[A-F0-9]{64}$/.test(row.stateHash)&&Number.isSafeInteger(row.serverCreatedUtcMs)&&row.serverCreatedUtcMs>=0&&row.eventMatches===true,'TRACE_JOURNAL_ROW_INVALID')
    const keys=['ordinal','room','ownedScopeVerified','present',...(row.present?['commandSequence','accepted','revision','stateHash','serverCreatedUtcMs','eventMatches']:[])]
    assert.deepEqual(Object.keys(row).sort(),keys.toSorted(),'TRACE_JOURNAL_ROW_INVALID')
    rows.push({...row,requestHash:hashes.get(row.ordinal)})
   }
   assert.equal(expected.size,0)
  }
  return {schema:1,kind:'full-owned-confirmation-journal-readback',readOnly:true,capturedBeforeAnonymization:true,rows,databaseWrites:0,formalWrites:0,onlineCapacityAccepted:false}
 })}finally{budget.remainingMs=Math.max(0,budget.remainingMs-(now()-started))}
}

export async function runConfirmationWindow({rooms,hooks,observer,context,metrics,assertFresh,now=Date.now,wait=ms=>new Promise(resolve=>setTimeout(resolve,ms)),existsStop=()=>false}){
 assert.equal(rooms.length,TRACE_LIMITS.rooms);assert.ok(context.cleanupDeadline-now()>=TRACE_LIMITS.recoveryReserveMs+TRACE_LIMITS.windowMs+TRACE_LIMITS.passiveTailMs+20000,'TRACE_RECOVERY_RESERVE_REQUIRED')
 const until=now()+TRACE_LIMITS.windowMs,stop=new AbortController(),signal=AbortSignal.any([context.signal,stop.signal])
 const local={...context,signal,measuredCommand:false},counts=rooms.map(()=>0)
 let reason='command-cap-reached',firstFailure=false,tailComplete=false
 const halt=(source,error)=>{if(!stop.signal.aborted){observer.setStopSource(source);stop.abort(error);reason=source}}
 const watcher=setInterval(()=>{try{assertFresh();if(existsStop())halt('guard-or-stop-file',new Error('TRACE_GUARD_STOP'))}catch{halt('guard-or-stop-file',new Error('TRACE_GUARD_STOP'))}},100)
 const timer=setTimeout(()=>halt('window-complete',new Error('TRACE_WINDOW_COMPLETE')),TRACE_LIMITS.windowMs)
 observer.setPhase('actions');metrics.currentStage='confirmation-trace'
 let expected=performance.now()+20
 const loop=setInterval(()=>{const current=performance.now();observer.markers.recordLoopDrift(Math.max(0,current-expected));expected=current+20},20)
 try{
  await Promise.allSettled(rooms.map(async(room,slot)=>{
   try{
    while(counts[slot]<TRACE_LIMITS.commandsPerRoom&&!signal.aborted){
     await hooks.waitForPreparationCadence(room,until,signal)
     if(now()+TRACE_LIMITS.confirmationMs+250>=until)break
     assertFresh();let selected=null
     for(const client of [room.host,room.guest]){
      const snapshot=await hooks.authoritativeFullSnapshot(client,until,local),candidate=hooks.commandFromSnapshot(client,snapshot,room)
      if(candidate.command){selected={client,snapshot,...candidate};break}
     }
     assert.ok(selected,'TRACE_NO_LEGAL_COMMAND');let recorded=0,outcome=null
     const exactBefore=room.payloadFloorEvidence.exactAckMatches
     const sink={recordCommandSkip(){throw new Error('TRACE_COMMAND_SKIPPED')},recordCommand(...args){recorded++;outcome=args[1];observer.sealRoom(slot,args);metrics.recordCommand(...args)}}
     await hooks.sendAuthoritativeCommand(room,selected.client,selected.snapshot,selected,until,local,sink)
     counts[slot]++
     if(recorded!==1||outcome!=='accepted'||room.payloadFloorEvidence.exactAckMatches!==exactBefore+1
       ||room.payloadFloorEvidence.peerConvergenceMatches!==exactBefore+1){
      if(!signal.aborted){firstFailure=true;halt('primary-command-failure',new Error('TRACE_COMMAND_NOT_CONFIRMED'))}
      break
     }
    }
   }catch{if(!signal.aborted){firstFailure=true;halt('trace-fault',new Error('TRACE_WORKER_FAILED'))}}
  }))
  if(context.signal.aborted&&reason==='command-cap-reached')reason=observer.getStopSource?.()??'overall-deadline'
  observer.setPhase('passive')
  const tailUntil=now()+TRACE_LIMITS.passiveTailMs
  while(now()<tailUntil){assertFresh();assert.ok(!existsStop(),'TRACE_GUARD_STOP');await wait(Math.min(100,tailUntil-now()))}
  tailComplete=true
 }finally{clearInterval(watcher);clearTimeout(timer);clearInterval(loop);observer.setPhase('capture')}
 return {reason,firstFailure,tailComplete,commandsPerRoom:counts,maximumCommands:TRACE_LIMITS.rooms*TRACE_LIMITS.commandsPerRoom,
  original3sUnchanged:true,measuredPerformance:false,onlineCapacityAccepted:false}
}

// Shared verbatim by the fixed remote wrapper and actual in-memory SQLite tests.
// No remote path, connection mode or identity is injectable by the CLI.
export const OWNED_JOURNAL_QUERY=String.raw`
import datetime,time
def read_owned_confirmation_rows(connection,requests,profile='bounded-10-room'):
 assert profile in ['bounded-10-room','representative-30-plus-10-f737']
 maximum_commands,maximum_rooms=(10470,30) if profile=='representative-30-plus-10-f737' else (160,10)
 assert isinstance(requests,list) and len(requests)<=160
 rows=[];owned={};seen=set()
 for request in requests:
  assert set(request)=={'ordinal','room','matchId','account0','account1','playerIndex','requestId'}
  assert type(request['ordinal']) is int and 0<=request['ordinal']<maximum_commands and request['ordinal'] not in seen
  seen.add(request['ordinal']);assert type(request['room']) is int and 0<=request['room']<maximum_rooms
  assert type(request['playerIndex']) is int and request['playerIndex'] in [0,1] and request['account0']!=request['account1']
  assert all(isinstance(request[key],str) and 1<=len(request[key])<=128 for key in ['matchId','account0','account1','requestId'])
  scope=(request['matchId'],request['account0'],request['account1'])
  if request['room'] in owned:assert owned[request['room']]==scope
  else:
   matched=connection.execute('SELECT COUNT(*) FROM matches WHERE match_id=? AND account_0=? AND account_1=? AND identity_scrubbed=0',scope).fetchone()[0]
   assert matched==1;owned[request['room']]=scope
  value=connection.execute('SELECT command_sequence,accepted,revision,state_hash,created_utc FROM match_action_requests WHERE match_id=? AND player_index=? AND request_id=?',(request['matchId'],request['playerIndex'],request['requestId'])).fetchone()
  row={'ordinal':request['ordinal'],'room':request['room'],'ownedScopeVerified':True,'present':value is not None}
  if value is not None:
   sequence,accepted,revision,state_hash,created=value
   assert type(sequence) is int and 0<=sequence<=9007199254740991 and type(accepted) is int and accepted in [0,1] and type(revision) is int and 0<=revision<=9007199254740991
   assert isinstance(state_hash,str) and len(state_hash)==64 and all(char in '0123456789abcdefABCDEF' for char in state_hash)
   event=connection.execute('SELECT accepted,revision,state_hash FROM match_events WHERE match_id=? AND sequence=? AND player_index=? AND request_id=?',(request['matchId'],sequence,request['playerIndex'],request['requestId'])).fetchall()
   assert len(event)==1 and event[0]==(accepted,revision,state_hash)
   created_ms=int(datetime.datetime.fromisoformat(created.replace('Z','+00:00')).timestamp()*1000)
   row.update(commandSequence=sequence,accepted=accepted,revision=revision,stateHash=state_hash.upper(),serverCreatedUtcMs=created_ms,eventMatches=True)
  rows.append(row)
 return rows
`
// Executed only by the existing pinned stdin RPC after the passive tail.
// Fixed test DB, read-only connection, exact owned match/request predicates.
export const OWNED_JOURNAL_CODE=IDENTITY_CODE+OWNED_JOURNAL_QUERY+String.raw`
import sqlite3,stat
db=pathlib.Path('/opt/legion12-testrun-runtime/matches.db')
test_runtime=pathlib.Path('/opt/legion12-testrun/publish/runtime').resolve(strict=True)
formal_runtime=pathlib.Path('/opt/legion12-test/publish/runtime').resolve(strict=True)
assert test_runtime==db.parent and test_runtime!=formal_runtime and db.resolve(strict=True)==db
assert stat.S_ISREG(db.lstat().st_mode) and not db.is_symlink()
forbidden=set()
for suffix in ['', '-wal', '-shm']:
 item=formal_runtime/('matches.db'+suffix)
 if item.exists():
  info=item.stat();forbidden.add((info.st_dev,info.st_ino))
assert (formal_runtime/'matches.db').is_file()
for suffix in ['', '-wal', '-shm']:
 item=pathlib.Path(str(db)+suffix)
 if item.exists() or item.is_symlink():
  info=item.lstat();assert stat.S_ISREG(info.st_mode) and not item.is_symlink() and (info.st_dev,info.st_ino) not in forbidden
until=time.monotonic()+12
connection=sqlite3.connect('file:'+str(db)+'?mode=ro',uri=True,timeout=2)
connection.execute('PRAGMA query_only=ON');connection.set_progress_handler(lambda: int(time.monotonic()>until),1000)
try:
 connection.execute('BEGIN');rows=read_owned_confirmation_rows(connection,p['requests'])
 connection.rollback()
finally:connection.close()
print(json.dumps({'schema':1,'kind':'owned-confirmation-journal-readback','readOnly':True,'capturedBeforeAnonymization':True,'rows':rows,'formalWrites':0,'databaseWrites':0},separators=(',',':')))
`
// Same fixed read-only database/alias/identity checks, explicit bounded full scope.
export const FULL_OWNED_JOURNAL_CODE=OWNED_JOURNAL_CODE.replace("read_owned_confirmation_rows(connection,p['requests'])","read_owned_confirmation_rows(connection,p['requests'],p['profile'])")
