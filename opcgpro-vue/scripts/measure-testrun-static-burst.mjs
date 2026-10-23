import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import dns from 'node:dns/promises'
import { execFileSync } from 'node:child_process'
import { monitorEventLoopDelay } from 'node:perf_hooks'
import { pathToFileURL } from 'node:url'

const repo=path.resolve(import.meta.dirname,'../..')
const trusted='D:/GPT/Legion12/cache/primary/master-art-deck-copy-20261023/r1/trusted-card-assets'
const source='D:/GPT/Legion12/artifacts/representative-load-current-20261022/terminal-v4-live-r2/load/pressure-report.json'
const relative='cards/20260912-batch308/S01-01M1A/3fc3fc13fec9558bd7ef/original.webp'
const expected='C417E2AA2906AB762609A986FEA698E6F77A123E3FED7D7AFFC8D43BB50C8668'
const expectedSource='E43083FFF80EA583E7351B01E5A95BE39A78EC7B6D40F4325848AFA9CE0A335C'
const sha=bytes=>crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()

export function terminalBurstSizes(report) {
  assert.deepEqual(report.expectedCommit,{value:'2eda18c115b0810296cb9c35cfebf60c35b120e5',
    source:'exact-normal-binding-and-actual-running-process'})
  assert.deepEqual(report.target,{baseUrl:'https://legion-12.com/testrun/',productionTouched:false})
  assert.ok(Array.isArray(report.terminalFrames) && report.terminalFrames.length===60)
  const slots=new Set()
  for(const row of report.terminalFrames) {
    assert.ok(Number.isSafeInteger(row.numericClientSlot)&&row.numericClientSlot>=0&&row.numericClientSlot<60)
    assert.ok(!slots.has(row.numericClientSlot));slots.add(row.numericClientSlot)
    assert.ok(Number.isSafeInteger(row.wireBytes)&&row.wireBytes>0&&row.wireBytes<=318436)
    assert.equal(row.terminal,true)
  }
  const sizes=report.terminalFrames.toSorted((a,b)=>a.numericClientSlot-b.numericClientSlot).map(row=>row.wireBytes)
  assert.ok(sizes.reduce((sum,value)=>sum+value,0)+sizes[0]<=16*1024**2,'STATIC_CONTROL_BYTE_LIMIT')
  return sizes
}

export function boundTerminalBurst(bytes) {
  assert.equal(sha(bytes),expectedSource,'EXACT_ORIGINAL_CAPTURE_REQUIRED')
  return terminalBurstSizes(JSON.parse(bytes))
}

