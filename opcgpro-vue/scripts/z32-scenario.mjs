import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const WS='wss://legion-12.com/testrun/ws'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const OUT='D:/GPT/Legion12/artifacts/z9-moon-poison-20261127'
const sleep=ms=>new Promise(r=>setTimeout(r,ms))
const login=async n=>{
  const j=await (await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:n,password:pw})})).json()
  const d=await (await fetch(BASE+'/api/decks',{headers:{Authorization:'Bearer '+j.token}})).json()
  return {name:n,token:j.token,decks:Array.isArray(d)?d:(d.decks||[])}
}
const A0=await login('彩排01'), B0=await login('彩排02')
const deckA=A0.decks.find(d=>d.name==='月读实机A'), deckB=B0.decks.find(d=>d.name.includes('月读实机B'))
const ws=new WebSocket(WS); const box={state:null,errors:[]}
ws.addEventListener('message',e=>{let m=null;try{m=JSON.parse(String(e.data))}catch{};if(!m)return
  if(m.type==='gameState')box.state=m; if(m.type==='error')box.errors.push(m.message)})
ws.addEventListener('open',()=>ws.send(JSON.stringify({type:'hello',authToken:A0.token,capabilities:{requestIds:true,eventWindowDeltaV1:true,presenceDeltaV1:true,chatV1:true}})))
const send=o=>ws.send(JSON.stringify(o))
const S=()=>box.state?.state||null
const sync=async(ms=1000)=>{send({type:'syncState',plainFull:true});await sleep(ms);return S()}
const act=async(c,ms=1300)=>{send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command:c});await sleep(ms);return sync(700)}
const gm=async(c,ms=1200)=>{send({type:'gmAction',requestId:'g'+Math.random().toString(36).slice(2),command:c});await sleep(ms);return sync(700)}
const sb=async(i,c,ms=1300)=>{send({type:'sandboxAction',requestId:'s'+Math.random().toString(36).slice(2),actingPlayerIndex:i,command:c});await sleep(ms);return sync(700)}
const log=[]
const P=i=>S()?.players?.[i]
await sleep(2800); await sync()
if(S()){await act({type:'surrender'});send({type:'leaveRoom'});await sleep(2000);await sync()}
send({type:'createSandbox',request:{playerDeck:deckA,opponentDeck:deckB,disasterMode:'none'}})
await sleep(5000); await sync()
await gm({type:'restMorale',targetPlayer:0})
// 把毒药抽到 B 手上
let poisonId=null
for(let i=0;i<10 && !poisonId;i++){
  const b=P(1); const c=(b?.hand||[]).find(x=>x.cardId==='S02-0018')
  if(c){poisonId=c.instanceId;break}
  await gm({type:'draw',targetPlayer:1,count:3})
}
log.push({step:'poisonInHand',poisonId,handB:(P(1)?.hand||[]).map(c=>c.cardId)})
// A 把高天原军团放到前排
const high=(P(0)?.hand||[]).find(c=>['S02-0401','S02-0402','S02-0403'].includes(c.cardId))
let legionId=null
if(high){
  await sb(0,{type:'playCard',cardInstanceId:high.instanceId,row:0,slot:0,targetPlayerIndex:0},1700)
  const f=P(0)?.field
  legionId=f?.[0]?.[0]?.instanceId||null
}
log.push({step:'legion',legionId,field0:(P(0)?.field||[]).map(r=>r.map(c=>c&&c.cardId))})
// B 设置毒药
if(poisonId){ await sb(1,{type:'playCard',cardInstanceId:poisonId},1700) }
log.push({step:'poisonSet',err:box.errors.slice(-2)})
// A 位移触发月读
if(legionId){ await sb(0,{type:'move',cardInstanceId:legionId,row:1,slot:0},1900) }
// 处理提示并观察 B 的响应窗
for(let i=0;i<14;i++){
  const s=S(); if(!s) break
  const prompts=s.prompts||[]
  if(!prompts.length) break
  const p=prompts[0]
  const choices=p.validChoices||[]
  log.push({step:'prompt',kind:p.kind,player:p.playerIndex,choices:choices.slice(0,6),hasPoison:!!poisonId&&choices.includes(poisonId)})
  if(poisonId && choices.includes(poisonId)){
    await sb(p.playerIndex,{type:'resolvePrompt',promptId:p.promptId,choice:poisonId},1700)
    log.push({step:'COUNTERED'})
    break
  }
  const cmd={type:'resolvePrompt',promptId:p.promptId}
  if(p.kind==='order'||p.minChoose>1||choices.length>1) cmd.cardInstanceIds=choices.slice(0,p.maxChoose||1)
  else if(choices.length) cmd.choice=choices[0]
  else cmd.cardInstanceIds=[]
  await sb(p.playerIndex,cmd,1600)
}
const s=S()
log.push({step:'after',morale0:(P(0)?.morale||[]).map(m=>({id:m.cardId,t:m.tapped})),field0:(P(0)?.field||[]).map(r=>r.map(c=>c&&c.cardId)),prompts:(s?.prompts||[]).map(p=>({k:p.kind,pl:p.playerIndex,c:(p.validChoices||[]).slice(0,4)}))})
fs.writeFileSync(OUT+'/z32-log.json',JSON.stringify(log,null,2))
console.log(JSON.stringify(log.filter(e=>['poisonInHand','legion','poisonSet','prompt','COUNTERED','after'].includes(e.step)),null,2).slice(0,3000))
ws.close()
