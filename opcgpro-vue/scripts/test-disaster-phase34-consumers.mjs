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
  '. $env:L12_STORAGE_GUARD; @($env:L12_QA_OUT,$env:L12_VITE_CACHE_DIR,$env:L12_BROWSER_PROFILE,$env:TEMP) | ForEach-Object { Assert-L12GeneratedPath -Path $_ -Purpose "DSC phase34 QA" }'],
{ env: { ...process.env, L12_STORAGE_GUARD: path.join(repo, 'ops/windows/L12GeneratedStorage.ps1') } })
const phase = process.env.L12_QA_PHASE || 'browser-r1'
assert.match(phase, /^[a-z0-9-]+$/)
const out = path.join(process.env.L12_QA_OUT, phase)
assert.ok(!fs.existsSync(out), 'preserve previous evidence')
fs.mkdirSync(out, { recursive: true })
const requireApp = createRequire(path.join(appRoot, 'package.json'))
const ts = requireApp('typescript')
const { createServer } = await import(pathToFileURL(requireApp.resolve('vite')).href)
const vue = (await import(pathToFileURL(requireApp.resolve('@vitejs/plugin-vue')).href)).default
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT ||
  'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const sourcePaths = ['src/l12/disasterAnalytics.ts', 'src/l12/platform.ts', 'src/l12/site/ScopedDisasterAnalytics.vue',
  'src/l12/site/VisibleDisasterSummary.vue', 'src/l12/site/PublicDeckDisasterStatistics.vue',
  'src/l12/site/TournamentDisasterSummary.vue', 'src/l12/site/DeckSnapshotViewer.vue',
  'src/l12/site/AdminCardAnalyticsPanel.vue', 'src/l12/site/AdminMasterAnalyticsPanel.vue', 'src/l12/MatchRecords.vue',
  'src/l12/site/PublicDeckDetailPage.vue', 'src/l12/site/TournamentPublicDetail.vue', 'src/l12/site/TournamentAccountDetail.vue']
const hashes = () => Object.fromEntries(sourcePaths.map(file => [file,
  crypto.createHash('sha256').update(fs.readFileSync(path.join(appRoot, file))).digest('hex').toUpperCase()]))
