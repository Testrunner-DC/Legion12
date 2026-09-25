import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

const frontendRoot = fileURLToPath(new URL('..', import.meta.url))
const repositoryRoot = join(frontendRoot, '..')
const source = path => readFileSync(join(repositoryRoot, path), 'utf8').replaceAll('\r\n', '\n')
const deferred = () => {
  let resolve
  let reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

globalThis.document = { hidden: false }
globalThis.__friendPlatformState = { account: { id: 'account-a' } }
globalThis.__friendPlans = []
globalThis.__friendCalls = 0

const filename = join(frontendRoot, 'src', 'l12', 'friendResource.ts')
let friendSource = readFileSync(filename, 'utf8')
  .replace("import { reactive } from 'vue'", 'const reactive = value => value')
  .replace("import { friendApi, platformState, type PlatformFriend } from './platform'", `
    const platformState = globalThis.__friendPlatformState
    const friendApi = { overview: () => {
      globalThis.__friendCalls += 1
      const plan = globalThis.__friendPlans.shift()
      if (!plan) throw new Error('missing friend overview plan')
      return plan.promise
    } }
  `)
const compiled = ts.transpileModule(friendSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, isolatedModules: true },
  fileName: filename,
  reportDiagnostics: true,
})
assert.deepEqual((compiled.diagnostics || []).filter(item => item.category === ts.DiagnosticCategory.Error), [])
const moduleUrl = `data:text/javascript;base64,${Buffer.from(compiled.outputText).toString('base64')}`
const resource = await import(moduleUrl)

let checks = 0
const first = deferred()
globalThis.__friendPlans.push(first)
const firstRead = resource.refreshFriendResource()
const duplicateRead = resource.refreshFriendResource()
assert.strictEqual(firstRead, duplicateRead)
assert.equal(globalThis.__friendCalls, 1, 'concurrent readers must share one overview request')
first.resolve({ friends: [{ accountId: 'friend-1' }], requests: [], blocked: [] })
await firstRead
assert.equal(resource.friendResource.friends[0].accountId, 'friend-1')
checks += 2

const stale = deferred()
globalThis.__friendPlans.push(stale)
const staleRead = resource.refreshFriendResource()
globalThis.__friendPlatformState.account = { id: 'account-b' }
resource.resetFriendResource('account-b')
const fresh = deferred()
globalThis.__friendPlans.push(fresh)
const freshRead = resource.refreshFriendResource()
stale.resolve({ friends: [{ accountId: 'wrong-account' }], requests: [], blocked: [] })
await staleRead
assert.deepEqual(resource.friendResource.friends, [], 'an old account response must not overwrite the new account')
const duplicateFreshRead = resource.refreshFriendResource()
assert.strictEqual(freshRead, duplicateFreshRead, 'an old finally block must not detach the new account request')
assert.equal(globalThis.__friendCalls, 3, 'account switch may start one new request but not duplicate it')
fresh.resolve({ friends: [{ accountId: 'account-b-friend' }], requests: [], blocked: [] })
await freshRead
assert.equal(resource.friendResource.friends[0].accountId, 'account-b-friend')
checks += 4

const burstOne = deferred()
const burstTwo = deferred()
globalThis.__friendPlans.push(burstOne, burstTwo)
const burstRead = resource.refreshFriendResource()
resource.invalidateFriendResource()
resource.invalidateFriendResource()
assert.equal(globalThis.__friendCalls, 4, 'a burst during an in-flight request must not start parallel reads')
burstOne.resolve({ friends: [{ accountId: 'intermediate' }], requests: [], blocked: [] })
await burstRead
await Promise.resolve()
assert.equal(globalThis.__friendCalls, 5, 'a dirty in-flight read must replay exactly once')
burstTwo.resolve({ friends: [{ accountId: 'latest' }], requests: [], blocked: [] })
await Promise.resolve()
await Promise.resolve()
assert.equal(resource.friendResource.friends[0].accountId, 'latest')
checks += 3

const pollingFiles = [
  'opcgpro-vue/src/l12/site/FriendRequestNotifications.vue',
  'opcgpro-vue/src/l12/site/FriendsPage.vue',
  'opcgpro-vue/src/l12/site/MaintenanceTicker.vue',
  'opcgpro-vue/src/l12/site/RankedIntegrityNotice.vue',
  'opcgpro-vue/src/l12/site/SiteShell.vue',
]
for (const path of pollingFiles) assert.ok(!source(path).includes('setInterval('), `${path} restored page polling`)
assert.equal((source('opcgpro-vue/src/l12/site/BattleHubPage.vue').match(/setInterval\(/g) || []).length, 1,
  'BattleHub may retain only its local countdown clock')
assert.ok(source('服务端WebSocket/TwelveLegions/L12WebSocketServer.cs').includes('/api/friends/overview'))
assert.ok(source('服务端WebSocket/TwelveLegions/L12WebSocketServer.ResourceSync.cs').includes('resourceChanged'))
assert.ok(source('opcgpro-vue/src/l12/net.ts').includes("60_000"), 'offline fallback must stay low-frequency')
const resourceSyncServer = source('服务端WebSocket/TwelveLegions/L12WebSocketServer.ResourceSync.cs')
const tournamentPages = [
  source('opcgpro-vue/src/l12/site/TournamentHubPage.vue'),
  source('opcgpro-vue/src/l12/site/TournamentDetailPage.vue'),
  source('opcgpro-vue/src/l12/site/AdminTournamentWorkbench.vue'),
].join('\n')
assert.ok(resourceSyncServer.includes('[TournamentsResource]'), 'server snapshot must expose the tournament revision')
assert.ok(resourceSyncServer.includes('NotifyTournamentsChanged()'),
  'successful tournament mutations must broadcast their revision')
assert.ok(tournamentPages.includes("'l12-resource-tournaments'"),
  'the tournament center must react to tournament resource changes')
checks += pollingFiles.length + 7

console.log(`Resource change synchronization: ${checks}/${checks} checks passed`)
