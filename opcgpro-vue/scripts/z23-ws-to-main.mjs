import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const WS='wss://legion-12.com/testrun/ws'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const login=async n=>{
  const j=await (await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:n,password:pw})})).json()
  const d=await (await fetch(BASE+'/api/decks',{headers:{Authorization:'Bearer '+j.token}})).json()
  return {name:n,token:j.token,decks:Array.isArray(d)?d:(d.decks||[])}
}
function connect(s){
  const ws=new WebSocket(WS); const box={ws,state:null,room:null,errors:[]}
  ws.addEventListener('message',e=>{
    let m=null; try{m=JSON.parse(String(e.data))}catch{}
    if(!m) return
    if(m.type==='gameState') box.state=m
    if(m.type==='roomState') box.room=m
    if(m.type==='error') box.errors.push(m.message)
  })
  ws.addEventListener('open',()=>ws.send(JSON.stringify({type:'hello',authToken:s.token,capabilities:{requestIds:true,deltaGameState:true,eventWindowDeltaV1:true,presenceDeltaV1:true,presenceOrderReuseV1:true,chatV1:true,gameStateCardFrameV1:true}})))
  box.send=o=>ws.send(JSON.stringify(o))
  box.act=c=>box.send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command:c})
  box.me=0
  return box
}
const sleep=ms=>new Promise(r=>setTimeout(r,ms))
const A0=await login('彩排01'), B0=await login('彩排02')
const A=connect(A0), B=connect(B0)
await sleep(3500)
const log=[]
if(A.state||B.state){ A.act({type:'surrender'}); await sleep(2000); A.send({type:'leaveRoom'}); B.send({type:'leaveRoom'}); await sleep(2000) }
A.send({type:'createRoom',options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'all',useCardRestrictions:false}})
await sleep(3000)
const deckA=A0.decks.find(d=>d.name==='月读实机A'); if(deckA) A.send({type:'selectCustomDeck',deck:deckA})
const code=A.room?.roomCode||''
if(code) B.send({type:'joinRoom',roomCode:code})
await sleep(2500)
const deckB=B0.decks.find(d=>d.name.includes('月读实机B')); if(deckB) B.send({type:'selectCustomDeck',deck:deckB})
await sleep(2000)
A.send({type:'ready',ready:true}); B.send({type:'ready',ready:true})
await sleep(5000)
for (let i=0;i<25;i++){
  const st=A.state?.state||B.state?.state||{}
  const prompts=st.prompts||[]
  if(!prompts.length){ await sleep(1200); continue }
  const p=prompts[0]
  const box=p.playerIndex===A.state?.state?.you?A:B
  const me=box.state?.state?.you
  log.push({i,kind:p.kind,player:p.playerIndex,me,choices:p.validChoices.slice(0,4),phase:st.phase})
  box.act({type:'resolvePrompt',promptId:p.promptId,choice:p.validChoices[0]})
  await sleep(1400)
  if(st.phase==='Main') break
}
const st=A.state?.state||{}
const summarize=s=>({phase:s.phase,activePlayer:s.activePlayer,you:s.you,round:s.round,
  hand0:(s.players?.[0]?.hand||[]).map(c=>c.cardId).slice(0,6),hand1:(s.players?.[1]?.hand||[]).map(c=>c.cardId).slice(0,6),
  field0:(s.players?.[0]?.field||[]).map(r=>r.map(c=>c&&c.cardId)),morale0:(s.players?.[0]?.morale||[]).map(m=>({id:m.cardId,tapped:m.tapped}))})
log.push({step:'state',sum:summarize(st),errorsA:A.errors.slice(-3),errorsB:B.errors.slice(-3)})
console.log(JSON.stringify(log,null,2))
A.ws.close(); B.ws.close()
