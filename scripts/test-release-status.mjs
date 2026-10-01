import assert from 'node:assert/strict'
import { buildStatus, normalizeHealth, renderMarkdown } from './release-status.mjs'

const local = 'a'.repeat(40)
const remote = 'b'.repeat(40)
const observedAt = '2026-10-02T00:00:00.000Z'
const healthy = version => ({ service: 'twelve-legions', status: 'ok', maintenance: false, serverVersion: version })
const maintained = version => ({ service: 'twelve-legions', status: 'maintenance', maintenance: true, serverVersion: version })

const separate = buildStatus({
  head: local, branch: 'codex/candidate', dirtyCount: 2, remoteHead: remote,
  testrunHealth: healthy(remote), productionHealth: maintained(local), observedAt,
})
assert.equal(separate.development.worktree, 'dirty')
assert.equal(separate.git.relationToDevelopment, 'different')
assert.equal(separate.testrun.commit, remote)
assert.equal(separate.production.commit, local)
assert.equal(separate.maintenance.active, true)
assert.match(renderMarkdown(separate), /正式服维护 \| 开启/)

const unknown = buildStatus({
  head: local, branch: 'main', dirtyCount: 0, remoteHead: local,
  testrunHealth: null, productionHealth: null, observedAt,
})
assert.equal(unknown.git.relationToDevelopment, 'same')
assert.equal(unknown.testrun.commit, null)
assert.equal(unknown.production.commit, null)
assert.equal(unknown.maintenance.active, null)
assert.match(renderMarkdown(unknown), /正式服维护 \| 未知/)

for (const invalid of [
  { ...healthy(local), service: 'impostor' },
  { ...healthy(local), status: 'maintenance' },
  { ...maintained(local), maintenance: false },
  { ...healthy(local), serverVersion: 'a1b2c3' },
]) {
  assert.equal(normalizeHealth(invalid, 'fixture', observedAt).status, 'unknown')
}

console.log('Release state source passed: independent Git, test, production and maintenance facts; malformed/unavailable health fails unknown.')
