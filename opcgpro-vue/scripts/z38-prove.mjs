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
function conn(tok,name){
  const ws=new WebSocket(WS); const b={name,ws,state:null,room:null,errors:[]}
  ws.addEventListener('message',e=>{let m=null;try{m=JSON.parse(String(e.data))}catch{};if(!m)return
    if(m.type==='gameState')b.state=m; if(m.type==='roomState')b.room=m; if(m.type==='error')b.errors.push(m.message)})
  ws.addEventListener('open',()=>ws.send(JSON.stringify({type:'hello',authToken:tok,capabilities:{requestIds:true,eventWindowDeltaV1:true,presenceDeltaV1:true,chatV1:true}})))
  b.send=o=>ws.send(JSON.stringify(o)); return b
}
const S=b=>b.state?.state||null
const PL=(b,i)=>S(b)?.players?.[i]||null
const myP=b=>{const s=S(b);return s?(s.prompts||[]).find(p=>p.playerIndex===s.you)||null:null}
const sync=async(b,ms=800)=>{b.send({type:'syncState',plainFull:true});await sleep(ms);return S(b)}
const act=async(b,c,ms=1100)=>{b.send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command:c});await sleep(ms);return sync(b,600)}
const log=[]
const A0=await login('彩排01'), B0=await login('彩排02')
const deckA=A0.decks.find(d=>d.name==='月读实机A'), deckB=B0.decks.find(d=>d.name.includes('月读实机B'))
async function newMatch(){
  const A=conn(A0.token,'A'), B=conn(B0.token,'B')
  await sleep(2600); await sync(A); await sync(B)
  if(S(A)||S(B)){await act(A,{type:'surrender'});A.send({type:'leaveRoom'});B.send({type:'leaveRoom'});await sleep(1800);await sync(A);await sync(B)}
  A.send({type:'createRoom',options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'none',useCardRestrictions:false}})
  await sleep(2200)
  for(let i=0;i<6;i++){await sync(A,500);const p=(A.room?.players||[]).find(x=>x.name==='彩排01');if(p&&p.deckName===deckA.name)break;A.send({type:'selectCustomDeck',deck:deckA});await sleep(1000)}
  const code=A.room?.roomCode||''; if(code) B.send({type:'joinRoom',roomCode:code}); await sleep(2000)
  for(let i=0;i<6;i++){await sync(B,500);const p=(B.room?.players||[]).find(x=>x.name==='彩排02');if(p&&p.deckName===deckB.name)break;B.send({type:'selectCustomDeck',deck:deckB});await sleep(1000)}
  A.send({type:'ready',ready:true}); B.send({type:'ready',ready:true}); await sleep(4000)
  await sync(A); await sync(B)
  return {A,B}
}
async function settle(A,B,max=50){
  for(let i=0;i<max;i++){
    let did=false
    for(const b of [A,B]){
      const p=myP(b); if(!p) continue
      const ch=p.validChoices||[]; const cmd={type:'resolvePrompt',promptId:p.promptId}
      if(p.kind==='initiative') cmd.choice=ch.includes('first')?'first':ch[0]
      else if(ch.length) cmd.choice=ch[0]; else cmd.cardInstanceIds=[]
      await act(b,cmd); did=true; break
    }
    if(!did){
      const s=S(A)||S(B)
      if(s?.phase==='Mulligan'){
        const aD=PL(A,0)?.mulliganDone,bD=PL(B,1)?.mulliganDone
        if(!aD) await act(A,{type:'mulligan',cardInstanceIds:[]})
        else if(!bD) await act(B,{type:'mulligan',cardInstanceIds:[]})
        else await sleep(500)
        await sync(A,400); await sync(B,400); continue
      }
      break
    }
  }
}
const POISON='S02-0018', HIGH=['S02-0401','S02-0402','S02-0403']
let captured=null
for(let attempt=1; attempt<=3 && !captured; attempt++){
  const {A,B}=await newMatch()
  await settle(A,B)
  log.push({attempt,step:'opened',phase:S(A)?.phase,active:S(A)?.activePlayer})
  let poisonId=null, placed=null, done=false
  for(let turn=0; turn<40 && !done; turn++){
    await sync(A,500); await sync(B,500); await settle(A,B,4)
    const s=S(A); if(!s) break
    if(s.phase!=='Main'){ await sleep(400); continue }
    if(s.activePlayer===1){
      if(!poisonId){ const c=(PL(B,1)?.hand||[]).find(x=>x.cardId===POISON)
        if(c){ await act(B,{type:'playCard',cardInstanceId:c.instanceId},1500); poisonId=c.instanceId; log.push({attempt,step:'poisonSet',id:poisonId}) } }
      await act(B,{type:'endTurn'},1300); continue
    }
    // A 回合
    const spendable=(PL(A,0)?.morale||[]).filter(m=>!m.tapped).length
    if(!poisonId){ await act(A,{type:'endTurn'},1300); continue }
    if(spendable<1){ log.push({attempt,step:'noResource'}); await act(A,{type:'endTurn'},1300); continue }
    const legion=(PL(A,0)?.hand||[]).find(c=>HIGH.includes(c.cardId))
    if(!legion){ log.push({attempt,step:'noLegion'}); await act(A,{type:'endTurn'},1300); continue }
    await act(A,{type:'playCard',cardInstanceId:legion.instanceId,row:0,slot:0,targetPlayerIndex:0},1600)
    placed=PL(A,0)?.field?.[0]?.[0]||null
    const tappedAfter=(PL(A,0)?.morale||[]).filter(m=>m.tapped).length
    log.push({attempt,step:'placed',ok:!!placed,card:legion.cardId,tapped:tappedAfter})
    if(!placed){ await act(A,{type:'endTurn'},1300); continue }
    await act(A,{type:'move',cardInstanceId:placed.instanceId,row:1,slot:0},1600)
    log.push({attempt,step:'moved',ok:!!(PL(A,0)?.field?.[1]?.[0])})
    // 长轮询：捕获 B 的响应窗
    for(let w=0; w<25; w++){
      await sync(B,600); await sync(A,400)
      const pb=myP(B)
      if(pb){
        const ch=pb.validChoices||[]
        log.push({attempt,step:'B_WINDOW',kind:pb.kind,choices:ch.slice(0,8),hasPoison:!!poisonId&&ch.includes(poisonId)})
        const cmd={type:'resolvePrompt',promptId:pb.promptId}
        if(poisonId&&ch.includes(poisonId)){ cmd.choice=poisonId; await act(B,cmd,1600); log.push({attempt,step:'COUNTERED',id:poisonId})
          await sync(A,900)
          log.push({attempt,step:'afterCounter',moraleAll:(PL(A,0)?.morale||[]).map(m=>({id:m.instanceId,t:m.tapped})),nextPromptA:myP(A)?.kind||null})
          captured=true; done=true; break }
        if(pb.kind==='order'||(pb.maxChoose||1)>1) cmd.cardInstanceIds=ch.slice(0,pb.maxChoose||1)
        else if(ch.length) cmd.choice=ch[0]; else cmd.cardInstanceIds=[]
        await act(B,cmd,1400); continue
      }
      const pa=myP(A)
      if(pa){
        const ch=pa.validChoices||[]
        log.push({attempt,step:'A_WINDOW',kind:pa.kind,choices:ch.slice(0,8)})
        const cmd={type:'resolvePrompt',promptId:pa.promptId}
        if(pa.kind==='order'||(pa.maxChoice||pa.maxChoose||1)>1) cmd.cardInstanceIds=ch.slice(0,pa.maxChoose||1)
        else if(ch.length){ const tapped=(PL(A,0)?.morale||[]).filter(m=>m.tapped).map(m=>m.instanceId); cmd.choice=ch.find(x=>tapped.includes(x))||ch[0] }
        else cmd.cardInstanceIds=[]
        await act(A,cmd,1400); continue
      }
      await sleep(300)
    }
    if(!done) await act(A,{type:'endTurn'},1300)
  }
  A.ws.close(); B.ws.close(); await sleep(1500)
}
fs.writeFileSync(OUT+'/z38-log.json',JSON.stringify(log,null,2))
console.log(JSON.stringify(log.filter(e=>['poisonSet','placed','moved','B_WINDOW','COUNTERED','afterCounter','A_WINDOW','noResource'].includes(e.step)).slice(-16),null,2))
