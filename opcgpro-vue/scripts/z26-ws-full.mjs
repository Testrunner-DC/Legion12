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
const S=b=>b.state?.state||null
const me=b=>{const s=S(b);return s?s.players[s.you]:null}
const myPrompt=b=>{const s=S(b);if(!s)return null;return (s.prompts||[]).find(p=>p.playerIndex===s.you)||null}
async function sync(b,ms=1100){b.send({type:'syncState',plainFull:true});await sleep(ms);return S(b)}
async function act(b,cmd,ms=1300){b.send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command:cmd});await sleep(ms);return sync(b,900)}
const log=[]
const A0=await login('彩排01'),B0=await login('彩排02')
const A=connect(A0),B=connect(B0)
await sleep(3000); await sync(A); await sync(B)
if(S(A)||S(B)){await act(A,{type:'surrender'});A.send({type:'leaveRoom'});B.send({type:'leaveRoom'});await sleep(2500);await sync(A);await sync(B)}
A.send({type:'createRoom',options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'all',useCardRestrictions:false}})
await sleep(2500)
const deckA=A0.decks.find(d=>d.name==='月读实机A'); if(deckA) A.send({type:'selectCustomDeck',deck:deckA})
const code=A.room?.roomCode||''
if(code) B.send({type:'joinRoom',roomCode:code}); await sleep(2200)
const deckB=B0.decks.find(d=>d.name.includes('月读实机B')); if(deckB) B.send({type:'selectCustomDeck',deck:deckB})
await sleep(1500); A.send({type:'ready',ready:true}); B.send({type:'ready',ready:true}); await sleep(4500)
await sync(A); await sync(B)
log.push({step:'opened',matchId:S(A)?.matchId,phase:S(A)?.phase,code})

async function answerPrompts(max=60,target){
  for(let i=0;i<max;i++){
    if(target && (S(A)?.phase===target)) return true
    let done=false
    for(const b of [A,B]){
      const p=myPrompt(b); if(!p) continue
      const st=S(b)
      const cmd={type:'resolvePrompt',promptId:p.promptId}
      if(p.kind==='initiative') cmd.choice=p.validChoices.includes('first')?'first':p.validChoices[0]
      else if((p.validChoices||[]).length) cmd.choice=p.validChoices[0]
      else cmd.cardInstanceIds=[]
      log.push({step:'prompt',kind:p.kind,who:b.name,phase:st?.phase,choice:cmd.choice||'(ids)'})
      await act(b,cmd); done=true; break
    }
    if(!done){
      // mulligan 阶段是命令而非提示
      if(S(A)?.phase==='Mulligan'){
        if(!me(A)?.mulliganDone) { log.push({step:'mulligan',who:A.name}); await act(A,{type:'mulligan',cardInstanceIds:[]}) }
        else if(!me(B)?.mulliganDone) { log.push({step:'mulligan',who:B.name}); await act(B,{type:'mulligan',cardInstanceIds:[]}) }
        else await sleep(900)
        await sync(A,700); await sync(B,700)
        continue
      }
      await sleep(900); await sync(A,700); await sync(B,700)
    }
  }
  return S(A)?.phase===target
}
await answerPrompts(60,'Main')
log.push({step:'main',a:{phase:S(A)?.phase,active:S(A)?.activePlayer,hand:me(A)?.hand?.map(c=>c.cardId),morale:me(A)?.morale?.map(m=>({id:m.cardId,t:m.tapped}))},
  b:{phase:S(B)?.phase,hand:me(B)?.hand?.map(c=>c.cardId)}})
fs.writeFileSync(OUT+'/z26-log.json',JSON.stringify(log,null,2))
console.log(JSON.stringify(log.slice(-6),null,2))
A.ws.close();B.ws.close()
