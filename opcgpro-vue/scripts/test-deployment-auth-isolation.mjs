import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import path from 'node:path'
import crypto from 'node:crypto'
import { createServer } from 'node:http'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { deploymentSourceModuleUrl } from './deployment-source-module.mjs'

const source = fs.readFileSync(new URL('../src/l12/platform.ts', import.meta.url), 'utf8')
const tree = ts.createSourceFile('platform.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
assert.equal(tree.parseDiagnostics.length, 0)
const names = new Set(['loadAccount', 'platformState', 'authState', 'remember', 'forgetAccount',
  'platformSessionVersion', 'authRefreshPromise'])
const selected = tree.statements.filter(node => {
  if (ts.isFunctionDeclaration(node)) return names.has(node.name?.text)
  return ts.isVariableStatement(node) && node.declarationList.declarations.some(d => names.has(d.name.getText(tree)))
})
const compiled = ts.transpileModule(selected.map(node => node.getText(tree).replace(/^export\s+/, '')).join('\n'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText
const values = new Map()
const storage = {
  getItem: key => values.get(key) ?? null,
  setItem: (key, value) => values.set(key, String(value)),
  removeItem: key => values.delete(key),
}
let checks = 0
function equal(actual, expected, label) { assert.deepEqual(actual, expected, label); checks++ }
const account = id => ({ id, username: id, role: 'player' })
async function runtime(base) {
  const deployment = await import(deploymentSourceModuleUrl(base))
  const l12State = { nickname: '' }
  let disconnected = 0
  const exports = {}
  const actual = Function('exports', 'reactive', 'localStorage', 'deploymentStorageKey',
    'l12State', 'clearAuthRefreshRetry', 'disconnect', compiled + '\nreturn { platformState, authState, remember, forgetAccount }')(
      exports, v => v, storage, deployment.deploymentStorageKey, l12State, () => {}, () => disconnected++)
  return { ...actual, deployment, l12State, disconnected: () => disconnected }
}

values.set('l12-auth-token', 'legacy-formal')
values.set('l12-account', JSON.stringify(account('formal')))
values.set('l12-nickname', 'formal')
values.set('l12-endpoint', 'wss://formal.invalid/ws')
const test = await runtime('/testrun/')
equal(test.platformState.token, '', 'test must not inherit ambiguous legacy formal token')
equal(test.platformState.account, null, 'test must not inherit formal account')
const formal = await runtime('/')
equal(formal.platformState.token, 'legacy-formal', 'formal keeps its existing login cache')
equal(formal.platformState.account, account('formal'), 'formal keeps its existing account cache')
test.remember(account('test'), 'test-token')
equal(storage.getItem('l12-auth-token'), 'legacy-formal', 'test remember cannot replace formal token')
equal(storage.getItem('l12-account'), JSON.stringify(account('formal')), 'test remember cannot replace formal identity')
formal.remember(account('formal-new'), 'formal-token')
const testReload = await runtime('/testrun/')
equal(testReload.platformState.token, 'test-token', 'test reload reads its own token')
equal(testReload.platformState.account, account('test'), 'test reload reads its own account')
test.forgetAccount('wrong-token')
equal(test.platformState.token, 'test-token', 'existing stale-token logout guard remains')
test.forgetAccount()
equal(storage.getItem('l12-auth-token'), 'formal-token', 'test logout preserves formal token')
equal(storage.getItem('l12-nickname'), 'formal-new', 'test logout preserves formal nickname')
equal(test.disconnected(), 1, 'test logout still retires its connection')
testReload.remember(account('test-second'), 'test-token-2')
formal.forgetAccount()
const testAfterFormalLogout = await runtime('/testrun/')
equal(testAfterFormalLogout.platformState.token, 'test-token-2', 'formal logout preserves test token')
equal(testAfterFormalLogout.platformState.account, account('test-second'), 'formal logout preserves test account')
const testKeys = new Set()
for (const legacy of ['l12-auth-token', 'l12-account', 'l12-nickname', 'l12-endpoint']) {
  equal(formal.deployment.deploymentStorageKey(legacy), legacy, 'formal keys remain exactly backward compatible')
  const key = test.deployment.deploymentStorageKey(legacy)
  assert.notEqual(key, legacy)
  testKeys.add(key)
  checks++
}
equal(testKeys.size, 4, 'all auth/connection fields have distinct scoped keys')
const normalized = await runtime('testrun')
equal(normalized.platformState.token, 'test-token-2', 'normalized mount retains the same environment')
const otherMount = await runtime('/preview/')
equal(otherMount.platformState.token, '', 'another mounted environment cannot inherit test')
console.log(JSON.stringify({ suite: 'actual-platform-deployment-auth-isolation', passed: checks, failed: 0,
  actualPlatformFunctions: true, actualBuildBaseResolver: true, networkCalls: 0 }))
export const deploymentAuthIsolationVerified = true

async function browserContract() {
  const repo = path.resolve(fileURLToPath(new URL('../..', import.meta.url)))
  const out = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/auth-environment-isolation-20261021/browser'
  const cache = process.env.L12_WORK_CACHE || 'D:/GPT/Legion12/cache/primary/auth-environment-isolation-20261021'
  const env = { ...process.env, L12_TASK_REPO: repo, L12_TASK_OUT: out, L12_TASK_CACHE: cache }
  execFileSync(process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
    ['-NoProfile', '-NonInteractive', '-Command',
      '. "$env:L12_TASK_REPO/ops/windows/L12GeneratedStorage.ps1"; Assert-L12GeneratedPath -Path $env:L12_TASK_OUT | Out-Null; '
      + '. "$env:L12_TASK_REPO/ops/windows/Initialize-L12BuildEnvironment.ps1" -CacheRoot $env:L12_TASK_CACHE | Out-Null'],
    { env, stdio: 'pipe' })
  Object.assign(process.env, { TEMP: path.join(cache, 'temp'), TMP: path.join(cache, 'temp'), TMPDIR: path.join(cache, 'temp') })
  fs.mkdirSync(out, { recursive: true })
  const digest = () => crypto.createHash('sha256').update(fs.readFileSync(new URL('../src/l12/platform.ts', import.meta.url))).digest('hex')
  const before = digest()
  const program = compiled + '\nreturn { platformState, authState, remember, forgetAccount }'
  const server = createServer((req, res) => {
    const base = req.url?.startsWith('/testrun/') ? '/testrun/' : '/'
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' })
    res.end(`<script type="module">
      const {deploymentStorageKey}=await import(${JSON.stringify(deploymentSourceModuleUrl(base))});
      let disconnects=0; const net={nickname:''};
      window.actualAuth=Function('exports','reactive','localStorage','deploymentStorageKey','l12State','clearAuthRefreshRetry','disconnect',${JSON.stringify(program)})
        ({},v=>v,localStorage,deploymentStorageKey,net,()=>{},()=>disconnects++);
      window.actualAuth.disconnected=()=>disconnects;
    </script>`)
  })
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
  const origin = `http://127.0.0.1:${server.address().port}`
  let browser, closed = false
  const errors = []
  const report = { mode: 'two native same-origin tabs; actual platform AST methods and build-base resolver; no live server auth', status: 'running' }
  try {
    const require = createRequire(import.meta.url)
    const { chromium } = require('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
    browser = await chromium.launch({ channel: 'msedge', headless: true })
    browser.once('disconnected', () => { closed = true })
    const context = await browser.newContext()
    await context.route('**/*', route => route.request().url().startsWith(origin + '/') ? route.continue() : route.abort())
    const formalPage = await context.newPage(), testPage = await context.newPage()
    for (const page of [formalPage, testPage]) page.on('pageerror', error => errors.push(error.message))
    const open = async (page, suffix) => { await page.goto(origin + suffix); await page.waitForFunction(() => Boolean(window.actualAuth)) }
    const remember = (page, id, token) => page.evaluate(([id, token]) => window.actualAuth.remember({ id, username: id }, token), [id, token])
    const state = page => page.evaluate(() => JSON.parse(JSON.stringify(window.actualAuth.platformState)))
    await open(formalPage, '/')
    await remember(formalPage, 'formal-browser', 'formal-browser-token')
    await open(testPage, '/testrun/')
    assert.deepEqual(await state(testPage), { token: '', account: null })
    await remember(testPage, 'test-browser', 'test-browser-token')
    await open(formalPage, '/')
    assert.deepEqual(await state(formalPage), { token: 'formal-browser-token', account: { id: 'formal-browser', username: 'formal-browser' } })
    await open(testPage, '/testrun/')
    assert.equal((await state(testPage)).token, 'test-browser-token')
    await testPage.evaluate(() => window.actualAuth.forgetAccount())
    await open(formalPage, '/')
    assert.equal((await state(formalPage)).token, 'formal-browser-token')
    await remember(testPage, 'test-second', 'test-second-token')
    await formalPage.evaluate(() => window.actualAuth.forgetAccount())
    await open(testPage, '/testrun/')
    assert.equal((await state(testPage)).token, 'test-second-token')
    assert.deepEqual(errors, [])
    assert.equal(digest(), before, 'source must remain pinned during browser run')
    report.status = 'passed'
    report.formalLegacyReload = true
    report.mountedStartsUnauthenticated = true
    report.bothLogoutDirectionsIsolated = true
    report.testReloadKeepsOwnAccount = true
    report.sourceSha256 = before
  } catch (error) {
    report.status = 'failed'
    report.error = error instanceof Error ? error.message : String(error)
    throw error
  } finally {
    await browser?.close()
    await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()))
    Object.assign(report, { errors, browserClosed: closed, serverClosed: !server.listening })
    fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2))
    console.log(JSON.stringify(report))
  }
}
if (process.argv.includes('--browser')) await browserContract()
