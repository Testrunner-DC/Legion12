import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {execFileSync} from 'node:child_process'
import {fileURLToPath} from 'node:url'
import {inspectNormalPressureEvidence} from '../../opcgpro-vue/scripts/testrun-pressure-finalizer.mjs'
import {REPO, FROZEN_CORE, FROZEN_SHA, digest} from '../../opcgpro-vue/scripts/testrun-pressure-protocol.mjs'

// One historical run only. No URL, run, receipt, threshold or output override is
// accepted. This does not turn the general prospective validator into admission.
const measuredCommit='3a376f17e5a6f9431303b35775977fabd2083b6a'
const runId='ba08330509e6416abdaac0cf6485c4e1'
const live='D:/GPT/Legion12/artifacts/representative-load-current-20261022/stage-goals-live-3a376f17'
const root='D:/GPT/Legion12/artifacts/stage-goals-20261024/property-frame'
const offline='D:/GPT/Legion12/artifacts/representative-load-current-20261022/offline-31ca72a613bf4481927c087ee26de653/offline-tests.json'
const finalizer='opcgpro-vue/scripts/testrun-pressure-finalizer.mjs'
const regression='opcgpro-vue/scripts/test-testrun-pressure-current.mjs'
const self='ops/qa/revalidate-pressure-clock-origin-3a376f17.mjs'
const oldFinalizerSha='99B4D2CC153C2D68AAE2823B1D117F369FFF7B51F733C265E3758B851F1B6F63'
const pins=new Map([
  [live+'/normal-pressure-acceptance.json','8015D8DAE68B49130476819D361CA209DEE398A0E6860C970BCC49F70BB4CAE4'],
  [live+'/pressure-report.json','8AFB6EE827C686C0610DCCD91B9DB377A2E94366F25EB89B00E96DA1F72C6C2A'],
  [root+'/normal-load-binding.json','889850B79387DA5F139DE8D82C28139127813BE5877E742E181EDB471A3A316E'],
  [live+'/execution-adaptation.json','17EAE565323D93672465C5ABA447BB1F7959C5101B81D954DF7B536A22D7DC13'],
  [live+'/cold-ledger-proof.json','CC72A7D88C2D323F6AC31A6B91CD50375E38BC6577483DA2B6F13DB0303CFB78'],
  [offline,'CA269B4871522449B3E3E4AC1F366C6D5BBFE1B23D4215484F3841298C3CDBF0'],
  [FROZEN_CORE,FROZEN_SHA],
  ['D:/GPT/Legion12/cache/primary/representative-load-current-20261022/stage-goals-live-3a376f17/normal-original-workload.mjs',
    '1646DA9864C37FD5FF7665EB37A85B3F6ED9FB8D08F00D0DAF8FFCB273C305BE'],
  ['D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/load-r7-markers.mjs',
    '41544DFB2A7327239ED4EF55BD654D7CA352A3FF9621F7676CB7F40CFBD1D4D9'],
  ['C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/client-boundaries.mjs',
    'E62BF9BCEC47C06727CA75D3ADF5C278E4FB5ACD6F0302E40531262A1DED06AB'],
  ['C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/load-v2-r2/pressure-pure-selftest.mjs',
    'C431C0BF87072655487DDF6C103062D9DC46E20FCDC127AB435BF44F98C03838'],
  [root+'/Current-clock-origin-red.log','B47758B0F88AFE2E7D7C8EFDC6917C19549ACF2C4F5E946A37E555C7553B7D75'],
  [root+'/Current-clock-origin-green.log','8D33D96CD8CB17B1991B7CC1C8D48E4806832C49EC4639B805707DEF9C420E36'],
])
const read=file=>{
  const stat=fs.lstatSync(file)
  assert.ok(stat.isFile()&&!stat.isSymbolicLink()&&stat.size>0&&stat.size<4*1024**2,'BOUNDED_PLAIN_INPUT_REQUIRED')
  return fs.readFileSync(file)
}
const git=args=>execFileSync('git',['-C',REPO,...args],{encoding:'utf8',windowsHide:true,timeout:15000,maxBuffer:4*1024**2})
const lf=value=>value.replaceAll('\r\n','\n')

