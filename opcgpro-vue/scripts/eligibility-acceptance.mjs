import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const out={steps:[]}
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
const login=async(u,p)=>(await api('POST','/api/auth/login',{body:{username:u,password:p}})).json?.token
const admin=await login('Admin',adminPw), p1=await login('彩排01',pw), p2=await login('彩排02',pw)
const me=async t=>(await api('GET','/api/auth/me',{token:t})).json
const id1=(await me(p1)).id, id2=(await me(p2)).id
const stamp=Date.now().toString(36)
const createBody=(name)=>({ tournament:{ name, format:'swiss', visibility:'public', maxPlayers:16,
  startAt:new Date(Date.now()+3600000).toISOString(), ruleset:'official', description:'验收',
  deckVisibility:'after', disasterMode:'none', banList:'', roundMinutes:50, checkInMinutes:5,
  swissRounds:4, registrationVisibility:'public', lateGraceMinutes:5, operationMode:'managed' } })
const pv=async t=>(await api('GET','/api/tournaments/summaries?section=host&page=1&pageSize=1',{token:t})).json.platformVersion
// a) 未获资质建赛被拒
const denied=await api('POST','/api/tournaments',{token:p1,key:'acc-denied-'+stamp,version:await pv(p1),body:createBody('[ACC] 未授权建赛 '+stamp)})
out.steps.push({step:'a-unqualified-create', status:denied.status, code:denied.json?.code??null, message:denied.json?.message??''})
// b) 申请 → 过审 → 建赛成功
const applied=await api('POST','/api/tournament-eligibility/applications',{token:p1,body:{reason:'验收：本人计划每周办一场瑞士轮赛事，预计 8–16 人。'}})
const pending=await api('GET','/api/admin/tournament-eligibility',{token:admin})
const approved=await api('POST','/api/admin/tournament-eligibility/'+id1+'/review',{token:admin,body:{approve:true,note:'验收通过'}})
const created=await api('POST','/api/tournaments',{token:p1,key:'acc-create-'+stamp,version:await pv(p1),body:createBody('[ACC] 资质建赛 '+stamp)})
const tId=(created.json?.tournament||created.json)?.id
out.steps.push({step:'b-apply-approve-create', apply:applied.status, pendingCount:(pending.json||[]).length,
  approve:approved.status, create:created.status, tournamentId:tId})
// d) 赛前改轮数（含建议值）
if(tId){
  const suggestion=await api('GET','/api/tournaments/'+tId+'/schedule-suggestion',{token:admin})
  const detail=(await api('GET','/api/tournaments/'+tId,{token:admin})).json
  const updated=await api('PUT','/api/tournaments/'+tId+'/schedule',{token:p1,key:'acc-sched-'+stamp,version:detail.version,
    body:{maxPlayers:8, swissRounds:suggestion.json?.suggestedSwissRounds??3, reason:'验收：实际人数不足，调整轮数'}})
  out.steps.push({step:'d-schedule-adjust', suggestion:suggestion.json, put:updated.status,
    maxPlayers:updated.json?.maxPlayers, swissRounds:updated.json?.swissRounds})
}
// c) 账号页直接授予后建赛成功
const granted=await api('POST','/api/admin/tournament-eligibility/'+id2+'/grant',{token:admin,body:{note:'验收：管理员直接授予'}})
const created2=await api('POST','/api/tournaments',{token:p2,key:'acc-grant-'+stamp,version:await pv(p2),body:createBody('[ACC] 直授建赛 '+stamp)})
const self1=(await api('GET','/api/tournament-eligibility',{token:p1})).json
const self2=(await api('GET','/api/tournament-eligibility',{token:p2})).json
out.steps.push({step:'c-admin-grant-create', grant:granted.status, create:created2.status,
  el1:self1?.status, eff1:self1?.effective, el2:self2?.status, eff2:self2?.effective})
fs.writeFileSync('D:/GPT/Legion12/artifacts/a3-clear-20261126/eligibility-acceptance.json', JSON.stringify(out,null,2))
console.log(JSON.stringify(out,null,1).slice(0,2200))
