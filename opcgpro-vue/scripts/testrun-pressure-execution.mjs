import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {spawn,execFileSync} from 'node:child_process'
import {pathToFileURL} from 'node:url'
import {REPO,FROZEN_CORE,adaptFrozenCore,digest,PLAN} from './testrun-pressure-protocol.mjs'
import {remoteJson,pause,durableJson} from './testrun-pressure-runtime.mjs'
import {validatePressureMeasurementCheckpoint} from '../../scripts/lib/l12-pressure-checkpoint.mjs'

export const PWSH='C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
export const FROZEN_GUARD=path.join(REPO,'ops/qa/testrun-frozen-resource-guard.py')
const LEDGER=path.join(REPO,'ops/qa/ledger')
const SINK_SHA='B74A6359DA15536248EE660F01B33DFD8A59376AF6D930DDC0F2BC54CC94DA81'
const CLIENT_SHA='88BB6ACB68C3867CEC7A5E3DBA5A2B429A672FE4CDEF94C8D47D5921EA38B7F9'
export function createExecutableCore(adapterUrl,{confirmationTrace=false}={}){
 const original=fs.readFileSync(FROZEN_CORE,'utf8'),prepared=adaptFrozenCore(original,adapterUrl)
 const changes=[
  [" // Deliberately fail closed until the independent remote lifecycle is implemented and verified.\n requireContract(false,'normal_pressure_execution_not_admitted')"," requireContract(typeof configuration.assertNormalExecution==='function','normal_pressure_admission_required')\n await configuration.assertNormalExecution()"],
  ['cleanupDeadline: startedClock + MAX_WALL_MS,','cleanupDeadline: Math.min(startedClock + MAX_WALL_MS,configuration.executionDeadlineMs),'],
  ['pressureDeadline: startedClock + PRESSURE_DEADLINE_MS,','pressureDeadline: Math.min(startedClock + PRESSURE_DEADLINE_MS,configuration.executionDeadlineMs-180000),'],
  ['if(representativeBinding!==null)return cleanupOwnedTestrunAccounts(context,adminIdentity,provisioning.owned??[],cohortRequest(context))',"if(representativeBinding!==null){await context.onBeforeAccountTeardown();return cleanupOwnedTestrunAccounts(context,adminIdentity,provisioning.owned??[],cohortRequest(context))}"],
 ]
 if(confirmationTrace)changes.push(
  ['const actionMarkers = new LoadV2Markers({nonce:configuration.diagnosticNonce,onMarker:emitProgress})',
   'const originalActionMarkers = new LoadV2Markers({nonce:configuration.diagnosticNonce,onMarker:emitProgress})\n  const actionMarkers = configuration.confirmationTrace.compose(originalActionMarkers,metrics)'],
  ['tracked.started = true\n  return tracked','tracked.started = true\n  context.confirmationTrace?.bindRoom(tracked,hostIdentity,guestIdentity)\n  return tracked'],
  ["typeof confirmedPhase === 'string' && confirmedPhase !== snapshot.phase)","typeof confirmedPhase === 'string' && confirmedPhase !== snapshot.phase)\n  context.confirmationTrace?.sealRequest(room,requestId,[selected.command.type,outcome,round(performance.now()-started)],context.signal)"],
  ["metrics.currentStage = 'cleanup'\n    context.terminalWindow", "const traceOriginalCommands=metrics.commandSummary(stageDurations)\n    const traceOriginalFailed=Boolean(failure||stop.reason)||metrics.reconstructionErrors>0\n      ||Object.values(metrics.httpSummary(skips)).some(row=>(row.errors??0)>0)\n      ||['rejected','timed-out','transport-error','advanced-without-ack'].some(kind=>traceOriginalCommands.overall.outcomes[kind]>0)\n    await configuration.confirmationTrace.beforeTerminal({failed:traceOriginalFailed,workersFrozen:context.payloadFloorWorkersFrozen,originalFailure:failure,originalStopReason:stop.reason,stage:metrics.currentStage,deadlineMs:context.cleanupDeadline})\n    metrics.currentStage = 'cleanup'\n    context.terminalWindow"],
  ['cleanup.compliantSurrenders += outcome.compliantSurrenders','context.confirmationTrace?.sealTerminal(room,outcome)\n    cleanup.compliantSurrenders += outcome.compliantSurrenders'],
  // Reserve tracing inside, never beyond, the original 900s wall and 180s recovery.
  ['configuration.executionDeadlineMs-180000','configuration.executionDeadlineMs-220000'],
 )
 let source=prepared.source
 for(const [before,after] of changes){assert.equal(source.split(before).length,2);source=source.replace(before,after)}
 let restored=source
 for(const [before,after] of changes.toReversed())restored=restored.replace(after,before)
 assert.equal(restored,prepared.source,'execution-only changes must reverse exactly to prepared source')
 return {source,evidence:{...prepared.evidence,executionEnabled:true,confirmationTrace,traceRecoveryReserveMs:confirmationTrace?220000:180000,preparedSha256:digest(prepared.source),adaptedSha256:digest(source),executionChanges:changes.map(([before,after])=>({beforeSha256:digest(before),afterSha256:digest(after)})),originalWorkloadUnchanged:true,deadlineOnlyShortened:true}}
}
export function assertCapturedMeasurement(file,{candidate,coreSha256}){
 const info=fs.lstatSync(file);assert.ok(info.isFile()&&!info.isSymbolicLink()&&info.size>0&&info.size<=2*1024**2)
 const bytes=fs.readFileSync(file),checkpoint=validatePressureMeasurementCheckpoint(JSON.parse(bytes))
 assert.equal(checkpoint.candidate,candidate);assert.equal(checkpoint.coreSourceSha256,coreSha256)
 return {sha256:digest(bytes),capturedBeforeTeardown:true,capturedUtcMs:Date.now(),terminalFrames:checkpoint.metrics.terminalFrames.length,finalAccepted:false}
}
export async function readPressureDisk(ownedGuardRoot){
 return remoteJson(String.raw`
import json,os,pathlib,socket,stat
assert os.geteuid()==0 and socket.gethostname()=='ser550468033481'
def tree(name):
 root=pathlib.Path(name)
 if not root.exists():return 0
 assert root.is_dir() and not root.is_symlink() and root.resolve()==root
 total=0;count=0
 for base,dirs,files in os.walk(root,followlinks=False,onerror=lambda error: (_ for _ in ()).throw(error)):
  dirs[:]=[name for name in dirs if not pathlib.Path(base,name).is_symlink()]
  for name in files:
   try:info=pathlib.Path(base,name).lstat()
   except FileNotFoundError:continue
   if stat.S_ISREG(info.st_mode):total+=info.st_size;count+=1
   assert count<250000
 return total
program=sum(tree(name) for name in ['/opt/legion12-testrun-releases','/opt/legion12-testrun-static/card-assets','/opt/legion12-testrun-static/web-assets'])
data=sum(tree(name) for name in ['/opt/legion12-testrun-runtime','/opt/legion12-testrun-deployment/runtime-backups','/opt/legion12-testrun-deployment/failures'])
temporary=tree('/opt/legion12-testrun-deployment/incoming')+tree(p['ownedGuardRoot'])
for entry in pathlib.Path('/opt').iterdir():
 if entry.name.startswith(('legion12-testrun-staging-','legion12-testrun-card-assets-staging-')):temporary+=tree(str(entry))
free=os.statvfs('/opt/legion12-test');free=free.f_bavail*free.f_frsize
print(json.dumps({'programBytes':program,'dataBytes':data,'temporaryPeakBytes':temporary,'totalPeakBytes':program+data+temporary,'formalFreeBytes':free}))
`,{ownedGuardRoot},{timeoutMs:15000})
}
export function assertPressureDisk(disk){
 for(const [field,cap] of [['programBytes',1.5*1024**3],['dataBytes',512*1024**2],['temporaryPeakBytes',1024**3],['totalPeakBytes',3*1024**3]]){assert.ok(Number.isSafeInteger(disk[field])&&disk[field]>=0&&disk[field]<=cap,'NORMAL_PRESSURE_DISK_BUDGET')}
 assert.ok(Number.isSafeInteger(disk.formalFreeBytes)&&disk.formalFreeBytes>=8*1024**3,'NORMAL_PRESSURE_FORMAL_FREE_SPACE')
 return disk
}
export async function readPeripheralPreflight(manager,handle){
 const storage=(await manager.request(handle,'api/admin/storage/private-deck-persistence')).body
 const privateDeck={configuredEnabled:storage?.configuredEnabled,effectiveMode:storage?.effectiveMode,writable:storage?.writable}
 assert.deepEqual(privateDeck,{configuredEnabled:true,effectiveMode:'object',writable:true})
 const analytics=(await manager.request(handle,'api/admin/analytics/cards')).body
 const audit=(await manager.request(handle,'api/admin/v1/audit?limit=50')).body
 const tournaments=(await manager.request(handle,'api/tournaments')).body
 const counts={analyticsItems:analytics?.items?.length??0,auditItems:Array.isArray(audit)?audit.length:audit?.items?.length??0,tournamentItems:tournaments?.items?.length??0}
 assert.ok(Object.values(counts).every(value=>Number.isSafeInteger(value)&&value>0),'NORMAL_PRESSURE_REQUIRED_DATA_ABSENT')
 const id=tournaments.items.find(item=>typeof item?.id==='string'&&item.id)?.id;assert.ok(id)
 await manager.request(handle,'api/tournaments/'+encodeURIComponent(id))
 return {privateDeck,...counts}
}
export async function createOwnedLedgerSinks(control,{cache,output}){
 const sinkPath=path.join(LEDGER,'account-ledger-sink-r2.ps1'),clientPath=path.join(LEDGER,'account-ledger-client-r2.mjs')
 assert.equal(digest(fs.readFileSync(sinkPath)),SINK_SHA);assert.equal(digest(fs.readFileSync(clientPath)),CLIENT_SHA)
 const {AccountLedgerClient,NamedPipeLedgerTransport}=await import(pathToFileURL(clientPath).href)
 const entries=control.groups.map(group=>({group,pipeName:'l12-normal-ledger-'+crypto.randomBytes(16).toString('hex'),child:null,done:null,client:null}))
 for(const entry of entries)entry.client=new AccountLedgerClient({runId:entry.group.runId,maxSlots:entry.group.maxSlots,transport:new NamedPipeLedgerTransport({pipeName:entry.pipeName})})
 const start=async index=>{
  assert.ok(index===0||index===1);const entry=entries[index];assert.equal(entry.child,null)
  if(index===1){assert.ok(entries[0].done);const previous=await Promise.race([entries[0].done,pause(5000).then(()=>null)]);assert.ok(previous,'NORMAL_FIRST_LEDGER_EXIT_UNPROVEN');assert.equal(previous.code,0);assert.equal(previous.errorBytes,0)}
  const child=spawn(PWSH,['-NoProfile','-File',sinkPath,'-Apply','-RunId',entry.group.runId,'-PipeName',entry.pipeName,'-LedgerPath',entry.group.ledgerPath,'-MaxSlots',String(entry.group.maxSlots),'-ConnectionTimeoutMs','15000','-FrameTimeoutMs','30000'],{cwd:cache,windowsHide:true,stdio:['ignore','pipe','pipe']})
  entry.child=child;let bytes=0,errorBytes=0;const lines=[]
  child.stdout.on('data',chunk=>{bytes+=chunk.length;if(bytes>8192)child.kill();else lines.push(chunk)})
  child.stderr.on('data',chunk=>{errorBytes+=chunk.length;if(errorBytes>4096)child.kill()})
  entry.done=new Promise(resolve=>{child.once('error',()=>resolve({code:null,errorBytes}));child.once('close',code=>{const result={code,errorBytes,publicOutputBytes:bytes};durableJson(path.join(output,'ledger-sink-'+index+'-exit.json'),result);resolve(result)})})
  execFileSync(PWSH,['-NoProfile','-Command',String.raw`$until=[DateTime]::UtcNow.AddSeconds(10);while([DateTime]::UtcNow -lt $until){if([IO.Directory]::EnumerateFiles('\\.\pipe\') -contains ('\\.\pipe\'+$env:L12_NORMAL_PIPE)){exit 0};Start-Sleep -Milliseconds 25};exit 2`],{env:{...process.env,L12_NORMAL_PIPE:entry.pipeName},windowsHide:true,stdio:'pipe',timeout:12000,maxBuffer:4096})
 }
 const finish=async()=>{
  const result=[]
  for(const entry of entries){entry.client.close();if(entry.child){let done;const until=Date.now()+5000;while(Date.now()<until){done=await Promise.race([entry.done,pause(50).then(()=>null)]);if(done)break}if(!done){entry.child.kill();done=await entry.done}result.push({index:entry.group.index,...done});assert.equal(done.code,0);assert.equal(done.errorBytes,0)}}
  return result
 }
 return {clients:entries.map(entry=>entry.client),start,finish}
}
