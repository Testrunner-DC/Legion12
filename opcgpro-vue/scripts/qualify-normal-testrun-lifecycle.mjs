import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {spawn} from 'node:child_process'
import {pathToFileURL} from 'node:url'
import {REPO,EVIDENCE_ROOT,CACHE_ROOT,validateBinding,assertGeneratedDirectories,digest} from './testrun-pressure-protocol.mjs'
import {observeIdentity,assertRuntimeIdentity,assertGuardReady,acquireTestSession,testRequest,installGuardFiles,startNormalGuard,remoteJson,durableJson,pause,publicFailure} from './testrun-pressure-runtime.mjs'
import {createNormalSessionManager} from './testrun-pressure-recovery.mjs'

const PWSH='C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
const FROZEN_GUARD='D:/GPT/Legion12/artifacts/performance-measurement-recovery-20261020/guard-deadline-r4/resource-guard-deadline-r4.py'
export async function protectOwnedSession({file,runId,kind,token}){
 assert.match(runId,/^[a-f0-9]{32}$/);assert.ok(['original','recovery'].includes(kind));assert.match(token,/^[a-f0-9]{64}$/)
 assert.equal(path.basename(file),kind+'-session.dpapi')
 const code=String.raw`$ErrorActionPreference='Stop';Add-Type -AssemblyName System.Security;$m=[IO.MemoryStream]::new();[Console]::OpenStandardInput().CopyTo($m);$bytes=$m.ToArray();$entropy=[Text.Encoding]::UTF8.GetBytes($env:L12_SESSION_RUN);$protected=[Security.Cryptography.ProtectedData]::Protect($bytes,$entropy,[Security.Cryptography.DataProtectionScope]::CurrentUser);$f=[IO.FileStream]::new($env:L12_SESSION_FILE,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$f.Write($protected);$f.Flush($true)}finally{$f.Dispose();[Array]::Clear($bytes,0,$bytes.Length);$m.Dispose()};[Console]::Write('PROTECTED')`
 const child=spawn(PWSH,['-NoProfile','-Command',code],{windowsHide:true,stdio:['pipe','pipe','pipe'],env:{...process.env,L12_SESSION_RUN:runId,L12_SESSION_FILE:file}})
 let reply='',errors='',expired=false
 child.stdout.on('data',v=>{reply+=v;if(reply.length>64)child.kill()});child.stderr.on('data',v=>{errors+=v;if(errors.length>4096)child.kill()});child.stdin.on('error',()=>{})
 const exited=new Promise(resolve=>{child.once('error',()=>resolve(null));child.once('close',code=>resolve(code))})
 const timer=setTimeout(()=>{expired=true;child.kill()},10000)
 child.stdin.end(JSON.stringify({schema:1,runId,kind,token}))
 const exit=await exited;clearTimeout(timer)
 assert.ok(!expired&&exit===0&&reply==='PROTECTED'&&errors==='','SESSION_PROTECTION_FAILED')
 const bytes=fs.readFileSync(file);assert.ok(bytes.length>64&&!bytes.includes(Buffer.from(token)))
 return {kind,cipherSha256:digest(bytes),currentUserDpapi:true}
}
export async function removeExitedGuardFiles(installed,proof){
 assert.equal(proof.transportExitCode,143);assert.equal(proof.completion.allOwnedChildrenReaped,true)
 return remoteJson(String.raw`
import hashlib,json,os,pathlib,socket,stat
assert os.geteuid()==0 and socket.gethostname()=='ser550468033481'
root=pathlib.Path(p['root']);assert str(root).startswith('/tmp/l12-normal-pressure-') and len(root.name)==52 and root.resolve()==root
ready=p['ready'];proc=pathlib.Path('/proc/'+str(ready['supervisorPid']))
if proc.exists():
 ticks=proc.joinpath('stat').read_text().rsplit(')',1)[1].split()[19]
 assert str(ticks)!=str(ready['processStartTicks'])
expected={'normal-guard.py':ready['sourceSha256'],'frozen-guard.py':'549BC59A31A3EDF836A045C5F915C9DB2E283B070150ABC7E369A01BBC8A2C1F'}
assert {entry.name for entry in root.iterdir()}==set(expected)
for name,sha in expected.items():
 item=root/name; info=item.lstat();assert stat.S_ISREG(info.st_mode) and info.st_uid==0 and not item.is_symlink()
 assert hashlib.sha256(item.read_bytes()).hexdigest().upper()==sha
for name in expected:(root/name).unlink()
root.rmdir()
print('{"schema":1,"ownedFilesRemoved":2,"ownedDirectoryRemoved":true,"serviceChanges":0}')
`,{root:installed.root,ready:proof.ready},{timeoutMs:15000})
}
export function planQualificationSessionRecovery(previous,slots){
 const kinds=['original','recovery'];assert.ok(Array.isArray(previous.protectedSessions)&&previous.protectedSessions.length<=2)
 assert.equal(new Set(previous.protectedSessions.map(item=>item.kind)).size,previous.protectedSessions.length)
 for(const item of previous.protectedSessions){assert.ok(kinds.includes(item.kind));assert.match(item.cipherSha256,/^[A-F0-9]{64}$/)}
 if(previous.sessionAttempts!==undefined){assert.ok(Array.isArray(previous.sessionAttempts)&&previous.sessionAttempts.length<=2);for(const item of previous.sessionAttempts){assert.ok(kinds.includes(item.kind));assert.equal(item.phase,'acquire-requested')}}
 const recover=[],pending=[]
 for(const kind of kinds){
  const slot=slots[kind];assert.equal(typeof slot.intent,'boolean');if(slot.cipherSha256!==null)assert.match(slot.cipherSha256,/^[A-F0-9]{64}$/)
  const item=previous.protectedSessions.find(value=>value.kind===kind),attempted=slot.intent||(previous.sessionAttempts??[]).some(value=>value.kind===kind)
  if(slot.cipherSha256!==null){if(item)assert.equal(slot.cipherSha256,item.cipherSha256);recover.push({kind,cipherSha256:slot.cipherSha256});continue}
  const revoked=previous.revocation?.results?.some(value=>value.kind===kind&&value.afterStatus===401)
  if((item||attempted)&&!revoked)pending.push({kind,code:'ACQUISITION_OUTCOME_OR_SESSION_REVOCATION_UNPROVEN'})
  else if(!item&&!attempted&&previous.sessionAttempts===undefined)pending.push({kind,code:'LEGACY_ACQUISITION_ATTEMPT_NOT_DURABLY_RECORDED'})
 }
 return {recover,pending,allAcquisitionsAccountedFor:pending.length===0}
}
export async function recoverQualification({reportPath,reportSha256,bindingPath,bindingSha256}){
 const reportBytes=fs.readFileSync(reportPath);assert.equal(digest(reportBytes),reportSha256)
 const previous=JSON.parse(reportBytes),runId=previous.runId
 assert.match(runId,/^[a-f0-9]{32}$/);assert.equal(previous.status,'QUALIFICATION_FAILED');assert.equal(previous.bindingSha256,bindingSha256)
 const bindingBytes=fs.readFileSync(bindingPath);assert.equal(digest(bindingBytes),bindingSha256)
 const binding=validateBinding(JSON.parse(bindingBytes)),output=path.dirname(reportPath),cache=path.join(CACHE_ROOT,'live-qualification-'+runId)
 assertGeneratedDirectories(output,cache);assert.equal(path.basename(output),'live-qualification-'+runId)
 const slots={}
 for(const kind of ['original','recovery']){
  const intent=path.join(cache,kind+'-acquire-intent.json')
  if(fs.existsSync(intent)){
   const info=fs.lstatSync(intent);assert.ok(info.isFile()&&!info.isSymbolicLink()&&info.size<2048)
   const value=JSON.parse(fs.readFileSync(intent,'utf8'));assert.equal(value.runId,runId);assert.equal(value.kind,kind);assert.equal(value.phase,'acquire-requested')
  }
  const file=path.join(cache,kind+'-session.dpapi')
  if(fs.existsSync(file)){const info=fs.lstatSync(file);assert.ok(info.isFile()&&!info.isSymbolicLink()&&info.size>64&&info.size<4096)}
  slots[kind]={intent:fs.existsSync(intent),cipherSha256:fs.existsSync(file)?digest(fs.readFileSync(file)):null}
 }
 const plan=planQualificationSessionRecovery(previous,slots)
 const before=assertRuntimeIdentity(await observeIdentity(binding),binding),sessions=[],pending=plan.pending
 for(const item of plan.recover){
  const file=path.join(cache,item.kind+'-session.dpapi')
  assert.equal(digest(fs.readFileSync(file)),item.cipherSha256)
  const command=String.raw`$ErrorActionPreference='Stop';Add-Type -AssemblyName System.Security;$bytes=[IO.File]::ReadAllBytes($env:L12_SESSION_FILE);$entropy=[Text.Encoding]::UTF8.GetBytes($env:L12_SESSION_RUN);$plain=[Security.Cryptography.ProtectedData]::Unprotect($bytes,$entropy,[Security.Cryptography.DataProtectionScope]::CurrentUser);try{[Console]::OpenStandardOutput().Write($plain)}finally{[Array]::Clear($plain,0,$plain.Length)}`
  const child=spawn(PWSH,['-NoProfile','-Command',command],{windowsHide:true,stdio:['ignore','pipe','pipe'],env:{...process.env,L12_SESSION_FILE:file,L12_SESSION_RUN:runId}})
  let bytes=Buffer.alloc(0),errorBytes=0;child.stdout.on('data',chunk=>{bytes=Buffer.concat([bytes,chunk]);if(bytes.length>4096)child.kill()});child.stderr.on('data',chunk=>{errorBytes+=chunk.length;if(errorBytes>4096)child.kill()})
  const done=new Promise(resolve=>{child.once('error',()=>resolve(null));child.once('close',code=>resolve(code))});const timer=setTimeout(()=>child.kill(),10000)
  const exit=await done;clearTimeout(timer);assert.ok(exit===0&&errorBytes===0&&bytes.length<4096,'SESSION_PRIVATE_READ_FAILED')
  let privateSession
  try{privateSession=JSON.parse(bytes.toString('utf8'))}finally{bytes.fill(0)}
  try{
   assert.equal(privateSession.runId,runId);assert.equal(privateSession.kind,item.kind);assert.match(privateSession.token,/^[a-f0-9]{64}$/)
   const me=await testRequest(binding.baseUrl,'api/auth/me',{token:privateSession.token});assert.ok([200,401].includes(me.status))
   let activeSessionsBefore=null
   if(me.status===200){
    assert.equal(me.body.role,'admin')
    const list=await testRequest(binding.baseUrl,'api/auth/sessions',{token:privateSession.token});assert.equal(list.status,200);assert.ok(Array.isArray(list.body))
    activeSessionsBefore=list.body.length
    const retired=await testRequest(binding.baseUrl,'api/auth/sessions/current',{method:'DELETE',token:privateSession.token});assert.ok([200,204].includes(retired.status))
   }
   const after=await testRequest(binding.baseUrl,'api/auth/me',{token:privateSession.token});assert.equal(after.status,401)
   sessions.push({kind:item.kind,beforeStatus:me.status,afterStatus:401,activeSessionsBefore,onlyExactOwnedTokenRevoked:true})
  }finally{privateSession.token=''}
 }
 const ready=JSON.parse(fs.readFileSync(path.join(output,'guard.jsonl'),'utf8').split('\n')[0])
 assertGuardReady(ready,binding,'2CD7EBC3F06A4E1845537EB16A71EBB6EBF296079491494C8516097F237E86BD')
 const owned=await remoteJson(String.raw`
import hashlib,json,os,pathlib,signal,socket,stat,time
assert os.geteuid()==0 and socket.gethostname()=='ser550468033481'
ready=p['ready'];pid=ready['supervisorPid'];root=pathlib.Path('/tmp/l12-normal-pressure-'+p['runId'])
assert pathlib.Path('/proc/sys/kernel/random/boot_id').read_text().strip()==ready['bootId']
proc=pathlib.Path('/proc/'+str(pid));signalled=False
if proc.exists():
 ticks=proc.joinpath('stat').read_text().rsplit(')',1)[1].split()[19]
 if str(ticks)==str(ready['processStartTicks']):
  cmd=proc.joinpath('cmdline').read_bytes().split(b'\0');assert str(root/'normal-guard.py').encode() in cmd and b'--observe' in cmd and pid not in [ready['testPid'],ready['formalPid']]
  assert hashlib.sha256((root/'normal-guard.py').read_bytes()).hexdigest().upper()==ready['sourceSha256']
  os.kill(pid,signal.SIGTERM);signalled=True
def owned_processes():
 found=[]
 for entry in pathlib.Path('/proc').iterdir():
  if not entry.name.isdigit():continue
  try:cmd=entry.joinpath('cmdline').read_bytes().split(b'\0')
  except (FileNotFoundError,PermissionError,ProcessLookupError):continue
  if any(value.startswith(str(root).encode()+b'/') for value in cmd):found.append(int(entry.name))
 return found
until=time.monotonic()+6
while owned_processes() and time.monotonic()<until:time.sleep(.05)
assert not owned_processes()
expected={'normal-guard.py':ready['sourceSha256'],'frozen-guard.py':'549BC59A31A3EDF836A045C5F915C9DB2E283B070150ABC7E369A01BBC8A2C1F'}
assert root.resolve()==root and {item.name for item in root.iterdir()}==set(expected)
for name,sha in expected.items():
 item=root/name;assert stat.S_ISREG(item.lstat().st_mode) and not item.is_symlink() and hashlib.sha256(item.read_bytes()).hexdigest().upper()==sha
for name in expected:(root/name).unlink()
root.rmdir()
print(json.dumps({'schema':1,'exactSupervisorSignalled':signalled,'ownedCommandLinesAbsent':True,'ownedFilesRemoved':2,'ownedDirectoryRemoved':True,'originalCompletionNotClaimed':True}))
`,{runId,ready},{timeoutMs:15000})
 const after=assertRuntimeIdentity(await observeIdentity(binding),binding)
 for(const name of ['test','formal'])assert.equal(after[name].restartCount,before[name].restartCount)
 const proof={schema:1,status:pending.length?'FAILED_QUALIFICATION_KNOWN_RESOURCES_RETIRED_SESSION_PENDING':'FAILED_QUALIFICATION_OWNED_RESOURCES_RETIRED',reportSha256,bindingSha256,sessions,pending,allAcquisitionsAccountedFor:pending.length===0,owned,before,after,formalWrites:0,serviceRestarts:0,maintenanceWrites:0,businessAccountsCreated:0,businessRoomsCreated:0,onlineCapacityAccepted:false}
 durableJson(path.join(output,'qualification-cleanup-'+crypto.randomUUID()+'.json'),proof)
 console.log(JSON.stringify({status:proof.status,sessionsRevoked401:sessions.length,pendingSessionKinds:pending.map(item=>item.kind),ownedCommandLinesAbsent:true,onlineCapacityAccepted:false}))
 return proof
}
export async function qualifyNormalLifecycle({bindingPath,bindingSha256}){
 assert.match(bindingSha256,/^[A-F0-9]{64}$/)
 const bindingBytes=fs.readFileSync(bindingPath);assert.equal(digest(bindingBytes),bindingSha256)
 const binding=validateBinding(JSON.parse(bindingBytes))
 const runId=crypto.randomUUID().replaceAll('-',''),output=path.join(EVIDENCE_ROOT,'live-qualification-'+runId),cache=path.join(CACHE_ROOT,'live-qualification-'+runId)
 assertGeneratedDirectories(output,cache);fs.mkdirSync(output,{recursive:true});fs.mkdirSync(cache,{recursive:true})
 process.env.TEMP=cache;process.env.TMP=cache;process.env.TMPDIR=cache
 const started=Date.now(),deadlineMs=started+120000,protectedSessions=[],sessionAttempts=[]
 let before,after,installed,guard,guardProof,removed,manager,original,recovery,revocation,code=null
 const stages=[]
 const stage=name=>{stages.push({name,elapsedMs:Date.now()-started});console.log(JSON.stringify({stage:name,runId}))}
 try{
  before=assertRuntimeIdentity(await observeIdentity(binding),binding);durableJson(path.join(output,'identity-before.json'),before);stage('dual-identity-bound')
  installed=await installGuardFiles(runId,path.join(REPO,'ops/qa/testrun-normal-resource-guard.py'),FROZEN_GUARD)
  durableJson(path.join(output,'owned-guard-install.json'),installed)
  guard=startNormalGuard(binding,installed,{stopFile:path.join(cache,'stop.json'),logPath:path.join(output,'guard.jsonl')})
  await guard.waitReady();guard.assertFresh();stage('actual-linux-guard-ready')
  manager=createNormalSessionManager({binding,request:testRequest,deadlineMs,
   observeIdentity:async()=>{guard.assertFresh();const value=assertRuntimeIdentity(await observeIdentity(binding),binding);return {baseUrl:binding.baseUrl,testPid:value.test.pid,testCommit:value.test.commit,testDllSha256:value.test.dllSha256,diagnosticsOff:true,matchStorage:'ephemeral'}},
   acquireSession:async({kind})=>{
    guard.assertFresh();assert.ok(['original','recovery'].includes(kind))
    const attempt={kind,phase:'acquire-requested'};sessionAttempts.push(attempt)
    durableJson(path.join(cache,kind+'-acquire-intent.json'),{schema:1,runId,...attempt})
    const session=await acquireTestSession(binding)
    try{protectedSessions.push(await protectOwnedSession({file:path.join(cache,kind+'-session.dpapi'),runId,kind,token:session.token}));return session}
    catch(error){
     // Manager has not received this token yet: retire exactly this new token,
     // rather than leave a session behind when local encryption fails.
     const retired=await testRequest(binding.baseUrl,'api/auth/sessions/current',{method:'DELETE',token:session.token})
     const after=await testRequest(binding.baseUrl,'api/auth/me',{token:session.token})
     assert.ok([200,204].includes(retired.status)&&after.status===401,'UNPROTECTED_SESSION_REVOCATION_UNPROVEN')
     throw error
    }
   }})
  original=await manager.prepareOriginal();stage('original-session-and-real-F2-object-verified')
  recovery=await manager.prepareRecovery(original);stage('independent-recovery-session-verified')
  assert.notEqual(original.privateSessionHandle,recovery.privateSessionHandle)
  await pause(1500);guard.assertFresh()
 }catch(error){code=publicFailure(error)}
 finally{
  if(manager)try{revocation=await manager.revokeAll();assert.equal(revocation.complete,true);stage('both-owned-sessions-revoked-401')}catch(error){code??=publicFailure(error)}
  try{after=assertRuntimeIdentity(await observeIdentity(binding),binding);for(const name of ['test','formal'])assert.equal(after[name].restartCount,before?.[name].restartCount);stage('unchanged-dual-service-readback')}catch(error){code??=publicFailure(error)}
  if(guard)try{guardProof=await guard.finish();durableJson(path.join(output,'guard-supervision.json'),guardProof);assert.equal(guardProof.stopRequested,false);removed=await removeExitedGuardFiles(installed,guardProof);stage('own-supervisor-exited-and-files-removed')}catch(error){code??=publicFailure(error)}
 }
 if(Date.now()-started>120000)code??='QUALIFICATION_WALL_BOUND_EXCEEDED'
 const report={schema:1,status:code?'QUALIFICATION_FAILED':'NORMAL_LINUX_LIFECYCLE_QUALIFIED',runId,bindingSha256,stages,failure:code,
  durationMs:Date.now()-started,protectedSessions,sessionAttempts,revocation,before,after,removed,
  authenticatedSessions:manager?.publicStatus(),actualLinuxGuardReady:guardProof?.ready?.selfTestOnly===false,
  ownedGuardExitCode:guardProof?.transportExitCode,allOwnedChildrenReaped:guardProof?.completion?.allOwnedChildrenReaped===true,
  businessAccountsCreated:0,businessRoomsCreated:0,formalWrites:0,serviceRestarts:0,maintenanceWrites:0,onlineCapacityAccepted:false,
  sources:['qualify-normal-testrun-lifecycle.mjs','testrun-pressure-runtime.mjs','testrun-pressure-recovery.mjs'].map(file=>({file,sha256:digest(fs.readFileSync(path.join(import.meta.dirname,file)))})),
  boundary:'Real normal Linux guard, two distinct test-only admin sessions, effective F2 object/writable and owned shutdown only. No 30+10 load, browser interaction, operating-system notification or capacity acceptance.'}
 durableJson(path.join(output,'normal-lifecycle-qualification.json'),report)
 console.log(JSON.stringify({status:report.status,output,failure:code,onlineCapacityAccepted:false}))
 return report
}
if(process.argv[1]&&pathToFileURL(path.resolve(process.argv[1])).href===import.meta.url){
 const args=process.argv.slice(2);assert.equal(args.length,5);assert.equal(args[0],'--qualify');assert.equal(args[1],'--binding');assert.equal(args[3],'--binding-sha')
 const report=await qualifyNormalLifecycle({bindingPath:args[2],bindingSha256:args[4]});if(report.failure)process.exitCode=2
}