function inspectFixedRun(bytes) {
  for(const [file,sha] of pins)assert.equal(digest(bytes.get(file)),sha,'PINNED_ORIGINAL_CHANGED: '+file)
  const parsed=file=>JSON.parse(bytes.get(file).toString('utf8'))
  const original=parsed(live+'/normal-pressure-acceptance.json'),report=parsed(live+'/pressure-report.json')
  const binding=parsed(root+'/normal-load-binding.json'),adaptation=parsed(live+'/execution-adaptation.json')
  const cold=parsed(live+'/cold-ledger-proof.json')
  assert.equal(original.runId,runId);assert.equal(original.status,'NOT_ACCEPTED')
  assert.equal(original.onlineCapacityAccepted,false);assert.equal(original.actualFrozenWorkloadInvoked,true)
  assert.deepEqual(original.failures,[{phase:'final-original-contract',code:'NORMAL_RUNTIME_FAILURE'}])
  assert.deepEqual(report,original.evidence.report)
  assert.deepEqual(original.lifecycle,original.evidence.lifecycle)
  assert.deepEqual(original.sourceAtAdmission,original.sourceAfter)
  assert.equal(original.sourceAtAdmission.length,17)
  assert.equal(new Set(original.sourceAtAdmission.map(row=>row.file)).size,17)
  assert.equal(original.sourceAtAdmission.find(row=>row.file==='testrun-pressure-finalizer.mjs').sha256,oldFinalizerSha)
  assert.equal(original.bindingSha256,pins.get(root+'/normal-load-binding.json'))
  assert.equal(original.evidence.bindingSha256,original.bindingSha256)
  assert.equal(cold.complete,true);assert.equal(cold.runId,runId);assert.equal(cold.bindingSha256,original.bindingSha256)
  assert.equal(cold.groups.length,2)
  cold.groups.forEach((group,index)=>{
    assert.equal(group.index,index);assert.equal(group.runId,`normal-${runId}-${index}`)
    assert.equal(group.missing,false);assert.equal(group.wrongRunRejected,true);assert.equal(group.intent,0)
    const ledger=original.evidence.ledgers[index]
    assert.equal(group.count,ledger.count);assert.equal(group.sequence,ledger.seq);assert.equal(group.confirmed,group.count)
    assert.equal(group.cipherSha256,ledger.cipherSha256)
  })
  assert.deepEqual(original.coreAdaptation,adaptation)
  assert.equal(adaptation.frozenSha256,FROZEN_SHA);assert.equal(adaptation.restoredSha256,FROZEN_SHA)
  assert.equal(adaptation.byteExactRoundtrip,true);assert.equal(adaptation.executionEnabled,true)
  assert.equal(adaptation.originalWorkloadUnchanged,true);assert.equal(adaptation.confirmationTrace,false)
  assert.equal(adaptation.traceRecoveryReserveMs,180000)
  assert.equal(adaptation.adaptedSha256,pins.get([...pins.keys()].find(file=>file.endsWith('/normal-original-workload.mjs'))))
  assert.equal(binding.test.commit,measuredCommit);assert.equal(report.observedCommit,measuredCommit)
  assert.equal(original.durationMs,819556);assert.ok(original.durationMs<=900000)
  for(const key of ['formalWrites','serviceRestarts','maintenanceWrites'])assert.equal(original[key],0)
  const prospective=inspectNormalPressureEvidence(original.evidence,binding)
  assert.equal(prospective.status,'prospective-input-contract-valid')
  assert.equal(prospective.onlineCapacityAccepted,false)
  report.actionBoundsClient.client.rows.forEach((row,index)=>{
    const slot=[0,5,11,17,23,29][index]*2
    const frame=report.terminalFrames.find(item=>item.numericClientSlot===slot)
    assert.ok(row.send_to_callback_ms>frame.sendToCallbackMs,'ACTUAL_OLD_EQUALITY_FAILURE_REQUIRED')
  })
  return {original,report,binding,prospective}
}

