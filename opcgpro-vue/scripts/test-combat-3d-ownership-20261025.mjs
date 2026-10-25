import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const require = createRequire(import.meta.url), ts = require('typescript')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../src/l12/game')
const source = fs.readFileSync(path.join(root, 'CombatMotionPresentationLayer.vue'), 'utf8')
const script = source.match(/<script setup[^>]*>([\s\S]*?)<\/script>/)[1]
const ast = ts.createSourceFile('CombatMotion.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const statements = ast.statements.filter(node => !ts.isImportDeclaration(node))
const names = statements.flatMap(node => ts.isFunctionDeclaration(node) && node.name ? [node.name.text]
  : ts.isVariableStatement(node) ? node.declarationList.declarations.flatMap(item => ts.isIdentifier(item.name) ? [item.name.text] : []) : [])
const flush = async () => { for (let index = 0; index < 35; index++) await Promise.resolve() }

// Real component functions/watch callbacks and original coordinator; deterministic
// DOM/WAAPI adapters establish lifecycle contracts, not rendered 3D pixels.
function fixture({ mode = 'standard', paused = false, tapped = false } = {}) {
  const timers = [], watches = [], unmounts = [], animations = [], emitted = []
  class Element {
    constructor(tag = 'div') {
      this.tagName = tag.toUpperCase(); this.children = []; this.parentElement = null
      this.dataset = {}; this.className = ''; this.attributes = new Map(); this.connected = false
      this.style = { visibility:'', transform:'none', border:'1px solid white', background:'#0b0d0e', outline:'auto', boxShadow:'ring' }
      this.rect = { left:100, top:100, width:100, height:140 }; this.complete = true; this.naturalWidth = 100
    }
    get classList() { return { contains:value=>this.className.split(/\s+/).includes(value),
      remove:(...values)=>{this.className=this.className.split(/\s+/).filter(value=>!values.includes(value)).join(' ')},
      add:value=>{this.className+=' '+value} } }
    get isConnected() { return this.connected || Boolean(this.parentElement?.isConnected) }
    append(...nodes) { nodes.forEach(node=>this.appendChild(node)) }
    appendChild(child) { this.children.push(child); child.parentElement=this; return child }
    remove() { if(this.parentElement)this.parentElement.children.splice(this.parentElement.children.indexOf(this),1);this.parentElement=null;this.connected=false }
    removeAttribute(name) { this.attributes.delete(name)
      if(name.startsWith('data-'))delete this.dataset[name.slice(5).replace(/-([a-z])/g,(_,letter)=>letter.toUpperCase())] }
    getBoundingClientRect() { return this.rect }
    closest() { for(let node=this;node;node=node.parentElement)if(node.dataset.cardInstanceId)return node;return null }
    contains(node) { return node===this || this.children.some(child=>child.contains(node)) }
    querySelectorAll(selector) {
      const descendants=this.children.flatMap(child=>[child,...child.querySelectorAll('*')])
      return selector==='*' ? descendants : descendants.filter(child=>selector==='img' ? child.tagName==='IMG'
        : selector==='.card-power' ? child.className==='card-power' : false)
    }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null }
    cloneNode() {
      const clone=new Element(this.tagName);clone.style={...this.style};clone.className=this.className;clone.dataset={...this.dataset}
      this.children.forEach(child=>clone.appendChild(child.cloneNode(true)));return clone
    }
    animate(frames, options) {
      const listeners=new Map();let yes,no
      const animation={target:this,frames,options,playState:'running',
        finished:new Promise((resolve,reject)=>{yes=resolve;no=reject}),
        addEventListener(type,callback) {if(!listeners.has(type))listeners.set(type,[]);listeners.get(type).push(callback)},
        finish() {if(this.playState!=='running')return;this.playState='finished';for(const fn of listeners.get('finish')||[])fn();yes()},
        cancel() {if(this.playState!=='running')return;this.playState='idle';for(const fn of listeners.get('cancel')||[])fn();no(new Error('cancelled'))},
      }
      void animation.finished.catch(()=>{})
      animations.push(animation);return animation
    }
  }
  const host=new Element();host.connected=true
  const live=new Map()
  function add(id, rested=false) {
    const node=new Element('button');node.dataset.cardInstanceId=id;node.className='card-tile selected'+(rested?' tapped':'')
    node.connected=true;node.style.transform=rested?'rotate(90deg)':'none'
    const badge=new Element('b');badge.className='card-power';node.append(new Element('img'),badge);live.set(id,node);return node
  }
  const unit=add('unit',tapped),target=add('target'),grave=new Element();grave.rect={left:500,top:400,width:80,height:100}
  const props={events:[],matchId:'combat-qa',revision:1,synchronizing:false,paused,playbackSpeed:null}
  const env={console,Map,Set,WeakMap,Promise,HTMLElement:Element,HTMLImageElement:Element,CSS:{escape:value=>value},
    setTimeout:fn=>{timers.push(fn);return timers.length},clearTimeout:()=>{},
    document:{addEventListener:()=>{},removeEventListener:()=>{},createElement:tag=>new Element(tag),querySelector:selector=>selector.includes('data-l12-zone') ? grave
      : live.get(selector.match(/data-card-instance-id="([^"]+)"/)?.[1]) || null,
      querySelectorAll:()=>[...live.values()]},
    window:{addEventListener:()=>{},removeEventListener:()=>{}},
    getComputedStyle:element=>({display:'block',visibility:element.style.visibility || 'visible',transform:element.style.transform || 'none'}),
    landscapeTeleportElement:()=>host,viewportLayoutRect:element=>element.rect,viewportRect:element=>element.rect,
    snapshotDecodedAuthoritativeCardVisual:element=>element.querySelectorAll('img').every(image=>image.complete&&image.naturalWidth>0),
    l12BattleMotionDisabled:()=>mode==='off',
    l12AnimationDuration:(ms,min)=>Math.max(min,Math.round(ms*(mode==='off'?0:mode==='fast'?.55:1))),
    defineProps:()=>props,withDefaults:value=>value,defineEmits:()=>((...args)=>emitted.push(args)),
    nextTick:callback=>Promise.resolve().then(callback),onMounted:()=>{},onBeforeUnmount:callback=>unmounts.push(callback),
    watch:(select,callback,options={})=>{const item={select,callback,last:JSON.stringify(select())};watches.push(item);if(options.immediate)callback(select());return()=>{}},
  }
  const module={exports:{}}
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(path.join(root,'presentationSequenceCoordinator.ts'),'utf8'),
    {compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS}}).outputText,{...env,module,exports:module.exports})
  props.sequenceCoordinator=module.exports.createPresentationSequenceCoordinator()
  const projectionModule={exports:{}}
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(path.join(root,'visualTransitionProjection.ts'),'utf8'),
    {compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS}}).outputText,
    {...env,module:projectionModule,exports:projectionModule.exports})
  Object.assign(env,projectionModule.exports)
  vm.createContext(env)
  vm.runInContext(ts.transpileModule(statements.map(node=>node.getText(ast)).join('\n')
    +'\nglobalThis.__combat=Object.defineProperties({}, {'+names.map(name=>`${name}:{get:()=>${name}}`).join(',')+'})',
    {compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.ESNext}}).outputText,env)
  function changed() {for(const item of watches){const next=JSON.stringify(item.select());if(next!==item.last){item.last=next;item.callback(item.select())}}}
  async function advance() {for(let i=0;i<12;i++){await flush();const batch=timers.splice(0);batch.forEach(fn=>fn())}await flush()}
  function apply(events,{remove=[],revision=props.revision+1}={}) {
    props.events=[...props.events,...events];props.revision=revision;changed()
    for(const id of remove){const node=live.get(id);if(node)node.connected=false;live.delete(id)}
  }
  async function settle() {for(let i=0;i<8;i++){animations.filter(item=>item.playState==='running').forEach(item=>item.finish());await advance()}}
  return {props,state:env.__combat,host,live,unit,target,animations,emitted,advance,apply,settle,add,
    active:()=>animations.filter(item=>item.playState==='running'),
    pause:async value=>{props.paused=value;changed();await advance()},
    sync:async()=>{props.synchronizing=true;props.revision++;changed();await advance()},
    dispose:()=>{unmounts.forEach(fn=>fn());props.sequenceCoordinator.reset()},
    match:async()=>{props.matchId='combat-next';props.events=[];props.revision=1;changed();await advance()},
  }
}
const publicCard=(id,extra={})=>({instanceId:id,cardId:'public',name:'公开军团',tapped:false,hidden:false,...extra})
const combat=(sequence=1,text='造成 3000 点战斗伤害；防守军团以当前兵力 2000 反击',cards=[publicCard('unit'),publicCard('target')])=>({sequence,type:'combat',playerIndex:0,text,cards})
const leave=(sequence=2,card=publicCard('target'))=>({sequence,type:'leave',playerIndex:1,text:'公开军团阵亡',cards:[card]})
const cases=[]
async function test(name,run){try{await run();cases.push({name,passed:true})}catch(error){cases.push({name,passed:false,error:error.stack})}}
await test('authority damage parser and non-death leave exclusions remain unchanged',async()=>{
  const f=fixture();try{
    assert.equal(f.state.combatDamageValue(combat(),0),2000);assert.equal(f.state.combatDamageValue(combat(),1),3000)
    for(const text of ['即将阵亡，由友军代替承受','返回手牌','回到手牌','位移','移动','转移','弃置'])assert.equal(f.state.isDefeatLeave({...leave(),text}),false)
  }finally{f.dispose()}
})
for(const tapped of [false,true]) await test(`survivor uses one real-card 3D recoil with intact base pose/${tapped}`,async()=>{
  const f=fixture({tapped});try{await f.advance();f.apply([combat()]);await f.advance()
    const recoil=f.animations.find(item=>item.target===f.unit)
    assert(recoil,'real authority itself must recoil; badge-only is insufficient')
    assert(recoil.frames.some(frame=>/perspective\(/.test(frame.transform)&&/rotateX\(/.test(frame.transform)))
    assert.equal(recoil.options.duration,280);assert.equal(recoil.frames.at(-1).transform,f.unit.style.transform)
    assert.equal(f.host.children.length,0,'survivor must not get a clone')
    assert(f.props.sequenceCoordinator.ownsVisualKey('unit'));await f.settle()
    assert.equal(f.unit.style.transform,tapped?'rotate(90deg)':'none');assert.equal(f.props.sequenceCoordinator.ownsVisualKey('unit'),false)
  }finally{f.dispose()}
})
await test('zero, missing and ranged-no-loss damage never invent a hit',async()=>{
  const f=fixture();try{await f.advance();f.apply([combat(1,'造成 0 点战斗伤害；进攻无损')]);await f.advance()
    assert.equal(f.animations.length,0);f.apply([combat(2,'双方交战')]);await f.advance();assert.equal(f.animations.length,0)
  }finally{f.dispose()}
})
await test('confirmed damage scales one real recoil without changing its duration or final pose',async()=>{
  const poses=[]
  for(const damage of [1000,6000,10000]){const f=fixture({tapped:true});try{
    await f.advance();f.apply([combat(1,`进攻无损；造成 ${damage} 点战斗伤害`)]);await f.advance()
    const hit=f.animations.find(item=>item.target===f.target);assert(hit)
    const mid=hit.frames.find(frame=>frame.offset===.28).transform.match(/translate3d\(0,([-.\d]+)px,([-.\d]+)px\)/)
    assert(mid);poses.push([+mid[1],+mid[2]])
    assert.equal(hit.options.duration,280);assert.equal(hit.frames.at(-1).transform,'none');assert.equal(f.host.children.length,0)
    await f.settle()
  }finally{f.dispose()}}
  assert(poses.every((pose,index)=>!index||pose[0]>poses[index-1][0]&&pose[1]>poses[index-1][1]))
})
await test('real survivor marks only its animation interval frame-free and restores the prior marker',async()=>{
  const f=fixture();try{
    await f.advance();f.apply([combat()]);await f.advance()
    assert(f.unit.dataset.l12CombatRecoil,'a real-card recoil needs scoped UI-frame suppression')
    assert.match(source,/:global\(\[data-l12-combat-recoil\]\)[\s\S]*?border-color:transparent!important/)
    await f.pause(true);assert.equal(f.unit.dataset.l12CombatRecoil,undefined)
    await f.pause(false);await f.settle();assert.equal(f.target.dataset.l12CombatRecoil,undefined)
    assert.equal(f.unit.style.transform,'none');assert.equal(f.unit.style.border,'1px solid white')
  }finally{f.dispose()}
})
await test('hidden first card cannot borrow an old public face or change defender damage index',async()=>{
  const f=fixture();try{await f.advance();f.apply([combat(1,undefined,[publicCard('unit',{hidden:true}),publicCard('target')])]);await f.advance()
    assert(!f.animations.some(item=>item.target===f.unit || item.target===f.unit.querySelector('.card-power')))
    assert(f.animations.some(item=>item.target===f.target),'remaining defender has real positive damage')
    assert.equal(f.state.fieldSnapshots.has('unit'),false)
  }finally{f.dispose()}
})
await test('hidden departed card never falls back to cached public snapshot',async()=>{
  const f=fixture();try{await f.advance();f.apply([leave(1,publicCard('target',{hidden:true}))],{remove:['target']});await f.advance()
    assert.equal(f.host.children.length,0)
  }finally{f.dispose()}
})
await test('confirmed death is one 920ms 3D owner with frame-free clone and exact defender owner',async()=>{
  const f=fixture();try{await f.advance();f.apply([leave(1),combat(2)],{remove:['target']});await f.advance()
    assert.equal(f.host.children[0]?.style.visibility,'visible','prepared victim keeps its pixels while the other hit waits/plays')
    await f.settle()
    const deaths=f.animations.filter(item=>item.target.className==='l12-combat-defeat-ghost')
    assert.equal(deaths.length,1);assert.equal(deaths[0].options.duration,920)
    assert(deaths[0].frames.some(frame=>/perspective\(/.test(frame.transform)&&/rotateX\(/.test(frame.transform)))
    const clone=deaths[0].target.children[0];assert.equal(clone.style.border,'0');assert.equal(clone.style.background,'transparent')
    assert.equal(clone.dataset.l12CombatAnimationFace,'true');assert.equal(clone.dataset.cardInstanceId,undefined)
    assert.equal(f.state.defeatOwner(combat(),publicCard('target'),1),1);assert.equal(f.host.children.length,0)
  }finally{f.dispose()}
})
await test('combat followed by return or replacement is never an unconfirmed death',async()=>{
  for(const text of ['返回手牌','即将阵亡，由友军代替承受']){const f=fixture();try{await f.advance();f.apply([combat(),{...leave(),text}],{remove:['target']});await f.advance();await f.settle()
    assert(!f.animations.some(item=>item.target.className==='l12-combat-defeat-ghost'))
  }finally{f.dispose()}}
})
await test('prepared pause defers real recoil and unpause grants it exactly once',async()=>{
  const f=fixture({paused:true});try{await f.advance();f.apply([combat()]);await f.advance();assert.equal(f.animations.length,0)
    await f.pause(false);assert(f.animations.some(item=>item.target===f.unit));await f.settle()
    const count=f.animations.length;await f.pause(true);await f.pause(false);assert.equal(f.animations.length,count)
  }finally{f.dispose()}
})
await test('mid-motion pause cancels without replay and releases the shared owner',async()=>{
  const f=fixture();try{await f.advance();f.apply([combat()]);await f.advance();await f.pause(true)
    assert.equal(f.active().length,0);assert.equal(f.props.sequenceCoordinator.ownsVisualKey('unit'),false)
    const unitCount=f.animations.filter(item=>item.target===f.unit).length;await f.pause(false)
    assert.equal(f.animations.filter(item=>item.target===f.unit).length,unitCount,'cancelled unit never replays')
    assert(f.animations.some(item=>item.target===f.target),'the other unstarted real hit remains valid')
  }finally{f.dispose()}
})
for(const boundary of ['sync','match']) await test(`recovery boundary cancels pending/active work and old callbacks/${boundary}`,async()=>{
  const f=fixture();try{await f.advance();f.apply([combat()]);await f.advance();await f[boundary]()
    assert.equal(f.active().length,0);assert.equal(f.host.children.length,0)
    f.animations.forEach(item=>item.finish());await f.advance();assert.equal(f.host.children.length,0)
  }finally{f.dispose()}
})
await test('same sequence retransmission and revision-only replacement never replay',async()=>{
  const f=fixture();try{await f.advance();const event=combat();f.apply([event]);await f.advance();await f.settle()
    const count=f.animations.length;f.apply([event]);await f.advance();assert.equal(f.animations.length,count)
  }finally{f.dispose()}
})
await test('original coordinator serializes preceding owner and survivor without a second clone',async()=>{
  const f=fixture();try{await f.advance();let release
    const prior=f.props.sequenceCoordinator.reserve(1,30,{visualKey:'unit'})
    prior.prepareVisualOwner({show(){},hide(){},dispose(){}})
    void prior.waitUntilGranted().then(fn=>{release=fn});await f.advance()
    f.apply([combat(2)]);await f.advance();assert.equal(f.animations.length,0)
    release();await f.advance();assert(f.animations.some(item=>item.target===f.unit));assert.equal(f.host.children.length,0)
    await f.settle();assert.equal(f.props.sequenceCoordinator.ownsVisualKey('unit'),false)
  }finally{f.dispose()}
})
await test('old death finish cannot delete a new snapshot for the reentered instance',async()=>{
  const f=fixture();try{await f.advance();f.apply([leave(1)],{remove:['target']});await f.advance()
    const replacement=f.add('target');f.props.revision++;f.apply([]);await f.advance()
    const snapshot=f.state.fieldSnapshots.get('target');assert(snapshot)
    await f.settle();assert.equal(f.state.fieldSnapshots.get('target'),snapshot);assert.equal(f.live.get('target'),replacement)
  }finally{f.dispose()}
})
await test('a reentered face decoded after the authority render refreshes its exact current death pixels',async()=>{
  const f=fixture();try{
    await f.advance();f.apply([leave(1)],{remove:['target']});await f.advance();await f.settle()
    const replacement=f.add('target'),image=replacement.querySelector('img')
    image.complete=false;image.naturalWidth=0;f.apply([]);await f.advance()
    assert.equal(f.state.fieldSnapshots.has('target'),false)
    image.complete=true;image.naturalWidth=100
    f.state.cardFaceLoaded({target:image});await f.advance()
    assert(f.state.fieldSnapshots.has('target'),'natural decode must refresh cache without an invented extra authority revision')
    f.apply([leave(2)],{remove:['target']});await f.advance()
    assert.equal(f.host.children.length,1);await f.settle();assert.equal(f.host.children.length,0)
  }finally{f.dispose()}
})
await test('image-load capture is attached to document and retired with the component',()=>{
  assert(source.includes("document.addEventListener('load', cardFaceLoaded, true)"))
  assert(source.includes("document.removeEventListener('load', cardFaceLoaded, true)"))
  assert(!source.includes("window.addEventListener('load', cardFaceLoaded"),'image load does not propagate from Document to Window')
})
await test('retired or old-generation image callbacks cannot install a cached public face',async()=>{
  const f=fixture();try{
    await f.advance();const image=f.target.querySelector('img')
    f.state.cardFaceLoaded({target:image});await f.sync();await f.advance()
    f.apply([leave(2,publicCard('target',{hidden:true}))],{remove:['target']});await f.advance()
    f.state.cardFaceLoaded({target:image});await f.advance()
    assert.equal(f.state.fieldSnapshots.has('target'),false);assert.equal(f.host.children.length,0)
  }finally{f.dispose()}
})
await test('a delayed public image cannot refresh a now-concealed but still mounted instance',async()=>{
  const f=fixture();try{
    await f.advance();const image=f.target.querySelector('img')
    f.apply([combat(1,undefined,[publicCard('unit'),publicCard('target',{hidden:true})])])
    assert.equal(f.live.get('target'),f.target,'DOM retirement has not happened yet')
    assert.equal(f.state.concealedInstances.has('target'),true)
    f.state.cardFaceLoaded({target:image});await f.advance()
    assert.equal(f.state.fieldSnapshots.has('target'),false,'pre-paint old public pixels cannot reenter the concealed cache')
    await f.settle()
  }finally{f.dispose()}
})
await test('later concealed authority retires a previously prepared public death face',async()=>{
  const f=fixture({paused:true});try{await f.advance();f.apply([leave(1)],{remove:['target']});await f.advance()
    assert.equal(f.host.children.length,1)
    f.apply([leave(2,publicCard('target',{hidden:true}))]);await f.advance();assert.equal(f.host.children.length,0)
    await f.pause(false);assert.equal(f.animations.length,0)
  }finally{f.dispose()}
})
for(const mode of ['fast','off']) await test(`explicit preference bounds hit/death without off-mode tilt/${mode}`,async()=>{
  const f=fixture({mode});try{await f.advance();f.apply([leave(1),combat(2)],{remove:['target']});await f.advance();await f.settle()
    const hit=f.animations.find(item=>item.target===f.unit),death=f.animations.find(item=>item.target.className==='l12-combat-defeat-ghost')
    assert(hit&&death);assert(hit.options.duration<280);assert(death.options.duration<920)
    if(mode==='off')for(const animation of [hit,death])assert(animation.frames.every(frame=>!/perspective|rotateX/.test(frame.transform)))
  }finally{f.dispose()}
})
await test('unmount disposes all animation owners and ghosts',async()=>{
  const f=fixture();await f.advance();f.apply([leave(1)],{remove:['target']});await f.advance();f.dispose();await f.advance()
  assert.equal(f.host.children.length,0);assert.equal(f.active().length,0);assert.equal(f.props.sequenceCoordinator.ownsVisualKey('target'),false)
})
console.log(JSON.stringify({suite:'actual CombatMotion functions + original coordinator (deterministic DOM adapter)',total:cases.length,
  passed:cases.filter(item=>item.passed).length,failed:cases.filter(item=>!item.passed).length,cases},null,2))
if(cases.some(item=>!item.passed))process.exitCode=1