const before = hashes()
const apiSource = fs.readFileSync(path.join(appRoot, 'src/l12/platform.ts'), 'utf8')
const apiAst = ts.createSourceFile('platform.ts', apiSource, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const localDate = apiAst.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'localDateBoundary')
assert.ok(localDate, 'reuse existing date boundary, not server-local date parsing')
const dateCode = ts.transpileModule(localDate.getText(apiAst), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
const platformStub = `import {reactive} from 'vue';
export const platformState=reactive({account:{id:'qa-a',permissionVersion:1},token:'synthetic-qa-a'});
export const authState=reactive({verified:true});
${dateCode}
export const calls=window.__calls=[];export const pending=window.__pending=new Map();let sequence=0;
export function platformRequest(url,options={}){const id=++sequence;calls.push({id,url,signal:options.signal});return new Promise((resolve,reject)=>pending.set(id,{resolve,reject}));}
export const hasPermission=()=>true;`
const cards = [{ id: 'D-A', nameZh: '天灾甲', cardType: 'destruction' }, { id: 'D-B', nameZh: '天灾乙', cardType: 'destruction' },
  { id: 'D-STALE', nameZh: '过期候选哨兵', cardType: 'destruction' }]
const entry = `import {createApp,reactive,h,nextTick} from 'vue';
import '/src/style.css';import '/src/l12/site/uiSystem.css';
import Scoped from '/src/l12/site/ScopedDisasterAnalytics.vue';import Visible from '/src/l12/site/VisibleDisasterSummary.vue';
import PublicDeck from '/src/l12/site/PublicDeckDisasterStatistics.vue';import Tournament from '/src/l12/site/TournamentDisasterSummary.vue';
import {platformState,calls,pending} from 'phase34-platform';import {scopedDisasterPath} from '/src/l12/disasterAnalytics.ts';
const cards=${JSON.stringify(cards)};
const state=reactive({mode:'none',target:'C-A',path:'/api/me/matches/m-a/disasters',scope:{from:'2026-10-01',to:'2026-10-17',effectVersion:'all',initiative:'first',masterId:'M-A'}});
createApp({setup(){return()=>h('main',{class:'qa-host'},state.mode==='scoped'?h(Scoped,{scope:state.scope,cardId:state.target,cards}):state.mode==='visible'?h(Visible,{path:state.path}):state.mode==='public'?h(PublicDeck,{reference:'PUB1',readToken:'a'.repeat(64),cards}):state.mode==='tournament'?h(Tournament,{code:'T1',rounds:[{number:1,matches:[{table:1,status:'running'},{table:2,status:'completed',result:'bye'},{table:3,status:'completed',result:'player-a'}]}]}):null)}}).mount('#app');
window.qa={state,calls,pending,platformState,scopedDisasterPath,async mount(mode){state.mode='none';await nextTick();calls.length=0;pending.clear();state.mode=mode;await nextTick()},resolve(id,data){pending.get(id).resolve(data)},reject(id){pending.get(id).reject(new Error('合成失败，可重试'))}};
window.qaReady=true;`
const plugin = { name: 'phase34-test', enforce: 'pre', resolveId(source, importer) {
  if (source === 'phase34-platform' || source === '@/l12/platform' || source === './platform' && importer?.endsWith('/disasterAnalytics.ts')) return '\0phase34-platform'
  if (source === '@/l12/decks' || source.replaceAll('\\', '/').replace(/\.ts$/, '') === `${appRoot.replaceAll('\\', '/')}/src/l12/decks`) return '\0phase34-decks'
  if (source === '/phase34-entry.js') return '\0phase34-entry'
}, load(id) { if (id === '\0phase34-platform') return platformStub
  if (id === '\0phase34-decks') return `export const loadDeckCatalog=async()=>${JSON.stringify(cards)};`
  if (id === '\0phase34-entry') return entry
}, configureServer(server) { server.middlewares.use('/phase34-qa', (_req, res) => { res.setHeader('Content-Type', 'text/html');res.end('<!doctype html><meta name="viewport" content="width=device-width,initial-scale=1"><div id="app"></div><script type="module" src="/phase34-entry.js"></script>') }) } }
const server = await createServer({ root: appRoot, configFile: false, cacheDir: process.env.L12_VITE_CACHE_DIR,
  plugins: [plugin, vue()], resolve: { alias: { '@': path.join(appRoot, 'src') } }, server: { host: '127.0.0.1', port: 0 } })
await server.listen()
const url = `http://127.0.0.1:${server.httpServer.address().port}/phase34-qa`
const context = await chromium.launchPersistentContext(process.env.L12_BROWSER_PROFILE, { channel: 'msedge', headless: true, timezoneId: 'Asia/Shanghai' })
const page = await context.newPage()
const cases = [], errors = []
let runError
page.on('pageerror', error => errors.push(error.message))
page.setDefaultTimeout(8000)
await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
const report = (cardId = 'D-A') => ({coverage:{participantSamples:4,distinctMatches:3,usableSamples:3,usableMatches:2,unknownSamples:1,incompleteSamples:0,notApplicableSamples:0},items:[{cardId,step:0,origin:'player',opportunities:4,selections:2,selectionRate:.5,selectedWinRate:null}],pools:[{id:'A'.repeat(64),mode:'all',cardIds:['D-A']}],minimumWinRateSamples:30})
async function check(id, work) { try { await work(); cases.push({id,passed:true});console.log(`PASS ${id}`) } catch(error) { cases.push({id,passed:false,error:error.stack});console.error(`FAIL ${id}: ${error.message}`);await page.screenshot({path:path.join(out,`${id}-failed.png`)}) } }
async function lastCall(count = 1) { await page.waitForFunction(count => window.qa.calls.length >= count, count);return page.evaluate(() => { const c=window.qa.calls.at(-1);return {id:c.id,url:c.url} }) }
try {
  await page.goto(url);await page.waitForFunction(() => window.qaReady)
  await check('real-query-date-and-scope-mapping', async () => { const value=await page.evaluate(()=>window.qa.scopedDisasterPath({from:'2026-10-01',to:'2026-10-17',minimumSample:7,effectVersion:'all',initiative:'first',opponentMasterId:'M-B'},'C A'))
    const q=new URL(value,url).searchParams;assert.equal(q.get('fromUtc'),'2026-09-30T16:00:00.000Z');assert.equal(q.get('toUtc'),'2026-10-17T16:00:00.000Z');assert.equal(q.get('minimumSampleSize'),'7');assert.equal(q.get('opponentMasterId'),'M-B');assert.equal(q.get('includedCardId'),'C A');assert.equal(q.has('from'),false) })
  for (const viewport of [{width:1366,height:900},{width:844,height:390},{width:390,height:844}]) await check(`scoped-visible-${viewport.width}`,async()=>{
    await page.setViewportSize(viewport);await page.evaluate(()=>window.qa.mount('scoped'));assert.equal(await page.evaluate(()=>window.qa.calls.length),0)
    await page.getByRole('button',{name:'查看禁选分析'}).click();const call=await lastCall();await page.evaluate(({id,data})=>window.qa.resolve(id,data),{id:call.id,data:report()})
    await page.getByText('50.0%',{exact:true}).waitFor();const bounds=await page.locator('.scoped-disasters').boundingBox();assert.ok(bounds.x>=0&&bounds.x+bounds.width<=viewport.width+1)
    assert.equal(await page.locator('.table-scroll').evaluate(e=>e.scrollWidth>=e.clientWidth),true);await page.screenshot({path:path.join(out,`scoped-${viewport.width}.png`)})
  })
  await check('context-change-retires-pending-and-old-result',async()=>{
    await page.evaluate(()=>window.qa.mount('scoped'));await page.getByRole('button',{name:'查看禁选分析'}).click();const old=await lastCall();await page.evaluate(()=>window.qa.state.target='C-B')
    assert.equal(await page.evaluate(id=>window.qa.calls.find(c=>c.id===id).signal.aborted,old.id),true)
    await page.evaluate(({id,data})=>window.qa.resolve(id,data),{id:old.id,data:report('D-STALE')});assert.equal(await page.getByText('过期候选哨兵',{exact:true}).count(),0)
    await page.getByRole('button',{name:'查看禁选分析'}).click();const fresh=await lastCall(2);assert.equal(new URL(fresh.url,url).searchParams.get('includedCardId'),'C-B')
    await page.evaluate(({id,data})=>window.qa.resolve(id,data),{id:fresh.id,data:report()});await page.getByText('50.0%',{exact:true}).waitFor()
  })
  await check('identity-change-clears-private-response',async()=>{await page.evaluate(()=>window.qa.platformState.token='synthetic-qa-b');assert.equal(await page.getByText('50.0%',{exact:true}).count(),0);assert.equal(await page.evaluate(()=>window.qa.calls.length),2)})
  await check('visible-own-candidates-and-failure-retry',async()=>{await page.evaluate(()=>window.qa.mount('visible'));assert.equal(await page.evaluate(()=>window.qa.calls.length),0);await page.getByRole('button',{name:'查看天灾禁选记录'}).click();let call=await lastCall();await page.evaluate(id=>window.qa.reject(id),call.id);await page.getByRole('alert').waitFor();await page.getByRole('button',{name:'查看天灾禁选记录'}).click();call=await lastCall(2);await page.evaluate(({id,data})=>window.qa.resolve(id,data),{id:call.id,data:{coverage:'exact',status:'complete',mode:'all',visibility:'self',decisions:[{step:4,action:'pick',actorIndex:0,chosenCardId:'D-A',ownCandidateCardIds:['D-A','D-B']}],visibleCompositionCardIds:['D-A']}});await page.getByText('本人当时的候选').click();assert.match(await page.locator('.visible-disasters').innerText(),/天灾乙/);assert.equal(await page.locator('.visible-disasters').evaluate(e=>e.scrollWidth<=e.clientWidth+1),true)})
  await check('public-small-sample-and-no-private-groups',async()=>{await page.evaluate(()=>window.qa.mount('public'));await page.getByRole('button',{name:'查看统计',exact:true}).click();const call=await lastCall();await page.evaluate(({id,data})=>window.qa.resolve(id,data),{id:call.id,data:{status:'insufficient',usableMatches:0,items:[],minimumMatches:3,recentDays:90}});assert.match(await page.locator('.public-disasters').innerText(),/不足 3 场/);assert.equal(await page.locator('.public-disasters li').count(),0)})
  await check('tournament-only-completed-nonbye-explicit-load',async()=>{await page.evaluate(()=>window.qa.mount('tournament'));assert.equal(await page.locator('select option').count(),2);await page.locator('select').selectOption('1/3');assert.equal(await page.evaluate(()=>window.qa.calls.length),0);await page.getByRole('button',{name:'查看天灾禁选记录'}).click();const call=await lastCall();assert.match(call.url,/code\/T1\/rounds\/1\/tables\/3\/disasters$/)})
  assert.deepEqual(errors,[]);assert.deepEqual(hashes(),before)
} catch(error) { runError=error.stack;console.error(error.stack) } finally {
  await context.close();await server.close()
  const result={schema:1,status:cases.length===9&&cases.every(c=>c.passed)&&errors.length===0&&!runError?'passed':'failed',source:before,cases,pageErrors:errors,runError,
    limits:['Real new Vue SFCs and shared composable; synthetic API/identity, not live HTTP or engine integration.','Desktop and mobile-sized Edge, not physical iOS/WeChat.','Parent wiring additionally covered by type/build/contracts, not full end-to-end page navigation.']}
  fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(result,null,2));if(result.status!=='passed')process.exitCode=1
}
