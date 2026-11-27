import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const WS='wss://legion-12.com/testrun/ws'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const log=[]
const note=(step,obj)=>log.push({step,...obj})

async function login(name){
  const r=await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:name,password:pw})})
  const j=await r.json()
  const dr=await fetch(BASE+'/api/decks',{headers:{Authorization:'Bearer '+j.token}})
  const decks=await dr.json()
  const list=Array.isArray(decks)?decks:(decks.decks||[])
  return {name,token:j.token,decks:list}
}
function connect(session){
  return new Promise((resolve)=>{
    const ws=new WebSocket(WS)
    const messages=[]
    ws.addEventListener('message',e=>{
      let m=null; try{m=JSON.parse(String(e.data))}catch{}
      if(m) messages.push(m)
    })
    ws.addEventListener('open',()=>{
      ws.send(JSON.stringify({type:'hello',authToken:session.token,capabilities:{requestIds:true,deltaGameState:true,eventWindowDeltaV1:true,presenceDeltaV1:true,presenceOrderReuseV1:true,chatV1:true,gameStateCardFrameV1:true}}))
    })
    setTimeout(()=>resolve({ws,session,messages,send:(o)=>ws.send(JSON.stringify(o)),
      types:()=>messages.map(m=>m.type).slice(-12),
      last:(t)=>[...messages].reverse().find(m=>m.type===t)}),4000)
  })
}
const a=await login('彩排01')
const b=await login('彩排02')
note('decks',{a:a.decks.map(d=>d.name),b:b.decks.map(d=>d.name)})
const deckA=a.decks.find(d=>d.name==='月读实机A')
const deckB=b.decks.find(d=>d.name.includes('月读实机B'))

const A=await connect(a)
note('a-hello-types',{types:A.types()})
A.send({type:'createRoom',options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'all',useCardRestrictions:false}})
await new Promise(r=>setTimeout(r,4000))
const roomMsg=A.last('roomState')||A.last('room')
note('a-room',{types:A.types(),room:roomMsg?JSON.stringify(roomMsg).slice(0,220):null})
const code=(JSON.stringify(roomMsg||{}).match(/"code":"([A-Z0-9]{5,8})"/)||[])[1]||null
note('room-code',{code})

if(deckA) A.send({type:'selectCustomDeck',deck:deckA})
const B=await connect(b)
if(code) B.send({type:'joinRoom',roomCode:code})
await new Promise(r=>setTimeout(r,4000))
if(deckB) B.send({type:'selectCustomDeck',deck:deckB})
await new Promise(r=>setTimeout(r,3000))
A.send({type:'ready',ready:true}); B.send({type:'ready',ready:true})
await new Promise(r=>setTimeout(r,6000))
note('after-ready',{a:A.types(),b:B.types()})
note('a-game',{game:JSON.stringify((A.last('state')||A.last('gameState')||{})).slice(0,300)})
note('b-game',{game:JSON.stringify((B.last('state')||B.last('gameState')||{})).slice(0,300)})
console.log(JSON.stringify(log,null,2))
A.ws.close(); B.ws.close()
