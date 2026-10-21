import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
const source=fs.readFileSync(new URL('../src/l12/handArrayDelta.ts',import.meta.url),'utf8')
const compiled=ts.transpileModule(source,{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS}}).outputText
const exports={};new Function('exports',compiled)(exports)
const restore=exports.restoreHandSplices
const card=id=>({instanceId:id,cardId:'same-name',effectText:'完整冻结文本',attachedCards:[{instanceId:`attached-${id}`,unknownField:true}]})
const frame=(revision,ids)=>({type:'gameState',state:{matchId:'synthetic',revision,stateHash:`hash-${revision}`,
  players:[{playerIndex:0,hand:ids.map(card)},{playerIndex:1,hand:[]}]}})
const clone=value=>structuredClone(value)
let cases=0
for(const [ids,prefix,remove,insert] of [[['a','b'],2,0,['c']],[['a','b','c'],1,1,[]],[['a','b','c'],1,1,['d','e']],[[],0,0,['a']],[['a'],0,1,[]]]) {
  const baseline=frame(1,ids),rebuilt=frame(2,ids),original=JSON.stringify(baseline)
  const result=[...ids.slice(0,prefix),...insert,...ids.slice(prefix+remove)]
  const wire={baseStateHash:'hash-1',resultStateHash:'hash-2',edits:[{playerIndex:0,baseIds:ids,prefix,remove,insert:insert.map(card),resultIds:result}]}
  assert.equal(restore(baseline,rebuilt,wire,[]),true)
  assert.deepEqual(rebuilt.state.players[0].hand,result.map(card));assert.equal(JSON.stringify(baseline),original)
  if(rebuilt.state.players[0].hand.length)rebuilt.state.players[0].hand[0].effectText='consumer mutation'
  assert.equal(JSON.stringify(baseline),original);cases++
}
const baseline=frame(1,['a','b']),wire={baseStateHash:'hash-1',resultStateHash:'hash-2',edits:[{playerIndex:0,baseIds:['a','b'],prefix:1,remove:0,insert:[card('c')],resultIds:['a','c','b']}]}
const faults=[w=>w.baseStateHash='wrong',w=>w.resultStateHash='wrong',w=>w.edits.push(clone(w.edits[0])),w=>w.edits[0].playerIndex=2,
  w=>w.edits[0].baseIds.reverse(),w=>w.edits[0].resultIds.reverse(),w=>w.edits[0].prefix=-1,w=>w.edits[0].prefix=3,
  w=>w.edits[0].prefix='1',w=>w.edits[0].remove=1.5,w=>w.edits[0].remove=2,w=>w.edits[0].insert[0].instanceId='a',
  w=>w.edits[0].insert[0].instanceId='',w=>w.edits[0].insert=null,w=>w.edits=[],w=>w.extra=true]
for(const fault of faults){const bad=clone(wire),rebuilt=frame(2,['a','b']),before=JSON.stringify(rebuilt);fault(bad)
  assert.equal(restore(baseline,rebuilt,bad,[]),false);assert.equal(JSON.stringify(rebuilt),before);cases++}
for(const path of [['state'],['state','players'],['state','players',0],['state','players',0,'hand'],['state','players',0,'hand',0,'name'],['state','players','0','hand',0,'name']]){
  assert.equal(restore(baseline,frame(2,['a','b']),clone(wire),[{path,value:[]}]),false);cases++}
