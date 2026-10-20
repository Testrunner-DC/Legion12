import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

const front = path.resolve(import.meta.dirname, '..')
const repo = path.dirname(front)
const output = process.env.L12_QA_OUT
const cache = process.env.L12_QA_CACHE
assert(output && cache && path.isAbsolute(output) && path.isAbsolute(cache), 'Explicit governed output/cache required')
const quote = value => `'${value.replaceAll("'", "''")}'`
execFileSync(process.env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-Command',
  `. ${quote(path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'))}; Assert-L12GeneratedPath -Path ${quote(output)} | Out-Null; Assert-L12GeneratedPath -Path ${quote(cache)} | Out-Null`])
fs.mkdirSync(output, { recursive: true })
fs.mkdirSync(cache, { recursive: true })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry = `
import { createApp, defineComponent, h, ref } from 'vue'
import '/src/style.css'
import SiteMediaPicker from '/src/l12/site/SiteMediaPicker.vue'
import { adminApi } from '/src/l12/platform.ts'
const image = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="120" height="90"><rect width="120" height="90" fill="#275b68"/></svg>')
const rows = Array.from({length:1031}, (_,index)=>({id:'asset-'+index,kind:'card-art',altText:index===1030?'遥远旧卡图':'合成卡图 '+index,contentHash:'hash-'+index,thumbnailUrl:image,referenceCount:0}))
rows.push(...Array.from({length:3},(_,index)=>({id:'rule-'+index,kind:'rule',altText:'合成规则图 '+index,contentHash:'rhash-'+index,thumbnailUrl:image,referenceCount:0})))
const requests=[]; const held=[]; let delayNext=false; let failNext=false
adminApi.searchSiteMedia=async options=>{
 requests.push({...options})
 const matches=rows.filter(row=>row.kind===options.kind && (!options.search || [row.altText,row.id,row.contentHash].some(text=>text.toLowerCase().includes(options.search.toLowerCase()))))
 const size=24; const page=Math.min(options.page,Math.max(1,Math.ceil(matches.length/size)))
 const result={items:matches.slice((page-1)*size,page*size),page,pageSize:size,total:matches.length,hasMore:page*size<matches.length}
 if(failNext){failNext=false;throw new Error('合成请求失败')}
 if(delayNext){delayNext=false;await new Promise(resolve=>held.push(resolve))}
 return result
}
const model=ref('');const selected=ref();const kind=ref('card-art');const mounted=ref(true)
window.__mediaQa={requests,held,delay:()=>delayNext=true,fail:()=>failNext=true,release:()=>held.splice(0).forEach(resolve=>resolve()),kind:value=>kind.value=value,model:()=>model.value,unmount:()=>mounted.value=false}
createApp(defineComponent({setup:()=>()=>h('main',{style:'position:fixed;inset:0;z-index:3500;background:#101b23;padding:30px;color:white'},[
 h('h1','素材选择验收'), mounted.value?h(SiteMediaPicker,{modelValue:model.value,kind:kind.value,selectedMedia:selected.value,'onUpdate:modelValue':value=>model.value=value,onSelected:value=>selected.value=value}):null,
 h('output',{id:'selected-id'},model.value)
])})).mount('#app')
`
const server = await createServer({ configFile: false, root: front, cacheDir: path.join(cache, 'vite'),
  plugins: [vue(), tailwindcss(), { name: 'media-pagination-qa',
    resolveId(id) { if (id === '/__media-entry.js') return '\0media-pagination-qa' },
    load(id) { if (id === '\0media-pagination-qa') return entry },
    configureServer(instance) {
    instance.middlewares.use((request, response, next) => {
      if (request.url === '/__media-qa') { response.setHeader('content-type','text/html'); response.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module" src="/__media-entry.js"></script></body></html>'); return }
      next()
    })
  }}], resolve: { alias: { '@': path.join(front, 'src') } }, server: { host:'127.0.0.1',port:0 }, logLevel:'warn' })
let context
const errors=[]
const report={schema:1,status:'RUNNING',api:'synthetic UI only; real HTTP permission/pagination covered by SiteMediaPaginationTests',viewports:[],cases:[],sourceSha256:{}}
for(const leaf of ['src/l12/site/SiteMediaPicker.vue','scripts/verify-site-media-pagination.mjs'])
  report.sourceSha256[leaf]=createHash('sha256').update(fs.readFileSync(path.join(front,leaf))).digest('hex')
try {
  await server.listen()
  const origin=server.resolvedUrls.local[0]
  context=await chromium.launchPersistentContext(path.join(cache,'edge-profile'),{channel:'msedge',headless:true})
  await context.route('**/*',route=>new URL(route.request().url()).hostname==='127.0.0.1'?route.continue():route.abort())
  const page=await context.newPage()
  page.on('pageerror',error=>errors.push(error.message))
  const modal=()=>page.getByRole('dialog',{name:'选择已上传素材',exact:true})
  const show=()=>page.locator('.media-choice-trigger').click()
  for(const viewport of [{width:1366,height:900},{width:390,height:844},{width:844,height:390}]) {
    await page.setViewportSize(viewport)
    await page.goto(origin+'__media-qa')
    await page.locator('.media-choice-trigger').waitFor()
    assert.equal(await page.evaluate(()=>window.__mediaQa.requests.length),0)
    await show()
    await modal().getByText('共 1031 项',{exact:false}).waitFor()
    assert.equal(await modal().locator('.site-media-picker-grid button').count(),24)
    await modal().getByLabel('搜索素材',{exact:true}).fill('遥远旧卡图')
    await modal().getByRole('button',{name:'搜索',exact:true}).click()
    await modal().getByRole('button',{name:/遥远旧卡图/}).waitFor()
    for(const button of [modal().getByRole('button',{name:'关闭素材选择'}),modal().getByRole('button',{name:'搜索',exact:true}),modal().getByRole('button',{name:/遥远旧卡图/})]) {
      await button.scrollIntoViewIfNeeded()
      assert(await button.evaluate(element=>{const box=element.getBoundingClientRect();return element.contains(document.elementFromPoint(box.x+box.width/2,box.y+box.height/2))}), 'button center is obscured')
    }
    await page.screenshot({path:path.join(output,'media-picker-'+viewport.width+'x'+viewport.height+'.png')})
    await modal().getByRole('button',{name:/遥远旧卡图/}).click()
    assert.equal(await page.locator('#selected-id').innerText(),'asset-1030')
    await show()
    await modal().getByRole('button',{name:'下一页',exact:true}).click()
    await modal().getByText('第 2 页',{exact:false}).waitFor()
    await modal().locator('.site-media-picker-grid button').first().click()
    assert.equal(await page.locator('#selected-id').innerText(),'asset-24')
    report.viewports.push({...viewport,lazy:true,oldAssetSearch:true,paging:true,hitTesting:true})
  }
  await show()
  await modal().getByText('共 1031 项',{exact:false}).waitFor()
  await page.evaluate(()=>window.__mediaQa.fail())
  await modal().getByLabel('搜索素材',{exact:true}).fill('合成')
  await modal().getByRole('button',{name:'搜索',exact:true}).click()
  await modal().getByRole('alert').waitFor()
  assert(await modal().locator('.site-media-picker-grid button').first().isDisabled())
  assert.equal(await page.locator('#selected-id').innerText(),'asset-24')
  await modal().getByRole('button',{name:'重试',exact:true}).click()
  await modal().getByText('共 1030 项',{exact:false}).waitFor()
  report.cases.push('failure preserves selection, stale rows disabled, exact request retry')
  await page.evaluate(()=>window.__mediaQa.delay())
  await modal().getByRole('button',{name:'下一页',exact:true}).click()
  await page.waitForFunction(()=>window.__mediaQa.held.length===1)
  await page.evaluate(()=>window.__mediaQa.kind('rule'))
  await modal().getByText('共 3 项',{exact:false}).waitFor()
  await page.evaluate(()=>window.__mediaQa.release())
  await page.waitForFunction(()=>document.querySelectorAll('.site-media-picker-grid button').length===3)
  assert.equal(await modal().locator('.site-media-picker-grid button').count(),3)
  await modal().locator('.site-media-picker-grid button').first().click()
  assert.equal(await page.locator('#selected-id').innerText(),'rule-0')
  report.cases.push('kind change rejects old delayed response')
  await page.evaluate(()=>window.__mediaQa.delay())
  await show()
  await page.waitForFunction(()=>window.__mediaQa.held.length===1)
  await page.keyboard.press('Escape')
  assert(await page.locator('.media-choice-trigger').evaluate(element=>element===document.activeElement))
  await show()
  await modal().getByText('共 3 项',{exact:false}).waitFor()
  await page.evaluate(()=>window.__mediaQa.release())
  await page.keyboard.press('Escape')
  await page.getByRole('button',{name:'清除素材选择',exact:true}).click()
  assert.equal(await page.locator('#selected-id').innerText(),'')
  report.cases.push('closed generation cannot overwrite reopened picker; Escape restores focus; explicit clear')
  assert.deepEqual(errors,[])
  report.status='PASSED'
} catch(error) { report.status='FAILED';report.error=String(error.stack||error);throw error }
finally {
  if(context) await context.close()
  await server.close()
  report.closed=true;report.errors=errors
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2)+'\n')
}
console.log('Actual SiteMediaPicker native acceptance passed: three sizes, search/paging/hit testing, failure/retry, delayed generations and keyboard')
