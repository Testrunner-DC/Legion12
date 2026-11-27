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
const ws=new WebSocket(WS); const box={state:null,room:null,errors:[]}
ws.addEventListener('message',e=>{let m=null;try{m=JSON.parse(String(e.data))}catch{};if(!m)return
  if(m.type==='gameState')box.state=m; if(m.type==='roomState')box.room=m; if(m.type==='error')box.errors.push(m.message)})
ws.addEventListener('open',()=>ws.send(JSON.stringify({type:'hello',authToken:A0.token,capabilities:{requestIds:true,eventWindowDeltaV1:true,presenceDeltaV1:true,chatV1:true}})))
const send=o=>ws.send(JSON.stringify(o))
const sync=async(ms=1100)=>{send({type:'syncState',plainFull:true});await sleep(ms);return box.state?.state||null}
const act=async(cmd,ms=1300)=>{send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command:cmd});await sleep(ms);return sync(700)}
const gm=async(cmd,ms=1200)=>{send({type:'gmAction',requestId:'g'+Math.random().toString(36).slice(2),command:cmd});await sleep(ms);return sync(700)}
const sb=async(idx,cmd,ms=1200)=>{send({type:'sandboxAction',requestId:'s'+Math.random().toString(36).slice(2),actingPlayerIndex:idx,command:cmd});await sleep(ms);return sync(700)}
const log=[]
await sleep(3000); await sync()
if(box.state) { await act({type:'surrender'}); send({type:'leaveRoom'}); await sleep(2000); await sync() }
send({type:'createSandbox',request:{playerDeck:deckA,opponentDeck:deckB,disasterMode:'none'}})
await sleep(5000); await sync()
const st=box.state?.state
log.push({step:'sandbox',ok:!!st,matchId:st?.matchId,phase:st?.phase,gmEnabled:st?.gmEnabled,
  players:(st?.players||[]).map(p=>({n:p.name,deck:p.deckName,hand:(p.hand||[]).map(c=>c.cardId).slice(0,6),morale:(p.morale||[]).length}))})
// 让 A 至少一张士气休整
await gm({type:'restMorale',targetPlayer:0})
const s1=box.state?.state
log.push({step:'afterRestMorale',morale0:(s1?.players?.[0]?.morale||[]).map(m=>({id:m.cardId,t:m.tapped})),err:box.errors.slice(-2)})
fs.writeFileSync(OUT+'/z31-log.json',JSON.stringify(log,null,2))
console.log(JSON.stringify(log,null,2).slice(0,2200))
ws.close()
