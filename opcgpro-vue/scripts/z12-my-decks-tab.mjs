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

await page.goto(BASE+'/decks',{waitUntil:'domcontentloaded'})
await page.waitForTimeout(6000)
const tab=page.getByText('我的牌库',{exact:false}).first()
if(await tab.count()){ await tab.click({force:true}); }
await page.waitForTimeout(8000)
let body=(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
out.myDecksTab={hasDeckName:body.includes('月读实机A'),snippet:body.slice(0,600)}
await page.screenshot({path:'D:/GPT/Legion12/artifacts/z9-moon-poison-20261127/z12-my-decks.png'})

// 大厅里点开牌库选择器
await page.goto(BASE+'/battle',{waitUntil:'domcontentloaded'})
await page.waitForTimeout(9000)
const picker=page.getByText('点击封面选择牌库',{exact:false}).first()
if(await picker.count()){ await picker.click({force:true}); }
await page.waitForTimeout(5000)
body=(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
out.battlePicker={hasDeckName:body.includes('月读实机A'),snippet:body.slice(0,600)}
await page.screenshot({path:'D:/GPT/Legion12/artifacts/z9-moon-poison-20261127/z12-battle-picker.png'})
console.log(JSON.stringify(out,null,2))
await browser.close()
