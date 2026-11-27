import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const root='D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/服务端WebSocket/TwelveLegions/Data/'
const c2=JSON.parse(fs.readFileSync(root+'cards.s2.json','utf8'))
const arr=Array.isArray(c2)?c2:(c2.cards||[])
const morales=arr.filter(c=>c.cardType==='morale')
const universalLegions=arr.filter(c=>c.cardType==='legion'&&c.faction==='universal')
const universalTactics=arr.filter(c=>c.cardType==='tactic'&&c.faction==='universal')
console.log('morales='+morales.length+' univLegions='+universalLegions.length+' univTactics='+universalTactics.length)
console.log('moraleIds='+morales.slice(0,12).map(m=>m.id).join(','))
console.log('someLegions='+universalLegions.slice(0,10).map(m=>m.id).join(','))
console.log('someTactics='+universalTactics.slice(0,10).map(m=>m.id).join(','))
