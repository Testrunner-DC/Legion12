import assert from 'node:assert/strict'
import {projectLog} from '../src/l12/game/logViewModel.ts'
import {replayGameAt} from '../src/l12/replayModel.ts'

let checks=0
const test=(name,run)=>{run();checks++;console.log('PASS library return: '+name)}
const card=(id,hidden=false)=>({instanceId:id,cardId:'SYN-'+id,name:hidden?'':'合成'+id,
 cardType:'legion',faction:'sun',cost:2,baseTroops:3000,troops:3000,hidden})
const fact=(extra={})=>({operationId:'library-return:0:100',libraryOwner:0,
 topCount:1,bottomCount:0,shuffledCount:0,identitiesPublic:false,...extra})
const event=(sequence=100,extra={})=>({sequence,type:'return',playerIndex:0,text:'不得从这句旧文案推断位置',cards:[],playerLibraryReturn:fact(),...extra})
const rows=input=>projectLog(input,0,[])
const text=output=>JSON.stringify(output.map(row=>row.kind==='line'
 ? {parts:row.parts.map(part=>part.text),badges:row.badges.map(badge=>badge.value)}:row))

test('new private return displays known top without inventing a card',()=>{
 const out=rows([event()]);assert.equal(out.length,1)
 assert.match(text(out),/返回牌库顶部/);assert.match(text(out),/1张/)
 assert.doesNotMatch(text(out),/隐藏卡牌|合成|SYN-/)
})
for(const owner of [0,1])for(const destination of ['top','bottom','shuffled'])test(`owner ${owner}, ${destination}`,()=>{
 const input=event(105,{playerIndex:1-owner,playerLibraryReturn:fact({operationId:`library-return:${owner}:105`,libraryOwner:owner,
 topCount:destination==='top'?1:0,bottomCount:destination==='bottom'?1:0,shuffledCount:destination==='shuffled'?1:0})})
 const out=rows([input]);assert.equal(out.length,1)
 assert.match(text(out),destination==='top'?/返回牌库顶部/:destination==='bottom'?/返回牌库底部/:/返回牌库并重洗/)
 assert.equal(out[0].actor,owner===0?'我方':'对方','library ownership, not effect actor, identifies destination')
})
test('three individual public moves become one log without changing their events',()=>{
 const input=[0,1,2].map(index=>event(100+index,{cards:[card('c'+index)],playerLibraryReturn:fact({identitiesPublic:true})}))
 const before=JSON.stringify(input),out=rows(input)
 assert.equal(out.length,1);assert.match(text(out),/3张/)
 for(const name of ['合成c0','合成c1','合成c2'])assert.match(text(out),new RegExp(name))
 assert.equal(JSON.stringify(input),before);assert.equal(input.length,3)
})
test('same-name distinct cards remain distinct',()=>{
 const input=[card('a'),{...card('b'),name:'合成a'}].map((value,index)=>event(100+index,{cards:[value],playerLibraryReturn:fact({identitiesPublic:true})}))
 const out=rows(input);assert.equal(out.length,1);assert.match(text(out),/2张/)
 const ids=out[0].parts.filter(part=>part.card).map(part=>part.card.instanceId)
 assert.deepEqual(ids,['a','b'])
})
test('private owner sees only the cards already present in their recipient projection',()=>{
 const input=[event(100,{cards:[card('own')]}),event(101,{cards:[card('covered',true)]})]
 const out=rows(input);assert.match(text(out),/合成own/);assert.doesNotMatch(text(out),/covered|SYN-|隐藏卡牌/)
})
test('independent cost/effect operations never merge',()=>{
 const out=rows([event(),event(101,{playerLibraryReturn:fact({operationId:'library-return:0:101'})})])
 assert.equal(out.length,2)
})
test('a cost summary or effect cannot masquerade as another successful movement',()=>{
 for(const type of ['cost','effect','search','discard']){
  const out=rows([event(100,{type,text:'支付费用',cards:[card('cost')]}),event(101,{cards:[card('returned')]})])
  assert.equal(out.length,2,type)
  const returned=out.filter(row=>/返回牌库顶部/.test(text([row])))
  assert.equal(returned.length,1);assert.match(text(returned),/1张/)
  assert.doesNotMatch(text(returned),/合成cost/)
 }
})
test('different effect actors never merge even under a contradictory operation',()=>{
 assert.equal(rows([event(),event(101,{playerIndex:1})]).length,2)
})
test('a broad effect group cannot consume a return from a different operation',()=>{
 const play={sequence:99,type:'play',playerIndex:0,text:'打出',cards:[card('source')],playerLogGroupId:'effect:x'}
 const returned=event(100,{playerLogGroupId:'effect:x',cards:[card('returned')]})
 const out=rows([play,returned]);assert.equal(out.length,2)
 assert.doesNotMatch(text(out),/返回牌库顶部|返回牌库底部/)
 assert.doesNotMatch(text([out[0]]),/合成returned/)
})
test('one reorder preserves both public totals without exposing private order',()=>{
 const out=rows([event(100,{type:'reorder',playerLibraryReturn:fact({topCount:2,bottomCount:3})})])
 assert.equal(out.length,1);assert.match(text(out),/顶部 2张/);assert.match(text(out),/底部 3张/)
 assert.doesNotMatch(text(out),/不得从这句/)
})
test('same-sequence retransmission counts exactly once',()=>{
 const a=event(),out=rows([a,structuredClone(a),event(101)])
 assert.equal(out.length,1);assert.match(text(out),/2张/)
})
test('reconnect adds one consistent structured fact to a legacy copy',()=>{
 const a=event(),old={...a};delete old.playerLibraryReturn
 const out=rows([old,a]);assert.equal(out.length,1);assert.match(text(out),/返回牌库顶部/)
})
test('conflicting same-sequence directions never pick an arbitrary result',()=>{
 const a=event(),b=event(100,{playerLibraryReturn:fact({topCount:0,bottomCount:1})})
 for(const input of [[a,b],[b,a]])assert.doesNotMatch(text(rows(input)),/牌库顶部|牌库底部/)
})
test('malformed members poison the complete operation, not just their own row',()=>{
 const a=event(),bad=event(101,{playerLibraryReturn:fact({topCount:'1'})})
 for(const input of [[a,bad],[bad,a]]){
  const out=rows(input);assert.equal(out.length,1)
  assert.doesNotMatch(text(out),/返回牌库顶部|返回牌库底部|返回牌库并重洗/)
 }
 for(const input of [[a,{...bad,sequence:100}],[{...bad,sequence:100},a]])
  assert.doesNotMatch(text(rows(input)),/返回牌库顶部|返回牌库底部|返回牌库并重洗/)
})
for(const [name,invalid] of [
 ['unknown operation',fact({operationId:'effect:secret'})],
 ['operation owner mismatch',fact({operationId:'library-return:1:100'})],
 ['unsafe sequence',fact({operationId:'library-return:0:9007199254740993'})],
 ['invalid owner',fact({libraryOwner:2})],
 ['negative count',fact({topCount:-1})],
 ['string count',fact({topCount:'1'})],
 ['fractional count',fact({topCount:1.5})],
 ['overflow count',fact({topCount:2147483648})],
 ['all zero',fact({topCount:0})],
 ['mixed shuffle and placed',fact({shuffledCount:1})],
 ['string visibility',fact({identitiesPublic:'false'})],
 ['missing count',((value)=>{delete value.bottomCount;return value})(fact())],
])test('reject '+name,()=>{
 const out=rows([event(100,{type:'library',text:'秘密文字牌库底部',playerLibraryReturn:invalid})])
 assert.doesNotMatch(text(out),/秘密|返回牌库顶部|返回牌库底部|返回牌库并重洗/)
})
test('raw private events cannot become a new public row',()=>{
 assert.equal(rows([event(100,{type:'private-return',cards:[card('private-secret')]})]).length,0)
})
test('legacy neutral return does not guess a direction',()=>{
 const input=event();delete input.playerLibraryReturn
 const out=rows([input]);assert.doesNotMatch(text(out),/牌库顶部|牌库底部/)
})
test('library legacy missing direction no longer defaults to top',()=>{
 const input=event(100,{type:'library',text:'旧记录返回牌库'});delete input.playerLibraryReturn
 assert.doesNotMatch(text(rows([input])),/牌库顶部|牌库底部/)
})
test('legacy reorder does not invent missing top or bottom counts',()=>{
 for(const phrase of ['旧记录整理牌库','2张牌放回牌库顶部']){
  const input=event(100,{type:'reorder',text:phrase});delete input.playerLibraryReturn
  const out=rows([input]);assert.doesNotMatch(text(out),/底部 0张|顶部 0张/)
  assert.equal(out[0].badges.length,phrase.startsWith('2')?1:0)
 }
})
for(const camel of [true,false])test(`actual replay normalizes ${camel?'camel':'Pascal'} facts without coercion`,()=>{
 const key=name=>camel?name[0].toLowerCase()+name.slice(1):name
 const rawFact=Object.fromEntries(Object.entries({OperationId:'library-return:0:100',LibraryOwner:0,TopCount:0,BottomCount:2,ShuffledCount:0,IdentitiesPublic:false}).map(([name,value])=>[key(name),value]))
 const rawEvent=Object.fromEntries(Object.entries({Sequence:100,Type:'return',PlayerIndex:0,Text:'不得猜方向',Cards:[],PlayerLibraryReturn:rawFact}).map(([name,value])=>[key(name),value]))
 const input={match:{matchId:'library-synthetic'},viewerPlayerIndex:0,commands:[{revision:1,state:{Events:[rawEvent]}}]}
 const before=JSON.stringify(input),state=replayGameAt(input,0)
 assert.deepEqual(state.recentEvents[0].playerLibraryReturn,fact({topCount:0,bottomCount:2}))
 assert.match(text(rows(state.recentEvents)),/返回牌库底部/)
 assert.equal(JSON.stringify(input),before)
})
test('public grouping does not alter movement animation input',()=>{
 const input=[event(100,{cards:[card('a')],originZone:'graveyard'}),event(101,{cards:[card('b')],originZone:'graveyard'})]
 const before=JSON.stringify(input);rows(input);assert.equal(JSON.stringify(input),before)
 assert.deepEqual(input.map(e=>[e.sequence,e.cards[0].instanceId]),[[100,'a'],[101,'b']])
})
test('only explicitly paired leave is folded, not a nearby or same-card leave',()=>{
 const returned=event(102,{cards:[card('a')],playerLogGroupId:'library-return:0:100'})
 const leave={sequence:100,type:'leave',playerIndex:0,text:'离场',cards:[card('a')],playerLogGroupId:'library-return:0:100'}
 const unrelated={...leave,sequence:101,playerLogGroupId:undefined}
 const input=[leave,unrelated,returned],before=JSON.stringify(input),out=rows(input)
 assert.equal(out.length,2);assert.match(text(out),/返回牌库顶部/);assert.match(text(out),/离场/)
 assert.equal(JSON.stringify(input),before)
 assert.equal(rows([{...leave,playerIndex:1},returned]).length,1,'controller leave and original-owner return share an explicit destination operation')
 assert.equal(rows([{...leave,playerIndex:1},returned,event(103,{playerIndex:1})]).length,3,'conflicting return actors cannot settle which group owns a leave')
 assert.equal(rows([leave]).length,1,'an unmatched leave remains visible')
})
test('a single real leave can carry the receipt without inventing a return event',()=>{
 const input=[event(100,{type:'leave',cards:[card('a')],playerLogGroupId:'library-return:0:100'})]
 const out=rows(input);assert.equal(out.length,1);assert.match(text(out),/返回牌库顶部/)
 assert.equal(input.length,1)
})
test('promotion foundations retain raw movement while new receipt contributes to the owner batch',()=>{
 const input=[event(100,{type:'leave',cards:[card('host')]}),event(101,{type:'promotion-stack-leave',cards:[card('base')]})]
 const before=JSON.stringify(input),out=rows(input);assert.equal(out.length,1)
 assert.match(text(out),/2张/);assert.match(text(out),/合成host/);assert.match(text(out),/合成base/)
 assert.equal(JSON.stringify(input),before)
 const legacy={...input[1]};delete legacy.playerLibraryReturn;assert.equal(rows([legacy]).length,0)
})
test('single host leave and its promotion foundation merge despite their different authority actors',()=>{
 const receipt=fact({operationId:'library-return:1:100',libraryOwner:1,identitiesPublic:true})
 const input=[event(100,{type:'promotion-stack-leave',playerIndex:1,playerLibraryReturn:receipt,cards:[card('foundation')]}),
  event(101,{type:'leave',playerIndex:0,playerLibraryReturn:{...receipt,identitiesPublic:false},cards:[]})]
 const before=JSON.stringify(input),out=rows(input)
 assert.equal(out.length,1);assert.equal(out[0].actor,'对方');assert.match(text(out),/2张/)
 assert.match(text(out),/合成foundation/);assert.equal(JSON.stringify(input),before)
})
test('contradictory shuffled and placed operation does not choose a destination',()=>{
 const out=rows([event(),event(101,{playerLibraryReturn:fact({topCount:0,shuffledCount:1})})])
 assert.equal(out.length,1);assert.doesNotMatch(text(out),/返回牌库顶部|返回牌库并重洗/)
})
console.log(`Library return log/replay: ${checks}/${checks} passed`)
