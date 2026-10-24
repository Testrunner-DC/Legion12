import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import ts from 'typescript'
import {createTutorialWireObserver} from './tutorial-wire-observer.mjs'

const observer=await createTutorialWireObserver()
const names=JSON.parse(fs.readFileSync(new URL('../src/l12/gameStatePropertyNamesV1.json',import.meta.url),'utf8'))
const encode=value=>Array.isArray(value)?value.map(encode):value&&typeof value==='object'
  ?Object.fromEntries(Object.entries(value).map(([name,item])=>[
    names.includes(name)?'@'+names.indexOf(name).toString(36):/^[~@]/.test(name)?'~'+name:name,encode(item)])):value
const plain={type:'gameState',requestId:'synthetic-ack',tutorial:{lessonId:'response',status:'active'},
  state:{matchId:'synthetic-only',revision:12,you:0,players:[],recentEvents:[],lastAction:null}}
const frame=value=>({type:'gameStatePropertyFrameV1',version:1,matchId:'synthetic-only',payload:encode(value)})
const card={type:'gameStateCardFrameV1',payload:plain,cardSnapshots:[],eventCardRefs:[],lastActionCardRefs:null}
const delta={type:'gameStateDelta',matchId:'synthetic-only',revision:13,baseRevision:12,
  changes:[{path:['requestId'],value:'synthetic-delta-ack'}]}
let cases=0
for(const [value,expected] of [[plain,plain],[delta,delta],[card,plain],[frame(plain),plain],[frame(delta),delta],[frame(card),plain]]){
  const actual=observer.decode(JSON.stringify(value))
  assert.deepEqual(actual.value,expected)
  cases++
}
const session={type:'session',capabilities:{gameStatePropertyFrameV1:true}}
assert.deepEqual(observer.decode(JSON.stringify(session)).value,session);cases++
for(const value of [frame({...plain,state:{...plain.state,matchId:'wrong'}}),{...frame(plain),version:2},
  {type:'gameStateCardFrameV1',payload:plain},'{invalid']){
  assert.throws(()=>observer.decode(typeof value==='string'?value:JSON.stringify(value)));cases++
}
for(const name of ['test-tutorial-ui.mjs','qa-tutorial-candidate-images-20261023.mjs']){
const entry=fs.readFileSync(new URL('./'+name,import.meta.url),'utf8')
const ast=ts.createSourceFile(name,entry,ts.ScriptTarget.Latest,true,ts.ScriptKind.JS)
const callbacks=[]
function visit(node){
  if(ts.isCallExpression(node)&&ts.isPropertyAccessExpression(node.expression)&&node.expression.name.text==='on'
    &&ts.isStringLiteral(node.arguments[0])&&node.arguments[0].text==='framereceived')callbacks.push(node.arguments[1])
  ts.forEachChild(node,visit)
}
visit(ast);assert.equal(callbacks.length,1)
const report={transport:[],errors:[]}
const callback=new Function('report','wireObserver','return ('+callbacks[0].getText(ast)+')')(report,observer)
callback({payload:JSON.stringify(frame(plain))})
assert.equal(report.transport.length,1,'Actual tutorial collector must retain encoded game ACKs')
assert.equal(report.transport[0].requestId,plain.requestId);assert.equal(report.transport[0].revision,12);cases++
callback({payload:JSON.stringify(frame(delta))})
assert.equal(report.transport.at(-1).requestId,'synthetic-delta-ack');assert.equal(report.transport.at(-1).revision,13);cases++
const receipts=report.transport.length
callback({payload:JSON.stringify({...frame(plain),version:0})})
assert.equal(report.transport.length,receipts);assert.equal(report.errors.length,1);cases++
// Execute the actual startup prefix with an inert filesystem. A child initializer
// cannot mutate this fake Node parent; the entry itself must set all three vars.
const start=entry.indexOf('const repo ='),end=entry.indexOf('const report =',start)
assert.ok(start>0&&end>start)
const prefix=entry.slice(start,end).replaceAll('import.meta.dirname',JSON.stringify(import.meta.dirname))
// These paths are consumed only by the inert filesystem below. Use this host's
// absolute spelling instead of feeding a Windows drive string to POSIX path.
const syntheticRoot=path.resolve(import.meta.dirname,'synthetic-only')
for(const rejected of [false,true]){
  const parent={env:{L12_TUTORIAL_UI_OUTPUT:path.join(syntheticRoot,'evidence'),
    L12_TUTORIAL_UI_CACHE:path.join(syntheticRoot,'cache'),TEMP:'C:/must-not-write',TMP:'C:/must-not-write',TMPDIR:'C:/must-not-write'}}
  const created=[]
  const run=()=>new Function('fs','path','process','execFileSync','assert',prefix)(
    {existsSync:()=>false,mkdirSync:where=>{assert.equal(parent.env.TEMP,parent.env.L12_TUTORIAL_UI_CACHE);created.push(where)}},
    path,parent,()=>{if(rejected)throw new Error('PHYSICAL_ROOT_REJECTED')},assert)
  if(rejected){assert.throws(run,/PHYSICAL_ROOT_REJECTED/);assert.deepEqual(created,[]);assert.equal(parent.env.TEMP,'C:/must-not-write')}
  else{run();assert.equal(created.length,2);for(const key of ['TEMP','TMP','TMPDIR'])assert.equal(parent.env[key],parent.env.L12_TUTORIAL_UI_CACHE)}
  cases++
}
}
const deployed=fs.readFileSync(new URL('./test-tutorial-deployed.mjs',import.meta.url),'utf8')
const deployedAst=ts.createSourceFile('test-tutorial-deployed.mjs',deployed,ts.ScriptTarget.Latest,true,ts.ScriptKind.JS)
const receivers=deployedAst.statements.filter(node=>ts.isFunctionDeclaration(node)&&node.name?.text==='receive')
assert.equal(receivers.length,1)
const report={lessonsObserved:[]}
const receiver=new Function('report','wireObserver',`let failureCode='',ownedRoom=false;${receivers[0].getText(deployedAst)};
  return {receive,state:()=>({failureCode,ownedRoom})}`)(report,observer)
receiver.receive(JSON.stringify({type:'session',accountId:'synthetic-only',sessionId:'synthetic-only',tutorialAvailable:true,capabilities:{requestIds:true}}))
assert.deepEqual(report.session,{ordinaryIdentityBound:true,tutorialAvailable:true,requestIds:true});cases++
const completed={...plain,tutorial:{lessonId:'deploy',version:1,status:'completed'}}
receiver.receive(JSON.stringify(frame(completed)))
assert.equal(report.authoritativeDeployCompleted,true);assert.equal(report.lessonsObserved.length,1);cases++
receiver.receive(JSON.stringify(frame(completed)))
assert.equal(report.lessonsObserved.length,1,'A repeated received fact must not invent a second completion');cases++
receiver.receive(JSON.stringify({type:'roomState',started:true,tutorial:{lessonId:'move',status:'active'}}))
assert.equal(receiver.state().ownedRoom,true);assert.equal(report.authoritativeNextRoom,true);cases++
receiver.receive(JSON.stringify({type:'roomLeft'}))
assert.equal(receiver.state().ownedRoom,false);assert.equal(report.roomLeftAcknowledgements,1);cases++
receiver.receive(JSON.stringify({...frame(completed),version:0}))
assert.equal(receiver.state().failureCode,'wire-observation-rejected');cases++
console.log(JSON.stringify({status:'passed',cases,actualTutorialCollectors:3,actualShippedDecoders:true,networkCalls:0,filesWritten:0,applicationStarts:0}))
