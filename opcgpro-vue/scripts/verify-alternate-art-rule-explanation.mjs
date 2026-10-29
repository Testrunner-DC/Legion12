import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { createServer } from 'vite'

const root = fileURLToPath(new URL('../', import.meta.url))
const output = process.env.L12_QA_OUT, cache = process.env.L12_QA_CACHE
assert(output && cache, 'Require explicit governed output and cache paths; caller must run Assert-L12GeneratedPath and initializer')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const inputs = ['src/l12/site/AdminAlternateArtsPanel.vue', 'src/l12/site/alternateArtRuleExplanation.ts']
const pins = () => Object.fromEntries(inputs.map(file => [file, createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')]))
const before = pins(), results = [], errors = []
const entry = `
import {createApp} from 'vue'
import '/src/style.css'
import Panel from '/src/l12/site/AdminAlternateArtsPanel.vue'
import {adminApi,platformState,authState} from '@/l12/platform'
const kinds=['rank-reached','season-final','master-champion-season-final','event']
const rules=kinds.map((kind,index)=>({id:'rule-'+index,kind,alternateArtId:'qa-art',seasonId:'season-qa',eventId:'event-qa',minimumTierIndex:index,masterId:index===2?'master-qa':'',active:index!==1,createdAt:'',updatedAt:''}))
const calls=[];window.__qa={calls}
platformState.token='synthetic-token';platformState.account={id:'synthetic-admin',username:'合成管理员',role:'admin',permissions:['admin.content.draft'],createdAt:'',publicHistory:false};authState.verified=true
const reads={alternateArtProducts:[],alternateArtRarities:[],siteCategories:[],alternateArts:[{id:'qa-art',displayName:'演示异画',artCode:'QA',active:true}],alternateArtAwardRules:rules,
 searchAlternateArts:{items:[],page:1,pageSize:20,total:0},searchAlternateArtGrants:{items:[],page:1,pageSize:20,total:0},seasonCatalog:{current:{seasonId:'season-qa',name:'演示赛季'},archives:[]}}
for(const [key,value] of Object.entries(reads))adminApi[key]=async()=>{calls.push(key);return value}
for(const key of ['saveAlternateArtAwardRule','dispatchAlternateArtEvent','grantAlternateArt'])adminApi[key]=async()=>{calls.push('WRITE:'+key);throw Error('Unexpected write')}
createApp(Panel).mount('#app')
`
const server = await createServer({ root, configLoader: 'runner', cacheDir: cache, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'rule-explanation-isolated-qa', enforce: 'pre',
  resolveId(id) { if (id === '/__rule_qa__.js') return id },
  load(id) { if (id === '/__rule_qa__.js') return entry },
  configureServer(vite) { vite.middlewares.use((req, res, next) => { if (req.url === '/__rule_qa__') { res.setHeader('Content-Type','text/html'); res.end('<html lang="zh"><meta charset="utf-8"><style>html,body{margin:0;background:#080d11;color:#eee;font-family:Arial,"Microsoft YaHei",sans-serif}#app{box-sizing:border-box;max-width:1100px;margin:auto;padding:12px}</style><div id="app"></div><script type="module" src="/__rule_qa__.js"></script></html>') } else next() }) },
}] })
let browser
try {
  fs.mkdirSync(output, { recursive: true }); await server.listen()
  const url = `http://127.0.0.1:${server.httpServer.address().port}/__rule_qa__`
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  page.on('pageerror', e => errors.push(e.message))
  await page.route('**/*', route => { const url = new URL(route.request().url()); if (url.origin !== new URL(page.url() === 'about:blank' ? url : page.url()).origin || url.pathname.startsWith('/api/')) return route.abort(); return route.continue() })
  for (const viewport of [{width:1440,height:900},{width:390,height:844},{width:844,height:390}]) {
    await page.setViewportSize(viewport); await page.goto(url); await page.getByRole('button',{name:'权益与派发',exact:true}).click()
    const panel = page.locator('.award-rules'), draft = panel.locator('.rule-draft-explanation')
    await panel.locator('article').first().waitFor()
    assert.equal(await panel.locator('article .rule-explanation').count(),4)
    assert.match(await panel.locator('article').nth(1).innerText(),/当前已停用/)
    const selects = panel.locator('.form-grid select'), kind = selects.nth(0)
    for (const value of ['rank-reached','season-final','master-champion-season-final','event']) {
      await kind.selectOption(value)
      const t = await draft.innerText(); assert(t.includes('达成条件') && t.includes('发放时机'))
      assert.match(t, value === 'rank-reached' ? /立即检查.*补发/s : value === 'event' ? /选择玩家并执行派发/ : /归档该旧赛季/)
      if(value==='rank-reached') { await selects.nth(2).selectOption('4'); assert.match(await draft.innerText(),/第 5 档/); await selects.nth(1).selectOption('season-qa'); assert.match(await draft.innerText(),/season-qa/) }
      if(value==='master-champion-season-final') { const option=selects.nth(2).locator('option').nth(1); const id=await option.getAttribute('value'), name=await option.innerText(); assert(id && name); await selects.nth(2).selectOption(id); assert((await draft.innerText()).includes(name)); await selects.nth(2).selectOption(''); assert.match(await draft.innerText(),/所有主宰/) }
      const checkbox=panel.locator('.form-grid input[type=checkbox]'); await checkbox.uncheck(); assert.match(await draft.innerText(),/当前已停用/); await checkbox.check()
      const geometry=await panel.evaluate(el=>{const targets=[el,...el.querySelectorAll('.rule-explanation, .rule-explanation dd')];return{overflow:document.documentElement.scrollWidth>innerWidth+1,bad:targets.filter(n=>{const b=n.getBoundingClientRect();return b.left < -1 || b.right>innerWidth+1 || n.scrollWidth>n.clientWidth+1}).map(n=>n.tagName)}})
      assert.deepEqual(geometry,{overflow:false,bad:[]}); results.push({viewport,kind:value,geometry})
    }
    await panel.locator('article').nth(2).getByRole('button',{name:'编辑',exact:true}).click(); assert.match(await draft.innerText(),/master-qa/)
    await draft.screenshot({path:path.join(output,`draft-${viewport.width}x${viewport.height}.png`)})
    await page.screenshot({path:path.join(output,`rules-${viewport.width}x${viewport.height}.png`),fullPage:true})
    const calls=await page.evaluate(()=>window.__qa.calls); assert(!calls.some(c=>c.startsWith('WRITE:')))
  }
  assert.deepEqual(errors,[]); assert.deepEqual(pins(),before)
  fs.writeFileSync(path.join(output,'browser-result.json'),JSON.stringify({passed:results.length,failed:0,results,errors,pins:before,scope:'Actual SFC, synthetic read-only API; no live awards, real server, full site navigation or physical-device proof'},null,2))
  console.log(JSON.stringify({passed:results.length,failed:0,pins:before}))
} finally { await browser?.close(); await server.close() }
