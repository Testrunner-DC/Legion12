import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
async function api(path, opts={}, token){
  const r=await fetch(BASE+path,{...opts,headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{}),...(opts.headers||{})}})
  const text=await r.text()
  let json=null; try{json=JSON.parse(text)}catch{}
  return {status:r.status,json,text}
}
const out={}
for (const name of ['彩排01','彩排02']){
  const login=await api('/api/auth/login',{method:'POST',body:JSON.stringify({username:name,password:pw})})
  if(!login.json?.token){ out[name]={loginStatus:login.status,body:login.text.slice(0,120)}; continue }
  const decks=await api('/api/decks',{},login.json.token)
  const list=Array.isArray(decks.json)?decks.json:(decks.json?.decks||[])
  out[name]={token:login.json.token.slice(0,8)+'…',decks:(list||[]).map(d=>({id:d.id,name:d.name,masterId:d.masterId,cards:(d.cardIds||d.cards||[]).length}))}
}
console.log(JSON.stringify(out,null,2))
