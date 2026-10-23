import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {spawn} from 'node:child_process'
import {EVIDENCE_ROOT,CACHE_ROOT,assertGeneratedDirectories} from './testrun-pressure-protocol.mjs'
import {terminalDiagnosticProfile,assertTerminalGuardReady,assertTerminalGuardSample,assertGuardReady,
 startTerminalGuard,startNormalGuard} from './testrun-pressure-runtime.mjs'
const run=crypto.randomBytes(16).toString('hex'),output=path.join(EVIDENCE_ROOT,'terminal-runtime-'+run),cache=path.join(CACHE_ROOT,'terminal-runtime-'+run)
assertGeneratedDirectories(output,cache);fs.mkdirSync(output);fs.mkdirSync(cache)
const setting='terminal-recipients-v4:30:'+'2'.repeat(32)
const binding={test:{pid:101,commit:'a'.repeat(40),dllSha256:'B'.repeat(64)},formal:{pid:102,commit:'c'.repeat(40),dllSha256:'D'.repeat(64)}}
const installed={root:'/tmp/l12-terminal-pressure-'+run,sourceSha256:'E'.repeat(64)}
const ready={schema:2,kind:'terminal-recipient-guard-ready',selfTestOnly:false,diagnosticOnly:true,onlineCapacityAccepted:false,
 supervisorPid:103,processStartTicks:'123',bootId:'12345678-1234-1234-1234-123456789abc',hostname:'ser550468033481',sourceSha256:installed.sourceSha256,
 profileSha256:terminalDiagnosticProfile(setting).sha256,testPid:101,formalPid:102,testCommit:binding.test.commit,formalCommit:binding.formal.commit,testDllSha256:binding.test.dllSha256,formalDllSha256:binding.formal.dllSha256}
const sample=()=>({schema:1,selfTestOnly:false,diagnosticOnly:true,onlineCapacityAccepted:false,readOnly:true,stop:false,phase:'complete',
 testPid:101,formalPid:102,roundElapsedMs:3,testMemoryBytes:200*1024**2,hostAvailableBytes:1024**3,oomDelta:0,restartDelta:0,observedUtcMs:Date.now()})
let cases=0
assertTerminalGuardReady(ready,binding,installed.sourceSha256,setting);cases++
assert.throws(()=>assertGuardReady(ready,binding,installed.sourceSha256));cases++
for(const [key,value]of[['kind','normal-guard-ready'],['diagnosticOnly',false],['onlineCapacityAccepted',true],['profileSha256','F'.repeat(64)],['testPid',102]]) {
 assert.throws(()=>assertTerminalGuardReady({...ready,[key]:value},binding,installed.sourceSha256,setting));cases++
}
for(const [key,value]of[['diagnosticOnly',false],['onlineCapacityAccepted',true],['testMemoryBytes',850*1024**2],['oomDelta',1],['testPid',102]]) {
 assert.throws(()=>assertTerminalGuardSample({...sample(),[key]:value},binding,null));cases++
}
const fixture=String.raw`const p=JSON.parse(Buffer.from(process.argv[1],'base64').toString());const emit=x=>process.stdout.write(JSON.stringify(x)+'\n');emit(p.ready);emit({...p.sample,observedUtcMs:Date.now()});process.stdin.on('data',()=>{emit({...p.ready,kind:'terminal-recipient-guard-completion',reason:'SIGTERM_REQUESTED',allOwnedChildrenReaped:true});process.exit(143)});`
let started=0,closed=0
for(const mode of ['diagnostic-good','diagnostic-wrong-profile','diagnostic-missing-tag','normal-rejects-diagnostic']) {
 let child
 const payload={ready:{...ready},sample:sample()}
 if(mode==='diagnostic-wrong-profile')payload.ready.profileSha256='F'.repeat(64)
 if(mode==='diagnostic-missing-tag')payload.sample.diagnosticOnly=false
 const options={diagnosticSetting:setting,stopFile:path.join(cache,mode+'-stop.json'),logPath:path.join(output,mode+'.jsonl'),
  processFactory:(_exe,args)=>{
   const command=args.at(-1)
   assert.ok(command.includes(mode.startsWith('normal')?'normal-guard.py':'terminal-guard.py'))
   child=spawn(process.execPath,['-e',fixture,Buffer.from(JSON.stringify(payload)).toString('base64')],{windowsHide:true,stdio:['pipe','pipe','pipe']})
   started++;child.once('close',()=>closed++);return child
  },signalOwned:async()=>child.stdin.end('stop')}
 const watch=(mode.startsWith('normal')?startNormalGuard:startTerminalGuard)(binding,installed,options)
 if(mode==='diagnostic-good') {
  await watch.waitReady();watch.assertFresh();const result=await watch.finish()
  assert.equal(result.diagnosticOnly,true);assert.equal(result.onlineCapacityAccepted,false)
  assert.equal(result.stopRequested,false);assert.equal(result.transportExitCode,143)
 } else {
  await assert.rejects(()=>watch.waitReady());try{await watch.finish()}catch{}
  assert.ok(fs.existsSync(options.stopFile))
 }
 assert.ok(child.exitCode!==null||child.signalCode!==null);cases++
}
assert.equal(started,4);assert.equal(closed,4)
const result={schema:1,status:'passed',cases,actualOwnedChildCases:4,allOwnedChildrenClosed:true,
 normalRejectsDiagnostic:true,profileSha256:ready.profileSha256,networkCalls:0,linuxSignalsAndCgroupClaim:false,capacityClaim:false}
fs.writeFileSync(path.join(output,'terminal-runtime-tests.json'),JSON.stringify(result,null,2)+'\n',{flag:'wx'})
console.log(JSON.stringify({...result,output}))
