import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createHash } from 'node:crypto'

const project = process.env.L12_GALLERY_PROJECT_ROOT || fileURLToPath(new URL('..', import.meta.url))
const require = createRequire(path.join(project, 'package.json'))
const Vue = require('vue'), ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const plain = value => JSON.parse(JSON.stringify(value))
const flush = async () => { for (let i=0;i<12;i++) { await Vue.nextTick(); await Promise.resolve() } }
const makeNode = (tag,text='') => ({tag,text,props:{},children:[],parent:null})
const descendants = root => [root,...root.children.flatMap(descendants)]
const textContent = root => root.text + root.children.map(textContent).join('')
const detach = child => { if(child.parent){const i=child.parent.children.indexOf(child);if(i>=0)child.parent.children.splice(i,1);child.parent=null} }
const body = makeNode('body')
const renderer = Vue.createRenderer({querySelector:()=>body,createElement:tag=>makeNode(tag),createText:t=>makeNode('#text',t),createComment:t=>makeNode('#comment',t),
  insert(child,parent,anchor=null){detach(child);child.parent=parent;const i=anchor?parent.children.indexOf(anchor):-1;if(i<0)parent.children.push(child);else parent.children.splice(i,0,child)},remove:detach,
  parentNode:child=>child.parent,nextSibling:child=>child.parent?.children[child.parent.children.indexOf(child)+1]??null,
  setText(child,text){child.text=text},setElementText(child,text){child.text=text;child.children=[]},patchProp(child,key,old,value){child.props[key]=value},setScopeId(){}})