assert.equal(restore(baseline,frame(2,['a','b']),clone(wire),[{path:['state','players',0,'morale'],value:[]}]),true);cases++
const next=frame(2,['a','c','b']);assert.equal(restore(next,frame(2,['a','c','b']),wire,[]),false);cases++
const netSource=fs.readFileSync(new URL('../src/l12/net.ts',import.meta.url),'utf8')
const start=netSource.indexOf('function rebuildDeltaGameState('),end=netSource.indexOf('function clearReconnectTimer(',start)
assert(start>=0&&end>start)
const netBody=ts.transpileModule(netSource.slice(start,end),{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText
const rebuild=(base,delta,enabled=true)=>new Function('lastGameStateEnvelope','negotiatedEventWindowDelta','negotiatedHandArrayDelta','restoreHandSplices','validHandDeltaChangePath','delta',
  netBody+';return rebuildDeltaGameState(delta)')(base,true,enabled,restore,exports.validHandDeltaChangePath,delta)
const delta={type:'gameStateDelta',matchId:'synthetic',baseRevision:1,revision:2,
  changes:[{path:['state','revision'],value:2,remove:false},{path:['state','stateHash'],value:'hash-2',remove:false}],handSplices:clone(wire)}
const expected=frame(2,['a','c','b']),frozen=JSON.stringify(baseline)
assert.deepEqual(rebuild(baseline,delta),expected);assert.equal(JSON.stringify(baseline),frozen)
assert.equal(rebuild(baseline,delta,false),null)
assert.equal(rebuild(expected,delta),null)
assert.equal(rebuild(null,delta),null)
const badHash=clone(delta);badHash.handSplices.baseStateHash='wrong';assert.equal(rebuild(baseline,badHash),null)
const otherMatch=clone(delta);otherMatch.matchId='other';assert.equal(rebuild(baseline,otherMatch),null)
const overlap=clone(delta);overlap.changes.push({path:['state','players',0,'hand'],value:[]});assert.equal(rebuild(baseline,overlap),null)
const legacy=clone(delta);delete legacy.handSplices;legacy.changes.push({path:['state','players',0,'hand'],value:expected.state.players[0].hand})
assert.deepEqual(rebuild(baseline,legacy,false),expected)
let hostilePaths=0
for(const path of [['state','players','0','hand',0,'name'],['state','players',0,'hand',0,'name'],
  ['state','__proto__','handDeltaPollution'],['state','players',-1,'hand'],['state','players',0.5,'hand']]){
  const bad=clone(delta);bad.changes.push({path,value:'bad'})
  assert.equal(rebuild(baseline,bad),null);assert.equal({}.handDeltaPollution,undefined);hostilePaths++
}
const mutateMatch=clone(delta);mutateMatch.changes.push({path:['state','matchId'],value:'other'})
assert.equal(rebuild(baseline,mutateMatch),null)
const stringRevision=clone(delta);stringRevision.baseRevision='1';assert.equal(rebuild(baseline,stringRevision),null)
for(const path of [['state','you'],['observerView']]){
  const changeScope=clone(delta);changeScope.changes.push({path,value:path.length===1?'referee':1})
  assert.equal(rebuild(baseline,changeScope),null)
}
assert.equal(JSON.stringify(baseline),frozen)
let actualServerCases=0,actualSocketCases=0,wireTotals=null
if(process.argv[2]){
  const fixtures=JSON.parse(fs.readFileSync(process.argv[2],'utf8'))
  assert.equal(fixtures.schema,1);assert.equal(fixtures.syntheticOnly,true)
  assert.equal(fixtures.frames,57);assert.equal(fixtures.realEndTurns,18)
  assert(Array.isArray(fixtures.clientCases)&&fixtures.clientCases.length>0)
  for(const item of fixtures.clientCases){
    const before=JSON.stringify(item.baseline)
    assert.deepEqual(rebuild(item.baseline,item.delta),item.expected)
    assert.equal(rebuild(item.baseline,item.delta,false),null)
    assert.equal(JSON.stringify(item.baseline),before)
    actualServerCases++
  }
  assert.equal(actualServerCases,fixtures.handDeltaFrames)
  wireTotals={newBytes:fixtures.newBytes,oldBytes:fixtures.oldBytes}
}
if(process.argv[3]){
  const fixtures=JSON.parse(fs.readFileSync(process.argv[3],'utf8'))
  assert.equal(fixtures.schema,1);assert.equal(fixtures.syntheticOnly,true);assert.equal(fixtures.realSocket,true)
  assert.equal(fixtures.realCommands,10);assert(fixtures.clientCases.length>0)
  for(const item of fixtures.clientCases){
    const frozen=JSON.stringify(item.baseline)
    assert.deepEqual(rebuild(item.baseline,item.delta),item.expected)
    assert.equal(rebuild(item.baseline,item.delta,false),null)
    assert.equal(JSON.stringify(item.baseline),frozen);actualSocketCases++
  }
  assert.equal(actualSocketCases,fixtures.handDeltaFrames)
}
console.log(JSON.stringify({status:'passed',cases,actualNetReconstructionCases:12,hostilePaths,actualServerCases,actualSocketCases,wireTotals,
  scope:'actual net reconstruction of engine and optional real socket fixtures; not full browser UI or online capacity',outputs:0}))
