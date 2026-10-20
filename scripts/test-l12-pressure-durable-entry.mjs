// Local import-seam tests of the real CLI. Never admits a synthetic binding as a release.
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import assert from 'node:assert/strict'
import {fileURLToPath,pathToFileURL} from 'node:url'
import {execFileSync,spawnSync} from 'node:child_process'
import {validatePressureMeasurementCheckpoint} from './lib/l12-pressure-checkpoint.mjs'
const repo='D:/GPT/Legion12/worktrees/lubu-pingyang-20261001'
const base='D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016'
const next=base+'/cohort30-full-workload-r7/recorder-v3-next'
const entry=next+'/pressure-load-r7-v3-preparation-durable-r4.mjs'
const core=base+'/cohort30-full-workload-r7/pressure-load-r7-approved-preparation-durable-r5.mjs'
const parentCore=core.replace('durable-r5','durable-r4')
const helper=base+'/cohort30-candidate-frame-r6/large-terminal-contract-notes-base-r3.mjs'
const checkpoint=repo+'/scripts/lib/l12-pressure-checkpoint.mjs'
const fixedSource=base+'/cohort30-candidate-frame-r6/large-terminal-contract-r6.mjs'
const root=process.env.L12_DURABLE_ENTRY_TEST_OUTPUT
assert(root&&path.isAbsolute(root)&&path.dirname(path.resolve(root)).toLowerCase()===path.resolve(next).toLowerCase()&&!fs.existsSync(root))
const pwsh=process.env.L12_PWSH||'pwsh'
const guarded=execFileSync(pwsh,['-NoProfile','-NonInteractive','-Command',
 `. '${repo}/ops/windows/Initialize-L12BuildEnvironment.ps1' -CacheRoot 'D:/GPT/Legion12/cache/primary' 6>$null | Out-Null; `+
 `$null=Assert-L12GeneratedPath -Path $env:L12_DURABLE_ENTRY_TEST_OUTPUT -Purpose 'bounded durable entry synthetic QA'; `+
 '$null=Assert-L12GeneratedPath -Path $env:TEMP -Purpose "durable entry QA TEMP"; @{TEMP=$env:TEMP;TMP=$env:TMP;TMPDIR=$env:TMPDIR}|ConvertTo-Json -Compress'],
 {encoding:'utf8',windowsHide:true,timeout:20000,maxBuffer:65536})
