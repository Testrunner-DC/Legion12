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
const PL=(b,i)=>{const s=S(b);return s?.players?.[i]||null}
const myP=b=>{const s=S(b);return s?(s.prompts||[]).find(p=>p.playerIndex===s.you)||null:null}
const sync=async(b,ms=900)=>{b.send({type:'syncState',plainFull:true});await sleep(ms);return S(b)}
const act=async(b,c,ms=1200)=>{b.send({type:'gameAction',requestId:'r'+Math.random().toString(36).slice(2),command:c});await sleep(ms);return sync(b,700)}
const log=[]
const A0=await login('彩排01'), B0=await login('彩排02')
const A=conn(A0.token,'A'), B=conn(B0.token,'B')
await sleep(3000); await sync(A); await sync(B)
if(S(A)||S(B)){await act(A,{type:'surrender'});A.send({type:'leaveRoom'});B.send({type:'leaveRoom'});await sleep(2200);await sync(A);await sync(B)}
A.send({type:'createRoom',options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'none',useCardRestrictions:false}})
await sleep(2400)
const deckA=A0.decks.find(d=>d.name==='月读实机A'), deckB=B0.decks.find(d=>d.name.includes('月读实机B'))
async function ensureDeck(b,deck){for(let i=0;i<6;i++){await sync(b,600);const p=(b.room?.players||[]).find(x=>x.name===b.name);if(p&&p.deckName===deck.name)return true;b.send({type:'selectCustomDeck',deck});await sleep(1100)}return false}
log.push({deckA:await ensureDeck(A,deckA)})
const code=A.room?.roomCode||''
if(code) B.send({type:'joinRoom',roomCode:code}); await sleep(2200)
log.push({deckB:await ensureDeck(B,deckB)})
A.send({type:'ready',ready:true}); B.send({type:'ready',ready:true}); await sleep(4500)
await sync(A); await sync(B)
async function settle(max=60){
  for(let i=0;i<max;i++){
    const s=S(A)||S(B); if(!s) break
    let did=false
    for(const b of [A,B]){
      const p=myP(b); if(!p) continue
      const ch=p.validChoices||[]
      const cmd={type:'resolvePrompt',promptId:p.promptId}
      if(p.kind==='initiative') cmd.choice=ch.includes('first')?'first':ch[0]
      else if(ch.length) cmd.choice=ch[0]
      else cmd.cardInstanceIds=[]
      log.push({step:'auto',kind:p.kind,who:b.name})
      await act(b,cmd); did=true; break
    }
    if(!did){
      if(s.phase==='Mulligan'){
        const aDone=PL(A,0)?.mulliganDone,bDone=PL(B,1)?.mulliganDone
        if(!aDone) await act(A,{type:'mulligan',cardInstanceIds:[]})
        else if(!bDone) await act(B,{type:'mulligan',cardInstanceIds:[]})
        else await sleep(600)
        await sync(A,500); await sync(B,500); continue
      }
      break
    }
  }
}
await settle()
log.push({step:'mainReached',phase:S(A)?.phase,active:S(A)?.activePlayer})

