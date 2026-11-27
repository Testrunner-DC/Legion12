import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const OUT='D:/GPT/Legion12/artifacts/z9-moon-poison-20261127'
const state={}
const browser=await chromium.launch()
const txt=async p=>(await p.evaluate(()=>document.body.innerText||'')).replace(/\s+/g,' ')
const btn=async(p,name)=>{const loc=p.getByRole('button',{name,exact:false}).first(); if(await loc.count()){await loc.click({force:true}); return true} return false}
async function login(name){
  const ctx=await browser.newContext({viewport:{width:1440,height:1000}})
  const page=await ctx.newPage(); page.on('dialog',d=>d.accept())
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',pw)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:45000})
  return page
}
async function toRoom(page){
  await page.goto(BASE+'/decks',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(4000)
  const mine=page.getByText('我的牌库',{exact:false}).first(); if(await mine.count()) await mine.click({force:true})
  await page.waitForTimeout(1500)
  await page.goto(BASE+'/battle',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(6000)
  const room=page.getByText('ROOM好友房',{exact:false}).first(); if(await room.count()) await room.click({force:true})
  await page.waitForTimeout(2200)
}
async function pickRoomDeck(page,deck){
  const cover=page.getByText('点击封面更换牌库',{exact:false}).first()
  if(!(await cover.count())) return false
  await cover.click({force:true}); await page.waitForTimeout(2000)
  const card=page.getByText(deck,{exact:false}).first(); if(!(await card.count())) return false
  await card.click({force:true}); await page.waitForTimeout(800)
  if(!(await btn(page,'确认使用'))) return false
  await page.waitForTimeout(1800)
  return (await txt(page)).includes(deck)
}
try{
  const a=await login('彩排01'); await toRoom(a); state.aDeck=await pickRoomDeck(a,'月读实机A')
  await btn(a,'创建新房间'); await a.waitForTimeout(5000)
  state.code=(await txt(a)).match(/([A-Z0-9]{5,8})\s*复制房间码/)?.[1]||null
  const b=await login('彩排02'); await toRoom(b)
  await pickRoomDeck(b,'月读实机B(毒药)')
  await b.locator('input[placeholder*="房间码"]').first().fill(state.code||'')
  await btn(b,'加入对战'); await b.waitForTimeout(5000)
  state.bInRoom=(await txt(b)).slice(0,150)
  state.roomReadyLabelsA=await a.evaluate(()=>Array.from(document.querySelectorAll('button')).map(x=>x.textContent.trim()).filter(t=>/准备|开始/.test(t)))
  state.readyA=await btn(a,'准备对战')||await btn(a,'准备')
  await a.waitForTimeout(1500)
  state.readyB=await btn(b,'准备对战')||await btn(b,'准备')
  await b.waitForTimeout(1500)
  state.roomA=(await txt(a)).slice(0,200)
  state.start=await btn(a,'开始对战')
  await a.waitForTimeout(12000)
  state.boardA=(await txt(a)).slice(0,700)
  state.boardB=(await txt(b)).slice(0,300)
  await a.screenshot({path:OUT+'/z19-board-a.png',fullPage:false})
  await b.screenshot({path:OUT+'/z19-board-b.png',fullPage:false})
}catch(e){ state.error=String(e).slice(0,250) }
fs.writeFileSync(OUT+'/z19-state.json',JSON.stringify(state,null,2))
console.log(JSON.stringify(state,null,2))
await browser.close()