export function revalidateFixedClockRun({write=false}={}) {
  const bytes=new Map([...pins.keys()].map(file=>[file,read(file)]))
  const checked=inspectFixedRun(bytes)
  // Each independently pinned input is fail-closed, including a whitespace-only
  // change to a raw receipt. Genuine old timeouts cannot be substituted here.
  let pinMutationCases=0
  for(const file of pins.keys()){
    const changed=new Map(bytes);changed.set(file,Buffer.concat([bytes.get(file),Buffer.from('\n')]))
    assert.throws(()=>inspectFixedRun(changed),/PINNED_ORIGINAL_CHANGED/);pinMutationCases++
  }
  const {original,report}=checked
  for(const row of original.sourceAtAdmission){
    if(row.file==='testrun-pressure-finalizer.mjs')continue
    const relative=row.file.startsWith('opcgpro-vue/')?row.file:'opcgpro-vue/scripts/'+row.file
    assert.equal(digest(read(path.join(REPO,relative))),row.sha256,'HISTORICAL_SOURCE_DRIFT: '+relative)
  }
  const oldSource=lf(git(['show',measuredCommit+':'+finalizer]))
  assert.equal(digest(Buffer.from(oldSource)),oldFinalizerSha,'HISTORICAL_FINALIZER_CONTENT_MISMATCH')
  const newSource=lf(read(path.join(REPO,finalizer)).toString('utf8'))
  const before='    assert.equal(row.wire_bytes, frame.wireBytes); assert.equal(row.send_to_callback_ms, frame.sendToCallbackMs)'
  const after=`    assert.equal(row.wire_bytes, frame.wireBytes)
    // Frozen cleanupRoom starts the marker before WsClient.send captures the
    // later WsClient.send timestamp. Both use the same callback/parse observation,
    // but their elapsed values are not equal. Keep the earlier (stricter)
    // marker independently inside the original3s, and require the source order.
    nonnegative(row.send_to_callback_ms); assert.ok(row.send_to_callback_ms <= 3000)
    assert.ok(row.send_to_callback_ms >= frame.sendToCallbackMs)`
  assert.equal(oldSource.split(before).length,2)
  assert.equal(oldSource.replace(before,after),newSource,'ONLY_REVIEWED_CLOCK_PREDICATE_MAY_CHANGE')
  const qa=JSON.parse(read(offline))
  assert.equal(qa.status,'offline-tests-passed');assert.equal(qa.cases,136)
  assert.equal(qa.networkCalls,0);assert.equal(qa.remoteApplyReady,false);assert.equal(qa.onlineCapacityAccepted,false)
  assert.deepEqual(qa.qaSources.map(row=>row.path),['ops/qa/Invoke-TestrunRepresentativePressure.ps1',
    'opcgpro-vue/scripts/testrun-pressure-protocol.mjs','ops/qa/testrun-normal-resource-guard.py',finalizer,regression])
  for(const source of qa.qaSources){
    let current=read(path.join(REPO,source.path))
    if(source.path===finalizer){
      // The sole post-GREEN edit corrected this measurement-boundary comment.
      const text=current.toString('utf8')
      assert.equal(text.split('later WsClient.send timestamp.').length,2)
      current=Buffer.from(text.replace('later WsClient.send timestamp.','later native-send origin.'))
    }
    assert.equal(digest(current),source.sha256,'GREEN_QA_SOURCE_DRIFT')
  }
  const commit=git(['rev-parse','HEAD']).trim()
  const sources=[self,finalizer,regression,'ops/qa/toolchain-sources.json'].map(relative=>({path:relative,sha256:digest(read(path.join(REPO,relative)))}))
  if(write){
    assert.notEqual(commit,measuredCommit,'COMMIT_QA_CORRECTION_BEFORE_ADJUDICATION')
    assert.equal(git(['status','--porcelain=v1','--untracked-files=all']).trim(),'','CLEAN_ADJUDICATOR_COMMIT_REQUIRED')
    const allowed=new Set([...sources.map(row=>row.path),'docs/HANDOFF.md','docs/TASK-LEDGER.md','docs/BUGFIX-REGISTRY.md'])
    for(const relative of git(['-c','core.quotepath=false','diff','--name-only',measuredCommit,commit]).trim().split(/\r?\n/).filter(Boolean))
      assert.ok(allowed.has(relative),'UNRELATED_APPLICATION_CHANGE: '+relative)
    for(const row of sources)assert.equal(lf(git(['show',commit+':'+row.path])),lf(read(path.join(REPO,row.path)).toString('utf8')))
  }
  const result={schema:1,status:'EXISTING_LIVE_EVIDENCE_REVALIDATED',onlineCapacityAccepted:true,
    runId,measuredApplicationCommit:measuredCommit,adjudicatorCommit:commit,newOnlineExecution:false,
    reusedExistingLiveEvidence:true,originalReceiptPreserved:true,originalReceiptStatus:original.status,
    inputs:[...pins].map(([file,sha256])=>({path:file,sha256})),sources,offlineRegression:{cases:136,path:offline,sha256:digest(read(offline)),pinMutationCases},
    originalLimits:{confirmationMs:3000,lifecycleMs:900000,rooms:[10,20,30],stageSeconds:60,outsideUsers:10},
    measurements:{acceptedCommands:report.commands.overall.accepted,commandP95Ms:report.commands.overall.latencyMs.p95,
      maximumCommandMs:report.commands.overall.latencyMs.max,businessHttpRequests:Object.values(report.http).reduce((sum,row)=>sum+row.requests,0),
      terminalFrames:report.terminalFrames.length,maximumTerminalMs:Math.max(...report.terminalFrames.map(row=>row.sendToCallbackMs)),
      lateTerminalFrames:0,lifecycleMs:original.durationMs,roomsLeft:report.cleanup.roomsLeft,
      deletedAccounts:report.cleanup.syntheticAccounts.logicalDeletesConfirmed,revokedSessions:report.cleanup.sessionsRevoked},
    measurementBoundary:'Client command timestamp to message callback; not NIC, first-byte, one-way latency or rendered browser FPS.',
    scope:'This unchanged historical Linux isolated TEST run only; no production capacity, physical-device, long-soak or universal performance claim.',
    generalProspectiveValidatorStillDoesNotIssueAcceptance:true,networkCalls:0,formalWrites:0,maintenanceWrites:0}
  if(write){
    const pwsh=process.env.L12_PWSH||'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
    execFileSync(pwsh,['-NoProfile','-Command',`. '${REPO}/ops/windows/L12GeneratedStorage.ps1'; Assert-L12GeneratedPath -Path '${root}' -Purpose 'fixed historical load revalidation'`],
      {windowsHide:true,timeout:15000,stdio:'pipe'})
    // Reread every original immediately before the exclusive new write.
    for(const [file,sha] of pins)assert.equal(digest(read(file)),sha)
    assert.equal(git(['rev-parse','HEAD']).trim(),commit,'ADJUDICATOR_HEAD_CHANGED')
    assert.equal(git(['status','--porcelain=v1','--untracked-files=all']).trim(),'','ADJUDICATOR_SOURCE_CHANGED')
    for(const row of sources){
      const current=read(path.join(REPO,row.path))
      assert.equal(digest(current),row.sha256)
      assert.equal(lf(git(['show',commit+':'+row.path])),lf(current.toString('utf8')))
    }
    fs.writeFileSync(root+'/normal-clock-revalidation.json',JSON.stringify(result,null,2)+'\n',{flag:'wx'})
  }
  return {...result,onlineCapacityAccepted:write,status:write?result.status:'FIXED_REVALIDATION_CHECKS_PASSED_NOT_ISSUED',inputs:undefined,sources:undefined}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  assert.ok(process.argv.length===3&&['--check','--write'].includes(process.argv[2]),'Only fixed-run --check or --write is supported')
  console.log(JSON.stringify(revalidateFixedClockRun({write:process.argv[2]==='--write'})))
}
