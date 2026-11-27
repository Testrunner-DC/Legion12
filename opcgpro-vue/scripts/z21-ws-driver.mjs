import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const WS='wss://legion-12.com/testrun/ws'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const log=[]
const login=async name=>{
  const j=await (await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:name,password:pw})})).json()
  const decks=await (await fetch(BASE+'/api/decks',{headers:{Authorization:'Bearer '+j.token}})).json()
  const list=Array.isArray(decks)?decks:(decks.decks||[])
  return {name,token:j.token,decks:list}
}
function connect(s){
  const ws=new WebSocket(WS); const box={ws,state:null,room:null,errors:[],sent:0}
  ws.addEventListener('message',e=>{
    let m=null; try{m=JSON.parse(String(e.data))}catch{}
    if(!m) return
    if(m.type==='gameState') box.state=m
    if(m.type==='roomState') box.room=m
    if(m.type==='error') box.errors.push(JSON.stringify(m).slice(0,120))
  })
  ws.addEventListener('open',()=>ws.send(JSON.stringify({type:'hello',authToken:s.token,capabilities:{requestIds:true,deltaGameState:true,eventWindowDeltaV1:true,presenceDeltaV1:true,presenceOrderReuseV1:true,chatV1:true,gameStateCardFrameV1:true}})))
  box.send=o=>{box.sent++; ws.send(JSON.stringify(o))}
  box.act=(command)=>box.send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command})
  return box
}
const sleep=ms=>new Promise(r=>setTimeout(r,ms))
const A0=await login('彩排01'), B0=await login('彩排02')
const deckA=A0.decks.find(d=>d.name==='月读实机A'), deckB=B0.decks.find(d=>d.name.includes('月读实机B'))
const A=connect(A0); const B=connect(B0)
await sleep(3500)
A.send({type:'createRoom',options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'all',useCardRestrictions:false}})
await sleep(3000)
if(deckA) A.send({type:'selectCustomDeck',deck:deckA})
const code=A.room?.roomCode||null
if(code) B.send({type:'joinRoom',roomCode:code})
await sleep(3000)
if(deckB) B.send({type:'selectCustomDeck',deck:deckB})
await sleep(2500)
A.send({type:'ready',ready:true}); B.send({type:'ready',ready:true})
await sleep(5000)
log.push({step:'start',matchId:A.state?.state?.matchId,phase:A.state?.state?.phase,code})
// 通用推进：谁有提示就谁都尝试应答，最多 40 步
for (let i=0;i<40;i++){
  const active=[A,B].find(box=>box.state?.state?.pendingPrompts?.length)
  if(!active){ await sleep(1200); continue }
  const st=active.state.state
  const prompt=st.pendingPrompts[0]
  const choice=prompt.validChoices?.[0]
  const player=active===A?A:B
  log.push({step:'prompt',i,kind:prompt.kind,player:player===A?'A':'B',choices:(prompt.validChoices||[]).slice(0,4),phase:st.phase})
  player.act({type:'resolvePrompt',promptId:prompt.promptId,choice,cardInstanceIds:prompt.validChoices?[prompt.validChoices[0]]:[]})
  await sleep(1500)
  if((active.state.state.phase==='Main')) break
}
const st=A.state?.state||{}
log.push({step:'final',phase:st.phase,activePlayer:st.activePlayer,round:st.round,hand:(st.players?.[0]?.hand||[]).length,field:(st.players?.[0]?.field||[]).map(r=>r.filter(Boolean).length),morale:(st.players?.[0]?.morale||[]).length,errorsA:A.errors.slice(0,3),errorsB:B.errors.slice(0,3)})
console.log(JSON.stringify(log,null,2))
A.ws.close(); B.ws.close()
