import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createHash } from 'node:crypto'

const project = process.env.L12_GALLERY_PROJECT_ROOT || fileURLToPath(new URL('..', import.meta.url))
const require = createRequire(path.join(project, 'package.json'))
const Vue = require('vue')
const ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const sourcePath = path.join(project, 'src/l12/CardArchive.vue')
const source = fs.readFileSync(sourcePath, 'utf8')
const normalize = value => JSON.parse(JSON.stringify(value))
const flush = async () => { for(let i=0;i<8;i++){await Vue.nextTick();await Promise.resolve()} }
const node = (tag,text='') => ({tag,text,props:{},children:[],parent:null})
const descendants = root => [root,...root.children.flatMap(descendants)]
const detach = child => { if(child.parent){const i=child.parent.children.indexOf(child);if(i>=0)child.parent.children.splice(i,1);child.parent=null} }
const teleportRoot=node('body')
const renderer = Vue.createRenderer({querySelector:()=>teleportRoot,createElement:tag=>node(tag),createText:t=>node('#text',t),createComment:t=>node('#comment',t),
  insert(child,parent,anchor=null){detach(child);child.parent=parent;const i=anchor?parent.children.indexOf(anchor):-1;if(i<0)parent.children.push(child);else parent.children.splice(i,0,child)},
  remove:detach,parentNode:child=>child.parent,nextSibling:child=>child.parent?.children[child.parent.children.indexOf(child)+1]??null,
  setText(child,text){child.text=text},setElementText(child,text){child.text=text;child.children=[]},patchProp(child,key,old,value){child.props[key]=value},setScopeId(){}})
