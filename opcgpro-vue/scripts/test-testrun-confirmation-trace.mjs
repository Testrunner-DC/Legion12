import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {execFileSync} from 'node:child_process'
import {pathToFileURL} from 'node:url'
import {TRACE_LIMITS,createConfirmationTraceCore,createConfirmationObserver,runConfirmationWindow,snapshotDecodeCounters,OWNED_JOURNAL_QUERY,OWNED_JOURNAL_CODE} from './testrun-confirmation-trace.mjs'
import {createTraceLedgerControl,registerTwentyOwned} from './run-testrun-confirmation-trace.mjs'
import {EVIDENCE_ROOT,CACHE_ROOT,assertGeneratedDirectories,digest} from './testrun-pressure-protocol.mjs'
import {createOwnedLedgerSinks} from './testrun-pressure-execution.mjs'
import {readColdLedgersPrivately,recoverOwnedLedgerAccounts} from './testrun-pressure-recovery.mjs'
import {durableJson} from './testrun-pressure-runtime.mjs'
import * as confirmation from './testrun-confirmation-trace.mjs'

const args=process.argv.slice(2);assert.equal(args.length,4);assert.equal(args[0],'--output');assert.equal(args[2],'--cache')
const output=args[1],cache=args[3];assert.ok(path.isAbsolute(output)&&path.isAbsolute(cache));assertGeneratedDirectories(output,cache)
for(const root of [output,cache]){if(!fs.existsSync(root))fs.mkdirSync(root,{recursive:true});assert.deepEqual(fs.readdirSync(root),[])}
process.env.TEMP=cache;process.env.TMP=cache;process.env.TMPDIR=cache
const cases=[],details={};let networkCalls=0
const originalFetch=globalThis.fetch,originalWebSocket=globalThis.WebSocket
globalThis.fetch=()=>{networkCalls++;throw new Error('OFFLINE_NETWORK_FORBIDDEN')}
globalThis.WebSocket=class {constructor(){networkCalls++;throw new Error('OFFLINE_SOCKET_FORBIDDEN')}}
async function check(name,run){try{await run();cases.push({name,passed:true})}catch(error){cases.push({name,passed:false,code:/^[A-Z_0-9]{1,80}$/.test(error.code??'')?error.code:'OFFLINE_ASSERTION',locations:String(error.stack??'').split('\n').filter(line=>/^\s+at /.test(line)).slice(0,4)})}}
const binding={kind:'normal-testrun-pressure-v1',baseUrl:'https://legion-12.com/testrun/',wsUrl:'wss://legion-12.com/testrun/ws',
 test:{pid:542637,commit:'131989a65ef3b26a22e58e5fe5c480c061aae9cb',dllSha256:'B'.repeat(64)},
 formal:{pid:534086,commit:'546187bc'+ 'a'.repeat(32),dllSha256:'C'.repeat(64)},
 client:{netSourcePath:path.resolve(import.meta.dirname,'../src/l12/net.ts'),netSha256:digest(fs.readFileSync(path.resolve(import.meta.dirname,'../src/l12/net.ts'))),
 cardFrameSourcePath:path.resolve(import.meta.dirname,'../src/l12/gameStateCardFrame.ts'),cardFrameSha256:digest(fs.readFileSync(path.resolve(import.meta.dirname,'../src/l12/gameStateCardFrame.ts'))),
 handArraySourcePath:path.resolve(import.meta.dirname,'../src/l12/handArrayDelta.ts'),handArraySha256:digest(fs.readFileSync(path.resolve(import.meta.dirname,'../src/l12/handArrayDelta.ts')))}}
let core,fullCore,LoadV2Markers
await check('actual generated core imports; only export suffix added; original functions retained',async()=>{
 const generated=createConfirmationTraceCore(new URL('./testrun-pressure-protocol.mjs',import.meta.url).href)
 assert.equal(generated.evidence.originalWorkloadUnchanged,true);assert.equal(generated.evidence.byteExactRoundtrip,true)
 const file=path.join(cache,'actual-generated-core.mjs');fs.writeFileSync(file,generated.source,{flag:'wx'})
 core=await import(pathToFileURL(file).href);await core.initializeNormalTestrunProtocol(binding)
 for(const name of ['authoritativeFullSnapshot','waitForPreparationCadence','registerAccounts'])assert.equal(typeof core.confirmationTraceHooks[name],'function')
 assert.equal(typeof core.workloadTestHooks.WsClient,'function');assert.equal(typeof core.payloadFloorTestHooks.sendAuthoritativeCommand,'function')
 details.generatedSourceSha256=generated.evidence.traceSourceSha256
})

