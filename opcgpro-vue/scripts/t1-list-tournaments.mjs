import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const lr=await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:'Admin',password:adminPw})})
const admin=(await lr.json()).token
const r=await fetch(BASE+'/api/tournaments/summaries?section=host&page=1&pageSize=20',{headers:{Accept:'application/json',Authorization:'Bearer '+admin}})
const rows=(await r.json()).items ?? []
console.log(JSON.stringify(rows.map(x=>({code:x.code,name:(x.name||'').slice(0,26),status:x.status})),null,1).slice(0,1600))
