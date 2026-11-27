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
  const ws=new WebSocket(WS); const box={ws,state:null,room:null,errors:[],raw:[]}
  ws.addEventListener('message',e=>{
    let m=null; try{m=JSON.parse(String(e.data))}catch{}
    if(!m) return
    if(m.type==='gameState') box.state=m
    if(m.type==='roomState') box.room=m
    if(m.type==='error') box.errors.push(m.message)
    if(box.raw.length<400) box.raw.push(m.type)
  })
  ws.addEventListener('open',()=>ws.send(JSON.stringify({type:'hello',authToken:s.token,capabilities:{requestIds:true,deltaGameState:true,eventWindowDeltaV1:true,presenceDeltaV1:true,presenceOrderReuseV1:true,chatV1:true,gameStateCardFrameV1:true}})))
  box.send=o=>ws.send(JSON.stringify(o))
  box.act=c=>box.send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command:c})
  return box
}
const sleep=ms=>new Promise(r=>setTimeout(r,ms))
const A0=await login('彩排01'), B0=await login('彩排02')
const A=connect(A0), B=connect(B0)
await sleep(3500)
const out={}
// 清掉残留对局
if(A.state||B.state){ A.act({type:'surrender'}); await sleep(2500); A.send({type:'leaveRoom'}); B.send({type:'leaveRoom'}); await sleep(2500) }
out.afterCleanup={aState:!!A.state,bState:!!B.state,errors:A.errors.slice(0,3)}
A.send({type:'createRoom',options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'all',useCardRestrictions:false}})
await sleep(3000)
const deckA=A0.decks.find(d=>d.name==='月读实机A'); if(deckA) A.send({type:'selectCustomDeck',deck:deckA})
const code=A.room?.roomCode; out.code=code
if(code) B.send({type:'joinRoom',roomCode:code})
await sleep(2500)
const deckB=B0.decks.find(d=>d.name.includes('月读实机B')); if(deckB) B.send({type:'selectCustomDeck',deck:deckB})
await sleep(2000)
A.send({type:'ready',ready:true}); B.send({type:'ready',ready:true})
await sleep(6000)
const st=A.state?.state||{}
out.keys=Object.keys(st)
out.promptish={prompts:st.prompts,waitingPrompt:st.waitingPrompt,pending:st.pending}
out.sample=JSON.stringify({prompts:st.prompts,waitingPrompt:st.waitingPrompt}).slice(0,700)
out.phase=st.phase; out.matchId=st.matchId
out.errorsLast=A.errors.slice(-3)
console.log(JSON.stringify(out,null,2))
A.ws.close(); B.ws.close()
