import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {spawn} from 'node:child_process'
import {REPO,EVIDENCE_ROOT,CACHE_ROOT,assertGeneratedDirectories,digest} from './testrun-pressure-protocol.mjs'
import {assertGuardReady,assertGuardSample,assertRuntimeIdentity,startNormalGuard,NORMAL_PROFILE_SHA,testRequest} from './testrun-pressure-runtime.mjs'
const runId=crypto.randomUUID().replaceAll('-',''),output=path.join(EVIDENCE_ROOT,'runtime-unit-'+runId),cache=path.join(CACHE_ROOT,'runtime-unit-'+runId)
assertGeneratedDirectories(output,cache);fs.mkdirSync(output,{recursive:true});fs.mkdirSync(cache,{recursive:true})
process.on('uncaughtExceptionMonitor',error=>fs.writeFileSync(path.join(output,'runtime-failure.json'),JSON.stringify({schema:1,status:'failed',name:error.name,message:error.message,realNetworkCalls:0}),{flag:'wx'}))
const binding={test:{pid:101,commit:'a'.repeat(40),dllSha256:'B'.repeat(64)},formal:{pid:102,commit:'c'.repeat(40),dllSha256:'D'.repeat(64)}}
const installed={root:'/tmp/l12-normal-pressure-'+runId,sourceSha256:'E'.repeat(64)}
const ready={schema:2,kind:'normal-guard-ready',selfTestOnly:false,supervisorPid:103,processStartTicks:'123',bootId:'12345678-1234-1234-1234-123456789abc',hostname:'ser550468033481',sourceSha256:installed.sourceSha256,profileSha256:NORMAL_PROFILE_SHA,testPid:101,formalPid:102,testCommit:binding.test.commit,formalCommit:binding.formal.commit,testDllSha256:binding.test.dllSha256,formalDllSha256:binding.formal.dllSha256}
const sample=()=>({schema:1,selfTestOnly:false,readOnly:true,stop:false,phase:'complete',testPid:101,formalPid:102,roundElapsedMs:3,testMemoryBytes:200*1024**2,hostAvailableBytes:1024**3,oomDelta:0,restartDelta:0,observedUtcMs:Date.now()})
const results=[];const check=(name,fn)=>{fn();results.push({name,status:'passed'})}
check('actual-readback-shape',()=>assertGuardReady(ready,binding,installed.sourceSha256))
for(const [name,mutate] of [['memory-mode',v=>v.selfTestOnly=true],['wrong-host',v=>v.hostname='other'],['service-is-not-supervisor',v=>v.supervisorPid=101],['source-sha',v=>v.sourceSha256='F'.repeat(64)],['profile-sha',v=>v.profileSha256='F'.repeat(64)],['formal-pid',v=>v.formalPid=104],['start-ticks',v=>v.processStartTicks='bad'],['boot-id',v=>v.bootId='bad']])check('ready-reject-'+name,()=>{const value=structuredClone(ready);mutate(value);assert.throws(()=>assertGuardReady(value,binding,installed.sourceSha256))})
check('healthy-sample',()=>assertGuardSample(sample(),binding,null))
for(const [name,mutate] of [['memory-limit',v=>v.testMemoryBytes=850*1024**2],['available-limit',v=>v.hostAvailableBytes=512*1024**2-1],['round-deadline',v=>v.roundElapsedMs=3750],['restart',v=>v.restartDelta=1],['oom',v=>v.oomDelta=1],['test-pid',v=>v.testPid=999],['formal-pid',v=>v.formalPid=999],['stale',v=>v.observedUtcMs-=5000],['fake-readonly',v=>v.readOnly=false],['selftest-row',v=>v.selfTestOnly=true],['stop',v=>v.stop=true]])check('sample-reject-'+name,()=>{const value=sample();mutate(value);assert.throws(()=>assertGuardSample(value,binding,null))})
const identity={...structuredClone(binding),resourcePolicy:{cpuWeight:10,ioWeight:10,nice:10,oomScoreAdjust:750,memoryHighBytes:768*1024**2,memoryMaxBytes:896*1024**2}}
for(const key of ['test','formal'])Object.assign(identity[key],{status:'ok',maintenance:false,diagnosticsOff:true,restartCount:0})
check('runtime-complete-identity',()=>assertRuntimeIdentity(identity,binding))
check('runtime-cannot-borrow-formal-pid',()=>{const bad=structuredClone(identity);bad.test.pid=102;assert.throws(()=>assertRuntimeIdentity(bad,binding))})
const originalFetch=globalThis.fetch;let fetchCalls=0
globalThis.fetch=async()=>{fetchCalls++;return new Response(JSON.stringify({status:'ok'}),{status:200})}
try{
 for(const [base,relative] of [['https://legion-12.com/','health'],['https://legion-12.com/testrun/','../api/auth/me'],['https://legion-12.com/testrun/','https://other/']]){await assert.rejects(()=>testRequest(base,relative));results.push({name:'http-reject-target-'+results.length,status:'passed'})}
 assert.equal(fetchCalls,0)
 assert.deepEqual(await testRequest('https://legion-12.com/testrun/','health'),{status:200,body:{status:'ok'}});results.push({name:'native-streamed-json-body',status:'passed'})
}finally{globalThis.fetch=originalFetch}
const fixture=String.raw`const p=JSON.parse(Buffer.from(process.argv[1],'base64').toString());const emit=x=>process.stdout.write(JSON.stringify(x)+'\n');emit(p.ready);emit({...p.sample,observedUtcMs:Date.now()});process.stdin.setEncoding('utf8');process.stdin.on('data',()=>{emit({...p.ready,kind:'normal-guard-completion',reason:'SIGTERM_REQUESTED',allOwnedChildrenReaped:true});process.exit(143)});`
let childrenStarted=0,childrenClosed=0
for(const fault of ['none','wrong-source','memory-limit']){
 let child
 const payload={ready:structuredClone(ready),sample:sample()}
 if(fault==='wrong-source')payload.ready.sourceSha256='F'.repeat(64)
 if(fault==='memory-limit')payload.sample.testMemoryBytes=850*1024**2
 const watch=startNormalGuard(binding,installed,{stopFile:path.join(cache,fault+'-stop.json'),logPath:path.join(output,fault+'-guard.jsonl'),
  processFactory:()=>{childrenStarted++;child=spawn(process.execPath,['-e',fixture,Buffer.from(JSON.stringify(payload)).toString('base64')],{windowsHide:true,stdio:['pipe','pipe','pipe']});child.once('close',()=>childrenClosed++);return child},
  signalOwned:async()=>child.stdin.end('controlled stop\n')})
 if(fault==='none'){
  await watch.waitReady();watch.assertFresh();const proof=await watch.finish();assert.equal(proof.stopRequested,false);assert.equal(proof.transportExitCode,143)
 }else{
  await assert.rejects(()=>watch.waitReady());try{await watch.finish()}catch{}
  assert.equal(fs.existsSync(path.join(cache,fault+'-stop.json')),true)
 }
 assert.ok(child.exitCode!==null||child.signalCode!==null);results.push({name:'actual-owned-child-'+fault,status:'passed'})
}
assert.equal(childrenStarted,3);assert.equal(childrenClosed,3)
const proof={schema:1,status:'passed',cases:results.length,results,actualOwnedChildCases:3,allOwnedChildrenClosed:true,realNetworkCalls:0,syntheticFetchCalls:fetchCalls,
 sources:['testrun-pressure-runtime.mjs','test-testrun-pressure-runtime.mjs'].map(file=>({file,sha256:digest(fs.readFileSync(path.join(import.meta.dirname,file)))})),
 boundary:'actual local child pipes and injected numeric rows only; not Linux signals, cgroup, real sessions or representative pressure acceptance'}
fs.writeFileSync(path.join(output,'runtime-tests.json'),JSON.stringify(proof,null,2),{flag:'wx'});console.log(JSON.stringify({status:proof.status,cases:proof.cases,output,realNetworkCalls:0}))
