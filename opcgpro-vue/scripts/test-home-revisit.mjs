import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import { compileScript, parse as parseSfc } from '@vue/compiler-sfc'
import { homeAccountVerified, homeCanContinueGame } from '../src/l12/site/homeRevisit.ts'
import { tournamentHubSection } from '../src/l12/site/tournamentHubNavigation.ts'
import { articleBodyText } from '../src/l12/site/articleBlocks.ts'

let assertions = 0
const equal = (value, expected, message) => { assert.deepEqual(value, expected, message); assertions++ }
equal(homeAccountVerified('account-a', 'token-a', true), true)
for (const args of [[undefined, 'token', true], ['a', '', true], ['a', 'token', false]])
  equal(homeAccountVerified(...args), false, 'cached identity alone cannot expose personal shortcuts')
const connection = { accountId:'a', status:'online', recoveryPhase:'snapshot-acknowledged', leavingRoom:false,
  game:{ matchId:'fixture-match', phase:'Main' } }
equal(homeCanContinueGame('a', true, connection), true)
for (const change of [{ accountId:'b' }, { status:'offline' }, { status:'connecting' },
  { recoveryPhase:'restoring' }, { leavingRoom:true }, { game:null },
  { game:{ matchId:'', phase:'Main' } }, { game:{ matchId:'fixture-match', phase:'GameOver' } }])
  equal(homeCanContinueGame('a', true, { ...connection, ...change }), false, 'only confirmed ongoing own connection offers continue')
equal(homeCanContinueGame('a', false, connection), false)
equal(homeCanContinueGame(undefined, true, connection), false)
for (const value of ['discover','mine','history','host','create']) equal(tournamentHubSection(value), value)
for (const value of [undefined, null, ['mine'], 'other', '/admin']) equal(tournamentHubSection(value), 'discover')

const cacheSource = fs.readFileSync(new URL('../src/l12/site/homePublishedCache.ts', import.meta.url),'utf8')
const module = await import(`data:text/javascript;base64,${Buffer.from(ts.transpileModule(cacheSource,{
  compilerOptions:{ module:ts.ModuleKind.ESNext, target:ts.ScriptTarget.ES2022 },
}).outputText).toString('base64')}`)
const payload = { composition:'{}', legal:'{}', news:[], videos:[], products:[], media:[] }
equal(module.loadPublishedHomeCache({getItem:() => { throw Error('storage blocked') }}), null)
equal(module.loadPublishedHomeCache({getItem:() => '{broken'}), null)
equal(module.loadPublishedHomeCache({getItem: key => key.endsWith('-v2') ? JSON.stringify({version:2,payload}) : null}), payload)
equal(module.loadPublishedHomeCache({getItem: key => key.endsWith('-v1') ? JSON.stringify(payload) : null}), payload)
equal(module.savePublishedHomeCache(payload,{setItem:() => { throw Error('quota') }}), false)
equal(module.savePublishedHomeCache(payload,{setItem:() => undefined}), true)
for(const collection of ['news','videos','products'])
  for(const badRow of [null,42,'broken',{}, {id:'missing-title'}])
    equal(module.loadPublishedHomeCache({getItem:()=>JSON.stringify({version:2,payload:{...payload,[collection]:[badRow]}})}),null,'malformed cached content rows are rejected without touching storage')
for(const badRow of [null,{},'broken'])
  equal(module.loadPublishedHomeCache({getItem:()=>JSON.stringify({...payload,media:[badRow]})}),null,'malformed cached media rows are rejected')