function memoryFixture({respond=true,full=false,roomSlot=0,sharedMarkers=null,sourceOverride=null}={}){
 const currentCore=sourceOverride??(full?fullCore:core)
 let responseMode=respond
 const sockets=[],wire=[],roomCode='ROOM-SECRET-CANARY',matchId='MATCH-SECRET-CANARY'
 let revision=1
 const state=(socket,revision)=>({matchId,roomCode,you:socket.playerIndex,revision,phase:'Main',
  players:[{name:'PLAYER-SECRET-CANARY',hand:[{cardId:'CARD-SECRET-CANARY',name:'CARD-NAME-SECRET-CANARY'}]}],round:1,activePlayer:0})
 class MemorySocket extends EventTarget {
  static OPEN=1;static CLOSING=2;readyState=1
  constructor(url){super();assert.ok(url instanceof URL,'the real core must pass its URL object unchanged');assert.equal(url.href,'wss://legion-12.com/testrun/ws');this.playerIndex=sockets.length===0?1:0;this.sent=[];sockets.push(this);queueMicrotask(()=>this.dispatchEvent(new Event('open')))}
  emit(message){this.dispatchEvent(Object.assign(new Event('message'),{data:JSON.stringify(message)}))}
  send(raw){
   const message=JSON.parse(raw);this.sent.push(message);wire.push(message.type)
   if(message.type==='hello')queueMicrotask(()=>{this.emit({type:'session',capabilities:{...message.capabilities,handArrayDeltaV1:true}});this.emit({type:'recoveryComplete'})})
   else if(['createRoom','joinRoom','selectDeck'].includes(message.type))queueMicrotask(()=>this.emit({type:'roomState',roomCode,yourPlayerIndex:this.playerIndex}))
   else if(message.type==='ready'&&sockets.every(socket=>socket.sent.some(row=>row.type==='ready')))queueMicrotask(()=>sockets.forEach(socket=>socket.emit({type:'gameState',state:state(socket,1)})))
   else if(message.type==='gameAction'&&responseMode)queueMicrotask(()=>{
    if(responseMode==='reject'){this.emit({type:'actionRejected',requestId:message.requestId});return}
    revision++;const next=socket=>({...state(socket,revision),phase:message.command.type==='surrender'?'GameOver':'Main'})
    sockets.find(socket=>socket!==this).emit({type:'gameState',state:next(sockets.find(socket=>socket!==this))})
    this.emit({type:'gameState',requestId:message.requestId,state:next(this)})
   })
   else if(message.type==='leaveRoom')queueMicrotask(()=>this.emit({type:'roomLeft'}))
   else if(message.type==='ping')queueMicrotask(()=>this.emit({type:'pong'}))
  }
  close(){this.readyState=3;this.dispatchEvent(new Event('close'))}
 }
 globalThis.WebSocket=MemorySocket
 const observer=createConfirmationObserver({confirmationForRequest:currentCore.payloadFloorTestHooks.confirmationForRequest,salt:Buffer.alloc(32,7),...(full?{profile:confirmation.FULL_TRACE_PROFILE.name}:{})})
 const controller=new AbortController(),context={local:false,baseUrl:binding.baseUrl,requestTimeoutMs:10000,signal:controller.signal,actionMarkers:observer.markers,cleanupDeadline:Date.now()+900000}
 const metrics=new currentCore.workloadTestHooks.Metrics();metrics.currentHandArrayDelta={negotiatedSessions:0,packets:0,wireBytes:0,applied:0,rejected:0}
 metrics.currentPresenceOrderReuse={negotiatedSessions:0,packets:0,wireBytes:0,applied:0,rejected:0}
 const originalMarkers=full?(sharedMarkers??new LoadV2Markers({nonce:'a'.repeat(32)})):null
 if(full){
  observer.setStageReader(()=>metrics.currentStage);context.actionMarkers=confirmation.composeConfirmationMarkers(originalMarkers,observer)
  context.confirmationTrace={bindRoom(room,host,guest){room.host.numericRoomSlot=room.guest.numericRoomSlot=roomSlot;room.host.numericClientSlot=roomSlot*2;room.guest.numericClientSlot=roomSlot*2+1;observer.bindRoom(room,host,guest)},sealRequest:observer.sealRequest,sealTerminal:observer.sealTerminal}
 }
 const rooms=[]
 return {sockets,wire,state,observer,context,metrics,rooms,originalMarkers,controller,setResponse(value){responseMode=value},async start(){
  const room=await currentCore.payloadFloorTestHooks.createRoomPair(context,{token:'TOKEN-HOST-SECRET-CANARY',accountId:'HOST-ACCOUNT-SECRET-CANARY'},{token:'TOKEN-GUEST-SECRET-CANARY',accountId:'GUEST-ACCOUNT-SECRET-CANARY'},metrics,rooms)
  const accounts=[];accounts[room.host.playerIndex]='HOST-ACCOUNT-SECRET-CANARY';accounts[room.guest.playerIndex]='GUEST-ACCOUNT-SECRET-CANARY'
  if(!full){observer.attachRoom(room,accounts);observer.setPhase('actions')};metrics.currentStage=full?'payload-preparation':'confirmation-trace';return room
 },dispose(){rooms.forEach(room=>{room.host.close();room.guest.close()});globalThis.WebSocket=class {constructor(){networkCalls++;throw new Error('OFFLINE_SOCKET_FORBIDDEN')}}}}
}
if(core){
 await check('real WsClient native send -> native message -> parser marker -> afterDispatch -> original exact ACK/convergence',async()=>{
  const fixture=memoryFixture();try{
   const room=await fixture.start(),client=room.host,snapshot=structuredClone(client.envelope.state)
   assert.equal(fixture.metrics.currentPresenceOrderReuse.negotiatedSessions,2,'fixture retains both real presence-order negotiations')
   const sink={recordCommandSkip(){assert.fail('unexpected skip')},recordCommand(...args){fixture.observer.sealRoom(0,args)}}
   await core.payloadFloorTestHooks.sendAuthoritativeCommand(room,client,snapshot,{command:{type:'endTurn'},revisionKey:'synthetic-r1'},Date.now()+5000,fixture.context,sink)
   const report=fixture.observer.report(),command=report.commands[0]
   assert.equal(command.originalOutcome,'accepted');assert.equal(command.actorClient,0);assert.equal(command.ackRevision,2)
   assert.ok(command.nativeSendMs<=command.nativeReturnMs);assert.ok(command.nativeSendMs<=command.ackCallbackMs)
   assert.equal(command.convergence.filter(row=>row.revision===2).length,2)
   assert.ok(report.frames.every(row=>row.callbackMs<=row.parseStartedMs&&row.parseStartedMs<=row.parsedMs&&row.parsedMs<=row.dispatchMs))
   await fixture.observer.withPrivateRequests(async requests=>{assert.equal(requests[0].playerIndex,1);assert.equal(requests[0].account0,'GUEST-ACCOUNT-SECRET-CANARY');assert.equal(requests[0].account1,'HOST-ACCOUNT-SECRET-CANARY')})
   const publicText=JSON.stringify(report);for(const secret of ['TOKEN-HOST-SECRET-CANARY','TOKEN-GUEST-SECRET-CANARY','HOST-ACCOUNT-SECRET-CANARY','GUEST-ACCOUNT-SECRET-CANARY','ROOM-SECRET-CANARY','MATCH-SECRET-CANARY','PLAYER-SECRET-CANARY','CARD-SECRET-CANARY','CARD-NAME-SECRET-CANARY'])assert.ok(!publicText.includes(secret))
   const before=fixture.wire.length;fixture.observer.setPhase('passive');client.send({type:'ping'});assert.equal(fixture.wire.length,before);assert.equal(fixture.observer.report().blockedPassiveSends,1)
  }finally{fixture.dispose()}
 })
 await check('real original 3000ms confirmation timeout stays failed after a late exact ACK and both revisions',async()=>{
  const fixture=memoryFixture({respond:false});try{
   const room=await fixture.start(),client=room.host,snapshot=structuredClone(client.envelope.state)
   await core.payloadFloorTestHooks.sendAuthoritativeCommand(room,client,snapshot,{command:{type:'endTurn'},revisionKey:'synthetic-timeout'},Date.now()+6000,fixture.context,
    {recordCommandSkip(){assert.fail()},recordCommand(...args){fixture.observer.sealRoom(0,args)}})
   assert.equal(fixture.observer.report().commands[0].originalOutcome,'timed-out')
   fixture.observer.setPhase('passive')
   const sent=fixture.sockets[0].sent.find(row=>row.type==='gameAction')
   fixture.sockets[1].emit({type:'gameState',state:fixture.state(fixture.sockets[1],2)})
   fixture.sockets[0].emit({type:'gameState',requestId:sent.requestId,state:fixture.state(fixture.sockets[0],2)})
   const command=fixture.observer.report().commands[0]
   assert.equal(command.originalOutcome,'timed-out');assert.equal(command.ackAfterOriginal3s,true);assert.equal(command.convergence.filter(row=>row.revision===2).length,2)
  }finally{fixture.dispose()}
 })
 await check('actual decoder failure before parser marker remains visible as numeric codec counters',async()=>{
  const fixture=memoryFixture();try{
   await fixture.start();let stopped=0;fixture.metrics.requestStop=()=>{stopped++}
   const before=fixture.observer.report().frames.length
   fixture.sockets[0].emit({type:'gameStateCardFrameV1',malformed:'CARD-SECRET-CANARY'})
   const counters=snapshotDecodeCounters(fixture.metrics,fixture.rooms)
   assert.equal(fixture.observer.report().frames.length,before);assert.equal(counters.reconstructionErrors,1);assert.equal(counters.cardFrameDecodeFailures,1);assert.equal(counters.plainRecoveryRequests,1);assert.equal(stopped,1)
   fixture.metrics.currentHandArrayDelta={negotiatedSessions:2,packets:3,applied:2,rejected:1,wireBytes:500}
   assert.equal(snapshotDecodeCounters(fixture.metrics,fixture.rooms).handRejected,1)
   assert.ok(Object.values(counters).every(value=>typeof value==='number'))
  }finally{fixture.dispose()}
 })
}

