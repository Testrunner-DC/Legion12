import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const api=async(method,path,{token,body,version,key}={})=>{
  const h={Accept:'application/json'}
  if(token)h.Authorization='Bearer '+token
  if(body!==undefined)h['Content-Type']='application/json'
  if(key)h['Idempotency-Key']=key
  if(version!==undefined)h['If-Match']='"'+version+'"'
  const p=body===undefined||version===undefined?body:{...body,expectedVersion:version}
  const r=await fetch(BASE+path,{method,headers:h,body:p===undefined?undefined:JSON.stringify(p)})
  const t=await r.text(); let j=null; try{j=t?JSON.parse(t):null}catch{}
  return {status:r.status,json:j,text:t}
}
const lr=await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:'Admin',password:adminPw})})
const admin=(await lr.json()).token
const page=await api('GET','/api/admin/bugs/query?page=1&pageSize=20&includeDetails=false',{token:admin})
const rows=page.json?.items??page.json?.reports??page.json??[]
const targets=rows.filter(r=>['BUG-20261125-c780d4b8','BUG-20261125-fb70de2b','BUG-20261125-eb53a702'].includes(r.id))
const results=[]
for(const row of targets){
  const detail=await api('GET','/api/admin/bugs/'+row.id,{token:admin})
  const version=detail.json?.platformVersion ?? page.json?.platformVersion ?? detail.json?.version
  const r=await api('PATCH','/api/admin/bugs/'+row.id,{token:admin,key:'verify-'+row.id.slice(-8)+'-'+Date.now().toString(36),version,
    body:{adminNotes:(detail.json?.adminNotes?detail.json.adminNotes+'\n':'')+'测试服 3ffa45d3 已验收通过（2026-11-26），待正式服部署后按合同关单。'}})
  results.push({id:row.id,status:r.status,version})
}
console.log(JSON.stringify({found:targets.length,results,platformVersion:page.json?.platformVersion},null,1).slice(0,1500))
