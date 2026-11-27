import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const WS='wss://legion-12.com/testrun/ws'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const sleep=ms=>new Promise(r=>setTimeout(r,ms))
const login=async n=>{
  const j=await (await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:n,password:pw})})).json()
  return j.token
}
async function cleanup(name){
  const token=await login(name)
  const ws=new WebSocket(WS); const box={state:null,room:null,errors:[]}
  ws.addEventListener('message',e=>{let m=null;try{m=JSON.parse(String(e.data))}catch{};if(!m)return
    if(m.type==='gameState')box.state=m; if(m.type==='roomState')box.room=m; if(m.type==='error')box.errors.push(m.message)})
  ws.addEventListener('open',()=>ws.send(JSON.stringify({type:'hello',authToken:token,capabilities:{requestIds:true,eventWindowDeltaV1:true,presenceDeltaV1:true,chatV1:true}})))
  const send=o=>ws.send(JSON.stringify(o))
  await sleep(2500)
  send({type:'syncState',plainFull:true}); await sleep(1200)
  const before=box.state?.state?.matchId||null
  const result={name,matchBefore:before}
  if(before){
    send({type:'gameAction',requestId:'c'+Math.random().toString(36).slice(2),command:{type:'surrender'}}); await sleep(2000)
    send({type:'leaveRoom'}); await sleep(1500)
    send({type:'syncState',plainFull:true}); await sleep(1200)
  }
  result.matchAfter=box.state?.state?.matchId||null
  result.roomAfter=box.room?.roomCode||null
  result.errors=box.errors.slice(-2)
  ws.close()
  return result
}
const out=[await cleanup('彩排01'), await cleanup('彩排02')]
console.log(JSON.stringify(out,null,2))