const homeSource = fs.readFileSync(new URL('../src/l12/site/OfficialHomePage.vue', import.meta.url), 'utf8')
const descriptor = parseSfc(homeSource, { filename:'OfficialHomePage.vue' }).descriptor
let compiledHome = compileScript(descriptor, { id:'home-initial-failure-test', genDefaultAs:'__sfc__' }).content
compiledHome = compiledHome.replace(/^import[^\n]*\n/gm, '')
compiledHome = `
const { computed, defineAsyncComponent, onBeforeUnmount, onMounted, ref } = globalThis.__homeHarness.vue
const { authState, platformState, siteContentApi } = globalThis.__homeHarness.platform
const navigator = globalThis.__homeHarness.navigator
const { articleBodyText, defaultHomeComposition, defaultSiteLegal, parseHomeComposition, parseSiteLegal,
  loadPublishedHomeCache, savePublishedHomeCache, homeAccountVerified, deploymentPath } = globalThis.__homeHarness.dependencies
const _defineComponent = value => value
${compiledHome}
export default __sfc__
`
compiledHome = ts.transpileModule(compiledHome, {
  compilerOptions:{ module:ts.ModuleKind.ESNext, target:ts.ScriptTarget.ES2022 },
}).outputText

const deferred = () => {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve=yes; reject=no })
  return { promise, resolve, reject }
}
const flush = async () => { for (let index=0;index<8;index++) await Promise.resolve() }
const publishedPayload = {
  composition:JSON.stringify({ version:1, heroSlides:[], notices:[] }),
  legal:'{}', news:[], videos:[], products:[], media:[],
}
async function mountHome({ cached=null, hidden=false, online=true } = {}) {
  const mounted=[], unmounted=[], requests=[], saved=[], timers=new Map(), listeners=[]
  let nextTimer=1
  const originalWindow=globalThis.window, originalDocument=globalThis.document
  globalThis.window={
    matchMedia:() => ({ matches:true }),
    setTimeout(callback, delay){ const id=nextTimer++; timers.set(id,{callback,delay}); return id },
    clearTimeout(id){ timers.delete(id) },
    addEventListener(type,callback){ listeners.push({target:'window',type,callback}) },
    removeEventListener(type,callback){ const index=listeners.findIndex(item=>item.target==='window'&&item.type===type&&item.callback===callback); if(index>=0)listeners.splice(index,1) },
  }
  globalThis.document={
    hidden,
    addEventListener(type,callback){ listeners.push({target:'document',type,callback}) },
    removeEventListener(type,callback){ const index=listeners.findIndex(item=>item.target==='document'&&item.type===type&&item.callback===callback); if(index>=0)listeners.splice(index,1) },
  }
  const harness={
    navigator:{onLine:online},
    vue:{
      ref:value=>({value}),
      computed:read=>({get value(){return read()}}),
      defineAsyncComponent:()=>({}),
      onMounted:callback=>mounted.push(callback),
      onBeforeUnmount:callback=>unmounted.push(callback),
    },
    platform:{ authState:{verified:false}, platformState:{account:null,token:''}, siteContentApi:{homeSummary(){ const item=deferred(); requests.push(item); return item.promise },home(){throw Error('Homepage must not request the legacy full payload')}} },
    dependencies:{
      articleBodyText, defaultHomeComposition:()=>({heroSlides:[],notices:[],newsTitle:'',videoEyebrow:'',videoTitle:'',productTitle:'',productEyebrow:''}),
      defaultSiteLegal:()=>({}), parseHomeComposition:value=>JSON.parse(value), parseSiteLegal:()=>({}),
      loadPublishedHomeCache:()=>cached, savePublishedHomeCache:value=>{saved.push(value);return true},
      homeAccountVerified:()=>false, deploymentPath:value=>value,
    },
  }
  globalThis.__homeHarness=harness
  const url=`data:text/javascript;base64,${Buffer.from(compiledHome).toString('base64')}#${Math.random()}`
  const component=(await import(url)).default
  const state=component.setup({}, { expose(){} })
  mounted.forEach(callback=>callback())
  return { state, requests, saved, timers, listeners, navigator:harness.navigator, document:globalThis.document, async unmount(){ unmounted.forEach(callback=>callback()); await flush(); globalThis.window=originalWindow; globalThis.document=originalDocument; delete globalThis.__homeHarness } }
}

