import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/profile-admin-responsive')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import '/src/style.css'

const permissions=['admin.accounts.read','admin.accounts.status.write','admin.sessions.read','admin.sessions.revoke','admin.bugs.read','admin.effects.read','admin.effects.review','admin.audit.read','admin.security.read','admin.runtime.read']
localStorage.setItem('l12-auth-token','qa-token')
localStorage.setItem('l12-account',JSON.stringify({id:'qa-admin',username:'移动端验收管理员',role:'admin',createdAt:'2026-09-21T00:00:00Z',publicHistory:true,permissions,permissionVersion:8}))
const platform=await import('/src/l12/platform.ts')
platform.platformState.token='qa-token'
platform.platformState.account={id:'qa-admin',username:'移动端验收管理员',role:'admin',createdAt:'2026-09-21T00:00:00Z',publicHistory:true,permissions,permissionVersion:8}
platform.authState.initialized=true
platform.authState.verified=true
platform.authState.refreshing=false

const stat=(games,wins,losses,draws,firstGames,firstWins,secondGames,secondWins)=>({games,wins,losses,draws,firstGames,firstWins,secondGames,secondWins})
platform.playerApi.statistics=async(range='season')=>{
 const empty=new URLSearchParams(location.search).get('qaEmpty')==='1'
 const values=empty?{games:0,wins:0,ranked:0,masterGames:[]}:
  range==='7d'?{games:7,wins:4,ranked:5,masterGames:[4,3]}:
  range==='30d'?{games:30,wins:18,ranked:21,masterGames:[16,14]}:
  {games:128,wins:77,ranked:86,masterGames:[65,63]}
 const overall=stat(values.games,values.wins,Math.max(0,values.games-values.wins),0,Math.ceil(values.games/2),Math.ceil(values.wins/2),Math.floor(values.games/2),Math.floor(values.wins/2))
 return {range,fromUtc:range==='7d'?'2026-09-18T08:00:00Z':range==='30d'?'2026-08-26T08:00:00Z':'2026-07-01T00:00:00Z',untilUtc:'2026-09-25T08:00:00Z',seasonId:range==='season'?'S2026-2':undefined,
  overall,ranked:stat(values.ranked,Math.min(values.wins,values.ranked),Math.max(0,values.ranked-values.wins),0,Math.ceil(values.ranked/2),Math.ceil(Math.min(values.wins,values.ranked)/2),Math.floor(values.ranked/2),Math.floor(Math.min(values.wins,values.ranked)/2)),updatedAt:'2026-09-21T08:00:00Z',
  masters:values.masterGames.map((games,index)=>({masterId:['S01-01M1','S01-02M1'][index],masterName:['梅杰德','阿斯加德'][index],overall:stat(games,Math.ceil(games*.6),Math.floor(games*.4),0,Math.ceil(games/2),Math.ceil(games*.3),Math.floor(games/2),Math.floor(games*.3)),ranked:stat(Math.max(0,games-1),Math.ceil(games*.5),Math.max(0,Math.floor(games*.5)-1),0,Math.ceil(games/2),Math.ceil(games*.25),Math.floor(games/2),Math.floor(games*.25))}))}
}
platform.rankedApi.overview=async()=>({
 profile:{accountId:'qa-admin',username:'移动端验收管理员',seasonId:'S2026-2',faction:'秩序',sevenValue:2380,displayValue:'七曜值 2380',placementPlayed:10,placementWins:7,placed:true,wins:52,losses:33,winStreak:3,lossStreak:0,tier:'璀璨群星',tierIndex:6,factionRank:3,titles:['秩序先锋','最强梅杰德'],rankLabel:'璀璨群星 · 秩序第 3',selectedMasterTitle:'最强梅杰德',masterTitles:['最强梅杰德','最强阿斯加德']},
 factionTotals:{秩序:36},config:{placementMatches:10,placementMaximum:10,broadcastEnabled:true,factions:[],masterTitles:[],timeControl:{totalTimeSeconds:1500,operationTimeSeconds:240,reconnectGraceSeconds:240,disasterDecisionSeconds:60,mulliganDecisionSeconds:60},broadcast:{displaySeconds:16,lobbyDelaySeconds:3,intervalSeconds:15,winStreakThreshold:5,streakEndedThreshold:5,minimumTierIndex:0,winStreakEnabled:true,streakEndedEnabled:true,highestTierEnabled:true,factionTitleEnabled:true,masterTitleEnabled:true}},history:[]
})
platform.sessionApi.list=async()=>[
 {id:'session-current-long-identifier',createdAt:'2026-09-20T09:00:00Z',expiresAt:'2026-10-20T09:00:00Z',current:true,authStrength:'password',permissionVersion:8},
 {id:'session-tablet-long-identifier',createdAt:'2026-09-19T09:00:00Z',expiresAt:'2026-10-19T09:00:00Z',current:false,authStrength:'password',permissionVersion:8}
]
platform.usernameChangeApi.status=async()=>({freeRenameAvailable:true,freeRenameUsed:0})
platform.emailApi.capability=async()=>({enabled:false,mailConfigured:false})

