import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const NEEDLE='本机事务牌库尚未就绪'
const browser=await chromium.launch()
const out={ checks:[], needle: NEEDLE }
async function login(ctx,name){
  const page=await ctx.newPage()
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',pw)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:30000})
  return page
}
const ctx=await browser.newContext({viewport:{width:1280,height:900}})
// 1) 登录后立刻进牌库（最容易撞上"还在打开"的路径）
let page=await login(ctx,'彩排01')
await page.goto(BASE+'/decks',{waitUntil:'domcontentloaded'})
await page.waitForTimeout(1200)
out.checks.push({ step:'login-then-decks-immediate', hit:(await page.evaluate(n=>document.body.innerText.includes(n),NEEDLE)) })
await page.waitForTimeout(4000)
out.checks.push({ step:'decks-after-4s', hit:(await page.evaluate(n=>document.body.innerText.includes(n),NEEDLE)) })
// 2) 立刻进对战大厅
await page.goto(BASE+'/battle',{waitUntil:'domcontentloaded'})
await page.waitForTimeout(1200)
out.checks.push({ step:'battle-immediate', hit:(await page.evaluate(n=>document.body.innerText.includes(n),NEEDLE)) })
await page.waitForTimeout(4000)
out.checks.push({ step:'battle-after-4s', hit:(await page.evaluate(n=>document.body.innerText.includes(n),NEEDLE)) })
// 3) 账号切换：换一个干净上下文登录彩排02，再马上进牌库
const ctx2=await browser.newContext({viewport:{width:1280,height:900}})
page=await login(ctx2,'彩排02')
await page.goto(BASE+'/decks',{waitUntil:'domcontentloaded'})
await page.waitForTimeout(1500)
out.checks.push({ step:'account-switch-decks', hit:(await page.evaluate(n=>document.body.innerText.includes(n),NEEDLE)) })
// 4) 编辑器页
await page.goto(BASE+'/decks/editor',{waitUntil:'domcontentloaded'}).catch(()=>{})
await page.waitForTimeout(3000)
out.checks.push({ step:'deck-editor', hit:(await page.evaluate(n=>document.body.innerText.includes(n),NEEDLE)) })
await page.screenshot({path:'D:/GPT/Legion12/artifacts/a3-clear-20261126/t2-deck-readiness.png'})
await browser.close()
out.passed = out.checks.every(c=>!c.hit)
console.log(JSON.stringify(out,null,1).slice(0,1500))
