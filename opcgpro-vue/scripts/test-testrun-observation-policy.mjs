import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {pathToFileURL} from 'node:url'
import {spawnSync} from 'node:child_process'
import ts from 'typescript'
import * as normal from './run-testrun-representative-pressure.mjs'
import {createExecutableCore} from './testrun-pressure-execution.mjs'
import {createTerminalRecipientRunner} from './terminal-recipient-runner.mjs'

const args=process.argv.slice(2)
assert.ok(args.length===0||(args.length===1&&args[0]==='--verify-local-inputs'))
const verifyLocalInputs=args.length===1
assert.equal(typeof normal.normalObservationPolicy,'function','normal entry must not force full diagnostic tracing')
const file=path.join(import.meta.dirname,'run-testrun-representative-pressure.mjs')
const source=fs.readFileSync(file,'utf8'),tree=ts.createSourceFile(file,source,ts.ScriptTarget.Latest,true,ts.ScriptKind.JS)
const entry=tree.statements.find(node=>ts.isFunctionDeclaration(node)&&node.name?.text==='executeNormalPressure')
const firstTry=entry.body.statements.find(node=>ts.isTryStatement(node))
const prefix=source.slice(entry.body.getStart(tree)+1,firstTry.getStart(tree)).replaceAll('import.meta.dirname',JSON.stringify(import.meta.dirname))
let cases=0,networkCalls=0
const oldFetch=globalThis.fetch
globalThis.fetch=()=>{networkCalls++;throw new Error('NETWORK_FORBIDDEN')}
try {
 const defaults={confirmationTrace:false,pressureStopMs:720000,businessReserveMs:180000,recoveryReserveMs:180000}
 assert.deepEqual(normal.normalObservationPolicy(),defaults);cases++
 assert.deepEqual(normal.normalObservationPolicy(false),defaults);cases++
 assert.deepEqual(normal.normalObservationPolicy(true),{confirmationTrace:true,pressureStopMs:680000,businessReserveMs:220000,recoveryReserveMs:180000});cases++
 for(const value of [null,0,1,'false','true',{},[]]){assert.throws(()=>normal.normalObservationPolicy(value));cases++}
 const runPrefix=new Function('configuration','dependencies','with(dependencies){'+prefix+';return {observationPolicy,startedUtcMs,deadlineMs}}')
 for(const mode of [undefined,false,true]) {
  const calls={writes:0,core:[],timers:[]}
  const policy=normal.normalObservationPolicy(mode)
  const dependencies={assert,normalObservationPolicy:normal.normalObservationPolicy,
   assertRunConfiguration:()=>({test:{commit:'a'.repeat(40)}}),
   fs:{existsSync:()=>false,mkdirSync:()=>calls.writes++,readdirSync:()=>[],writeFileSync:()=>calls.writes++},path,
   process:{env:{}},crypto:{randomUUID:()=> '0'.repeat(32)},Date:{now:()=>1000},sources:()=>[],
   createExecutableCore:(_url,options)=>{calls.core.push(options);return {source:'synthetic',evidence:{}}},pathToFileURL,
   durableJson:()=>calls.writes++,setTimeout:(_callback,delay)=>{calls.timers.push(delay);return 1},
   FULL_TRACE_PROFILE:{captureMs:40000}}
  const result=runPrefix({confirmationDiagnostics:mode,output:'D:/synthetic-evidence',cache:'D:/synthetic-cache',bindingSha256:'B'.repeat(64)},dependencies)
  assert.deepEqual(result.observationPolicy,policy)
  assert.deepEqual(calls.core,[{confirmationTrace:policy.confirmationTrace}])
  assert.deepEqual(calls.timers,[policy.pressureStopMs])
  assert.equal(result.deadlineMs-result.startedUtcMs,900000)
  assert.equal(dependencies.process.env.TEMP,'D:/synthetic-cache');cases++
 }
 let writes=0
 assert.throws(()=>runPrefix({confirmationDiagnostics:'false'}, {normalObservationPolicy:normal.normalObservationPolicy,assertRunConfiguration:()=>{writes++;throw new Error('TOO_LATE')}}))
 assert.equal(writes,0,'invalid mode rejected before directories, sessions or network');cases++
 let teardown
 const visit=node=>{if(ts.isPropertyAssignment(node)&&node.name.getText(tree)==='onBeforeAccountTeardown')teardown=node.initializer.getText(tree);ts.forEachChild(node,visit)}
 visit(tree);assert.ok(teardown)
 const AsyncFunction=Object.getPrototypeOf(async function(){}).constructor
 const runTeardown=new AsyncFunction('dependencies','with(dependencies){let capture,traceCapture;await ('+teardown+')();return {capture,traceCapture}}')
 for(const diagnostics of [false,true])for(const broken of [false,true]) {
  const calls={checkpoint:0,trace:0},failures=[]
  const result=await runTeardown({observationPolicy:normal.normalObservationPolicy(diagnostics),failures,
   measurementPath:'synthetic',binding:{test:{commit:'a'.repeat(40)}},coreBuild:{evidence:{adaptedSha256:'B'.repeat(64)}},
   assertCapturedMeasurement:()=>{calls.checkpoint++;if(broken)throw new Error('SYNTHETIC_MISSING_CHECKPOINT');return {capturedBeforeTeardown:true}},
   durableJson:()=>{},path,output:'D:/synthetic-evidence',captureTrace:async()=>{calls.trace++;return {diagnosticOnly:true}}})
  assert.equal(calls.checkpoint,1,'original numeric checkpoint remains mandatory in both modes')
  assert.equal(calls.trace,Number(diagnostics),'extra Journal capture is opt-in')
  assert.equal(failures.length,Number(broken));assert.equal(Boolean(result.capture),!broken);cases++
 }
 const adapter=pathToFileURL(path.join(import.meta.dirname,'testrun-pressure-protocol.mjs')).href
 // CI owns and executes the real normal-entry prefix/teardown above. The four
 // pinned historical inputs are checked only by an explicitly selected local
 // qualification, never silently skipped because a file happens to be absent.
 if(verifyLocalInputs)for(const mode of [false,true]) {
  const core=createExecutableCore(adapter,{confirmationTrace:mode})
  assert.equal(core.evidence.originalWorkloadUnchanged,true);assert.equal(core.evidence.deadlineOnlyShortened,true)
  assert.equal(core.evidence.traceRecoveryReserveMs,mode?220000:180000)
  assert.equal(core.source.includes('configuration.confirmationTrace.compose'),mode)
  assert.ok(core.source.includes('const MAX_WALL_MS = 900_000'))
  assert.ok(core.source.includes('const ACTION_CONFIRMATION_MS = 3_000'));cases++
 }
 const diagnostic=createTerminalRecipientRunner()
 assert.ok(diagnostic.source.includes('normalObservationPolicy(true)'),'separate diagnostic entry explicitly preserves tracing')
 assert.equal(diagnostic.evidence.reverseExact,true);assert.equal(diagnostic.evidence.onlineCapacityAccepted,false)
 const parsed=spawnSync(process.execPath,['--input-type=module','--check'],{input:diagnostic.source,encoding:'utf8',windowsHide:true})
 assert.equal(parsed.status,0,parsed.stderr);cases++
 assert.equal(networkCalls,0)
 console.log(JSON.stringify({status:'passed',cases,networkCalls,actualEntryPrefixAndTeardownExecuted:true,immutableCoreVerified:verifyLocalInputs,originalLifecycleMs:900000,originalConfirmationMs:3000,onlineCapacityAccepted:false}))
} finally {globalThis.fetch=oldFetch}
