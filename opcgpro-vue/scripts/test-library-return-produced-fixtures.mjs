import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {createHash} from 'node:crypto'
import {projectLog} from '../src/l12/game/logViewModel.ts'
import {replayGameAt} from '../src/l12/replayModel.ts'

const directory=process.argv[2]
assert.ok(directory&&path.isAbsolute(directory),'explicit synthetic fixture directory required')
const files=fs.readdirSync(directory).filter(name=>name.endsWith('.json')).sort()
assert.ok(files.length>0&&files.length<=120)
const compact=rows=>rows.map(row=>row.kind==='line'?{sequence:row.sequence,actor:row.actor,
 parts:row.parts.map(part=>({text:part.text,id:part.card?.instanceId})),badges:row.badges}:row)
const results=[]
for(const file of files){
 const raw=fs.readFileSync(path.join(directory,file));assert.ok(raw.length<=4*1024*1024)
 const fixture=JSON.parse(raw);assert.equal(fixture.synthetic,true)
 for(const [view,you,neutral] of [['player0',0,false],['player1',1,false],['spectator',0,true],['referee',0,true]]){
  const events=fixture[view],before=JSON.stringify(events);assert.ok(Array.isArray(events))
  const operations=new Map()
  for(const event of events){
   const fact=event.playerLibraryReturn;if(!fact)continue
   const group=operations.get(fact.operationId)??[];group.push(event);operations.set(fact.operationId,group)
  }
  const rows=projectLog(events,you,[],neutral)
  for(const [operation,members] of operations){
   const first=Math.min(...members.map(event=>event.sequence))
   const row=rows.find(row=>row.sequence===first)
   assert.equal(row?.kind,'line',`${file}/${view}/${operation}: one operation needs a line`)
   const receipt=members[0].playerLibraryReturn
   const totals=members.reduce((out,event)=>{for(const key of Object.keys(out))out[key]+=event.playerLibraryReturn[key];return out},{topCount:0,bottomCount:0,shuffledCount:0})
   const total=Object.values(totals).reduce((a,b)=>a+b,0)
   const prose=row.parts.map(part=>part.text).join('')
   assert.match(prose,new RegExp(`（${total}张）`),`${file}/${view}: complete successful count`)
   const wanted=totals.shuffledCount?'返回牌库并重洗':totals.topCount&&totals.bottomCount?'返回牌库':totals.topCount?'返回牌库顶部':'返回牌库底部'
   assert.ok(prose.includes(wanted),`${file}/${view}: destination`)
   assert.equal(row.actor,neutral?(receipt.libraryOwner===you?'下方':'上方'):(receipt.libraryOwner===you?'我方':'对方'))
   const visible=new Set(members.flatMap(event=>(event.cards??[]).filter(card=>!card.hidden&&card.name).map(card=>card.instanceId)))
   assert.deepEqual(new Set(row.parts.filter(part=>part.card).map(part=>part.card.instanceId)),visible,`${file}/${view}: only this recipient's receipt cards`)
   const memberSequences=new Set(members.map(event=>event.sequence))
   assert.equal(rows.filter(row=>memberSequences.has(row.sequence)).length,1,`${file}/${view}: no duplicate per-card log`)
  }
  const replay=replayGameAt({match:{matchId:'synthetic-return-consumer'},viewerPlayerIndex:you,commands:[{revision:1,state:{Events:events}}]},0)
  assert.deepEqual(compact(projectLog(replay.recentEvents,you,[],neutral)),compact(rows),`${file}/${view}: actual replay parity`)
  assert.equal(JSON.stringify(events),before,`${file}/${view}: raw movements unchanged`)
  results.push({file,view,operations:operations.size,eventCount:events.length,fixtureSha256:createHash('sha256').update(raw).digest('hex')})
 }
}
console.log(JSON.stringify({status:'passed',fixtureDirectory:directory,files:files.length,cases:results.length,
 syntheticOnly:true,actualEngineRecipientProjections:true,actualLogAndReplayConsumers:true,
 networkCalls:0,productionWrites:0,results},null,2))
