import assert from 'node:assert/strict'
import fs from 'node:fs'

// Exercise the actual runner's correlation function without a browser, host,
// output directory, or wall-clock waiting. These are QA-oracle tests only.
const source = fs.readFileSync(new URL('./test-tutorial-ui.mjs', import.meta.url), 'utf8')
const planBegin=source.indexOf('function planWindowsHostRuntimeCopy(')
const planEnd=source.indexOf('const hostCopyMode=',planBegin)
assert(planBegin>=0&&planEnd>planBegin)
const planFactory=new Function('assert','path',source.slice(planBegin,planEnd)+'\nreturn planWindowsHostRuntimeCopy;')
const runtimePlan=planFactory(assert,{posix:{isAbsolute:value=>value.startsWith('/'),normalize:value=>{
  const parts=[]
  for(const part of value.split('/'))if(part==='..')parts.pop();else if(part!=='.'&&part!=='')parts.push(part)
  return parts.join('/')
},extname:value=>value.includes('.')?value.slice(value.lastIndexOf('.')):''}})
const runtimeTargets=[
  {relative:'runtimes/win-x64/native/e_sqlite3.dll',rid:'win-x64',assetType:'native'},
  {relative:'runtimes/win-x64/native/libSkiaSharp.dll',rid:'win-x64',assetType:'native'},
  {relative:'runtimes/win-x64/native/libSkiaSharp.pdb',rid:'win-x64',assetType:'native'},
  {relative:'runtimes/linux-x64/native/libSkiaSharp.so',rid:'linux-x64',assetType:'native'},
]
const runtimeInventory=[
  {relative:'runtimes/win-x64',kind:'directory'},{relative:'runtimes/linux-x64',kind:'directory'},
  {relative:'runtimes/win-x64/native',kind:'directory'},
  ...runtimeTargets.filter(asset=>asset.rid==='win-x64').map(asset=>({relative:asset.relative,kind:'file'})),
]
assert.deepEqual(runtimePlan(runtimeTargets,runtimeInventory).selectedRuntimeFiles,
  ['runtimes/win-x64/native/e_sqlite3.dll','runtimes/win-x64/native/libSkiaSharp.dll'])
for(const invalid of [
  {targets:runtimeTargets,inventory:[...runtimeInventory,{relative:'runtimes/plan9-x64',kind:'directory'}]},
  {targets:runtimeTargets,inventory:runtimeInventory.map((entry,index)=>index===0?{...entry,kind:'link'}:entry)},
  {targets:runtimeTargets,inventory:[...runtimeInventory,{relative:'runtimes/win-x64/native/injected.dll',kind:'file'}]},
  {targets:[...runtimeTargets,{relative:'runtimes/win-x64/../escape.dll',rid:'win-x64',assetType:'native'}],inventory:runtimeInventory},
]) assert.throws(()=>runtimePlan(invalid.targets,invalid.inventory))
const begin = source.indexOf('async function waitCommandAck(')
const end = source.indexOf('async function settleTo(', begin)
assert(begin >= 0 && end > begin)
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor
const exercise = new AsyncFunction('report', 'pause', 'Date', source.slice(begin, end)
  + '\nreturn waitCommandAck(0,entry=>entry.promptId==="bound-prompt",10,"synthetic oracle");')
const sent = { direction: 'sent', type: 'gameAction', command: 'resolvePrompt', requestId: 'bound-request', promptId: 'bound-prompt',
  cardInstanceIds: ['chosen-instance'], topCardInstanceIds: ['top-2','top-1'], bottomCardInstanceIds: [] }
const ack = { direction: 'received', type: 'gameState', requestId: 'bound-request', revision: 11 }
const cases = [
  { label: 'same-request-advance', rows: [sent, ack], passes: true },
  { label: 'normalized-delta-correlation', rows: [sent, { ...ack, wireType: 'gameStateDelta' }], passes: true },
  { label: 'different-request', rows: [sent, { ...ack, requestId: 'other' }] },
  { label: 'missing-both-request-ids', rows: [{ ...sent, requestId: undefined }, { ...ack, requestId: undefined }] },
  { label: 'empty-both-request-ids', rows: [{ ...sent, requestId: '' }, { ...ack, requestId: '' }] },
  { label: 'no-revision-advance', rows: [sent, { ...ack, revision: 10 }] },
  { label: 'old-revision', rows: [sent, { ...ack, revision: 9 }] },
  { label: 'other-prompt', rows: [{ ...sent, promptId: 'other-prompt' }, ack] },
  { label: 'explicit-rejection-not-ack', rows: [sent, { ...ack, type: 'actionRejected' }] },
]
for (const item of cases) {
  let clock = 0
  const run = () => exercise({ transport: item.rows }, async () => {}, { now: () => clock += 4000 })
  if (item.passes) {
    const result = await run()
    assert.equal(result.requestId, 'bound-request')
    assert.deepEqual(result.selectedValues, ['chosen-instance'])
    assert.deepEqual(result.topCardInstanceIds, ['top-2','top-1'])
    assert.deepEqual(result.bottomCardInstanceIds, [])
    assert.equal(result.ackRevision, 11)
  } else await assert.rejects(run, /No same-request authoritative ACK/)
}
const finalizerBegin=source.lastIndexOf('} finally {')+'} finally {'.length
const finalizerEnd=source.lastIndexOf('\n}')
assert(finalizerBegin>11&&finalizerEnd>finalizerBegin)
const finalize=new AsyncFunction('fs','path','out','report','page','context','pause','vite','hostDone','host','hostLog','process','console','execFileSync','cache',source.slice(finalizerBegin,finalizerEnd))
const never=()=>new Promise(()=>{})
for(const mode of ['closed','async-rejection','sync-throw','timeout','still-listening']) {
  const report={status:'GREEN',cases:[]},exit={exitCode:0},writes=[]
  const vite={httpServer:{listening:true},close:()=>{
    if(mode==='sync-throw')throw new Error('synthetic sync close')
    if(mode==='async-rejection')return Promise.reject(new Error('synthetic async close'))
    if(mode==='timeout')return never()
    if(mode==='closed')vite.httpServer.listening=false
    return Promise.resolve()
  }}
  await finalize({writeFileSync:(file,value)=>writes.push(JSON.parse(value)),closeSync(){}},{join:(...parts)=>parts.join('/')},
    'no-disk-output',report,null,null,ms=>mode==='timeout'&&ms===8000?Promise.resolve():never(),vite,true,{},0,exit,{log(){}},()=>{throw new Error('Unexpected shell')},'no-cache')
  assert.equal(report.viteClosed,mode==='closed')
  assert.equal(report.status,mode==='closed'?'GREEN':'FAILED')
  assert.equal(exit.exitCode,mode==='closed'?0:1)
  assert.equal(writes.length,2)
  assert.equal(writes[1].status,report.status)
  assert.equal(report.hostExited,true,'Vite failure does not skip the remaining finalizer')
}
const publicSource=fs.readFileSync(new URL('./verify-mobile-public-target-full-chain.mjs',import.meta.url),'utf8')
const publicBegin=publicSource.indexOf('async function waitAck(')
const publicEnd=publicSource.indexOf('async function selectPromptChoice(',publicBegin)
assert(publicBegin>=0&&publicEnd>publicBegin)
const publicAck=new AsyncFunction('entries','delay','Date',publicSource.slice(publicBegin,publicEnd)
  +'\nreturn waitAck(entries,0,command=>command.promptId==="bound-prompt",10,"synthetic public oracle");')