const stub = {render(){return Vue.h('div',null,this.$slots.default?.())}}
const base = {id:'S01-0002',number:'S01-0002',nameZh:'基础卡',cardType:'legion',faction:'tianting',product:'S1',products:['S1'],traits:[],cost:2,troops:2000,effect:''}
const product = (id,name,order,extra={}) => ({id,name,releaseOrder:order,active:true,showInGalleryFilter:true,source:'legacy',status:'active',...extra})
const art = (id,code,products) => ({id,artCode:code,baseCardId:'',displayName:id,imageUrl:'',mediaAssetId:'',active:true,kind:'gallery-card',products,effect:'',artist:'',collaboration:''})
function evaluate(code,imports,globals={}){
  const module={exports:{}};const output=ts.transpileModule(code,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText
  vm.runInNewContext(output,{module,exports:module.exports,require:id=>{assert(Object.hasOwn(imports,id),`unmapped real SFC import: ${id}`);return imports[id]},console,...globals});return module.exports
}
async function fixture(sourceKind='legacy'){
  const s1=product('catalog:s1','S1',20),hidden=product('hidden','隐藏产品',0,{showInGalleryFilter:false})
  const collab=product('collab','联动',1,{source:sourceKind,status:sourceKind==='site'?'published':'active'})
  const empty=product('empty','已勾选无卡产品',5),s2=product('catalog:s2','S2',10)
  const inactive=product('inactive','停用产品',0,{active:false}),tie=product('tie','同序产品',20)
  const products=[s1,hidden,collab,empty,s2,inactive,tie]
  const arts=[art('new','Z-10',[collab]),art('s1','A-01',[s1]),art('multi','A-02',[hidden,s2]),art('hidden','A-00',[hidden]),art('inactive','A-03',[inactive]),art('none','A-04',[])]
  const calls=[];const listeners=new Map()
  const {descriptor,errors}=parse(source);assert.deepEqual(errors,[])
  const script=compileScript(descriptor,{id:'gallery-product-order'})
  const api={gallery:async()=>{calls.push('gallery');return arts},presentationCatalog:async()=>{calls.push('presentation');return {regularCards:[],filterProducts:products}}}
  const imports={vue:Vue,'./cardPresentation':{cardTypeFilterKey:type=>type,cardTypeLabel:type=>type,isHorizontalCardType:type=>type==='master'},
    './cardArchiveVersions':{compareArchiveVersions:(a,b)=>a.number.localeCompare(b.number,'zh-CN',{numeric:true}),groupArchiveCards:cards=>cards.map(c=>({logicalId:c.id,defaultVersion:c,versions:[c]}))},
    './decks':{cardArchiveProducts:['S1','S2'],displayCardNumber:c=>c.number,filterableCardCost:c=>c.cost??null,loadCardArchiveCatalog:async()=>{calls.push('catalog');return [base]}},
    './data/cardErrata':{cardErrataForCard:()=>[]},'./platform':{alternateArtApi:api},'./MobileDeferredCardImage.vue':{__esModule:true,default:stub},'./CardDetailContent.vue':{__esModule:true,default:stub},'./site/MobileFilterSheet.vue':{__esModule:true,default:stub}}
  const win={addEventListener:(type,fn)=>listeners.set(type,fn),removeEventListener:type=>listeners.delete(type),matchMedia:()=>({matches:false})}
  const component=evaluate(script.content,imports,{window:win}).default
  const template=compileTemplate({source:descriptor.template.content,filename:sourcePath,id:'gallery-product-order',compilerOptions:{bindingMetadata:script.bindings}})
  assert.deepEqual(template.errors,[])
  component.render=evaluate(template.code,{vue:{...Vue,vModelSelect:{},vModelText:{}}},{window:win}).render
  const root=node('root'),app=renderer.createApp({render:()=>Vue.h(component)})
  app.mount(root);await flush()
  const state=app._instance.subTree.component.setupState
  assert.equal(state.loadError,'');assert.equal(state.loading,false)
  return {app,root,state,products,arts,calls,listeners}
}
let cases=0;const failures=[]
const check=(name,body)=>{cases++;try{body()}catch(error){failures.push({name,error:error.message})}}
for(const sourceKind of ['legacy','catalog','site']){
  const f=await fixture(sourceKind),s=f.state
  const information={cardType:'legion',faction:'联动阵营',cost:0,troops:5432,hp:9,disasterLevel:2,trialValue:3,profession:'战士',traits:['人类'],deckLimit:2,isCounterTactic:false}
  check(`${sourceKind}: gallery projection forwards every optional field`,()=>{
    const projected=s.projectedUploadedArt({...f.arts[0],cardInformation:information},true)
    for(const [key,value]of Object.entries(information))assert.deepEqual(normalize(projected[key]),value)
    assert.equal(projected.galleryStandalone,true)
  })
  check(`${sourceKind}: absent optional information never invents stats`,()=>{
    const projected=s.projectedUploadedArt({...f.arts[0],cardInformation:null},true)
    for(const key of ['cost','troops','hp','disasterLevel','trialValue','deckLimit','isCounterTactic'])assert.equal(projected[key],undefined)
    assert.equal(s.projectedUploadedArt({...f.arts[0],cardInformation:information},false),null)
  })
  const expected=['联动','已勾选无卡产品','S2','S1','同序产品']
  check(`${sourceKind}: filter order comes only from managed order`,()=>assert.deepEqual(normalize(s.productOptions),expected))
  check(`${sourceKind}: explicit enabled empty product stays selectable`,()=>assert(s.productOptions.includes('已勾选无卡产品')))
  check(`${sourceKind}: disabled and unchecked products are absent`,()=>{assert(!s.productOptions.includes('停用产品'));assert(!s.productOptions.includes('隐藏产品'))})
  check(`${sourceKind}: source preference array is not mutated`,()=>assert.deepEqual(f.products.map(p=>p.name),['S1','隐藏产品','联动','已勾选无卡产品','S2','停用产品','同序产品']))
  check(`${sourceKind}: catalog keeps independent number sorting`,()=>assert.equal(s.catalogSort,'number'))
  s.showPage('gallery');await flush()
  check(`${sourceKind}: gallery default uses same managed order`,()=>assert.deepEqual(normalize(s.filteredGallery.map(c=>c.id)),['GALLERY-new','GALLERY-multi','GALLERY-s1','GALLERY-hidden','GALLERY-inactive','GALLERY-none']))
  check(`${sourceKind}: unmanaged product cards remain browseable`,()=>assert.equal(s.filteredGallery.length,6))
  check(`${sourceKind}: gallery sorting summary is product`,()=>assert.match(s.activeFilterSummary,/按产品排序/))
  check(`${sourceKind}: desktop and mobile product options stay identical`,()=>{
    const selects=descendants(f.root).filter(n=>n.tag==='select'&&descendants(n).some(c=>c.tag==='option'&&c.text==='全部产品'))
    assert.equal(selects.length,2)
    for(const select of selects)assert.deepEqual(descendants(select).filter(n=>n.tag==='option').map(n=>n.text),['全部产品',...expected])
  })
  s.product='已勾选无卡产品';await flush()
  check(`${sourceKind}: selecting empty product reports zero cards`,()=>assert.equal(s.filteredGallery.length,0))
  s.product='all';s.sort='number';await flush()
  check(`${sourceKind}: explicit number sort is retained`,()=>assert.deepEqual(normalize(s.filteredGallery.map(c=>c.number)),['A-00','A-01','A-02','A-03','A-04','Z-10']))
  s.sort='product';s.galleryFilterProducts=[...f.products].map(p=>p.name==='S1'?{...p,releaseOrder:0}:p);await flush()
  check(`${sourceKind}: preference update affects filter and card order together`,()=>{
    assert.equal(s.productOptions[0],'S1');assert.equal(s.filteredGallery[0].id,'GALLERY-s1')
  })
  s.galleryFilterProducts=f.products.map(p=>({...p,showInGalleryFilter:false}));await flush()
  check(`${sourceKind}: clearing displayed products leaves stable browse fallback`,()=>{assert.deepEqual(normalize(s.productOptions),[]);assert.equal(s.filteredGallery.length,6)})
  check(`${sourceKind}: only three public source reads occur`,()=>assert.deepEqual(f.calls.sort(),['catalog','gallery','presentation']))
  f.app.unmount();check(`${sourceKind}: event listeners released`,()=>assert.equal(f.listeners.size,0))
}
console.log(JSON.stringify({cases,failed:failures.length,sourceSha256:createHash('sha256').update(source).digest('hex'),scope:'Actual compiled CardArchive SFC with synthetic public API and renderer; native mobile/desktop option templates; no browser or live publication claim',failures},null,2))
if(failures.length)process.exitCode=1
