import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'

const appRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repo = path.resolve(appRoot, '..')
for (const key of ['L12_QA_OUT', 'L12_VITE_CACHE_DIR', 'L12_BROWSER_PROFILE']) assert.ok(process.env[key], `${key} required`)
execFileSync(process.env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-Command',
  '. $env:L12_STORAGE_GUARD; @($env:L12_QA_OUT,$env:L12_VITE_CACHE_DIR,$env:L12_BROWSER_PROFILE,$env:TEMP) | ForEach-Object { Assert-L12GeneratedPath -Path $_ -Purpose "DSC-01B browser QA" }'],
{ env: { ...process.env, L12_STORAGE_GUARD: path.join(repo, 'ops/windows/L12GeneratedStorage.ps1') } })
const phase = process.env.L12_QA_PHASE || 'browser-r1'
assert.match(phase, /^[a-z0-9-]+$/)
const out = path.join(process.env.L12_QA_OUT, phase)
assert.ok(!fs.existsSync(out), 'old QA evidence must not be overwritten')
fs.mkdirSync(out, { recursive: true })
const requireApp = createRequire(path.join(appRoot, 'package.json'))
const { createServer } = await import(pathToFileURL(requireApp.resolve('vite')).href)
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT ||
  'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const sourcePaths = ['src/l12/platform.ts', 'src/l12/site/AdminMasterAnalyticsPanel.vue',
  'src/l12/site/AdminMatchesPanel.vue', 'src/l12/site/DisasterDecisionSummary.vue']
const hashes = () => Object.fromEntries(sourcePaths.map(file => [file,
  crypto.createHash('sha256').update(fs.readFileSync(path.join(appRoot, file))).digest('hex').toUpperCase()]))
