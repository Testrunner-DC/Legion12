import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const browser=await chromium.launch()
const ctx=await browser.newContext({viewport:{width:1440,height:1000}})
const page=await ctx.newPage()
const out={}
await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
await page.fill('form input[autocomplete=username]','彩排01')
await page.fill('form input[type=password]',pw)
await page.click('form button[type=submit]')
await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:40000})

for (const route of ['/decks','/deck','/battle/decks']){
  await page.goto(BASE+route,{waitUntil:'domcontentloaded'})
  await page.waitForTimeout(9000)
  const body=(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
  out[route]={url:page.url(),hasDeckName:body.includes('月读实机A'),snippet:body.slice(0,260)}
}
// 再回大厅看 ACTIVE DECK
await page.goto(BASE+'/battle',{waitUntil:'domcontentloaded'})
await page.waitForTimeout(12000)
const battle=(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
out['/battle']={hasDeckName:battle.includes('月读实机A'),noDeck:battle.includes('当前没有已保存牌库'),snippet:battle.slice(0,300)}
console.log(JSON.stringify(out,null,2))
await browser.close()
