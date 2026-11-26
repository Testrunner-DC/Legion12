import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const prep=JSON.parse(fs.readFileSync('D:/GPT/Legion12/artifacts/a3-clear-20261126/b1-prep.json','utf8'))
const CODE=prep.code
const browser=await chromium.launch()
const out={code:CODE}
for(const name of ['彩排08','彩排07']){
  const ctx=await browser.newContext({viewport:{width:1280,height:900}})
  const page=await ctx.newPage()
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',pw)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:30000})
  await page.goto(BASE+'/battle/tournaments',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(5000)
  const row=page.getByRole('button',{name:new RegExp(CODE)})
  out[name+'-rowFound']=await row.count()
  if(await row.count()){ await row.first().click({force:true}); await page.waitForTimeout(4000) }
  out[name+'-url']=page.url()
  // 尝试切到赛程页签
  const tab=page.getByRole('button',{name:'赛程',exact:true})
  if(await tab.count()){ await tab.first().click({force:true}); await page.waitForTimeout(2500) }
  out[name+'-buttons']=await page.evaluate(()=>Array.from(document.querySelectorAll('button')).map(b=>({t:(b.innerText||'').replace(/\s+/g,' ').trim().slice(0,24),d:b.disabled})).filter(b=>b.t).slice(0,20))
  out[name+'-text']=(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ').slice(0,260)
  await page.screenshot({path:'D:/GPT/Legion12/artifacts/a3-clear-20261126/b3-diag-'+name+'.png'})
  await ctx.close()
}
await browser.close()
console.log(JSON.stringify(out,null,1).slice(0,2600))