{
  const home=await mountHome()
  equal(home.requests.length, 1, 'mount starts one public request')
  home.listeners.forEach(listener=>listener.callback())
  await flush()
  equal(home.requests.length, 1, 'online and visibility recovery events share the active request')
  home.requests[0].reject(Error('503'))
  await flush()
  equal(home.state.initialLoadFailed.value, true, 'first failure replaces the endless skeleton with an explanation')
  equal(home.state.ready.value, false, 'failure never fabricates published content')
  equal([...home.timers.values()].map(timer=>timer.delay), [2_000], 'initial failure keeps one bounded background retry')
  home.state.retryPublishedHome(); home.state.retryPublishedHome()
  await flush()
  equal(home.requests.length, 2, 'repeated retry clicks share one in-flight request')
  equal(home.timers.size, 0, 'manual retry replaces the pending background retry')
  home.requests[1].resolve(publishedPayload)
  await flush()
  equal(home.state.ready.value, true, 'a later success restores the published home')
  equal(home.state.initialLoadFailed.value, false, 'success clears the initial failure explanation')
  equal(home.saved.length, 1, 'only a successful published payload is cached')
  await home.unmount()
  equal(home.listeners.length, 0, 'unmount removes recovery listeners')
}

{
  const home=await mountHome({ cached:publishedPayload })
  equal(home.state.ready.value, true, 'a cached published snapshot renders immediately')
  home.requests[0].reject(Error('503'))
  await flush()
  equal(home.state.ready.value, true, 'a later failure cannot erase the cached published snapshot')
  equal(home.state.initialLoadFailed.value, false, 'cached content does not get covered by the initial failure explanation')
  await home.unmount()
}

{
  const home=await mountHome()
  const timeout=Error('request timed out'); timeout.name='TimeoutError'
  home.requests[0].reject(timeout)
  await flush()
  equal(home.state.initialLoadFailed.value, true, 'a timed-out first request also becomes actionable')
  const [retryTimer]=home.timers.values()
  retryTimer.callback()
  await flush()
  equal(home.requests.length, 2, 'the bounded background retry can recover without user input')
  home.requests[1].resolve(publishedPayload)
  await flush()
  equal(home.state.ready.value, true, 'background recovery publishes the later successful snapshot')
  await home.unmount()
}

{
  const home=await mountHome()
  await home.unmount()
  home.requests[0].resolve(publishedPayload)
  await flush()
  equal(home.state.ready.value, false, 'a late response after unmount cannot publish content')
  equal(home.saved.length, 0, 'a late response after unmount cannot update the cache')
}

{
  const home=await mountHome()
  try {
    for(let index=0; index<9; index++) {
      home.requests.at(-1).reject(Error('synthetic-persistent-503')); await flush()
      const timer=home.timers.entries().next().value
      if(!timer) break
      home.timers.delete(timer[0]); timer[1].callback(); await flush()
    }
    equal(home.requests.length,5,'persistent failure stops after initial request plus four automatic retries')
    equal(home.timers.size,0,'exhausted recovery never becomes an endless 20-second poll')
    home.state.retryPublishedHome(); await flush()
    equal(home.requests.length,6,'manual retry remains available after automatic budget exhaustion')
    home.requests.at(-1).reject(Error('manual-failure')); await flush()
    equal([...home.timers.values()].map(timer=>timer.delay),[2000],'explicit manual retry starts a new bounded recovery burst')
  } finally { await home.unmount() }
}

for(const paused of ['hidden','offline']) {
  const home=await mountHome()
  try {
    home.requests[0].reject(Error('synthetic-503'));await flush()
    const [timerId,timer]=home.timers.entries().next().value
    if(paused==='hidden')home.document.hidden=true
    else home.navigator.onLine=false
    home.timers.delete(timerId);timer.callback();await flush()
    equal(home.requests.length,1,paused+' timer firing never sends a background retry')
    equal(home.timers.size,0,paused+' page keeps no automatic retry timer')
    home.document.hidden=false;home.navigator.onLine=true
    home.listeners.find(item=>item.type===(paused==='hidden'?'visibilitychange':'online')).callback();await flush()
    equal(home.requests.length,2,paused+' resume can recover once through the original single-flight path')
    home.requests[1].resolve(publishedPayload);await flush()
    equal(home.state.ready.value,true,paused+' recovery restores the confirmed published snapshot')
  }finally{await home.unmount()}
}

