import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const [commit, knownHosts, identity] = process.argv.slice(2)
assert.match(commit || '', /^[a-f0-9]{40}$/)
assert(knownHosts && identity && fs.existsSync(knownHosts) && fs.existsSync(identity))
const base = 'https://legion-12.com/testrun'
const ssh = ['-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '-o', `UserKnownHostsFile=${knownHosts}`, '-o', 'HostName=154.201.80.91', '-o', 'HostKeyAlias=154.201.80.91', '-o', 'IdentitiesOnly=yes', '-i', identity, 'root@154.201.80.91']
const preset = JSON.parse(fs.readFileSync(path.join(root, 'opcgpro-vue/public/data/l12/preset-decks.s1.json'), 'utf8'))[0]
const password = crypto.randomBytes(24).toString('hex')
const username = `qf${crypto.randomBytes(4).toString('hex')}`
let token, current, createdId, report
const cases = []
const request = async (endpoint, method = 'GET', body, authorization = token) => {
  const response = await fetch(`${base}${endpoint}`, { method, redirect: 'error',
    headers: { ...(body ? { 'Content-Type': 'application/json' } : {}), ...(authorization ? { Authorization: `Bearer ${authorization}` } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(20000),
  })
  const text = await response.text()
  let result
  try { result = text ? JSON.parse(text) : null } catch { throw new Error(`non-JSON test response: ${method} ${endpoint} (${response.status})`) }
  return { status: response.status, body: result }
}
const checked = (result, expected) => { assert.equal(result.status, expected, 'unexpected test API status'); return result.body }
try {
  assert.equal(checked(await request('/health', 'GET', undefined, null), 200).serverVersion, commit)
  token = checked(await request('/api/auth/register', 'POST', { username, password }, null), 200).token
  assert.equal(checked(await request('/api/decks'), 200).length, 0)
  current = checked(await request('/api/decks', 'POST', { ...preset, name: '[验收] F2临时牌库' }), 201)
  assert.equal(current.revision, 1)
  const originalId = current.id
  createdId = originalId
  cases.push('create')
  const stale = structuredClone(current)
  current = checked(await request(`/api/decks/by-id/${current.id}`, 'PUT', { expectedRevision: current.revision, deck: { ...preset, name: '[验收] F2临时改名' } }), 200)
  assert.equal(current.id, originalId); assert.equal(current.revision, 2)
  const clash = await request(`/api/decks/by-id/${current.id}`, 'PUT', { expectedRevision: stale.revision, deck: { ...preset, name: '[验收] 陈旧标签页' } })
  assert.equal(clash.status, 409); assert.equal(clash.body.code, 'deck_revision_conflict')
  let listed = checked(await request('/api/decks'), 200)
  assert.equal(listed.length, 1); assert.equal(listed[0].id, originalId); assert.equal(listed[0].name, current.name)
  cases.push('rename-same-identity', 'stale-tab-conflict-no-overwrite')
  token = checked(await request('/api/auth/login', 'POST', { username, password }, null), 200).token
  listed = checked(await request('/api/decks'), 200)
  assert.equal(listed.length, 1); assert.equal(listed[0].revision, current.revision)
  assert.deepEqual(listed[0].cardIds, preset.cardIds)
  cases.push('login-readback')
  const mode = spawnSync('ssh', [...ssh, `node --input-type=module - ${commit}`], { input: fs.readFileSync(path.join(root, 'ops/server/verify-l12-testrun-private-deck-mode.mjs'), 'utf8'), encoding: 'utf8', timeout: 60000 })
  if (mode.status !== 0) throw new Error('protected test mode read failed; credentials were not logged')
  const status = JSON.parse(mode.stdout.trim())
  assert.equal(status.commit, commit)
  assert.equal(status.effectiveMode, 'object'); assert.equal(status.writable, true)
  cases.push('protected-effective-mode-read')
  checked(await request(`/api/decks/by-id/${current.id}?expectedRevision=${current.revision}`, 'DELETE'), 204)
  current = null
  assert.equal(checked(await request('/api/decks'), 200).length, 0)
  token = checked(await request('/api/auth/login', 'POST', { username, password }, null), 200).token
  assert.equal(checked(await request('/api/decks'), 200).length, 0)
  cases.push('delete-no-reappearance')
  report = { status: 'passed', commit, testOnly: true, cases, persistence: status, cleanup: 'temporary deck deleted; isolated test account retained; no credentials emitted' }
} finally {
  if (token) {
    const remaining = checked(await request('/api/decks'), 200)
    const owned = remaining.filter(deck => createdId ? deck.id === createdId : ['[验收] F2临时牌库', '[验收] F2临时改名'].includes(deck.name))
    assert(owned.length <= 1, 'unexpected duplicate verification deck; stop automatic cleanup')
    for (const deck of owned) checked(await request(`/api/decks/by-id/${deck.id}?expectedRevision=${deck.revision}`, 'DELETE'), 204)
    assert.equal(checked(await request('/api/decks'), 200).length, 0, 'verification deck cleanup must be confirmed')
    const logout = await request('/api/auth/sessions', 'DELETE')
    assert(logout.status >= 200 && logout.status < 300, 'verification sessions cleanup must succeed')
  }
}
console.log(JSON.stringify(report, null, 2))
