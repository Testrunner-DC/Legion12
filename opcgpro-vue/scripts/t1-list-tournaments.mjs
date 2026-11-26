import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const get=async(path,token)=>{const r=await fetch(BASE+path,{headers:{Accept:'application/json',Authorization:'Bearer '+token}});const t=await r.text();let j=null;try{j=JSON.parse(t)}catch{};return {status:r.status,json:j,text:t}}
const lr=await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:'Admin',password:adminPw})})
const admin=(await lr.json()).token
const list=await get('/api/tournaments/summaries?section=host&page=1&pageSize=20',admin)
const rows = Array.isArray(list.json) ? list.json : (list.json?.items ?? list.json?.tournaments ?? list.json?.summaries ?? [])
console.log(JSON.stringify({status:list.status, keys: list.json && !Array.isArray(list.json) ? Object.keys(list.json) : 'array', count: rows.length,
  sample: rows.slice(0,12).map(r=>({code:r.code,name:(r.name||'').slice(0,34),status:r.status,version:r.version,tournamentId:r.id||r.tournamentId}))},null,1).slice(0,1800))
