import assert from 'node:assert/strict'
import fs from 'node:fs'
import {execFileSync,spawn} from 'node:child_process'
import {configureTerminalRuntime,terminalSetting,identityDerivation,assertTerminalIdentity,
 observeTerminalIdentity,installTerminalGuardFiles,removeTerminalGuardFiles,readTerminalServerRows,terminalJournalRemote,
 createTerminalAnalyticsPackage,prepareTerminalAnalytics} from './terminal-recipient-runtime.mjs'
import {bindOwnedControl} from './testrun-pressure-runtime.mjs'
import {openOwnedControl,openTerminalDiagnosticControl} from './testrun-pressure-control.mjs'
import {digest,REPO} from './testrun-pressure-protocol.mjs'
import {IDENTITY_CODE} from './testrun-pressure-runtime.mjs'
let cases=0
assert.throws(()=>terminalSetting());cases++
assert.throws(()=>configureTerminalRuntime('terminal-recipients-v4:1:'+'2'.repeat(32)));cases++
const setting='terminal-recipients-v4:30:'+'2'.repeat(32)
configureTerminalRuntime(setting);assert.equal(terminalSetting(),setting);cases++
assert.throws(()=>configureTerminalRuntime(setting));cases++
assert.equal(identityDerivation.reverseExact,true);assert.equal(identityDerivation.exactReplacements,2);cases++
const binding={test:{pid:101,commit:'a'.repeat(40),dllSha256:'B'.repeat(64)},formal:{pid:102,commit:'c'.repeat(40),dllSha256:'D'.repeat(64)}}
const analyticsBinding={
 test:{pid:201,commit:'2eda18c115b0810296cb9c35cfebf60c35b120e5',dllSha256:'908E146B9A92D52A0FA6E3B5CAD8260A07443515D82E35AA528204A41365321B'},
 formal:{pid:202,commit:'57ef89b3726f96c06ff1a44d1c073f525669ec6b',dllSha256:'D'.repeat(64)},
}
const identity={test:{...binding.test,status:'ok',maintenance:false,diagnosticsOff:false,diagnosticSetting:setting,restartCount:0},
 formal:{...binding.formal,status:'ok',maintenance:false,diagnosticsOff:true,diagnosticSetting:'',restartCount:0},
 resourcePolicy:{cpuWeight:10,ioWeight:10,nice:10,oomScoreAdjust:750,memoryHighBytes:768*1024**2,memoryMaxBytes:896*1024**2}}
