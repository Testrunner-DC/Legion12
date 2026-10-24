import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { compileScript, compileTemplate, parse } from '@vue/compiler-sfc'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT
if (!output) throw new Error('L12_QA_OUT is required and must be validated by Assert-L12GeneratedPath before this check')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const panelPath = path.join(root, 'src/l12/site/AdminAlternateArtsPanel.vue')
const pagerPath = path.join(root, 'src/l12/site/PagedCollection.vue')
const storePath = path.resolve(root, '../服务端WebSocket/TwelveLegions/L12PlatformStore.AlternateArts.cs')
const panel = fs.readFileSync(panelPath, 'utf8')
const pager = fs.readFileSync(pagerPath, 'utf8')
const store = fs.readFileSync(storePath, 'utf8')
const checks = []
const check = (name, run) => { run(); checks.push(name) }

check('component-script-and-template-compile', () => {
  const parsed = parse(panel, { filename: panelPath })
  assert.deepEqual(parsed.errors, [])
  assert.ok(parsed.descriptor.scriptSetup)
  assert.ok(parsed.descriptor.template)
  compileScript(parsed.descriptor, { id: 'gallery-dispatch-pagination' })
  const template = compileTemplate({
    id: 'gallery-dispatch-pagination', filename: panelPath, source: parsed.descriptor.template.content,
    scoped: parsed.descriptor.styles.some(style => style.scoped),
  })
  assert.deepEqual(template.errors, [])
})

