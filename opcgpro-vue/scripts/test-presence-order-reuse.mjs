import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'

const source=fs.readFileSync(new URL('../src/l12/net.ts',import.meta.url),'utf8')
const ast=ts.createSourceFile('net.ts',source,ts.ScriptTarget.Latest,true)
const names=['validPresenceItem','validPresenceIdentity','rebuildPresenceDelta']
const functions=ast.statements.filter(node=>ts.isFunctionDeclaration(node)&&names.includes(node.name?.text))
assert.equal(functions.length,names.length)
const body=functions.map(node=>node.getText(ast)).join('\n')+'\nglobalThis.rebuild=rebuildPresenceDelta;'
const executable=ts.transpileModule(body,{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText
function item(index){return {accountId:index.toString(16).padStart(32,'0'),username:'synthetic-'+index,
 online:true,activity:'idle',roomCode:null,canInvite:true,canSpectate:false,actionReason:null,friendStatus:'none',friendDirection:'none'}}
const fixture=()=>Array.from({length:12},(_,index)=>item(index))
function consumer({base=fixture(),reuse=true,delta=true,revision=10,epoch='presence-order-test',pending=false}={}) {
 const l12State={presence:base,resourceEpoch:epoch,resourceRevisions:{presence:revision}}
 const context=vm.createContext({l12State,negotiatedPresenceDelta:delta,negotiatedPresenceOrderReuse:reuse,
  presenceRecoveryPending:pending,presenceBaselineEpoch:epoch})
 vm.runInContext(executable,context)
 return {rebuild:context.rebuild,l12State}
}
const message=()=>({type:'presenceDelta',resource:'presence',epoch:'presence-order-test',
 baseRevision:10,revision:11,removedAccountIds:[],upserts:[{...item(3),activity:'inRoom'}],orderFromBaseline:true})
const plain=value=>JSON.parse(JSON.stringify(value))
let cases=0
{
 const c=consumer(),before=plain(c.l12State.presence),rebuilt=c.rebuild(message())
 assert.notEqual(rebuilt,null,'negotiated unchanged order must rebuild without resending all ids')
 assert.deepEqual(plain(rebuilt.items),before.map((row,index)=>index===3?{...row,activity:'inRoom'}:row))
 assert.deepEqual(plain(c.l12State.presence),before,'rebuild may not mutate the old baseline');cases++
}
{
 const c=consumer({reuse:false}),value=message();delete value.orderFromBaseline
 value.order=fixture().map(row=>row.accountId).toReversed()
 const result=c.rebuild(value)
 assert.notEqual(result,null)
 assert.deepEqual(plain(result.items).map(row=>row.accountId),value.order);cases++
}
for(const [name,mutate,options]of[
 ['unnegotiated reuse',()=>{},{reuse:false}],
 ['unnegotiated delta',()=>{},{delta:false}],
 ['recovery outstanding',()=>{},{pending:true}],
 ['both representations',value=>{value.order=fixture().map(row=>row.accountId)}],
 ['false flag',value=>{value.orderFromBaseline=false}],
 ['string flag',value=>{value.orderFromBaseline='true'}],
 ['null flag',value=>{value.orderFromBaseline=null}],
 ['missing order',value=>{delete value.orderFromBaseline}],
 ['inherited flag is not an explicit representation',value=>{delete value.orderFromBaseline;Object.setPrototypeOf(value,{orderFromBaseline:true})}],
 ['duplicate removals',value=>{value.removedAccountIds=[item(1).accountId,item(1).accountId]}],
 ['unknown removals',value=>{value.removedAccountIds=[item(99).accountId]}],
 ['new identity cannot reuse order',value=>{value.upserts.push(item(99))}],
 ['duplicate upserts',value=>{value.upserts.push({...value.upserts[0]})}],
 ['incomplete entry',value=>{delete value.upserts[0].username}],
 ['wrong base revision',value=>{value.baseRevision=9}],
 ['non-integer revision',value=>{value.revision=11.5}],
 ['no advance',value=>{value.revision=10}],
 ['different epoch',value=>{value.epoch='other-epoch'}],
 ['wrong resource',value=>{value.resource='friends'}],
 ['duplicate baseline',()=>{},{base:[item(1),item(1)]}],
]) {
 const c=consumer(options),before=plain(c.l12State),value=message();mutate(value)
 assert.equal(c.rebuild(value),null,name)
 assert.deepEqual(plain(c.l12State),before,'rejected frame must not mutate the published baseline: '+name);cases++
}
{
 const c=consumer(),before=plain(c.l12State.presence),value=message()
 value.removedAccountIds=[item(1).accountId,item(4).accountId]
 const rebuilt=c.rebuild(value)
 assert.deepEqual(plain(rebuilt.items),before.filter(row=>![item(1).accountId,item(4).accountId].includes(row.accountId))
  .map(row=>row.accountId===item(3).accountId?{...row,activity:'inRoom'}:row))
 assert.deepEqual(plain(c.l12State.presence),before);cases++
}
{
 const c=consumer(),value=message()
 value.upserts=fixture().map(row=>({...row,activity:'playing',actionReason:'合成 <>& 卡面说明  '}))
 const rebuilt=c.rebuild(value)
 assert.deepEqual(plain(rebuilt.items),value.upserts,'all values, Unicode and safe characters survive');cases++
}
assert.match(source,/presenceOrderReuseV1: true/)
assert.match(source,/negotiatedPresenceOrderReuse = negotiatedPresenceDelta && message\.capabilities\?\.presenceOrderReuseV1 === true/)
assert.equal((source.match(/negotiatedPresenceOrderReuse = false/g)??[]).length,3,
 'initial state, socket close and explicit disconnect must all retire negotiation');cases++
let actualServerPackets=0
if(process.argv[2]) {
 const fixtures=JSON.parse(fs.readFileSync(process.argv[2]))
 assert.equal(fixtures.syntheticOnly,true);assert.match(fixtures.serverDllSha256,/^[A-F0-9]{64}$/)
 assert.equal(fixtures.rows.length,60)
 for(const row of fixtures.rows) {
  const decode=field=>JSON.parse(Buffer.from(row[field],'base64').toString('utf8'))
  const before=decode('before'),wire=decode('wire'),after=decode('after')
  const c=consumer({base:before.items,revision:before.revision,epoch:before.epoch}),rebuilt=c.rebuild(wire)
  assert.notEqual(rebuilt,null,'actual C# codec packet must restore')
  assert.deepEqual(plain(rebuilt.items),after.items);assert.equal(rebuilt.revision,after.revision)
  actualServerPackets++;cases++
 }
}
console.log(JSON.stringify({status:'passed',cases,actualServerPackets,realNetFunctions:true,networkCalls:0}))
