import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const playerPw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
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
const out={}
for(const [key,name,pw,path] of [['player','彩排03',playerPw,'/battle/tournaments'],['admin','Admin',adminPw,'/admin/tournaments']]){
  const page=await login(name,pw)
  await page.goto(BASE+path,{waitUntil:'domcontentloaded'}); await page.waitForTimeout(6000)
  out[key]={url:page.url(), text:(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ').slice(0,420),
    buttons:await page.evaluate(()=>Array.from(document.querySelectorAll('button')).map(b=>(b.innerText||'').replace(/\s+/g,' ').trim()).filter(Boolean).slice(0,18))}
  await page.screenshot({path:'D:/GPT/Legion12/artifacts/a3-clear-20261126/diag-'+key+'.png'})
}
await browser.close()
console.log(JSON.stringify(out,null,1).slice(0,3000))
