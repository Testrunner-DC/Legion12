import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
async function login(name){
  const r=await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:name,password:pw})})
  return (await r.json()).token
}
const legions=['S02-0401','S02-0402','S02-0403','S02-0001','S02-0002','S02-0003','S02-0004','S02-0005','S02-0006','S02-0007']
const tactics=['S02-0009','S02-0010','S02-0011','S02-0012','S02-0013','S02-0014','S02-0015','S02-0016','S02-0017','S02-0018']
function cards(withPoison){
  const ids=[]
  for (let round=0; round<3; round++) for (const id of legions) ids.push(id)
  for (let i=0;i<10;i++) ids.push(withPoison ? (i<4?'S02-0018':tactics[i]) : tactics[i])
  return ids
}
const results={}
for (const [name,withPoison,deckName] of [['彩排01',false,'月读实机A'],['彩排02',true,'月读实机B(毒药)']]){
  const t=await login(name)
  const payload={name:deckName,masterId:'S02-04M1',cardIds:cards(withPoison),moraleIds:Array.from({length:8},()=>'S01-04C1'),specialIds:[]}
  const r=await fetch(BASE+'/api/decks',{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+t},body:JSON.stringify(payload)})
  const body=await r.text()
  results[name]={status:r.status,body:body.slice(0,300)}
}
console.log(JSON.stringify(results,null,2))
