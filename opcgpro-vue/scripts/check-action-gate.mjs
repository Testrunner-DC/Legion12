import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'

const filename = new URL('../src/l12/useActionGate.ts', import.meta.url)
let source = fs.readFileSync(filename, 'utf8')
source = source.replace("import { computed, reactive } from 'vue'", `
  const reactive = value => value
  const computed = getter => ({ get value() { return getter() } })
`)
const javascript = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { useActionGate } = await import(`data:text/javascript;base64,${Buffer.from(javascript).toString('base64')}`)
let checks = 0

{
  const gate = useActionGate()
  let finish
  let calls = 0
  const first = gate.run('friend-write', async () => {
    calls += 1
    await new Promise(resolve => { finish = resolve })
    return 'done'
  }, 0)
  assert.equal(gate.pending.value, true); checks += 1
  assert.equal(await gate.run('friend-write', async () => { calls += 1; return 'duplicate' }, 0), undefined); checks += 1
  assert.equal(calls, 1); checks += 1
  finish()
  assert.equal(await first, 'done'); checks += 1
  assert.equal(gate.pending.value, false); checks += 1
}

{
  const gate = useActionGate()
  const results = await Promise.all([
    gate.run('left', async () => 'left', 0),
    gate.run('right', async () => 'right', 0),
  ])
  assert.deepEqual(results, ['left', 'right']); checks += 1
  await assert.rejects(gate.run('failure', async () => { throw new Error('failed') }, 0), /failed/)
  assert.equal(gate.isPending('failure'), false); checks += 1
}

{
  const gate = useActionGate()
  const pending = gate.run('cooldown', async () => 'ok', 10)
  await Promise.resolve()
  assert.equal(gate.isPending('cooldown'), true); checks += 1
  assert.equal(await pending, 'ok')
  assert.equal(gate.isPending('cooldown'), false); checks += 1
}

const friends = fs.readFileSync(new URL('../src/l12/site/FriendsPage.vue', import.meta.url), 'utf8')
assert(friends.includes("useActionGate()")); checks += 1
assert(friends.includes("runAction('friends-write'")); checks += 1
assert((friends.match(/:disabled="actionBusy"/g) ?? []).length >= 8); checks += 1
assert(friends.includes(':aria-busy="actionBusy"')); checks += 1

console.log(`Player action gate: ${checks}/${checks} checks passed`)