// ===== 关键顺序：先 B 设毒药 → 再 A 同一回合内「出军团 + 立刻前→后位移」 =====
const POISON='S02-0018', HIGH=['S02-0401','S02-0402','S02-0403']
let poisonSet=false, countered=false, movedOk=false
for(let turn=0; turn<45 && !countered; turn++){
  await sync(A,600); await sync(B,600)
  await settle(6)
  const s=S(A); if(!s) break
  if(s.phase!=='Main'){ await sleep(500); continue }
  const active=s.activePlayer
  if(active===1 && !poisonSet){
    const card=(PL(B,1)?.hand||[]).find(c=>c.cardId===POISON)
    if(card){ await act(B,{type:'playCard',cardInstanceId:card.instanceId},1700); poisonSet=true; log.push({step:'poisonSet',id:card.instanceId}) }
    else { await act(B,{type:'endTurn'},1500); const card2=(PL(B,1)?.hand||[]).find(c=>c.cardId===POISON); log.push({step:'bTurnNoPoison'}) ; continue }
  }
  await sync(A,500)
  const s2=S(A)
  if(s2?.phase==='Main' && s2.activePlayer===0 && poisonSet && !movedOk){
    // 1) 先出一张高天原军团（会消耗资源 → 产生休整士气）
    const legion=(PL(A,0)?.hand||[]).find(c=>HIGH.includes(c.cardId))
    let placed=null
    if(legion){
      await act(A,{type:'playCard',cardInstanceId:legion.instanceId,row:0,slot:0,targetPlayerIndex:0},1700)
      placed=PL(A,0)?.field?.[0]?.[0]||null
      log.push({step:'placed',ok:!!placed,card:legion.cardId,moraleTapped:(PL(A,0)?.morale||[]).filter(m=>m.tapped).length})
    }
    if(placed){
      await act(A,{type:'move',cardInstanceId:placed.instanceId,row:1,slot:0},1700)
      movedOk=!!(PL(A,0)?.field?.[1]?.[0]); log.push({step:'moved',ok:movedOk,err:A.errors.slice(-2)})
      // 处理提示并对 B 的响应窗取证
      for(let k=0;k<12;k++){
        const pA=myP(A), pB=myP(B)
        if(pB){
          const ch=pB.validChoices||[]
          log.push({step:'B_WINDOW',kind:pB.kind,choices:ch.slice(0,6),hasPoison:ch.includes(poisonSet?pB.validChoices.find(x=>x.startsWith('p1-')):null)||ch.some(x=>/^p1-cd+$/.test(x))})
          const cmd={type:'resolvePrompt',promptId:pB.promptId}
          if(pB.kind==='order'||(pB.maxChoose||1)>1) cmd.cardInstanceIds=ch.slice(0,pB.maxChoose||1)
          else if(ch.length) cmd.choice=ch[0]
          else cmd.cardInstanceIds=[]
          await act(B,cmd,1400); continue
        }
        if(pA){
          const ch=pA.validChoices||[]
          log.push({step:'A_WINDOW',kind:pA.kind,choices:ch.slice(0,6),morale:(PL(A,0)?.morale||[]).filter(m=>m.tapped).length})
          const cmd={type:'resolvePrompt',promptId:pA.promptId}
          if(pA.kind==='order'||(pA.maxChoose||1)>1) cmd.cardInstanceIds=ch.slice(0,pA.maxChoose||1)
          else if(ch.length){
            // 选一张休整士气
            const tapped=(PL(A,0)?.morale||[]).filter(m=>m.tapped).map(m=>m.instanceId)
            cmd.choice=ch.find(x=>tapped.includes(x))||ch[0]
          } else cmd.cardInstanceIds=[]
          await act(A,cmd,1400); continue
        }
        break
      }
      // 关键取证：轮询 B 端响应窗 10 秒
      for(let w=0; w<20; w++){
        await sync(B,600); await sync(A,400)
        const pb=myP(B)
        if(pb){
          const ch=pb.validChoices||[]
          log.push({step:'B_WINDOW_CAPTURE',kind:pb.kind,choices:ch.slice(0,8),hasPoison:ch.includes('p1-c29')})
          const cmd={type:'resolvePrompt',promptId:pb.promptId}
          if(pb.kind==='order'||(pb.maxChoose||1)>1) cmd.cardInstanceIds=ch.slice(0,pb.maxChoose||1)
          else if(ch.includes('p1-c29')) { cmd.choice='p1-c29'; log.push({step:'COUNTER_CHOSEN'}) }
          else if(ch.length) cmd.choice=ch[0]
          else cmd.cardInstanceIds=[]
          await act(B,cmd,1500); continue
        }
        const pa=myP(A)
        if(pa){
          const ch=pa.validChoices||[]
          log.push({step:'A_WINDOW2',kind:pa.kind,choices:ch.slice(0,8)})
          const cmd={type:'resolvePrompt',promptId:pa.promptId}
          if(pa.kind==='order'||(pa.maxChoose||1)>1) cmd.cardInstanceIds=ch.slice(0,pa.maxChoose||1)
          else if(ch.length){
            const tapped=(PL(A,0)?.morale||[]).filter(m=>m.tapped).map(m=>m.instanceId)
            cmd.choice=ch.find(x=>tapped.includes(x))||ch[0]
          } else cmd.cardInstanceIds=[]
          await act(A,cmd,1500); continue
        }
        await sleep(400)
      }
      const pB2=myP(B)
      log.push({step:'afterMove',bPrompt:pB2?{k:pB2.kind,choices:(pB2.validChoices||[]).slice(0,6)}:null,
        moraleTapped:(PL(A,0)?.morale||[]).filter(m=>m.tapped).length,
        moraleAll:(PL(A,0)?.morale||[]).map(m=>({id:m.instanceId,t:m.tapped}))})
      break
    }
  } else if(s2?.phase==='Main' && s2.activePlayer===0){
    await act(A,{type:'endTurn'},1500)
  } else if(s2?.phase==='Main'){
    await act(B,{type:'endTurn'},1500)
  }
  await sleep(400)
}
fs.writeFileSync(OUT+'/z37-log.json',JSON.stringify(log,null,2))
console.log(JSON.stringify(log.filter(e=>['poisonSet','placed','moved','B_WINDOW','A_WINDOW','afterMove','mainReached','bTurnNoPoison'].includes(e.step)).slice(-14),null,2))
A.ws.close();B.ws.close()