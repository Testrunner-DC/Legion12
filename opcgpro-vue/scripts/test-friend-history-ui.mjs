import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'
import * as vue from 'vue'
import { parse, compileScript, compileTemplate, compileStyle } from '@vue/compiler-sfc'

const site = new URL('../src/l12/site/', import.meta.url)
const profile = fs.readFileSync(new URL('ProfilePage.vue', site), 'utf8')
const privacyInitializer = profile.match(/^const publicHistory = .+$/m)?.[0]
assert.ok(privacyInitializer)
const state = vue.reactive({ account: { id: 'owner', publicHistory: true }, token: 'token' })
const privacy = vm.runInNewContext(`${privacyInitializer}; publicHistory`, { ...vue, platformState: state, localStorage: { getItem: () => 'false' } })
assert.equal(privacy.value, true, 'Server publicHistory=true must override another account’s old browser-local false')
state.account = { id: 'other', publicHistory: false }
assert.equal(privacy.value, false, 'Switching accounts must display that account’s own server setting')
assert.ok(!profile.includes("watch(publicHistory, value => localStorage.setItem('l12-public-history'"), 'Privacy must persist on server, not the shared browser key')
console.log('PASS server privacy source and account switch')

function run(source, dependencies, globals = {}) {
  const module = { exports: {} }
  vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
    { module, exports: module.exports, require: id => { if (id in dependencies) return dependencies[id]; throw new Error(id) }, AbortController, Error, ...globals })
  return module.exports
}
const tick = async () => { await vue.nextTick(); await Promise.resolve(); await vue.nextTick() }
const pending = []
const owner = vue.reactive({ account: { id: 'owner' }, token: 'one' })
const friends = vue.reactive({ accountId: 'owner', friends: [{ accountId: 'a' }, { accountId: 'b' }] })
const props = vue.reactive({ accountId: 'a', friendName: 'A' })
const unmounts = []
const scope = vue.effectScope()
const panelSource = fs.readFileSync(new URL('FriendHistoryPanel.vue', site), 'utf8').match(/<script setup lang="ts">([\s\S]*?)<\/script>/)[1]
let panel
scope.run(() => { panel = run(`${panelSource}\nexport {data,loading,error,range,load}`, {
  vue: { ...vue, onBeforeUnmount: action => unmounts.push(action) },
  '../platform': { platformState: owner, friendApi: { history: (id, range, signal) => new Promise((resolve, reject) => pending.push({ id, range, signal, resolve, reject })) } },
  '../friendResource': { friendResource: friends },
}, { defineProps: () => props }) })
const history = (id, range = 'season') => ({ accountId: id, statistics: { range }, recentMatches: [], recentLimit: 10 })
assert.equal(pending.length, 1, 'One read for the explicitly opened friend')
pending[0].resolve(history('a')); await tick()
assert.equal(panel.data.value.accountId, 'a')
panel.range.value = '7d'; await tick()
assert.equal(panel.data.value, null, 'Clear old range while loading')
props.accountId = 'b'; await tick()
assert.ok(pending[1].signal.aborted)
pending[1].resolve(history('a', '7d')); await tick()
assert.equal(panel.data.value, null, 'Late old-friend read cannot cross selection')
pending[2].resolve(history('WRONG', '7d')); await tick()
assert.equal(panel.data.value, null)
assert.match(panel.error.value, /变化/)
void panel.load(); await tick()
pending[3].reject(new Error('该好友未公开战绩')); await tick()
assert.equal(panel.data.value, null)
assert.match(panel.error.value, /未公开/)
void panel.load(); await tick()
owner.account = { id: 'another' }; owner.token = 'two'; await tick()
assert.ok(pending[4].signal.aborted)
pending[4].resolve(history('b', '7d')); await tick()
assert.equal(panel.data.value, null, 'No late response after account switch')
assert.equal(pending.length, 5, 'Invalid friendship does not start another read')
owner.account = { id: 'owner' }; owner.token = 'one'; await tick()
friends.friends = []; await tick()
assert.ok(pending[5].signal.aborted)
assert.equal(panel.data.value, null, 'Relationship removal clears displayed history')
friends.friends = [{ accountId: 'b' }]; await tick()
for (const action of unmounts) action()
scope.stop()
pending[6].resolve(history('b', '7d')); await tick()
assert.equal(panel.data.value, null, 'Unmount drops data and late responses')
assert.ok(pending[6].signal.aborted)
console.log('PASS actual panel read, range/selection/account/removal/unmount isolation, denial and explicit retry')

