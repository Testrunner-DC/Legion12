import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/site/RankingsPage.vue', import.meta.url), 'utf8')
const logic = source.slice(source.indexOf('let disposed ='), source.indexOf('const query ='))
const code = ts.transpileModule(logic, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
let nextTimer = 0
const timers = new Map()
const document = { hidden: false }
const requests = []
const rankedApi = { leaderboard(faction, range) { return new Promise((resolve, reject) => requests.push({ faction, range, resolve, reject })) }, history: async () => [] }
const state = Object.fromEntries(['faction','range','loading','error','players','analytics','honors'].map(k => [k, { value: k === 'range' ? 'season' : k === 'loading' ? false : '' }]))
const api = new Function(...Object.keys(state), 'rankedApi', 'document', 'setTimeout', 'clearTimeout', `${code}; return {load,onVisibilityChange,dispose(){disposed=true;clearTimeout(refreshTimer)}}`)(
  ...Object.values(state), rankedApi, document, (fn, delay) => { const id = ++nextTimer; timers.set(id, {fn,delay}); return id }, id => timers.delete(id))
const response = label => ({ players: [label], analytics: { label } })
const flush = async () => { for (let i=0;i<8;i++) await Promise.resolve() }
void api.load()
assert.equal(requests.length, 1)
state.range.value = '7d'
void api.load()
assert.equal(requests.length, 1, 'no overlapping requests')
requests[0].resolve(response('stale'))
await flush()
assert.equal(requests.length, 2)
assert.equal(requests[1].range, '7d')
assert.notDeepEqual(state.players.value, ['stale'])
requests[1].resolve(response('fresh'))
await flush()
assert.deepEqual(state.players.value, ['fresh'])
assert.equal(timers.size, 1)
assert.equal([...timers.values()][0].delay, 60000)
document.hidden = true
api.onVisibilityChange()
assert.equal(timers.size, 0)
document.hidden = false
api.onVisibilityChange()
assert.equal(requests.length, 3)
requests[2].reject(new Error('offline'))
await flush()
assert.deepEqual(state.players.value, ['fresh'], 'failure retains last good snapshot')
assert.equal(state.error.value, 'offline')
assert.equal(timers.size, 1, 'failure retries later, not tight loop')
api.dispose()
assert.equal(timers.size, 0)
await api.load()
assert.equal(requests.length, 3)
assert.match(source, /removeEventListener\('visibilitychange', onVisibilityChange\)/)
const admin = readFileSync(new URL('../src/l12/site/AdminOperationsPanel.vue', import.meta.url), 'utf8')
assert.match(admin, /:max="placementMaximumLimit"/)
assert.match(admin, /tiers\[2\]\?\.minimum/)
console.log('Ranking refresh: serial requests, stale-filter guard, visibility, retry, disposal and dynamic placement checks passed')
