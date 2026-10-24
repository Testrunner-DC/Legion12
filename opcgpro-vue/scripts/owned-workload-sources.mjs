import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {pathToFileURL} from 'node:url'

export const OWNED_WORKLOAD_REPO=path.resolve(import.meta.dirname,'../..')
export const FROZEN_WORKLOAD_SHA='F737052DCBFDA4A29FF53BA5DFAD76E771774C1C89C993275FFAFEAA023684FD'
const rows=[
  {
    "id": "core",
    "path": "ops/qa/workload/pressure-load-r7-approved-preparation-cadence-r6.mjs",
    "sha256": "F737052DCBFDA4A29FF53BA5DFAD76E771774C1C89C993275FFAFEAA023684FD",
    "originalPath": "D:/GPT/Legion12/artifacts/performance-measurement-recovery-20261020/preparation-cadence-r6/pressure-load-r7-approved-preparation-cadence-r6.mjs",
    "originalSha256": "F737052DCBFDA4A29FF53BA5DFAD76E771774C1C89C993275FFAFEAA023684FD",
    "transformations": []
  },
  {
    "id": "full",
    "path": "ops/qa/workload/full-workload-contract-r7.mjs",
    "sha256": "9D69F1B879B71C7A18FEB02CBD9861D50732D56A0FF3A40A4E69326343C2BEA3",
    "originalPath": "D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/full-workload-contract-r7.mjs",
    "originalSha256": "92497819602F4C2719FE80DF29FD5CF7626AA410F2D816058FF97504E4724BFA",
    "transformations": [
      {
        "before": "'file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/large-terminal-contract-r6.mjs'",
        "after": "'./cohort30-candidate-frame-r6/large-terminal-contract-r6.mjs'",
        "count": 1
      },
      {
        "before": "'file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/cohort30-contract-r6.mjs'",
        "after": "'./cohort30-candidate-frame-r6/cohort30-contract-r6.mjs'",
        "count": 1
      },
      {
        "before": "\". 'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/ops/windows/L12GeneratedStorage.ps1';",
        "after": "\". '\"+fileURLToPath(new URL('../../windows/L12GeneratedStorage.ps1',import.meta.url)).replaceAll(\"'\",\"''\")+\"';",
        "count": 1
      }
    ]
  },
  {
    "id": "pure",
    "path": "ops/qa/workload/load-v2-r2/pressure-pure-selftest.mjs",
    "sha256": "C431C0BF87072655487DDF6C103062D9DC46E20FCDC127AB435BF44F98C03838",
    "originalPath": "C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/load-v2-r2/pressure-pure-selftest.mjs",
    "originalSha256": "C431C0BF87072655487DDF6C103062D9DC46E20FCDC127AB435BF44F98C03838",
    "transformations": []
  },
  {
    "id": "r6",
    "path": "ops/qa/workload/cohort30-candidate-frame-r6/cohort30-terminal-v2-r6.mjs",
    "sha256": "CB86280CD5124179BA89A7A834CA8B7CC2B546BD42ED1BEA6F8033AD1F18854C",
    "originalPath": "D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/cohort30-terminal-v2-r6.mjs",
    "originalSha256": "D0A3E0AA64156E95DC60C40BA197339B5A315316A2E5BDC5FAD3DAA9E76C4531",
    "transformations": [
      {
        "before": "'file:///C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/client-boundaries.mjs'",
        "after": "'../client-boundaries.mjs'",
        "count": 1
      },
      {
        "before": "'file:///C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/load-v2-r2/pressure-pure-selftest.mjs'",
        "after": "'../load-v2-r2/pressure-pure-selftest.mjs'",
        "count": 1
      },
      {
        "before": "'file:///C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/cohort30-terminal-v2/placement-selector-r1.mjs'",
        "after": "'../cohort30-terminal-v2/placement-selector-r1.mjs'",
        "count": 1
      },
      {
        "before": "'file:///C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/cohort30-terminal-v2/ledger-r5/account-ledger-client-r2.mjs'",
        "after": "'../../ledger/account-ledger-client-r2.mjs'",
        "count": 1
      },
      {
        "before": "const LEDGER_SOURCE = 'C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/cohort30-terminal-v2/ledger-r5/account-ledger-client-r2.mjs'",
        "after": "const LEDGER_SOURCE = fileURLToPath(new URL('../../ledger/account-ledger-client-r2.mjs',import.meta.url))",
        "count": 1
      },
      {
        "before": "const TOOL_ROOT = path.dirname(fileURLToPath(import.meta.url))",
        "after": "const TOOL_ROOT = path.dirname(fileURLToPath('file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/cohort30-terminal-v2-r6.mjs'))",
        "count": 1
      },
      {
        "before": "const FROZEN_ROOT = 'C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2'",
        "after": "const FROZEN_ROOT = path.resolve(fileURLToPath(new URL('..',import.meta.url)))",
        "count": 1
      },
      {
        "before": "const TYPESCRIPT_SOURCE = 'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/opcgpro-vue/node_modules/typescript/lib/typescript.js'",
        "after": "const TYPESCRIPT_SOURCE = fileURLToPath(new URL('../../../../opcgpro-vue/node_modules/typescript/lib/typescript.js',import.meta.url))",
        "count": 1
      },
      {
        "before": "\". 'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/ops/windows/L12GeneratedStorage.ps1';",
        "after": "\". '\"+fileURLToPath(new URL('../../../windows/L12GeneratedStorage.ps1',import.meta.url)).replaceAll(\"'\",\"''\")+\"';",
        "count": 1
      }
    ]
  },
  {
    "id": "notes",
    "path": "ops/qa/workload/cohort30-candidate-frame-r6/large-terminal-contract-notes-base-r3.mjs",
    "sha256": "DFC4FD2AF1C87EE987212A652704FC35961B4D00C14D9797984835545F8BD756",
    "originalPath": "D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/large-terminal-contract-notes-base-r3.mjs",
    "originalSha256": "DFC4FD2AF1C87EE987212A652704FC35961B4D00C14D9797984835545F8BD756",
    "transformations": []
  },
  {
    "id": "contract",
    "path": "ops/qa/workload/cohort30-candidate-frame-r6/large-terminal-contract-r6.mjs",
    "sha256": "EC10922545EECB76831A79C5B65935D2EFC6AAAA945FAC9646F38AC1CE512841",
    "originalPath": "D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/large-terminal-contract-r6.mjs",
    "originalSha256": "EC10922545EECB76831A79C5B65935D2EFC6AAAA945FAC9646F38AC1CE512841",
    "transformations": []
  },
  {
    "id": "cohort",
    "path": "ops/qa/workload/cohort30-candidate-frame-r6/cohort30-contract-r6.mjs",
    "sha256": "0D8F8DF28798B0A3C4AA4A0775766ABAF4BFDC0A3088BC9786DEF9406B60176D",
    "originalPath": "D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/cohort30-contract-r6.mjs",
    "originalSha256": "0D8F8DF28798B0A3C4AA4A0775766ABAF4BFDC0A3088BC9786DEF9406B60176D",
    "transformations": []
  },
  {
    "id": "markers",
    "path": "ops/qa/workload/load-r7-markers.mjs",
    "sha256": "B3F1DFAA598013810269F2F973B4BEE19DB20EC455766427A26543A5D2A5985E",
    "originalPath": "D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/load-r7-markers.mjs",
    "originalSha256": "41544DFB2A7327239ED4EF55BD654D7CA352A3FF9621F7676CB7F40CFBD1D4D9",
    "transformations": [
      {
        "before": "'file:///C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/client-boundaries.mjs'",
        "after": "'./client-boundaries.mjs'",
        "count": 1
      }
    ]
  },
  {
    "id": "boundaries",
    "path": "ops/qa/workload/client-boundaries.mjs",
    "sha256": "E62BF9BCEC47C06727CA75D3ADF5C278E4FB5ACD6F0302E40531262A1DED06AB",
    "originalPath": "C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/client-boundaries.mjs",
    "originalSha256": "E62BF9BCEC47C06727CA75D3ADF5C278E4FB5ACD6F0302E40531262A1DED06AB",
    "transformations": []
  },
  {
    "id": "selector",
    "path": "ops/qa/workload/cohort30-terminal-v2/placement-selector-r1.mjs",
    "sha256": "94222135D91627B18C1D13067A4A141C6AAA901A090C79980C882B5ED5449AF1",
    "originalPath": "C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/cohort30-terminal-v2/placement-selector-r1.mjs",
    "originalSha256": "94222135D91627B18C1D13067A4A141C6AAA901A090C79980C882B5ED5449AF1",
    "transformations": []
  },
  {
    "id": "publicContract",
    "path": "ops/qa/workload/large-terminal-public/large-terminal-contract.mjs",
    "sha256": "D328933729E54167078F123329A0880981C8C5A55771E32865F77B9DEAE1B456",
    "originalPath": "C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/large-terminal-public/large-terminal-contract.mjs",
    "originalSha256": "D328933729E54167078F123329A0880981C8C5A55771E32865F77B9DEAE1B456",
    "transformations": []
  }
]
export const OWNED_WORKLOAD_SOURCES=Object.freeze(rows.map(row=>Object.freeze({...row,transformations:Object.freeze(row.transformations.map(Object.freeze))})))
export const ownedWorkloadFile=id=>{
 const row=OWNED_WORKLOAD_SOURCES.find(row=>row.id===id)
 assert.ok(row,'UNKNOWN_OWNED_WORKLOAD_SOURCE')
 return path.join(OWNED_WORKLOAD_REPO,row.path)
}
export const OWNED_FROZEN_CORE=ownedWorkloadFile('core')
const hash=bytes=>crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()
const plainRead=file=>{
 const stat=fs.lstatSync(file)
 assert.ok(stat.isFile()&&!stat.isSymbolicLink()&&stat.size>0&&stat.size<=256*1024,'WORKLOAD_SOURCE_NOT_BOUNDED_REGULAR_FILE')
 return fs.readFileSync(file)
}
const replaceExact=(source,before,after,count)=>{
 assert.ok(typeof source==='string'&&typeof before==='string'&&before.length>0&&typeof after==='string')
 assert.equal(source.split(before).length-1,count,'WORKLOAD_PATH_ANCHOR_COUNT_CHANGED')
 return source.split(before).join(after)
}
export function restoreOwnedWorkloadSource(row,bytes){
 assert.equal(hash(bytes),row.sha256,'OWNED_WORKLOAD_SOURCE_DRIFT')
 let source=bytes.toString('utf8')
 for(const step of row.transformations.toReversed())source=replaceExact(source,step.after,step.before,step.count)
 assert.equal(hash(Buffer.from(source)),row.originalSha256,'WORKLOAD_ORIGINAL_REVERSE_DRIFT')
 return source
}
export function inspectOwnedWorkloadSources({read=plainRead}={}){
 const result=[]
 for(const row of OWNED_WORKLOAD_SOURCES){
  const bytes=read(path.join(OWNED_WORKLOAD_REPO,row.path))
  assert.ok(Buffer.isBuffer(bytes)&&bytes.length>0&&bytes.length<=256*1024,'WORKLOAD_SOURCE_BYTES_INVALID')
  restoreOwnedWorkloadSource(row,bytes)
  result.push({id:row.id,path:row.path,sha256:row.sha256,originalSha256:row.originalSha256,byteExact:row.transformations.length===0,pathTransformations:row.transformations.length})
 }
 return {status:'passed',sources:result,byteExactSources:result.filter(row=>row.byteExact).length,pathDerivedSources:result.filter(row=>!row.byteExact).length,legacyCodeReads:0}
}
export function restoreRebasedWorkloadSource(source,steps){
 let result=source
 for(const step of steps.toReversed())result=replaceExact(result,step.after,step.before,step.count)
 return result
}
const CORE_GUARD_COMMAND=". 'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/ops/windows/L12GeneratedStorage.ps1'; $null=Assert-L12GeneratedPath -Path 'D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7' -Purpose 'r7 approved local workload tool'"
// Only source-code locations are rebound. The original TOOL_ROOT, external
// evidence/credential/ready-file constraints and import.meta checkpoint identity
// remain untouched. The two existing protocol adapters still own admission.
export function rebaseFrozenWorkloadSource(source,adapterUrl,{mode}={}){
 assert.ok(mode==='normal'||mode==='local','WORKLOAD_REBASE_MODE_INVALID')
 inspectOwnedWorkloadSources()
 const rowsById=new Map(OWNED_WORKLOAD_SOURCES.map(row=>[row.id,row]))
 const url=id=>pathToFileURL(ownedWorkloadFile(id)).href
 const steps=[]
 const apply=(before,after,count=1)=>{source=replaceExact(source,before,after,count);steps.push({before,after,count})}
 const moduleIds=['full','pure','r6','notes','markers','selector']
 for(const id of moduleIds){const row=rowsById.get(id);apply("'file:///"+row.originalPath+"'",JSON.stringify(url(id)),id==='full'?2:1)}
 const ownUrl=relative=>pathToFileURL(path.join(OWNED_WORKLOAD_REPO,relative)).href
 apply("'file:///D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/scripts/lib/l12-pressure-checkpoint.mjs'",JSON.stringify(ownUrl('scripts/lib/l12-pressure-checkpoint.mjs')))
 apply("'file:///C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/load-v2-r2/../cohort30-terminal-v2/ledger-r5/account-ledger-client-r2.mjs'",JSON.stringify(ownUrl('ops/qa/ledger/account-ledger-client-r2.mjs')))
 const oldBurst="'file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/ws-burst-r7.mjs'"
 if(mode==='local')apply(oldBurst,JSON.stringify(new URL('./ws-burst-observer.mjs',adapterUrl).href))
 else assert.ok(!source.includes(oldBurst),'NORMAL_OBSERVER_WAS_NOT_REBOUND')
 for(const id of ['selector','r6']){const row=rowsById.get(id);apply("'"+row.originalPath+"'",JSON.stringify(ownedWorkloadFile(id)))}
 apply("'"+rowsById.get('r6').originalSha256+"'","'"+rowsById.get('r6').sha256+"'")
 apply("'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/opcgpro-vue/node_modules/typescript/lib/typescript.js'",JSON.stringify(path.join(OWNED_WORKLOAD_REPO,'opcgpro-vue/node_modules/typescript/lib/typescript.js')))
 const guard=path.join(OWNED_WORKLOAD_REPO,'ops/windows/L12GeneratedStorage.ps1').replaceAll('\\','/').replaceAll("'","''")
 const relocatedCommand=CORE_GUARD_COMMAND.replace('D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/ops/windows/L12GeneratedStorage.ps1',guard)
 apply(JSON.stringify(CORE_GUARD_COMMAND),JSON.stringify(relocatedCommand))
 const restored=restoreRebasedWorkloadSource(source,steps)
 return {source,restored,steps,evidence:{mode,relocatedSourceSha256:hash(Buffer.from(source)),restoredPreparedSha256:hash(Buffer.from(restored)),byteExactPathRoundtrip:true,
  sourceBindings:OWNED_WORKLOAD_SOURCES.map(({id,sha256,originalSha256})=>({id,sha256,originalSha256})),
  pathChanges:steps.map(({before,after,count})=>({beforeSha256:hash(Buffer.from(before)),afterSha256:hash(Buffer.from(after)),count})),
  originalDataRootRetained:true,checkpointUsesActualGeneratedModule:true}}
}
