import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {execFileSync,spawnSync} from 'node:child_process'
import {pathToFileURL} from 'node:url'
import {FROZEN_CORE,FROZEN_SHA,REPO,PLAN,adaptFrozenCore} from './testrun-pressure-protocol.mjs'
import {adaptFrozenPressureCore} from './local-pressure-protocol.mjs'
import {createExecutableCore} from './testrun-pressure-execution.mjs'
import {OWNED_WORKLOAD_SOURCES,ownedWorkloadFile,inspectOwnedWorkloadSources,
  restoreOwnedWorkloadSource,restoreRebasedWorkloadSource,rebaseFrozenWorkloadSource} from './owned-workload-sources.mjs'

let cases=0
const check=action=>{action();cases++}
const sha=bytes=>crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()
const ownedCore=path.join(REPO,'ops/qa/workload/pressure-load-r7-approved-preparation-cadence-r6.mjs')
check(()=>assert.equal(path.resolve(FROZEN_CORE),path.resolve(ownedCore),'FROZEN_WORKLOAD_STILL_EXECUTES_HISTORICAL_SOURCE'))
check(()=>assert.equal(FROZEN_SHA,'F737052DCBFDA4A29FF53BA5DFAD76E771774C1C89C993275FFAFEAA023684FD'))
const closure=inspectOwnedWorkloadSources()
check(()=>{assert.equal(closure.sources.length,11);assert.equal(closure.byteExactSources,8);assert.equal(closure.pathDerivedSources,3)
  assert.equal(closure.sources.reduce((sum,row)=>sum+row.pathTransformations,0),13)})
for(const row of OWNED_WORKLOAD_SOURCES){
 const bytes=fs.readFileSync(ownedWorkloadFile(row.id))
 check(()=>assert.equal(sha(restoreOwnedWorkloadSource(row,bytes)),row.originalSha256))
 check(()=>{const changed=Buffer.from(bytes);changed[0]^=1
  assert.throws(()=>inspectOwnedWorkloadSources({read:file=>path.resolve(file)===path.resolve(ownedWorkloadFile(row.id))?changed:fs.readFileSync(file)}),/OWNED_WORKLOAD_SOURCE_DRIFT/)})
 check(()=>assert.throws(()=>restoreOwnedWorkloadSource({...row,originalSha256:'F'.repeat(64)},bytes),/WORKLOAD_ORIGINAL_REVERSE_DRIFT/))
}
for(const bytes of [Buffer.alloc(0),Buffer.alloc(256*1024+1),'not bytes'])check(()=>assert.throws(()=>inspectOwnedWorkloadSources({read:()=>bytes}),/WORKLOAD_SOURCE_BYTES_INVALID/))
check(()=>assert.throws(()=>ownedWorkloadFile('not-an-owned-source'),/UNKNOWN_OWNED_WORKLOAD_SOURCE/))
check(()=>{
 const r6=fs.readFileSync(ownedWorkloadFile('r6'),'utf8')
 assert.ok(r6.includes("const TOOL_ROOT = path.dirname(fileURLToPath('file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/cohort30-terminal-v2-r6.mjs'))"))
 assert.ok(r6.includes("const FROZEN_ROOT = path.resolve(fileURLToPath(new URL('..',import.meta.url)))"))
 assert.ok(r6.includes('path.dirname(stopFile).toLowerCase() === TOOL_ROOT.toLowerCase()'))
 assert.ok(r6.includes('path.dirname(output).toLowerCase() === TOOL_ROOT.toLowerCase()'))
})
for(const [before,after]of [
 ["const TOOL_ROOT = path.dirname(fileURLToPath('file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/cohort30-terminal-v2-r6.mjs'))","const TOOL_ROOT = path.dirname(fileURLToPath(import.meta.url))"],
 ["const FROZEN_ROOT = path.resolve(fileURLToPath(new URL('..',import.meta.url)))","const FROZEN_ROOT = 'C:/old-historical-code'"],
])check(()=>{
 const row=OWNED_WORKLOAD_SOURCES.find(row=>row.id==='r6'),source=fs.readFileSync(ownedWorkloadFile('r6'),'utf8')
 assert.ok(source.includes(before));assert.throws(()=>restoreOwnedWorkloadSource(row,Buffer.from(source.replace(before,after))),/OWNED_WORKLOAD_SOURCE_DRIFT/)
})
const original=fs.readFileSync(FROZEN_CORE,'utf8')
const normalUrl=new URL('./testrun-pressure-protocol.mjs',import.meta.url).href
const localUrl=new URL('./local-pressure-protocol.mjs',import.meta.url).href
const normal=adaptFrozenCore(original,normalUrl)
const local=adaptFrozenPressureCore(original,localUrl,{withEvidence:true})
const executable=createExecutableCore(normalUrl)
check(()=>{assert.equal(normal.evidence.restoredSha256,FROZEN_SHA);assert.equal(local.evidence.restoredSha256,FROZEN_SHA)
 assert.equal(normal.evidence.byteExactRoundtrip,true);assert.equal(local.evidence.byteExactRoundtrip,true)
 assert.equal(adaptFrozenPressureCore(original,localUrl),local.source)})
