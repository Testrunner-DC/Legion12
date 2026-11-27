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
function connect(s){
  const ws=new WebSocket(WS); const box={name:s.name,ws,state:null,room:null,errors:[]}
  ws.addEventListener('message',e=>{
    let m=null; try{m=JSON.parse(String(e.data))}catch{}
    if(!m) return
    if(m.type==='gameState') box.state=m
    if(m.type==='roomState') box.room=m
    if(m.type==='error') box.errors.push(m.message)
  })
  ws.addEventListener('open',()=>ws.send(JSON.stringify({type:'hello',authToken:s.token,capabilities:{requestIds:true,eventWindowDeltaV1:true,presenceDeltaV1:true,chatV1:true}})))
  box.send=o=>ws.send(JSON.stringify(o))
  return box
}
async function sync(box){
  box.send({type:'syncState',plainFull:true}); await sleep(1200)
  return box.state?.state||null
}
async function act(box,command){
  box.send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command})
  await sleep(1400); return sync(box)
}
const st_of=b=>b.state?.state||null
const myPrompts=b=>{const s=st_of(b); if(!s) return []; return (s.prompts||[]).filter(p=>p.playerIndex===s.you)}
const summary=b=>{const s=st_of(b)||{};return {phase:s.phase,you:s.you,activePlayer:s.activePlayer,round:s.round,
  hand:(s.players?.[s.you]?.hand||[]).map(c=>c.cardId),
  field:(s.players?.[s.you]?.field||[]).map(r=>r.map(c=>c&&c.cardId)),
  morale:(s.players?.[s.you]?.morale||[]).map(m=>({id:m.cardId,tapped:m.tapped}))}}
const log=[]
const A0=await login('彩排01'), B0=await login('彩排02')
const A=connect(A0), B=connect(B0)
await sleep(3000)
await sync(A); await sync(B)
if(st_of(A)||st_of(B)){ await act(A,{type:'surrender'}); A.send({type:'leaveRoom'}); B.send({type:'leaveRoom'}); await sleep(2500) }
await sync(A); await sync(B)
A.send({type:'createRoom',options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'all',useCardRestrictions:false}})
await sleep(2500)
const deckA=A0.decks.find(d=>d.name==='月读实机A'); if(deckA) A.send({type:'selectCustomDeck',deck:deckA})
const code=A.room?.roomCode||''; log.push({code})
if(code) B.send({type:'joinRoom',roomCode:code})
await sleep(2200)
const deckB=B0.decks.find(d=>d.name.includes('月读实机B')); if(deckB) B.send({type:'selectCustomDeck',deck:deckB})
await sleep(1500)
A.send({type:'ready',ready:true}); B.send({type:'ready',ready:true})
await sleep(4500)
await sync(A); await sync(B)
log.push({step:'opened',matchId:st_of(A)?.matchId,phase:st_of(A)?.phase})

// 通用应答：initiative 选先攻，其它取第一个合法项
async function answerAll(){
  for (let i=0;i<30;i++){
    let answered=false
    for (const box of [A,B]){
      const ps=myPrompts(box)
      if(!ps.length) continue
      const p=ps[0]
      const choice=p.kind==='initiative' ? (p.validChoices.includes('first')?'first':p.validChoices[0]) : p.validChoices[0]
      log.push({step:'prompt',kind:p.kind,who:box.name,choice,phase:st_of(box)?.phase})
      await act(box,{type:'resolvePrompt',promptId:p.promptId,choice})
      answered=true
      break
    }
    if(!answered) break
  }
}
await answerAll()
log.push({step:'after-setup',a:summary(A),b:summary(B)})
console.log(JSON.stringify(log,null,2))
fs.writeFileSync(OUT+'/z24-log.json',JSON.stringify(log,null,2))
A.ws.close(); B.ws.close()
