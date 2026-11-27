import fs from 'node:fs'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const cards=JSON.parse(fs.readFileSync('D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/服务端WebSocket/TwelveLegions/Data/cards.s2.json','utf8'))
const cardsArr=Array.isArray(cards)?cards:(cards.cards||[])
const gaotian=cardsArr.filter(c=>c.faction==='gaotianyuan' || c.faction==='高天原')
const byType=t=>gaotian.filter(c=>c.cardType===t)
const legions=byType('legion'), tactics=byType('tactic')
console.log(JSON.stringify({
  total:cardsArr.length, gaotian:gaotian.length,
  legions:legions.length, tactics:tactics.length,
  sampleLegions:legions.slice(0,8).map(c=>c.id+':'+c.nameZh),
  sampleTactics:tactics.slice(0,8).map(c=>c.id+':'+c.nameZh),
  poison: cardsArr.filter(c=>c.id==='S02-0018').map(c=>({id:c.id,name:c.nameZh,faction:c.faction,type:c.cardType})),
  masters: gaotian.filter(c=>c.cardType==='master').map(c=>c.id+':'+c.nameZh),
},null,2))
