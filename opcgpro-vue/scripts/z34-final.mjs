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
const P=i=>S()?.players?.[i]
const sync=async(ms=1000)=>{send({type:'syncState',plainFull:true});await sleep(ms);return S()}
const act=async(c,ms=1200)=>{send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command:c});await sleep(ms);return sync(700)}
const gm=async(c,ms=1200)=>{send({type:'gmAction',requestId:'g'+Math.random().toString(36).slice(2),command:c});await sleep(ms);return sync(700)}
const sb=async(i,c,ms=1300)=>{send({type:'sandboxAction',requestId:'s'+Math.random().toString(36).slice(2),actingPlayerIndex:i,command:c});await sleep(ms);return sync(700)}
const log=[]
await sleep(2800); await sync()
if(S()){send({type:'leaveRoom'});await sleep(1500);await sync()}
send({type:'createSandbox',request:{PlayerDeck:deckA,OpponentDeck:deckB,DisasterMode:'none'}})
await sleep(5000); await sync()
log.push({step:'sandbox',matchId:S()?.matchId,phase:S()?.phase,players:(S()?.players||[]).map(p=>({n:p.name,hand:(p.hand||[]).length,morale:(p.morale||[]).length}))})
// GM（PascalCase）: A 士气休整 + 把毒药抽到 B 手
await gm({Type:'restMorale',TargetPlayer:0})
let poisonId=null
for(let i=0;i<8 && !poisonId;i++){
  const c=(P(1)?.hand||[]).find(x=>x.cardId==='S02-0018')
  if(c){poisonId=c.instanceId;break}
  await gm({Type:'draw',TargetPlayer:1,Value:3})
}
log.push({step:'setup1',morale0:(P(0)?.morale||[]).map(m=>m.tapped),poisonId,err:box.errors.slice(-2)})
// A 军团：用 GM 放到前排
const high=(P(0)?.hand||[]).find(c=>['S02-0401','S02-0402','S02-0403'].includes(c.cardId))
let legionId=null
if(high){
  await gm({Type:'placeCard',TargetPlayer:0,CardInstanceId:high.instanceId,Row:0,Slot:0},1600)
  legionId=P(0)?.field?.[0]?.[0]?.instanceId||null
  if(!legionId){ await gm({Type:'playHandCard',TargetPlayer:0,CardInstanceId:high.instanceId,Row:0,Slot:0},1600); legionId=P(0)?.field?.[0]?.[0]?.instanceId||null }
}
log.push({step:'setup2',legionId,field0:(P(0)?.field||[]).map(r=>r.map(c=>c&&c.cardId)),err:box.errors.slice(-2)})
// B 设毒药（playCard 作为设置战术）
if(poisonId&&legionId){ await sb(1,{type:'playCard',cardInstanceId:poisonId},1700) }
log.push({step:'setup3',err:box.errors.slice(-2)})
// A 位移
if(legionId){ await sb(0,{type:'move',cardInstanceId:legionId,row:1,slot:0},1900) }
for(let i=0;i<16;i++){
  const s=S(); const prompts=s?.prompts||[]
  if(!prompts.length) break
  const p=prompts[0]; const ch=p.validChoices||[]
  log.push({step:'prompt',kind:p.kind,player:p.playerIndex,choices:ch.slice(0,6),hasPoison:!!poisonId&&ch.includes(poisonId)})
  if(poisonId&&ch.includes(poisonId)){ await sb(p.playerIndex,{type:'resolvePrompt',promptId:p.promptId,choice:poisonId},1700); log.push({step:'COUNTERED'}); break }
  const cmd={type:'resolvePrompt',promptId:p.promptId}
  if((p.maxChoose||1)>1||p.kind==='order') cmd.cardInstanceIds=ch.slice(0,p.maxChoose||1)
  else if(ch.length) cmd.choice=ch[0]
  else cmd.cardInstanceIds=[]
  await sb(p.playerIndex,cmd,1500)
}
log.push({step:'after',morale0:(P(0)?.morale||[]).map(m=>({t:m.tapped})),field0:(P(0)?.field||[]).map(r=>r.map(c=>c&&c.cardId)),
  prompts:(S()?.prompts||[]).map(p=>({k:p.kind,pl:p.playerIndex,c:(p.validChoices||[]).slice(0,5)}))})
fs.writeFileSync(OUT+'/z34-log.json',JSON.stringify(log,null,2))
console.log(JSON.stringify(log,null,2).slice(0,3000))
ws.close()
