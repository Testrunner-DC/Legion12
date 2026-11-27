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
const txt=async p=>(await p.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
async function pickDeck(page,name,inRoom){
  await page.goto(BASE+'/decks',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(5000)
  await page.getByText('我的牌库',{exact:false}).first().click({force:true}).catch(()=>{})
  await page.waitForTimeout(2000)
  await page.goto(BASE+'/battle',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(7000)
  await page.getByText('ROOM好友房',{exact:false}).first().click({force:true}); await page.waitForTimeout(3000)
  const cover=page.getByText(inRoom?'点击封面更换牌库':'点击封面选择牌库',{exact:false}).first()
  if(await cover.count()) await cover.click({force:true})
  await page.waitForTimeout(3000)
  const card=page.getByText(name,{exact:false}).first(); if(await card.count()) await card.click({force:true})
  await page.waitForTimeout(1200)
  const ok=page.getByRole('button',{name:'确认使用'}).first(); if(await ok.count()) await ok.click({force:true})
  await page.waitForTimeout(2000)
}
const out={}
const a=await login('彩排01')
await pickDeck(a,'月读实机A',false)
await a.getByRole('button',{name:'创建新房间'}).first().click({force:true}); await a.waitForTimeout(6000)
let body=await txt(a)
const m=body.match(/复制房间码\s*([A-Z0-9]{5,8})/)||body.match(/\b([A-Z0-9]{6})\b/)
out.roomCode=m?m[1]:null
const b=await login('彩排02')
await pickDeck(b,'月读实机B(毒药)',false)
await b.getByPlaceholder(/房间码/).first().fill(out.roomCode||'').catch(async()=>{
  const inp=b.locator('input').last(); await inp.fill(out.roomCode||'')
})
await b.getByRole('button',{name:'加入对战'}).first().click({force:true}); await b.waitForTimeout(6000)
out.afterJoinB=(await txt(b)).slice(0,220)
// 双方准备
for (const p of [b,a]) {
  const ready=p.getByRole('button',{name:/准备(?!状态)/}).first()
  if(await ready.count()) await ready.click({force:true}).catch(()=>{})
  await p.waitForTimeout(2500)
}
out.afterReadyA=(await txt(a)).slice(0,260)
const start=a.getByRole('button',{name:/开始对战|开始比赛|开始/}).first()
if(await start.count()) await start.click({force:true}).catch(()=>{})
await a.waitForTimeout(9000); await b.waitForTimeout(3000)
out.boardA=(await txt(a)).slice(0,400)
out.boardB=(await txt(b)).slice(0,240)
await a.screenshot({path:OUT+'/z15-board-a.png'})
console.log(JSON.stringify(out,null,2))
await browser.close()