Object.assign(process.env,JSON.parse(guarded.trim()));fs.mkdirSync(root)
const sha=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase()
const hash=text=>crypto.createHash('sha256').update(text).digest('hex').toUpperCase()
const write=(file,value)=>fs.writeFileSync(file,typeof value==='string'?value:JSON.stringify(value)+'\n',{flag:'wx',mode:0o600})
const sourceFiles=[entry,core,parentCore,helper,checkpoint,fixedSource]
const before=Object.fromEntries(sourceFiles.map(file=>[file,sha(file)]))
const normalize=file=>fs.readFileSync(file,'utf8').replaceAll('\r\n','\n').trimEnd()
assert.equal(normalize(core).replaceAll('large-terminal-contract-notes-base-r3.mjs','large-terminal-contract-notes-base-r2.mjs'),normalize(parentCore))
// Only the imported authority/protocol implementation is replaced in child processes.
// All real CLI parsing, frozen file hashes, storage, receipt/config checks and final IO still execute.
const fake=path.join(root,'synthetic-authority.mjs'),loader=path.join(root,'local-only-loader.mjs')
write(fake,`import fs from 'node:fs';import path from 'node:path';
import {FIXED} from ${JSON.stringify(pathToFileURL(fixedSource).href)};
import {createPressureMeasurementCheckpoint,persistPressureMeasurementCheckpoint} from ${JSON.stringify(pathToFileURL(checkpoint).href)};
export async function initializeR7Protocol(){
 const fixture=JSON.parse(fs.readFileSync(process.env.L12_LOCAL_AUTHORITY_FIXTURE,'utf8'));
 if(fixture.localTestAuthority!==true||fixture.syntheticOnly!==true)throw new Error('local fixture required');
 Object.assign(FIXED,fixture.binding,{syntheticOnly:false});Object.freeze(FIXED);
}
async function runPressure(configuration){
 fs.writeFileSync(path.join(path.dirname(configuration.output),'core-entered.json'),JSON.stringify({localSeam:true,networkCalls:0})+'\\n',{flag:'wx'});
 const terminalFrames=Array.from({length:60},(_,slot)=>({numericClientSlot:slot,numericRoomSlot:Math.floor(slot/2),receiver:slot%2,
 sendToCallbackMs:slot===59?3001:1200,exceededOriginal3s:Number(slot===59),logicalJsonBytes:190000,wireBytes:173000}));
 const metrics={elapsedMs:700000,stages:[10,20,30].map(rooms=>({rooms,outsideUsers:10,durationMs:60000})),terminalFrames,
 actionBounds:{client:{rows:[{slot:0,exceeded_original_3s:1}]}},http:{audit:{requests:30,errors:0}},
 commands:{overall:{attempted:100,accepted:100}},websocket:{gameState:{fullPayloadBytes:1000000}},payloadFloor:{observedRooms:30},reconstructionErrors:0,presenceReconstructionErrors:0};
 persistPressureMeasurementCheckpoint(configuration.measurementCheckpointPath,createPressureMeasurementCheckpoint({candidate:configuration.expectedCommit,coreSourceSha256:'A'.repeat(64),metrics}));
 if(process.env.L12_LOCAL_CLEANUP_MODE==='throw')throw new Error('synthetic cleanup failure');
 if(process.env.L12_LOCAL_CLEANUP_MODE==='block')await new Promise(()=>{setInterval(()=>{},1000)});
 return {status:'inconclusive',durationMs:700000,cleanup:{roomsCreated:30},syntheticOnly:true,finalAccepted:false};
}
export const workloadTestHooks={runPressure};export async function waitForSecondLedgerReader(){}
`)
write(loader,`export async function resolve(specifier,context,nextResolve){const resolved=await nextResolve(specifier,context);return resolved.url===${JSON.stringify(pathToFileURL(core).href)}?{url:${JSON.stringify(pathToFileURL(fake).href)},shortCircuit:true}:resolved}\n`)
const memory={adminToken:'abcdef0123456789'.repeat(4),diagnosticNonce:'0123456789abcdef'.repeat(2)}
const candidate='8c33b7f595bc0cab599eef560c5d5e3d80c96124',formal='d7f59725eab623c207969e7d4cd7d4777d27b8f9'
let serial=0;const results=[]
function make(...values){
 const baseValue=values.length===0?'':values[0]
 const selected=path.join(root,String(++serial).padStart(2,'0'));fs.mkdirSync(selected)
 const manifest=path.join(selected,'manifest.json'),receipt=path.join(selected,'install.json'),authority=path.join(selected,'authority.json')
 const binding={candidate,formalCandidate:formal,formalPid:434300,bindingSha256:'B'.repeat(64),manifestSha256:'',releaseSha256:'C'.repeat(64),
 testDllSha256:'D'.repeat(64),formalDllSha256:'E'.repeat(64),deployedNetSha256:'F'.repeat(64),cardFrameSha256:'1'.repeat(64),syntheticOnly:true}
 const manifestValue={schema:3,commit:candidate,releaseBaseCommit:baseValue,releaseSha256:binding.releaseSha256.toLowerCase()}
 write(manifest,manifestValue);binding.manifestSha256=sha(manifest)
 const content=`# l12-owned-terminal-recorder-v3-20261018 ${candidate}\n[Service]\nEnvironment="L12_TESTRUN_TRANSPORT_DIAGNOSTICS=terminal-recorder-v3:6:${memory.diagnosticNonce}"\n`
 const install={schema:3,status:'installed',operation:'install',candidate,formalCandidate:formal,mode:'terminal-recorder-v3',expectedSamples:6,
 nonceSha256:hash(memory.diagnosticNonce),configSha256:hash(content),startupExecEnvReady:true,currentDiagnosticStillActive:true,ephemeralTestMatches:true,
 formalWrites:0,testPidBefore:424200,testPidAfter:424201,formalPid:binding.formalPid,serviceRestarts:1,ownedTestConfigInstalled:true,
 lastMomentIdleVerified:true,ownedContractRevision:1,candidateBindingSha256:binding.bindingSha256,manifestSha256:binding.manifestSha256,
 releaseSha256:binding.releaseSha256,renderedBodySha256:'2'.repeat(64)}
 write(receipt,install);write(authority,{localTestAuthority:true,syntheticOnly:true,binding})
 const args={execute:true,binding:authority,'binding-sha':binding.bindingSha256,'expected-commit':candidate,manifest,'config-receipt':receipt,
 'config-receipt-sha':sha(receipt),'test-pid':'424201','formal-pid':String(binding.formalPid),'test-dll-sha':binding.testDllSha256,
 'formal-dll-sha':binding.formalDllSha256,output:path.join(selected,'client.json'),'stop-file':path.join(selected,'stop.json'),
 'ledger-ready-1':path.join(selected,'ready.json'),'measurement-checkpoint':path.join(selected,'measurement-before-account-cleanup.json'),
 'ledger-run-0':'local-core-run','ledger-run-1':'local-outer-run','ledger-pipe-0':'local_core_pipe_000','ledger-pipe-1':'local_outer_pipe_000'}
 return {selected,manifestValue,install,args,authority}
}
function run(fixture,{mode='normal',seam=true,timeout=15000}={}){
 const env={...process.env,L12_LOCAL_AUTHORITY_FIXTURE:fixture.authority,L12_LOCAL_CLEANUP_MODE:mode,
 L12_PRESSURE_ADMIN_TOKEN:memory.adminToken,L12_SYNTHETIC_TRANSPORT_DIAGNOSTIC_NONCE:memory.diagnosticNonce}
 delete env.L12_PRESSURE_CREDENTIALS_FILE;delete env.L12_PRESSURE_CREDENTIALS_JSON;delete env.NODE_OPTIONS
 const argv=Object.entries(fixture.args).flatMap(([key,value])=>key==='execute'?['--execute']:['--'+key,String(value)])
 const reply=spawnSync(process.execPath,[...(seam?['--no-warnings','--experimental-loader',pathToFileURL(loader).href]:[]),entry,...argv],
 {env,encoding:'utf8',windowsHide:true,timeout,maxBuffer:65536})
 assert(!reply.stdout.includes(memory.adminToken)&&!reply.stderr.includes(memory.adminToken))
 assert(!reply.stdout.includes(memory.diagnosticNonce)&&!reply.stderr.includes(memory.diagnosticNonce))
 return reply
}
function check(name,fn){try{fn();results.push({name,passed:true})}catch(error){results.push({name,passed:false,reason:error.message})}}
function expectReject(f,code){const reply=run(f);assert.equal(reply.error,undefined);assert.equal(reply.status,1);assert.equal(reply.stdout,'');
 assert.equal(JSON.parse(reply.stderr).code,code);assert(!fs.existsSync(path.join(f.selected,'core-entered.json')))}