const platformSource = fs.readFileSync(new URL('../src/l12/platform.ts', import.meta.url), 'utf8')
const ast = ts.createSourceFile('platform.ts', platformSource, ts.ScriptTarget.Latest, true)
const declaration = ast.statements.filter(ts.isVariableStatement).flatMap(node => node.declarationList.declarations).find(node => node.name.getText(ast) === 'playerApi')
const setter = declaration.initializer.properties.find(node => node.name.getText(ast) === 'setHistoryPreference').initializer.getText(ast)
const apiState = { account: { id: 'owner', publicHistory: true, username: '保留用户名' }, token: 'one' }, writes = [], reads = []
const setterApi = run(`export const save = ${setter}`, {}, { platformState: apiState,
  platformRequest: (url, options) => new Promise(resolve => reads.push({ url, options, resolve })),
  remember: (account, token) => { writes.push({ account, token }); apiState.account = account },
})
const saving = setterApi.save(false)
assert.equal(reads[0].url, '/api/me/history-preferences')
assert.deepEqual(JSON.parse(reads[0].options.body), { publicHistory: false })
apiState.account = { id: 'other', publicHistory: true }; apiState.token = 'two'
reads[0].resolve({ accountId: 'owner', publicHistory: false }); await saving
assert.equal(writes.length, 0, 'Late preference save must not rewrite another account storage')
const currentSave = setterApi.save(false)
reads[1].resolve({ accountId: 'other', publicHistory: false }); await currentSave
assert.equal(writes.length, 0, 'A preference update must not rewrite login credentials')
assert.equal(apiState.account.publicHistory, false)
console.log('PASS actual API preference PUT and cross-account late-save isolation')

const profileState = vue.reactive({ account: { id: 'owner', publicHistory: true }, token: 'one' })
const profilePending = [], profileUnmounts = [], profileScope = vue.effectScope()
const privacyBlock = profile.slice(profile.indexOf('const publicHistory ='), profile.indexOf('const canUseSandbox ='))
let profileModel
profileScope.run(() => { profileModel = run(`${privacyBlock}\nexport {publicHistory,historySaving,historyNotice,saveHistoryPreference}`, {}, {
  ...vue, onBeforeUnmount: action => profileUnmounts.push(action), platformState: profileState,
  playerApi: { setHistoryPreference: value => new Promise((resolve, reject) => profilePending.push({ value, resolve, reject })) },
}) })
void profileModel.saveHistoryPreference(); void profileModel.saveHistoryPreference()
assert.equal(profilePending.length, 1, 'Saving locks repeated preference clicks')
assert.equal(profilePending[0].value, false)
profileState.account = { id: 'owner', publicHistory: false }
profilePending[0].resolve(); await tick()
assert.equal(profileModel.historySaving.value, false)
assert.match(profileModel.historyNotice.value, /不公开/)
void profileModel.saveHistoryPreference()
profilePending[1].reject(new Error('服务器暂时不可用')); await tick()
assert.equal(profileModel.publicHistory.value, false, 'Failed save never flips the confirmed setting')
assert.match(profileModel.historyNotice.value, /暂时不可用/)
void profileModel.saveHistoryPreference()
profileState.account = { id: 'other', publicHistory: true }; profileState.token = 'two'; await tick()
profilePending[2].resolve(); await tick()
assert.equal(profileModel.historyNotice.value, '')
assert.equal(profileModel.historySaving.value, false)
void profileModel.saveHistoryPreference()
for (const action of profileUnmounts) action()
profileScope.stop()
profilePending[3].resolve(); await tick()
assert.equal(profileModel.historyNotice.value, '', 'Unmount ignores old save notices')
console.log('PASS actual profile save gate, error keeps confirmed value, account/unmount isolation')

for (const name of ['FriendHistoryPanel.vue', 'FriendsPage.vue', 'SocialPopover.vue', 'ProfilePage.vue']) {
  const source = fs.readFileSync(new URL(name, site), 'utf8')
  const { descriptor, errors } = parse(source)
  assert.equal(errors.length, 0)
  const script = compileScript(descriptor, { id: 'history-qa' })
  assert.equal(compileTemplate({ source: descriptor.template.content, filename: name, id: 'history-qa', compilerOptions: { bindingMetadata: script.bindings } }).errors.length, 0)
  for (const style of descriptor.styles) assert.equal(compileStyle({ source: style.content, filename: name, id: 'history-qa', scoped: style.scoped }).errors.length, 0)
  console.log(`PASS ${name} actual SFC compilation`)
}

