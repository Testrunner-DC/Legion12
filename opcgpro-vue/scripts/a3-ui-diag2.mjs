import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
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
const out={}
const p=await login('彩排03',pw)
const responses=[]
p.on('response', async r=>{ if(r.url().includes('tournament-eligibility')) responses.push({url:r.url().replace(BASE,''),status:r.status()}) })
await p.goto(BASE+'/battle/tournaments',{waitUntil:'domcontentloaded'});await p.waitForTimeout(5000)
out.before=(await p.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ').match(/赛事资质：\S+/)?.[0]
const ta=p.locator('[data-tournament-eligibility] textarea')
out.textareas=await ta.count()
if(await ta.count()){ await ta.fill('验收：计划每月办一场瑞士轮赛事，预计 8 人参加。') }
const btn=p.locator('[data-tournament-eligibility] button')
out.buttons=await btn.count()
if(await btn.count()){ await btn.first().click({force:true}) }
await p.waitForTimeout(4000)
out.responses=responses
out.after=(await p.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ').match(/赛事资质：\S+/)?.[0]
out.panel=(await p.evaluate(()=>document.querySelector('[data-tournament-eligibility]')?.innerText||'' )).replace(/\s+/g,' ').slice(0,160)
const a=await login('Admin',adminPw)
await a.goto(BASE+'/admin/matches/tournaments',{waitUntil:'domcontentloaded'});await a.waitForTimeout(7000)
out.adminText=(await a.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ').slice(0,300)
out.adminHasPending=(await a.evaluate(()=>document.body.innerText)).includes('待审赛事资质')
out.adminButtons=await a.evaluate(()=>Array.from(document.querySelectorAll('button')).map(b=>(b.innerText||'').replace(/\s+/g,' ').trim()).filter(Boolean).slice(0,12))
await a.screenshot({path:'D:/GPT/Legion12/artifacts/a3-clear-20261126/diag2-admin.png'})
await browser.close()
console.log(JSON.stringify(out,null,1).slice(0,2600))