const sampleAccount=(id,username,disabled=false,deleted=false)=>({id,username,role:'player',createdAt:'2026-09-01T00:00:00Z',publicHistory:true,permissionVersion:3,disabled,deleted,disabledReason:disabled?'异常对局复核中':''})
platform.adminApi.accounts=async()=>[
 sampleAccount('account-001','长昵称玩家一号'),sampleAccount('account-002','长昵称玩家二号',true),sampleAccount('account-003','已删除玩家',false,true)
]
platform.adminApi.account=async id=>(await platform.adminApi.accounts()).find(item=>item.id===id)
platform.adminApi.resetAccountPassword=async()=>({applied:true,account:{...sampleAccount('account-001','长昵称玩家一号'),mustChangePassword:true},revokedSessions:2,temporaryPassword:'7F1A5C9E2D4B8A6031CE97B5420D8F6A'})
platform.adminApi.bugs=async()=>[{id:'BUG-20260921-001',reporterName:'验收玩家',title:'移动端弹框在极窄屏幕下信息显示不完整',description:'用于验证较长问题标题、描述与管理操作在窄屏不会互相侵入。',page:'/battle/room/qa',roomCode:'QA001',version:'2026.09.21',status:'retest',priority:'high',assignee:'维护者',adminNotes:'',fixCommit:'abc1234',regressionTest:'MobileAdminEvidenceRegression',deployedVersion:'2026.09.25',verifiedBy:'验收管理员',verifiedAt:'2026-09-25T04:00:00Z',history:[],createdAt:'2026-09-21T08:00:00Z',updatedAt:'2026-09-25T04:00:00Z'}]
platform.adminApi.effects=async()=>({items:[{cardId:'S01-02C1',name:'移动端长名称卡效验收卡牌',product:'第一弹',faction:'秩序',cardType:'legion',isCounterTactic:false,effectText:'我方 回合1次：支付1士气，执行一项移动端验收效果。',abilities:[],migrationStatus:'declarative-ready',atomCount:3,executableAtomCount:3,legacyAtomCount:0,atomKinds:['cost.morale'],reviewStatus:'confirmed',reviewSource:'qa'}],total:1,page:1,pageSize:50,coverage:{totalCards:324,cardsWithText:280,totalAbilities:510,totalAtoms:1160,declarativeReadyAbilities:480,verifiedAbilities:420,legacyBackedAbilities:12,byStatus:{},byAtomKind:{}}})
platform.adminApi.effectAtoms=async()=>[]
platform.adminApi.sessions=async()=>platform.sessionApi.list()
platform.adminApi.audit=async()=>[{id:'audit-1',actorId:'qa-admin',actorName:'验收管理员',category:'account',action:'status',target:'account-001',createdAt:'2026-09-25T03:00:00Z'}]
platform.adminApi.workbenchSummary=async()=>({sampledAt:'2026-09-25T04:00:00Z',pending:[{id:'bugs',kind:'bug',label:'Bug 闭环',detail:'1 条需要确认、处理或验证',path:'/admin/users/bugs',severity:'attention',count:1}],anomalies:[{id:'storage',kind:'storage',label:'存储容量',detail:'最高卷已使用 84%',path:'/admin/system/storage',severity:'warning'}],recentActivities:[{id:'audit-1',kind:'account',label:'status',detail:'验收管理员 · account-001',path:'/admin/system/audit',severity:'neutral',occurredAt:'2026-09-25T03:00:00Z'}]})
platform.adminApi.serverStorage=async()=>({observedAt:'2026-09-25T04:00:00Z',processId:42,workingSetBytes:268435456,health:'warning',conclusion:'存储容量需要关注，最高卷已使用 84%',impact:'短期仍可运行，但应在下一次运营窗口检查增长来源。',recommendedAction:'检查增长趋势与占用分类，提前安排容量。',thresholds:{warningPercent:80,criticalPercent:90,source:'服务端容量治理策略'},trendScope:'current-process',trendDescription:'本次服务进程内的真实采样，重启后重新累计，最多保留 24 小时 / 120 个样本',volumes:[{mountPoint:'D:',totalBytes:1000000000,usedBytes:840000000,freeBytes:160000000}],categories:[{id:'matches-db',label:'对局数据库',path:'/runtime/matches.db',bytes:104857600,available:true}],trend:[{observedAt:'2026-09-25T03:30:00Z',workingSetBytes:250000000,volumeUsedPercent:{'D:':82}},{observedAt:'2026-09-25T04:00:00Z',workingSetBytes:268435456,volumeUsedPercent:{'D:':84}}]})

