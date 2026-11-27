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
  ws.addEventListener('message',e=>{let m=null;try{m=JSON.parse(String(e.data))}catch{};if(!m)return
    if(m.type==='gameState')box.state=m; if(m.type==='roomState')box.room=m; if(m.type==='error')box.errors.push(m.message)})
  ws.addEventListener('open',()=>ws.send(JSON.stringify({type:'hello',authToken:s.token,capabilities:{requestIds:true,eventWindowDeltaV1:true,presenceDeltaV1:true,chatV1:true}})))
  box.send=o=>ws.send(JSON.stringify(o)); return box
}
const S=b=>b.state?.state||null
const me=b=>{const s=S(b);return s?s.players[s.you]:null}
const myPrompt=b=>{const s=S(b);if(!s)return null;return (s.prompts||[]).find(p=>p.playerIndex===s.you)||null}
const sleepSync=async(b,ms=900)=>{b.send({type:'syncState',plainFull:true});await sleep(ms);return S(b)}
const act=async(b,cmd,ms=1300)=>{b.send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command:cmd});await sleep(ms);return sleepSync(b,800)}
const log=[]
const A0=await login('彩排01'),B0=await login('彩排02')
const A=connect(A0),B=connect(B0)
await sleep(3000); await sleepSync(A); await sleepSync(B)
if(S(A)||S(B)){await act(A,{type:'surrender'});A.send({type:'leaveRoom'});B.send({type:'leaveRoom'});await sleep(2500);await sleepSync(A);await sleepSync(B)}
A.send({type:'createRoom',options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'all',useCardRestrictions:false}})
await sleep(2500)
const deckA=A0.decks.find(d=>d.name==='月读实机A'), deckB=B0.decks.find(d=>d.name.includes('月读实机B'))
async function ensureDeck(box,deck,wait=6){
  for(let i=0;i<wait;i++){
    await sleepSync(box,700)
    const p=(box.room?.players||[]).find(x=>x.name===box.name)
    if(p && p.deckName===deck.name) return true
    box.send({type:'selectCustomDeck',deck}); await sleep(1200)
  }
  return false
}
log.push({deckA:await ensureDeck(A,deckA)})
const code=A.room?.roomCode||''
if(code) B.send({type:'joinRoom',roomCode:code})
await sleep(2500)
log.push({deckB:await ensureDeck(B,deckB)})
log.push({roomSnapshot:(A.room?.players||[]).map(p=>({n:p.name,deck:p.deckName,ready:p.ready})),code})
A.send({type:'ready',ready:true}); B.send({type:'ready',ready:true}); await sleep(4500)
await sleepSync(A); await sleepSync(B)
log.push({match:S(A)?.matchId,phase:S(A)?.phase})
async function answerPrompts(max=70,target){
  for(let i=0;i<max;i++){
    if(target && S(A)?.phase===target) return true
    let done=false
    for(const b of [A,B]){
      const p=myPrompt(b); if(!p) continue
      const cmd={type:'resolvePrompt',promptId:p.promptId}
      if(p.kind==='initiative') cmd.choice=p.validChoices.includes('first')?'first':p.validChoices[0]
      else if((p.validChoices||[]).length) cmd.choice=p.validChoices[0]
      else cmd.cardInstanceIds=[]
      log.push({p:p.kind,who:b.name,choice:cmd.choice||'(ids)'})
      await act(b,cmd); done=true; break
    }
    if(!done){
      if(S(A)?.phase==='Mulligan'){
        const aDone=me(A)?.mulliganDone, bDone=me(B)?.mulliganDone
        if(!aDone) await act(A,{type:'mulligan',cardInstanceIds:[]})
        else if(!bDone) await act(B,{type:'mulligan',cardInstanceIds:[]})
        else await sleep(800)
        await sleepSync(A,600); await sleepSync(B,600); continue
      }
      await sleep(800); await sleepSync(A,600); await sleepSync(B,600)
    }
  }
  return S(A)?.phase===target
}
await answerPrompts(70,'Main')
const a0=S(A),b0=S(B)
log.push({step:'main',phaseA:a0?.phase,active:a0?.activePlayer,first:a0?.firstPlayer,
  handA:me(A)?.hand?.map(c=>c.cardId),handB:me(B)?.hand?.map(c=>c.cardId),
  moraleA:me(A)?.morale?.map(m=>({id:m.cardId,t:m.tapped})),moraleB:me(B)?.morale?.map(m=>({id:m.cardId,t:m.tapped}))})
fs.writeFileSync(OUT+'/z27-log.json',JSON.stringify(log,null,2))
console.log(JSON.stringify(log,null,2).slice(0,3000))
A.ws.close();B.ws.close()