for(const paused of ['hidden','offline']) {
  const home=await mountHome(paused==='hidden'?{hidden:true}:{online:false})
  try {
    equal(home.requests.length,0,paused+' initial mount does not send an unusable request')
    equal(home.state.initialLoadFailed.value,true,paused+' initial mount still exposes an actionable state')
    equal(home.timers.size,0,paused+' initial mount starts no retry timer')
    home.document.hidden=false;home.navigator.onLine=true
    home.listeners.find(item=>item.type===(paused==='hidden'?'visibilitychange':'online')).callback();await flush()
    equal(home.requests.length,1,paused+' initial mount recovers on the matching event')
    home.requests[0].resolve(publishedPayload);await flush()
    equal(home.state.ready.value,true)
  }finally{await home.unmount()}
}

for(const paused of ['hidden','offline']) {
  const home=await mountHome({cached:publishedPayload})
  try {
    home.requests[0].reject(Error('synthetic-503'));await flush()
    equal(home.timers.size,1)
    if(paused==='hidden')home.document.hidden=true
    else home.navigator.onLine=false
    home.listeners.find(item=>item.type===(paused==='hidden'?'visibilitychange':'offline')).callback();await flush()
    equal(home.timers.size,0,paused+' event cancels an already scheduled background retry')
    equal(home.requests.length,1)
    equal(home.state.ready.value,true,paused+' event never erases the last published snapshot')
  }finally{await home.unmount()}
  equal(home.listeners.length,0,paused+' cleanup removes every recovery listener')
}

{
  const legacyRow={id:'legacy-news',title:'旧缓存标题',summary:'',body:'旧的完整公开正文'.repeat(100),bodyMedia:[{id:'body-only'}]}
  const cached={...publishedPayload,news:[legacyRow]}
  const home=await mountHome({cached})
  try{
    equal(home.state.news.value[0].title,'旧缓存标题')
    equal(home.state.news.value[0].bodyPreview.length,260,'legacy cached content keeps only a bounded preview')
    equal('body' in home.state.news.value[0],false,'legacy full body never enters homepage state')
    equal('bodyMedia' in home.state.news.value[0],false,'legacy body embeds never enter homepage state')
    const summary={id:'fresh-news',title:'最新摘要',summary:'',bodyPreview:'服务端有界预览',mediaAssetId:'cover'}
    home.requests[0].resolve({...publishedPayload,news:[summary]});await flush()
    equal(home.state.news.value[0],summary)
    equal(home.saved[0].news[0],summary,'new cache saves the actual summary payload')
    equal(legacyRow.body.length>260,true,'reading old cached data does not mutate its source')
  }finally{await home.unmount()}
}
{
  const home=await mountHome({cached:{...publishedPayload,news:[null]}})
  try{
    equal(home.requests.length,1,'an unusable old cache must never block the fresh summary request')
    equal(home.state.ready.value,false,'corrupt cache must not claim a published snapshot')
    home.requests[0].resolve(publishedPayload);await flush()
    equal(home.state.ready.value,true,'the fresh published summary recovers from corrupt cached rows')
  }finally{await home.unmount()}
}
assert(homeSource.includes('homeNews[0]!.bodyPreview'),'homepage feature must render the bounded preview, not a full-body fallback')
assert(!homeSource.includes('articleBodyText(homeNews[0]!.body)'))

const localStorageDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'localStorage')
Object.defineProperty(globalThis, 'localStorage', { configurable:true, get:() => { throw Error('storage getter blocked') } })
try {
  equal(module.loadPublishedHomeCache(), null, 'a rejected localStorage getter cannot block the public request')
  equal(module.savePublishedHomeCache(payload), false, 'a rejected localStorage getter cannot report a saved snapshot')
} finally {
  if (localStorageDescriptor) Object.defineProperty(globalThis, 'localStorage', localStorageDescriptor)
  else delete globalThis.localStorage
}
console.log(`Home revisit and public empty-state support passed: ${assertions} assertions`)