for(const item of cases) {
  let clock=0
  const rows=item.rows.map(entry=>entry.direction==='sent'
    ? {...entry,command:{type:'resolvePrompt',promptId:entry.promptId}}:entry)
  const run=()=>publicAck(rows,async()=>{},{now:()=>clock+=4000})
  if(item.passes) assert.equal((await run()).requestId,'bound-request')
  else await assert.rejects(run,/No same-request authoritative ACK/)
}
const closeBegin=publicSource.indexOf('async function closeOwnedUi(')
const closeEnd=publicSource.indexOf('async function runCase(',closeBegin)
assert(closeBegin>=0&&closeEnd>closeBegin)
const publicClose=new AsyncFunction('assert','delay','resources','result',
  publicSource.slice(closeBegin,closeEnd)+'\nawait closeOwnedUi(...resources,result);')
let publicCloseCases=0
for(const target of [0,1,2]) for(const mode of ['closed','sync-throw','async-rejection','timeout','still-open']) {
  const touched=[],result={}
  const resources=[0,1,2].map(index=>{
    let open=true
    const resource={httpServer:{listening:true},browser:()=>({isConnected:()=>open}),close:()=>{
      touched.push(index)
      if(index===target&&mode==='sync-throw')throw new Error('synthetic close')
      if(index===target&&mode==='async-rejection')return Promise.reject(new Error('synthetic close'))
      if(index===target&&mode==='timeout')return never()
      if(index!==target||mode==='closed'){open=false;resource.httpServer.listening=false}
      return Promise.resolve()
    }}
    return resource
  })
  await publicClose(assert,()=>new Promise(resolve=>setImmediate(resolve)),resources,result)
  assert.deepEqual(touched,[0,1,2],'One close failure cannot skip another owned resource')
  assert.equal(result.cleanupErrors.length,mode==='closed'?0:1)
  publicCloseCases++
}
const mandatorySource=fs.readFileSync(new URL('./verify-mobile-mandatory-target-full-chain.mjs',import.meta.url),'utf8')
const mandatoryCleanupBegin=mandatorySource.indexOf('function canRemoveMandatoryOwnedRoot(')
const mandatoryCleanupEnd=mandatorySource.indexOf('async function closeOwnedUi(',mandatoryCleanupBegin)
assert(mandatoryCleanupBegin>=0&&mandatoryCleanupEnd>mandatoryCleanupBegin)
const canRemoveMandatoryRoot=new Function(mandatorySource.slice(mandatoryCleanupBegin,mandatoryCleanupEnd)
  +'\nreturn canRemoveMandatoryOwnedRoot;')()
const cleanMandatory={cleanupErrors:[],hostExit:{code:0},gracefulHostStop:true}
const mandatoryCleanupCases=[
  [cleanMandatory,true],
  [{...cleanMandatory,cleanupErrors:['browser remains connected']},false],
  [{...cleanMandatory,cleanupErrors:['Vite remains listening']},false],
  [{...cleanMandatory,cleanupErrors:['host exit unknown']},false],
  [{...cleanMandatory,hostExit:{code:1}},false],
  [{...cleanMandatory,hostExit:null},false],
  [{...cleanMandatory,gracefulHostStop:false},false],
  [{...cleanMandatory,forcedStop:true},false],
]
for(const [state,expected] of mandatoryCleanupCases)assert.equal(canRemoveMandatoryRoot(state),expected)
assert.match(mandatorySource,/if \(canRemoveMandatoryOwnedRoot\(result\)\) result\.ownedRootUsage = removeOwnedRoot\(root, runId\)/)
console.log(JSON.stringify({ status: 'passed', cases: cases.length, viteFinalizerCases:5,
  publicAckCases:cases.length,publicCloseCases,runtimeCopyGuardCases:5,mandatoryCleanupCases:mandatoryCleanupCases.length,actualRunnerFunction: true,
  boundary: 'correlation oracle only; normalized-delta fixture does not test the codec or claim engine/touch coverage', browsers: 0, filesWritten: 0 }))
