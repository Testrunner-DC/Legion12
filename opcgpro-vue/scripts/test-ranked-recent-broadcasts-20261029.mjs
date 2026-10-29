import assert from 'node:assert/strict'
import fs from 'node:fs'
import {createHash} from 'node:crypto'
import {createRequire} from 'node:module'
import * as vue from 'vue'
import ts from 'typescript'
import {parse,compileScript} from '@vue/compiler-sfc'

const require=createRequire(import.meta.url)
const filename=new URL('../src/l12/site/BattleHubPage.vue',import.meta.url)
const source=fs.readFileSync(filename,'utf8')
const descriptor=parse(source,{filename:'BattleHubPage.vue'}).descriptor
const compiled=compileScript(descriptor,{id:'ranked-recent-runtime',inlineTemplate:true})
const code=ts.transpileModule(compiled.content,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText
const deferred=()=>{let resolve,reject;const promise=new Promise((yes,no)=>{resolve=yes;reject=no});return{promise,resolve,reject}}
const rows=(count=7)=>Array.from({length:count},(_,index)=>({id:'broadcast-'+index,matchId:'match-'+index,
  eventType:['win-streak','streak-ended','highest-tier','faction-first','master-champion-S02-01M1','unknown'][index%6],
  message:'权威广播 '+index,createdAt:new Date(Date.UTC(2026,9,29,15,48-index)).toISOString()}))
const settle=async()=>{for(let index=0;index<18;index++){await Promise.resolve();await vue.nextTick()}}
const node=(tag,text='')=>({tag,text,children:[],props:{},parent:null})
const teleportBody=node('body')
const renderer=vue.createRenderer({
  createElement:tag=>node(tag),createText:text=>node('#text',text),createComment:text=>node('#comment',text),
  setText:(target,text)=>{target.text=text},setElementText:(target,text)=>{target.text=text;target.children=[]},
  insert(child,parent,anchor){if(child.parent){const index=child.parent.children.indexOf(child);if(index>=0)child.parent.children.splice(index,1)}
    child.parent=parent;const index=anchor?parent.children.indexOf(anchor):-1;if(index<0)parent.children.push(child);else parent.children.splice(index,0,child)},
  remove(child){if(child.parent){const index=child.parent.children.indexOf(child);if(index>=0)child.parent.children.splice(index,1)}child.parent=null},
  patchProp:(target,key,_old,value)=>{target.props[key]=value},parentNode:target=>target.parent,
  nextSibling(target){const siblings=target.parent?.children??[];return siblings[siblings.indexOf(target)+1]},
  querySelector:selector=>selector==='body'?teleportBody:null,setScopeId(){},insertStaticContent(){throw Error('Unexpected static-content host operation')},
})
const walk=root=>[root,...root.children.flatMap(walk)]
const text=root=>root.tag==='#comment'?'':root.text+root.children.map(text).join('')
const hasClass=(target,value)=>String(target.props.class??'').split(/\s+/).includes(value)
let assertions=0
const equal=(actual,expected,message)=>{assert.deepEqual(actual,expected,message);assertions++}

function mount(options={}){
  const calls=[],hooks={claims:0,completions:0}, timers=new Set()
  const win=new EventTarget();win.setInterval=callback=>{timers.add(callback);return callback};win.clearInterval=timer=>timers.delete(timer)
  globalThis.window=win;globalThis.localStorage={getItem:()=>null,setItem(){}}
  const platformState=vue.reactive({account:null,token:''})
  const l12State=vue.reactive({status:'online',room:null,game:null,matchFound:null,matchmaking:null,notice:'',operationsPolicy:null})
  const playback=vue.reactive({claim:null})
  const policy={version:1,season:{id:'qa',name:'合成赛季'},announcements:[],maintenance:{entryBlocked:false},cardRestrictions:[],
    defaultRoomConfig:{spectating:'public',handVisibility:'request',disasterMode:'all'}}
  let responder=options.respond??(()=>Promise.resolve(rows()))
  const rankedApi={broadcasts(limit){calls.push(limit);return responder(calls.length)},
    overview:async()=>({profile:{faction:'混沌',placed:true,tier:'破败先锋Ⅲ',displayValue:'七曜值 31,427'},config:{factions:[]},factionTotals:{}}),
    claimBroadcast(){hooks.claims++;throw Error('History must not claim')},completeBroadcast(){hooks.completions++;throw Error('History must not complete')}}
  const stub=vue.defineComponent({render:()=>vue.h('span')})
  const deckScopes=['ranked','casual','friendly','sandbox-player','sandbox-opponent'],emptyDecks={}
  const requireDeckScope=scope=>assert.ok(deckScopes.includes(scope),'Unexpected deck selection scope '+scope)
  const deps={
    vue:{...vue,vModelText:{},vModelSelect:{}},
    'vue-router':{useRouter:()=>({push(){}})},
    '@/l12/net':{l12State,connect:async()=>{},selectCustomDeck(){},cancelMatchmaking(){},createRoom(){},joinMatchmaking(){},joinRoom(){},leaveRoom(){},setReady(){},spectateRoom(){},updateRoomOptions(){}},
    '@/l12/platform':{platformState,rankedApi,getEffectiveOperationsPolicy:async()=>policy,normalizeRankedConfig:value=>value},
    '@/l12/decks':{deckCountSummary:()=>({label:43}),ensureOfficialPrebuiltDecks:async()=>emptyDecks,L12_DECK_SELECTION_SCOPES:deckScopes,
      loadDeckCatalog:async()=>[],loadPrivateDeckBody:async()=>null,loadSavedDecksState:()=>({status:'ready',decks:emptyDecks}),deckErrorBelongsToCurrentAccount:()=>false,
      initializeDeckCache:async()=>{},loadSavedDecks:()=>emptyDecks,
      hasSavedDeckSelection(scope){requireDeckScope(scope);return false},
      loadSelectedDeckName(scope,decks){requireDeckScope(scope);return Object.keys(decks)[0]??''},
      async saveSelectedDeckName(scope,name){requireDeckScope(scope);assert.ok(Object.hasOwn(emptyDecks,name),'No saved deck in this broadcast fixture')},
      SELECTED_DECK_KEY:'qa',validateDeck:()=>''},
    './rankedBroadcastPlayback':{rankedBroadcastPlayback:playback},
    './maintenanceCountdown':{maintenanceCountdown:()=>null},
    '@/l12/friendlyRoomPreferences':{isFriendlyDisasterMode:()=>true,loadFriendlyDisasterMode:()=>undefined,saveFriendlyDisasterMode(){}},
  }
  const deckImports=Object.values(compiled.imports).filter(value=>value.source==='@/l12/decks'&&!value.isType)
  assert.ok(deckImports.length>0,'Actual SFC deck imports must be inspected')
  for(const dependency of deckImports)assert.ok(Object.hasOwn(deps['@/l12/decks'],dependency.imported),'Missing actual deck port '+dependency.imported)
  const module={exports:{}}
  new Function('require','module','exports',code)(id=>{
    if(id in deps)return deps[id]
    if(id.endsWith('.vue'))return{__esModule:true,default:stub}
    if(id.startsWith('@/assets/'))return{__esModule:true,default:'fixture-art.png'}
    throw Error('Unexpected product dependency '+id)
  },module,module.exports)
  const props=vue.reactive({initialTab:options.tab??'match',matchmakingOnly:options.matchmakingOnly??false})
  const root=node('root'),app=renderer.createApp({render:()=>vue.h(module.exports.default,props)})
  app.component('router-link',stub);app.mount(root)
  const history=()=>walk(root).find(target=>target.props['data-ui-contract']==='ranked-recent-five-broadcasts')
  const items=()=>history()?walk(history()).filter(target=>target.tag==='li'):[]
  const click=async label=>{const button=walk(root).find(target=>target.tag==='button'&&text(target)===label);assert.ok(button,'Missing button '+label);button.props.onClick();await settle()}
  return{root,app,props,calls,hooks,timers,playback,l12State,history,items,click,setRespond:fn=>{responder=fn}}
}

const results=[]
async function test(name,work){await work();results.push(name)}
await test('real SFC reads limit five, renders server order and all public categories',async()=>{
  const f=mount();await settle();equal(f.calls,[5]);equal(f.items().length,5)
  equal(f.items().map(item=>text(item).match(/权威广播 \d/)?.[0]),rows(5).map(row=>row.message))
  equal(f.items().map(item=>text(item).includes('10/29')||text(item).includes('10-29')),Array(5).fill(true),'Shanghai dates remain visible')
  equal(f.items().map(item=>text(item).replace(/^.*?\s/,'')).length,5)
  equal([f.hooks.claims,f.hooks.completions],[0,0]);f.app.unmount();equal(f.timers.size,0)
})
for(const count of [2,0])await test('only '+count+' real rows, without placeholders',async()=>{
  const f=mount({respond:async()=>rows(count)});await settle();equal(f.items().length,count)
  if(!count)equal(text(f.history()).includes('暂无排位广播'),true)
  f.app.unmount()
})
await test('failed reads show a neutral failure and user retry obtains real rows',async()=>{
  const f=mount({respond:async()=>{throw Error('private diagnostic must not render')}});await settle()
  equal(text(f.history()).includes('广播暂时无法加载'),true);equal(text(f.history()).includes('private diagnostic'),false)
  f.setRespond(async()=>rows(2));await f.click('重试');equal(f.calls,[5,5]);equal(f.items().length,2);f.app.unmount()
})
await test('message markup remains literal, unknown category and malformed dates stay neutral',async()=>{
  const message='<img src=x onerror=alert(1)> '+ '长'.repeat(160)
  const f=mount({respond:async()=>[{...rows(1)[0],message,eventType:'new-category',createdAt:'bad-date'}]});await settle()
  equal(text(f.items()[0]).includes(message),true);equal(walk(f.history()).filter(target=>target.tag==='img').length,0)
  equal(text(f.items()[0]).includes('时间未知'),true);equal(text(f.items()[0]).includes('广播'),true);f.app.unmount()
})
await test('ranked only; reentry refreshes, hidden claims do not request history',async()=>{
  const f=mount();await settle();await f.click('休闲匹配');equal(f.history(),undefined)
  f.playback.claim={broadcast:{id:'hidden'}};await settle();equal(f.calls,[5])
  await f.click('排位匹配');equal(f.calls,[5,5]);equal(f.items().length,5)
  f.props.initialTab='friendly';await settle();equal(f.history(),undefined);f.app.unmount()
})
for(const options of [{tab:'friendly'},{tab:'tutorial'},{matchmakingOnly:true}])await test('excluded initial view '+JSON.stringify(options),async()=>{
  const f=mount(options);await settle();equal(f.calls,[]);equal(f.history(),undefined);f.app.unmount()
})
await test('new claim coalesces concurrent refreshes; same ID and completion never read again',async()=>{
  const pending=deferred(),f=mount({respond:call=>call===1?pending.promise:Promise.resolve(rows(2))});await settle()
  f.playback.claim={broadcast:{id:'new-1'}};f.playback.claim={broadcast:{id:'new-2'}};await settle();equal(f.calls,[5])
  pending.resolve(rows());await settle();equal(f.calls,[5,5]);equal(f.items().length,2)
  f.playback.claim={broadcast:{id:'new-2'}};await settle();f.playback.claim=null;await settle();equal(f.calls,[5,5])
  equal([f.hooks.claims,f.hooks.completions],[0,0]);f.app.unmount()
})
await test('mode leave rejects stale responses; return uses one fresh request after the pending flight',async()=>{
  const pending=deferred(),fresh=deferred(),f=mount({respond:call=>call===1?pending.promise:fresh.promise});await settle()
  await f.click('休闲匹配');await f.click('排位匹配');equal(f.calls,[5])
  equal(text(f.history()).includes('正在加载广播'),true,'reentry waiting for the previous flight must not pretend the list is empty')
  pending.resolve(rows());await settle()
  equal(f.calls,[5,5]);equal(f.items().length,0,'old generation cannot fill reentered view')
  fresh.resolve(rows(2));await settle();equal(f.items().length,2);f.app.unmount()
})
await test('unmount rejects late data and new claims do not resurrect the component',async()=>{
  const pending=deferred(),f=mount({respond:()=>pending.promise});await settle();f.app.unmount()
  pending.resolve(rows());f.playback.claim={broadcast:{id:'after-unmount'}};await settle()
  equal(f.root.children.length,0);equal(f.calls,[5]);equal(f.timers.size,0)
})
await test('unmount before the queued first read starts prevents network initiation',async()=>{
  const f=mount();f.app.unmount();await settle();equal(f.calls,[])
})
await test('no visibility-triggered reads or additional polling timers',async()=>{
  const f=mount();await settle();equal(f.timers.size,1,'only the preexisting maintenance clock remains')
  window.dispatchEvent(new Event('visibilitychange'));await settle();equal(f.calls,[5]);f.app.unmount()
})
console.log(JSON.stringify({suite:'actual BattleHub SFC recent broadcast lifecycle',sourceSha256:createHash('sha256').update(source).digest('hex'),assertions,scenarios:results.length,results},null,2))
