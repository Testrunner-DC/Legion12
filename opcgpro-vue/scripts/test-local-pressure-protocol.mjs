import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {pathToFileURL} from 'node:url'
import {assertLocalPressureTarget, adaptFrozenPressureCore, createLocalQualificationProtocol, cleanupLocalQualificationAccounts, digest} from './local-pressure-protocol.mjs'

const repo = path.resolve(import.meta.dirname, '../..')
let cases = 0
assert.equal(assertLocalPressureTarget('http://127.0.0.1:34567/'), 'http://127.0.0.1:34567/'); cases++
for (const url of ['https://127.0.0.1:34567/', 'http://localhost:34567/', 'http://154.201.80.91:34567/',
  'http://user:pass@127.0.0.1:34567/', 'http://127.0.0.1:34567/x', 'http://127.0.0.1:34567/?x=1',
  'http://127.0.0.1:34567/#x', 'http://127.0.0.1/']) {
  assert.throws(() => assertLocalPressureTarget(url)); cases++
}
const frozenPath = 'D:/GPT/Legion12/artifacts/performance-measurement-recovery-20261020/preparation-cadence-r6/pressure-load-r7-approved-preparation-cadence-r6.mjs'
const bytes = fs.readFileSync(frozenPath)
assert.equal(digest(bytes), 'F737052DCBFDA4A29FF53BA5DFAD76E771774C1C89C993275FFAFEAA023684FD'); cases++
const source = bytes.toString('utf8')
const adapted = adaptFrozenPressureCore(source, pathToFileURL(path.join(import.meta.dirname, 'local-pressure-protocol.mjs')).href)
assert.ok(adapted.includes('local_qualification_cannot_run_remote')); cases++
assert.throws(() => adaptFrozenPressureCore(source.replace('async function runPressure(configuration) {', 'async function different(configuration) {'), 'file:///unused'))
cases++
assert.equal(digest(fs.readFileSync(frozenPath)), digest(bytes)); cases++
const netSourcePath = path.join(repo, 'opcgpro-vue/src/l12/net.ts')
const cardFrameSourcePath = path.join(repo, 'opcgpro-vue/src/l12/gameStateCardFrame.ts')
const binding = {kind: 'local-real-http-qualification-v1', baseUrl: 'http://127.0.0.1:34567/',
  netSourcePath, cardFrameSourcePath, deployedNetSha256: digest(fs.readFileSync(netSourcePath)),
  cardFrameSha256: digest(fs.readFileSync(cardFrameSourcePath))}
const protocol = await createLocalQualificationProtocol(binding)
assert.equal(typeof protocol.reduce, 'function'); cases++
assert.equal(typeof protocol.codec.restoreGameStateCardFrame, 'function'); cases++
assert.equal(protocol.reduce(null, {type: 'gameStateDelta'}, false), null); cases++
for (const bad of [{...binding, deployedNetSha256: '0'.repeat(64)}, {...binding, cardFrameSha256: '0'.repeat(64)},
  {...binding, baseUrl: 'https://legion-12.com/'}, {...binding, kind: 'remote-release'}]) {
  await assert.rejects(() => createLocalQualificationProtocol(bad)); cases++
}
console.log(JSON.stringify({status: 'passed', cases, frozenCoreUnchanged: true, networkCalls: 0}))

const accounts = Array.from({length: 69}, (_, index) => ({preflightAbsent: true,
  registrationOutcome: 'confirmed', id: 'synthetic-' + index, username: 'local-' + index,
  createdAt: '2026-10-21T00:00:00Z'}))
const context = {local: true, baseUrl: binding.baseUrl, expectedCommit: 'actual-local-version',
  cleanupDeadline: Date.now() + 900000}
let mutations = 0, waits = 0
const deleted = new Set()
const request = async (_base, route, _deadline, options) => {
  if (route === 'health') return {status: 'ok', serverVersion: context.expectedCommit}
  if (options.method) {mutations++; if (route.endsWith('/delete')) deleted.add(route.split('/')[4]); return}
  const id = route.split('/')[3], row = accounts.find(item => item.id === id)
  return {...row, role: 'player', permissionVersion: 1, deleted: deleted.has(id),
    username: deleted.has(id) ? 'deleted-' + id : row.username}
}
const cleanup = await cleanupLocalQualificationAccounts(context, {token: 'synthetic'}, accounts, request,
  async ms => {assert.equal(ms, 1300); waits++})
assert.deepEqual(cleanup, {playerSessionsRevoked: 69, adminSessionRevoked: 1, syntheticAccountsDeleted: 69, failures: 0})
assert.equal(mutations, 139); assert.equal(waits, 139)
await assert.rejects(() => cleanupLocalQualificationAccounts({...context, baseUrl: 'https://legion-12.com/'}, {}, [], request))
let mismatchWrites = 0
const mismatch = await cleanupLocalQualificationAccounts(context, {}, [accounts[0]], async (_base, route, _deadline, options) => {
  if (options.method) mismatchWrites++
  return route === 'health' ? {status: 'ok', serverVersion: 'wrong-version'} : undefined
}, async () => {})
assert.equal(mismatch.syntheticAccountsDeleted, 0); assert.equal(mismatch.failures, 2)
assert.equal(mismatchWrites, 0)
console.log(JSON.stringify({status: 'passed', localCleanupCases: 5, syntheticAccounts: 69, networkCalls: 0}))
