import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
const OUT='D:/GPT/Legion12/artifacts/a3-clear-20261126'
const here=new URL('.', import.meta.url).pathname.replace(/^\//,'')
const run=(name)=>{ execFileSync(process.execPath,[here+name],{stdio:'inherit'}) }
// 一次跑完：建赛事（含裁判）→ 转主办者 → 五种身份的受众矩阵
run('b1-tournament-prep.mjs')
run('b1-transfer-organizer.mjs')
run('b1b-referee-audience.mjs')
const report=JSON.parse(fs.readFileSync(OUT+'/b1b-report.json','utf8'))
const tabOf=name=>(report.identities.find(i=>i.name===name)||{}).hasTableTab
const expected={
  '彩排01':1, '彩排02':1, '彩排08':1, '彩排07':1, '彩排03':0,
}
const tabs=Object.fromEntries(Object.entries(expected).map(([n,e])=>[n,tabOf(n)]))
const tabsOk=Object.entries(expected).every(([n,e])=>tabs[n]===e)
const msg=report.messages||{}
const msgOk=msg.playerTo?.b===true && msg.playerTo?.organizer===true && msg.playerTo?.referee===true
  && msg.playerTo?.ordinary===false && msg.refereeTo?.players===true && msg.refereeTo?.organizer===true
  && msg.organizerTo?.referee===true
const summary={ok: tabsOk && msgOk, code:report.code, tabs, messages:msg}
fs.writeFileSync(OUT+'/b3-audience-summary.json', JSON.stringify(summary,null,2))
console.log(JSON.stringify(summary,null,1))
if(!summary.ok) process.exitCode=1
