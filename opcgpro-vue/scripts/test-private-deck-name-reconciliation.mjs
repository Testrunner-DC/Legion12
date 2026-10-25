import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const vueRoot=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..')
const dependency=createRequire(path.join(vueRoot,'package.json'))
const ts=dependency('typescript'), { reactive, watch }=dependency('vue')
const read=p=>fs.readFileSync(path.join(vueRoot,p),'utf8').replaceAll('\r\n','\n')
const url=source=>'data:text/javascript;base64,'+Buffer.from(ts.transpileModule(source,{
  compilerOptions:{module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022}
}).outputText).toString('base64')
class MemoryStorage {
  values=new Map(); writes=0
  getItem(k){return this.values.get(k)??null}
  setItem(k,v){this.values.set(k,String(v));this.writes++}
  snapshot(){return [...this.values.entries()]}
}
const storage=new MemoryStorage(), descriptors=new Map()
for(const key of ['localStorage','navigator','__deckNameReconciliationTest']) descriptors.set(key,Object.getOwnPropertyDescriptor(globalThis,key))
Object.defineProperty(globalThis,'localStorage',{configurable:true,value:storage})
let lockDepth=0
Object.defineProperty(globalThis,'navigator',{configurable:true,value:{locks:{request:async(_key,run)=>{
  lockDepth++;try{return await run()}finally{lockDepth--}
}}}})
const state=reactive({account:null,token:''}), calls=[]
let handler=()=>{throw new Error('Unexpected platform read')}
Object.defineProperty(globalThis,'__deckNameReconciliationTest',{configurable:true,value:{watch,platformState:state,
  getDeckCoverRequestHeaders:()=>({'X-L12-Deck-Cover':'1'}),
  getEffectiveOperationsPolicy:async()=>({}),platformRequest:(p,init)=>{
    assert.equal(init?.method,undefined,'Name reconciliation is read-only')
    assert.equal(lockDepth,0,'HTTP may not run while holding the cache Web Lock')
    calls.push({path:p,init});return handler(p,init)
  }}})
const binary=url(read('src/l12/deckCodeCodec.ts'))
const codec=url(read('src/l12/deckCacheCodec.ts').replace("from './deckCodeCodec'",`from '${binary}'`))
const cache=url(read('src/l12/deckCacheStorage.ts').replace("from './deckCodeCodec'",`from '${binary}'`).replace("from './deckCacheCodec'",`from '${codec}'`))
const opening=url(read('src/l12/openingHandEligibility.ts'))
const cover=url(read('src/l12/deckCover.ts'))
const executable=read('src/l12/decks.ts')
  .replace("from './browserStorage'",`from '${url(read('src/l12/browserStorage.ts'))}'`)
  .replace("from './deckCover'",`from '${cover}'`)
  .replace("import { watch } from 'vue'",'const {watch}=globalThis.__deckNameReconciliationTest')
  .replace("from './deckCacheStorage'",`from '${cache}'`)
  .replace("from './deckCacheCodec'",`from '${codec}'`)
  .replace("import { normalizeLookupCardType } from './cardPresentation'",'const normalizeLookupCardType=value=>value')
  .replace("import { getDeckCoverRequestHeaders, getEffectiveOperationsPolicy, platformRequest, platformState, type OperationsCardRestriction } from './platform'",'const {getDeckCoverRequestHeaders,getEffectiveOperationsPolicy,platformRequest,platformState}=globalThis.__deckNameReconciliationTest')
  .replace("import { deploymentPath } from './deploymentBase'",'const deploymentPath=value=>value')
  .replace(/import (\w+) from '(\.\.\/\.\.\/\.\.\/服务端WebSocket\/TwelveLegions\/Data\/[^']+\.json)'/g,(_all,name,relative)=>
    `const ${name}=${fs.readFileSync(path.resolve(vueRoot,'src/l12',relative),'utf8').replace(/^\uFEFF/,'')}`)
  .replace("from './openingHandEligibility'",`from '${opening}'`)
