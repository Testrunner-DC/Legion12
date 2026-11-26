import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const api=async(method,path,{token,body,version,key}={})=>{
  const h={Accept:'application/json'}; if(token)h.Authorization='Bearer '+token
  if(body!==undefined)h['Content-Type']='application/json'
  if(key)h['Idempotency-Key']=key
  if(version!==undefined)h['If-Match']='"'+version+'"'
  const p=body===undefined||version===undefined?body:{...body,expectedVersion:version}
  const r=await fetch(BASE+path,{method,headers:h,body:p===undefined?undefined:JSON.stringify(p)})
  const t=await r.text(); let j=null; try{j=t?JSON.parse(t):null}catch{}
  return {status:r.status,json:j,text:t}
}
const login=async(u,p)=>(await api('POST','/api/auth/login',{body:{username:u,password:p}})).json.token
const admin=await login('Admin',adminPw)
const list=await api('GET','/api/tournaments?status=in-progress&page=1&pageSize=50',{token:admin})
const rows=Array.isArray(list.json)?list.json:(list.json?.items??list.json?.tournaments??[])
const targets=rows.filter(r=>/\[B1\]|\[A3\]|\[A2b\]/.test(r.name||''))
const results=[]
for (const t of targets.slice(0,8)) {
  const detail=await api('GET','/api/tournaments/'+(t.code||t.id),{token:admin})
  const v=detail.json?.version
  const id=detail.json?.id||t.id
  if(!id||v===undefined){results.push({name:t.name,skipped:'no id/version'});continue}
  const c=await api('POST','/api/tournaments/'+id+'/cancel',{token:admin,key:'clean-'+id.slice(0,8)+'-'+Date.now().toString(36),version:v,body:{reason:'测试服收尾：B1/演练遗留赛事'}})
  results.push({name:(t.name||'').slice(0,40),status:c.status})
}
console.log(JSON.stringify({found:rows.length,targets:targets.length,results},null,1).slice(0,1500))
