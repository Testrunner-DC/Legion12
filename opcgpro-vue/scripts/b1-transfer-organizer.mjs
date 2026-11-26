import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const prep=JSON.parse(fs.readFileSync('D:/GPT/Legion12/artifacts/a3-clear-20261126/b1-prep.json','utf8'))
const id=prep.tournamentId, id07=prep.ids.id07
const api=async(method,path,{token,body,version,key}={})=>{
  const h={Accept:'application/json'}; if(token)h.Authorization='Bearer '+token
  if(key)h['Idempotency-Key']=key
  if(body!==undefined)h['Content-Type']='application/json'
  if(version!==undefined)h['If-Match']='"'+version+'"'
  const p=body===undefined||version===undefined?body:{...body,expectedVersion:version}
  const r=await fetch(BASE+path,{method,headers:h,body:p===undefined?undefined:JSON.stringify(p)})
  const t=await r.text(); let j=null; try{j=t?JSON.parse(t):null}catch{}
  return {status:r.status,json:j,text:t}
}
const login=async(u,p)=>(await api('POST','/api/auth/login',{body:{username:u,password:p}})).json.token
const admin=await login('Admin',adminPw), t07=await login('彩排07',pw)
const idAdmin=(await api('GET','/api/auth/me',{token:admin})).json.id
// 接任者必须是现任主办者的好友：先建立 Admin↔彩排07。
await api('POST','/api/friends/requests',{token:admin,body:{accountId:id07}})
await api('POST','/api/friends/requests/'+idAdmin+'/resolve',{token:t07,body:{accept:true}})
const v=(await api('GET','/api/tournaments/'+id,{token:admin})).json.version
const stamp=Date.now().toString(16)
let view=(await api('GET','/api/tournaments/'+id,{token:admin})).json
let rid = view?.pendingOrganizerTransfer?.id
let reqStatus = 'existing'
if (!rid) {
  const req=await api('POST','/api/tournaments/'+id+'/organizer-transfer',{token:admin,key:'b1-tr-'+stamp,version:view.version,body:{accountId:id07,reason:'B1 验证：主办者身份观战'}})
  reqStatus = req.status
  view=(await api('GET','/api/tournaments/'+id,{token:admin})).json
  rid = view?.pendingOrganizerTransfer?.id
}
const v07=(await api('GET','/api/tournaments/'+id,{token:t07})).json.version
const dec = rid ? await api('POST','/api/tournaments/'+id+'/organizer-transfer/decision',{token:t07,key:'b1-trd-'+stamp,version:v07,body:{requestId:rid,accept:true}}) : {status:0,text:'no-request-id'}
const after=(await api('GET','/api/tournaments/'+id,{token:admin})).json
console.log(JSON.stringify({reqStatus, rid, decStatus:dec.status, decBody:dec.text.slice(0,140), organizer:after?.organizer?.username, referees:after?.referees?.map(r=>r.username)},null,1))
