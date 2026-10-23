import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {spawn} from 'node:child_process'
import {strictSshArguments, digest, PLAN} from './testrun-pressure-protocol.mjs'

// Normal-service control only. This module never starts/restarts a service,
// changes a maintenance plan, or authenticates against the formal application.
const SSH='C:/Windows/System32/OpenSSH/ssh.exe'
export const HOST='ser550468033481'
export const FROZEN_GUARD_SHA='549BC59A31A3EDF836A045C5F915C9DB2E283B070150ABC7E369A01BBC8A2C1F'
export const NORMAL_PROFILE_SHA=digest(JSON.stringify({baseUrl:'https://legion-12.com/testrun',matchStorage:'ephemeral',privateDeckObjectPersistence:'true',diagnostics:'off'}))
export const pause=milliseconds=>new Promise(resolve=>setTimeout(resolve,milliseconds))
// Existing host SSH ingress rate limits blocked six control connections in the
// first live qualification. Pace this owned chain; never change the firewall.
export function createSshAdmission({now=()=>performance.now(),wait=pause}={}){
 let last=null,queue=Promise.resolve()
 return {
  noteStart(){last=now()},
  async take(){
   const previous=queue;let release;queue=new Promise(resolve=>release=resolve);await previous
   try{if(last!==null){const delay=last+8000-now();assert.ok(delay<=8000,'SSH_ADMISSION_CLOCK_INVALID');if(delay>0)await wait(delay)}last=now()}
   finally{release()}
  },
 }
}
const sshAdmission=createSshAdmission()
let ownedControl=null
export function bindOwnedControl(control){
 assert.equal(ownedControl,null);assert.ok(control&&typeof control.call==='function');ownedControl=control
 return ()=>{assert.equal(ownedControl,control);ownedControl=null}
}
async function settleWithin(promise,milliseconds){let timer;try{return await Promise.race([promise,new Promise(resolve=>{timer=setTimeout(()=>resolve(null),milliseconds)})])}finally{clearTimeout(timer)}}
const posix=value=>"'"+String(value).replaceAll("'","'\\''")+"'"
const hash=value=>assert.match(value,/^[0-9A-F]{64}$/)
const pid=value=>assert.ok(Number.isSafeInteger(value)&&value>0)
export function durableJson(file,value){
 const descriptor=fs.openSync(file,'wx')
 try{fs.writeFileSync(descriptor,JSON.stringify(value,null,2));fs.fsyncSync(descriptor)}finally{fs.closeSync(descriptor)}
}
export function publicFailure(error){return /^[A-Z][A-Z0-9_]{0,99}$/.test(error?.message??'')?error.message:'NORMAL_RUNTIME_FAILURE'}

