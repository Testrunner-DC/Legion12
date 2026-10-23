import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import ts from 'typescript'
import {parse} from '@vue/compiler-sfc'
const front=path.resolve(import.meta.dirname,'..'),repo=path.dirname(front)
const source=fs.readFileSync(path.join(front,'src/l12/game/GmPanel.vue'),'utf8'),script=parse(source).descriptor.scriptSetup.content
assert.doesNotMatch(script,/\bfetch\s*\(/,'GM export must use the existing identity-bound shared request outlet')
assert.match(script,/import \{[^}]*platformRequest[^}]*platformState[^}]*\} from ['"]\.\.\/platform['"]/)
assert.match(script,/onBeforeUnmount\([\s\S]*?stopRecordExport\(\)/)
assert.match(script,/watch\(\[\(\) => props\.game\.matchId, \(\) => platformState\.token\], stopRecordExport\)/)
assert.match(source,/:disabled="exportingRecord" @click="exportRecord"/)
const ast=ts.createSourceFile('GmPanel.ts',script,ts.ScriptTarget.Latest,true)
const names=['stopRecordExport','exportRecord'],functions=ast.statements.filter(node=>ts.isFunctionDeclaration(node)&&names.includes(node.name?.text)).map(node=>node.getText(ast))
assert.equal(functions.length,2)
const executable=ts.transpileModule('let exportRecordController=null;const exportingRecord={value:false};'+functions.join('\n')+';globalThis.hooks={exportRecord,stopRecordExport,exportingRecord}',{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText
let cases=0
function fixture(){
 const calls=[],downloads=[],notices={notice:''},platformState={token:'synthetic-token'},props={game:{matchId:'synthetic-match'}},pending=[]
 const URL={createObjectURL(blob){downloads.push({blob});return 'blob:synthetic'},revokeObjectURL(value){assert.equal(value,'blob:synthetic')}}
 const context=vm.createContext({props,platformState,l12State:notices,URL,Blob,AbortController,Error,encodeURIComponent,
  document:{createElement(tag){assert.equal(tag,'a');return {click(){downloads.at(-1).clicked=true},set href(value){assert.equal(value,'blob:synthetic')},set download(value){downloads.at(-1).name=value}}}},
  platformRequest(route,options){calls.push({route,options});return new Promise((resolve,reject)=>pending.push({resolve,reject}))}})
 vm.runInContext(executable,context)
 return {hooks:context.hooks,props,platformState,calls,downloads,pending,notices}
}
{
 const f=fixture(),run=f.hooks.exportRecord();assert.equal(f.calls.length,1);assert.equal(f.calls[0].route,'/api/matches/synthetic-match');assert.equal(f.hooks.exportingRecord.value,true)
 f.pending[0].resolve({matchId:'synthetic-match',events:[1,2]});await run;assert.equal(f.downloads.length,1);assert.equal(f.downloads[0].clicked,true)
 assert.equal(await f.downloads[0].blob.text(),JSON.stringify({matchId:'synthetic-match',events:[1,2]},null,2));assert.equal(f.hooks.exportingRecord.value,false);cases++
}
{
 const f=fixture(),first=f.hooks.exportRecord();await f.hooks.exportRecord();assert.equal(f.calls.length,1);f.pending[0].resolve({});await first
 const next=f.hooks.exportRecord();assert.equal(f.calls.length,2);f.pending[1].resolve({});await next;cases++
}
for(const retire of [f=>f.hooks.stopRecordExport(),f=>{f.props.game.matchId='another-match'},f=>{f.platformState.token='another-synthetic-token'}]){
 const f=fixture(),run=f.hooks.exportRecord();retire(f);f.pending[0].resolve({privateSyntheticData:'must-not-download'});await run
 assert.equal(f.downloads.length,0);assert.equal(f.notices.notice,'');cases++
}
{
 const f=fixture(),first=f.hooks.exportRecord();f.hooks.stopRecordExport();assert.equal(f.calls[0].options.signal.aborted,true)
 const second=f.hooks.exportRecord();f.pending[0].resolve({});await first;assert.equal(f.hooks.exportingRecord.value,true)
 f.pending[1].resolve({});await second;assert.equal(f.downloads.length,1);cases++
}
{
 const f=fixture(),run=f.hooks.exportRecord();f.pending[0].reject(new Error('synthetic-read-failure'));await run
 assert.equal(f.notices.notice,'synthetic-read-failure');assert.equal(f.hooks.exportingRecord.value,false);assert.equal(f.calls.length,1);cases++
}
{
 const f=fixture();f.props.game.matchId='a/b?x#y';const run=f.hooks.exportRecord();assert.equal(f.calls[0].route,'/api/matches/a%2Fb%3Fx%23y');f.pending[0].resolve({});await run;cases++
}
assert.equal(fs.existsSync(path.join(front,'src/pages/ReplayPage.vue')),false,'Retired unreferenced M6 stub must not return')
const router=fs.readFileSync(path.join(front,'src/router/index.ts'),'utf8');assert.doesNotMatch(router,/pages\/ReplayPage/);assert.equal((router.match(/import\('@\/l12\/ReplayPage\.vue'\)/g)??[]).length,3)
assert.deepEqual(JSON.parse(fs.readFileSync(path.join(repo,'ops/performance-exceptions.json'))).exceptions,[])
console.log(JSON.stringify({status:'passed',behaviorCases:cases,realSfcFunctions:true,sharedRequestOutlet:true,legacyReplayRoutesPreserved:3,expiredExceptionsRemaining:0,realNetworkCalls:0}))