const before = hashes()
const ts = requireApp('typescript')
const apiSource = fs.readFileSync(path.join(appRoot, 'src/l12/platform.ts'), 'utf8')
const parsedApi = ts.createSourceFile('platform.ts', apiSource, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const adminDeclaration = parsedApi.statements.filter(ts.isVariableStatement)
  .flatMap(statement => [...statement.declarationList.declarations]).find(declaration => declaration.name.getText(parsedApi) === 'adminApi')
const masterApi = adminDeclaration.initializer.properties.find(property => property.name?.getText(parsedApi) === 'masterAnalytics')
assert.ok(masterApi && ts.isArrowFunction(masterApi.initializer), 'test the real TS wrapper, not a copied request builder')
let mappedUrl = ''
const apiCode = ts.transpileModule(`const build=${masterApi.initializer.getText(parsedApi)};`,
  { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
const buildRequest = new Function('platformRequest', 'localDateBoundary', `${apiCode};return build;`)(
  url => { mappedUrl = url; return Promise.resolve({}) }, (date, end) => date ? `${date}-${end ? 'to' : 'from'}` : undefined)
await buildRequest({ from: '2026-10-01', to: '2026-10-17', masterId: 'qa-master', opponentMasterId: 'other-master',
  initiative: 'first', minimumSample: 1, effectVersion: 'all', seasonId: 'qa-season', disasterMode: 'season',
  disasterPoolId: 'A'.repeat(64), disasterOrigin: 'server-setup-timeout' })
const mapped = new URL(mappedUrl, 'http://127.0.0.1').searchParams
for (const [key, value] of Object.entries({ fromUtc: '2026-10-01-from', toUtc: '2026-10-17-to',
  minimumSampleSize: '1', opponentMasterId: 'other-master', initiative: 'first', disasterMode: 'season',
  disasterPoolId: 'A'.repeat(64), disasterOrigin: 'server-setup-timeout' })) assert.equal(mapped.get(key), value)
assert.equal(mapped.has('minimumSample'), false)
console.log('PASS api-query-mapping')
const platformStub = `import {reactive} from 'vue';
window.__dscStubLoads=(window.__dscStubLoads||0)+1;
export const platformState=reactive({account:{id:'qa-a',permissionVersion:1},token:'synthetic-qa-a'});
export const authState=reactive({verified:true});
export class PlatformRequestError extends Error { constructor(message,status,code){super(message);this.status=status;this.code=code;} }
export const calls=window.__dscCalls=[];export const pending=window.__dscPending=new Map();let sequence=0;
function request(kind,args){const id=++sequence;calls.push({id,kind,args});return new Promise((resolve,reject)=>pending.set(id,{resolve,reject}));}
export const adminApi={masterAnalytics:q=>request('master',q),matches:q=>request('list',q),playerMatches:(id,q)=>request('list',{...q,accountId:id}),match:id=>request('detail',{matchId:id})};
export const getEffectiveOperationsPolicy=async()=>({season:{id:'qa-season',name:'合成赛季'}});
export const hasPermission=()=>true;
// The new lazy scoped child is imported by the real parent. This older fixture
// never opens it: fail loudly if it unexpectedly creates a new query.
export const localDateBoundary=value=>value;
export const platformRequest=()=>Promise.reject(new Error('unexpected scoped query in phase2 fixture'));`
const cards = [{ id: 'qa-master', cardType: 'master', nameZh: '合成主宰' },
  { id: 'D-A', cardType: 'destruction', nameZh: '合成天灾甲' },
  { id: 'D-FRESH', cardType: 'destruction', nameZh: '当前天灾' },
  { id: 'D-STALE', cardType: 'destruction', nameZh: '过期天灾' },
  { id: 'D-SECRET', cardType: 'destruction', nameZh: '禁止泄露哨兵Q7' }]
const entry = `import {createApp,reactive,h,nextTick} from 'vue';
import '/src/style.css';import '/src/l12/site/uiSystem.css';
import {createRouter,createMemoryHistory} from 'vue-router';
import AdminMaster from '/src/l12/site/AdminMasterAnalyticsPanel.vue';
import AdminMatches from '/src/l12/site/AdminMatchesPanel.vue';
import Summary from '/src/l12/site/DisasterDecisionSummary.vue';
import {platformState,authState,calls,pending} from 'dsc-platform';
const cards=${JSON.stringify(cards)};
const state=reactive({mode:'none',key:0,summary:undefined});
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/',component:{render:()=>null}}]});
const app=createApp({setup(){return()=>h('main',{class:'qa-host'},state.mode==='master'?h(AdminMaster,{key:state.key}):state.mode==='matches'?h(AdminMatches,{key:state.key}):state.mode==='summary'?h(Summary,{summary:state.summary,cards}):null)}});
app.use(router);await router.push('/');await router.isReady();app.mount('#app');
function master(args,variant){const selected=args.masterId||null;const result={items:[{masterId:'qa-master',participantSamples:40,distinctMatches:20,distinctDecks:2,usageRate:1,deckShare:1,wins:20,winRate:.5,firstSamples:20,firstWinRate:.5,secondSamples:20,secondWinRate:.5,averageDurationSeconds:300}],matchups:[],trend:[],selectedMasterId:selected,popularDecks:[],cards:{items:[]},disasters:{coverage:{participantSamples:2,distinctMatches:1,usableSamples:2,usableMatches:1,unknownSamples:0,incompleteSamples:0,notApplicableSamples:0},items:selected?[{cardId:variant==='stale'?'D-STALE':variant==='fresh'?'D-FRESH':'D-A',initiative:'first',action:'ban',step:0,origin:args.disasterOrigin||'player',opportunities:2,selections:1,selectedWins:1,distinctMatches:1,selectedMatches:1,selectionRate:.5,selectedWinRate:null}]:[],pools:[{id:'A'.repeat(64),mode:'all',cardIds:['D-A']}],selectedMasterId:selected,mode:args.disasterMode||null,poolId:args.disasterPoolId||null,origin:args.disasterOrigin||null,minimumWinRateSamples:30}};if(variant==='legacy')delete result.disasters;if(variant==='unknown'){result.disasters.items=[];result.disasters.coverage={...result.disasters.coverage,usableSamples:0,usableMatches:0,unknownSamples:2};}return result;}
function summary(variant='exact'){return {coverage:variant==='active'?'private':variant==='unknown'?'unknown':'exact',status:variant==='active'?'active':'complete',mode:'all',firstPlayer:0,publicCardId:'D-A',composition:[{cardId:variant==='active'?'D-SECRET':'D-A',source:'player-pick',ownerIndex:0}],decisions:[{step:0,action:'ban',actorIndex:0,initiative:'first',candidateCardIds:[variant==='active'?'D-SECRET':'D-A'],chosenCardId:variant==='active'?'D-SECRET':'D-A',origin:'player',offeredSequence:1,resolvedSequence:2}]};}
function match(id){return {matchId:id,modeId:'friendly',status:'completed',players:[{playerIndex:0,displayName:'合成甲-'+id,masterId:'qa-master',result:'win'},{playerIndex:1,displayName:'合成乙',masterId:'qa-master',result:'loss'}],startedUtc:'2026-10-17T00:00:00Z',endedUtc:'2026-10-17T00:05:00Z',durationSeconds:300,commandCount:0};}
function detail(id,variant){return {summary:match(id),participants:[{playerIndex:0,displayName:'合成甲-'+id,masterId:'qa-master',result:'win',deckCards:[]},{playerIndex:1,displayName:'合成乙',result:'loss',deckCards:[]}],replay:[],cardFacts:[],coverage:{exactFacts:0,inferredFacts:0,partialFacts:0},...(variant==='legacy'?{}:{disasterSetup:summary(variant)})};}
window.qa={calls,state,async mount(mode){state.mode='none';await nextTick();calls.length=0;pending.clear();state.key++;state.mode=mode;await nextTick();},async resolveMaster(id,variant){pending.get(id).resolve(master(calls.find(c=>c.id===id).args,variant));pending.delete(id);await nextTick();},async resolveList(id){pending.get(id).resolve({items:[match('m1'),match('m2')],total:2});pending.delete(id);await nextTick();},async resolveDetail(id,variant){pending.get(id).resolve(detail(calls.find(c=>c.id===id).args.matchId,variant));pending.delete(id);await nextTick();},async reject(id){pending.get(id).reject(new Error('合成加载失败'));pending.delete(id);await nextTick();},async setAccount(id){platformState.account={id,permissionVersion:1};platformState.token='synthetic-'+id;await nextTick();},async showSummary(variant){state.summary=variant==='legacy'?undefined:summary(variant);state.mode='summary';await nextTick();}};
window.qaReady=true;`
const rawIndex = fs.readFileSync(path.join(appRoot, 'index.html'), 'utf8')
const document = rawIndex.replace(/<script\b[^>]*type="module"[\s\S]*?<\/script>/g, '')
  .replace('</head>', '<style>html,body{margin:0;box-sizing:border-box;background:#0c141c;color:#e9e5d9;font-family:"Microsoft YaHei","Noto Sans SC",sans-serif}*,*:before,*:after{box-sizing:border-box}.qa-host{padding:14px;width:100%;min-width:0}button,input,select{font:inherit}</style></head>')
  .replace('</body>', '<script type="module" src="/__dsc_main.js"></script></body>')
const server = await createServer({ root: appRoot, configLoader: 'runner', cacheDir: process.env.L12_VITE_CACHE_DIR,
  logLevel: 'error', server: { host: '127.0.0.1', port: 0, strictPort: false }, plugins: [{
    name: 'dsc01b-real-component-fixture',
    enforce: 'pre',
    resolveId(id, importer) {
      const normalized = id.replaceAll('\\', '/')
      if (id === '/__dsc_main.js') return '\0dsc-entry'
      if (id === 'dsc-platform' || id === '@/l12/platform' || id === './platform' && importer?.replaceAll('\\', '/').endsWith('/disasterAnalytics.ts')
        || /\/src\/l12\/platform(?:\.ts)?$/.test(normalized)) return '\0dsc-platform'
      if (id === '@/l12/decks' || /\/src\/l12\/decks(?:\.ts)?$/.test(normalized)) return '\0dsc-decks'
      if (id === '@/l12/specialAssets' || /\/src\/l12\/specialAssets(?:\.ts)?$/.test(normalized)) return '\0dsc-assets'
      if (id === '@/l12/CardImage.vue' || normalized.endsWith('/src/l12/CardImage.vue')) return '\0dsc-card'
      if (/^\.\/(DeckSnapshotViewer|AdminAccountPicker|AdminMatchContextLinks)\.vue$/.test(id)) return '\0dsc-child'
    },
    load(id) {
      if (id === '\0dsc-entry') return entry
      if (id === '\0dsc-platform') return platformStub
      if (id === '\0dsc-decks') return `export const loadDeckCatalog=async()=>(${JSON.stringify(cards)});`
      if (id === '\0dsc-assets') return 'export const masterProfileUrl=()=>"";'
      if (id === '\0dsc-card') return `import {defineComponent,h} from 'vue';export default defineComponent({props:['cardId','alt'],setup:p=>()=>h('span',{'data-card-id':p.cardId},p.alt)});`
      if (id === '\0dsc-child') return `import {defineComponent,h} from 'vue';export default defineComponent({setup:()=>()=>h('span')});`
    },
    configureServer(vite) { vite.middlewares.use((req, res, next) => {
      if (req.url?.startsWith('/__dsc01b__')) { res.setHeader('Content-Type', 'text/html; charset=utf-8'); res.end(document); return }
      next()
    }) },
  }] })
await server.listen()
const address = server.httpServer.address()
const url = `http://127.0.0.1:${address.port}/__dsc01b__`
const cases = [{ id: 'api-query-mapping', passed: true }], errors = []
let runError = null
const context = await chromium.launchPersistentContext(process.env.L12_BROWSER_PROFILE, {
  channel: 'msedge', headless: true, viewport: { width: 1366, height: 900 },
  args: ['--disable-gpu', '--disable-background-networking', '--disable-sync', '--no-first-run'],
})
const page = await context.newPage()
page.setDefaultTimeout(10000)
page.on('pageerror', error => errors.push(error.message))
await page.route('**/*', route => {
  const request = new URL(route.request().url())
  return request.hostname === '127.0.0.1' || request.protocol === 'data:' ? route.continue() : route.abort()
})
async function check(id, run) {
  try { await run(); cases.push({ id, passed: true }); console.log(`PASS ${id}`) }
  catch (error) {
    cases.push({ id, passed: false, error: error.stack }); console.log(`FAIL ${id}: ${error.message}`)
    await page.screenshot({ path: path.join(out, `${id}-failed.png`) })
    if (await page.evaluate(() => window.qa.calls.length === 0)) throw error
  }
}
async function waitCall(kind, count) {
  await page.waitForFunction(({ kind, count }) => window.qa.calls.filter(call => call.kind === kind).length >= count, { kind, count }, { timeout: 8000 })
  return await page.evaluate(kind => window.qa.calls.filter(call => call.kind === kind).at(-1), kind)
}
async function masterSelected(variant) {
  await page.evaluate(() => window.qa.mount('master'))
  let call = await waitCall('master', 1)
  await page.evaluate(({ id, variant }) => window.qa.resolveMaster(id, variant), { id: call.id, variant })
  await page.locator('.master-list > button').click()
  call = await waitCall('master', 2)
  await page.evaluate(({ id, variant }) => window.qa.resolveMaster(id, variant), { id: call.id, variant })
  await page.getByRole('region', { name: '主宰天灾禁选分析' }).waitFor()
}
try {
  await page.goto(url)
  await page.waitForFunction(() => window.qaReady)
  assert.equal(await page.evaluate(() => window.__dscStubLoads), 1, 'one canonical platform shim must serve both components and fixture')
  for (const viewport of [{ width: 1366, height: 900 }, { width: 844, height: 390 }, { width: 390, height: 844 }]) {
    await check(`visible-${viewport.width}`, async () => {
      await page.setViewportSize(viewport)
      await masterSelected()
      const panel = page.getByRole('region', { name: '主宰天灾禁选分析' })
      assert.match(await panel.innerText(), /1 \/ 2/)
      assert.match(await panel.innerText(), /50\.0%/)
      assert.match(await panel.innerText(), /先手第1禁/)
      assert.match(await panel.innerText(), /不足 30/)
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)
      assert.equal(overflow, false, 'document overflow must not be hidden by fixture CSS')
      const select = await page.getByLabel('禁选决定来源').evaluate(el => ({ background: getComputedStyle(el).backgroundColor, option: getComputedStyle(el.options[0]).backgroundColor }))
      assert.notEqual(select.background, 'rgb(255, 255, 255)'); assert.notEqual(select.option, 'rgb(255, 255, 255)')
      await panel.scrollIntoViewIfNeeded()
      await page.screenshot({ path: path.join(out, `visible-${viewport.width}.png`) })
    })
  }
  await check('legacy-unknown-and-restricted', async () => {
    await masterSelected('legacy')
    assert.match(await page.locator('.disaster-panel').innerText(), /尚未提供禁选分析/)
    await masterSelected('unknown')
    assert.match(await page.locator('.disaster-panel').innerText(), /历史未知/)
    assert.equal(await page.locator('.disaster-table-scroll tbody tr').count(), 0)
    await page.evaluate(() => window.qa.mount('master'))
    let call = await waitCall('master', 1)
    await page.evaluate(id => window.qa.resolveMaster(id), call.id)
    await page.locator('.master-list > button').click()
    call = await waitCall('master', 2)
    await page.evaluate(id => {
      const pending = window.__dscPending.get(id)
      const resolve = pending.resolve
      pending.resolve = result => { result.disasters.privatePickGroupsHidden = true; resolve(result) }
      return window.qa.resolveMaster(id)
    }, call.id)
    assert.match(await page.locator('.disaster-panel').innerText(), /私密选牌机会分组未返回/)
  })
  await check('filters-and-late-response', async () => {
    await masterSelected()
    await page.getByLabel('先后手', { exact: true }).selectOption('second')
    const old = await waitCall('master', 3)
    await page.getByLabel('先后手', { exact: true }).selectOption('first')
    const current = await waitCall('master', 4)
    assert.equal(current.args.initiative, 'first')
    await page.evaluate(id => window.qa.resolveMaster(id, 'fresh'), current.id)
    await page.evaluate(id => window.qa.resolveMaster(id, 'stale'), old.id)
    assert.match(await page.locator('.disaster-panel').innerText(), /当前天灾/)
    assert.doesNotMatch(await page.locator('.disaster-panel').innerText(), /过期天灾/)
    await page.getByLabel('禁选决定来源').selectOption('server-setup-timeout')
    const timeout = await waitCall('master', 5)
    assert.equal(timeout.args.disasterOrigin, 'server-setup-timeout')
    await page.evaluate(id => window.qa.resolveMaster(id), timeout.id)
    await page.getByLabel('禁选卡池快照').selectOption('A'.repeat(64))
    const pool = await waitCall('master', 6)
    assert.equal(pool.args.disasterPoolId, 'A'.repeat(64))
    await page.evaluate(id => window.qa.resolveMaster(id), pool.id)
  })
  await check('account-change-fails-closed', async () => {
    await masterSelected()
    await page.getByLabel('先后手', { exact: true }).selectOption('second')
    const old = await waitCall('master', 3)
    await page.evaluate(() => window.qa.setAccount('qa-b'))
    await page.evaluate(id => window.qa.resolveMaster(id, 'stale'), old.id)
    assert.equal(await page.locator('.disaster-panel').count(), 0)
    assert.equal(await page.locator('.master-list > button').count(), 0)
  })
  await check('detail-late-response-and-retry', async () => {
    await page.evaluate(() => window.qa.mount('matches'))
    let call = await waitCall('list', 1)
    await page.evaluate(id => window.qa.resolveList(id), call.id)
    const first = await waitCall('detail', 1)
    await page.locator('.match-row').nth(1).click()
    const second = await waitCall('detail', 2)
    await page.evaluate(id => window.qa.resolveDetail(id), second.id)
    await page.evaluate(id => window.qa.resolveDetail(id), first.id)
    assert.match(await page.locator('.detail-header').innerText(), /m2/)
    assert.match(await page.locator('.disaster-summary').innerText(), /摘要不依赖录像留存/)
    await page.locator('.match-row').first().click()
    const failed = await waitCall('detail', 3)
    await page.evaluate(id => window.qa.reject(id), failed.id)
    assert.equal(await page.locator('.disaster-summary').count(), 0)
    await page.getByRole('button', { name: '重试详情' }).click()
    call = await waitCall('detail', 4)
    await page.evaluate(id => window.qa.resolveDetail(id, 'legacy'), call.id)
    assert.match(await page.locator('.disaster-summary').innerText(), /此后端版本尚未提供/)
  })
  await check('summary-privacy-unknown-and-candidates', async () => {
    await page.evaluate(() => window.qa.mount('none'))
    await page.evaluate(() => window.qa.showSummary('active'))
    assert.match(await page.locator('.disaster-summary').innerText(), /进行中/)
    assert.doesNotMatch(await page.locator('.disaster-summary').innerText(), /禁止泄露哨兵Q7|D-SECRET/)
    assert.equal(await page.locator('.setup-decisions').count(), 0)
    await page.evaluate(() => window.qa.showSummary('unknown'))
    assert.match(await page.locator('.disaster-summary').innerText(), /历史禁选事实未知/)
    assert.equal(await page.locator('.setup-decisions').count(), 0)
    await page.evaluate(() => window.qa.showSummary('exact'))
    await page.locator('.setup-decisions summary').click()
    assert.match(await page.locator('.setup-decisions details p').innerText(), /合成天灾甲/)
  })
  assert.deepEqual(errors, [], 'real component page errors')
  assert.deepEqual(hashes(), before, 'QA source must not drift')
  assert.equal(cases.length, 9, 'all requested component cases must execute')
} catch (error) {
  runError = error.stack
  console.error(error.stack)
} finally {
  await context.close()
  await server.close()
  const report = { schema: 1, status: cases.length === 9 && cases.every(item => item.passed) && errors.length === 0 && !runError ? 'passed' : 'failed',
    source: before, cases, pageErrors: errors, runError, browser: 'installed Edge headless',
    limits: ['Real Vue SFCs compiled and rendered; synthetic in-memory API/identity responses, not real HTTP in this QA.',
      'CardImage, account picker, context links and deck viewer are stubs; shared matrix/scope/pager and the new summary are real.',
      'No production data, no physical mobile device, no claim about complete site integration.'] }
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2))
  if (report.status !== 'passed') process.exitCode = 1
}