export async function boundedSsh(command,{input='',timeoutMs=15000,maximumBytes=262144}={}){
 assert.ok(timeoutMs>=1000&&timeoutMs<=60000&&maximumBytes>0&&maximumBytes<=1048576)
 await sshAdmission.take()
 const child=spawn(SSH,[...strictSshArguments(),command],{windowsHide:true,stdio:['pipe','pipe','pipe']})
 const chunks=[],errors=[];let bytes=0,errorBytes=0,overflow=false,expired=false
 const kill=()=>{if(child.exitCode===null)child.kill()}
 child.stdout.on('data',chunk=>{bytes+=chunk.length;if(bytes>maximumBytes){overflow=true;kill()}else chunks.push(chunk)})
 child.stderr.on('data',chunk=>{errorBytes+=chunk.length;if(errorBytes<=8192)errors.push(chunk);else{overflow=true;kill()}})
 const finished=new Promise(resolve=>{child.once('error',()=>resolve({code:null,spawnFailed:true}));child.once('close',(code,signal)=>resolve({code,signal}))})
 const timer=setTimeout(()=>{expired=true;kill()},timeoutMs)
 child.stdin.on('error',()=>{});child.stdin.end(input)
 const result=await finished;clearTimeout(timer)
 // Never include the remote response or stderr in thrown errors: a session
 // acquisition response is intentionally private and lives only in memory.
 if(overflow||expired||result.spawnFailed||result.code!==0||errorBytes!==0){
  const diagnostic=Buffer.concat(errors).toString('utf8')
  const category=overflow?'OUTPUT_BOUND':expired?'LOCAL_DEADLINE':result.spawnFailed?'SPAWN_FAILED':
   /Connection timed out|Operation timed out/i.test(diagnostic)?'CONNECT_TIMEOUT':
   /Permission denied/i.test(diagnostic)?'AUTH_REJECTED':
   /Connection reset|Connection closed|Broken pipe/i.test(diagnostic)?'CONNECTION_CLOSED':
   /HTTP Error 429/.test(diagnostic)?'REMOTE_HTTP_429':/AssertionError/.test(diagnostic)?'REMOTE_ASSERTION':
   /HTTP Error [0-9]{3}/.test(diagnostic)?'REMOTE_HTTP_ERROR':'UNCLASSIFIED_EXIT'
  const error=new Error('NORMAL_SSH_'+category)
  error.controlDiagnostic={category,exitCode:result.code,expired,stdoutBytes:bytes,stderrBytes:errorBytes}
  throw error
 }
 return Buffer.concat(chunks).toString('utf8')
}
export async function remoteJson(code,parameters={},options={}){
 const source=Buffer.from(code).toString('base64'),input=JSON.stringify(parameters)
 assert.ok(source.length<24000&&Buffer.byteLength(input)<524288,'NORMAL_CONTROL_INPUT_BOUND')
 if(ownedControl)return ownedControl.call(code,parameters,options)
 const bootstrap='import base64,json,sys; p=json.load(sys.stdin); exec(compile(base64.b64decode('+JSON.stringify(source)+'),"<owned-normal-control>","exec"))'
 const raw=await boundedSsh('timeout 45 python3 -I -B -c '+posix(bootstrap),{timeoutMs:50000,...options,input})
 assert.ok(Buffer.byteLength(raw)<=262144,'NORMAL_CONTROL_REPLY_BOUND')
 return JSON.parse(raw)
}

