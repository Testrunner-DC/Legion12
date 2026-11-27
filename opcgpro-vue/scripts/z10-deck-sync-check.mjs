import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const browser=await chromium.launch()
const ctx=await browser.newContext({viewport:{width:1440,height:1000}})
const page=await ctx.newPage()
await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
await page.fill('form input[autocomplete=username]','彩排01')
await page.fill('form input[type=password]',pw)
await page.click('form button[type=submit]')
await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:40000})
const serverDecks=await page.evaluate(async()=>{
  const token=JSON.parse(localStorage.getItem('l12-auth')||'{}').token||''
  const r=await fetch('/testrun/api/decks',{headers:token?{Authorization:'Bearer '+token}:{}})
  const t=await r.text(); let j=null; try{j=JSON.parse(t)}catch{}
  return {status:r.status,count:Array.isArray(j)?j.length:(j?.decks||[]).length,names:(Array.isArray(j)?j:(j?.decks||[])).map(d=>d.name)}
})
await page.goto(BASE+'/battle',{waitUntil:'domcontentloaded'})
await page.waitForTimeout(14000)
const body=(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
console.log(JSON.stringify({serverDecks,uiSaysNoDeck:body.includes('当前没有已保存牌库'),snippet:body.slice(0,220)},null,2))
await browser.close()
