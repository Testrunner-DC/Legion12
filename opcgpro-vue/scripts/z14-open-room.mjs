import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const heroPw=fs.readFileSync('D:/GPT/Legion12/artifacts/deploy-credentials.json','utf8')
const OUT='D:/GPT/Legion12/artifacts/z9-moon-poison-20261127'
const browser=await chromium.launch()
async function login(name,password=pw){
  const ctx=await browser.newContext({viewport:{width:1440,height:1000}})
  const page=await ctx.newPage(); page.on('dialog',d=>d.accept())
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',password)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:40000})
  return page
}
async function pickDeck(page,name){
  await page.goto(BASE+'/decks',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(5000)
  await page.getByText('我的牌库',{exact:false}).first().click({force:true}).catch(()=>{})
  await page.waitForTimeout(2500)
  await page.goto(BASE+'/battle',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(7000)
  const room=page.getByText('ROOM好友房',{exact:false}).first(); if(await room.count()) await room.click({force:true})
  await page.waitForTimeout(3500)
  const cover=page.getByText('点击封面更换牌库',{exact:false}).first()
  if(await cover.count()) await cover.click({force:true}); else await page.getByText('点击封面选择牌库',{exact:false}).first().click({force:true}).catch(()=>{})
  await page.waitForTimeout(3500)
  const card=page.getByText(name,{exact:false}).first(); if(await card.count()) await card.click({force:true})
  await page.waitForTimeout(1200)
  const ok=page.getByRole('button',{name:'确认使用'}).first(); if(await ok.count()) await ok.click({force:true})
  await page.waitForTimeout(2500)
}
const a=await login('彩排01')
await pickDeck(a,'月读实机A')
await a.getByRole('button',{name:'创建新房间'}).first().click({force:true})
await a.waitForTimeout(6000)
let body=(await a.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
const codeMatch=body.match(/房间码[：:\s]*([A-Z0-9]{4,8})/)||body.match(/\b([A-Z0-9]{6,8})\b/)
const code=codeMatch?codeMatch[1]:null
await a.screenshot({path:OUT+'/z14-room-created.png'})
console.log(JSON.stringify({code, roomSnippet:body.slice(0,400), url:a.url()},null,2))
fs.writeFileSync(OUT+'/room-code.json',JSON.stringify({code, at:new Date().toISOString()}))
await browser.close()