await check('bounded window reuses supplied helpers, 16/room and 2s cadence helper; passive tail makes no command calls',async()=>{
 let clock=0,phase='setup',cadences=0,sends=0,stopSource='none'
 const rooms=Array.from({length:10},(_,slot)=>({slot,host:{},guest:{},payloadFloorEvidence:{exactAckMatches:0,peerConvergenceMatches:0}}))
 const observer={setPhase(value){phase=value},setStopSource(value){stopSource=value},sealRoom(){},markers:{recordLoopDrift(){}}}
 const result=await runConfirmationWindow({rooms,observer,context:{signal:new AbortController().signal,cleanupDeadline:900000},metrics:{recordCommand(){}},assertFresh(){},now:()=>clock,
  wait:async ms=>{assert.equal(phase,'passive');clock+=ms},hooks:{
   async waitForPreparationCadence(){cadences++},async authoritativeFullSnapshot(){return {revision:1}},commandFromSnapshot(){return {command:{type:'endTurn'}}},
   async sendAuthoritativeCommand(room,_client,_snapshot,_selected,until,_context,sink){assert.equal(phase,'actions');assert.ok(until-clock>=3000);sends++;room.payloadFloorEvidence.exactAckMatches++;room.payloadFloorEvidence.peerConvergenceMatches++;sink.recordCommand('endTurn','accepted',1,true,false)},
  }})
 assert.equal(sends,160);assert.equal(cadences,160);assert.deepEqual(result.commandsPerRoom,Array(10).fill(16));assert.equal(clock,10000);assert.equal(stopSource,'none')
})
await check('one original failure cancels sibling workers, never re-labels nine cancellations as nine timeouts',async()=>{
 let clock=0,source='none';const seen=[]
 const rooms=Array.from({length:10},(_,slot)=>({slot,host:{},guest:{},payloadFloorEvidence:{exactAckMatches:0,peerConvergenceMatches:0}}))
 const observer={setPhase(){},setStopSource(value){source=value},sealRoom(slot,args){seen.push({slot,outcome:args[1],source})},markers:{recordLoopDrift(){}}}
 const result=await runConfirmationWindow({rooms,observer,context:{signal:new AbortController().signal,cleanupDeadline:900000},metrics:{recordCommand(){}},assertFresh(){},now:()=>clock,wait:async ms=>{clock+=ms},hooks:{
  async waitForPreparationCadence(){},async authoritativeFullSnapshot(){return {}},commandFromSnapshot(){return {command:{type:'endTurn'}}},
  async sendAuthoritativeCommand(room,_client,_snapshot,_selected,_until,context,sink){if(room.slot)await Promise.resolve();sink.recordCommand('endTurn',context.signal.aborted?'cancelled':'timed-out',3001,false,false)},
 }})
 assert.equal(result.firstFailure,true);assert.equal(seen.filter(row=>row.outcome==='timed-out').length,1);assert.equal(seen.filter(row=>row.outcome==='cancelled').length,9)
 assert.ok(seen.filter(row=>row.outcome==='cancelled').every(row=>row.source==='primary-command-failure'))
})
await check('recovery reserve prevents business admission before any send',async()=>{
 await assert.rejects(runConfirmationWindow({rooms:Array(10).fill({}),observer:{},context:{cleanupDeadline:1000},now:()=>0}),/TRACE_RECOVERY_RESERVE_REQUIRED/)
})
await check('registration wrapper enforces 20 POST calls even if an imported provider requests a 21st',async()=>{
 let requests=0,closed=0
 await assert.rejects(registerTwentyOwned({hooks:{async registerAccounts(_admin,count,_deadline,_secrets,_owned,options){assert.equal(count,20);for(let index=0;index<21;index++)await options.requestFn('', 'api/auth/register',{}, {method:'POST'})}},
  adminToken:'private',ledger:{close(){closed++}},owned:[],privateValues:new Set(),ledgerReceipt:{},request:async()=>{requests++;return {}},wait:async()=>{},assertFresh(){}}),/TRACE_ACCOUNT_CAP/)
 assert.equal(requests,20);assert.equal(closed,1)
})

const python=process.env.L12_QA_PYTHON||'E:/Tran/Miniconda3/python.exe'
await check('actual SQLite exact owned request/event joins: accepted/rejected/missing, identity scrub, wrong account, bad fields and injection',()=>{
 assert.ok(path.isAbsolute(python)&&fs.statSync(python).isFile())
 const harness=String.raw`
import json,sqlite3,sys
data=json.load(sys.stdin);exec(data['query'])
def fixture():
 c=sqlite3.connect(':memory:')
 c.executescript('CREATE TABLE matches(match_id TEXT,account_0 TEXT,account_1 TEXT,identity_scrubbed INTEGER);CREATE TABLE match_action_requests(match_id TEXT,player_index INTEGER,request_id TEXT,command_sequence INTEGER,accepted INTEGER,revision INTEGER,state_hash TEXT,created_utc TEXT);CREATE TABLE match_events(match_id TEXT,sequence INTEGER,player_index INTEGER,request_id TEXT,accepted INTEGER,revision INTEGER,state_hash TEXT);')
 c.execute('INSERT INTO matches VALUES(?,?,?,0)',('MATCH-CANARY','ACCOUNT0-CANARY','ACCOUNT1-CANARY'))
 c.execute('INSERT INTO match_action_requests VALUES(?,?,?,?,?,?,?,?)',('MATCH-CANARY',0,'REQUEST-CANARY',1,1,2,'a'*64,'2026-10-22T12:00:00Z'))
 c.execute('INSERT INTO match_events VALUES(?,?,?,?,?,?,?)',('MATCH-CANARY',1,0,'REQUEST-CANARY',1,2,'a'*64))
 return c,{'ordinal':0,'room':0,'matchId':'MATCH-CANARY','account0':'ACCOUNT0-CANARY','account1':'ACCOUNT1-CANARY','playerIndex':0,'requestId':'REQUEST-CANARY'}
results=[]
tests=[('accepted',None,None,False),('rejected','UPDATE match_action_requests SET accepted=0;UPDATE match_events SET accepted=0',None,False),('missing','DELETE FROM match_action_requests',None,False),('anonymized','UPDATE matches SET account_0=NULL,identity_scrubbed=1',None,True),('wrong-account',None,('account1','FOREIGN'),True),('foreign-match',None,('matchId','FOREIGN'),True),('event-player','UPDATE match_events SET player_index=1',None,True),('event-request',"UPDATE match_events SET request_id='OTHER'",None,True),('event-hash',"UPDATE match_events SET state_hash='"+'b'*64+"'",None,True),('unsafe-sequence','UPDATE match_action_requests SET command_sequence=9223372036854775807',None,True),('unsafe-revision','UPDATE match_action_requests SET revision=9223372036854775807',None,True),('accepted-invalid','UPDATE match_action_requests SET accepted=2',None,True),('hash-invalid',"UPDATE match_action_requests SET state_hash='bad'",None,True),('date-invalid',"UPDATE match_action_requests SET created_utc='bad'",None,True),('injection',None,('matchId',"' OR 1=1 --"),True)]
for name,sql,change,rejected in tests:
 c,request=fixture()
 if sql:c.executescript(sql)
 if change:request[change[0]]=change[1]
 c.commit();before=c.total_changes;c.execute('PRAGMA query_only=ON')
 caught=False
 try:rows=read_owned_confirmation_rows(c,[request])
 except (AssertionError,ValueError):caught=True
 assert caught==rejected
 if not caught:
  if name=='accepted':assert rows[0]['accepted']==1 and rows[0]['eventMatches']
  if name=='rejected':assert rows[0]['accepted']==0
  if name=='missing':assert rows[0]['present'] is False
 assert c.total_changes==before
 c.close();results.append({'name':name,'passed':True})
print(json.dumps({'sqliteCases':results,'directSqlWrites':0}))
`
 const result=JSON.parse(execFileSync(python,['-I','-B','-c',harness],{input:JSON.stringify({query:OWNED_JOURNAL_QUERY}),encoding:'utf8',windowsHide:true,timeout:15000,maxBuffer:16384,env:{...process.env,PYTHONDONTWRITEBYTECODE:'1'}}))
 assert.equal(result.sqliteCases.length,15);assert.ok(result.sqliteCases.every(row=>row.passed));details.actualSqliteCases=result.sqliteCases.length
 assert.ok(OWNED_JOURNAL_CODE.includes("'?mode=ro'"));assert.ok(OWNED_JOURNAL_CODE.includes("PRAGMA query_only=ON"));assert.ok(!OWNED_JOURNAL_CODE.includes('immutable=1'))
})

