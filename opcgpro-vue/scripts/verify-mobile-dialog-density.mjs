import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const phase = process.argv.includes('--before') ? 'before' : 'after'
const baselineRef = process.env.L12_DIALOG_BASE || 'ef2abaeb1c47db81da4ea2477128adbf0bc7b75b'
const baselineCss = phase==='before' ? execFileSync('git',['show',`${baselineRef}:opcgpro-vue/src/l12/mobileViewport.css`],{encoding:'utf8'}) : null
const pickersOnly = process.argv.includes('--pickers')
const only = process.argv.find(arg=>arg.startsWith('--only='))?.slice(7)
const withArtwork = process.argv.includes('--art')
const assetCache = new Map()
async function publicAsset(pathname) {
 if(!pathname.startsWith('/card-assets/'))throw new Error('Only public card assets may be fetched')
 if(!assetCache.has(pathname))assetCache.set(pathname,fetch('https://legion-12.com'+pathname).then(async response=>{
  if(!response.ok)throw new Error(`Public asset ${response.status}: ${pathname}`)
  return {contentType:response.headers.get('content-type')||'application/octet-stream',body:Buffer.from(await response.arrayBuffer())}
 }))
 return assetCache.get(pathname)
}
const output = path.resolve('../artifacts/mobile-dialog-density', phase)
fs.mkdirSync(output, { recursive: true })
const profiles = [[568,320],[667,375],[740,360],[844,390],[1366,768],[1920,1080]].map(([width,height]) => ({width,height,desktop:width>1000}))
profiles.push({width:667,height:375,insets:{left:59,right:0,top:0,bottom:21}}, {width:568,height:320,font:1.25}, {width:844,height:390,font:1.5})
const cases = [
  ...[1,2,4,8].map(n => [`cards-${n}`,`card-choice=1&choice-count=${n}&mixed-orientation=1`,'prompt']),
  ['disaster-ban','disaster-choice=1&choice-count=12&mixed-availability=1','prompt'],
  ['disaster-pick','disaster-choice=1&disaster-pick=1&choice-count=8','prompt'],
  ['effects','option-fixture=1','prompt'], ['targets','response-fixture=1','prompt'],
  ['yes-no','opponent-confirm-fixture=1','prompt'], ['countdown','invalid-response-fixture=1','prompt'],
  ['master','modalFixture=1','master'], ['faction','modalFixture=1','faction'],
  ['master-actions','modalFixture=1','master'],
  ['graveyard','modalFixture=1','graveyard'],
  ['payment','morale-payment=1&runes=5&rune-usable=3&payment-actions=1','payment'],
  ['single-picker','','single-picker'], ['disaster-pool','','disaster-pool'],
]
const browser = await chromium.launch({headless:true,channel:'msedge'})
const results=(pickersOnly||only)&&fs.existsSync(path.join(output,'results.json'))?JSON.parse(fs.readFileSync(path.join(output,'results.json'),'utf8')).filter(x=>only?!x.key.includes(only):!x.key.endsWith('single-picker')&&!x.key.endsWith('disaster-pool')):[]
try {
 for (const profile of profiles) {
  const context=await browser.newContext({viewport:{width:profile.width,height:profile.height},hasTouch:!profile.desktop})
  const page=await context.newPage()
  page.setDefaultTimeout(5000)
  const cdp=await context.newCDPSession(page)
  await page.route('**/*', route => ['127.0.0.1','localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())
  if(withArtwork)await page.route('**/card-assets/**',async route=>{
   const url=new URL(route.request().url())
   try{await route.fulfill(await publicAsset(url.pathname))}catch{await route.abort()}
  })
  // The product diff is CSS-only. Serve the exact leased baseline CSS and omit
  // the two added mobile blocks, so before/after remains reproducible after edits.
  if(phase==='before'){
   await page.route('**/src/l12/mobileViewport.css*',route=>route.fulfill({contentType:'application/javascript',body:`import { updateStyle } from '/@vite/client'; updateStyle('density-baseline',${JSON.stringify(baselineCss)}); export default '';`}))
   await page.route(/(?:SingleCardPicker|DisasterPoolPicker)\.vue\?.*type=style.*index=1/,route=>route.fulfill({contentType:'application/javascript',body:'export default ""'}))
  }
  if(profile.insets) await cdp.send('Emulation.setSafeAreaInsetsOverride',{insets:profile.insets}).catch(()=>{})
  for (const [name,query,kind] of cases) {
   if(pickersOnly&&!['single-picker','disaster-pool'].includes(kind))continue
   if(profile.desktop && kind==='payment') continue
   const key=`${profile.width}x${profile.height}${profile.insets?'-safe':''}${profile.font?'-font'+profile.font:''}-${name}`
   if(only&&!key.includes(only))continue
   const result={key,desktop:!!profile.desktop,errors:[]}
   try {
    await page.goto(`http://127.0.0.1:5197/__l12_battle_preview__?canvas=1&${profile.desktop?'':'mobile=1&'}field=full&piles=8&hand=6&${query}`)
    await page.waitForFunction(() => !!window.__l12State?.game)
    await page.locator('.board-stage').waitFor()
    await page.waitForTimeout(250)
    let selector='.prompt-panel'
    if(name==='master-actions')await page.evaluate(()=>{window.__l12State.game.players[0].master.abilities=Array.from({length:4},(_,i)=>({id:'density-'+i,label:i?'选项 '+i+'：一段比较长的效果文字，必须可以完整阅读并保持竖向同宽。':'选项 0：简短效果',enabled:true}))})
    if(kind==='master'){await page.locator('.mini-master').last().click();selector='.master-dialog'}
    if(kind==='faction'){await page.locator('.resource-faction-action').last().click();selector='.faction-effect-dialog'}
    if(kind==='graveyard'){await page.locator('.graveyard').last().click();selector='.graveyard-window'}
    if(kind==='payment'){
     if(await page.locator('.prompt-minimize').count()) await page.locator('.prompt-minimize').click()
     await page.locator('.resource-morale-summary').last().click();selector='.mobile-morale-overlay'
    }
    if(['single-picker','disaster-pool'].includes(kind)){
     await page.evaluate(async kind=>{
      const entry=await fetch('/__l12_battle_preview__.js').then(r=>r.text())
      const vueUrl=entry.match(/from "([^\"]*\/deps\/vue\.js[^\"]*)"/)[1]
      const {createApp,h,ref}=await import(vueUrl)
      const host=document.createElement('div');document.querySelector('#l12-landscape-teleports').append(host)
      const cards=window.__l12State.game.players[0].hand
      if(kind==='single-picker'){
       const {default:Picker}=await import('/src/l12/SingleCardPicker.vue')
       const app=createApp({setup(){return()=>h(Picker,{title:'选择卡牌：验证长名称与横竖卡图',items:Array.from({length:8},(_,i)=>({...cards[i%cards.length],id:'qa-'+i,number:'qa-'+i,name:'卡牌长名称·候选 '+i,nameZh:'卡牌长名称·候选 '+i,cardType:i%2?'disaster':'legion',cardImageId:cards[i%cards.length].cardId})),onSelect:()=>window.__pickerSelected=true,onClose:()=>{app.unmount();host.remove()}})}});app.mount(host)
      }else{
       const {default:Picker}=await import('/src/l12/site/DisasterPoolPicker.vue')
       host.style.cssText='position:fixed;inset:0;display:grid;place-items:center;background:#000b'
       createApp({setup(){const selected=ref([]);return()=>h('section',{class:'battle-dialog',style:'background:#101718;padding:8px;max-width:90%;max-height:90%;overflow:auto'},[h(Picker,{cards:Array.from({length:8},(_,i)=>({...cards[i%cards.length],id:'qa-'+i,nameZh:'很长的天灾名称·候选 '+i,cardType:'disaster'})),modelValue:selected.value,'onUpdate:modelValue':v=>selected.value=v})])}}).mount(host)
      }
     },kind)
     selector=kind==='single-picker'?'.single-card-picker':'.battle-dialog'
    }
    await page.locator(selector).waitFor()
    // Apply safe canvas changes after opening so this case tests dialog resize,
    // independently of unrelated battlefield hit-testing under emulated insets.
    if(profile.insets) await page.evaluate(insets=>{
     const root=document.documentElement,w=innerWidth-insets.left-insets.right,h=innerHeight-insets.top-insets.bottom
     const frameW=Math.min(w*.75,h*.75*16/9)
     // Focus changes legitimately refresh inline viewport tokens. A stylesheet
     // override keeps the simulated insets stable during that refresh, just as
     // native env(safe-area-inset-*) remains stable on a real device.
     const style=document.createElement('style')
     style.textContent=':root{'+Object.entries({'viewport-left':insets.left,'viewport-top':insets.top,'viewport-width':w,'viewport-height':h,'mobile-dialog-width':frameW,'mobile-dialog-height':frameW*9/16}).map(([key,value])=>'--l12-'+key+':'+value+'px!important;').join('')+'}'
     document.head.append(style)
     window.dispatchEvent(new Event('l12-viewport-change'))
    },profile.insets)
    if(name==='cards-4') await page.evaluate(()=>{const p=window.__l12State.game.prompts[0];p.text='请选择一张卡牌：较长的标题需要完整阅读并且不能遮住下方候选与确认按钮'; for(const id of p.validChoices)p.data[id+':name']='很长的卡牌名称·用于验证换行后同级选项保持整齐'})
    if(profile.font) await page.evaluate(f=>{document.documentElement.style.fontSize=`${16*f}px`},profile.font)
    await page.waitForTimeout(150)
    if(withArtwork)await page.locator(selector).evaluate(async dialog=>{
     await Promise.all([...dialog.querySelectorAll('img')].map(img=>img.decode().catch(()=>{})))
    })
    result.geometry=await page.locator(selector).evaluate((dialog)=>{
     const rect=el=>{const r=el.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height,right:r.right,bottom:r.bottom}}
     const visible=el=>el.getClientRects().length&&getComputedStyle(el).visibility!=='hidden'
     const nodes=sel=>[...dialog.querySelectorAll(sel)].filter(visible)
     return {
      frame:rect(dialog),mobile:document.documentElement.dataset.l12Mobile,
      header:dialog.querySelector('header')?rect(dialog.querySelector('header')):null,
      body:dialog.querySelector('.prompt-choice-body')?rect(dialog.querySelector('.prompt-choice-body')):null,
      footer:dialog.querySelector('.prompt-action-footer')?rect(dialog.querySelector('.prompt-action-footer')):null,
      title:nodes('h2').map(el=>({rect:rect(el),font:parseFloat(getComputedStyle(el).fontSize)})),
      choices:nodes('.prompt-card-candidate,.response-target-row,.effect-option-list>button,.uniform-text-option-list>button,.master-abilities>button,.faction-effect-actions>button,.graveyard-card-entry,.mobile-morale-choice,.single-card-result-card,.pool-grid>button').map(el=>({class:el.className,...rect(el)})),
      labels:nodes('.prompt-card-candidate__name,.prompt-card-candidate__state,.graveyard-card-name').map(el=>({text:el.textContent,font:parseFloat(getComputedStyle(el).fontSize),...rect(el)})),
      buttons:nodes('button,[role="button"]').map(el=>({text:el.getAttribute('aria-label')||el.textContent?.trim(),...rect(el)})),
      fonts:nodes('h2,p,button,small,span').map(el=>[el.className,parseFloat(getComputedStyle(el).fontSize)]),
      groups:nodes('.prompt-card-strip,.effect-option-list,.uniform-text-option-list,.response-target-list,.master-abilities,.faction-effect-actions,.single-card-grid,.pool-grid').map(group=>({class:group.className,items:[...group.children].filter(visible).map(rect)})),
     }
    })
    const g=result.geometry
    if(!profile.desktop){
     for(const b of g.buttons) if(b.w<43.5||b.h<43.5) result.errors.push(`small hit: ${b.text} ${b.w}x${b.h}`)
     for(const label of g.labels) if(label.text && label.font<11) result.errors.push(`small label ${label.font}: ${label.text}`)
     if(g.body&&g.footer&&g.body.bottom>g.footer.y+1)result.errors.push('body/footer overlap')
     if(g.body&&g.header&&g.header.bottom>g.body.y+1)result.errors.push('header/body overlap')
     if(g.body&&g.body.h<44)result.errors.push('content viewport below 44px')
     if(g.footer&&g.footer.bottom>g.frame.bottom+1)result.errors.push('footer outside frame')
     const cards=g.choices.filter(x=>x.class.includes('prompt-card-candidate'))
     if(cards.length&&Math.max(...cards.map(x=>x.h))-Math.min(...cards.map(x=>x.h))>1)result.errors.push('horizontal heights differ')
     const rows=g.choices.filter(x=>x.class.includes('response-target-row'))
     if(rows.length&&Math.max(...rows.map(x=>x.w))-Math.min(...rows.map(x=>x.w))>1)result.errors.push('vertical widths differ')
     for(const group of g.groups){
      for(const a of group.items)for(const b of group.items){
       if(Math.abs(a.y-b.y)<1&&Math.abs(a.h-b.h)>1)result.errors.push('same-row heights differ: '+group.class)
       if(Math.abs(a.x-b.x)<1&&Math.abs(a.w-b.w)>1)result.errors.push('same-column widths differ: '+group.class)
      }
     }
     const inset=profile.insets||{left:0,right:0,top:0,bottom:0}
     if(g.frame.x<inset.left-1||g.frame.y<inset.top-1||g.frame.right>profile.width-inset.right+1||g.frame.bottom>profile.height-inset.bottom+1)result.errors.push('frame outside safe canvas')
    }
    if(name==='cards-2'){
     const card=page.locator('.prompt-card-candidate').first();await card.focus();await page.keyboard.press('Enter')
     if(await card.getAttribute('aria-pressed')!=='true')result.errors.push('keyboard selection failed')
     await page.keyboard.press('Space');if(await card.getAttribute('aria-pressed')!=='false')result.errors.push('toggle failed')
     await page.locator('.prompt-minimize').click();await page.locator('.prompt-minimized-bar button').first().click();await page.locator('.prompt-panel').waitFor()
    }
    if(name==='disaster-ban'){
     const card=page.locator('.prompt-card-candidate:not(.unavailable)').first();await card.click()
     if(await card.getAttribute('aria-pressed')!=='true')result.errors.push('disaster selection failed')
     const disabled=page.locator('.prompt-card-candidate.unavailable').first();await disabled.click({force:true});if(await disabled.getAttribute('aria-pressed')==='true')result.errors.push('disabled choice selected')
    }
    await page.screenshot({path:path.join(output,key+'.png')})
    if(!profile.desktop){
     const scroll=page.locator(selector).locator('.prompt-choice-body,.graveyard-columns article,.master-content,.faction-effect-content,.single-card-grid,.pool-grid').first()
     if(await scroll.count()){
      await scroll.evaluate(el=>{el.scrollTop=el.scrollHeight})
      await page.screenshot({path:path.join(output,key+'-scrolled.png')})
     }
    }
    // Commands remain entirely inside the sanitized socket fixture.
    result.interactions=[]
    if(name==='cards-2'){
     const card=page.locator('.prompt-card-candidate').first();await card.scrollIntoViewIfNeeded()
     if(profile.desktop)await card.click();else await card.tap()
     if(await card.getAttribute('aria-pressed')!=='true')result.errors.push('touch/pointer selection failed')
     await page.getByRole('button',{name:'确认选择',exact:true}).click()
     const sent=await page.evaluate(()=>window.__sentCommands.at(-1)?.command)
     if(sent?.type!=='resolvePrompt'||sent?.cardInstanceIds?.[0]!=='choice-0')result.errors.push('confirmation changed selected ID')
     result.interactions.push('keyboard Enter/Space, minimize/restore, touch/pointer, explicit confirmation')
    }
    if(name==='yes-no'){
     for(const label of ['同意','不同意']){
      await page.evaluate(()=>window.__resetSentCommands())
      await page.getByRole('button',{name:label,exact:true}).click()
      const confirm=page.getByRole('button',{name:'确认选择',exact:true})
      if(await confirm.count()&&await confirm.isEnabled())await confirm.click()
      const sent=await page.evaluate(()=>window.__sentCommands.at(-1)?.command)
      if(sent?.type!=='resolvePrompt'||sent?.cardInstanceIds?.[0]!== (label==='同意'?'yes':'refuse'))result.errors.push('yes/no command mismatch '+label)
     }
     result.interactions.push('yes/no retain authoritative choice IDs')
    }
    if(name==='payment'){
     await page.locator(selector).getByRole('button',{name:'取消打出',exact:true}).click()
     const sent=await page.evaluate(()=>window.__sentCommands.at(-1)?.command)
     if(sent?.cardInstanceIds?.[0]!=='cancel')result.errors.push('payment cancel command mismatch')
     result.interactions.push('payment cancel')
    }
    if(kind==='single-picker'){
     const select=page.locator('.single-card-actions button').last()
     await select.scrollIntoViewIfNeeded()
     await page.waitForTimeout(250)
     result.pickerHit=await select.evaluate(el=>{const r=el.getBoundingClientRect(),hit=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2),grid=el.closest('.single-card-grid');return {rect:r.toJSON(),scroll:grid.scrollTop,behavior:getComputedStyle(grid).scrollBehavior,target:el.textContent,hit:hit?.outerHTML?.slice(0,180),matches:el===hit||el.contains(hit)}})
     await page.evaluate(()=>{window.__densityClicks=[];document.addEventListener('click',e=>window.__densityClicks.push({x:e.clientX,y:e.clientY,target:e.target?.outerHTML?.slice(0,180)}),{capture:true})})
     if(!result.pickerHit.matches)result.errors.push('picker click center obstructed')
     await select.click()
     result.pickerClicks=await page.evaluate(()=>window.__densityClicks)
     result.pickerAfter=await select.evaluate(el=>({rect:el.getBoundingClientRect().toJSON(),scroll:el.closest('.single-card-grid').scrollTop}))
     if(!await page.evaluate(()=>window.__pickerSelected))result.errors.push('picker selection event failed')
     await page.locator('.single-card-picker>header button').click()
     if(await page.locator('.single-card-picker').count())result.errors.push('picker close failed')
    }
    if(name==='countdown'){
     if(!(await page.locator('.prompt-auto-close').textContent())?.includes('秒'))result.errors.push('countdown text missing')
    }
    if(!profile.desktop){
     if(name==='master'||name==='master-actions')await page.getByRole('button',{name:'关闭',exact:true}).click()
     if(name==='graveyard')await page.getByRole('button',{name:'关闭墓地',exact:true}).click()
    }
   } catch(error){result.errors.push(String(error))}
   results.push(result)
   console.log(key, result.errors.length?result.errors.join('; '):'PASS')
  }
  await context.close()
 }
} finally {await browser.close();fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(results,null,2))}
if(phase==='after'){
 const baseline=JSON.parse(fs.readFileSync(path.resolve(output,'../before/results.json'),'utf8'))
 for(const result of results.filter(x=>x.desktop)){
  const old=baseline.find(x=>x.key===result.key)
  if(JSON.stringify(old?.geometry)!==JSON.stringify(result.geometry))result.errors.push('desktop geometry/style differs from baseline')
 }
 for(const result of results.filter(x=>/font/.test(x.key))){
  const normal=results.find(x=>x.key===result.key.replace(/-font[\d.]+/,''))
  if(result.geometry?.labels.length&&normal?.geometry?.labels.length&&result.geometry.labels[0].font<=normal.geometry.labels[0].font*1.1)result.errors.push('font enlargement did not reach candidate labels')
 }
 fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(results,null,2))
 const files=['src/l12/mobileViewport.css','src/l12/SingleCardPicker.vue','src/l12/site/DisasterPoolPicker.vue','scripts/check-mobile-responsive-layouts.mjs','scripts/verify-mobile-dialog-density.mjs']
 const evidence={baselineRef,withArtwork,cases:results.length,failures:results.filter(x=>x.errors.length).map(x=>({key:x.key,errors:x.errors})),desktopComparisons:results.filter(x=>x.desktop).length,profiles,sourceHashes:Object.fromEntries(files.map(file=>[file,createHash('sha256').update(fs.readFileSync(file)).digest('hex')])),limitations:['Safe areas are browser CSS simulation after opening, not a physical device test.','Font enlargement changes the root rem size to 125%/150%; native OS text autosizing is not emulated.','Public artwork is read-only and optional; no production gameplay requests are sent.']}
 fs.writeFileSync(path.join(output,'evidence.json'),JSON.stringify(evidence,null,2))
 fs.writeFileSync(path.join(output,'tracked.diff'),execFileSync('git',['diff','--binary'],{encoding:'utf8'}))
 if(results.some(x=>x.errors.length))process.exitCode=1
}
