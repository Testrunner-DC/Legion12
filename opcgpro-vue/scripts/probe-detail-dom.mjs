import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const browser=await chromium.launch()
const ctx=await browser.newContext({viewport:{width:1280,height:900}})
const page=await ctx.newPage()
await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
await page.fill('form input[autocomplete=username]','彩排01')
await page.fill('form input[type=password]',pw)
await page.click('form button[type=submit]')
await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:30000})
await page.goto(BASE+'/battle/tournaments/ZP65A7',{waitUntil:'domcontentloaded'})
await page.waitForTimeout(8000)
const info=await page.evaluate(()=>({
  mains:Array.from(document.querySelectorAll('main')).map(m=>m.className),
  blockedAttr:document.querySelector('main')?.getAttribute('data-schedule-blocked')??null,
  hasPanelText:document.body.innerText.includes('赛前调整'),
  bodyLen:document.body.innerText.length,
  snippet:document.body.innerText.replace(/\s+/g,' ').slice(0,200),
}))
console.log(JSON.stringify(info,null,1).slice(0,1200))
await browser.close()