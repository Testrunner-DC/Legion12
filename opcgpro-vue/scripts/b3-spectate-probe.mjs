import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const browser=await chromium.launch()
const ctx=await browser.newContext({viewport:{width:1280,height:900}})
const page=await ctx.newPage()
const log=[]
page.on('console',m=>{ if(m.type()==='error') log.push('console:'+String(m.text()).slice(0,160)) })
page.on('response',r=>{ const u=r.url().replace(BASE,''); if(u.includes('spectate')||u.includes('/ws')) log.push('resp:'+u.slice(0,80)+' '+r.status()) })
page.on('websocket',ws=>{ ws.on('framesent',f=>{const t=String(f.payload); if(t.includes('spectate')) log.push('ws:out:'+t.slice(0,120))}); ws.on('framereceived',f=>{const t=String(f.payload); if(t.includes('spectate')||t.includes('error')) log.push('ws:in:'+t.slice(0,120))}) })
await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
await page.fill('form input[autocomplete=username]','彩排08')
await page.fill('form input[type=password]',pw)
await page.click('form button[type=submit]')
await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:30000})
await page.goto(BASE+'/battle/tournaments/J7WHPS',{waitUntil:'domcontentloaded'})
await page.waitForTimeout(6000)
const tab=page.getByRole('button',{name:'赛程',exact:true}); if(await tab.count()) await tab.first().click({force:true})
await page.waitForTimeout(2500)
const btn=page.getByRole('button',{name:'观战',exact:true})
const before=page.url()
if(await btn.count()){ await btn.first().click({force:true}) }
await page.waitForTimeout(6000)
console.log(JSON.stringify({before, after:page.url(), log:log.slice(0,10), text:(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ').slice(0,160)},null,1).slice(0,1800))
await browser.close()
