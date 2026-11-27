import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const login=await (await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:'彩排01',password:pw})})).json()
const token=login.token
const get=async p=>{const r=await fetch(BASE+p,{headers:{Authorization:'Bearer '+token}});const t=await r.text();let j=null;try{j=JSON.parse(t)}catch{};return {status:r.status,j,head:t.slice(0,160)}}
for (const p of ['/api/decks/presets','/api/catalog/presets','/api/l12/presets','/api/cards?pageSize=1']){
  const res=await get(p)
  let summary=res.head
  if(res.j){
    const arr=Array.isArray(res.j)?res.j:(res.j.items||res.j.presets||res.j.decks||[])
    if(Array.isArray(arr)&&arr.length) summary=arr.slice(0,12).map(d=>d.name+'/'+d.masterId+'/'+((d.cardIds||[]).length)).join(' | ')
  }
  console.log(p+' -> '+res.status+' :: '+summary)
}
