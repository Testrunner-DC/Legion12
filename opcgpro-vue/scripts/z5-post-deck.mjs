import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
async function token(name){
  const r=await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:name,password:pw})})
  return (await r.json()).token
}
const t=await token('彩排01')
const get=async p=>{const r=await fetch(BASE+p,{headers:{Authorization:'Bearer '+t}});return {status:r.status,body:await r.text()}}
const decks=await get('/api/decks')
const parsed=JSON.parse(decks.body)
const first=Array.isArray(parsed)?parsed[0]:(parsed.decks||[])[0]
console.log('S1 deck shape: '+JSON.stringify({name:first.name,masterId:first.masterId,cardIds:(first.cardIds||[]).length,moraleIds:(first.moraleIds||[]).slice(0,6),specialIds:(first.specialIds||[]).slice(0,6),revision:first.revision}))

const legions=['S02-0401','S02-0402','S02-0403','S02-0001','S02-0002','S02-0003','S02-0004','S02-0005','S02-0006','S02-0007']
const tactics=['S02-0009','S02-0010','S02-0011','S02-0012','S02-0013','S02-0014','S02-0015','S02-0016','S02-0017','S02-0018','S02-0405','S02-0406']
const cardIds=[]
for (let round=0; round<3; round++) for (const id of legions) cardIds.push(id)
for (let i=0;i<10;i++) cardIds.push(tactics[i])
const moraleIds=Array.from({length:8},()=>'S02-04C1')
const payload={name:'月读实机 20261127',masterId:'S02-04M1',cardIds,moraleIds,specialIds:[]}
const post=await fetch(BASE+'/api/decks',{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+t},body:JSON.stringify(payload)})
console.log('POST /api/decks -> '+post.status+' :: '+(await post.text()).slice(0,400))
