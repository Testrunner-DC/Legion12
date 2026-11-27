import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const OUT='D:/GPT/Legion12/artifacts/z9-moon-poison-20261127'
const browser=await chromium.launch()
async function login(name){
  const ctx=await browser.newContext({viewport:{width:1440,height:1000}})
  const page=await ctx.newPage(); page.on('dialog',d=>d.accept())
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',pw)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:40000})
  return page
}
const a=await login('彩排01')
await a.goto(BASE+'/decks',{waitUntil:'domcontentloaded'}); await a.waitForTimeout(6000)
await a.getByText('我的牌库',{exact:false}).first().click({force:true}).catch(()=>{})
await a.waitForTimeout(3000)
await a.goto(BASE+'/battle',{waitUntil:'domcontentloaded'}); await a.waitForTimeout(8000)
await a.getByText('点击封面选择牌库',{exact:false}).first().click({force:true}).catch(()=>{})
await a.waitForTimeout(4000)
// 选中月读实机A
const card=a.getByText('月读实机A',{exact:false}).first()
if(await card.count()) await card.click({force:true})
await a.waitForTimeout(1500)
const confirm=a.getByRole('button',{name:'确认使用'}).first()
if(await confirm.count()) await confirm.click({force:true})
await a.waitForTimeout(3000)
const afterPick=(await a.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
await a.screenshot({path:OUT+'/z13-after-pick.png'})
// 打开好友房
const roomTab=a.getByText('ROOM好友房',{exact:false}).first()
if(await roomTab.count()) await roomTab.click({force:true})
await a.waitForTimeout(4000)
const roomBody=(await a.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
await a.screenshot({path:OUT+'/z13-room-tab.png'})
const btns=await a.evaluate(()=>Array.from(document.querySelectorAll('button')).map(b=>b.textContent.trim()).filter(Boolean))
console.log(JSON.stringify({deckSelected:afterPick.includes('月读实机A')&&!afterPick.includes('当前没有已保存牌库'),roomSnippet:roomBody.slice(0,500),buttons:btns.slice(0,20)},null,2))
await browser.close()
