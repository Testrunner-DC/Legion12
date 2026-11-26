import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const OUT='D:/GPT/Legion12/artifacts/a3-clear-20261126'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const report={startedAt:new Date().toISOString(),checks:[]}
const browser=await chromium.launch()
async function login(name,p){
  const ctx=await browser.newContext({viewport:{width:1280,height:900}})
  const page=await ctx.newPage()
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',p)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:30000})
  return page
}
const body=async p=>(await p.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
const a=await login('Admin',adminPw)
await a.goto(BASE+'/admin/matches/tournaments',{waitUntil:'domcontentloaded'});await a.waitForTimeout(7000)
const before=await a.getByRole('button',{name:'通过',exact:true}).count()
if(before){ await a.getByRole('button',{name:'通过',exact:true}).first().click({force:true}); await a.waitForTimeout(4000) }
const after=await a.getByRole('button',{name:'通过',exact:true}).count()
report.checks.push({step:'admin-approve', before, after})
await a.screenshot({path:OUT+'/a3-7-admin-approved.png'})
const p=await login('彩排03',pw)
await p.goto(BASE+'/battle/tournaments',{waitUntil:'domcontentloaded'});await p.waitForTimeout(5000)
let text=await body(p)
report.checks.push({step:'player-approved', label:(text.match(/赛事资质：(\S+)/)||[])[1]??null,
  hostButton:await p.getByRole('button',{name:'创建赛事'}).count()})
await p.screenshot({path:OUT+'/a3-8-player-approved.png'})
// 赛前调整面板
const row=p.getByRole('button',{name:/\[ACC\] 资质建赛/})
if(await row.count()){ await row.first().click({force:true}); await p.waitForTimeout(4000)
  text=await body(p)
  report.checks.push({step:'schedule-panel', panel:text.includes('赛前调整'), suggestion:/建议 \d+ 轮/.test(text),
    detailOpen:text.includes('赛事详情')||text.includes('瑞士轮')})
  const adopt=p.getByRole('button',{name:'采纳'})
  if(await adopt.count()){ await adopt.first().click({force:true}); await p.waitForTimeout(1200) }
  const reason=p.locator('[data-tournament-schedule] input[placeholder*="理由"]')
  if(await reason.count()) await reason.fill('验收：按实际人数调整轮数')
  const save=p.getByRole('button',{name:'保存赛前调整'})
  if(await save.count()){ await save.first().click({force:true}); await p.waitForTimeout(3500) }
  text=await body(p)
  report.checks[report.checks.length-1].saved=text.includes('赛前调整已保存')||text.includes('已保存')
  await p.screenshot({path:OUT+'/a3-9-schedule-saved.png'})
}
fs.writeFileSync(OUT+'/a3-ui-acceptance2.json',JSON.stringify(report,null,2))
await browser.close()
console.log(JSON.stringify(report,null,1).slice(0,1800))