for(const adapter of [s=>adaptFrozenCore(s,normalUrl),s=>adaptFrozenPressureCore(s,localUrl)])check(()=>assert.throws(()=>adapter(original+'\n')))
check(()=>assert.throws(()=>adaptFrozenPressureCore(original,localUrl,{withEvidence:'yes'})))
for(const [mode,prepared] of [['normal',normal],['local',local]]){
 const evidence=prepared.evidence.pathRelocation
 check(()=>{assert.equal(evidence.mode,mode);assert.equal(evidence.byteExactPathRoundtrip,true)
   assert.equal(evidence.sourceBindings.length,11);assert.equal(evidence.originalDataRootRetained,true)})
 check(()=>assert.equal(prepared.source.split('from '+JSON.stringify(pathToFileURL(ownedWorkloadFile('full')).href)).length-1,2))
 // Rebase operates after the protocol adapter. Reverse only its path edits,
 // then apply the same rebase again to prove exact anchor cardinalities.
 const common=mode==='local'?original:original.replace("'file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/ws-burst-r7.mjs'",JSON.stringify(new URL('./ws-burst-observer.mjs',normalUrl).href))
 const rebound=rebaseFrozenWorkloadSource(common,mode==='local'?localUrl:normalUrl,{mode})
 check(()=>assert.equal(restoreRebasedWorkloadSource(rebound.source,rebound.steps),common))
 const first=rebound.steps[0]
 check(()=>assert.throws(()=>rebaseFrozenWorkloadSource(common.replace(first.before,'missing source anchor'),normalUrl,{mode}),/WORKLOAD_PATH_ANCHOR_COUNT_CHANGED/))
 check(()=>assert.throws(()=>rebaseFrozenWorkloadSource(common+'\n// '+first.before,normalUrl,{mode}),/WORKLOAD_PATH_ANCHOR_COUNT_CHANGED/))
 check(()=>assert.throws(()=>restoreRebasedWorkloadSource(rebound.source+'\n// '+first.after,rebound.steps),/WORKLOAD_PATH_ANCHOR_COUNT_CHANGED/))
 for(const line of ["const MAX_WALL_MS = 900_000","const ACTION_CONFIRMATION_MS = 3_000",
  "const TOOL_ROOT = path.dirname(fileURLToPath('file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/pressure-load-r7-approved-preparation-durable-r5.mjs'))",
  'coreSourceSha256: sha256File(fileURLToPath(import.meta.url))'])check(()=>assert.ok(prepared.source.includes(line)))
}
check(()=>{assert.equal(PLAN.wsConfirmationMs,3000);assert.equal(PLAN.outsideUsers,10);assert.deepEqual([...PLAN.stages],[10,20,30])})
check(()=>assert.ok(normal.source.includes("requireContract(false,'normal_pressure_execution_not_admitted')")))
check(()=>assert.ok(executable.source.includes("requireContract(typeof configuration.assertNormalExecution==='function','normal_pressure_admission_required')")))
check(()=>assert.ok(local.source.includes('local_qualification_cannot_run_remote')))
check(()=>assert.throws(()=>rebaseFrozenWorkloadSource(original,normalUrl,{mode:'remote'}),/WORKLOAD_REBASE_MODE_INVALID/))
const attributes=fs.readFileSync(path.join(process.env.L12_QA_SOURCE_REPO||REPO,'.gitattributes'),'utf8')
check(()=>assert.ok(attributes.split(/\r?\n/).includes('/ops/qa/workload/** -text')))
const manifest=JSON.parse(fs.readFileSync(path.join(REPO,'ops/qa/toolchain-sources.json')))
check(()=>{assert.equal(manifest.legacyExecutableInputs.length,0);assert.equal(manifest.completeLegacyCodeClosure,true)
 for(const row of OWNED_WORKLOAD_SOURCES){assert.ok(manifest.sources.includes(row.path))
  assert.ok(manifest.immutableEvidenceInputs.some(item=>item.path===row.originalPath&&item.sha256===row.originalSha256&&!Object.hasOwn(item,'execution')))}})

