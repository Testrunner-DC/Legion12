import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'

const filename = new URL('../src/l12/useActionGate.ts', import.meta.url)
let source = fs.readFileSync(filename, 'utf8')
source = source.replace("import { computed, getCurrentScope, onScopeDispose, reactive } from 'vue'", `
  const reactive = value => value
  const computed = getter => ({ get value() { return getter() } })
  const getCurrentScope = () => ({})
  const onScopeDispose = callback => { globalThis.__disposeActionGate = callback }
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
  const first = gate.run('left-player', async () => 'left', 40)
  await Promise.resolve()
  assert.equal(gate.isPending('left-player'), true); checks += 1
  assert.equal(await gate.run('right-player', async () => 'right', 0), 'right'); checks += 1
  globalThis.__disposeActionGate()
  assert.equal(await first, 'left'); checks += 1
  assert.equal(gate.pending.value, false); checks += 1
  assert.equal(await gate.run('after-dispose', async () => 'unexpected', 0), undefined); checks += 1
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
assert(friends.includes('runAction(actionKey(player)')); checks += 1
assert(!friends.includes("runAction('friends-write'")); checks += 1
assert((friends.match(/:disabled="actionPending\(actionKey\(/g) ?? []).length >= 8); checks += 1
assert(!friends.includes('busy || actionBusy')); checks += 1
assert(friends.includes(':aria-busy="actionBusy"')); checks += 1

const deckLibrary = fs.readFileSync(new URL('../src/l12/site/DeckLibraryPage.vue', import.meta.url), 'utf8')
assert(deckLibrary.includes('useActionGate()')); checks += 1
assert(deckLibrary.includes('publicDeckActionKey') && deckLibrary.includes('runAction(publicDeckActionKey(entry.id')); checks += 1
assert(deckLibrary.includes('actionPending(publicDeckActionKey(entry.id))')); checks += 1
assert(deckLibrary.includes('runAction(`public-deck:publish:${deck.name}`')); checks += 1
assert(deckLibrary.includes("actionPending(`public-deck:publish:${publishName}`)")); checks += 1

const publicDeck = fs.readFileSync(new URL('../src/l12/site/PublicDeckDetailPage.vue', import.meta.url), 'utf8')
const publicDeckContent = fs.readFileSync(new URL('../src/l12/site/PublicDeckContentEditor.vue', import.meta.url), 'utf8')
assert(publicDeck.includes('useActionGate()')); checks += 1
assert(publicDeck.includes('publicDeckActionKey')); checks += 1
assert((publicDeck.match(/runAction\(publicDeckActionKey\(id, accountId\)/g) ?? []).length === 3); checks += 1
assert((publicDeck.match(/actionPending\(publicDeckActionKey\(entry.id\)\)/g) ?? []).length >= 6); checks += 1
assert(publicDeck.includes("accountId === platformState.account?.id")); checks += 1
assert(publicDeckContent.includes('useActionGate()') && publicDeckContent.includes('run(actionKey.value')); checks += 1
assert(publicDeckContent.includes(':disabled="isPending(actionKey)"')); checks += 1

const shell = fs.readFileSync(new URL('../src/l12/site/SiteShell.vue', import.meta.url), 'utf8')
assert(shell.includes('runOnlineAction(onlineFriendActionKey(player.accountId, accountId)')); checks += 1
assert(shell.includes('onlineActionPending(onlineFriendActionKey(player.accountId))')); checks += 1
assert(shell.includes("`online-friend:${accountId}:${playerId}`")); checks += 1
assert(!shell.includes('onlineActionBusy')); checks += 1

const tournaments = fs.readFileSync(new URL('../src/l12/site/AdminTournamentWorkbench.vue', import.meta.url), 'utf8')
assert(tournaments.includes('runGatedAction(key')); checks += 1
assert((tournaments.match(/runAction\(tournamentActionKey\(item\)/g) ?? []).length >= 13); checks += 1
assert(tournaments.includes(':disabled="actionPending(tournamentActionKey(item))')); checks += 1
assert(!tournaments.includes('if (busy.value)')); checks += 1
assert(!tournaments.includes('Promise.all([refreshTournaments(), loadFriends(), loadDeckCatalog(), getEffectiveOperationsPolicy(), syncSavedDecksFromAccount()])')); checks += 1
assert(tournaments.includes('mergeTournamentSnapshot(result.items, tournaments.value, baselineVersions)')); checks += 1
assert(!tournaments.includes('if (!incoming || current.version > incoming.version)')); checks += 1

const mergeFilename = new URL('../src/l12/tournamentSnapshotMerge.ts', import.meta.url)
const mergeJavascript = ts.transpileModule(fs.readFileSync(mergeFilename, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { mergeTournamentSnapshot } = await import(`data:text/javascript;base64,${Buffer.from(mergeJavascript).toString('base64')}`)
const baseline = new Map([['kept', 1], ['deleted', 4], ['advanced', 2]])
const merged = mergeTournamentSnapshot(
  [{ id: 'kept', version: 1 }, { id: 'advanced', version: 2 }],
  [{ id: 'kept', version: 1 }, { id: 'deleted', version: 4 }, { id: 'advanced', version: 3 }, { id: 'created', version: 1 }],
  baseline,
)
assert.deepEqual(merged.map(item => `${item.id}:${item.version}`).sort(), ['advanced:3', 'created:1', 'kept:1']); checks += 1

console.log(`Player action gate: ${checks}/${checks} checks passed`)
