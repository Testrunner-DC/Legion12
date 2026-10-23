import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {pathToFileURL} from 'node:url'
import {createExecutableCore,assertPressureDisk,assertCapturedMeasurement,readPeripheralPreflight,createOwnedLedgerSinks} from './testrun-pressure-execution.mjs'
import {assertGeneratedDirectories,EVIDENCE_ROOT,CACHE_ROOT,digest} from './testrun-pressure-protocol.mjs'
import {createPressureMeasurementCheckpoint,persistPressureMeasurementCheckpoint} from '../../scripts/lib/l12-pressure-checkpoint.mjs'
import {readColdLedgersPrivately} from './testrun-pressure-recovery.mjs'
const runId=crypto.randomUUID().replaceAll('-',''),output=path.join(EVIDENCE_ROOT,'execution-unit-'+runId),cache=path.join(CACHE_ROOT,'execution-unit-'+runId)
assertGeneratedDirectories(output,cache);fs.mkdirSync(output,{recursive:true});fs.mkdirSync(cache,{recursive:true})
process.env.TEMP=cache;process.env.TMP=cache;process.env.TMPDIR=cache
process.on('uncaughtExceptionMonitor',error=>fs.writeFileSync(path.join(output,'execution-failure.json'),JSON.stringify({schema:1,status:'failed',name:error.name,code:error.code??null,realNetworkCalls:0}),{flag:'wx'}))
const originalFetch=globalThis.fetch;let networkCalls=0;globalThis.fetch=()=>{networkCalls++;throw new Error('NETWORK_FORBIDDEN')}
const results=[];const check=(name,fn)=>{fn();results.push({name,status:'passed'})};const asyncCheck=async(name,fn)=>{await fn();results.push({name,status:'passed'})}
const built=createExecutableCore(pathToFileURL(path.join(import.meta.dirname,'testrun-pressure-protocol.mjs')).href)
check('original-F737-reverse-closure-and-new-core-SHA',()=>{assert.equal(built.evidence.originalWorkloadUnchanged,true);assert.equal(built.evidence.deadlineOnlyShortened,true);assert.equal(digest(built.source),built.evidence.adaptedSha256);assert.equal(built.source.includes("requireContract(false,'normal_pressure_execution_not_admitted')"),false)})
const generated=path.join(cache,'normal-execution-offline.mjs');fs.writeFileSync(generated,built.source,{flag:'wx'})
const core=await import(pathToFileURL(generated).href)
await asyncCheck('missing-real-admission-rejected-before-network',()=>assert.rejects(()=>core.workloadTestHooks.runPressure({}),/normal_pressure_admission_required/))
await asyncCheck('failed-real-admission-rejected-before-network',()=>assert.rejects(()=>core.workloadTestHooks.runPressure({assertNormalExecution:async()=>{throw new Error('SYNTHETIC_ADMISSION_DENIED')}}),/SYNTHETIC_ADMISSION_DENIED/))
const validDisk={programBytes:1.5*1024**3,dataBytes:512*1024**2,temporaryPeakBytes:1024**3,totalPeakBytes:3*1024**3,formalFreeBytes:8*1024**3}
check('original-exact-disk-boundaries',()=>assertPressureDisk(validDisk))
for(const field of ['programBytes','dataBytes','temporaryPeakBytes','totalPeakBytes']){
 check('reject-one-byte-over-'+field,()=>assert.throws(()=>assertPressureDisk({...validDisk,[field]:validDisk[field]+1})))
 check('reject-nonnumeric-'+field,()=>assert.throws(()=>assertPressureDisk({...validDisk,[field]:String(validDisk[field])})))
}
check('formal-reserve-not-relaxed',()=>assert.throws(()=>assertPressureDisk({...validDisk,formalFreeBytes:8*1024**3-1})))
const checkpoint=createPressureMeasurementCheckpoint({candidate:'a'.repeat(40),coreSourceSha256:built.evidence.adaptedSha256,metrics:{elapsedMs:1,stages:[],terminalFrames:[],actionBounds:{},http:{},commands:{},websocket:{},payloadFloor:{},reconstructionErrors:0,presenceReconstructionErrors:0}})
const checkpointPath=path.join(output,'measurement-before-account-cleanup.json');persistPressureMeasurementCheckpoint(checkpointPath,checkpoint)
check('actual-durable-checkpoint-bound-to-candidate-and-generated-core',()=>assert.equal(assertCapturedMeasurement(checkpointPath,{candidate:'a'.repeat(40),coreSha256:built.evidence.adaptedSha256}).capturedBeforeTeardown,true))
check('wrong-checkpoint-candidate-rejected',()=>assert.throws(()=>assertCapturedMeasurement(checkpointPath,{candidate:'b'.repeat(40),coreSha256:built.evidence.adaptedSha256})))
check('wrong-checkpoint-core-rejected',()=>assert.throws(()=>assertCapturedMeasurement(checkpointPath,{candidate:'a'.repeat(40),coreSha256:'B'.repeat(64)})))
const fakeManager=changes=>({request:async(_handle,route)=>{
 if(route==='api/admin/storage/private-deck-persistence')return {body:{configuredEnabled:true,effectiveMode:'object',writable:true,...changes?.storage}}
 if(route==='api/admin/analytics/cards')return {body:{items:changes?.emptyAnalytics?[]:[{}]}}
 if(route==='api/admin/v1/audit?limit=50')return {body:[{}]}
 if(route==='api/tournaments')return {body:{items:[{id:'synthetic-tournament'}]}}
 if(route==='api/tournaments/synthetic-tournament')return {body:{id:'synthetic-tournament'}}
 throw new Error('UNEXPECTED_SYNTHETIC_ROUTE')
}})
await asyncCheck('all-three-representative-data-families-required',async()=>{const proof=await readPeripheralPreflight(fakeManager(),{});assert.equal(proof.analyticsItems,1);assert.equal(proof.auditItems,1);assert.equal(proof.tournamentItems,1)})
await asyncCheck('snapshot-F2-refused',()=>assert.rejects(()=>readPeripheralPreflight(fakeManager({storage:{effectiveMode:'snapshot'}}),{})))
await asyncCheck('empty-analytics-not-silently-skipped',()=>assert.rejects(()=>readPeripheralPreflight(fakeManager({emptyAnalytics:true}),{}),/NORMAL_PRESSURE_REQUIRED_DATA_ABSENT/))
const ledgerRoot=path.join(cache,runId);fs.mkdirSync(ledgerRoot)
const binding=JSON.parse(fs.readFileSync('D:/GPT/Legion12/artifacts/wire-json-release-20261022/normal-load-binding.json','utf8'))
const windowClock=Date.now()
const control={schema:1,kind:'normal-testrun-account-ledgers-v1',runId,baseUrl:binding.baseUrl,bindingSha256:'B'.repeat(64),test:Object.fromEntries(['pid','commit','dllSha256'].map(key=>[key,binding.test[key]])),formal:Object.fromEntries(['pid','commit','dllSha256'].map(key=>[key,binding.formal[key]])),registrationWindow:{openedUtc:new Date(windowClock).toISOString(),notAfterUtc:new Date(windowClock+900000).toISOString()},groups:[60,9].map((maxSlots,index)=>({index,maxSlots,runId:`normal-${runId}-${index}`,ledgerPath:path.join(ledgerRoot,['players60.dpapi','outer9.dpapi'][index])}))}
const controlPath=path.join(output,'ledger-control.json');fs.writeFileSync(controlPath,JSON.stringify(control),{flag:'wx'});const controlSha256=digest(fs.readFileSync(controlPath))
let sinks,exits,cold
try{
 sinks=await createOwnedLedgerSinks(control,{cache:ledgerRoot,output})
 const clientModule=await import('file:///C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/cohort30-terminal-v2/ledger-r5/account-ledger-client-r2.mjs')
 for(const index of [0,1]){
  await sinks.start(index);const username=`QActrl${index}`,password=crypto.randomBytes(24).toString('hex'),requestedAtUtc=new Date().toISOString()
  await sinks.clients[index].persistIntent({slot:0,username,password,requestedAtUtc,preflightAbsent:true,requestFingerprint:clientModule.registrationFingerprint(username,password)})
  await sinks.clients[index].persistConfirmed({slot:0,id:'synthetic-account-'+index,createdAtUtc:new Date().toISOString()});sinks.clients[index].close()
 }
 exits=await sinks.finish();assert.equal(exits.length,2);assert.ok(exits.every(item=>item.code===0&&item.errorBytes===0))
 cold=await readColdLedgersPrivately({controlPath,controlSha256,bindingSha256:control.bindingSha256,allowPartial:true,consume:envelope=>assert.equal(envelope.accounts.length,2)})
 assert.equal(cold.complete,false);assert.deepEqual(cold.groups.map(item=>[item.count,item.sequence,item.confirmed,item.wrongRunRejected]),[[1,2,1,true],[1,2,1,true]])
 results.push({name:'two-actual-frozen-pipes-DPAPI-cold-reader-and-normal-writer-exits',status:'passed'})
}finally{if(sinks)await sinks.finish();globalThis.fetch=originalFetch}
assert.equal(networkCalls,0)
const proof={schema:1,status:'passed',cases:results.length,results,realNetworkCalls:0,actualLocalLedgerSinks:2,ledgerCounts:[1,1],full69LedgerAcceptanceClaimed:false,cold,exits,sources:['testrun-pressure-execution.mjs','test-testrun-pressure-execution.mjs','run-testrun-representative-pressure.mjs'].map(file=>({file,sha256:digest(fs.readFileSync(path.join(import.meta.dirname,file)))})),onlineCapacityAccepted:false}
fs.writeFileSync(path.join(output,'execution-tests.json'),JSON.stringify(proof,null,2),{flag:'wx'});console.log(JSON.stringify({status:proof.status,cases:proof.cases,output,realNetworkCalls:0}))