for(const [name,baseValue] of [['empty-base',''],['different-valid-base','a'.repeat(40)]])check('actual CLI '+name,()=>{
 const f=make(baseValue),reply=run(f);assert.equal(reply.error,undefined);assert.equal(reply.status,1);
 assert.equal(JSON.parse(reply.stdout).status,'inconclusive');assert.equal(reply.stderr,'')
 const proof=validatePressureMeasurementCheckpoint(JSON.parse(fs.readFileSync(f.args['measurement-checkpoint'],'utf8')))
 assert.equal(proof.metrics.terminalFrames.length,60);assert.equal(proof.metrics.terminalFrames[59].sendToCallbackMs,3001)
 assert.equal(proof.metrics.terminalFrames[59].exceededOriginal3s,1);assert.equal(proof.finalAccepted,false);assert.equal(proof.cleanupPending,true)
 assert.equal(JSON.parse(fs.readFileSync(f.args.output,'utf8')).finalAccepted,false)
})
for(const [name,value] of [['null',null],['missing',undefined],['blank',' '],['uppercase','A'.repeat(40)],['number',0],['malformed','broken']])
 check('actual CLI rejects '+name+' notes base',()=>expectReject(make(value),'v3_pressure_manifest_binding_invalid'))
for(const [name,edit,code] of [
 ['missing checkpoint',f=>delete f.args['measurement-checkpoint'],'v3_pressure_absolute_path_required'],
 ['wrong checkpoint name',f=>f.args['measurement-checkpoint']=path.join(f.selected,'wrong.json'),'v3_pressure_checkpoint_name_invalid'],
 ['checkpoint output collision',f=>f.args.output=f.args['measurement-checkpoint'],'v3_pressure_path_collision'],
 ['checkpoint scope escape',f=>f.args['measurement-checkpoint']='D:/GPT/Legion12/artifacts/measurement-before-account-cleanup.json','v3_pressure_output_scope_invalid'],
 ['test DLL mismatch',f=>f.args['test-dll-sha']='0'.repeat(64),'v3_pressure_site_identity_mismatch'],
 ['install SHA mismatch',f=>f.args['config-receipt-sha']='0'.repeat(64),'v3_pressure_install_file_hash_mismatch'],
 ['candidate mismatch',f=>f.args['expected-commit']='0'.repeat(40),'v3_pressure_candidate_argument_mismatch'],
 ['existing proof',f=>write(f.args['measurement-checkpoint'],'do-not-overwrite\n'),'v3_pressure_existing_output_or_stop_refused'],
 ['manifest byte drift',f=>fs.appendFileSync(f.args.manifest,' '),'v3_pressure_manifest_binding_invalid'],
 ])check('actual CLI rejects '+name,()=>{const f=make();edit(f);expectReject(f,code);if(name==='existing proof')assert.equal(fs.readFileSync(f.args['measurement-checkpoint'],'utf8'),'do-not-overwrite\n')})