const mode=new URLSearchParams(location.search).get('mode')||'profile'
const AdminShell=(await import('/src/l12/site/AdminPage.vue')).default
const AdminWorkbench=(await import('/src/l12/site/AdminWorkbenchPage.vue')).default
const AdminAccounts=(await import('/src/l12/site/AdminAccountsPage.vue')).default
const AdminAccountDetail=(await import('/src/l12/site/AdminAccountDetailPage.vue')).default
const AdminBugs=(await import('/src/l12/site/AdminBugsPage.vue')).default
const AdminEffects=(await import('/src/l12/site/AdminEffectsPage.vue')).default
const AdminStorage=(await import('/src/l12/site/AdminServerStoragePanel.vue')).default
const Profile=(await import('/src/l12/site/ProfilePage.vue')).default
const Empty={template:'<section class="qa-empty">当前模块不在本次截图范围</section>'}
const moduleRoutes=[
 ['users/renames','username-requests'],['content/site','content'],['content/rules','rules'],['content/alternate-arts','alternate-arts'],
 ['matches/archive/:matchId?','matches'],['matches/governance','match-governance'],['matches/integrity','integrity'],['matches/tournaments','tournaments'],
 ['operations/config','operations'],['operations/global','global-data'],['operations/cards','card-analytics'],['system/releases','releases'],
 ['system/security','security'],['system/storage','storage'],['system/commands','commands'],['system/audit','audit'],
].map(([path,adminSection])=>({path,component:adminSection==='storage'?AdminStorage:Empty,meta:{adminSection}}))
const router=createRouter({history:createMemoryHistory(),routes:[
 {path:'/me',component:Profile},
 {path:'/admin',component:AdminShell,children:[
  {path:'',component:AdminWorkbench,meta:{adminSection:'overview'}},
  {path:'users/accounts',component:AdminAccounts,meta:{adminSection:'accounts'}},
  {path:'users/accounts/:accountId',component:AdminAccountDetail,meta:{adminSection:'accounts'}},
  {path:'users/bugs/:bugId?',component:AdminBugs,meta:{adminSection:'bugs'}},
  {path:'content/effects/:cardId?',component:AdminEffects,meta:{adminSection:'effects'}},
  ...moduleRoutes,
 ]},
]})
const app=createApp({render:()=>h(RouterView)});app.use(router);await router.push(mode==='admin'?'/admin':'/me?section=performance');await router.isReady();app.mount('#app')
window.__qaRouter=router
`

let browser
const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-profile-admin-responsive'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'profile-admin-responsive-fixture',
    resolveId(id) { if (id === '/__profile_admin__.js') return id },
    load(id) { if (id === '/__profile_admin__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__profile_admin__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#eee}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__profile_admin__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const viewports = [
  { width: 320, height: 700 },
  { width: 360, height: 780 },
  { width: 390, height: 844 },
  { width: 844, height: 390 },
  { width: 430, height: 932 },
  { width: 768, height: 1024 },
  { width: 851, height: 900 },
  { width: 1024, height: 768 },
  { width: 1280, height: 800 },
  { width: 1280, height: 720 },
  { width: 1440, height: 900 },
  { width: 1920, height: 1080 },
]
const suffix = viewport => `${viewport.width}x${viewport.height}`
const overflow = page => page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)

try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const report = []

  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    const compactAdmin = viewport.width <= 850

    await page.goto(`http://127.0.0.1:${port}/__profile_admin__?mode=profile`)
    await page.locator('.master-records').waitFor()
    await page.locator('.master-records').evaluate(element => { element.open = true })
    await page.locator('.master-records article').first().waitFor()
    assert.equal(await overflow(page), false, `profile page overflows at ${suffix(viewport)}`)
    assert.equal(await page.locator('.master-records').evaluate(element => element.scrollWidth > element.clientWidth + 1), false, `profile master records overflow at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `profile-${suffix(viewport)}.png`), fullPage: true })
    await page.getByRole('button',{name:'账号与安全',exact:true}).click()
    await page.locator('.session-manager').waitFor()
    await page.locator('.session-manager').evaluate(element => { element.open = true })
    await page.locator('.session-row').first().waitFor()
    assert.equal(await overflow(page), false, `profile account controls overflow at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `profile-account-${suffix(viewport)}.png`), fullPage: true })

    await page.goto(`http://127.0.0.1:${port}/__profile_admin__?mode=admin`)
    await page.locator('.admin-shell').waitFor()
    const adminBase = await page.evaluate(() => ({
      overflow: document.documentElement.scrollWidth > innerWidth + 1,
      mobileVisible: getComputedStyle(document.querySelector('.admin-mobile-navigation')).display !== 'none',
      sidebarVisible: getComputedStyle(document.querySelector('.admin-sidebar')).display !== 'none',
    }))
    assert.equal(adminBase.overflow, false, `admin overview overflows at ${suffix(viewport)}`)
    assert.equal(adminBase.mobileVisible, compactAdmin, `admin mobile picker breakpoint mismatch at ${suffix(viewport)}`)
    assert.equal(adminBase.sidebarVisible, !compactAdmin, `admin desktop sidebar breakpoint mismatch at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `admin-overview-${suffix(viewport)}.png`), fullPage: true })

    if (compactAdmin) {
      const picker = page.locator('.admin-mobile-navigation select')
      const bounds = await page.locator('.admin-mobile-navigation').evaluate(element => {
        const rect = element.getBoundingClientRect()
        return { left: rect.left, right: rect.right, width: rect.width }
      })
      assert(bounds.left >= -1 && bounds.right <= viewport.width + 1, `admin picker leaves safe viewport at ${suffix(viewport)}`)
      await picker.selectOption('/admin/users/accounts')
      await page.getByRole('heading', { name: '账号、权限与会话' }).waitFor()
      assert.equal(await overflow(page), false, `admin accounts overflow at ${suffix(viewport)}`)
      await page.screenshot({ path: path.join(output, `admin-accounts-${suffix(viewport)}.png`), fullPage: true })
      await page.locator('.actions input').first().fill('浏览器安全验收')
      await page.getByRole('button',{name:'重置密码',exact:true}).first().click()
      await page.getByRole('dialog').getByRole('button', { name: '生成并撤销会话' }).click()
      await page.locator('.secret code').waitFor()
      assert.equal((await page.locator('.secret code').textContent())?.length, 32, `temporary password length mismatch at ${suffix(viewport)}`)
      await page.getByRole('dialog').getByRole('button', { name: '我已安全保存' }).click()
      await picker.selectOption('/admin/users/bugs')
      await page.getByRole('heading', { name: 'Bug 分诊与证据闭环' }).waitFor()
      assert.equal(await overflow(page), false, `admin bugs overflow at ${suffix(viewport)}`)
      await page.screenshot({ path: path.join(output, `admin-bugs-${suffix(viewport)}.png`), fullPage: true })
      if (viewport.width === 390 || viewport.width === 768) {
        await picker.selectOption('/admin/content/effects')
        await page.getByRole('heading', { name: '卡效原子化与发布工作台' }).first().waitFor()
        assert.equal(await overflow(page), false, `admin effects overflow at ${suffix(viewport)}`)
        await page.screenshot({ path: path.join(output, `admin-effects-${suffix(viewport)}.png`), fullPage: true })
      }
    }
    if (viewport.width === 390 || viewport.width === 1440) {
      await page.evaluate(()=>window.__qaRouter.push('/admin/users/accounts/account-001?tab=sessions'))
      await page.getByRole('heading',{name:'长昵称玩家一号'}).waitFor()
      assert.equal(await overflow(page), false, `admin account detail overflows at ${suffix(viewport)}`)
      await page.screenshot({ path: path.join(output, `admin-account-detail-${suffix(viewport)}.png`), fullPage: true })
      await page.evaluate(()=>window.__qaRouter.push('/admin/system/storage'))
      await page.getByRole('heading',{name:'服务器状态与存储'}).waitFor()
      assert.equal(await overflow(page), false, `admin storage overflows at ${suffix(viewport)}`)
      await page.screenshot({ path: path.join(output, `admin-storage-${suffix(viewport)}.png`), fullPage: true })
    }
    report.push({ viewport, admin: adminBase })
  }

  assert.equal(errors.length, 0, `page errors: ${errors.join(' | ')}`)
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status: 'passed', viewports, report, errors }, null, 2))
  console.log(JSON.stringify({ status: 'passed', output, viewports: viewports.length, errors }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
