import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const WS='wss://legion-12.com/testrun/ws'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const OUT='D:/GPT/Legion12/artifacts/z9-moon-poison-20261127'
const sleep=ms=>new Promise(r=>setTimeout(r,ms))
const login=async n=>{
  const j=await (await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:n,password:pw})})).json()
  const d=await (await fetch(BASE+'/api/decks',{headers:{Authorization:'Bearer '+j.token}})).json()
  return {token:j.token,decks:Array.isArray(d)?d:(d.decks||[])}
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
const sync=async(ms=900)=>{send({type:'syncState',plainFull:true});await sleep(ms);return S()}
const gm=async(c,ms=1100)=>{send({type:'gmAction',requestId:'g'+Math.random().toString(36).slice(2),command:c});await sleep(ms);return sync(600)}
const sb=async(i,c,ms=1200)=>{send({type:'sandboxAction',requestId:'s'+Math.random().toString(36).slice(2),actingPlayerIndex:i,command:c});await sleep(ms);return sync(600)}
const log=[]
await sleep(2600); await sync()
if(S()){send({type:'leaveRoom'});await sleep(1500);await sync()}
send({type:'createSandbox',request:{PlayerDeck:deckA,OpponentDeck:deckB,DisasterMode:'none'}})
await sleep(5000); await sync()
log.push({step:'sandbox',matchId:S()?.matchId,phase:S()?.phase,gmEnabled:S()?.gmEnabled,
  p0:(P(0)?.morale||[]).length,p1:(P(1)?.morale||[]).length})
// 给 A 补士气并全部休整
await gm({Type:'addMorale',TargetPlayer:0,Value:8})
await gm({Type:'restMorale',TargetPlayer:0})
log.push({step:'morale',after:(P(0)?.morale||[]).map(m=>m.tapped)})
// A 的军团用 CardId 放前排
await gm({Type:'placeCard',TargetPlayer:0,CardId:'S02-0403',Row:0,Slot:0},1400)
const legion=P(0)?.field?.[0]?.[0]||null
log.push({step:'legion',ok:!!legion,field:(P(0)?.field||[]).map(r=>r.map(c=>c&&c.cardId)),err:box.errors.slice(-2)})
// B 的毒药放到场上（设置位）
await gm({Type:'placeCard',TargetPlayer:1,CardId:'S02-0018',Row:1,Slot:0},1400)
const poison=(P(1)?.field?.[1]?.[0])||(P(1)?.hand||[]).find(c=>c.cardId==='S02-0018')||null
log.push({step:'poison',ok:!!poison,id:poison?.instanceId,fieldB:(P(1)?.field||[]).map(r=>r.map(c=>c&&c.cardId)),err:box.errors.slice(-2)})
// A 位移
if(legion){ await sb(0,{type:'move',cardInstanceId:legion.instanceId,row:1,slot:0},1500) }
log.push({step:'moved',ok:!!(P(0)?.field?.[1]?.[0])})
// 处理提示，抓 B 的窗口
for(let i=0;i<14;i++){
  const s=S(); const ps=s?.prompts||[]
  if(!ps.length) break
  const p=ps[0]; const ch=p.validChoices||[]
  log.push({step:'prompt',kind:p.kind,player:p.playerIndex,choices:ch.slice(0,6),hasPoison:!!poison&&ch.includes(poison.instanceId)})
  if(poison&&ch.includes(poison.instanceId)){
    await sb(p.playerIndex,{type:'resolvePrompt',promptId:p.promptId,choice:poison.instanceId},1500)
    log.push({step:'COUNTERED'})
    break
  }
  const cmd={type:'resolvePrompt',promptId:p.promptId}
  if(p.kind==='order'||(p.maxChoose||1)>1) cmd.cardInstanceIds=ch.slice(0,p.maxChoose||1)
  else if(ch.length){ const tapped=(P(0)?.morale||[]).filter(m=>m.tapped).map(m=>m.instanceId); cmd.choice=ch.find(x=>tapped.includes(x))||ch[0] }
  else cmd.cardInstanceIds=[]
  await sb(p.playerIndex,cmd,1400)
}
log.push({step:'after',morales:(P(0)?.morale||[]).map(m=>({t:m.tapped})),prompts:(S()?.prompts||[]).map(p=>({k:p.kind,pl:p.playerIndex,c:(p.validChoices||[]).slice(0,4)}))})
fs.writeFileSync(OUT+'/z39-log.json',JSON.stringify(log,null,2))
console.log(JSON.stringify(log,null,2).slice(0,3000))
ws.close()
