import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const front = path.resolve(import.meta.dirname, '..')
const out = process.env.L12_SELECTOR_OUTPUT || 'D:/GPT/Legion12/artifacts/deck-private-lookup-20261007/selector-browser-r1'
fs.mkdirSync(out, { recursive: true })
const { chromium } = createRequire(import.meta.url)('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const tracked = ['scripts/verify-private-deck-selector.mjs', 'src/App.vue', 'src/l12/SavedDeckSelector.vue', 'src/l12/DeckProfile.vue', 'src/l12/decks.ts', 'src/l12/platform.ts']
const hashes = () => Object.fromEntries(tracked.map(file => [file, crypto.createHash('sha256').update(fs.readFileSync(path.join(front, file))).digest('hex')]))
const report = { sourceBefore: hashes(), cases: [], sizes: [], errors: [], productionWrites: 0,
  limitations: ['Actual App, Vue component, platform request and summary reader with synthetic HTTP data.',
    'Selector confirmation emits metadata; parent mode validation is covered separately, not a live match test.',
    'Edge emulated viewports are not physical iOS Safari, WeChat IME or hardware safe areas.'] }
const catalog = JSON.parse(fs.readFileSync(path.join(front, 'public/data/l12/cards.s1.json'), 'utf8').replace(/^\uFEFF/, ''))
const master = catalog.find(card => card.cardType === 'master') || catalog[0]
const entry = `
import {createApp,defineComponent,h,ref} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import App from '/src/App.vue'
import Selector from '/src/l12/SavedDeckSelector.vue'
import {platformState,authState} from '/src/l12/platform.ts'
import '/src/style.css'
platformState.account={id:'selector-qa',username:'合成验收',role:'player',permissions:[]};platformState.token='synthetic-only';authState.initialized=true;authState.verified=true
const open=ref(true), confirmations=[];window.__selector={confirmations,platformState,open}
const fixture=defineComponent({setup(){return()=>h(Selector,{open:open.value,mode:'casual',catalog:${JSON.stringify([master])},currentDeckId:'qa-01',currentDeckRevision:1,currentDeckName:'合成牌库01',usesSeasonRestrictions:false,onCancel:()=>open.value=false,onConfirm:deck=>{confirmations.push(deck);open.value=false}})}})
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/selector',component:fixture,meta:{immersive:true}}]})
await router.push('/selector');createApp(App).use(router).mount('#app')
`
const server = await createServer({ root: front, configLoader: 'runner', cacheDir: 'D:/GPT/Legion12/cache/private-selector-browser/vite',
  server: { host: '127.0.0.1', port: 0 }, plugins: [{ name: 'private-selector-fixture',
    resolveId(id) { if (id === '/__selector__.js') return id }, load(id) { if (id === '/__selector__.js') return entry },
    configureServer(vite) { vite.middlewares.use((request, response, next) => {
      if (new URL(request.url || '/', 'http://fixture').pathname !== '/__selector__') return next()
      response.setHeader('Content-Type', 'text/html'); response.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/__selector__.js"></script></body></html>')
    }) },
  }] })
let browser, page
const decks = Array.from({ length: 75 }, (_, index) => ({ id: `qa-${String(index + 1).padStart(2, '0')}`, revision: 1,
  name: `合成牌库${String(index + 1).padStart(2, '0')}`, masterId: master.id, updatedAt: '2026-10-07T00:00:00Z',
  publicationId: null, publicationVersion: null, counts: { main: 40, uncountedMain: 0, morale: 8, special: 0, bench: 0 }, legal: false, legalityReason: '合成赛季限制' }))
async function check(name, work) { await work(); report.cases.push(name) }
try {
  await server.listen(); browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const [width, height] of [[1920,1080],[1366,768],[1024,500],[844,390],[390,844],[320,568]]) {
    const size = `${width}x${height}`, reads = []
    const context = await browser.newContext({ viewport: { width, height }, hasTouch: true, isMobile: width < 820 })
    await context.routeWebSocket(/.*/, socket => socket.close())
    await context.route('**/*', async route => {
      const request = route.request(), url = new URL(request.url())
      if (url.hostname !== '127.0.0.1') return route.abort()
      if (url.pathname.startsWith('/api/')) {
        reads.push({ path: url.pathname + url.search, method: request.method() })
        if (url.pathname === '/api/decks/summaries') {
          const number = Number(url.searchParams.get('page')), pageSize = Number(url.searchParams.get('pageSize')), keyword = url.searchParams.get('keyword') || ''
          const all = decks.filter(deck => deck.name.includes(keyword))
          return route.fulfill({ json: { items: all.slice((number-1)*pageSize,number*pageSize), page: number, pageSize, total: all.length,
            generation: 1, permissionVersion: 1, catalogVersion: 'A'.repeat(64), policyVersion: 1,
            facets: { masters: all.length ? [{ masterId: master.id, count: all.length }] : [], legal: 0, illegal: all.length } } })
        }
        if (url.pathname === '/api/auth/me') return route.fulfill({ json: { id: 'selector-qa', username: '合成验收', role: 'player', permissions: [] } })
        if (url.pathname === '/api/friends/overview') return route.fulfill({ json: { friends: [], requests: [], blocked: [] } })
        if (url.pathname.includes('notifications')) return route.fulfill({ json: { items: [], nextCursor: null } })
        if (url.pathname.startsWith('/api/telemetry/')) return route.fulfill({ status: 204 })
        return route.fulfill({ json: [] })
      }
      if (url.pathname.startsWith('/card-assets/')) {
        const assets = 'D:/L12-assets/published/current', file = path.resolve(assets, decodeURIComponent(url.pathname.slice('/card-assets/'.length)))
        if (file.startsWith(path.resolve(assets) + path.sep) && fs.existsSync(file)) return route.fulfill({ path: file })
        return route.abort()
      }
      return route.continue()
    })
    page = await context.newPage(); page.setDefaultTimeout(8000)
    page.on('pageerror', error => report.errors.push(error.stack || error.message))
    await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__selector__`)
    const modal = page.getByRole('dialog'), rows = modal.locator('.selector-list>button')
    await rows.first().filter({ hasText: '合成牌库01' }).waitFor()
    await check(`${size}: bounded directory, no body reads and season metadata does not disable casual selection`, async () => {
      assert.equal(await rows.count(), 30); assert.equal(await rows.first().isEnabled(), true)
      assert.equal(reads.some(item => item.path === '/api/decks' || item.path.startsWith('/api/decks/by-id/')), false)
    })
    await check(`${size}: all pages reachable while footer remains outside the scroll area`, async () => {
      for (const first of ['合成牌库31','合成牌库61']) {
        await modal.getByRole('button', { name: '下一页', exact: true }).click()
        await rows.first().filter({ hasText: first }).waitFor()
      }
      assert.equal(await rows.count(), 15); await rows.last().scrollIntoViewIfNeeded()
      const geometry = await modal.evaluate(element => {
        const r=element.getBoundingClientRect(), footer=element.querySelector('footer').getBoundingClientRect(), last=element.querySelector('.selector-list>button:last-child').getBoundingClientRect()
        return { left:r.left,right:r.right,top:r.top,bottom:r.bottom,footerTop:footer.top,footerBottom:footer.bottom,lastTop:last.top,lastBottom:last.bottom,width:innerWidth,height:innerHeight }
      })
      assert.ok(geometry.left>=-1 && geometry.right<=geometry.width+1 && geometry.top>=-1 && geometry.bottom<=geometry.height+1, JSON.stringify(geometry))
      assert.ok(geometry.lastTop>=geometry.top && geometry.lastBottom<=geometry.footerTop+1, JSON.stringify(geometry))
      assert.ok(geometry.footerBottom<=geometry.height+1)
      await page.screenshot({ animations:'disabled',timeout:15000,path:path.join(out,`${size}-last-page.png`) })
      report.sizes.push({ width,height,geometry })
    })
    await check(`${size}: search controls remain reachable and confirmation emits exactly the chosen stable identity`, async () => {
      const search=modal.getByRole('searchbox'); await search.scrollIntoViewIfNeeded(); await search.fill('合成牌库75')
      await modal.getByRole('button',{name:'搜索',exact:true}).click()
      await rows.first().filter({hasText:'合成牌库75'}).waitFor(); assert.equal(await rows.count(),1)
      await rows.first().click(); await modal.getByRole('button',{name:'确认使用',exact:true}).click()
      await modal.waitFor({state:'hidden'})
      const confirmations=await page.evaluate(()=>window.__selector.confirmations)
      assert.equal(confirmations.length,1); assert.equal(confirmations[0].id,'qa-75'); assert.equal(confirmations[0].revision,1)
      assert.equal('cardIds' in confirmations[0],false)
    })
    await context.close()
  }
  report.sourceAfter=hashes(); assert.deepEqual(report.sourceAfter,report.sourceBefore); assert.deepEqual(report.errors,[])
  console.log(`Private selector browser: ${report.cases.length} checks, ${report.sizes.length} viewports, errors=0`)
} catch(error) {
  report.failure=String(error.stack)
  if(page&&!page.isClosed()) await page.screenshot({timeout:15000,path:path.join(out,'failed.png')}).catch(()=>undefined)
  throw error
} finally { fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2)); await browser?.close(); await server.close() }
