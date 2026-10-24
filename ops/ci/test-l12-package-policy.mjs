import assert from 'node:assert/strict'
import { readPolicy, selectPackage } from './l12-package-policy.mjs'

const head = 'a'.repeat(40), before = 'b'.repeat(40)
const push = { ref: 'refs/heads/main', after: head, before, deleted: false }
let cases = 0
for (const paths of [[], ['AGENTS.md'], ['docs/HANDOFF.md', 'docs/TASK-LEDGER.md'],
  ['scripts/test-ci-host-step.mjs'], ['opcgpro-vue/scripts/fixtures/sample.txt'],
  ['TwelveLegions.Tests/Test.cs', 'TwelveLegions.Platform.Tests/Test.cs']]) {
  assert.equal(selectPackage('push', push, head, paths).package, false); cases++
}
assert.equal(selectPackage('push',push,head,['docs/HANDOFF.md']).verify,false); cases++
assert.equal(selectPackage('push',push,head,['docs/HANDOFF.md','scripts/test-ci-host-step.mjs']).verify,true); cases++
for (const name of ['opcgpro-vue/src/l12/GameBoard.vue', 'opcgpro-vue/package-lock.json',
  '服务端WebSocket/TwelveLegions/L12Actions.cs', '.github/workflows/verify-release.yml',
  'ops/ci/l12-card-assets-input.py', 'ops/windows/verify-l12.ps1', 'global.json',
  'scripts/invoke-l12-tests.ps1', 'scripts/verify-l12-change.ps1', 'unknown-config.json']) {
  const decision = selectPackage('push', push, head, ['docs/HANDOFF.md', name])
  assert.equal(decision.package,true)
  assert.equal(decision.verify,false,'canonical lane must not repeat standalone full verification'); cases++
}
assert.equal(selectPackage('pull_request', {}, head).package, false); cases++
assert.equal(selectPackage('workflow_dispatch', {}, head).package, true); cases++
assert.equal(selectPackage('push', { ...push, ref: 'refs/tags/v1.2.3' }, head).package, true); cases++
for (const [name, event, actualHead, paths] of [
  ['schedule', push, head, []], ['push', push, 'bad', []],
  ['push', { ...push, after: before }, head, []], ['push', { ...push, deleted: true }, head, []],
  ['push', { ...push, ref: 'refs/heads/other' }, head, []], ['push', push, head, undefined],
  ...['../docs/a', '/docs/a', 'docs//a', 'docs/./a', 'docs/../a', 'docs\\a', 'docs/a\nb'].map(value => ['push', push, head, [value]]),
]) { assert.throws(() => selectPackage(name, event, actualHead, paths)); cases++ }
const calls = []
const git = args => { calls.push(args); return args[0] === 'diff' ? 'docs/HANDOFF.md\0' : '' }
assert.equal(readPolicy('push', push, head, git).package, false)
assert.deepEqual(calls, [['cat-file', '-e', `${before}^{commit}`], ['merge-base', '--is-ancestor', before, head],
  ['diff', '--no-renames', '--name-only', '-z', before, head]]); cases++
calls.length = 0
readPolicy('pull_request', {}, head, () => { throw Error('PR_MUST_NOT_FETCH') }); cases++
let attempts = 0
const missing = args => { calls.push(args); if (attempts++ === 0) throw Error('missing'); return '' }
assert.equal(readPolicy('push', push, head, missing).package, false)
assert.deepEqual(calls[1], ['fetch', '--no-tags', '--depth=1', 'origin', before]); cases++
assert.throws(() => readPolicy('push', { ...push, before: '--upload-pack=evil' }, head, git), /CI_BASE_INVALID/); cases++
assert.equal(readPolicy('push', push, head, args => {
  if (args[0] === 'merge-base') throw Error('NON_ANCESTOR'); return ''
}).package, true); cases++
assert.throws(() => readPolicy('push', push, head, args => args[0] === 'diff' ? 'docs/a' : ''), /PATH_OUTPUT/); cases++
const initialCalls = []
assert.equal(readPolicy('push', { ...push, before: '0'.repeat(40) }, head, args => {
  initialCalls.push(args); return 'opcgpro-vue/src/main.ts\0'
}).package, true)
assert.deepEqual(initialCalls, [['ls-tree', '-r', '--name-only', '-z', head]]); cases++
assert.equal(readPolicy('push', {...push,ref:'refs/tags/v1',after:before},head,args=>{
  assert.deepEqual(args,['rev-parse','--verify',`${before}^{commit}`]); return head+'\n'
}).package,true); cases++
assert.throws(()=>readPolicy('push',{...push,ref:'refs/tags/v1',after:'--bad'},head,git),/TAG_OBJECT_INVALID/); cases++
assert.throws(()=>readPolicy('push',{...push,ref:'refs/tags/v1',after:before},head,()=>before),/TAG_COMMIT_MISMATCH/); cases++
console.log(`Canonical package policy: ${cases} cases passed; docs/QA-only stay verification-only, source builds retain the single canonical gate.`)