// A real module load in each fresh process proves the generated import graph,
// including R6's top-level ledger/selector/TypeScript hashes. Deny historical
// executable files and all transport/process starts. This is deliberately not
// an online workload, credentials check or application build.
const childProgram=String.raw`
import assert from 'node:assert/strict'
import fs from 'node:fs'
import net from 'node:net'
import tls from 'node:tls'
import http from 'node:http'
import https from 'node:https'
import cp from 'node:child_process'
import {registerHooks,syncBuiltinESMExports} from 'node:module'
import {fileURLToPath,pathToFileURL} from 'node:url'
const [core,repo,mode]=process.argv.slice(1)
process.argv[1]='synthetic-no-direct-cli.mjs'
let legacyReads=0,networkCalls=0,childProcesses=0,sourceReads=0
const deny=value=>{let name=value instanceof URL?fileURLToPath(value):String(value)
 if(name.startsWith('file:'))name=fileURLToPath(name)
 name=name.replaceAll('\\','/')
 if((name.includes('/artifacts/')&&/\.(mjs|ps1|py)$/.test(name))||name.includes('/ledger-r5/')){
  legacyReads++;throw new Error('LEGACY_EXECUTABLE_READ_FORBIDDEN')}
 if(name.includes('/.ssh/')||name.endsWith('.dpapi'))throw new Error('PRIVATE_INPUT_READ_FORBIDDEN')
 sourceReads++
}
for(const name of ['readFileSync','openSync']){const original=fs[name];fs[name]=function(file,...rest){deny(file);return original.call(this,file,...rest)}}
const readAsync=fs.promises.readFile.bind(fs.promises);fs.promises.readFile=async(file,...rest)=>{deny(file);return readAsync(file,...rest)}
for(const [object,names]of [[net,['connect','createConnection']],[tls,['connect']],[http,['request','get']],[https,['request','get']]])
 for(const name of names)object[name]=()=>{networkCalls++;throw new Error('NETWORK_FORBIDDEN')}
globalThis.fetch=()=>{networkCalls++;throw new Error('NETWORK_FORBIDDEN')}
globalThis.WebSocket=class{constructor(){networkCalls++;throw new Error('NETWORK_FORBIDDEN')}}
for(const name of ['exec','execFile','execSync','execFileSync','spawn','spawnSync','fork'])cp[name]=()=>{childProcesses++;throw new Error('CHILD_PROCESS_FORBIDDEN')}
syncBuiltinESMExports()
registerHooks({load(url,context,next){if(url.startsWith('file:'))deny(url);return next(url,context)}})
const loaded=await import(pathToFileURL(core).href)
assert.equal(typeof loaded.workloadTestHooks.WsClient,'function')
assert.equal(typeof loaded.workloadTestHooks.remoteConfiguration,'function')
assert.equal(typeof loaded.waitForSecondLedgerReader,'function')
assert.equal(typeof loaded.validateFullWorkloadFinal,'function')
const selector=await import(pathToFileURL(repo+'/ops/qa/workload/cohort30-terminal-v2/placement-selector-r1.mjs').href)
const snapshot={phase:'Main',revision:10,activePlayer:0,prompts:[{playerIndex:0,promptId:'synthetic-all-bottom',kind:'order',
 minChoose:2,maxChoose:2,validChoices:['a','b'],data:{placementMode:'all-bottom'}}],players:[]}
const selected=selector.selectLegalCommandWithAllBottom({playerIndex:0},snapshot,{})
assert.deepEqual(selected.command.bottomCardInstanceIds,['a','b'])
assert.deepEqual(selected.command.topCardInstanceIds,[])
const invalid=structuredClone(snapshot);invalid.prompts[0].validChoices=['a','a']
assert.equal(selector.selectLegalCommandWithAllBottom({playerIndex:0},invalid,{}).skip,'unsupported_authoritative_prompt')
assert.equal(legacyReads,0);assert.equal(networkCalls,0);assert.equal(childProcesses,0)
console.log(JSON.stringify({status:'passed',mode,actualModuleLoaded:true,allBottomActualSelectorPassed:true,legacyReads,networkCalls,childProcesses,sourceReads}))
`
const root=path.join('D:/GPT/Legion12/cache/primary/qa-owned-workload-20261024','source-test-'+crypto.randomUUID())
execFileSync(process.env.L12_PWSH||'pwsh',['-NoProfile','-NonInteractive','-Command',
 '. $env:L12_WORKLOAD_GUARD; $null=Assert-L12GeneratedPath -Path $env:L12_WORKLOAD_TEST_ROOT -Purpose "workload source import acceptance"'],
 {windowsHide:true,timeout:15000,env:{...process.env,L12_WORKLOAD_GUARD:path.join(REPO,'ops/windows/L12GeneratedStorage.ps1'),L12_WORKLOAD_TEST_ROOT:root}})
