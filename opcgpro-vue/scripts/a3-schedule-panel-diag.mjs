import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const OUT='D:/GPT/Legion12/artifacts/a3-clear-20261126'
const pw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const api=async(method,path,{token,body,version,key}={})=>{
  const h={Accept:'application/json'}
  if(token)h.Authorization='Bearer '+token
  if(body!==undefined)h['Content-Type']='application/json'
  if(key)h['Idempotency-Key']=key
  if(version!==undefined)h['If-Match']='"'+version+'"'
  const p=body===undefined||version===undefined?body:{...body,expectedVersion:version}
  const r=await fetch(BASE+path,{method,headers:h,body:p===undefined?undefined:JSON.stringify(p)})
  const t=await r.text(); let j=null; try{j=t?JSON.parse(t):null}catch{}
  return {status:r.status,json:j,text:t}
}
const login=async(u,p)=>(await api('POST','/api/auth/login',{body:{username:u,password:p}})).json?.token
const t1=await login('彩排01',pw)
const pv=(await api('GET','/api/tournaments/summaries?section=host&page=1&pageSize=1',{token:t1})).json.platformVersion
const created=await api('POST','/api/tournaments',{token:t1,key:'panel2-'+Date.now().toString(36),version:pv,body:{tournament:{name:'[ACC] 面板2 '+Date.now().toString(36),format:'swiss',visibility:'public',maxPlayers:16,startAt:new Date(Date.now()+3600000).toISOString(),ruleset:'official',description:'面板保存链路诊断',deckVisibility:'after',disasterMode:'none',banList:'',roundMinutes:50,checkInMinutes:5,swissRounds:4,registrationVisibility:'public',lateGraceMinutes:5,operationMode:'managed'}}})
const code=(created.json?.tournament||created.json)?.code
const browser=await chromium.launch()
const ctx=await browser.newContext({viewport:{width:1280,height:900}})
const page=await ctx.newPage()
const calls=[]
page.on('response', async r=>{ const u=r.url().replace(BASE,''); if(u.includes('/api/tournaments')) calls.push({u:u.split('?')[0],s:r.status()}) })
page.on('console', m=>{ if(m.type()==='error') calls.push({console:String(m.text()).slice(0,120)}) })
await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
await page.fill('form input[autocomplete=username]','彩排01')
await page.fill('form input[type=password]',pw)
await page.click('form button[type=submit]')
await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:30000})
await page.goto(BASE+'/battle/tournaments/'+code,{waitUntil:'domcontentloaded'})
await page.waitForTimeout(7000)
const snap=async label=>({ label, panel:await page.locator('[data-tournament-schedule]').count(),
  text:(await page.evaluate(()=>document.querySelector('[data-tournament-schedule]')?.innerText||'')).replace(/\s+/g,' ').slice(0,180) })
const out={code, seq:[await snap('after-load')]}
const reason=page.locator('[data-tournament-schedule] input[placeholder*="理由"]')
out.reasonCount=await reason.count()
if(await reason.count()) await reason.fill('验收：按实际人数调整轮数')
const save=page.getByRole('button',{name:'保存赛前调整'})
out.saveCount=await save.count(); out.saveDisabled=out.saveCount? await save.first().isDisabled():null
if(await save.count()){ await save.first().click({force:true}); await page.waitForTimeout(5000) }
out.seq.push(await snap('after-save'))
out.calls=calls
await page.screenshot({path:OUT+'/a3-11-panel-diag.png'})
await browser.close()
console.log(JSON.stringify(out,null,1).slice(0,2200))
