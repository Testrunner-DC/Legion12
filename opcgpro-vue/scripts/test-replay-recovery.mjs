import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const here=path.dirname(fileURLToPath(import.meta.url))
const root=process.env.L12_REPLAY_SOURCE_ROOT || path.resolve(here,'../src/l12')
const require=createRequire(process.env.L12_REPLAY_TOOLING_PACKAGE || path.resolve(here,'../package.json'))
const ts=require('typescript'),vue=require('vue')
const results=[]
async function test(name,run){try{await run();results.push({name,passed:true})}catch(error){results.push({name,passed:false,error:error.stack})}}
function source(name){return fs.readFileSync(path.join(root,name),'utf8')}
async function module(name){return import('data:text/javascript;base64,'+Buffer.from(ts.transpileModule(source(name),{compilerOptions:{module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022}}).outputText).toString('base64'))}
const model=await module('replayModel.ts')
const key=(name,camel)=>camel?name[0].toLowerCase()+name.slice(1):name
function state(step=0,camel=false,god=true,tapped=false){
 const player=owner=>Object.fromEntries(Object.entries({PlayerIndex:owner,Name:'public-'+owner,Faction:'olympus',MasterId:'MASTER-'+owner,Hp:8,MaxHp:8,Library:[],Hand:[],MoraleDeck:[],Morale:[Object.fromEntries(Object.entries({InstanceId:'resource-'+owner,CardId:'MORALE',IsGodPower:god,Tapped:tapped,CannotUntapAtNextReset:true,CannotUntapUntilRound:4}).map(([k,v])=>[key(k,camel),v]))],Field:[[null,null,null],[null,null,null]],SpecialZones:{},Graveyard:[]}).map(([k,v])=>[key(k,camel),v]))
 return Object.fromEntries(Object.entries({MatchId:'synthetic-only',Phase:'Main',Round:Math.floor(step/4)+1,ActivePlayer:step%2,Players:[player(0),player(1)],Events:[]}).map(([k,v])=>[key(k,camel),v]))
}
const command=(step,raw=state(step))=>({sequence:step+1,receivedUtc:'2026-10-16T00:00:00Z',playerIndex:0,command:{type:'passPriority'},accepted:true,revision:step+1,stateHash:'hash-'+step,state:raw})
const detail=commands=>({match:{matchId:'synthetic-only',player0:'A',player1:'B',commandCount:commands.length},viewerPlayerIndex:0,commands})
for(const camel of [false,true])for(const god of [false,true])for(const tapped of [false,true])for(const owner of [0,1]){
 await test(`resource/${camel}/${god}/${tapped}/${owner}`,()=>{
  const input=detail([command(0,state(0,camel,god,tapped))]),before=JSON.stringify(input)
  const resource=model.replayGameAt(input,0).players[owner].morale[0]
  assert.equal(Boolean(resource.isGodPower),god);assert.equal(resource.tapped,tapped)
  assert.equal(resource.cannotUntapAtNextReset,true);assert.equal(resource.cannotUntapUntilRound,4)
  assert.equal(JSON.stringify(input),before)
 })
}
for(const camel of [false,true])await test(`movement origin replay mapping/${camel?'camel':'pascal'}`,()=>{
 const raw=state(0,camel),event=Object.fromEntries(Object.entries({Sequence:7,Type:'discard',PlayerIndex:1,Text:'公开弃置',OriginZone:'hand',Cards:[]}).map(([k,v])=>[key(k,camel),v]))
 raw[key('Events',camel)]=[event]
 const input=detail([command(0,raw)]),before=JSON.stringify(input)
 const movement=model.replayGameAt(input,0).recentEvents[0]
 assert.equal(movement.originZone,'hand')
 assert.equal(JSON.stringify(input),before)
})
await test('legacy replay event keeps missing movement origin undefined',()=>{
 const raw=state(),event={Sequence:8,Type:'discard',PlayerIndex:1,Text:'历史公开弃置',Cards:[]}
 raw.Events=[event]
 assert.equal(model.replayGameAt(detail([command(0,raw)]),0).recentEvents[0].originZone,undefined)
})
const pageSource=source('ReplayPage.vue'),script=pageSource.match(/<script setup[^>]*>([\s\S]*?)<\/script>/)[1]
const ast=ts.createSourceFile('ReplayPage.ts',script,ts.ScriptTarget.Latest,true,ts.ScriptKind.TS)
const functions=names=>ast.statements.filter(n=>ts.isFunctionDeclaration(n)&&names.includes(n.name?.text)).map(n=>n.getText(ast)).join('\n')
for(const [speed,expected]of[[1,3000],[2,1500],[3,1000]])await test(`actual timer/${speed}`,()=>{
 const delays=[],context={playing:{value:true},playbackGeneration:1,playbackSpeed:{value:speed},timer:null,window:{setTimeout:(fn,delay)=>{delays.push(delay);return 1}},Math}
 vm.createContext(context);vm.runInContext(ts.transpileModule(functions(['schedulePlaybackAdvance']),{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText,context)
 vm.runInContext('schedulePlaybackAdvance(1)',context);assert.equal(delays[0],expected)
})
if(process.argv.includes('--baseline')){
 await test('actual legacy continuation clears recovered page error',async()=>{
  class RequestError extends Error{constructor(){super('synthetic503');this.status=503;this.code='storage_unavailable'}}
  const e={Error,PlatformRequestError:RequestError,isAdminReplay:{value:true},detail:{value:{commands:Array(2).fill({})}},replayNextCursor:{value:'p1'},loadingReplayPage:{value:false},replayTotalCommands:{value:4},disposed:false,route:{params:{matchId:'synthetic-only'}},error:{value:''},indexError:{value:''},playing:{value:true},playbackGeneration:1,timer:null,window:{clearTimeout(){}},adminApi:{replayPage:async()=>{throw new RequestError()}}}
  vm.createContext(e);vm.runInContext(ts.transpileModule(functions(['ensureReplayStepLoaded','stop','clearPlaybackTimer']),{}).outputText,e)
  await vm.runInContext('ensureReplayStepLoaded(2)',e)
  e.adminApi.replayPage=async()=>({items:[{}],nextCursor:null,totalCommands:3})
  await vm.runInContext('ensureReplayStepLoaded(2)',e);assert.equal(e.error.value,'')
 })
}else{
 const {ReplayArchiveLoader,classifyReplayReadFailure}=await module('replayArchiveLoader.ts')
 const index=await module('replayIndex.ts')
 class RequestError extends Error{constructor(status,code,retryAfterMs=0){super('synthetic-'+code);Object.assign(this,{status,code,retryAfterMs})}}
 const total=16,all=Array.from({length:total},(_,i)=>command(i))
 const makePage=(n,commands=all)=>({items:commands.slice(n*2,n*2+2),totalCommands:commands.length,nextCursor:(n+1)*2<commands.length?'p'+(n+1):null,pageBytes:500})
 const pageNumber=cursor=>cursor===undefined?0:Number(cursor.slice(1))
 const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b});return{promise,resolve,reject}}
 const flush=async()=>{for(let i=0;i<12;i++)await Promise.resolve();await vue.nextTick()}
 const create=(read,options={})=>new ReplayArchiveLoader(read,{matchId:'synthetic-only',sleep:async()=>{},...options})
 for(const [status,code,terminal,denied] of [[0,'network_error',false,false],[0,'request_timeout',false,false],[503,'storage_unavailable',false,false],[409,'storage_conflict',false,false],[429,'rate_limited',false,false],[409,'replay_incompatible',true,false],[410,'sandbox_replay_expired',true,false],[401,'unauthorized',true,true],[403,'forbidden',true,true],[0,'stale_session',true,true]])await test(`classification/${status}/${code}`,()=>{
  const issue=classifyReplayReadFailure(new RequestError(status,code));assert.equal(issue.terminal,terminal);assert.equal(issue.denied,denied)
 })
 await test('concurrent requests share one cursor read and retry without duplicate frames',async()=>{
  const wait=deferred();let calls=0,indexCalls=0
  const loader=create(async()=>{calls++;return wait.promise},{onPage:()=>indexCalls++});loader.seed(makePage(0))
  try{
   const a=loader.ensure(2),b=loader.ensure(2);await flush();assert.equal(calls,1)
   wait.resolve(makePage(1));assert.equal(await a,true);assert.equal(await b,true)
   assert.equal(loader.indexedSteps,4);assert.equal(indexCalls,2)
  }finally{loader.dispose()}
 })
 await test('503 recovery keeps same cursor and existing frame, then clears issue',async()=>{
  let calls=0;const cursors=[]
  const loader=create(async cursor=>{cursors.push(cursor);if(++calls===1)throw new RequestError(503,'storage_unavailable');return makePage(1)})
  loader.seed(makePage(0));const before=loader.commandAt(0)
  try{assert.equal(await loader.ensure(2),true);assert.equal(calls,2);assert.deepEqual(cursors,['p1','p1']);assert.equal(loader.issue,null);assert.equal(loader.commandAt(0),before);assert.equal(loader.indexedSteps,4)}finally{loader.dispose()}
 })
 await test('exhausted transient recovery retains frames and manual retry resumes',async()=>{
  let failing=true,calls=0;const loader=create(async()=>{calls++;if(failing)throw new RequestError(503,'storage_unavailable');return makePage(1)})
  loader.seed(makePage(0))
  try{assert.equal(await loader.ensure(2),false);assert.equal(calls,3);assert(loader.commandAt(0));assert.equal(loader.issue.terminal,false)
   failing=false;assert.equal(await loader.ensure(2,true),true);assert.equal(loader.issue,null);assert.equal(calls,4)
  }finally{loader.dispose()}
 })
 await test('429 never auto-retries and respects Retry-After on explicit retry',async()=>{
  let now=0,calls=0;const loader=create(async()=>{calls++;throw new RequestError(429,'rate_limited',5000)},{now:()=>now})
  loader.seed(makePage(0))
  try{await loader.ensure(2);assert.equal(calls,1);await loader.ensure(2,true);assert.equal(calls,1);assert(loader.issue)
   now=5001;await loader.ensure(2,true);assert.equal(calls,2)
  }finally{loader.dispose()}
 })
 await test('denied response clears private cache and aborts pending reads',async()=>{
  let signal;const loader=create(async(_cursor,s)=>{signal=s;throw new RequestError(403,'forbidden')});loader.seed(makePage(0))
  try{await loader.ensure(2);assert.equal(loader.cachedPages,0);assert.equal(loader.commandAt(0),undefined);assert(signal.aborted);assert.equal(loader.issue.denied,true);assert.equal(await loader.ensure(0,true),false)}finally{loader.dispose()}
 })
 for(const corrupt of ['total','duplicate-sequence','match','stalled-cursor'])await test(`page integrity/${corrupt}`,async()=>{
  const bad=structuredClone(makePage(1));if(corrupt==='total')bad.totalCommands++
  if(corrupt==='duplicate-sequence')bad.items[0].sequence=2
  if(corrupt==='match')bad.items[0].state.MatchId='another-match'
  if(corrupt==='stalled-cursor')bad.nextCursor='p1'
  let calls=0;const loader=create(async()=>{calls++;return bad});loader.seed(makePage(0))
  try{assert.equal(await loader.ensure(2),false);assert.equal(calls,1);assert.equal(loader.issue.terminal,true);assert.equal(loader.indexedSteps,2)}finally{loader.dispose()}
 })
 await test('bounded eviction, historical reload and index callback exactly once per page',async()=>{
  let indexCalls=0,reads=0;const loader=create(async cursor=>{reads++;return makePage(pageNumber(cursor))},{onPage:()=>indexCalls++});loader.seed(makePage(0))
  try{for(let step=2;step<total;step+=2){assert.equal(await loader.ensure(step),true);loader.focus(step);assert(loader.cachedPages<=4);assert(loader.cachedBytes<=20*1024*1024)}
   assert.equal(loader.commandAt(0),undefined);const count=indexCalls
   assert.equal(await loader.ensure(0),true);loader.focus(0);assert.equal(loader.commandAt(0).sequence,1);assert.equal(indexCalls,count);assert(reads>=8);assert(loader.cachedPages<=4)
  }finally{loader.dispose()}
 })
 for(const viewer of [null,0,1])for(const privileged of [false,true])await test(`full/incremental index parity/${viewer}/${privileged}`,()=>{
  const commands=all.map((item,i)=>({...item,state:{...item.state,Events:[{Sequence:i+1,Type:'play',PrivateTo:i%3===0?1:null,Cards:[{Name:'card-'+i,CardId:'C'+i,Hidden:i%2===0}]}]}}))
  const input={...detail(commands),viewerPlayerIndex:viewer},expected=index.buildReplayIndex(input,privileged),builder=index.createReplayIndexAccumulator(viewer,privileged)
  for(let start=0;start<commands.length;start+=3)builder.append(commands.slice(start,start+3),start)
  assert.deepEqual(builder.index,expected)
  if(viewer!==1)assert(!JSON.stringify(builder.index).includes('card-3'))
  const before=JSON.stringify(builder.index);assert.throws(()=>builder.append(commands.slice(0,2),0));assert.throws(()=>builder.append(commands.slice(0,2),100));assert.equal(JSON.stringify(builder.index),before)
 })
 await test('disaster context survives bounded cache eviction without rewriting raw frames',async()=>{
  const commands=structuredClone(all);const disaster={InstanceId:'disaster',CardId:'D',Name:'public-disaster',CardType:'disaster',Faction:'neutral'}
  commands[0].state.Events=[{Sequence:1,Type:'disaster-reveal',Cards:[disaster]}]
  const before=JSON.stringify(commands),loader=create(async cursor=>makePage(pageNumber(cursor),commands));loader.seed(makePage(0,commands))
  try{for(let step=2;step<total;step+=2){await loader.ensure(step);loader.focus(step)}
   const window=loader.window(14),game=model.replayGameAt(detail(window.commands),window.step,undefined,window.disasterBefore)
   assert.deepEqual(game.activeDisaster,model.replayGameAt(detail(commands),14).activeDisaster);assert.equal(JSON.stringify(commands),before)
  }finally{loader.dispose()}
 })
 // Run the actual script-setup with Vue refs/watchers; only route/API/device/timers are controlled.
 function fixture(read,options={}){
  const mounted=[],unmounted=[],timers=new Map(),listeners=new Map();let timerId=0
  const route=vue.reactive({name:'admin-match-replay',params:{matchId:'synthetic-only'},query:{}})
  const platformState=vue.reactive({token:'synthetic-token',account:{id:'synthetic-admin',role:'admin',permissionVersion:1,disabled:false}})
  const scope=vue.effectScope(),calls=[]
  const summary={summary:{matchId:'synthetic-only',status:'completed',players:[{displayName:'A'},{displayName:'B'}],startedUtc:'2026-10-16T00:00:00Z',commandCount:total}}
  const environment={...model,...index,ReplayArchiveLoader,computed:vue.computed,ref:vue.ref,shallowRef:vue.shallowRef,watch:vue.watch,nextTick:vue.nextTick,
   onMounted:fn=>mounted.push(fn),onBeforeUnmount:fn=>unmounted.push(fn),useRoute:()=>route,useRouter:()=>({push(){}}),
   platformState,PlatformRequestError:RequestError,loadDeckCatalog:options.catalog||(async()=>[]),isMobileDeviceExperience:()=>false,landscapeTeleportTarget:()=>'body',
   adminApi:{match:async()=>summary,replayPage:async(match,cursor,limit,signal)=>{calls.push({match,cursor,signal});return read(cursor,signal)}},
   platformRequest:async()=>{throw Error('unexpected player read')},rememberSettingsOpener:()=>null,closeSettingsAndRestore:()=>{},openBugFeedbackFromSettings:()=>{},
   window:{setTimeout:(fn,ms)=>{const id=++timerId;timers.set(id,{fn,ms});return id},clearTimeout:id=>timers.delete(id),addEventListener:(name,fn)=>listeners.set(name,fn),removeEventListener:name=>listeners.delete(name)},
   document:{getElementById:()=>null,querySelector:()=>null},Error,TypeError,AbortController,DOMException,TextEncoder,Math,Promise,console,setTimeout,clearTimeout}
  const stripped=ast.statements.filter(n=>!ts.isImportDeclaration(n)).map(n=>n.getText(ast)).join('\n')
  const names=ast.statements.flatMap(n=>ts.isFunctionDeclaration(n)&&n.name?[n.name.text]:ts.isVariableStatement(n)?n.declarationList.declarations.flatMap(d=>ts.isIdentifier(d.name)?[d.name.text]:[]):[])
  const code=ts.transpileModule(stripped+'\nglobalThis.__state={'+names.join(',')+',getPlaybackGeneration:()=>playbackGeneration}',{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText
  vm.createContext(environment);scope.run(()=>vm.runInContext(code,environment))
  return {state:environment.__state,route,platformState,calls,timers,listeners,dispose(){unmounted.forEach(fn=>fn());scope.stop()}}
 }
 await test('actual SFC preserves board during prefetch failure and recovers without page error',async()=>{
  let failures=0;const f=fixture(async cursor=>{if(cursor==='p1'&&failures++===0)throw new RequestError(503,'storage_unavailable');return makePage(pageNumber(cursor))})
  try{await f.state.loadReplay();assert(f.state.currentGame.value);assert.equal(f.state.error.value,'');await new Promise(resolve=>setTimeout(resolve,600));await flush()
   assert.equal(f.state.replayIssue.value,null);assert.equal(f.state.selectedStep.value,0);assert(f.state.currentGame.value);assert.equal(f.state.error.value,'')
  }finally{f.dispose()}
 })
 await test('actual SFC manual pause during buffering cannot advance or restart from late success',async()=>{
  const wait=deferred();const f=fixture(cursor=>cursor==='p1'?wait.promise:Promise.resolve(makePage(pageNumber(cursor))))
  try{await f.state.loadReplay();f.state.selectStep(1);f.state.playing.value=true
   const pending=f.state.advancePlayback(f.state.getPlaybackGeneration());await flush();assert.equal(f.state.replayBuffering.value,true)
   f.state.stop();wait.resolve(makePage(1));await pending;assert.equal(f.state.selectedStep.value,1);assert.equal(f.state.playing.value,false);assert.equal(f.timers.size,0)
  }finally{f.dispose()}
 })
 for(const change of ['account-ABA','route-ABA','unmount'])await test(`actual SFC abort and late rejection/${change}`,async()=>{
  const wait=deferred();let pendingSignal;const f=fixture((cursor,signal)=>{if(cursor==='p1'){pendingSignal=signal;return wait.promise}return Promise.resolve(makePage(0))})
  let disposed=false
  try{await f.state.loadReplay();await flush();assert(pendingSignal)
   if(change==='account-ABA'){f.platformState.account.id='another';f.platformState.account.id='synthetic-admin'}
   if(change==='route-ABA'){f.route.params.matchId='another';f.route.params.matchId='synthetic-only'}
   if(change==='unmount'){f.dispose();disposed=true}
   assert(pendingSignal.aborted);wait.resolve(makePage(1));await flush();assert.equal(f.state.detail.value,null);assert.equal(f.state.currentGame.value,null);assert.equal(f.state.playing.value,false)
  }finally{if(!disposed)f.dispose()}
 })
 await test('actual SFC unmount during catalog read sends no match request',async()=>{
  const wait=deferred(),f=fixture(async()=>makePage(0),{catalog:()=>wait.promise});const pending=f.state.loadReplay();f.dispose();wait.resolve([]);await pending;assert.equal(f.calls.length,0);assert.equal(f.state.detail.value,null)
 })
 await test('actual SFC newer manual navigation wins over an earlier buffered next',async()=>{
  const wait=deferred(),f=fixture(cursor=>cursor==='p1'?wait.promise:Promise.resolve(makePage(0)))
  try{await f.state.loadReplay();f.state.selectStep(1);const pending=f.state.next();await flush();await f.state.previous();assert.equal(f.state.selectedStep.value,0)
   wait.resolve(makePage(1));await pending;assert.equal(f.state.selectedStep.value,0);assert.equal(f.state.playing.value,false)
  }finally{f.dispose()}
 })
 await test('actual SFC keeps waiting for presentation before advancing at any speed',async()=>{
  const f=fixture(async cursor=>makePage(pageNumber(cursor)))
  try{await f.state.loadReplay();await flush();f.state.playing.value=true;f.state.replayPresentationBusy.value=true
   await f.state.advancePlayback(f.state.getPlaybackGeneration());assert.equal(f.state.selectedStep.value,0);assert([...f.timers.values()].some(x=>x.ms===40))
   f.state.replayPresentationBusy.value=false;await f.state.advancePlayback(f.state.getPlaybackGeneration());assert.equal(f.state.selectedStep.value,1)
  }finally{f.dispose()}
 })
 await test('terminal incompatible page cannot be bypassed by manual loadMore retry',async()=>{
  let calls=0;const loader=create(async()=>{calls++;throw new RequestError(409,'replay_incompatible')});loader.seed(makePage(0))
  try{assert.equal(await loader.ensure(2),false);assert.equal(await loader.loadMore(true),false);assert.equal(await loader.ensure(2,true),false);assert.equal(calls,1)}finally{loader.dispose()}
 })
 await test('actual SFC initial page request aborts and cannot restore private data after unmount',async()=>{
  const wait=deferred();let signal;const f=fixture((_cursor,s)=>{signal=s;return wait.promise})
  const pending=f.state.loadReplay();await flush();assert(signal);f.dispose();assert(signal.aborted);wait.resolve(makePage(0));await pending;assert.equal(f.state.detail.value,null)
 })
 for(const change of ['account','route'])await test(`actual SFC initial read invalidation ends loading/${change}`,async()=>{
  const wait=deferred(),f=fixture(()=>wait.promise)
  try{const pending=f.state.loadReplay();await flush();assert.equal(f.state.loading.value,true)
   if(change==='account')f.platformState.account.id='another';else f.route.params.matchId='another'
   assert.equal(f.state.loading.value,false);assert(f.state.error.value);assert.equal(f.state.detail.value,null)
   wait.resolve(makePage(0));await pending;assert.equal(f.state.detail.value,null);assert.equal(f.state.loading.value,false)
  }finally{f.dispose()}
 })
 for(const result of ['late-success','abort'])await test(`absolute read deadline remains retryable/${result}`,async()=>{
  let now=0,late=true
  const loader=create(async()=>{if(late){now=30001;if(result==='abort')throw new DOMException('deadline','AbortError')}return makePage(1)},{now:()=>now})
  loader.seed(makePage(0))
  try{assert.equal(await loader.ensure(2),false);assert.equal(loader.indexedSteps,2);assert(loader.commandAt(0))
   assert(loader.issue);assert.equal(loader.issue.terminal,false)
   late=false;assert.equal(await loader.ensure(2,true),true);assert.equal(loader.issue,null)
  }finally{loader.dispose()}
 })
 await test('actual SFC speed change while advancing buffer keeps the requested step',async()=>{
  const wait=deferred(),f=fixture(cursor=>cursor==='p1'?wait.promise:Promise.resolve(makePage(0)))
  try{await f.state.loadReplay();f.state.selectStep(1);f.state.playing.value=true
   const pending=f.state.advancePlayback(f.state.getPlaybackGeneration());await flush();assert.equal(f.state.replayBuffering.value,true)
   f.state.setPlaybackSpeed(3);wait.resolve(makePage(1));await pending;await flush()
   assert.equal(f.state.selectedStep.value,2);assert.equal(f.state.playing.value,true)
   assert([...f.timers.values()].some(item=>item.ms===1000))
  }finally{f.dispose()}
 })
 await test('actual SFC speed change while restarting an evicted first page resumes from zero',async()=>{
  const wait=deferred();let holdFirst=false
  const f=fixture(cursor=>holdFirst&&cursor===undefined?wait.promise:Promise.resolve(makePage(pageNumber(cursor))))
  try{await f.state.loadReplay();await flush()
   for(let step=2;step<total;step+=2){await f.state.archive.value.ensure(step);f.state.selectStep(step);await flush()}
   f.state.selectStep(total-1);assert.equal(f.state.archive.value.commandAt(0),undefined)
   holdFirst=true;f.state.toggle();await flush();assert.equal(f.state.replayBuffering.value,true)
   f.state.setPlaybackSpeed(3);wait.resolve(makePage(0));await flush();await flush()
   assert.equal(f.state.selectedStep.value,0);assert.equal(f.state.playing.value,true)
   assert([...f.timers.values()].some(item=>item.ms===1000))
  }finally{f.dispose()}
 })
 await test('changed replay modules pass strict TypeScript semantics without emitting files',()=>{
  const options={target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.ESNext,moduleResolution:ts.ModuleResolutionKind.Bundler,strict:true,noEmit:true,skipLibCheck:true,
   paths:{'@/*':[path.resolve(root,'../*')]},types:[]}
  const host=ts.createCompilerHost(options),shim=path.join(root,'__replay_type_environment__.d.ts')
  const originalGetSourceFile=host.getSourceFile.bind(host)
  host.getSourceFile=(name,languageVersion,...rest)=>path.resolve(name).toLowerCase()===path.resolve(shim).toLowerCase()?ts.createSourceFile(name,"declare module '*.vue' { const component: import('vue').DefineComponent; export default component }\ninterface ImportMeta { readonly env: Record<string, any> }",languageVersion,true):originalGetSourceFile(name,languageVersion,...rest)
  host.resolveModuleNames=(names,containing)=>names.map(name=>ts.resolveModuleName(name,containing,options,host).resolvedModule
   ?? ts.resolveModuleName(name,process.env.L12_REPLAY_TOOLING_PACKAGE || path.resolve(here,'../package.json'),options,host).resolvedModule)
  host.writeFile=()=>{throw new Error('Focused type validation must not emit files')}
  const program=ts.createProgram([shim,...['replayArchiveLoader.ts','replayIndex.ts','replayModel.ts'].map(name=>path.join(root,name))],options,host)
  const diagnostics=ts.getPreEmitDiagnostics(program)
  assert.equal(diagnostics.length,0,ts.formatDiagnosticsWithColorAndContext(diagnostics,{getCurrentDirectory:()=>root,getCanonicalFileName:x=>x,getNewLine:()=> '\n'}))
 })
 const {parse,compileTemplate}=require('@vue/compiler-sfc')
 await test('actual SFC template compiles and retains 1/2/3 speeds only',()=>{
  const parsed=parse(pageSource,{filename:'ReplayPage.vue'});assert.deepEqual(parsed.errors,[])
  const compiled=compileTemplate({source:parsed.descriptor.template.content,filename:'ReplayPage.vue',id:'replay-recovery'});assert.deepEqual(compiled.errors,[])
  assert(pageSource.includes('[1, 2, 3] as const'));assert(!pageSource.includes('[0.5,'))
 })
}
console.log(JSON.stringify({suite:'replay recovery actual source',sourceRoot:root,total:results.length,passed:results.filter(x=>x.passed).length,failed:results.filter(x=>!x.passed).length,cases:results},null,2))
if(results.some(x=>!x.passed))process.exitCode=1