assert.ok(!fs.existsSync(root));fs.mkdirSync(root)
const generated=[],imports=[]
try{
 for(const [mode,source]of [['normal-prepared',normal.source],['normal-executable',executable.source],['local',local.source]]){
  const file=path.join(root,mode+'.mjs');fs.writeFileSync(file,source,{flag:'wx'});generated.push(file)
  const result=spawnSync(process.execPath,['--input-type=module','-e',childProgram,file,REPO,mode],
   {encoding:'utf8',windowsHide:true,timeout:20000,maxBuffer:65536,env:{...process.env,TEMP:root,TMP:root,TMPDIR:root}})
  assert.equal(result.status,0,result.stderr||result.stdout)
  const proof=JSON.parse(result.stdout);assert.equal(proof.actualModuleLoaded,true);imports.push(proof);cases++
 }
}finally{
 for(const file of generated){assert.equal(path.dirname(file),root);assert.ok(fs.lstatSync(file).isFile()&&!fs.lstatSync(file).isSymbolicLink());fs.unlinkSync(file)}
 assert.deepEqual(fs.readdirSync(root),[]);fs.rmdirSync(root)
}
console.log(JSON.stringify({status:'passed',cases,repositoryCoreBound:true,originalF737Preserved:true,
 byteExactSources:8,pathDerivedSources:3,actualFreshProcessImports:imports,temporarySourceCopiesRemoved:true,
 runtimePackagesRemainExternal:true,networkCalls:0,onlineCapacityAccepted:false}))
