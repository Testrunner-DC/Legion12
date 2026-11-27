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


// ===== 剧本 v2：放置 → 设毒药 → 位移 → 响应 =====
const POISON='S02-0018'
const HIGH=['S02-0411','S02-0401','S02-0402','S02-0403']
const detail=b=>{const s=S(b);const p=s?.players?.[s.you]||{};return {you:s?.you,phase:s?.phase,active:s?.activePlayer,
  hand:(p.hand||[]).map(c=>c.cardId),field:(p.field||[]).map(r=>r.map(c=>c&&c.cardId)),morale:(p.morale||[]).map(m=>({id:m.cardId,t:m.tapped}))}}
async function pump(max=10){
  for(let i=0;i<max;i++){
    let acted=false
    for(const b of [A,B]){
      const p=myPrompt(b)
      if(!p) continue
      const cmd={type:'resolvePrompt',promptId:p.promptId}
      const kind=p.kind||''
      const choices=p.validChoices||[]
      if(kind.includes('discard')){
        const skip=choices.find(c=>c==='skip'||c==='no'||c==='keep'||c==='none')
        if(skip) cmd.choice=skip
        else if(choices.length){ cmd.cardInstanceIds=[]; cmd.choice=choices[0] }
        else cmd.cardInstanceIds=[]
      } else if(choices.length) cmd.choice=choices[0]
      else cmd.cardInstanceIds=[]
      log.push({step:'autoPrompt',kind:p.kind,who:b.name,choice:cmd.choice||'(ids)'})
      await act(b,cmd); acted=true; break
    }
    if(!acted) break
  }
}
let placedId=null, poisonId=null
for(let turn=0; turn<24 && !(placedId&&poisonId); turn++){
  await sleepSync(A,600); await sleepSync(B,600)
  const sA=S(A); if(!sA) break
  await pump(6)
  const s=S(A)
  if(s?.phase!=='Main'){ await sleep(600); continue }
  const actor = s.activePlayer===s.you ? A : B
  const meP = s.players[s.activePlayer]
  const hand=(meP?.hand||[]).map(c=>c.cardId)
  if(actor===A && !placedId){
    const card=(meP.hand||[]).find(c=>HIGH.includes(c.cardId))
    if(card){
      const r=await act(A,{type:'playCard',cardInstanceId:card.instanceId,row:0,slot:0,targetPlayerIndex:S(A).you},1900)
      if(S(A)?.players?.[0]?.field?.[0]?.[0]) { placedId=card.instanceId; log.push({step:'placeA',cardId:card.cardId,id:card.instanceId}) }
      else log.push({step:'placeFail',cardId:card.cardId,err:A.errors.slice(-2)})
    } else log.push({step:'A_noHigh',hand})
  } else if(actor===B && !poisonId){
    const card=(meP.hand||[]).find(c=>c.cardId===POISON)
    if(card){ await act(B,{type:'playCard',cardInstanceId:card.instanceId},1900); poisonId=card.instanceId; log.push({step:'poisonSet',id:poisonId}) }
    else log.push({step:'B_noPoison',hand})
  }
  await pump(6)
  const s2=S(A)
  if(s2?.phase==='Main'){ log.push({step:'endTurn',who:s2.activePlayer===0?'A':'B'}); if(s2.activePlayer===0) await act(A,{type:'endTurn'},1700); else await act(B,{type:'endTurn'},1700) }
  await sleep(400)
}
log.push({step:'preMove',placedId,poisonId,detailA:detail(A),detailB:detail(B)})
// A 位移触发
let countered=false
for(let guard=0; guard<16 && placedId && !countered; guard++){
  await pump(4)
  await sleepSync(A,700)
  const s=S(A); if(!s) break
  if(s.phase==='Main' && s.activePlayer===s.you){
    const r=await act(A,{type:'move',cardInstanceId:placedId,row:1,slot:0},1900)
    log.push({step:'moved',ok:!!(S(A)?.players?.[0]?.field?.[1]?.[0]),err:A.errors.slice(-2)})
  }
  await sleep(600)
  for(let k=0;k<6;k++){
    const pA=myPrompt(A), pB=myPrompt(B)
    if(pB){ log.push({step:'B_WINDOW',kind:pB.kind,choices:(pB.validChoices||[]).slice(0,6),hasPoison:!!poisonId&&(pB.validChoices||[]).includes(poisonId)})
      if(poisonId && (pB.validChoices||[]).includes(poisonId)){
        await act(B,{type:'resolvePrompt',promptId:pB.promptId,choice:poisonId},1900)
        countered=true; log.push({step:'B_COUNTERED'})
        break
      }
      const cmd={type:'resolvePrompt',promptId:pB.promptId}; if((pB.validChoices||[]).length)cmd.choice=pB.validChoices[0]; else cmd.cardInstanceIds=[]
      await act(B,cmd); continue
    }
    if(pA){ log.push({step:'A_WINDOW',kind:pA.kind,choices:(pA.validChoices||[]).slice(0,6)})
      const cmd={type:'resolvePrompt',promptId:pA.promptId}; if((pA.validChoices||[]).length)cmd.choice=pA.validChoices[0]; else cmd.cardInstanceIds=[]
      await act(A,cmd); continue
    }
    break
  }
  await sleep(700)
  if(countered) break
}
await sleepSync(A,1200); await sleepSync(B,600)
log.push({step:'after',countered,detailA:detail(A),detailB:detail(B),followPromptA:(myPrompt(A)?.kind)||null})
fs.writeFileSync(OUT+'/z30-log.json',JSON.stringify(log,null,2))
const keys=['placeA','placeFail','poisonSet','moved','B_WINDOW','B_COUNTERED','after','A_WINDOW','A_noHigh','B_noPoison']
console.log(JSON.stringify(log.filter(e=>keys.includes(e.step)).slice(-12),null,2))
A.ws.close();B.ws.close()