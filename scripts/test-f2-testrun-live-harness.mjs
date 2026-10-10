// Offline regression for the test-only online verifier. No network or credentials.
import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import path from 'node:path'
import crypto from 'node:crypto'
import { fileURLToPath } from 'node:url'

const source = fs.readFileSync(new URL('./verify-f2-testrun-live.mjs', import.meta.url), 'utf8')
const commit = 'a'.repeat(40)
const preset = { name: 'preset', masterId: 'master', cardIds: ['b', 'a', 'a'] }
const copy = value => JSON.parse(JSON.stringify(value))
async function run(fault = '') {
  const initial = Array.from({ length: 6 }, (_, i) => ({ id: `existing-${i}`, name: i === 0 ? '[验收] F2临时牌库' : `preset-${i}`, revision: 1, cardIds: ['a'] }))
  let decks = copy(initial), logout = false, baselineFailed = false
  const logs = [], calls = []
  const reply = (status, body) => ({ status, text: async () => body == null ? '' : JSON.stringify(body) })
  const fetch = async (url, options) => {
    assert.equal(options.redirect, 'error')
    assert(url.startsWith('https://legion-12.com/testrun/'))
    const endpoint = url.slice('https://legion-12.com/testrun'.length)
    const method = options.method
    calls.push([method, endpoint])
    const body = options.body ? JSON.parse(options.body) : null
    if (endpoint === '/health') return reply(200, { serverVersion: commit })
    if (endpoint === '/api/auth/register' || endpoint === '/api/auth/login') return reply(200, { token: 'isolated-test-token' })
    if (endpoint === '/api/auth/sessions' && method === 'DELETE') { logout = true; return reply(204) }
    if (endpoint === '/api/decks' && method === 'GET') {
      if (fault === 'baseline-read' && !baselineFailed) { baselineFailed = true; return reply(503, {}) }
      return reply(200, decks)
    }
    if (endpoint === '/api/decks' && method === 'POST') {
      if (fault === 'created-baseline-id') return reply(201, decks[0])
      const created = { ...body, id: 'temporary', revision: 1 }
      decks.push(created); return reply(201, created)
    }
    if (endpoint === '/api/decks/by-id/temporary' && method === 'PUT') {
      const current = decks.find(deck => deck.id === 'temporary')
      if (body.expectedRevision !== current.revision) return reply(409, { code: 'deck_revision_conflict' })
      const next = { ...body.deck, id: current.id, revision: current.revision + 1 }
      decks = decks.map(deck => deck.id === next.id ? next : deck)
      if (fault === 'baseline-change') decks[0].name = 'unexpected'
      return reply(200, next)
    }
    if (endpoint.startsWith('/api/decks/by-id/temporary?') && method === 'DELETE') {
      if (fault === 'delete-failure') return reply(503, {})
      decks = decks.filter(deck => deck.id !== 'temporary'); return reply(204)
    }
    throw new Error(`unexpected mock call: ${method} ${endpoint}`)
  }
  const context = vm.createContext({ process: { argv: ['node', 'script', commit, 'known-hosts', 'identity'] }, console: { log: line => logs.push(line) }, fetch, AbortSignal, structuredClone, URL })
  const modules = new Map()
  const values = {
    'node:assert/strict': assert,
    'node:fs': { existsSync: () => true, readFileSync: name => {
      if (name.endsWith('preset-decks.s1.json')) return '\uFEFF' + JSON.stringify([preset])
      assert(name.endsWith('verify-l12-testrun-private-deck-mode.mjs')); return 'guarded-test-mode-reader'
    } },
    'node:path': path, 'node:crypto': crypto,
    'node:url': { fileURLToPath },
    'node:child_process': { spawnSync: (executable, args, options) => {
      assert.equal(executable, 'ssh'); assert(args.includes('root@154.201.80.91'))
      assert.equal(args.at(-1), `node --input-type=module - ${commit}`)
      assert.equal(options.input, 'guarded-test-mode-reader')
      return fault === 'mode-read' ? { status: 1 } : { status: 0, stdout: JSON.stringify({ commit, effectiveMode: 'object', writable: true }) }
    } },
  }
  const module = new vm.SourceTextModule(source, { context, initializeImportMeta: meta => { meta.url = new URL('./verify-f2-testrun-live.mjs', import.meta.url).href } })
  await module.link(specifier => {
    assert(Object.hasOwn(values, specifier), `unexpected import ${specifier}`)
    if (!modules.has(specifier)) {
      const value = values[specifier], names = ['default', ...Object.keys(value)]
      modules.set(specifier, new vm.SyntheticModule([...new Set(names)], function () {
        this.setExport('default', value)
        for (const name of Object.keys(value)) this.setExport(name, value[name])
      }, { context }))
    }
    return modules.get(specifier)
  })
  let failed = false
  try { await module.evaluate() } catch { failed = true }
  assert.equal(failed, Boolean(fault))
  assert(logout, `${fault || 'success'} must revoke its sessions`)
  assert(!calls.some(([method, endpoint]) => method === 'DELETE' && endpoint.includes('existing-')))
  if (fault !== 'baseline-change') assert.deepEqual(decks.filter(deck => deck.id !== 'temporary'), initial)
  if (!fault) {
    const report = JSON.parse(logs.at(-1))
    assert.equal(report.status, 'passed'); assert.equal(report.preExistingDecksPreserved, 6)
    assert.equal(report.cases.length, 6); assert.equal(decks.length, 6)
  } else assert(!logs.some(line => JSON.parse(line).status === 'passed'), 'failure must never emit success')
}
for (const fault of ['', 'baseline-read', 'baseline-change', 'mode-read', 'delete-failure', 'created-baseline-id']) await run(fault)
console.log('F2 online verifier offline harness passed: BOM, 6 seeded decks, preserved baseline, wrong create identity, mode/read/delete failures, known-token cleanup and no false success (6 scenarios).')
