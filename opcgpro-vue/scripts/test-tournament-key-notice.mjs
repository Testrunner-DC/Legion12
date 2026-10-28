import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/net.ts', import.meta.url), 'utf8').replaceAll('\r\n', '\n')
const globals = source.slice(source.indexOf("let noticeIdentity = ''"), source.indexOf('let connectionAttemptSerial = 0'))
const startMarker = "      else if (message.type === 'tournamentKeyNotice') {"
const endMarker = "      else if (message.type === 'resourceVersions') {"
const start = source.indexOf(startMarker), end = source.indexOf(endMarker, start)
assert(start >= 0 && end > start)
const body = source.slice(start + startMarker.length, end).replace(/\s*}\s*$/, '')
const script = globals + '\nexport function inject(message: any, claimedGeneration: number | null, authToken: string) {' + body + '\n}'
const events = [], state = { accountId: 'a', sessionId: 'session-a', tournamentNotices: [] }, storage = { token: 'token-a' }
const sandbox = { exports: {}, module: { exports: {} }, Set, String, Number,
  l12State: state, localStorage: { getItem: () => storage.token }, deploymentStorageKey: key => key,
  window: { dispatchEvent: event => events.push(event.detail) },
  CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail } },
}
sandbox.exports = sandbox.module.exports
vm.runInNewContext(ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, sandbox)
const inject = sandbox.module.exports.inject
const packet = { type: 'tournamentKeyNotice', eventId: 'id-1', kind: 'announcement', tournamentCode: 'CODE1',
  tournamentName: '赛事', accountId: 'a', sessionId: 'session-a', connectionGeneration: 7 }
inject(packet, 7, 'token-a')
assert.equal(events.length, 1); assert.equal(events[0].kind, 'announcement')
assert.equal(sandbox.module.exports.tournamentNoticeTokenMatches('token-a'), true)
inject(packet, 7, 'token-a'); assert.equal(events.length, 1)
for (const change of [
  { eventId: 'bad1', accountId: 'other' }, { eventId: 'bad2', sessionId: 'old' },
  { eventId: 'bad3', connectionGeneration: 8 }, { eventId: 'bad4', kind: 'invented' },
  { eventId: '', kind: 'announcement' }, { eventId: 'bad5', tournamentCode: '' },
]) inject({ ...packet, ...change }, 7, 'token-a')
assert.equal(events.length, 1)
const storageSource = readFileSync(new URL('../src/l12/browserStorage.ts', import.meta.url), 'utf8')
const storageModule = { exports: {} }
vm.runInNewContext(ts.transpileModule(storageSource, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
  { module: storageModule, exports: storageModule.exports, globalThis: {}, Error })
const port = sandbox.localStorage
sandbox.localStorage = storageModule.exports.createOptionalBrowserStorage(() => { throw new Error('storage getter denied') })
assert.doesNotThrow(() => inject({ ...packet, eventId: 'denied-getter' }, 7, 'token-a'))
assert.equal(events.length, 1)
sandbox.localStorage = storageModule.exports.createOptionalBrowserStorage(() => ({ getItem() { throw new Error('storage read denied') } }))
assert.doesNotThrow(() => inject({ ...packet, eventId: 'denied-read' }, 7, 'token-a'))
assert.equal(events.length, 1)
sandbox.localStorage = port
storage.token = 'token-b'; inject({ ...packet, eventId: 'bad6' }, 7, 'token-a'); assert.equal(events.length, 1)
state.accountId = 'b'; state.sessionId = 'session-b'
inject({ ...packet, accountId: 'b', sessionId: 'session-b', eventId: 'id-1' }, 8, 'token-b')
assert.equal(events.length, 1)
inject({ ...packet, accountId: 'b', sessionId: 'session-b', connectionGeneration: 8, eventId: 'id-1' }, 8, 'token-b')
assert.equal(events.length, 2); assert.equal(events[1].accountId, 'b')
assert.equal(sandbox.module.exports.tournamentNoticeTokenMatches('token-a'), false)
const shell = readFileSync(new URL('../src/l12/site/SiteShell.vue', import.meta.url), 'utf8')
assert(shell.includes('tournamentNoticeTokenMatches(platformState.token)'))
assert(shell.includes('Object.hasOwn(tournamentNoticeText, value.kind)'))
assert(shell.includes('l12State.tournamentNotices = []'))
assert(shell.includes('dismissTournamentNotice()'))
assert(!shell.includes('v-html="tournamentNotices'))
console.log('Tournament key notice real net handler and shell contracts passed')