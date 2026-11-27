import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const OUT='D:/GPT/Legion12/artifacts/z9-moon-poison-20261127'
fs.mkdirSync(OUT,{recursive:true})
const browser=await chromium.launch()
const log=[]
async function login(name){
  const ctx=await browser.newContext({viewport:{width:1440,height:1000}})
  const page=await ctx.newPage()
  page.on('dialog',d=>d.accept())
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',pw)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:40000})
  return page
}
const text=async p=>(await p.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
const buttons=async p=>p.evaluate(()=>Array.from(document.querySelectorAll('button')).map(b=>b.textContent.trim()).filter(Boolean))

const a=await login('彩排01')
await a.goto(BASE+'/battle',{waitUntil:'domcontentloaded'})
await a.waitForTimeout(6000)
log.push({step:'battle-buttons',buttons:await buttons(a),text:(await text(a)).slice(0,400)})
await a.screenshot({path:OUT+'/z9-battle-lobby.png'})
console.log(JSON.stringify(log,null,2))
await browser.close()