const clone=v=>JSON.parse(JSON.stringify(v))
const body=(id,name='同名牌库',revision=1)=>({id,revision,name,masterId:'S01-01M1',cardIds:['S01-0101'],moraleIds:['S01-01G1'],specialIds:[],updatedAt:'2026-10-14T00:00:00Z'})
const key=owner=>`l12-custom-decks-v1:${owner}`
const route=deck=>`/api/decks/by-id/${encodeURIComponent(deck.id)}?expectedRevision=${deck.revision}`
const error=(status,extra={})=>Object.assign(new Error(`Synthetic HTTP ${status}`),{status,...extra})
const conflict=revision=>error(409,{code:'deck_revision_conflict',currentRevision:revision})
let passed=0, api,cacheApi
function account(owner){state.account={id:owner};state.token=`synthetic-${owner}`}
function seed(decks,owner='B',format='legacy'){
  const mapped=Object.fromEntries(decks.map(d=>[d.name,clone(d)]))
  if(format==='legacy')storage.setItem(key(owner),JSON.stringify(mapped))
  else cacheApi.commitDeckCache(storage,key(owner),cacheApi.requireDeckCache(cacheApi.readDeckCache(storage,key(owner),`account:${owner}`)),mapped,'synthetic-seed')
}
function reset(decks=[],format='legacy'){
  storage.values.clear();storage.writes=0;calls.length=0;account('A');seed([body('A-deck')],'A');account('B');seed(decks,'B',format)
  storage.setItem('l12:synthetic-draft:B','unsaved draft preserved')
  storage.setItem('l12-selected-custom-deck:B:ranked','old')
}
function serve(entries){handler=p=>{
  assert.ok(Object.hasOwn(entries,p),`Unexpected read ${p}`)
  const result=entries[p];return result instanceof Error?Promise.reject(result):Promise.resolve(clone(result))
}}
async function check(label,run){await run();passed++;console.log(`PASS deck-name ${passed}: ${label}`)}
function checkBudgets(){
  assert.ok(calls.length<=3)
  for(const call of calls.slice(1)){
    assert.equal(call.init.reliability.maxAttempts,1)
    assert.ok(call.init.reliability.timeoutMs>0&&call.init.reliability.timeoutMs<=5000)
  }
}
try {
  api=await import(url(executable));cacheApi=await import(cache)
  await check('different account same name remains isolated and needs one request',async()=>{
    reset();const a=storage.getItem(key('A'));const selected=body('B-current');serve({[route(selected)]:selected})
    assert.equal((await api.loadPrivateDeckBody(selected)).id,selected.id);assert.equal(calls.length,1);assert.equal(storage.getItem(key('A')),a)
  })
  for(const format of ['legacy','envelope']){
    await check(`${format}: remotely deleted/recreated name replaces only proven stale ID`,async()=>{
      const old=body('B-old'),other=body('B-unrelated','不相关'),selected=body('B-new')
      reset([old,other],format);const a=storage.getItem(key('A')),draft=storage.getItem('l12:synthetic-draft:B')
      serve({[route(selected)]:selected,[route(old)]:error(404)})
      assert.equal((await api.loadPrivateDeckBody(selected)).id,'B-new')
      assert.deepEqual(api.loadSavedDecks(),{'不相关':other,'同名牌库':selected})
      assert.equal(storage.getItem(key('A')),a);assert.equal(storage.getItem('l12:synthetic-draft:B'),draft)
      assert.equal(storage.getItem('l12-selected-custom-deck:B:ranked'),'old');checkBudgets()
    })
    await check(`${format}: rename conflict refreshes one revision and preserves renamed body`,async()=>{
      const old=body('B-old'),renamed=body('B-old','已改名',2),selected=body('B-new')
      reset([old],format);serve({[route(selected)]:selected,[route(old)]:conflict(2),[route(renamed)]:renamed})
      assert.equal((await api.loadPrivateDeckBody(selected)).id,'B-new')
      assert.deepEqual(api.loadSavedDecks(),{'已改名':renamed,'同名牌库':selected});checkBudgets();assert.equal(calls.length,3)
    })
    await check(`${format}: two remote names swapped preserve both current IDs`,async()=>{
      const old=body('B-old'),cached=body('B-selected','另名'),renamed=body('B-old','另名',2),selected=body('B-selected','同名牌库',2)
      reset([old,cached],format);serve({[route(selected)]:selected,[route(old)]:conflict(2),[route(renamed)]:renamed})
      await api.loadPrivateDeckBody(selected);assert.deepEqual(api.loadSavedDecks(),{'另名':renamed,'同名牌库':selected});checkBudgets()
    })
  }
  for(const [label,failure] of [['401',error(401)],['403',error(403)],['500',error(500)],['transport',new Error('transport failure')],
    ['unknown conflict',error(409,{code:'other',currentRevision:2})],['lower revision',conflict(1)],['invalid revision',conflict('2')]]){
    await check(`${label} never discards any cache or draft`,async()=>{
      const old=body('B-old'),selected=body('B-new');reset([old]);const before=storage.snapshot();serve({[route(selected)]:selected,[route(old)]:failure})
      await assert.rejects(api.loadPrivateDeckBody(selected),candidate=>candidate===failure);assert.deepEqual(storage.snapshot(),before);checkBudgets()
    })
  }
  await check('two consecutive revision conflicts stop at the bounded second read',async()=>{
    const old=body('B-old'),selected=body('B-new');reset([old]);const before=storage.snapshot()
    serve({[route(selected)]:selected,[route(old)]:conflict(2),[route({...old,revision:2})]:conflict(3)})
    await assert.rejects(api.loadPrivateDeckBody(selected),candidate=>candidate.currentRevision===3)
    assert.deepEqual(storage.snapshot(),before);assert.equal(calls.length,3);checkBudgets()
  })
  await check('still-live same-name ID is refused, not overwritten',async()=>{
    const old=body('B-old'),selected=body('B-new');reset([old]);const before=storage.snapshot();serve({[route(selected)]:selected,[route(old)]:old})
    await assert.rejects(api.loadPrivateDeckBody(selected),/另一副同名/);assert.deepEqual(storage.snapshot(),before)
  })
  for(const [label,confirmed] of [['wrong ID',body('wrong','已改名')],['wrong revision',body('B-old','已改名',8)],['invalid body',{id:'B-old',revision:1,name:'已改名'}]]){
    await check(`${label} proof fails closed`,async()=>{
      const old=body('B-old'),selected=body('B-new');reset([old]);const before=storage.snapshot();serve({[route(selected)]:selected,[route(old)]:confirmed})
      await assert.rejects(api.loadPrivateDeckBody(selected));assert.deepEqual(storage.snapshot(),before)
    })
  }
  await check('renamed proof colliding with unrelated cached name is not recursively reconciled',async()=>{
    const old=body('B-old'),other=body('B-other','已改名'),selected=body('B-new'),renamed=body('B-old','已改名',2)
    reset([old,other]);const before=storage.snapshot();serve({[route(selected)]:selected,[route(old)]:conflict(2),[route(renamed)]:renamed})
    await assert.rejects(api.loadPrivateDeckBody(selected),/其他缓存冲突/);assert.deepEqual(storage.snapshot(),before);checkBudgets()
  })
  for(const transition of ['account ABA','page retired','unrelated cache changed']){
    await check(`${transition} during proof cannot commit`,async()=>{
      const old=body('B-old'),selected=body('B-new');reset([old]);let resolve,allowed=true
      handler=p=>p===route(selected)?Promise.resolve(selected):new Promise(yes=>{resolve=yes})
      const pending=api.loadPrivateDeckBody(selected,()=>allowed)
      for(let i=0;i<8&&!resolve;i++)await Promise.resolve();assert.ok(resolve)
      if(transition==='account ABA'){account('A');account('B')}
      if(transition==='page retired')allowed=false
      if(transition==='unrelated cache changed')seed([old,body('B-other','新缓存')])
      const before=storage.snapshot();resolve(body('B-old','已改名'))
      await assert.rejects(pending);assert.deepEqual(storage.snapshot(),before)
    })
  }
  await check('same stable ID with newer cache revision remains protected',async()=>{
    const selected=body('B-current','同名牌库',2);reset([body('B-current','同名牌库',3)]);const before=storage.snapshot();serve({[route(selected)]:selected})
    await assert.rejects(api.loadPrivateDeckBody(selected),/更新修订/);assert.deepEqual(storage.snapshot(),before);assert.equal(calls.length,1)
  })
  await check('cache persistence failure leaves original contents and drafts intact',async()=>{
    const old=body('B-old'),selected=body('B-new');reset([old]);const before=storage.getItem(key('B'))
    serve({[route(selected)]:selected,[route(old)]:error(404)});const originalSet=storage.setItem
    storage.setItem=function(k,v){if(k===key('B'))throw new Error('QuotaExceededError');return originalSet.call(this,k,v)}
    try{await assert.rejects(api.loadPrivateDeckBody(selected),/写入失败/)}finally{storage.setItem=originalSet}
    assert.equal(storage.getItem(key('B')),before);assert.equal(storage.getItem('l12:synthetic-draft:B'),'unsaved draft preserved')
  })
  await check('duplicate old ID with a higher revision never gets downgraded or queried',async()=>{
    const old=body('B-old'),newer=body('B-old','更高修订缓存',5),selected=body('B-new'),renamed=body('B-old','远端名',2)
    reset([old,newer]);const before=storage.snapshot()
    serve({[route(selected)]:selected,[route(old)]:conflict(2),[route(renamed)]:renamed})
    await assert.rejects(api.loadPrivateDeckBody(selected),/重复缓存/)
    assert.deepEqual(storage.snapshot(),before);assert.equal(calls.length,1)
  })
  await check('same revision with different name is not a proven rename',async()=>{
    const old=body('B-old'),selected=body('B-new');reset([old]);const before=storage.snapshot()
    serve({[route(selected)]:selected,[route(old)]:body('B-old','不同名称')})
    await assert.rejects(api.loadPrivateDeckBody(selected),/新修订/);assert.deepEqual(storage.snapshot(),before)
  })
  await check('reconciliation gets independent abortable requests, never the ordinary shared read',async()=>{
    const old=body('B-old'),selected=body('B-new'),renamed=body('B-old','已改名',2)
    reset([old]);serve({[route(selected)]:selected,[route(old)]:conflict(2),[route(renamed)]:renamed})
    await api.loadPrivateDeckBody(selected)
    for(const call of calls.slice(1))assert.ok(call.init.signal instanceof AbortSignal)
    assert.notEqual(calls[1].init.signal,calls[2].init.signal);checkBudgets()
  })
  console.log(`Private deck name reconciliation: ${passed}/${passed}, failed=0, skipped=0`)
}finally{
  for(const [key,descriptor] of descriptors){if(descriptor)Object.defineProperty(globalThis,key,descriptor);else delete globalThis[key]}
}
