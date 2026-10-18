import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { execFileSync } from 'node:child_process'
import ts from 'typescript'
import { parse, compileScript } from '@vue/compiler-sfc'

const front = path.resolve(import.meta.dirname, '..')
const source = fs.readFileSync(path.join(front, 'src/l12/friendlyRoomPreferences.ts'), 'utf8')
const page = fs.readFileSync(path.join(front, 'src/l12/site/BattleHubPage.vue'), 'utf8')
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 }, reportDiagnostics: true })
assert.deepEqual(compiled.diagnostics, [])
const preferences = await import('data:text/javascript;base64,' + Buffer.from(compiled.outputText).toString('base64'))
class MemoryStorage {
  values = new Map()
  writes = 0
  getItem(key) { return this.values.get(key) ?? null }
  setItem(key, value) { this.writes++; this.values.set(key, String(value)) }
}
let storage = new MemoryStorage()
Object.defineProperty(globalThis, 'localStorage', { configurable: true, get: () => storage })
const cases = []
function check(name, work) { work(); cases.push(name); console.log('PASS ' + name) }
const { isFriendlyDisasterMode: valid, loadFriendlyDisasterMode: load, saveFriendlyDisasterMode: save } = preferences

for (const mode of ['all', 'random', 'season', 'none']) check('strict legal mode roundtrip: ' + mode, () => {
  assert.equal(valid(mode), true); assert.equal(save('a', mode), true); assert.equal(load('a'), mode)
})
check('account anonymous isolation and ABA readback', () => {
  save('a', 'season'); save('b', 'none'); save(undefined, 'random')
  assert.equal(load('a'), 'season'); assert.equal(load('b'), 'none'); assert.equal(load(null), 'random'); assert.equal(load('a'), 'season')
  assert.equal(load('guest'), undefined)
})
check('scope encoding cannot alias guest or another account', () => {
  save('a:b', 'all'); save('a%3Ab', 'none')
  assert.equal(load('a:b'), 'all'); assert.equal(load('a%3Ab'), 'none'); assert.equal(load(), 'random')
})
check('unknown custom whitespace case and non-string values never persist', () => {
  const before = JSON.stringify([...storage.values]); const writes = storage.writes
  for (const value of ['custom','ALL',' all','random ', '', null, undefined, 0, {}, ['all']]) {
    assert.equal(valid(value), false); assert.equal(save('a', value), false)
  }
  assert.equal(JSON.stringify([...storage.values]), before); assert.equal(storage.writes, writes)
})
check('missing and corrupt stored values return absent without rewriting', () => {
  assert.equal(load('missing'), undefined)
  save('bad', 'all'); const key = [...storage.values.keys()].find(key => key.endsWith(':account:bad'))
  for (const bad of ['custom', 'ALL', ' all', '{bad', '"season"', '']) { storage.values.set(key, bad); assert.equal(load('bad'), undefined) }
  const writes = storage.writes; load('a'); load('bad'); assert.equal(storage.writes, writes)
})
check('read denial safely falls back', () => {
  const saved = storage; storage = { getItem() { throw new Error('synthetic read denied') } }
  assert.equal(load('a'), undefined); storage = saved
})
check('quota failure does not throw or claim persistence', () => {
  const saved = storage; storage = { setItem() { throw new Error('synthetic quota') } }
  assert.equal(save('a', 'random'), false); storage = saved; assert.equal(load('a'), 'season')
})
check('blocked localStorage getter is contained', () => {
  Object.defineProperty(globalThis, 'localStorage', { configurable: true, get() { throw new Error('synthetic security denial') } })
  assert.equal(load('a'), undefined); assert.equal(save('a', 'random'), false)
  Object.defineProperty(globalThis, 'localStorage', { configurable: true, get: () => storage })
})
check('actual SFC compiles and only the two existing controls persist explicit changes', () => {
  const descriptor = parse(page, { filename: 'BattleHubPage.vue' }).descriptor
  assert.ok(compileScript(descriptor, { id: 'friendly-preference-contract' }).content)
  assert.equal((descriptor.template.content.match(/@change="rememberFriendlyDisasterMode"/g) || []).length, 2)
  for (const model of ['roomOptions', 'editableRoomOptions'])
    assert.ok(descriptor.template.content.includes(`v-model="${model}.disasterMode" @change="rememberFriendlyDisasterMode"`))
  assert.equal((page.match(/saveFriendlyDisasterMode\(/g) || []).length, 1)
  const handler = page.slice(page.indexOf('function rememberFriendlyDisasterMode('), page.indexOf('function consumeOperationsPolicy('))
  assert.ok(handler.includes('isFriendlyDisasterMode(mode)'))
  assert.ok(handler.includes('friendlyDisasterModeChosen = true'))
  assert.ok(handler.includes('saveFriendlyDisasterMode(platformState.account?.id, mode)'))
})
check('current scope restore and first late default preserve stored or manual mode', () => {
  assert.ok(page.includes("watch(() => platformState.account?.id, restoreFriendlyDisasterMode, { flush: 'sync' })"))
  assert.ok(page.includes('friendlyDisasterModeChosen = saved !== undefined'))
  assert.ok(page.includes('disasterMode: friendlyDisasterModeChosen ? roomOptions.value.disasterMode'))
})
check('original room submission and prepare rule paths have no preference protocol', () => {
  assert.ok(page.includes('function saveRoomRules() { if (isRoomHost.value) updateRoomOptions(editableRoomOptions.value) }'))
  assert.ok(page.includes('createRoom(roomOptions.value)'))
  const net = fs.readFileSync(path.join(front, 'src/l12/net.ts'), 'utf8')
  assert.ok(net.includes("send({ type: 'createRoom', options })")); assert.ok(net.includes("send({ type: 'updateRoomOptions', options })"))
  assert.equal(net.includes('friendlyRoomPreferences'), false)
  assert.equal(/fetch\(|WebSocket|cardIds|disasterCardIds|setInterval/.test(source), false)
})
console.log(`Friendly room preference focused checks: ${cases.length} passed`)

if (process.env.L12_FRIENDLY_PREFERENCE_UNIT_OUTPUT) {
  execFileSync(process.env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-NonInteractive', '-Command',
    '. $env:L12_GUARD; $null=Assert-L12GeneratedPath -Path $env:L12_FRIENDLY_PREFERENCE_UNIT_OUTPUT -Purpose "friendly mode focused evidence"'],
  { stdio: 'pipe', env: { ...process.env, L12_GUARD: path.join(front, '../ops/windows/L12GeneratedStorage.ps1') } })
  const out = process.env.L12_FRIENDLY_PREFERENCE_UNIT_OUTPUT
  fs.mkdirSync(out, { recursive: true })
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify({ status: 'GREEN', count: cases.length, cases,
    sourceSha256: Object.fromEntries(['src/l12/friendlyRoomPreferences.ts','src/l12/site/BattleHubPage.vue','scripts/test-friendly-room-preferences.mjs'].map(file =>
      [file, crypto.createHash('sha256').update(fs.readFileSync(path.join(front,file))).digest('hex').toUpperCase()])) }, null, 2)+'\n')
}