check('actual CLI cleanup throw keeps checkpoint without final report',()=>{
 const f=make(),reply=run(f,{mode:'throw'});assert.equal(reply.status,1);assert.equal(JSON.parse(reply.stderr).code,'v3_pressure_admission_failed')
 assert(fs.existsSync(f.args['measurement-checkpoint']));assert(!fs.existsSync(f.args.output))
})
check('actual CLI rejects junction aliases converging on one physical output',()=>{
 const f=make(),alias=path.join(root,'same-physical-alias')
 fs.symlinkSync(f.selected,alias,'junction')
 f.args.output=path.join(alias,'measurement-before-account-cleanup.json')
 expectReject(f,'v3_pressure_physical_path_collision')
 assert(!fs.existsSync(f.args['measurement-checkpoint']))
})
check('actual CLI bounded kill during cleanup keeps checkpoint without final report',()=>{
 const f=make(),reply=run(f,{mode:'block',timeout:8000});assert.equal(reply.error?.code,'ETIMEDOUT')
 assert(fs.existsSync(f.args['measurement-checkpoint']));assert(!fs.existsSync(f.args.output))
})
check('unmodified actual entry does not admit synthetic authority fixture',()=>{
 const f=make(),reply=run(f,{seam:false});assert.equal(reply.error,undefined);assert.equal(reply.status,1)
 assert.equal(JSON.parse(reply.stderr).code,'candidate_binding_hash_mismatch');assert(!fs.existsSync(path.join(f.selected,'core-entered.json')))
})
assert.deepEqual(Object.fromEntries(sourceFiles.map(file=>[file,sha(file)])),before)
const report={schema:1,status:results.every(row=>row.passed)?'passed':'failed',cases:results.length,passed:results.filter(row=>row.passed).length,
 results,sourceHashes:before,testSha256:sha(fileURLToPath(import.meta.url)),physicalOutputRoot:fs.realpathSync(root),
 localImportSeamOnly:true,realCandidateAdmissionVerified:false,actualRunPressureBodyUnchanged:true,networkCalls:0,sshCalls:0,
 sharedBuilds:0,remoteExecutionReady:false,measuredPerformance:false,finalAccepted:false}
write(path.join(root,'acceptance.json'),report)
console.log(JSON.stringify({status:report.status,cases:report.cases,passed:report.passed,measuredPerformance:false}))
if(report.status!=='passed')process.exitCode=1
