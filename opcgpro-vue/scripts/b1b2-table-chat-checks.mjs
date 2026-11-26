import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const OUT='D:/GPT/Legion12/artifacts/a3-clear-20261126'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const report={startedAt:new Date().toISOString(),checks:[]}
const browser=await chromium.launch()
const frames={a:[],b:[]}
async function login(name,side){
  const ctx=await browser.newContext({viewport:{width:1280,height:900}})
  const page=await ctx.newPage()
  if(side) page.on('websocket',ws=>{
    const tap=dir=>f=>{const t=String(f.payload); if(!t.includes('chat'))return; frames[side].push({dir,type:(t.match(/"type":"(\w+)"/)||[])[1]||'?'}); if(frames[side].length>60) frames[side].shift()}
    ws.on('framesent',tap('out')); ws.on('framereceived',tap('in'))
  })
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',pw)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:30000})
  return {ctx,page,name}
}
const click=async(page,name,exact=false)=>{const b=page.getByRole('button',{name,exact});if(!(await b.count()))return false;try{await b.first().click({force:true});return true}catch{return false}}
const a=await login('彩排01','a'), b=await login('彩排02','b')
for(const s of [a,b]){ await s.page.goto(BASE+'/battle',{waitUntil:'domcontentloaded'}); await s.page.waitForTimeout(3500); await click(s.page,'好友房'); await s.page.waitForTimeout(2000) }
await click(a.page,'创建新房间'); await a.page.waitForTimeout(3000)
const code=await a.page.evaluate(()=>{const btn=Array.from(document.querySelectorAll('button')).find(i=>(i.textContent||'').trim()==='复制房间码');if(!btn)return '';const host=btn.closest('section,article,div')||btn.parentElement;return (((host&&host.innerText)||'').match(/\b[A-Z0-9]{6}\b/g)||[])[0]||''})
report.checks.push({step:'room',code})
await b.page.fill('input[placeholder*="房间码"]',code); await click(b.page,'加入对战'); await b.page.waitForTimeout(3000)
for(const s of [a,b]){
  const slot=s.page.locator('text=点击封面选择牌库').first()
  if(await slot.count()){try{await slot.click({force:true})}catch{}}
  await s.page.waitForTimeout(1800)
  const dlg=s.page.locator('[role=dialog]').first()
  const opt=dlg.locator('text=张主牌').first()
  if(await opt.count()){try{await opt.click({force:true})}catch{}}
  await s.page.waitForTimeout(800); await click(s.page,'确认使用'); await s.page.waitForTimeout(1200)
  await click(s.page,'准备对战',true); await s.page.waitForTimeout(2000)
}
await a.page.waitForFunction(()=>!document.body.innerText.includes('友谊战整备室'),{timeout:45000})
report.checks.push({step:'game-start',ok:true})
// 双方打开本桌
for(const s of [a,b]){
  const opener=s.page.locator('button[aria-label="打开好友功能"]')
  if(await opener.count()) await s.page.evaluate(()=>document.querySelector('button[aria-label="打开好友功能"]')?.click())
  await s.page.waitForTimeout(1500)
  await click(s.page,'本桌',true); await s.page.waitForTimeout(2000)
}
// B1：A 发两条，B 打开后应发出 chatRead 并收到 chatReadAck
frames.b.length=0
const host=a.page.locator('[data-l12-social-popover]')
await host.locator('textarea').first().fill('B1-读回执-1')
await host.getByRole('button',{name:'发送',exact:true}).click({force:true})
await a.page.waitForTimeout(2000)
await host.locator('textarea').first().fill('B1-读回执-2')
await host.getByRole('button',{name:'发送',exact:true}).click({force:true})
await a.page.waitForTimeout(3000)
report.checks.push({step:'b1-read-receipt',
  bSawMessages: await b.page.evaluate(()=>document.body.innerText.includes('B1-读回执')),
  readSent: frames.b.some(f=>f.dir==='out'&&f.type==='chatRead'),
  readAck: frames.b.some(f=>f.dir==='in'&&f.type==='chatReadAck')})
// B2：窄屏可用性
for(const [label,w,h] of [['portrait',390,844],['landscape',844,390]]){
  await b.page.setViewportSize({width:w,height:h}); await b.page.waitForTimeout(1500)
  const info=await b.page.evaluate(()=>{
    const host=document.querySelector('[data-l12-social-popover]')
    const ta=host?.querySelector('textarea'); const send=Array.from(host?.querySelectorAll('button')||[]).find(b=>(b.textContent||'').trim()==='发送')
    const box=ta?.getBoundingClientRect()
    return {overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      textareaVisible: !!ta && (box?.width||0)>0 && (box?.height||0)>0,
      textareaInViewport: !!box && box.top>=0 && box.bottom<=window.innerHeight,
      sendVisible: !!send && send.offsetParent!==null}
  })
  report.checks.push({step:'b2-'+label, ...info})
  await b.page.screenshot({path:OUT+'/b2-'+label+'.png'})
}
fs.writeFileSync(OUT+'/b1b2-report.json',JSON.stringify(report,null,2))
await browser.close()
console.log(JSON.stringify(report,null,1).slice(0,2000))
