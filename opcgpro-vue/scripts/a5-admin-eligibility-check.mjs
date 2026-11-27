import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const OUT='D:/GPT/Legion12/artifacts/a4-eligibility-live-20261127'
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const browser=await chromium.launch()
const ctx=await browser.newContext({viewport:{width:1440,height:1000}})
const page=await ctx.newPage()
await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
await page.fill('form input[autocomplete=username]','Admin')
await page.fill('form input[type=password]',adminPw)
await page.click('form button[type=submit]')
await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:40000})
await page.goto(BASE+'/admin/users/accounts',{waitUntil:'domcontentloaded'})
await page.waitForTimeout(7000)

const data=await page.evaluate(()=>{
  const labels=Array.from(document.querySelectorAll('*'))
    .filter(el=>el.children.length===0 && /赛事资质/.test(el.textContent||''))
    .map(el=>(el.textContent||'').trim()).slice(0,14)
  const buttons=Array.from(document.querySelectorAll('button'))
    .filter(b=>/授予|撤销|赛事/.test(b.textContent||''))
    .map(b=>{
      let row=b.closest('tr,li,article,div[class*=row],div[class*=account]')||b.parentElement
      return {
        label:(b.textContent||'').trim(),
        disabled:b.disabled,
        rowText:((row&&row.innerText)||'').replace(/\s+/g,' ').slice(0,150),
      }
    })
  return {labels, buttons:buttons.slice(0,16), bodySnippet:(document.body.innerText||'').replace(/\s+/g,' ').slice(0,700)}
})
await page.screenshot({path:OUT+'/a5-admin-eligibility.png',fullPage:false})
console.log(JSON.stringify(data,null,2))
await browser.close()