assertTerminalIdentity(identity,binding);cases++
for(const [name,key,value]of[['test','diagnosticsOff',true],['test','diagnosticSetting','wrong'],['formal','diagnosticsOff',false],['formal','diagnosticSetting',setting],['test','pid',102]]) {
 const bad=structuredClone(identity);bad[name][key]=value;assert.throws(()=>assertTerminalIdentity(bad,binding));cases++
}
const captured=[]
const unbind=bindOwnedControl({call:async(code,parameters)=>{
 captured.push(code)
 execFileSync('python',['-B','-c',String.raw`import ast,re,sys
s=sys.stdin.read();tree=ast.parse(s)
for node in ast.walk(tree):
 if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='pattern' for t in node.targets):
  pattern=ast.literal_eval(node.value)
  assert re.search(pattern,'{"recipient":0,"schema":4,"kind":1,"slot":0}')
  assert re.search(pattern,'{"schema":4,"kind":2,"expected_commands":30}')
  assert not re.search(pattern,'{"schema":3,"kind":2}')
`],{input:code,encoding:'utf8',env:{...process.env,PYTHONUTF8:'1'},stdio:['pipe','pipe','pipe']})
 if(parameters.files)return {schema:1,root:'/tmp/l12-normal-pressure-'+parameters.runId,files:parameters.files.map(({name,sha256})=>({name,sha256}))}
 if(parameters.installed)return {ownedFilesRemoved:4,ownedDirectoryRemoved:true,serviceChanges:0}
 if(code.includes('journalctl'))return {schema:1,testPid:101,rows:[],numericOnly:true,otherApplicationLogsRead:false}
  if(parameters.templateBase64){
   const template=Buffer.from(parameters.templateBase64,'base64'),bindingValue=JSON.parse(parameters.bindingJson)
   assert.deepEqual(template,fs.readFileSync(REPO+'/ops/qa/testrun-owned-analytics-import.py'))
   assert.equal(digest(template),parameters.templateSourceSha256);assert.equal(digest(parameters.bindingJson),parameters.bindingSha256)
   assert.equal(bindingValue.templateSourceSha256,parameters.templateSourceSha256)
   assert.match(code,/--terminal-apply/);assert.doesNotMatch(code,/fixture-import-r2\/testrun-owned-analytics-import\.py/)
   return {schema:1,result:{status:'COMMITTED_VERIFIED',matches:16},diagnosticOnly:true,onlineCapacityAccepted:false,formalWrites:0}
  }
 return identity
}})
try {
 assertTerminalIdentity(await observeTerminalIdentity(binding),binding);cases++
 const installed=await installTerminalGuardFiles('1'.repeat(32),REPO+'/ops/qa/testrun-terminal-resource-guard.py',
  'D:/GPT/Legion12/artifacts/performance-measurement-recovery-20261020/guard-deadline-r4/resource-guard-deadline-r4.py')
 assert.equal(installed.files.length,4);cases++
 const removed=await removeTerminalGuardFiles(installed,{transportExitCode:143,completion:{allOwnedChildrenReaped:true},diagnosticOnly:true,onlineCapacityAccepted:false,ready:{}})
 assert.equal(removed.ownedFilesRemoved,4);cases++
 assert.equal((await readTerminalServerRows(binding)).numericOnly,true);cases++
 await assert.rejects(async()=>terminalJournalRemote('print(1)',binding),/EXACT_NORMAL_JOURNAL_IDENTITY_PREFIX_REQUIRED/);cases++
 await terminalJournalRemote(IDENTITY_CODE+'\nprint(json.dumps(result))',binding)
 assert.ok(captured.at(-1).startsWith((await import('./terminal-recipient-runtime.mjs')).TERMINAL_IDENTITY_CODE));cases++
 const readPaths=[]
 const packaged=createTerminalAnalyticsPackage(analyticsBinding,'4'.repeat(32),{read:(file,maximum)=>{
  const bytes=fs.readFileSync(file);assert.ok(bytes.length<=maximum);readPaths.push(file.replaceAll('\\','/'));return bytes
 }})
 assert.equal(packaged.evidence.repositoryTemplate,true);assert.equal(packaged.evidence.artifactSourceRead,false)
 assert.equal(readPaths.length,3);assert.ok(readPaths.some(file=>file.endsWith('/ops/qa/testrun-owned-analytics-import.py')))
 assert.ok(!readPaths.some(file=>file.endsWith('/fixture-import-r2/testrun-owned-analytics-import.py')));cases++
 const imported=await prepareTerminalAnalytics(analyticsBinding,'4'.repeat(32))
 assert.equal(imported.result.status,'COMMITTED_VERIFIED');assert.equal(imported.derivation.exactPolicyEdits,0)
 assert.equal(imported.derivation.repositoryTemplate,true);assert.equal(imported.derivation.artifactSourceRead,false);cases++
 const wrongAssembly=structuredClone(analyticsBinding);wrongAssembly.test.dllSha256='F'.repeat(64)
 assert.throws(()=>createTerminalAnalyticsPackage(wrongAssembly,'4'.repeat(32)));cases++
 assert.throws(()=>createTerminalAnalyticsPackage(analyticsBinding,'4'.repeat(32),{read:(file,maximum)=>{
  const bytes=fs.readFileSync(file);assert.ok(bytes.length<=maximum)
  return file.replaceAll('\\','/').endsWith('/fixture-generated.json')?Buffer.concat([bytes,Buffer.from('\n')]):bytes
 }}),/TERMINAL_ANALYTICS_FIXTURE_RECEIPT_CHANGED/);cases++
}finally{unbind()}
let starts=0
for(const invalid of ['',setting+':extra','terminal-recipients-v4:31:'+'2'.repeat(32)]) {
 assert.throws(()=>openTerminalDiagnosticControl({binding,diagnosticSetting:invalid,processFactory:()=>{starts++;throw new Error('NOT_ALLOWED')}}));cases++
}
assert.equal(starts,0)
const ready={schema:1,kind:'normal-control-ready',pid:103,startTicks:'123',bootId:'12345678-1234-1234-1234-123456789abc',hostname:'ser550468033481',uid:0}
const fixture=String.raw`const ready=JSON.parse(process.argv[1]);const emit=value=>process.stdout.write(JSON.stringify(value)+'\n');emit(ready);let buffer='';process.stdin.on('data',value=>{buffer+=value;while(buffer.includes('\n')){const at=buffer.indexOf('\n'),r=JSON.parse(buffer.slice(0,at));buffer=buffer.slice(at+1);if(r.kind==='close'){emit({...ready,kind:'normal-control-closed',id:r.id});process.exit(0)}else emit({schema:1,id:r.id,ok:true,result:r.parameters})}});`
for(const diagnostic of [false,true]) {
 let child
 const options={binding,diagnosticSetting:setting,processFactory:(_exe,args)=>{
  assert.ok(args.at(-1).startsWith('timeout '+(diagnostic?1400:940)+' '))
  return child=spawn(process.execPath,['-e',fixture,JSON.stringify(ready)],{windowsHide:true,stdio:['pipe','pipe','pipe']})
 }}
 const control=(diagnostic?openTerminalDiagnosticControl:openOwnedControl)(options)
 await control.waitReady();assert.deepEqual(await control.call('print(1)',{numeric:1}),{numeric:1})
 assert.equal((await control.close()).ownControlExited,true);assert.ok(child.exitCode!==null);cases++
}
console.log(JSON.stringify({status:'passed',cases,pythonRpcSourcesParsed:captured.length,numericJournalFilterMatched:true,
 actualOwnedControlChildren:2,normalOuterLeaseStill940:true,diagnosticOuterLease1400:true,realNetworkCalls:0,capacityClaim:false}))
