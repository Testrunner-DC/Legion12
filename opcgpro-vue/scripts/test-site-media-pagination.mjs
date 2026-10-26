import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const front=path.resolve(import.meta.dirname,'..')
const require=createRequire(import.meta.url)
const Vue=require('vue')
const {parse,compileScript}=require('@vue/compiler-sfc')
const ts=require('typescript')
const source=fs.readFileSync(path.join(front,'src/l12/site/SiteMediaPicker.vue'),'utf8')
const pending=[];const cleanups=[];const emitted=[]
const imports={...Vue,onBeforeUnmount:callback=>cleanups.push(callback)}
const {descriptor,errors}=parse(source)
assert.deepEqual(errors,[])
const compiled=compileScript(descriptor,{id:'media-pagination-unit'})
const code=ts.transpileModule(compiled.content,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText
const module={exports:{}}
new Function('require','exports','module',code)(id=>{
  if(id==='vue')return imports
  if(id==='@/l12/platform')return {adminApi:{searchSiteMedia:options=>new Promise((resolve,reject)=>pending.push({options,resolve,reject}))}}
  throw new Error('Unexpected SFC dependency '+id)
},module.exports,module)
const props=Vue.reactive({modelValue:'already-bound',kind:'card-art',emptyLabel:'选择',selectedMedia:undefined})
const view=module.exports.default.setup(props,{expose(){},emit:(event,value)=>emitted.push([event,value])})
const flush=async()=>{await Promise.resolve();await Vue.nextTick();await Promise.resolve()}
const row=(id,kind='card-art')=>({id,kind,altText:id,contentHash:'hash-'+id})
const page=(id,kind='card-art',number=1)=>({items:[row(id,kind)],page:number,pageSize:24,total:1005,hasMore:true})
assert.equal(pending.length,0,'must remain lazy until opened')
await view.show();assert.equal(pending.length,1);assert.equal(pending[0].options.pageSize,24)
pending.shift().resolve(page('first'));await flush()
assert.equal(view.result.value.items[0].id,'first')
await view.show();const closed=pending.shift();view.close();closed.resolve(page('closed-late'));await flush()
assert.equal(view.result.value.items[0].id,'first');assert.equal(view.busy.value,false)
await view.show();const old=pending.shift()
props.kind='rule';await flush();const current=pending.shift();assert.equal(current.options.kind,'rule')
current.resolve(page('rule-current','rule'));await flush();old.resolve(page('wrong-old'));await flush()
assert.equal(view.result.value.items[0].id,'rule-current')
view.choose(row('wrong-kind'));assert.deepEqual(emitted,[])
view.choose(row('rule-current','rule'));assert.equal(emitted[0][1],'rule-current');assert.equal(view.open.value,false)
emitted.length=0
await view.show();pending.shift().reject(new Error('synthetic-read-error'));await flush()
assert.equal(view.error.value,'synthetic-read-error');assert.equal(props.modelValue,'already-bound')
view.choose(row('rule-current','rule'));assert.deepEqual(emitted,[],'failed read must not select stale rows')
const retry=view.load(3);assert.equal(view.requestedPage.value,3);pending.shift().resolve(page('recovered','rule',3));await retry
assert.equal(view.result.value.page,3);assert.equal(view.error.value,'')
view.search.value='  old-asset  ';view.applySearch();assert.equal(pending[0].options.search,'old-asset');assert.equal(pending[0].options.page,1)
const unmounted=pending.shift();for(const cleanup of cleanups)cleanup();unmounted.resolve(page('unmounted','rule'));await flush()
assert.equal(view.result.value.items[0].id,'recovered')
view.clear();assert.deepEqual(emitted,[['update:modelValue','']])
for(const name of ['AdminAlternateArtsPanel','AdminArticlesPanel','AdminSiteContentPanel','AdminRuleRulingsPanel','ArticleDocumentEditor'])
  assert(fs.readFileSync(path.join(front,'src/l12/site',name+'.vue'),'utf8').includes('SiteMediaPicker'),name+' must share the paged picker')
assert(source.includes('generation++')&&source.includes('maxlength="160"')&&source.includes('type="button"'))
const referenceSource=fs.readFileSync(path.join(front,'src/l12/site/siteMediaReferences.ts'),'utf8')
const referenceModule={exports:{}}
const groups=[]
let referenceFault=''
const referenceCode=ts.transpileModule(referenceSource,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText
new Function('require','exports','module',referenceCode)(()=>({adminApi:{searchSiteMedia:async options=>{
 groups.push(options.ids)
 const items=options.ids.map(id=>row(id))
 if(referenceFault==='missing')items.pop()
 if(referenceFault==='duplicate')items[1]=items[0]
 if(referenceFault==='unexpected')items[0]=row('not-requested')
 return {items}
}}}),referenceModule.exports,referenceModule)
const resolveReferences=referenceModule.exports.loadSiteMediaReferences
assert.deepEqual(await resolveReferences([]),[]);assert.equal(groups.length,0)
const ids=Array.from({length:205},(_,index)=>'ref-'+index)
assert.deepEqual((await resolveReferences([...ids,ids[0]])).map(item=>item.id),ids)
assert.deepEqual(groups.map(group=>group.length),[100,100,5])
for(const fault of ['missing','duplicate','unexpected']){
 referenceFault=fault
 await assert.rejects(()=>resolveReferences(['ref-a','ref-b']))
}
referenceFault=''
await assert.rejects(()=>resolveReferences(['bad,id']))
await assert.rejects(()=>resolveReferences(Array.from({length:1001},(_,index)=>'ref-'+index)))
for(const name of ['AdminAlternateArtsPanel','AdminArticlesPanel','AdminSiteContentPanel','AdminRuleRulingsPanel'])
 assert(!fs.readFileSync(path.join(front,'src/l12/site',name+'.vue'),'utf8').includes('adminApi.siteMedia('),name+' must not depend on the truncated legacy list')
const articleSource=fs.readFileSync(path.join(front,'src/l12/site/AdminArticlesPanel.vue'),'utf8')
const ruleSource=parse(fs.readFileSync(path.join(front,'src/l12/site/AdminRuleRulingsPanel.vue'),'utf8')).descriptor.scriptSetup.content
const ruleAst=ts.createSourceFile('rules.ts',ruleSource,ts.ScriptTarget.Latest,true,ts.ScriptKind.TS)
const ruleLoad=ruleAst.statements.find(node=>ts.isFunctionDeclaration(node)&&node.name?.text==='load').getText(ruleAst)
const publishDocument=ruleLoad.indexOf('ruleCenterDocument.value = centerDocument')
const publishVersion=ruleLoad.indexOf('centerVersion.value = center.version')
const loadReferences=ruleLoad.indexOf('await loadSiteMediaReferences')
assert(publishDocument>=0&&publishVersion>publishDocument&&loadReferences>publishVersion,'Rules must publish matching document/CAS version synchronously before fallible preview IO')
assert(!ruleLoad.slice(publishDocument,publishVersion).includes('await '))
assert(articleSource.includes("!bodyMediaLoading && !bodyMediaError"),'Unconfirmed references must not open an empty body editor')
const script=parse(articleSource).descriptor.scriptSetup.content
const ast=ts.createSourceFile('article.ts',script,ts.ScriptTarget.Latest,true,ts.ScriptKind.TS)
const hydrate=ast.statements.find(node=>ts.isFunctionDeclaration(node)&&node.name?.text==='hydrateArticleMedia').getText(ast)
const hydratePending=[];const selectedArticle=Vue.ref({body:'{"blocks":[{"type":"image","mediaAssetId":"old-image"}]}',mediaAssetId:'old-cover'})
const mediaRows=Vue.ref([row('prior')]);const loading=Vue.ref(false);const mediaError=Vue.ref('')
const hydration=ts.transpileModule(hydrate,{compilerOptions:{module:ts.ModuleKind.None,target:ts.ScriptTarget.ES2022}}).outputText
const hydrateActual=new Function('selected','bodyMediaLoading','bodyMediaError','media','parseArticleBody','loadSiteMediaReferences',
 'let bodyMediaGeneration=0;'+hydration+';return hydrateArticleMedia')(selectedArticle,loading,mediaError,mediaRows,JSON.parse,
 ids=>new Promise((resolve,reject)=>hydratePending.push({ids,resolve,reject})))
const oldRead=hydrateActual();assert.equal(loading.value,true);assert.deepEqual(hydratePending[0].ids,['old-cover','old-image'])
const unchanged=selectedArticle.value.body
hydratePending.shift().reject(new Error('synthetic-reference-failure'));await oldRead
assert.equal(selectedArticle.value.body,unchanged);assert.equal(mediaRows.value[0].id,'prior');assert.equal(mediaError.value,'synthetic-reference-failure')
const lateA=hydrateActual();const heldA=hydratePending.shift()
selectedArticle.value={body:'{"blocks":[]}',mediaAssetId:'new-cover'}
const freshB=hydrateActual();hydratePending.shift().resolve([row('new-cover')]);await freshB
heldA.resolve([row('old-cover')]);await lateA
assert.equal(mediaRows.value[0].id,'new-cover');assert.equal(mediaError.value,'');assert.equal(loading.value,false)
console.log('Actual media SFC/reference modules: lazy pages, lifecycle fences, failure/retry, five consumers; 205 IDs in 100/100/5; corrupt/missing bindings rejected; actual article hydration preserves body and rejects late selections')
await import('./test-platform-site-content-contracts.mjs')
