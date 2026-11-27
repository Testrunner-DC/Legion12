import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE='https://legion-12.com/testrun'
const OUT='D:/GPT/Legion12/artifacts/a4-eligibility-live-20261127'
fs.mkdirSync(OUT,{recursive:true})
const playerPw=fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt','utf8').trim()
const adminPw=fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt','utf8').trim()
const report={startedAt:new Date().toISOString(),checks:[]}
const browser=await chromium.launch()

async function login(name,pw){
  const ctx=await browser.newContext({viewport:{width:1360,height:900}})
  const page=await ctx.newPage()
  page.on('dialog',d=>d.accept())
  await page.goto(BASE+'/me',{waitUntil:'domcontentloaded'})
  await page.fill('form input[autocomplete=username]',name)
  await page.fill('form input[type=password]',pw)
  await page.click('form button[type=submit]')
  await page.waitForFunction(()=>!document.querySelector('form input[type=password]'),{timeout:40000})
  return page
}

// 1) 管理端：账号、权限与会话 里每行是否显示赛事资质，授予/撤销按钮的禁用状态
const admin=await login('Admin',adminPw)
await admin.goto(BASE+'/admin/accounts',{waitUntil:'domcontentloaded'})
await admin.waitForTimeout(6000)
const rows=await admin.evaluate(()=>Array.from(document.querySelectorAll('tr')).map(tr=>{
  const text=tr.innerText.replace(/\s+/g,' ')
  const buttons=Array.from(tr.querySelectorAll('button'))
  return {
    text,
    grant:buttons.filter(b=>b.textContent.includes('授予')).map(b=>({disabled:b.disabled})),
    revoke:buttons.filter(b=>b.textContent.includes('撤销')).map(b=>({disabled:b.disabled})),
  }
}).filter(r=>r.text.length>10))
report.checks.push({step:'admin-rows',sample:rows.slice(0,6).map(r=>({
  hasEligibilityLabel:r.text.includes('赛事资质'),
  eligibility: (r.text.match(/赛事资质：([^ ]+)/)||[])[1]||'',
  grantDisabled:r.grant.map(g=>g.disabled), revokeDisabled:r.revoke.map(g=>g.disabled),
}))})
await admin.screenshot({path:OUT+'/a4-admin-accounts.png',fullPage:false})

// 2) 玩家端：找一个没有资质的彩排账号
const candidates=['彩排01','彩排02','彩排03','彩排04','彩排05']
let picked=null
for (const name of candidates){
  const page=await login(name,playerPw)
  await page.goto(BASE+'/battle/tournaments',{waitUntil:'domcontentloaded'})
  await page.waitForTimeout(4000)
  const body=(await page.evaluate(()=>document.body.innerText)).replace(/\s+/g,' ')
  const badge=body.includes('已获得赛事资质')
  const pending=body.includes('赛事资质待审核')
  const applyCount=await page.getByRole('button',{name:'申请赛事资格'}).count()
  const createCount=await page.getByRole('button',{name:'创建赛事'}).count()
  report.checks.push({step:'player',name,badge,pending,applyCount,createCount})
  if(applyCount>0 && !badge && !picked){
    picked=page
    await page.getByRole('button',{name:'申请赛事资格'}).first().click({force:true})
    await page.waitForTimeout(2500)
    const dialog=await page.evaluate(()=>{
      const el=document.querySelector('[role=dialog]')
      return el ? {present:true, hasTextarea:!!el.querySelector('textarea'), text:el.innerText.replace(/\s+/g,' ').slice(0,120)} : {present:false}
    })
    report.checks.push({step:'apply-dialog',name,...dialog})
    await page.screenshot({path:OUT+'/a4-apply-dialog.png',fullPage:false})
    const bgBefore=(await page.evaluate(()=>getComputedStyle(document.body).backgroundColor))
    const selectStyles=await page.evaluate(()=>Array.from(document.querySelectorAll('[role=dialog] select')).map(s=>getComputedStyle(s).backgroundColor))
    report.checks.push({step:'apply-dialog-theme',bodyBg:bgBefore,selectBg:selectStyles})
    await page.keyboard.press('Escape').catch(()=>{})
    break
  }
}
report.finishedAt=new Date().toISOString()
fs.writeFileSync(OUT+'/a4-report.json',JSON.stringify(report,null,2))
console.log(JSON.stringify(report,null,2))
await browser.close()