const stub = {render(){return Vue.h('div',null,this.$slots.default?.())}}
const stubs = {__esModule:true,default:stub}
const sources = new Map()
function evaluate(code,imports,globals={}) {
  const module={exports:{}}
  const output=ts.transpileModule(code,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText
  vm.runInNewContext(output,{module,exports:module.exports,require:name=>{assert(name in imports,'Unexpected import '+name);return imports[name]},console,...globals})
  return module.exports
}
function compile(file,imports,globals={}) {
  const source=fs.readFileSync(path.join(project,'src/l12',file),'utf8');sources.set(file,createHash('sha256').update(source).digest('hex'))
  const {descriptor,errors}=parse(source);assert.deepEqual(errors,[])
  const script=compileScript(descriptor,{id:'optional-gallery-information'})
  const component=evaluate(script.content,imports,globals).default
  const template=compileTemplate({source:descriptor.template.content,filename:file,id:'optional-gallery-information',compilerOptions:{bindingMetadata:script.bindings,hoistStatic:false}})
  assert.deepEqual(template.errors,[])
  component.render=evaluate(template.code,{vue:{...Vue,vModelText:{},vModelSelect:{},vModelCheckbox:{}}},globals).render
  return component
}
const information={cardType:'legion',faction:'联动阵营',cost:0,troops:5432,hp:9,disasterLevel:2,trialValue:3,profession:'战士',traits:['人类','英雄'],deckLimit:2,isCounterTactic:false}
const art={id:'collab',artCode:'PR-026',baseCardId:'',displayName:'联动卡',mediaAssetId:'media',imageUrl:'',thumbnailUrl:'',active:false,kind:'gallery-card',effect:'',products:[],rarities:[],cardInformation:information}
const calls={save:[],metadata:[],product:[],article:0};let stored=plain(art), products=[];let failRefresh=false
const adminApi={alternateArtProducts:async()=>{if(failRefresh){failRefresh=false;throw Error('synthetic refresh failure')}return products},alternateArtRarities:async()=>[],
  searchAlternateArts:async()=>({items:[stored],total:1,page:1,pageSize:20}),saveArticle:async()=>{calls.article++;throw Error('Merchandise route must not run')},
  saveAlternateArt:async draft=>{calls.save.push(plain(draft));stored={...stored,...plain(draft),id:draft.id||'created-card'};return stored},
  saveAlternateArtMetadata:async draft=>{calls.metadata.push(plain(draft));return stored},
  saveAlternateArtProduct:async draft=>{calls.product.push(plain(draft));const product={id:'legacy:new-gallery',name:draft.name,active:true};products.push(product);return product}}
const risk = evaluate(fs.readFileSync(path.join(project,'src/l12/site/useAdminRiskAction.ts'),'utf8'),{vue:Vue})
const imports={vue:Vue,'@/l12/platform':{adminApi,hasPermission:()=>true,platformState:Vue.reactive({account:{id:'admin'}})},
  '@/l12/decks':{loadDeckCatalog:async()=>[]},'./useAdminRiskAction':risk,'@/l12/deploymentBase':{sharedGalleryAdminPath:'',usesSharedGallery:false},'./alternateArtRuleExplanation':{alternateArtRuleExplanation:()=>''}}
for(const name of ['./MediaUploadField.vue','./SiteMediaPicker.vue','@/l12/SingleCardPicker.vue','@/l12/CardImage.vue','@/l12/CatalogCardDetails.vue','./AdminRiskActionDialog.vue','./AdminAccountPicker.vue'])imports[name]=stubs
const listeners=new Map(),window={addEventListener:(name,fn)=>listeners.set(name,fn),removeEventListener:name=>listeners.delete(name)}
const component=compile('site/AdminAlternateArtsPanel.vue',imports,{window})
const root=makeNode('root'),app=renderer.createApp({render:()=>Vue.h(component)})
app.mount(root);await flush()
const state=app._instance.subTree.component.setupState
let cases=0;const failures=[]
const check=(name,run)=>{cases++;try{run()}catch(error){failures.push({name,error:error.message})}}
state.openNewArt();state.artForm.kind='gallery-card';await flush()
check('all eleven new fields start empty',()=>assert.deepEqual(plain(state.optionalCardInformation()),Object.fromEntries(Object.keys(information).map(key=>[key,null]))))
check('optional fieldset exists without native required controls',()=>{const group=descendants(root).find(n=>n.tag==='fieldset'&&textContent(n).includes('卡牌资料（全部选填）'));assert(group);assert(descendants(group).filter(n=>['input','select'].includes(n.tag)).length===11);assert(descendants(group).every(n=>n.props.required==null||n.props.required===false))})
check('six numeric entries allow blank and zero',()=>{const controls=descendants(root).filter(n=>n.tag==='input'&&n.props.type==='number');assert.equal(controls.length,6);assert(controls.every(n=>n.props.min==='0'&&n.props.step==='1'))})
state.editRegistryArt(art);await flush()
check('editing restores every stored optional field including zero and false',()=>assert.deepEqual(plain(state.optionalCardInformation()),information))
state.showCardDetails(art)
check('admin details receive all information',()=>{for(const [key,value]of Object.entries(information))assert.deepEqual(plain(state.detailCard[key]),value)})
state.cardInformationForm.traits='人类、人类,英雄， ';check('traits normalize without empty or duplicate entries',()=>assert.deepEqual(plain(state.optionalCardInformation().traits),['人类','英雄']))
await state.performSaveArt();await flush()
check('save uses existing gallery route and contains optional values',()=>assert.deepEqual(calls.save[0].cardInformation,information))
state.openNewArt();state.artForm.kind='gallery-card';state.artForm.active=false;state.artForm.artCode='PR-EMPTY';state.artForm.displayName='全部选填为空';state.artForm.mediaAssetId='media';await flush()
check('new entry resets previous optional information',()=>assert(Object.values(plain(state.optionalCardInformation())).every(value=>value===null)))
await state.performSaveArt();await flush()
check('empty fields save as null without invented zero or false',()=>assert(Object.values(calls.save[1].cardInformation).every(value=>value===null)))
state.openNewArt();state.openProductDialog();state.productForm.title='画廊联动';failRefresh=true
await state.saveGalleryProduct();await flush()
check('create product calls gallery metadata only',()=>{assert.equal(calls.article,0);assert.deepEqual(calls.product,[{name:'画廊联动',active:true}]);assert.equal(state.pendingProductId,'legacy:new-gallery')})
await state.saveGalleryProduct();await flush()
check('retry selection does not create another product',()=>{assert.equal(calls.product.length,1);assert(state.artForm.productIds.includes('legacy:new-gallery'));assert.equal(state.pendingProductId,'')})
app.unmount();check('admin listener released',()=>assert.equal(listeners.size,0))
const detail=compile('CardDetailContent.vue',{vue:Vue,'./decks':{displayCardNumber:card=>card.number},'./cardPresentation':{cardTypeLabel:type=>type,isHorizontalCardType:()=>false},'./CardImage.vue':stubs})
for(const filled of [true,false]) {
  const card={id:'GALLERY-card',number:'PR-026',nameZh:'联动卡',cardType:'gallery-card',product:'',faction:'',galleryStandalone:true,...(filled?information:{})}
  const host=makeNode('root'),detailApp=renderer.createApp({render:()=>Vue.h(detail,{card})});detailApp.mount(host);await flush()
  check(`details ${filled?'filled':'blank'} have correct numeric rows`,()=>assert.deepEqual(descendants(host).filter(n=>n.tag==='dt').map(textContent),filled?['费用','兵力','血量','天灾等级','试炼值','同名卡上限']:[]))
  if(filled)check('detail renders zero and all six values',()=>assert.deepEqual(descendants(host).filter(n=>n.tag==='dd').map(textContent),['0','5432','9','2','3','2']))
  detailApp.unmount()
}
console.log(JSON.stringify({cases,failed:failures.length,scope:'Actual compiled admin/detail SFCs, synthetic API and memory renderer; no native browser geometry claim',sources:Object.fromEntries(sources),failures},null,2))
if(failures.length)process.exitCode=1