export async function measureStaticBurst(output) {
  assert.equal(path.resolve(output),path.resolve('D:/GPT/Legion12/artifacts/stage-goals-20261023/static-burst-60'))
  const env={...process.env,L12_STATIC_BURST_OUT:output,L12_STATIC_BURST_REPO:repo}
  execFileSync(process.env.L12_PWSH||'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
    ['-NoProfile','-NonInteractive','-Command','. "$env:L12_STATIC_BURST_REPO/ops/windows/L12GeneratedStorage.ps1"; $null=Assert-L12GeneratedPath -Path $env:L12_STATIC_BURST_OUT -Purpose "bounded static transport control, no accounts or games"'],
    {env,windowsHide:true,stdio:'pipe',timeout:15000})
  assert.ok(!fs.existsSync(output),'FRESH_CONTROL_OUTPUT_REQUIRED')
  const raw=fs.readFileSync(source),sizes=boundTerminalBurst(raw)
  const original=fs.readFileSync(path.join(trusted,relative))
  assert.equal(original.length,318436);assert.equal(sha(original),expected)
  const addresses=await dns.lookup('legion-12.com',{all:true})
  assert.ok(addresses.length>0 && addresses.every(row=>row.family===4&&row.address==='154.201.80.91'),'EXACT_DIRECT_HOST_REQUIRED')
  fs.mkdirSync(output)
  const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),30000)
  const loop=monitorEventLoopDelay({resolution:10});loop.enable()
  const cpu=process.cpuUsage(),began=performance.now(),rows=[],errors=[]
  let requests=0
  async function read(size,index,wave) {
    const started=performance.now();requests++
    const response=await fetch('https://legion-12.com/card-assets/'+relative,{
      method:'GET',headers:{Range:`bytes=0-${size-1}`,'Accept-Encoding':'identity'},
      redirect:'error',credentials:'omit',signal:AbortSignal.any([controller.signal,AbortSignal.timeout(15000)])})
    const headersAt=performance.now()
    assert.equal(response.status,206,'EXACT_RANGE_RESPONSE_REQUIRED')
    assert.equal(response.headers.get('content-range'),`bytes 0-${size-1}/318436`)
    assert.equal(Number(response.headers.get('content-length')),size)
    assert.ok([null,'identity'].includes(response.headers.get('content-encoding')))
    let count=0;const hash=crypto.createHash('sha256'),reader=response.body.getReader()
    for(;;){const {done,value}=await reader.read();if(done)break;count+=value.byteLength;assert.ok(count<=size);hash.update(value)}
    assert.equal(count,size);assert.equal(hash.digest('hex').toUpperCase(),sha(original.subarray(0,size)))
    const ended=performance.now()
    return {index,wave,bytes:count,headersMs:headersAt-started,bodyMs:ended-headersAt,totalMs:ended-started}
  }
  let burstWallMs=null
  try {
    rows.push(await read(sizes[0],0,'range-qualification'))
    const waveStart=performance.now()
    const batch=await Promise.allSettled(sizes.map((size,index)=>read(size,index,'sixty-matched-ranges')))
    burstWallMs=performance.now()-waveStart
    for(const item of batch)if(item.status==='fulfilled')rows.push(item.value);else errors.push('STATIC_RANGE_CONTROL_FAILED')
  }catch{errors.push('STATIC_RANGE_QUALIFICATION_FAILED');controller.abort()}
  finally{clearTimeout(timer);loop.disable()}
  const cpuSpent=process.cpuUsage(cpu),burst=rows.filter(row=>row.wave==='sixty-matched-ranges')
  const result={schema:1,status:errors.length?'CONTROL_INCOMPLETE':'STATIC_BURST_CONTROL_COMPLETE',
    sourceSha256:sha(raw),sourceCandidate:'2eda18c115b0810296cb9c35cfebf60c35b120e5',
    sourceFixtureIncomplete:true,originalResultReclassified:false,
    directHostConfirmed:true,node:process.versions.node,undici:process.versions.undici,
    concurrency:60,plannedBurstBytes:sizes.reduce((sum,value)=>sum+value,0),staticHttpRequests:requests,
    completedBurstRequests:burst.length,burstWallMs,maxBurstRequestMs:burst.length?Math.max(...burst.map(row=>row.totalMs)):null,
    burstRequestsOver3Seconds:burst.filter(row=>row.totalMs>3000).length,
    maximumClientLoopDelayMs:loop.max/1e6,clientCpuMs:(cpuSpent.user+cpuSpent.system)/1000,
    elapsedMs:performance.now()-began,rows,errors,gameAccountApiRequests:0,serviceChanges:0,maintenanceWrites:0,
    boundary:'Same-host static HTTP range control of the sixty actual terminal wire sizes. Not WebSocket/gameplay capacity, server-side TCP attribution, 30+10 qualification or a change to the 3-second gate.'}
  fs.writeFileSync(path.join(output,'static-burst.json'),JSON.stringify(result,null,2),{flag:'wx'})
  console.log(JSON.stringify(Object.fromEntries(Object.entries(result).filter(([key])=>key!=='rows')),null,2))
  return result
}

if(process.argv[1]&&pathToFileURL(path.resolve(process.argv[1])).href===import.meta.url) {
  const result=await measureStaticBurst(process.argv[2])
  if(result.status!=='STATIC_BURST_CONTROL_COMPLETE')process.exitCode=1
}
