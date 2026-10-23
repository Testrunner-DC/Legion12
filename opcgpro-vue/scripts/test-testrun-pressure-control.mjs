import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {spawn} from 'node:child_process'
import {openOwnedControl,CONTROL_BOOTSTRAP} from './testrun-pressure-control.mjs'
import {bindOwnedControl,remoteJson,observeIdentity,assertRuntimeIdentity,durableJson} from './testrun-pressure-runtime.mjs'
import {EVIDENCE_ROOT,CACHE_ROOT,assertGeneratedDirectories,digest,validateBinding} from './testrun-pressure-protocol.mjs'
const id=crypto.randomUUID().replaceAll('-',''),output=path.join(EVIDENCE_ROOT,'control-'+id),cache=path.join(CACHE_ROOT,'control-'+id)
assertGeneratedDirectories(output,cache);fs.mkdirSync(output,{recursive:true});fs.mkdirSync(cache,{recursive:true})
process.env.TEMP=cache;process.env.TMP=cache;process.env.TMPDIR=cache
process.on('uncaughtExceptionMonitor',error=>durableJson(path.join(output,'control-test-failure.json'),{schema:1,status:'failed',name:error.name,code:error.code??null,realNetworkCalls:0,onlineCapacityAccepted:false}))
const selected=process.argv.slice(2)
if(selected.length){
 assert.equal(selected.length,5);assert.equal(selected[0],'--live-transport');assert.equal(selected[1],'--binding');assert.equal(selected[3],'--binding-sha')
 const bytes=fs.readFileSync(selected[2]);assert.equal(digest(bytes),selected[4]);const binding=validateBinding(JSON.parse(bytes))
 let release,control,exit,identity,failure=null
 try{control=openOwnedControl({binding});await control.waitReady();release=bindOwnedControl(control);identity=assertRuntimeIdentity(await observeIdentity(binding),binding)}catch(error){failure=/^[A-Z_]+$/.test(error.message)?error.message:'CONTROL_LIVE_QUALIFICATION_FAILED'}
 finally{release?.();if(control)try{exit=await control.close()}catch{failure??='CONTROL_LIVE_EXIT_UNPROVEN'}}
 const proof={schema:1,status:failure?'failed':'ACTUAL_PINNED_CONTROL_TRANSPORT_QUALIFIED',failure,bindingSha256:selected[4],identity,exit,sshConnections:1,authenticatedApplicationSessions:0,businessWrites:0,formalWrites:0,onlineCapacityAccepted:false,bootstrapSha256:digest(CONTROL_BOOTSTRAP),sources:['testrun-pressure-control.mjs','testrun-pressure-runtime.mjs','test-testrun-pressure-control.mjs'].map(file=>({file,sha256:digest(fs.readFileSync(path.join(import.meta.dirname,file)))}))}
 durableJson(path.join(output,'control-live-qualification.json'),proof);console.log(JSON.stringify({status:proof.status,output,failure,onlineCapacityAccepted:false}));if(failure)process.exitCode=2
}else{
 const binding={test:{pid:101},formal:{pid:102}},results=[];let starts=0,closes=0
 const ready={schema:1,kind:'normal-control-ready',pid:103,startTicks:'123',bootId:'12345678-1234-1234-1234-123456789abc',hostname:'ser550468033481',uid:0}
 const fixture=String.raw`const p=JSON.parse(Buffer.from(process.argv[1],'base64'));const emit=x=>process.stdout.write(JSON.stringify(x)+'\n');emit(p.mode==='bad-host'?{...p.ready,hostname:'other'}:p.ready);let buffer='';process.stdin.on('data',chunk=>{buffer+=chunk;while(buffer.includes('\n')){let at=buffer.indexOf('\n');const r=JSON.parse(buffer.slice(0,at));buffer=buffer.slice(at+1);if(r.kind==='close'){emit({...p.ready,kind:'normal-control-closed',id:r.id,...(p.mode==='wrong-close'?{pid:104}:{})});process.exit(0)}if(p.mode==='timeout')continue;if(p.mode==='wrong-id')emit({schema:1,id:'0'.repeat(32),ok:true,result:{}});else if(p.mode==='remote-error')emit({schema:1,id:r.id,ok:false,errorClass:'AssertionError'});else emit({schema:1,id:r.id,ok:true,result:r.parameters})}});process.stdin.on('end',()=>process.exit(0));`
 for(const mode of ['healthy','bad-host','wrong-id','wrong-close','timeout','remote-error']){
  let child;const control=openOwnedControl({binding,timeoutMs:1000,processFactory:()=>{starts++;child=spawn(process.execPath,['-e',fixture,Buffer.from(JSON.stringify({ready,mode})).toString('base64')],{windowsHide:true,stdio:['pipe','pipe','pipe']});child.once('close',()=>closes++);return child}})
  let release
  try{
   if(mode==='bad-host'){await assert.rejects(()=>control.waitReady());continue}
   await control.waitReady();release=bindOwnedControl(control)
   if(mode==='healthy'){
    assert.throws(()=>bindOwnedControl(control))
    for(let index=0;index<60;index++)assert.deepEqual(await remoteJson('print(1)',{index,privateFixture:'synthetic-only'}),{index,privateFixture:'synthetic-only'})
   }else if(mode==='wrong-id'||mode==='timeout'||mode==='remote-error')await assert.rejects(()=>remoteJson('print(1)',{index:1},{timeoutMs:1000}))
  }finally{
   release?.()
   if(mode==='healthy'||mode==='remote-error'){const proof=await control.close();assert.equal(proof.transportExitCode,0);assert.equal(proof.ownControlExited,true)}else await assert.rejects(()=>control.close())
   assert.ok(child.exitCode!==null||child.signalCode!==null);results.push({mode,status:'passed'})
  }
 }
 assert.equal(starts,6);assert.equal(closes,6)
 const proof={schema:1,status:'passed',cases:results.length,results,actualOwnedLocalChildren:6,allChildrenClosed:true,serializedCallsOnOneHealthyChild:60,realNetworkCalls:0,credentialsEmitted:false,onlineCapacityAccepted:false,sources:['testrun-pressure-control.mjs','testrun-pressure-runtime.mjs','test-testrun-pressure-control.mjs'].map(file=>({file,sha256:digest(fs.readFileSync(path.join(import.meta.dirname,file)))}))}
 durableJson(path.join(output,'control-tests.json'),proof);console.log(JSON.stringify({status:proof.status,cases:proof.cases,output,realNetworkCalls:0}))
}