check('grant-read-boundary-is-truthful', () => {
  assert.match(store, /AlternateArtGrants[\s\S]*?OrderByDescending\(row => row\.GrantedAt\)\.Take\(1000\)/)
  assert.match(panel, /服务端单次最多返回最近 1000 条，本页筛选与分页仅作用于这批记录/)
  assert.doesNotMatch(panel, /派发记录总数\s*\{\{\s*grants\.length/)
})

check('existing-pager-bounds-the-list', () => {
  assert.match(panel, /import PagedCollection from '\.\/PagedCollection\.vue'/)
  assert.match(panel, /<PagedCollection :items="filteredGrants" :page-size="10" label="派发记录"/)
  assert.match(panel, /v-for="grantRow in visibleGrants"/)
  assert.doesNotMatch(panel, /v-for="grantRow in grants"/)
  assert.match(pager, /watch\(\(\) => props\.items, \(\) => \{ if \(!serverMode\.value\) page\.value = 1 \}\)/)
  assert.match(pager, /watch\(pages, count => \{ if \(!serverMode\.value\) page\.value = Math\.min\(page\.value, count\) \}\)/)
  assert.match(pager, /第 \{\{ currentPage \}\} \/ \{\{ pages \}\} 页 · 共 \{\{ total \}\} 条/)
})

check('filters-and-empty-states-are-complete', () => {
  assert.match(panel, /grantFilters = reactive\(\{ query: '', sourceKind: '', state:/)
  assert.match(panel, /item\.username, item\.alternateArtId, art\?\.displayName, item\.sourceReference/)
  assert.match(panel, /grantFilters\.sourceKind && item\.sourceKind !== grantFilters\.sourceKind/)
  assert.match(panel, /grantFilters\.state === 'active'[\s\S]*grantFilters\.state === 'revoked'/)
  assert.match(panel, /没有符合当前筛选条件的派发记录/)
  assert.match(panel, /暂无派发记录/)
})

check('reload-and-mutations-replace-the-page-source', () => {
  assert.match(panel, /let rightsLoadRequest = 0/)
  assert.match(panel, /const request = \+\+rightsLoadRequest/)
  assert.match(panel, /if \(request !== rightsLoadRequest\) return[\s\S]*grants\.value = nextGrants/)
  assert.match(panel, /async function reloadRights\(\) \{ rightsLoaded\.value = false; await loadRights\(\) \}/)
  assert.match(panel, /workspace === 'rights' \? reloadRights\(\) : load\(\)/)
  assert.match(panel, /function invalidateRightsState\(\)[\s\S]*rightsLoadRequest \+= 1[\s\S]*grants\.value = \[\][\s\S]*awardRules\.value = \[\][\s\S]*seasonCatalog\.value = null[\s\S]*betaGrantPreview\.value = null[\s\S]*resetRule\(\)/)
  assert.match(panel, /watch\(\(\) => platformState\.account\?\.id,[\s\S]*invalidateRightsState\(\)[\s\S]*if \(next && workspace\.value === 'rights'\) void loadRights\(\)/)
  assert.match(panel, /onBeforeUnmount\(\(\) => \{[\s\S]*rightsLoadRequest \+= 1/)
  assert.match(panel, /grants\.value = \[saved, \.\.\.grants\.value\.filter/)
  assert.match(panel, /grants\.value = grants\.value\.map/)
  assert.match(panel, /grants\.value = \[\.\.\.created, \.\.\.grants\.value\.filter/)
  assert.match(panel, /grants\.value = \[\.\.\.saved, \.\.\.grants\.value\.filter/)
})

check('season-select-reuses-authoritative-catalog', () => {
  assert.match(panel, /type SeasonCatalog/)
  assert.match(panel, /adminApi\.seasonCatalog\(\)/)
  assert.match(panel, /seasonCatalog\.value\?\.current\.seasonId/)
  assert.match(panel, /seasonCatalog\.value\?\.next\?\.seasonId/)
  assert.match(panel, /seasonCatalog\.value\?\.archives\.forEach/)
  assert.match(panel, /原规则 · \$\{current\}（赛季目录中不存在）/)
  assert.match(panel, /<label v-if="ruleForm\.kind !== 'event'">对应赛季<select v-model="ruleForm\.seasonId"/)
  assert.doesNotMatch(panel, /赛季编号<input v-model\.trim="ruleForm\.seasonId"/)
  assert.match(panel, /if \(ruleForm\.kind !== 'event' && !ruleForm\.seasonId\.trim\(\)\) \{ notice\('请选择对应赛季'\)/)
})

check('season-catalog-failure-does-not-invent-options', () => {
  assert.match(panel, /nextSeasonError = error instanceof Error \? error\.message : '赛季目录读取失败'/)
  assert.match(panel, /seasonCatalog\.value = nextSeasons/)
  assert.match(panel, /:disabled="!ruleSeasonOptions\.length"/)
  assert.match(panel, /seasonCatalogError \? '赛季目录读取失败' : '请选择赛季'/)
  assert.doesNotMatch(panel, /seasonOptions[\s\S]{0,500}(S01|S02)/)
})

const entry = `
import {createApp} from 'vue'
import '/src/style.css'
import Panel from '/src/l12/site/AdminAlternateArtsPanel.vue'
import {adminApi,authState,platformState} from '@/l12/platform'
const now='2026-10-24T00:00:00.000Z'
const accounts={A:{id:'admin-a',username:'管理员A',role:'admin',createdAt:now,publicHistory:false,permissions:['admin.accounts.read','admin.content.draft']},B:{id:'admin-b',username:'管理员B',role:'admin',createdAt:now,publicHistory:false,permissions:['admin.accounts.read','admin.content.draft']}}
const art={id:'alt-qa',artCode:'ALT-QA',baseCardId:'S01-0101',baseCardName:'吕布',displayName:'吕布异画',artist:'QA',mediaAssetId:'',imageUrl:'',thumbnailUrl:'',active:true,createdAt:now,updatedAt:now,cardImageId:'S01-0101b',builtIn:true,kind:'alternate-art',products:[],rarities:[]}
const grantsFor=account=>Array.from({length:account==='A'?23:account==='B'?12:0},(_,index)=>({id:'grant-'+account+'-'+(index+1),accountId:'player-'+account+'-'+(index+1),username:'玩家'+account+'-'+String(index+1).padStart(2,'0'),alternateArtId:art.id,sourceKind:index%3===0?'manual':index%3===1?'rank-reached':'season-final',sourceReference:index===22?'终页奖励':'批次'+(index+1),grantedAt:new Date(Date.parse(now)-index*60000).toISOString(),revokedAt:index===4?now:undefined}))
const rulesFor=account=>account==='A'?[{id:'rule-old',alternateArtId:art.id,kind:'season-final',seasonId:'OLD-SEASON',eventId:'',masterId:'',minimumTierIndex:1,active:true,createdAt:now,updatedAt:now}]:[{id:'rule-current',alternateArtId:art.id,kind:'season-final',seasonId:'S01',eventId:'',masterId:'',minimumTierIndex:1,active:true,createdAt:now,updatedAt:now}]
const seasonCatalog={current:{seasonId:'S01',name:'当前赛季'},next:{seasonId:'S02',name:'下赛季草稿'},archives:[{seasonId:'S00',name:'历史赛季'}],automaticActivationEnabled:false,operationsVersion:1}
const calls={grants:[],rules:[],seasons:[]}
let delayA=false,delayedA=null,failSeason=false
const accountKey=()=>platformState.account?.id==='admin-a'?'A':platformState.account?.id==='admin-b'?'B':''
adminApi.alternateArtGrants=async()=>{const account=accountKey();calls.grants.push(account||'none');if(account==='A'&&delayA){delayA=false;return new Promise(resolve=>{delayedA=resolve})}return structuredClone(grantsFor(account))}
adminApi.alternateArtAwardRules=async()=>{const account=accountKey();calls.rules.push(account||'none');return structuredClone(rulesFor(account))}
adminApi.seasonCatalog=async()=>{calls.seasons.push(accountKey()||'none');if(failSeason)throw new Error('合成目录失败');return structuredClone(seasonCatalog)}
adminApi.alternateArts=async()=>[structuredClone(art)]
adminApi.alternateArtProducts=async()=>[]
adminApi.alternateArtRarities=async()=>[]
adminApi.siteCategories=async()=>[]
adminApi.searchAlternateArts=async()=>({items:[structuredClone(art)],total:1,page:1,pageSize:20})
adminApi.searchRegularGalleryCards=async()=>({items:[],total:0,page:1,pageSize:20})
adminApi.accounts=async()=>[]
authState.verified=true
platformState.token='qa-a-token'
platformState.account=accounts.A
window.__dispatchQa={calls,delayA(){delayA=true},resolveA(){const resolve=delayedA;delayedA=null;resolve?.(structuredClone(grantsFor('A')))},failSeason(value){failSeason=value},setAccount(value){platformState.account=value?accounts[value]:null;platformState.token=value?'qa-'+value.toLowerCase()+'-token':''}}
createApp(Panel).mount('#app')
`

const visualOnly = process.env.L12_QA_VISUAL_ONLY === '1'
const viewports = visualOnly
  ? [{ width: 1440, height: 900 }, { width: 390, height: 844 }]
  : [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 844, height: 390 }]
const browserChecks = []
const pageErrors = []
const blockedRequests = []
let browser
let server
let failure
try {
  server = await createServer({
    root, configLoader: 'runner', cacheDir: process.env.L12_QA_CACHE,
    server: { host: '127.0.0.1', port: 0, strictPort: false },
    plugins: [{
      name: 'gallery-dispatch-pagination-fixture',
      resolveId(id) { if (id === '/__gallery_dispatch__.js') return id },
      load(id) { if (id === '/__gallery_dispatch__.js') return entry },
      configureServer(devServer) {
        devServer.middlewares.use((request, response, next) => {
          if (request.url?.match(/^\/__gallery_dispatch__(\?|$)/)) {
            response.setHeader('Content-Type', 'text/html')
            response.end('<meta name="viewport" content="width=device-width,initial-scale=1"><style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#eee}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__gallery_dispatch__.js"></script>')
            return
          }
          next()
        })
      },
    }],
  })
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of viewports) {
    const page = await browser.newPage({ viewport })
    page.on('pageerror', error => pageErrors.push({ viewport, message: error.message }))
    await page.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.hostname === '127.0.0.1' && url.port === String(port)) return route.continue()
      blockedRequests.push({ viewport, url: url.href, resourceType: route.request().resourceType() })
      return route.abort()
    })
    await page.goto(`http://127.0.0.1:${port}/__gallery_dispatch__`)
    await page.getByRole('heading', { name: '画廊卡片后台' }).waitFor()
    await page.getByRole('button', { name: '权益与派发' }).click()
    const list = page.locator('.grant-list')
    await list.getByText(/当前读取最近 23 条记录/).waitFor()
    const seasonSelect = page.getByLabel('对应赛季')
    if (visualOnly) {
      await seasonSelect.waitFor()
      const awardRules = page.locator('.award-rules')
      await awardRules.scrollIntoViewIfNeeded()
      await awardRules.screenshot({ path: path.join(output, `gallery-award-rules-${viewport.width}x${viewport.height}.png`) })
      const pager = page.getByRole('navigation', { name: '派发记录分页' })
      await pager.getByRole('button', { name: '下一页' }).click()
      assert.match(await pager.innerText(), /第 2 \/ 3 页 · 共 23 条/)
      await list.scrollIntoViewIfNeeded()
      await list.screenshot({ path: path.join(output, `gallery-grant-list-${viewport.width}x${viewport.height}.png`) })
      browserChecks.push({ viewport, visualOnly: true, awardRulesCaptured: true, grantListCaptured: true,
        currentPage: 2, total: 23, horizontalOverflow: await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1) })
      await page.close()
      continue
    }
    assert.equal(await list.locator('article').count(), 10, `first page size mismatch at ${viewport.width}x${viewport.height}`)
    const pager = page.getByRole('navigation', { name: '派发记录分页' })
    assert.match(await pager.innerText(), /第 1 \/ 3 页 · 共 23 条/)
    await pager.getByRole('button', { name: '下一页' }).click()
    assert.match(await list.locator('article').first().innerText(), /玩家A-11/)
    await list.getByPlaceholder('玩家、异画或派发原因').fill('玩家A-23')
    await list.getByText('玩家A-23', { exact: true }).waitFor()
    assert.equal(await list.locator('article').count(), 1)
    assert.equal(await page.getByRole('navigation', { name: '派发记录分页' }).count(), 0, 'filter did not converge the current page')
    await list.getByPlaceholder('玩家、异画或派发原因').fill('无匹配记录')
    await list.getByText('没有符合当前筛选条件的派发记录。').waitFor()
    await list.getByRole('button', { name: '清除筛选' }).click()
    assert.equal(await list.locator('article').count(), 10)
    assert.deepEqual(await seasonSelect.locator('option').evaluateAll(options => options.map(option => ({ value: option.value, text: option.textContent }))), [
      { value: '', text: '请选择赛季' },
      { value: 'S01', text: '当前赛季 · 当前赛季（S01）' },
      { value: 'S02', text: '下赛季草稿 · 下赛季草稿（S02）' },
      { value: 'S00', text: '历史赛季 · 历史赛季（S00）' },
    ])
    await page.locator('.award-rules article').filter({ hasText: 'OLD-SEASON' }).getByRole('button', { name: '编辑' }).click()
    assert.equal(await seasonSelect.inputValue(), 'OLD-SEASON')
    assert.match(await seasonSelect.locator('option[value="OLD-SEASON"]').innerText(), /原规则.*赛季目录中不存在/)
    await page.evaluate(() => window.__dispatchQa.failSeason(true))
    await page.locator('.alternate-admin>header').getByRole('button', { name: '刷新当前资料' }).click()
    await page.getByText('合成目录失败', { exact: true }).waitFor()
    assert.equal(await seasonSelect.inputValue(), 'OLD-SEASON', 'directory failure discarded the existing rule season')
    await page.evaluate(() => { window.__dispatchQa.failSeason(false); window.__dispatchQa.delayA() })
    const grantsBeforeDelay = await page.evaluate(() => window.__dispatchQa.calls.grants.length)
    await page.locator('.alternate-admin>header').getByRole('button', { name: '刷新当前资料' }).click()
    await page.waitForFunction(before => window.__dispatchQa.calls.grants.length > before, grantsBeforeDelay)
    await page.getByRole('button', { name: '异画 / 联动卡' }).click()
    await page.evaluate(() => { window.__dispatchQa.setAccount('B'); window.__dispatchQa.resolveA() })
    await page.getByRole('button', { name: '权益与派发' }).click()
    await list.getByText(/当前读取最近 12 条记录/).waitFor()
    assert.match(await list.locator('article').first().innerText(), /玩家B-01/)
    assert.equal(await list.getByText(/玩家A-/).count(), 0, 'account B inherited account A grants')
    await page.screenshot({ path: path.join(output, `gallery-dispatch-${viewport.width}x${viewport.height}.png`), fullPage: true })
    const callsBeforeLogout = await page.evaluate(() => window.__dispatchQa.calls.grants.length)
    await page.evaluate(() => window.__dispatchQa.setAccount(null))
    await list.getByText('暂无派发记录。').waitFor()
    await page.waitForTimeout(30)
    assert.equal(await page.evaluate(() => window.__dispatchQa.calls.grants.length), callsBeforeLogout, 'logout issued an unauthenticated rights reload')
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `horizontal overflow at ${viewport.width}x${viewport.height}`)
    browserChecks.push({ viewport, firstPage: 10, total: 23, filteredPageConverged: true, emptyState: true,
      seasonOptions: ['S01', 'S02', 'S00', 'OLD-SEASON'], directoryFailurePreservedRule: true,
      staleAccountResponseRejected: true, logoutClearedWithoutReload: true, horizontalOverflow: false })
    await page.close()
  }
  assert.deepEqual(pageErrors, [])
} catch (error) {
  failure = error instanceof Error ? { name: error.name, message: error.message, stack: error.stack } : { message: String(error) }
} finally {
  await browser?.close()
  await server?.close()
}

const sha256 = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const report = {
  status: failure ? 'failed' : 'passed', failure, checks, browserChecks, pageErrors, blockedRequests,
  mode: visualOnly ? 'visual-only' : 'full-interaction',
  apiMode: 'in-memory synthetic adminApi and synthetic accounts only; every non-fixture origin is blocked; no HTTP persistence',
  loadedGrantBoundary: 1000, pageSize: 10,
  lifecycle: { browserClosed: true, serverClosed: true },
  sourceHashes: {
    'src/l12/site/AdminAlternateArtsPanel.vue': sha256(panelPath),
    'src/l12/site/PagedCollection.vue': sha256(pagerPath),
    '../服务端WebSocket/TwelveLegions/L12PlatformStore.AlternateArts.cs': sha256(storePath),
    'scripts/verify-gallery-dispatch-pagination-20261024.mjs': sha256(fileURLToPath(import.meta.url)),
  },
}
fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
console.log(JSON.stringify({ status: report.status, checks: checks.length + browserChecks.length, output, lifecycle: report.lifecycle }, null, 2))
if (failure) throw Object.assign(new Error(failure.message), { cause: failure })
