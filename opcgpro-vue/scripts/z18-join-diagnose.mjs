import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const OUT='D:/GPT/Legion12/artifacts/z9-moon-poison-20261127'
const browser=await chromium.launch()
const txt=async p=>(await p.evaluate(()=>document.body.innerText||'')).replace(/\s+/g,' ')
async function login(name){
  const ctx=await browser.newContext({viewport:{width:1440,height:1000}})
  const page=await ctx.newPage()
  page.on('dialog',d=>d.accept())
  page.on('console',m=>{ if(m.type()==='error') (page.__errs||(page.__errs=[])).push(m.text().slice(0,200)) })
  page.on('response',r=>{ if(r.status()>=400) (page.__fails||(page.__fails=[])).push(r.status()+' '+r.url().slice(-60)) })
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
async function roomTab(page){
  await page.goto(BASE+'/decks',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(4000)
  await clickAny(page,['我的牌库']); await page.waitForTimeout(1500)
  await page.goto(BASE+'/battle',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(6000)
  await clickAny(page,['ROOM好友房']); await page.waitForTimeout(2200)
}
const out={}
const a=await login('彩排01')
await roomTab(a)
await clickAny(a,['点击封面更换牌库','点击封面选择牌库']); await a.waitForTimeout(2000)
await clickAny(a,['月读实机A']); await a.waitForTimeout(800)
const okA=a.getByRole('button',{name:'确认使用'}).first(); if(await okA.count()) await okA.click({force:true})
await a.waitForTimeout(1500)
await clickAny(a,['创建新房间']); await a.waitForTimeout(5000)
const code=(await txt(a)).match(/([A-Z0-9]{5,8})\s*复制房间码/)?.[1]||null
out.code=code

const b=await login('彩排02')
await roomTab(b)
out.bDeckBeforeJoin=(await txt(b)).match(/ROOM DECK[^\n]{0,80}/)?.[0]||null
const input=b.locator('input[placeholder*="房间码"]').first()
await input.fill(code||'')
out.joinButtons=await b.evaluate(()=>Array.from(document.querySelectorAll('button')).map(x=>({t:x.textContent.trim(),d:x.disabled})).filter(x=>/加入|观战/.test(x.t)))
await b.getByRole('button',{name:'加入对战'}).first().click({force:true}).catch(e=>out.joinClickError=String(e).slice(0,120))
await b.waitForTimeout(6000)
out.bAfterJoin=(await txt(b)).slice(0,200)
out.bErrors=(b.__errs||[]).slice(0,5)
out.bFails=(b.__fails||[]).slice(0,5)
out.aErrors=(a.__errs||[]).slice(0,5)
out.aFails=(a.__fails||[]).slice(0,5)
out.aRoom=(await txt(a)).slice(0,160)
console.log(JSON.stringify(out,null,2))
await browser.close()
