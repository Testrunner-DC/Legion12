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
const created=await api('POST','/api/tournaments',{token:t1,key:'panel-'+Date.now().toString(36),version:pv,body:{tournament:{name:'[ACC] 面板验收 '+Date.now().toString(36),format:'swiss',visibility:'public',maxPlayers:16,startAt:new Date(Date.now()+3600000).toISOString(),ruleset:'official',description:'赛前调整面板验收',deckVisibility:'after',disasterMode:'none',banList:'',roundMinutes:50,checkInMinutes:5,swissRounds:4,registrationVisibility:'public',lateGraceMinutes:5,operationMode:'managed'}}})
const code=(created.json?.tournament||created.json)?.code
const browser=await chromium.launch()
const ctx=await browser.newContext({viewport:{width:1280,height:900}})
const page=await ctx.newPage()
await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
await page.fill('form input[autocomplete=username]','彩排01')
await page.fill('form input[type=password]',pw)
await page.click('form button[type=submit]')
await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:30000})
await page.goto(BASE+'/battle/tournaments/'+code,{waitUntil:'domcontentloaded'})
await page.waitForTimeout(6000)
const calls=[]
page.on('response', async r=>{ if(r.url().includes('schedule')||r.url().includes('/api/tournaments/code')) calls.push({u:r.url().replace(BASE,''),s:r.status()}) })
const body=async()=>(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
let text=await body()
const out={code, panel:text.includes('赛前调整'), suggestion:/建议 \d+ 轮/.test(text)}
const adopt=page.getByRole('button',{name:'采纳'})
if(await adopt.count()){ await adopt.first().click({force:true}); await page.waitForTimeout(1000) }
const reason=page.locator('[data-tournament-schedule] input[placeholder*="理由"]')
if(await reason.count()) await reason.fill('验收：按实际人数调整轮数')
const save=page.getByRole('button',{name:'保存赛前调整'})
if(await save.count()){ await save.first().click({force:true}); await page.waitForTimeout(4000) }
text=await body()
out.saved=text.includes('赛前调整已保存'); out.calls=calls; out.panelText=(await page.evaluate(()=>document.querySelector('[data-tournament-schedule]')?.innerText||'')).replace(/\s+/g,' ').slice(0,200)
await page.screenshot({path:OUT+'/a3-10-schedule-panel.png'})
await browser.close()
console.log(JSON.stringify(out,null,1).slice(0,900))