export const IDENTITY_CODE=String.raw`
import hashlib,json,os,pathlib,socket,subprocess,urllib.request
assert socket.gethostname()=='ser550468033481' and os.geteuid()==0
result={}
for name,unit,root,port in [('test','legion12-testrun.service','/opt/legion12-testrun',8084),('formal','legion12-test.service','/opt/legion12-test',8083)]:
 expected=p[name]
 assert len(expected['commit'])==40 and len(expected['dllSha256'])==64 and type(expected['pid']) is int and expected['pid']>0
 info=dict(row.split('=',1) for row in subprocess.check_output(['systemctl','show',unit,'-p','MainPID','-p','ActiveState','-p','NRestarts','-p','MemoryHigh','-p','MemoryMax','-p','CPUWeight','-p','IOWeight','-p','Nice','-p','OOMScoreAdjust'],text=True,timeout=3).splitlines() if '=' in row)
 assert info['ActiveState']=='active' and int(info['MainPID'])==expected['pid']
 commit=pathlib.Path(root+'/.deployment-commit').read_text().strip()
 dll=hashlib.sha256(pathlib.Path(root+'/publish/GrandUMIServer.dll').read_bytes()).hexdigest().upper()
 assert commit==expected['commit'] and dll==expected['dllSha256']
 with urllib.request.urlopen('http://127.0.0.1:'+str(port)+'/health',timeout=3) as response:health=json.load(response)
 assert health.get('serverVersion')==commit and health.get('status')=='ok' and health.get('maintenance') is False
 env={}
 for value in pathlib.Path('/proc/'+str(expected['pid'])+'/environ').read_bytes().split(b'\0'):
  key,_,content=value.partition(b'=')
  if key in [b'L12_PUBLIC_BASE_URL',b'L12_TESTRUN_MATCH_STORAGE',b'L12_PRIVATE_DECK_OBJECT_PERSISTENCE',b'L12_TESTRUN_TRANSPORT_DIAGNOSTICS']:assert key not in env;env[key]=content
 assert not env.get(b'L12_TESTRUN_TRANSPORT_DIAGNOSTICS')
 if name=='test':
  assert env.get(b'L12_PUBLIC_BASE_URL',b'').rstrip(b'/')==b'https://legion-12.com/testrun' and env.get(b'L12_TESTRUN_MATCH_STORAGE')==b'ephemeral' and env.get(b'L12_PRIVATE_DECK_OBJECT_PERSISTENCE')==b'true'
 else:
  # The normal formal unit may omit its public-base setting. Its fixed unit,
  # process, commit path, DLL and local health are independently bound above.
  formal_base=env.get(b'L12_PUBLIC_BASE_URL',b'')
  assert formal_base in [b'',b'https://legion-12.com',b'https://legion-12.com/'] and not env.get(b'L12_TESTRUN_MATCH_STORAGE')
 result[name]={'pid':expected['pid'],'commit':commit,'dllSha256':dll,'status':health['status'],'maintenance':health['maintenance'],'diagnosticsOff':True,'restartCount':int(info['NRestarts'])}
 if name=='test':result['resourcePolicy']={'cpuWeight':int(info['CPUWeight']),'ioWeight':int(info['IOWeight']),'nice':int(info['Nice']),'oomScoreAdjust':int(info['OOMScoreAdjust']),'memoryHighBytes':int(info['MemoryHigh']),'memoryMaxBytes':int(info['MemoryMax'])}
assert result['test']['pid']!=result['formal']['pid']
`
export const observeIdentity=async binding=>remoteJson(IDENTITY_CODE+'\nprint(json.dumps(result,separators=(",",":")))',binding)
export async function acquireTestSession(binding){
 return remoteJson(IDENTITY_CODE+String.raw`
import re
values={}
for line in pathlib.Path('/etc/legion12-testrun.env').read_text().splitlines():
 if line.startswith('L12_ADMIN_PASSWORD='):values['password']=line.split('=',1)[1].strip().strip('"').strip("'")
password=values.get('password','');assert re.fullmatch('[0-9a-f]{64}',password)
request=urllib.request.Request('http://127.0.0.1:8084/api/auth/login',data=json.dumps({'username':'Admin','password':password}).encode(),headers={'Content-Type':'application/json'},method='POST')
with urllib.request.urlopen(request,timeout=10) as response:login=json.load(response)
token=login.get('token','');assert re.fullmatch('[0-9a-f]{64}',token) and login.get('account',{}).get('role')=='admin'
print(json.dumps({'token':token},separators=(',',':')))
`,binding)
}
export async function testRequest(base,relative,options={}){
 assert.equal(base,'https://legion-12.com/testrun/')
 assert.ok(typeof relative==='string'&&/^(?:health|api\/[A-Za-z0-9?&=._~%/+-]+)$/.test(relative))
 const url=new URL(relative,base);assert.ok(url.href.startsWith(base)&&url.origin==='https://legion-12.com')
 assert.ok(!url.username&&!url.password&&!url.hash)
 const timeout=Math.min(options.timeoutMs??10000,10000);assert.ok(Number.isFinite(timeout)&&timeout>0)
 const response=await fetch(url,{method:options.method??'GET',redirect:'error',signal:AbortSignal.timeout(timeout),headers:{Accept:'application/json',...(options.token?{Authorization:'Bearer '+options.token}:{}),...(options.body?{'Content-Type':'application/json'}:{})},...(options.body?{body:JSON.stringify(options.body)}:{})})
 const chunks=[];let count=0
 const reader=response.body?.getReader()
 if(reader)try{for(;;){const item=await reader.read();if(item.done)break;count+=item.value.byteLength;if(count>8*1024*1024){await reader.cancel();throw new Error('NORMAL_HTTP_RESPONSE_BOUND')}chunks.push(item.value)}}finally{reader.releaseLock()}
 return {status:response.status,body:count?JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(Buffer.concat(chunks))):null}
}
export function assertRuntimeIdentity(value,binding){
 for(const name of ['test','formal']){
  const item=value[name],expected=binding[name];pid(item.pid);hash(item.dllSha256)
  for(const key of ['pid','commit','dllSha256'])assert.equal(item[key],expected[key])
  assert.equal(item.status,'ok');assert.equal(item.maintenance,false);assert.equal(item.diagnosticsOff,true)
  assert.ok(Number.isSafeInteger(item.restartCount)&&item.restartCount>=0)
 }
 assert.notEqual(value.test.pid,value.formal.pid)
 assert.deepEqual(value.resourcePolicy,{cpuWeight:10,ioWeight:10,nice:10,oomScoreAdjust:750,memoryHighBytes:768*1024**2,memoryMaxBytes:896*1024**2})
 return value
}
export function assertGuardReady(ready,binding,sourceSha){
 assert.equal(ready.schema,2);assert.equal(ready.kind,'normal-guard-ready');assert.equal(ready.selfTestOnly,false)
 pid(ready.supervisorPid);assert.ok(ready.supervisorPid!==binding.test.pid&&ready.supervisorPid!==binding.formal.pid)
 assert.match(String(ready.processStartTicks),/^[0-9]+$/);assert.match(ready.bootId,/^[a-f0-9-]{36}$/)
 assert.equal(ready.hostname,HOST);assert.equal(ready.sourceSha256,sourceSha);assert.equal(ready.profileSha256,NORMAL_PROFILE_SHA)
 for(const name of ['test','formal'])for(const [field,property] of [['Pid','pid'],['Commit','commit'],['DllSha256','dllSha256']])assert.equal(ready[name+field],binding[name][property])
 return ready
}
export function assertGuardSample(row,binding,previous,now=Date.now()){
 assert.equal(row.schema,1)
 assert.equal(row.readOnly,true);assert.equal(row.selfTestOnly,false);assert.equal(row.stop,false);assert.equal(row.phase,'complete')
 assert.equal(row.testPid,binding.test.pid);assert.equal(row.formalPid,binding.formal.pid)
 assert.ok(Number.isFinite(row.roundElapsedMs)&&row.roundElapsedMs>=0&&row.roundElapsedMs<3750)
 assert.ok(Number.isSafeInteger(row.testMemoryBytes)&&row.testMemoryBytes>=0&&row.testMemoryBytes<850*1024**2)
 assert.ok(Number.isSafeInteger(row.hostAvailableBytes)&&row.hostAvailableBytes>=512*1024**2)
 assert.equal(row.oomDelta,0);assert.equal(row.restartDelta,0)
 assert.ok(Number.isSafeInteger(row.observedUtcMs)&&Math.abs(now-row.observedUtcMs)<5000)
 if(previous)assert.ok(row.observedUtcMs>previous.observedUtcMs&&row.observedUtcMs-previous.observedUtcMs<PLAN.guardStaleMs)
 return row
}
export function terminalDiagnosticProfile(setting){
 assert.match(setting??'',/^terminal-recipients-v4:30:[a-f0-9]{32}$/,'EXACT_TERMINAL_RECIPIENT_PROFILE_REQUIRED')
 const profile={baseUrl:'https://legion-12.com/testrun',matchStorage:'ephemeral',privateDeckObjectPersistence:'true',diagnosticSetting:setting}
 return {profile,sha256:digest(JSON.stringify(profile))}
}
export function assertTerminalGuardReady(ready,binding,sourceSha,setting){
 assert.equal(ready.schema,2);assert.equal(ready.kind,'terminal-recipient-guard-ready');assert.equal(ready.selfTestOnly,false)
 assert.equal(ready.diagnosticOnly,true);assert.equal(ready.onlineCapacityAccepted,false)
 pid(ready.supervisorPid);assert.ok(ready.supervisorPid!==binding.test.pid&&ready.supervisorPid!==binding.formal.pid)
 assert.match(String(ready.processStartTicks),/^[0-9]+$/);assert.match(ready.bootId,/^[a-f0-9-]{36}$/)
 assert.equal(ready.hostname,HOST);assert.equal(ready.sourceSha256,sourceSha);assert.equal(ready.profileSha256,terminalDiagnosticProfile(setting).sha256)
 for(const name of ['test','formal'])for(const [field,property] of [['Pid','pid'],['Commit','commit'],['DllSha256','dllSha256']])assert.equal(ready[name+field],binding[name][property])
 return ready
}
export function assertTerminalGuardSample(row,binding,previous,now=Date.now()){
 assert.equal(row.diagnosticOnly,true);assert.equal(row.onlineCapacityAccepted,false)
 return assertGuardSample(row,binding,previous,now)
}
export async function installGuardFiles(runId,guardFile,frozenFile){
 assert.match(runId,/^[a-f0-9]{32}$/)
 const guard=fs.readFileSync(guardFile),frozen=fs.readFileSync(frozenFile)
 assert.ok(guard.length<131072&&frozen.length<65536);assert.equal(digest(frozen),FROZEN_GUARD_SHA)
 const files=[{name:'normal-guard.py',sha256:digest(guard),base64:guard.toString('base64')},{name:'frozen-guard.py',sha256:digest(frozen),base64:frozen.toString('base64')}]
 const reply=await remoteJson(String.raw`
import base64,hashlib,json,os,pathlib,socket
assert os.geteuid()==0 and socket.gethostname()=='ser550468033481'
assert len(p['runId'])==32 and all(c in '0123456789abcdef' for c in p['runId'])
root=pathlib.Path('/tmp/l12-normal-pressure-'+p['runId']);root.mkdir(mode=0o700)
assert root.resolve()==root and root.stat().st_uid==0
for item in p['files']:
 assert item['name'] in ['normal-guard.py','frozen-guard.py']
 content=base64.b64decode(item['base64'],validate=True);assert len(content)<131072 and hashlib.sha256(content).hexdigest().upper()==item['sha256']
 with (root/item['name']).open('xb') as stream:stream.write(content);stream.flush();os.fsync(stream.fileno())
 os.chmod(root/item['name'],0o600)
print(json.dumps({'schema':1,'root':str(root),'files':[{'name':item['name'],'sha256':item['sha256']} for item in p['files']]}))
`,{runId,files},{timeoutMs:50000})
 assert.equal(reply.root,'/tmp/l12-normal-pressure-'+runId);assert.deepEqual(reply.files,files.map(({name,sha256})=>({name,sha256})))
 return {root:reply.root,sourceSha256:files[0].sha256}
}
export function startNormalGuard(binding,installed,options={}){
 return startBoundGuard(binding,installed,options,{entry:'normal-guard.py',readyKind:'normal-guard-ready',completionKind:'normal-guard-completion',validateReady:assertGuardReady,validateSample:assertGuardSample})
}
export function startTerminalGuard(binding,installed,{diagnosticSetting,...options}={}){
 terminalDiagnosticProfile(diagnosticSetting)
 return startBoundGuard(binding,installed,options,{entry:'terminal-guard.py',readyKind:'terminal-recipient-guard-ready',completionKind:'terminal-recipient-guard-completion',
  validateReady:(ready,binding,sha)=>assertTerminalGuardReady(ready,binding,sha,diagnosticSetting),validateSample:assertTerminalGuardSample,resultExtras:{diagnosticOnly:true,onlineCapacityAccepted:false}})
}
function startBoundGuard(binding,installed,{stopFile,logPath,clock=Date.now,processFactory=spawn,signalOwned=null},policy){
 const argumentsList=['python3','-I','-B',installed.root+'/'+policy.entry,'--observe','--frozen-guard',installed.root+'/frozen-guard.py','--test-commit',binding.test.commit,'--formal-commit',binding.formal.commit,'--test-pid',binding.test.pid,'--formal-pid',binding.formal.pid,'--test-dll',binding.test.dllSha256,'--formal-dll',binding.formal.dllSha256,'--seconds',930]
 if(processFactory===spawn)sshAdmission.noteStart()
 const child=processFactory(SSH,[...strictSshArguments(),'flock -n /run/lock/legion12-normal-pressure.lock '+argumentsList.map(posix).join(' ')],{windowsHide:true,stdio:['ignore','pipe','pipe']})
 const rows=[];let ready=null,completion=null,buffer='',bytes=0,errorBytes=0,failure=null,receivedAt=0,controlledStop=false
 const log=fs.openSync(logPath,'wx')
 const stop=reason=>{failure??=reason;if(!fs.existsSync(stopFile))durableJson(stopFile,{schema:1,reason,utc: new Date().toISOString()})}
 const finished=new Promise(resolve=>{child.once('error',()=>{stop('GUARD_SSH_START_FAILED');resolve({code:null})});child.once('close',(code,signal)=>resolve({code,signal}))})
 child.stdout.on('data',chunk=>{
  bytes+=chunk.length;if(bytes>4*1024*1024){stop('GUARD_OUTPUT_BOUND');return}
  fs.writeSync(log,chunk);buffer+=chunk.toString('utf8')
  while(buffer.includes('\n')){
   const position=buffer.indexOf('\n'),line=buffer.slice(0,position);buffer=buffer.slice(position+1)
   try{
    assert.ok(Buffer.byteLength(line)<=4096)
    const value=JSON.parse(line)
    if(value.kind===policy.readyKind){assert.equal(ready,null);ready=policy.validateReady(value,binding,installed.sourceSha256)}
    else if(value.kind===policy.completionKind){assert.equal(completion,null);completion=value;if(!controlledStop)stop('GUARD_ENDED_BEFORE_TEARDOWN')}
    else {assert.ok(ready);policy.validateSample(value,binding,rows.at(-1),clock());rows.push(value);receivedAt=clock()}
   }catch{stop('GUARD_REPLY_INVALID')}
  }
  if(buffer.length>8192)stop('GUARD_PARTIAL_LINE_BOUND')
 })
 child.stderr.on('data',chunk=>{errorBytes+=chunk.length;stop('GUARD_STDERR_NOT_EMPTY')})
 const watcher=setInterval(()=>{if(receivedAt&&clock()-receivedAt>=5000)stop('GUARD_STREAM_STALE')},250)
 const waitReady=async()=>{const until=clock()+12000;while(clock()<until){if(failure)throw new Error(failure);if(ready&&rows.length)return ready;if(child.exitCode!==null)throw new Error('GUARD_EXITED_BEFORE_READY');await pause(25)}throw new Error('GUARD_READY_DEADLINE')}
 async function finish(){
  controlledStop=true
  clearInterval(watcher)
  try{
   if(!ready){if(child.exitCode===null)child.kill();await settleWithin(finished,2000);throw new Error('GUARD_IDENTITY_NOT_BOUND')}
   if(ready&&child.exitCode===null&&signalOwned)await signalOwned(ready,installed)
   else if(ready&&child.exitCode===null)await remoteJson(String.raw`
import hashlib,json,os,pathlib,signal
ready=p['ready'];pid=ready['supervisorPid'];root=pathlib.Path(p['root'])
assert type(pid) is int and pid>1 and pid not in [ready['testPid'],ready['formalPid']]
assert pathlib.Path('/proc/sys/kernel/random/boot_id').read_text().strip()==ready['bootId']
stat=pathlib.Path('/proc/'+str(pid)+'/stat').read_text().rsplit(')',1)[1].split();assert stat[19]==str(ready['processStartTicks'])
assert p['scriptName'] in ['normal-guard.py','terminal-guard.py']
cmd=pathlib.Path('/proc/'+str(pid)+'/cmdline').read_bytes().split(b'\0');assert str(root/p['scriptName']).encode() in cmd and b'--observe' in cmd
assert pathlib.Path('/proc/'+str(pid)).stat().st_uid==0
assert hashlib.sha256((root/p['scriptName']).read_bytes()).hexdigest().upper()==ready['sourceSha256']
os.kill(pid,signal.SIGTERM)
print('{"schema":1,"signalSent":true}')
`,{ready,root:installed.root,scriptName:policy.entry},{timeoutMs:15000})
   const exit=await settleWithin(finished,10000)
   assert.ok(exit&&exit.code===143&&errorBytes===0&&buffer==='','GUARD_EXIT_NOT_PROVEN')
   assert.ok(completion&&completion.reason==='SIGTERM_REQUESTED'&&completion.allOwnedChildrenReaped===true&&completion.supervisorPid===ready.supervisorPid&&String(completion.processStartTicks)===String(ready.processStartTicks),'GUARD_COMPLETION_NOT_PROVEN')
   for(const key of ['bootId','hostname','sourceSha256','profileSha256','testPid','formalPid','testCommit','formalCommit','testDllSha256','formalDllSha256'])assert.equal(completion[key],ready[key],'GUARD_COMPLETION_IDENTITY_CHANGED')
   return {stopRequested:Boolean(failure),staleLimitMs:5000,rows,ready,completion,transportExitCode:exit.code,failure,sharedWindowExclusive:true,...policy.resultExtras}
  }finally{
   clearInterval(watcher)
   if(child.exitCode===null)child.kill()
   await settleWithin(finished,2000)
   child.stdout.removeAllListeners('data');child.stderr.removeAllListeners('data')
   fs.fsyncSync(log);fs.closeSync(log)
  }
 }
 return {waitReady,finish,assertFresh:()=>{assert.ok(!failure&&ready&&rows.length&&clock()-receivedAt<5000,'GUARD_NOT_FRESH')},state:()=>({ready,rows:rows.length,failure})}
}
