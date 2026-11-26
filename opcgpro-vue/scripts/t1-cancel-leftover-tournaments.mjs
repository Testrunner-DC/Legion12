import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const lr=await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:'Admin',password:adminPw})})
const admin=(await lr.json()).token
const listR=await fetch(BASE+'/api/tournaments/summaries?section=discover&page=1&pageSize=50',{headers:{Accept:'application/json',Authorization:'Bearer '+admin}})
const rows=(await listR.json()).items ?? []
const todo=rows.filter(r=>/\[B1\]|\[A3\]|\[A2b\]|\[ACC\]|\[彩排\]/.test(r.name||'') && !['canceled','completed'].includes(r.status))
const done=[]
for(const r of todo){
  const tid=r.id||r.tournamentId
  const key='clean-'+tid.slice(0,8)+'-'+Date.now().toString(36)
  const c=await fetch(BASE+'/api/tournaments/'+tid+'/cancel',{method:'POST',
    headers:{Accept:'application/json',Authorization:'Bearer '+admin,'Content-Type':'application/json','Idempotency-Key':key,'If-Match':'"'+r.version+'"'},
    body:JSON.stringify({reason:'测试服收尾：演练遗留赛事',expectedVersion:r.version})})
  done.push({code:r.code,status:r.status,http:c.status})
}
console.log(JSON.stringify({candidates:todo.length,done},null,1).slice(0,1200))
