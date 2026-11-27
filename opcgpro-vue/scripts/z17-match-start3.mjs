import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const OUT='D:/GPT/Legion12/artifacts/z9-moon-poison-20261127'
const state={}
const browser=await chromium.launch()
const txt=async p=>(await p.evaluate(()=>document.body.innerText||'')).replace(/\s+/g,' ')
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
async function clickAny(page,labels,timeout=6000){
  for (const label of labels){
    const loc=page.getByText(label,{exact:false}).first()
    try{ if(await loc.count()){ await loc.click({force:true,timeout}); return label } }catch{}
  }
  return null
}
async function selectDeck(page,deckName){
  await page.goto(BASE+'/decks',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(4500)
  await clickAny(page,['我的牌库']); await page.waitForTimeout(1800)
  await page.goto(BASE+'/battle',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(6000)
  await clickAny(page,['ROOM好友房']); await page.waitForTimeout(2200)
  await clickAny(page,['点击封面更换牌库','点击封面选择牌库']); await page.waitForTimeout(2200)
  await clickAny(page,[deckName]); await page.waitForTimeout(900)
  const ok=page.getByRole('button',{name:'确认使用'}).first(); if(await ok.count()) await ok.click({force:true})
  await page.waitForTimeout(1800)
  return (await txt(page)).includes(deckName)
}
try{
  const a=await login('彩排01'); state.deckA=await selectDeck(a,'月读实机A')
  await clickAny(a,['创建新房间']); await a.waitForTimeout(5500)
  const body=await txt(a)
  const m=body.match(/([A-Z0-9]{5,8})\s*复制房间码/)
  state.roomCode=m?m[1]:null
  const b=await login('彩排02'); state.deckB=await selectDeck(b,'月读实机B(毒药)')
  await clickAny(b,['ROOM好友房']); await b.waitForTimeout(2000)
  const input=b.locator('input[placeholder*="房间码"]').first()
  await input.fill(state.roomCode||'')
  await clickAny(b,['加入对战']); await b.waitForTimeout(6000)
  state.afterJoinB=(await txt(b)).slice(0,200)
  await clickAny(a,['准备对战','准备']); await a.waitForTimeout(2000)
  await clickAny(b,['准备对战','准备']); await b.waitForTimeout(2000)
  state.roomA=(await txt(a)).slice(0,180)
  state.roomB=(await txt(b)).slice(0,180)
  await clickAny(a,['开始对战']); await a.waitForTimeout(12000)
  state.boardA=(await txt(a)).slice(0,600)
  state.boardB=(await txt(b)).slice(0,300)
  await a.screenshot({path:OUT+'/z17-board-a.png'})
  await b.screenshot({path:OUT+'/z17-board-b.png'})
}catch(e){ state.error=String(e).slice(0,250) }
fs.writeFileSync(OUT+'/z17-state.json',JSON.stringify(state,null,2))
console.log(JSON.stringify(state,null,2))
await browser.close()