if(core)for(const loseAck of [false,true])await check(loseAck?'real DPAPI persisted intent with lost ACK remains in cold recovery despite empty owned memory':'real partial DPAPI group0 has exactly 20 owned records; unused group1 is missing; existing CAS recovery consumes all',async()=>{
 const runId=crypto.randomUUID().replaceAll('-',''),parent=path.join(cache,loseAck?'ack-loss':'twenty'),evidence=path.join(output,loseAck?'ack-loss':'twenty')
 fs.mkdirSync(parent);fs.mkdirSync(evidence);fs.mkdirSync(path.join(parent,runId))
 const control=createTraceLedgerControl({binding,bindingSha256:'A'.repeat(64),runId,cache:parent,openedUtcMs:Date.now()-1000,deadlineMs:Date.now()+800000})
 const controlPath=path.join(evidence,'ledger-control.json');durableJson(controlPath,control);const controlSha256=digest(fs.readFileSync(controlPath))
 const sinks=await createOwnedLedgerSinks(control,{cache:path.join(parent,runId),output:evidence}),owned=[],accounts=new Map(),privateValues=new Set()
 let mutations=0,consumeCount=0,ownershipQueries=0
 await sinks.start(0)
 if(loseAck){const transport=sinks.clients[0].transport,request=transport.request.bind(transport);let first=true;transport.request=async value=>{const result=await request(value);if(first){first=false;throw new Error('SYNTHETIC_ACK_LOST_AFTER_PERSIST')}return result}}
 try{
  const registration=registerTwentyOwned({hooks:core.confirmationTraceHooks,adminToken:'a'.repeat(64),ledger:sinks.clients[0],owned,privateValues,ledgerReceipt:{},assertFresh(){},wait:async()=>{},
   request:async(_base,route,_deadline,options)=>{
    if(route.startsWith('api/admin/accounts/query'))return {total:0,items:[]}
    assert.equal(route,'api/auth/register');mutations++
    const value={id:'owned-account-'+String(mutations).padStart(8,'0'),username:options.body.username,createdAt:new Date().toISOString(),role:'player',deleted:false,disabled:false,permissionVersion:1}
    accounts.set(value.id,value);return {token:crypto.randomBytes(32).toString('hex'),account:value}
   }})
  if(loseAck)await assert.rejects(registration);else await registration
 }finally{await sinks.finish()}
 assert.equal(mutations,loseAck?0:20);assert.equal(owned.length,loseAck?0:20)
 let recovery
 const cold=await readColdLedgersPrivately({controlPath,controlSha256,bindingSha256:'A'.repeat(64),allowPartial:true,consume:async privateLedger=>{
  details[loseAck?'ackLossConsumerEntered':'twentyConsumerEntered']=true
  consumeCount=privateLedger.accounts.length;assert.equal(consumeCount,loseAck?1:20);assert.ok(privateLedger.accounts.every(row=>row.ledgerIndex===0))
  recovery=await recoverOwnedLedgerAccounts({binding,control,privateLedger,recoveryHandle:{kind:'recovery'},deadlineMs:Date.now()+800000,wait:async()=>{},
   sessionManager:{request:async(_handle,route,options={})=>{
    if(route.startsWith('api/admin/accounts/query')){ownershipQueries++;return {status:200,body:{total:0,items:[]}}}
    const found=/^api\/admin\/(?:v1\/)?accounts\/([^/]+)/.exec(route);assert.ok(found)
    const account=accounts.get(decodeURIComponent(found[1]));assert.ok(account)
    if(route.endsWith('/sessions'))return {status:200,body:[]}
    if(route.endsWith('/delete')){assert.equal(options.body.expectedVersion,account.permissionVersion);account.deleted=true;account.username='anonymous-'+account.id;return {status:200,body:{}}}
    return {status:200,body:{...account}}
   }}})
 }})
 assert.equal(cold.complete,false);assert.equal(cold.groups[1].missing,true)
 if(loseAck){assert.equal(ownershipQueries,1);assert.equal(recovery.cleanupComplete,false);assert.equal(recovery.unknown,1);assert.equal(recovery.records,1)}
 else{assert.equal(recovery.cleanupComplete,true);assert.equal(recovery.deletedNow,20);assert.equal(recovery.playerSessionsRevoked,20)}
 details[loseAck?'realAckLossColdRows':'realPartialColdRows']=consumeCount
})

