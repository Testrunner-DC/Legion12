import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const login=async n=>{const r=await fetch(BASE+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:n,password:pw})});return (await r.json()).token}
const t=await login('彩排02')
const legions=['S02-0401','S02-0402','S02-0403','S02-0001','S02-0002','S02-0003','S02-0004','S02-0005','S02-0006','S02-0007']
const tactics=['S02-0009','S02-0010','S02-0011','S02-0012','S02-0013','S02-0014','S02-0015','S02-0016','S02-0017']
const ids=[]
for (let round=0; round<3; round++) for (const id of legions) ids.push(id)
ids.push('S02-0018','S02-0018','S02-0018','S02-0018'.slice(0,0)||tactics[0])
const rest=tactics.filter(x=>x!=='S02-0009').slice(0,4)
for (const x of rest) ids.push(x)
const payload={name:'月读实机B(毒药)',masterId:'S02-04M1',cardIds:ids,moraleIds:Array.from({length:8},()=>'S01-04C1'),specialIds:[]}
console.log('cards='+ids.length)
const r=await fetch(BASE+'/api/decks',{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+t},body:JSON.stringify(payload)})
console.log('POST -> '+r.status+' :: '+(await r.text()).slice(0,200))
