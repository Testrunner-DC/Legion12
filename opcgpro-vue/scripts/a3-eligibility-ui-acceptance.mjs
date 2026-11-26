import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const OUT='D:/GPT/Legion12/artifacts/a3-clear-20261126'
const playerPw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const report={startedAt:new Date().toISOString(),checks:[]}
const browser=await chromium.launch()
async function login(name,pw){
  const ctx=await browser.newContext({viewport:{width:1280,height:900}})
  const page=await ctx.newPage()
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',pw)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:30000})
  return page
}
const text=async page=>(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
const click=async(page,name,exact=false)=>{const b=page.getByRole('button',{name,exact});if(!(await b.count()))return false;try{await b.first().click({force:true});return true}catch{return false}}

// 1) 无资质玩家：赛事中心显示「尚未申请」、无「举办赛事」、有申请入口
const p=await login('彩排03',playerPw)
await p.goto(BASE+'/battle/tournaments',{waitUntil:'domcontentloaded'});await p.waitForTimeout(5000)
let body=await text(p)
report.checks.push({step:'player-eligibility-panel',
  hasPanel:body.includes('赛事资质'), label:body.includes('尚未申请')?'尚未申请':(body.includes('审核中')?'审核中':(body.includes('已具备')?'已具备':'未知')),
  applyButton:await p.getByRole('button',{name:'申请赛事资格'}).count(),
  hostButton:await p.getByRole('button',{name:'举办赛事'}).count()})
await p.screenshot({path:OUT+'/a3-1-player-eligibility.png'})
// 提交申请
const ta=p.locator('textarea').first()
if(await ta.count()){ await ta.fill('验收：计划每月办一场瑞士轮赛事，预计 8 人参加。') }
report.checks.push({step:'player-apply', clicked: await click(p,'申请赛事资格')})
await p.waitForTimeout(3500)
report.checks[report.checks.length-1].after = (await text(p)).includes('审核中')
await p.screenshot({path:OUT+'/a3-2-player-applied.png'})

// 2) 管理员：待审列表 + 通过
const a=await login('Admin',adminPw)
await a.goto(BASE+'/admin/matches/tournaments',{waitUntil:'domcontentloaded'});await a.waitForTimeout(7000)
body=await text(a)
report.checks.push({step:'admin-pending-list', hasSection:body.includes('待审赛事资质'),
  passButtons:await a.getByRole('button',{name:'通过'}).count()})
await a.screenshot({path:OUT+'/a3-3-admin-pending.png'})
if(await a.getByRole('button',{name:'通过'}).count()){ await a.getByRole('button',{name:'通过'}).first().click({force:true}); await a.waitForTimeout(3500) }
// 3) 玩家刷新后应为已具备
await p.reload({waitUntil:'domcontentloaded'});await p.waitForTimeout(5000)
body=await text(p)
report.checks.push({step:'player-after-approval', approved:body.includes('已具备'),
  hostButton:await p.getByRole('button',{name:'举办赛事'}).count()})
await p.screenshot({path:OUT+'/a3-4-player-approved.png'})
// 4) 账号页直接授予/撤销按钮
await a.goto(BASE+'/admin/users/accounts',{waitUntil:'domcontentloaded'});await a.waitForTimeout(6000)
report.checks.push({step:'admin-accounts-grant', grantButtons:await a.getByRole('button',{name:'授予赛事资质'}).count(),
  revokeButtons:await a.getByRole('button',{name:'撤销赛事资质'}).count()})
await a.screenshot({path:OUT+'/a3-5-admin-accounts.png'})
// 5) 赛前调整面板（用彩排01 已建赛事）
await p.goto(BASE+'/battle/tournaments',{waitUntil:'domcontentloaded'});await p.waitForTimeout(5000)
const row=p.getByRole('button',{name:/\[ACC\] 资质建赛/});
report.checks.push({step:'schedule-panel', rowFound:await row.count()})
if(await row.count()){ await row.first().click({force:true}); await p.waitForTimeout(3000)
  await click(p,'查看赛事'); await p.waitForTimeout(3500)
  body=await text(p)
  report.checks[report.checks.length-1].panel = body.includes('赛前调整')
  report.checks[report.checks.length-1].suggestion = /建议 \d+ 轮/.test(body)
  await p.screenshot({path:OUT+'/a3-6-schedule-panel.png'})
}
fs.writeFileSync(OUT+'/a3-ui-acceptance.json',JSON.stringify(report,null,2))
await browser.close()
console.log(JSON.stringify(report,null,1).slice(0,2000))