if (process.argv.includes('--serve')) {
  const path = await import('node:path'), child = await import('node:child_process')
  const { createServer } = await import('vite')
  const root = path.resolve(new URL('..', import.meta.url).pathname.replace(/^\/(\w:)/, '$1'))
  const out = process.env.L12_HISTORY_UI_OUT, cache = process.env.L12_HISTORY_UI_CACHE
  assert.ok(out && cache && process.env.L12_PWSH)
  child.execFileSync(process.env.L12_PWSH, ['-NoProfile', '-Command', '. $env:L12_HISTORY_GUARD; @($env:L12_HISTORY_UI_OUT,$env:L12_HISTORY_UI_CACHE,$env:TEMP,$env:TMP,$env:TMPDIR) | ForEach-Object { Assert-L12GeneratedPath -Path $_ -Purpose "friend history UI" | Out-Null }'], { env: { ...process.env, L12_HISTORY_GUARD: path.join(root, '../ops/windows/L12GeneratedStorage.ps1') }, windowsHide: true })
  fs.mkdirSync(out, { recursive: true })
  const oldFixture = fs.readFileSync(new URL('./verify-friends-mobile-reachability.mjs', import.meta.url), 'utf8')
  let entry = oldFixture.match(/const entry = String.raw`([\s\S]*?)`\r?\n/)[1]
  const net = oldFixture.match(/const qaNet = String.raw`([\s\S]*?)`\r?\n/)[1]
  entry = entry.replace("const [{default:SiteShell}", `
  localStorage.setItem('l12-public-history','false')
  let publicHistory=true
  const line={games:12,wins:7,losses:4,draws:1,firstGames:6,firstWins:4,secondGames:6,secondWins:3}
  platform.friendApi.history=async(id,range)=>{
    if(!publicHistory)throw new Error('该好友未公开战绩')
    return {accountId:id,username:'合成好友',statistics:{overall:line,ranked:line,masters:[],range},recentLimit:10,recentMatches:Array.from({length:10},(_,i)=>({startedUtc:stamp,endedUtc:stamp,opponentName:'合成对手 '+(i+1),masterName:'安格斯·麦·奥格',opponentMasterName:'荷鲁斯',modeId:'ranked',result:i%2?'loss':'win'}))}
  }
  platform.playerApi.setHistoryPreference=async(value)=>{publicHistory=value;platform.platformState.account={...platform.platformState.account,publicHistory:value};return{accountId:account.id,publicHistory:value}}
  const {default:ProfilePage}=await import('/src/l12/site/ProfilePage.vue')
  const [{default:SiteShell}`)
  entry = entry.replace("{path:'/',component:Stub}", "{path:'/me',component:ProfilePage},{path:'/',component:Stub}")
  entry = entry.replace("await router.push('/battle/friends')", "await router.push(new URLSearchParams(location.search).has('profile')?'/me?section=collection':'/battle/friends')")
  const server = await createServer({root, configLoader:'runner',cacheDir:cache, server:{host:'127.0.0.1',port:0},plugins:[{name:'history-fixture',enforce:'pre',
    transform(code,id){if(id.includes('?'))return;if(id.endsWith('/site/FriendsPage.vue'))return code.replace("from '@/l12/net'","from '/__qa_friends_net__.js'");if(id.endsWith('/site/SocialPopover.vue'))return code.replace("from '../net'","from '/__qa_friends_net__.js'")},
    resolveId(id){if(['/__friends_mobile__.js','/__qa_friends_net__.js'].includes(id))return id},
    load(id){if(id==='/__friends_mobile__.js')return entry;if(id==='/__qa_friends_net__.js')return net},
    configureServer(s){s.middlewares.use((req,res,next)=>{if(req.url?.startsWith('/__history__')){res.setHeader('Content-Type','text/html');res.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module" src="/__friends_mobile__.js"></script></body></html>')}else next()})},
  }]})
  await server.listen()
  const url=`http://127.0.0.1:${server.httpServer.address().port}/__history__?long=1`
  fs.writeFileSync(path.join(out,'fixture.json'),JSON.stringify({url,synthetic:true}))
  console.log(url)
  await new Promise(resolve=>{process.once('SIGINT',resolve);process.once('SIGTERM',resolve)})
  await server.close()
}
