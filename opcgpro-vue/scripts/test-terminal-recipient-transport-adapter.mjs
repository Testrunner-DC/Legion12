import assert from 'node:assert/strict'
import {createTerminalRecipientTransportAdapter} from './terminal-recipient-transport-adapter.mjs'

let cases=0
function setup({nativeThrows=false,buffer=false}={}) {
  let time=10;const sent=[],calls=[],nonce='3'.repeat(32)
  const native=function(raw,...args){sent.push({thisValue:this,raw,args});if(nativeThrows)throw new Error('ORIGINAL_SEND_FAILED');return 17}
  const clients=[0,1].map(index=>({numericRoomSlot:0,numericClientSlot:index,playerIndex:index,
    envelope:{state:{matchId:'synthetic',revision:4,phase:'Main',you:index}},socket:{send:native},
    send(value){const raw=JSON.stringify(value);return this.socket.send(buffer?Buffer.from(raw):raw,'original-callback')}}))
  const room={host:clients[0],guest:clients[1],expectedMatchId:'synthetic'}
  const original={observeParsed:(...args)=>{calls.push(['parsed',...args]);return 'old-token'},
    afterDispatch:(...args)=>calls.push(['dispatch',...args]),recordLoopDrift:value=>calls.push(['drift',value]),
    report:(...args)=>({original:true,args})}
  const adapter=createTerminalRecipientTransportAdapter({nonce,originalMarkers:original,expectedCommands:1,now:()=>time})
  return {adapter,room,clients,native,sent,calls,setTime:value=>{time=value}}
}
function begin(test) {
  assert.equal(test.adapter.bindRoom(test.room),true)
  const requestId=test.adapter.markers.tryBegin({stage:'cleanup',client:test.clients[0],commandType:'surrender',roomSlot:0,sentAt:10})
  assert.equal(typeof requestId,'string');return requestId
}
function deliver(test,requestId,offset=0) {
  for(const [index,client] of test.clients.entries()) {
    const message={type:'gameState',requestId:index?undefined:requestId,state:{matchId:'synthetic',phase:'GameOver',you:index,revision:5}}
    const before=structuredClone(message)
    test.setTime(30+offset+index)
    const token=test.adapter.markers.observeParsed(client,{message,callbackAt:20+offset+index,parseStartedAt:21+offset+index,parsedAt:22+offset+index,bytes:200000})
    test.adapter.markers.afterDispatch(token,24+offset+index)
    assert.deepEqual(message,before)
  }
}
for(const buffer of [false,true]) {
  const test=setup({buffer}),id=begin(test)
  assert.equal(test.clients[0].send({type:'command',requestId:id,command:{type:'surrender'}}),17)
  deliver(test,id)
  const report=test.adapter.diagnosticReport()
  assert.equal(report.trace_complete,1);assert.equal(report.rows.length,2);assert.equal(report.original_3s_satisfied,1)
  assert.equal(report.online_capacity_accepted,0);assert.equal(report.adapter_fault_count,0)
  assert.equal(test.sent.length,3);assert.ok(test.sent.every(row=>row.args[0]==='original-callback'))
  assert.equal(test.sent[0].thisValue,test.clients[0].socket)
  assert.equal(test.calls.filter(row=>row[0]==='parsed').length,2)
  assert.deepEqual(test.calls.filter(row=>row[0]==='dispatch').map(row=>row.slice(1)),[['old-token',24],['old-token',25]])
  test.adapter.markers.recordLoopDrift(5);assert.deepEqual(test.calls.at(-1),['drift',5])
  assert.deepEqual(test.adapter.markers.report('old'),{original:true,args:['old']})
  assert.equal(JSON.stringify(report).includes(id),false)
  assert.deepEqual(test.adapter.retire(),{restored:2,drifted:0,originalGameCommandsCancelled:0})
  assert.equal(test.clients[0].socket.send,test.native);cases++
}
{
  const test=setup(),id=begin(test);deliver(test,id)
  const report=test.adapter.diagnosticReport();assert.equal(report.trace_complete,0)
  assert.ok(report.rows.every(row=>row.native_send_valid===0));test.adapter.retire();cases++
}
{
  const test=setup(),id=begin(test);test.clients[0].send({requestId:id});deliver(test,id,3100)
  const report=test.adapter.diagnosticReport();assert.equal(report.trace_complete,1);assert.equal(report.original_3s_satisfied,0)
  assert.ok(report.rows.every(row=>row.exceeded_original_3s===1));test.adapter.retire();cases++
}
{
  const test=setup({nativeThrows:true}),id=begin(test)
  assert.throws(()=>test.clients[0].send({requestId:id}),/ORIGINAL_SEND_FAILED/)
  assert.equal(test.sent.length,1);test.adapter.retire();cases++
}
{
  const test=setup();assert.equal(test.adapter.bindRoom(test.room),true)
  assert.equal(test.adapter.markers.tryBegin({stage:'rooms-30',client:test.clients[0],commandType:'endTurn'}),null)
  assert.equal(test.clients[0].send({type:'ping'}),17)
  assert.equal(test.adapter.diagnosticReport().begun_commands,0);test.adapter.retire();cases++
}
{
  const test=setup();assert.equal(test.adapter.bindRoom(test.room),true)
  test.clients[0].socket.send=()=>99
  assert.equal(test.adapter.markers.tryBegin({stage:'cleanup',client:test.clients[0],commandType:'surrender',roomSlot:0,sentAt:10}),null)
  assert.equal(test.adapter.diagnosticReport().trace_complete,0)
  assert.ok(test.adapter.diagnosticReport().adapter_faults.includes('RECIPIENT_NATIVE_BINDING_DRIFT'))
  assert.deepEqual(test.adapter.retire(),{restored:1,drifted:1,originalGameCommandsCancelled:0});cases++
}
{
  const test=setup();test.clients[0].socket=null
  assert.equal(test.adapter.bindRoom(test.room),false);assert.equal(test.adapter.diagnosticReport().trace_complete,0);cases++
}
{
  const test=setup();assert.equal(test.adapter.bindRoom(test.room),true);assert.equal(test.adapter.bindRoom(test.room),false)
  assert.equal(test.adapter.diagnosticReport().trace_complete,0);test.adapter.retire();cases++
}
console.log(JSON.stringify({status:'passed',cases,realTerminalRecipientMarkers:true,nativeBoundary:'mock sockets, original methods and arguments preserved',networkCalls:0,capacityClaim:false}))
