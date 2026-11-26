import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const OUT='D:/GPT/Legion12/artifacts/a3-clear-20261126'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
// 赛事码由准备脚本产出，不能写死（写死会指向上一轮已收掉的赛事）。
const prep = JSON.parse(fs.readFileSync(OUT + '/b1-prep.json', 'utf8'))
const CODE = prep.code

const report={startedAt:new Date().toISOString(),code:CODE,identities:[],messages:{}}
const browser=await chromium.launch()
const click=async(page,name,exact=false)=>{const b=page.getByRole('button',{name,exact});if(!(await b.count()))return false;try{await b.first().click({force:true});return true}catch{return false}}
const prepInfo = JSON.parse(fs.readFileSync(OUT + '/b1-prep.json', 'utf8'))
const expectedRoomCode = prepInfo.match?.roomCode ?? ''
async function session(name){
  const ctx=await browser.newContext({viewport:{width:1280,height:900}})
  const page=await ctx.newPage()
  const rooms=[]
  page.on('websocket',ws=>{
    ws.on('framereceived',f=>{
      const t=String(f.payload)
      if(t.includes('"type":"session"')){ const m=t.match(/"roomCode":"([^"]*)"/); if(m) rooms.push(m[1]) }
    })
  })
  page.__rooms = rooms
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',pw)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:30000})
  // 先退出遗留对局：否则会停在上一场的房间里，进不了本场赛事桌
  await page.goto(BASE+'/game',{waitUntil:'domcontentloaded'});await page.waitForTimeout(5000)
  for(let i=0;i<3;i++){
    let left=false
    for(const l of ['返回大厅','返回赛事/大厅','投降']){ if(await click(page,l)){ left=true; break } }
    await page.waitForTimeout(2500)
    if(!left) break
    await page.goto(BASE+'/game',{waitUntil:'domcontentloaded'}); await page.waitForTimeout(2500)
  }
  await page.goto(BASE+'/battle/tournaments',{waitUntil:'domcontentloaded'});await page.waitForTimeout(4000)
  const row=page.getByRole('button',{name:new RegExp(CODE)})
  if(await row.count()){await row.first().click({force:true});await page.waitForTimeout(3500)}
  await click(page,'查看赛事');await page.waitForTimeout(3500)
  await click(page,'赛程');await page.waitForTimeout(2500)
  // 入口文案随身份/阶段变化：把赛程区里所有「进入/观战」按钮逐个点，直到真的落到 /game。
  let entered = false
  for (let attempt = 0; attempt < 4 && !entered; attempt++) {
    const tab=page.getByRole('button',{name:'赛程',exact:true})
    if(await tab.count()){ try{ await tab.first().click({force:true}) }catch{} ; await page.waitForTimeout(1500) }
    const labels = await page.evaluate(()=>Array.from(document.querySelectorAll('button'))
      .map(b=>(b.innerText||'').replace(/\s+/g,' ').trim())
      .filter(t=>/进入|观战/.test(t) && t.length<20))
    for (const label of labels) {
      try { await page.getByRole('button',{name:label,exact:true}).first().click({force:true}) } catch {}
      await page.waitForTimeout(3000)
      if (page.url().includes('/game')) { entered = true; break }
    }
  }
  if (entered) await page.waitForTimeout(6000)
  return {ctx,page,name,entered,url:page.url(),roomOk: rooms.includes(expectedRoomCode),rooms}
}
async function openTable(page){
  const opener=page.locator('button[aria-label="打开好友功能"]')
  if(!(await opener.count()))return {hasTab:0,note:'no opener'}
  await page.evaluate(()=>document.querySelector('button[aria-label="打开好友功能"]')?.click())
  await page.waitForTimeout(1500)
  const tab=page.getByRole('button',{name:'本桌',exact:true})
  const hasTab=await tab.count()
  if(hasTab){await tab.first().click({force:true});await page.waitForTimeout(2000)}
  return {hasTab}
}
const sessions=[await session('彩排01'),await session('彩排02'),await session('彩排08'),await session('彩排07'),await session('彩排03')]
for(const s of sessions){
  const t=await openTable(s.page)
  report.identities.push({name:s.name,entered:s.entered,url:s.url,roomOk:s.roomOk,rooms:s.rooms.slice(-2),
    hasTableTab:t.hasTab||0,note:t.note||''})
  await s.page.screenshot({path:OUT+'/b1b-'+s.name+'.png'})
}
report.expectedRoomCode = expectedRoomCode
report.playersInRoom = sessions.slice(0,2).every(s=>s.roomOk)
const send=async(page,token)=>{const host=page.locator('[data-l12-social-popover]');if(!(await host.count()))return false;try{await host.locator('textarea').first().fill(token);await host.getByRole('button',{name:'发送',exact:true}).click({force:true});await page.waitForTimeout(2500);return true}catch{return false}}
const sees=async(page,token)=>page.waitForFunction(v=>document.body.innerText.includes(v),token,{timeout:12000}).then(()=>true).catch(()=>false)
const [a,b,ref,org,ord]=sessions
const ta='B1A-'+Date.now().toString(36).slice(-5)
if(await send(a.page,ta)) report.messages.playerTo={b:await sees(b.page,ta),organizer:await sees(org.page,ta),referee:await sees(ref.page,ta),ordinary:await sees(ord.page,ta)}
const tr='B1R-'+Date.now().toString(36).slice(-5)
if(await send(ref.page,tr)) report.messages.refereeTo={players:(await sees(a.page,tr))&&(await sees(b.page,tr)),organizer:await sees(org.page,tr)}
const to='B1O-'+Date.now().toString(36).slice(-5)
if(await send(org.page,to)) report.messages.organizerTo={referee:await sees(ref.page,to)}
fs.writeFileSync(OUT+'/b1b-report.json',JSON.stringify(report,null,2))
await browser.close()
console.log(JSON.stringify(report,null,1).slice(0,2000))