await check('full profile is explicit, bounded and does not change the existing 160-command default',()=>{
 assert.equal(confirmation.FULL_TRACE_PROFILE?.name,'representative-30-plus-10-f737')
 assert.equal(confirmation.FULL_TRACE_PROFILE.rooms,30);assert.equal(TRACE_LIMITS.rooms,10);assert.equal(TRACE_LIMITS.commandsPerRoom,16)
 assert.throws(()=>createConfirmationObserver({confirmationForRequest:()=>null,profile:'unbounded'}))
})
await check('full composite preserves original six-slot marker return/report and adds the second observer',()=>{
 let observed=0,dispatched=0,drifted=0
 const original={tryBegin:()=> 'ORIGINAL-REQUEST',observeParsed:()=>({original:true}),afterDispatch:marker=>{assert.equal(marker.original,true);dispatched++},recordLoopDrift:()=>{drifted++},report:()=>({expected_samples:6})}
 const extra={markers:{observeParsed:()=>{observed++;return {extra:true}},afterDispatch:marker=>{assert.equal(marker.extra,true);dispatched++},recordLoopDrift:()=>{drifted++}}}
 const combined=confirmation.composeConfirmationMarkers(original,extra)
 assert.equal(combined.tryBegin({}),'ORIGINAL-REQUEST');combined.afterDispatch(combined.observeParsed({},{}),1);combined.recordLoopDrift(0)
 assert.equal(observed,1);assert.equal(dispatched,2);assert.equal(drifted,2);assert.deepEqual(combined.report(),{expected_samples:6})
})
await check('explicit full generated core reverses every adaptation, retains load/3s/floor and exposes original real helpers',async()=>{
 const generated=createConfirmationTraceCore(new URL('./testrun-pressure-protocol.mjs',import.meta.url).href,{profile:confirmation.FULL_TRACE_PROFILE.name})
 assert.equal(generated.evidence.byteExactRoundtrip,true);assert.equal(generated.evidence.originalWorkloadUnchanged,true)
 assert.equal(generated.evidence.confirmationTrace,true);assert.equal(generated.evidence.traceRecoveryReserveMs,220000)
 assert.ok(generated.source.includes('sizes.every(size => size >= 180*1024)'));assert.ok(generated.source.includes('const timeout = Math.max(1, Math.min(ACTION_CONFIRMATION_MS'))
 assert.ok(generated.source.includes('await configuration.confirmationTrace.beforeTerminal'))
 assert.ok(generated.source.indexOf('await configuration.confirmationTrace.beforeTerminal')<generated.source.indexOf('context.terminalWindow={'))
 assert.ok(generated.source.includes('context.confirmationTrace?.sealRequest(room,requestId'))
 const file=path.join(cache,'actual-full-generated-core.mjs');fs.writeFileSync(file,generated.source,{flag:'wx'})
 fullCore=await import(pathToFileURL(file).href);await fullCore.initializeNormalTestrunProtocol(binding)
 ;({LoadV2Markers}=await import('file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/load-r7-markers.mjs'))
 details.fullGeneratedSourceSha256=generated.evidence.traceSourceSha256
})
if(fullCore)await check('actual full WsClient: private slot29 setup seal, action seal, passive heartbeat, terminal seal and normal leave survive capture phase',async()=>{
 const fixture=memoryFixture({full:true,roomSlot:29});try{
  const room=await fixture.start()
  for(const [stage,key] of [['payload-preparation','setup'],['rooms-30','actions']]){
   fixture.metrics.currentStage=stage;room.lastCommandAt=0
   await fullCore.payloadFloorTestHooks.sendAuthoritativeCommand(room,room.host,structuredClone(room.host.envelope.state),{command:{type:'endTurn'},revisionKey:key},Date.now()+5000,fixture.context,
    {recordCommandSkip(){assert.fail('unexpected skip')},recordCommand(){}})
  }
  fixture.observer.setPhase('passive');const before=fixture.wire.length;fixture.sockets[0]&&room.host.send({type:'ping'});await Promise.resolve()
  assert.equal(fixture.wire.length,before+1,'full passive phase must not intercept the real heartbeat')
  fixture.observer.setPhase('capture');fixture.metrics.currentStage='cleanup';fixture.metrics.burstObserver.begin(performance.now())
  const cleanup={compliantSurrenders:0,roomsLeft:0,roomErrors:[]}
  await fullCore.workloadTestHooks.cleanupRoom(room,fixture.context,cleanup,29)
  assert.deepEqual(cleanup,{compliantSurrenders:1,roomsLeft:1,roomErrors:[]})
  const report=fixture.observer.report();assert.equal(report.commands.length,3);assert.ok(report.commands.every(row=>row.originalOutcome==='accepted'&&row.exactActorAck))
  assert.deepEqual(report.commands.map(row=>row.stage),['payload-preparation','rooms-30','cleanup']);assert.equal(report.commands.at(-1).type,'surrender')
  assert.equal(report.blockedPassiveSends,0);assert.equal(fixture.originalMarkers.report().begun_samples,1)
  assert.equal(fixture.originalMarkers.report().client.rows.length,1);assert.equal(fixture.originalMarkers.report().client.rows[0].slot,5)
  await fixture.observer.withPrivateRequests(async requests=>{assert.equal(requests.length,3);assert.ok(requests.every(row=>row.room===29&&row.playerIndex===1&&row.account0==='GUEST-ACCOUNT-SECRET-CANARY'))})
  const publicText=JSON.stringify(report);for(const secret of ['SECRET-CANARY',room.expectedMatchId,room.expectedRoomCode])assert.ok(!publicText.includes(secret))
  details.fullActualClient={commands:3,setup:1,actions:1,terminal:1,ordinaryRoomLeft:1,blockedHeartbeats:0}
 }finally{fixture.dispose()}
})
if(fullCore)await check('six original LoadV2Markers slots remain actual and complete with the full observer',async()=>{
 const original=new LoadV2Markers({nonce:'b'.repeat(32)})
 for(const slot of [0,5,11,17,23,29]){
  const fixture=memoryFixture({full:true,roomSlot:slot,sharedMarkers:original});try{
   const room=await fixture.start();fixture.metrics.currentStage='cleanup';fixture.metrics.burstObserver.begin(performance.now())
   const cleanup={compliantSurrenders:0,roomsLeft:0,roomErrors:[]};await fullCore.workloadTestHooks.cleanupRoom(room,fixture.context,cleanup,slot)
   assert.equal(cleanup.roomsLeft,1);assert.deepEqual(cleanup.roomErrors,[])
  }finally{fixture.dispose()}
 }
 const report=original.report();assert.equal(report.expected_samples,6);assert.equal(report.begun_samples,6);assert.equal(report.client_complete,1)
 assert.deepEqual(report.client.rows.map(row=>row.slot),[0,1,2,3,4,5]);details.originalSixMarkers=report
})
if(fullCore)await check('actual unchanged default generated core is a RED control for full private binding/outcome sealing',async()=>{
 const fixture=memoryFixture({full:true,sourceOverride:core});try{
  const room=await fixture.start();let accepted=0
  await core.payloadFloorTestHooks.sendAuthoritativeCommand(room,room.host,structuredClone(room.host.envelope.state),{command:{type:'endTurn'},revisionKey:'old-generation-control'},Date.now()+5000,fixture.context,
   {recordCommandSkip(){assert.fail()},recordCommand(_type,outcome){if(outcome==='accepted')accepted++}})
  assert.equal(accepted,1);assert.equal(fixture.observer.report().commands.length,0)
  details.actualGeneratedRedControl={defaultCoreCommandAccepted:1,requiredFullTraceCommands:1,actualFullTraceCommands:0,missingPrivateBindingAndSeal:true}
 }finally{fixture.dispose()}
})
if(fullCore)await check('full native timeout remains timed-out after late ACK; capture/passive does not block ordinary cleanup',async()=>{
 const fixture=memoryFixture({full:true,respond:false});try{
  const room=await fixture.start()
  await fullCore.payloadFloorTestHooks.sendAuthoritativeCommand(room,room.host,structuredClone(room.host.envelope.state),{command:{type:'endTurn'},revisionKey:'full-timeout'},Date.now()+6000,fixture.context,
   {recordCommandSkip(){assert.fail()},recordCommand(){}})
  fixture.observer.setPhase('passive');const sent=fixture.sockets[0].sent.find(row=>row.type==='gameAction')
  fixture.sockets[1].emit({type:'gameState',state:fixture.state(fixture.sockets[1],2)})
  fixture.sockets[0].emit({type:'gameState',requestId:sent.requestId,state:fixture.state(fixture.sockets[0],2)})
  assert.equal(fixture.observer.report().commands[0].originalOutcome,'timed-out');assert.equal(fixture.observer.report().commands[0].ackAfterOriginal3s,true)
  room.ended=true;fixture.observer.setPhase('capture');fixture.metrics.currentStage='cleanup';fixture.metrics.burstObserver.begin(performance.now())
  const cleanup={compliantSurrenders:0,roomsLeft:0,roomErrors:[]};await fullCore.workloadTestHooks.cleanupRoom(room,fixture.context,cleanup,0)
  assert.equal(cleanup.roomsLeft,1);assert.deepEqual(cleanup.roomErrors,[])
 }finally{fixture.dispose()}
})
await check('failed setup tail is frozen-only, <=10s and once; successful stages introduce zero waits',async()=>{
 let clock=0,waits=0
 const observer=createConfirmationObserver({profile:confirmation.FULL_TRACE_PROFILE.name,confirmationForRequest:()=>null,now:()=>clock})
 const input={observer,deadlineMs:900000,workersFrozen:true,assertFresh(){},now:()=>clock,wait:async ms=>{clock+=ms;waits++}}
 const success=await confirmation.observeFailedSetupTail({...input,failed:false});assert.equal(success.observed,false);assert.equal(waits,0)
 await assert.rejects(confirmation.observeFailedSetupTail({...input,failed:true,workersFrozen:false}),/TRACE_WORKERS_MUST_BE_FROZEN/)
 const failure=await confirmation.observeFailedSetupTail({...input,failed:true});assert.equal(failure.durationMs,10000);assert.equal(observer.report().passiveTail.durationMs,10000)
 await assert.rejects(confirmation.observeFailedSetupTail({...input,failed:true}),/TRACE_FAILURE_TAIL_ALREADY_OBSERVED/)
 await assert.rejects(confirmation.observeFailedSetupTail({...input,observer:createConfirmationObserver({profile:confirmation.FULL_TRACE_PROFILE.name,confirmationForRequest:()=>null}),deadlineMs:clock+180000,failed:true}),/TRACE_RECOVERY_RESERVE_REQUIRED/)
})
await check('full Journal uses private 160-row batches, zero raw IDs in proof and a shared 30s deadline outside 180s recovery',async()=>{
 let clock=0,calls=0,erased=0
 const requests=Array.from({length:161},(_,ordinal)=>({ordinal,room:ordinal%30,matchId:'PRIVATE-MATCH',account0:'PRIVATE-ACCOUNT0',account1:'PRIVATE-ACCOUNT1',playerIndex:0,requestId:'PRIVATE-REQUEST-'+ordinal}))
 const observer={report:()=>({commands:requests.map(row=>({ordinal:row.ordinal,requestHash:'A'.repeat(64)}))}),async withPrivateRequests(consume){const copy=structuredClone(requests);try{return await consume(copy)}finally{copy.forEach(row=>{row.matchId='';row.account0='';row.account1='';row.requestId=''});erased++}}}
 const budget={remainingMs:30000},options={observer,binding,deadlineMs:900000,budget,now:()=>clock,assertFresh(){},remote:async(code,parameters,{timeoutMs})=>{
  calls++;assert.equal(code,confirmation.FULL_OWNED_JOURNAL_CODE);assert.equal(parameters.profile,confirmation.FULL_TRACE_PROFILE.name);assert.ok(parameters.requests.length<=160)
  assert.ok(Buffer.byteLength(JSON.stringify(parameters))<524288);assert.ok(timeoutMs<=15000);clock+=5000
  return {readOnly:true,capturedBeforeAnonymization:true,rows:parameters.requests.map(row=>({ordinal:row.ordinal,room:row.room,ownedScopeVerified:true,present:false}))}
 }}
 const journal=await confirmation.readFullOwnedJournal(options);assert.equal(journal.rows.length,161);assert.equal(calls,2);assert.equal(budget.remainingMs,20000);assert.equal(erased,1)
 assert.ok(!JSON.stringify(journal).includes('PRIVATE-'))
 await confirmation.readFullOwnedJournal(options);assert.equal(budget.remainingMs,10000)
 await confirmation.readFullOwnedJournal(options);assert.equal(budget.remainingMs,0)
 await assert.rejects(confirmation.readFullOwnedJournal(options),/TRACE_RECOVERY_RESERVE_REQUIRED/);assert.equal(calls,6)
 await assert.rejects(confirmation.readFullOwnedJournal({...options,budget:{remainingMs:30000},deadlineMs:clock+180500}),/TRACE_RECOVERY_RESERVE_REQUIRED/)
 details.fullJournalBatches={rows:161,batchMaximum:160,aggregateBudgetMs:30000,rawIdentifiers:0}
})
await check('full Journal malformed ordinals, RPC exception and total deadline fail closed without changing original results',async()=>{
 let erased=0,clock=0
 const observer={report:()=>({commands:[{ordinal:0,requestHash:'B'.repeat(64),originalOutcome:'timed-out'}]}),async withPrivateRequests(consume){try{return await consume([{ordinal:0,room:0,matchId:'private',account0:'private0',account1:'private1',playerIndex:0,requestId:'private'}])}finally{erased++}}}
 const base={observer,binding,deadlineMs:900000,now:()=>clock,assertFresh(){}}
 await assert.rejects(confirmation.readFullOwnedJournal({...base,remote:async()=>({readOnly:true,capturedBeforeAnonymization:true,rows:[{ordinal:1}]})}),/TRACE_JOURNAL_ORDINAL_UNBOUND/)
 await assert.rejects(confirmation.readFullOwnedJournal({...base,remote:async()=>{throw new Error('SYNTHETIC_RPC_FAILURE')}}),/SYNTHETIC_RPC_FAILURE/)
 await assert.rejects(confirmation.readFullOwnedJournal({...base,remote:async()=>{clock+=30001;return {readOnly:true,capturedBeforeAnonymization:true,rows:[{ordinal:0}]}}}),/TRACE_CAPTURE_TOTAL_DEADLINE/)
 assert.equal(erased,3);assert.equal(observer.report().commands[0].originalOutcome,'timed-out')
})
// These execute the real generated runPressure, including its catch/finally and
// all original workers. No original function body is replaced by the fixture.
// Tokens, HTTP responses and native WebSockets below are in-memory only. The
// synthetic padding solely reaches the original worker branch, not capacity.
if(fullCore)for(const mode of ['prepareWorkloads','create','join','ready','stage-worker'])await check('actual generated runPressure '+mode+' failure runs the frozen-worker failure tail/capture without upgrading original result',async()=>{
 const sockets=[],rooms=[],observed=[],events=[];let httpCalls=0,registrationCalls=0,captures=0,clock=Date.now(),metricsSeen
 const syntheticAdmin='c'.repeat(64),revokedTokens=new Set()
 const observer=createConfirmationObserver({profile:confirmation.FULL_TRACE_PROFILE.name,confirmationForRequest:fullCore.payloadFloorTestHooks.confirmationForRequest})
 const reply=(body,status=200)=>new Response(JSON.stringify(body),{status,headers:{'content-type':'application/json'}})
 const savedFetch=globalThis.fetch,savedSocket=globalThis.WebSocket
 globalThis.fetch=async(input,options={})=>{
  const url=new URL(String(input));assert.equal(url.origin,'https://legion-12.com');assert.ok(url.pathname.startsWith('/testrun/'));httpCalls++
  const route=url.pathname.slice('/testrun/'.length)
  const token=String(options.headers?.Authorization??'').replace('Bearer ','')
  if(route==='health')return reply({status:'ok',serverVersion:binding.test.commit,maintenance:false})
  if(route==='api/auth/me')return revokedTokens.has(token)?reply({},401):reply({id:token})
  if(route==='api/tournaments')return reply({items:[]},mode==='prepareWorkloads'?503:200)
  if(route==='api/auth/register'){registrationCalls++;assert.fail('fixture must not register accounts')}
  if(route==='api/auth/sessions/current'){if(options.method==='DELETE')revokedTokens.add(token);return reply({})}
  if(['api/admin/analytics/cards','api/admin/v1/audit'].includes(route)){assert.equal(mode,'stage-worker');assert.equal(token,syntheticAdmin);return reply({items:[{}]})}
  if(/^api\/(?:decks\/summaries|ranked\/me|rankings|tournaments\/summaries|public-decks|matches)$/.test(route))return reply({items:[]})
  assert.fail('Unexpected synthetic HTTP route')
 }
 const state=(socket,phase='Main')=>({matchId:socket.room.matchId,roomCode:socket.room.code,you:socket.playerIndex,revision:socket.room.revision,phase,
  players:[0,1].map(playerIndex=>({playerIndex,hand:[],field:[[null]],mulliganDone:true})),activePlayer:0,round:socket.room.revision,
  prompts:socket.room.badPrompt?[{promptId:'synthetic-unsupported',playerIndex:0,validChoices:[],minChoose:0,maxChoose:0,data:{placementMode:'unsupported-fixture-only'}}]:[],
  syntheticFixturePadding:'x'.repeat(185000)})
 class FailureSocket extends EventTarget {
  static OPEN=1;static CLOSING=2;readyState=1
  constructor(url){super();assert.equal(url.href,binding.wsUrl);this.playerIndex=sockets.length%2
   if(this.playerIndex===0)rooms.push({code:'SYNTHETIC-'+rooms.length,matchId:'SYNTHETIC-MATCH-'+rooms.length,revision:0,peers:[],started:false,badPrompt:false})
   this.room=rooms.at(-1);this.room.peers.push(this);this.ready=false;sockets.push(this);queueMicrotask(()=>this.dispatchEvent(new Event('open')))}
  emit(message){this.dispatchEvent(Object.assign(new Event('message'),{data:JSON.stringify(message)}))}
  send(raw){const message=JSON.parse(raw)
   if(message.type==='hello')queueMicrotask(()=>{this.emit({type:'session',capabilities:message.capabilities});this.emit({type:'recoveryComplete'})})
   else if((mode==='create'&&message.type==='createRoom')||(mode==='join'&&message.type==='joinRoom')||(mode==='ready'&&message.type==='ready'))queueMicrotask(()=>this.close())
   else if(['createRoom','joinRoom','selectDeck'].includes(message.type))queueMicrotask(()=>this.emit({type:'roomState',roomCode:this.room.code,yourPlayerIndex:this.playerIndex}))
   else if(message.type==='ready'){this.ready=true;if(this.room.peers.every(peer=>peer.ready)&&!this.room.started){this.room.started=true;queueMicrotask(()=>this.room.peers.forEach(peer=>peer.emit({type:'gameState',state:state(peer)})))}}
   else if(message.type==='gameAction')queueMicrotask(()=>{this.room.revision++;this.room.peers.filter(peer=>peer!==this).forEach(peer=>peer.emit({type:'gameState',state:state(peer,message.command.type==='surrender'?'GameOver':'Main')}));this.emit({type:'gameState',requestId:message.requestId,state:state(this,message.command.type==='surrender'?'GameOver':'Main')})})
   else if(message.type==='ping')queueMicrotask(()=>{if(mode==='stage-worker'&&metricsSeen.currentStage==='payload-preparation'){this.room.badPrompt=true;this.room.peers.forEach(peer=>peer.emit({type:'gameState',state:state(peer)}))};this.emit({type:'pong'})})
   else if(message.type==='leaveRoom')queueMicrotask(()=>this.emit({type:'roomLeft'}))
  }
  close(){if(this.readyState===3)return;this.readyState=3;this.dispatchEvent(new Event('close'))}
 }
 globalThis.WebSocket=FailureSocket
 try{
  const result=await fullCore.workloadTestHooks.runPressure({local:false,baseUrl:binding.baseUrl,expectedCommit:binding.test.commit,
   credentials:{players:Array.from({length:69},(_,index)=>({token:'synthetic-player-'+index})),admin:mode==='stage-worker'?{token:syntheticAdmin}:null,tournamentReader:null},provisionPlayers:false,
   diagnosticNonce:'d'.repeat(32),runId:'e'.repeat(32),stageSeconds:60,httpIntervalMs:2000,wsIntervalMs:2000,requestTimeoutMs:10000,
   measurementCheckpointPath:path.join(output,'actual-'+mode+'-measurement.json'),executionDeadlineMs:Date.now()+900000,assertNormalExecution:async()=>{},progressFn:()=>{},
   onBeforeAccountTeardown:async()=>{events.push('before-account-teardown')},
   confirmationTrace:{compose(original,metrics){metricsSeen=metrics;observer.setStageReader(()=>metrics.currentStage);return confirmation.composeConfirmationMarkers(original,observer)},bindRoom:observer.bindRoom,sealRequest:observer.sealRequest,sealTerminal:observer.sealTerminal,
    async beforeTerminal(info){observed.push({...info});events.push('workers-frozen');const tail=await confirmation.observeFailedSetupTail({observer,...info,assertFresh(){},now:()=>clock,wait:async ms=>{clock+=ms}});events.push('tail-finished')
     if(info.failed){captures++;await observer.withPrivateRequests(async requests=>{events.push('owned-request-scope-captured');assert.ok(requests.every(row=>row.room<30))})}
     observed.at(-1).tail=tail
    }},
  })
  assert.equal(observed.length,1);assert.equal(observed[0].workersFrozen,true)
  const expectedFailure=mode==='prepareWorkloads'?'tournament-list-preflight-failed':mode==='stage-worker'?'unsupported-authoritative-prompt':'websocket-closed'
  assert.equal(result.failure,expectedFailure,mode+' must reach its intended original failure branch')
  assert.equal(observed[0].originalFailure,result.failure);assert.equal(observed[0].originalStopReason,result.stopReason)
  assert.equal(observed[0].stage,mode==='prepareWorkloads'?'setup':mode==='stage-worker'?'rooms-10':'setup-10')
  assert.equal(observed[0].failed,true,mode+' must bind the actual original failure, not payloadPreparation only')
  assert.equal(observed[0].tail.observed,true);assert.equal(observed[0].tail.durationMs,10000);assert.equal(captures,1)
  assert.notEqual(result.status,'completed');assert.ok(result.failure||result.stopReason);assert.equal(registrationCalls,0)
  assert.ok(sockets.every(socket=>socket.readyState===3));assert.deepEqual(events,['workers-frozen','tail-finished','owned-request-scope-captured',...(mode==='stage-worker'?['before-account-teardown']:[])])
  if(mode==='stage-worker'){assert.ok(revokedTokens.has(syntheticAdmin));assert.equal(result.cleanup.sessionsRevoked,1)}
  details.actualRunPressureFailures??=[];details.actualRunPressureFailures.push({mode,failure:result.failure,stopReason:result.stopReason,stage:observed[0].stage??null,tailMs:observed[0].tail.durationMs,tailClock:'injected virtual clock; original runPressure workers/catch/finally execute unchanged',captures,httpCalls,sockets:sockets.length,registrationCalls,syntheticPaddingDoesNotQualifyPayloadFloor:true})
 }finally{for(const socket of sockets)socket.close();globalThis.fetch=savedFetch;globalThis.WebSocket=savedSocket}
})
if(fullCore)for(const first of ['timed-out','rejected'])for(const cancellation of ['stage','hard'])await check('full '+first+' is not a causal abort for a later original '+cancellation+' cancellation',async()=>{
 const fixture=memoryFixture({full:true,respond:first==='rejected'?'reject':false});try{
  const room=await fixture.start(),sink={recordCommandSkip(){assert.fail()},recordCommand(){}}
  await fullCore.payloadFloorTestHooks.sendAuthoritativeCommand(room,room.host,structuredClone(room.host.envelope.state),{command:{type:'endTurn'},revisionKey:'first-'+first},Date.now()+6000,fixture.context,sink)
  assert.equal(fixture.observer.report().commands[0].originalOutcome,first);fixture.setResponse(false);room.lastCommandAt=0
  const hard=cancellation==='hard',timer=hard?setTimeout(()=>fixture.controller.abort(new DOMException('hard-deadline','AbortError')),5):null
  try{await fullCore.payloadFloorTestHooks.sendAuthoritativeCommand(room,room.host,structuredClone(room.host.envelope.state),{command:{type:'endTurn'},revisionKey:'later-cancel'},Date.now()+(hard?6000:400),fixture.context,sink)}finally{clearTimeout(timer)}
  const later=fixture.observer.report().commands[1];assert.equal(later.originalOutcome,'cancelled')
  assert.equal(later.cancelSource,hard?'original-hard-stop':'original-stage-deadline');assert.equal(later.workerStopCode,hard?'hard-deadline':null)
  assert.equal(fixture.observer.report().stopSource,'none','a first nonaccepted command did not abort the full workload')
 }finally{fixture.dispose()}
})
await check('real SQLite full room29/global ordinal160 read is scoped; short default still rejects it',()=>{
 const code=String.raw`import json,sqlite3,sys
p=json.load(sys.stdin);exec(p['query']);c=sqlite3.connect(':memory:')
c.executescript('CREATE TABLE matches(match_id TEXT,account_0 TEXT,account_1 TEXT,identity_scrubbed INTEGER);CREATE TABLE match_action_requests(match_id TEXT,player_index INTEGER,request_id TEXT,command_sequence INTEGER,accepted INTEGER,revision INTEGER,state_hash TEXT,created_utc TEXT);CREATE TABLE match_events(match_id TEXT,sequence INTEGER,player_index INTEGER,request_id TEXT,accepted INTEGER,revision INTEGER,state_hash TEXT);')
c.execute('INSERT INTO matches VALUES(?,?,?,0)',('own-match','own-acct0','own-acct1'));c.commit();c.execute('PRAGMA query_only=ON')
r={'ordinal':160,'room':29,'matchId':'own-match','account0':'own-acct0','account1':'own-acct1','playerIndex':1,'requestId':'request160'}
assert read_owned_confirmation_rows(c,[r],'representative-30-plus-10-f737')[0]['present'] is False
try:read_owned_confirmation_rows(c,[r])
except AssertionError:pass
else:raise AssertionError('SHORT_SCOPE_WIDENED')
for key,value in [('room',30),('ordinal',10470),('account1','foreign-account'),('matchId','foreign-match')]:
 bad=dict(r);bad[key]=value
 try:read_owned_confirmation_rows(c,[bad],'representative-30-plus-10-f737')
 except AssertionError:pass
 else:raise AssertionError('FULL_SCOPE_WIDENED')
for batch,profile in [([r]*161,'representative-30-plus-10-f737'),([r,r],'representative-30-plus-10-f737'),([r],'unbounded')]:
 try:read_owned_confirmation_rows(c,batch,profile)
 except AssertionError:pass
 else:raise AssertionError('FULL_BATCH_SCOPE_WIDENED')
print(json.dumps({'passed':True,'writes':c.total_changes-1}))`
 const result=JSON.parse(execFileSync(python,['-I','-B','-c',code],{input:JSON.stringify({query:OWNED_JOURNAL_QUERY}),encoding:'utf8',windowsHide:true,timeout:15000,maxBuffer:4096}))
 assert.equal(result.passed,true);assert.equal(result.writes,0)
})
await check('offline network prohibition and final limits remain explicit',()=>{assert.equal(networkCalls,0);assert.equal(TRACE_LIMITS.confirmationMs,3000);assert.equal(TRACE_LIMITS.cadenceMs,2000);assert.equal(TRACE_LIMITS.passiveTailMs,10000);assert.equal(TRACE_LIMITS.wallMs,900000)})
globalThis.fetch=originalFetch;globalThis.WebSocket=originalWebSocket
const report={schema:1,suite:'owned command confirmation trace offline',networkCalls,onlineExecuted:false,onlineCapacityAccepted:false,
 sources:['testrun-confirmation-trace.mjs','run-testrun-confirmation-trace.mjs','test-testrun-confirmation-trace.mjs','testrun-pressure-execution.mjs','run-testrun-representative-pressure.mjs'].map(file=>({file,sha256:digest(fs.readFileSync(path.join(import.meta.dirname,file)))})),
 details,passed:cases.filter(row=>row.passed).length,failed:cases.filter(row=>!row.passed).length,cases}
durableJson(path.join(output,'confirmation-trace-tests.json'),report);console.log(JSON.stringify(report,null,2));if(report.failed)process.exitCode=1
