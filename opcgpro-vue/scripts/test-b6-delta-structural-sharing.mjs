import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'

const source = fs.readFileSync(new URL('../src/l12/net.ts', import.meta.url), 'utf8')
const start = source.indexOf('function cloneDeltaContainer(')
const end = source.indexOf('function clearReconnectTimer(')
assert.ok(start >= 0 && end > start, 'clone-on-write delta implementation is present')
const body = source.slice(start, end)
const compiled = ts.transpileModule(body, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
const rebuild = (baseline, delta) => new Function('lastGameStateEnvelope', 'negotiatedEventWindowDelta',
  `${compiled}; return rebuildDeltaGameState(${JSON.stringify(delta)})`)(baseline, true)

const baseline = {
  type: 'gameState',
  state: {
    matchId: 'b6-sharing',
    revision: 7,
    lastAction: { text: 'stable', cards: [{ instanceId: 'a' }] },
    players: [{ name: '甲', troops: 2000 }, { name: '乙', troops: 2000 }],
    recentEvents: [],
  },
}
const delta = {
  matchId: 'b6-sharing',
  baseRevision: 7,
  revision: 8,
  changes: [
    { path: ['state', 'revision'], value: 8 },
    { path: ['state', 'players', 0, 'troops'], value: 1500 },
  ],
}
const rebuilt = rebuild(baseline, delta)
assert.equal(rebuilt.state.revision, 8)
assert.equal(rebuilt.state.players[0].troops, 1500)
assert.equal(baseline.state.revision, 7)
assert.equal(baseline.state.players[0].troops, 2000)
assert.equal(rebuilt.state.lastAction, baseline.state.lastAction, 'untouched branches are shared')
assert.notEqual(rebuilt.state.players, baseline.state.players, 'changed array is copied')
assert.notEqual(rebuilt.state.players[0], baseline.state.players[0], 'changed object is copied')

const malformed = structuredClone(delta)
malformed.changes.push({ path: ['state', 'missing', 'value'], value: 1 })
assert.equal(rebuild(baseline, malformed), null)
assert.equal(baseline.state.players[0].troops, 2000)

const section = source.slice(source.indexOf('function rebuildDeltaGameState('), end)
assert.equal(section.includes('JSON.parse(JSON.stringify(baseline))'), false)
assert.ok(section.includes('window.append.slice()'))
console.log('B6 delta structural sharing: 10/10 checks passed')
