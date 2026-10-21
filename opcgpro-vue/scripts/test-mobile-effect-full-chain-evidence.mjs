import assert from 'node:assert/strict'
import fs from 'node:fs'

// Exercise the actual runner's correlation function without a browser, host,
// output directory, or wall-clock waiting. These are QA-oracle tests only.
const source = fs.readFileSync(new URL('./test-tutorial-ui.mjs', import.meta.url), 'utf8')
const begin = source.indexOf('async function waitCommandAck(')
const end = source.indexOf('async function settleTo(', begin)
assert(begin >= 0 && end > begin)
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor
const exercise = new AsyncFunction('report', 'pause', 'Date', source.slice(begin, end)
  + '\nreturn waitCommandAck(0,entry=>entry.promptId==="bound-prompt",10,"synthetic oracle");')
const sent = { direction: 'sent', type: 'gameAction', command: 'resolvePrompt', requestId: 'bound-request', promptId: 'bound-prompt', cardInstanceIds: ['chosen-instance'] }
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
console.log(JSON.stringify({ status: 'passed', cases: cases.length, viteFinalizerCases:5, actualRunnerFunction: true,
  boundary: 'correlation oracle only; normalized-delta fixture does not test the codec or claim engine/touch coverage', browsers: 0, filesWritten: 0 }))
