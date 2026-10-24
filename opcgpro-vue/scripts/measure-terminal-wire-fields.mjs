import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { spawn, execFileSync } from 'node:child_process'
import { createInterface } from 'node:readline'
import { pathToFileURL, fileURLToPath } from 'node:url'
import { isDeepStrictEqual } from 'node:util'
import ts from 'typescript'
import { propertyFrameSourceModuleUrl } from './property-frame-source-module.mjs'
import {inspectOwnedWorkloadSources,ownedWorkloadFile} from './owned-workload-sources.mjs'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const hash = value => crypto.createHash('sha256').update(value).digest('hex').toUpperCase()
const size = value => Buffer.byteLength(JSON.stringify(value))

// Source ranges preserve the real UTF-8 writer's escaping. JSON.stringify of
// the parsed value would undercount e.g. HTML-sensitive \u003C text.
export function deltaFieldRanges(raw) {
  JSON.parse(raw)
  const ast = ts.parseJsonText('synthetic-wire.json', raw)
  assert.equal(ast.parseDiagnostics.length, 0)
  const root = ast.statements[0]?.expression
  assert(ts.isObjectLiteralExpression(root))
  const property = (node, name) => node.properties.find(item => item.name?.text === name)?.initializer
  const bytes = node => Buffer.byteLength(raw.slice(node.getStart(ast), node.end))
  const changes = property(root, 'changes')
  assert(changes && ts.isArrayLiteralExpression(changes))
  const result = changes.elements.map(node => {
    assert(ts.isObjectLiteralExpression(node))
    const value = property(node, 'value'); assert(value)
    return { changeBytes: bytes(node), valueBytes: bytes(value) }
  })
  const window = property(root, 'eventWindow')
  return { changes: result, eventWindowBytes: window ? bytes(window) : 0 }
}

// Offline experiment only: fixed public property names, never value-dependent dictionaries.
export function fixedPropertyTransform(raw, names, decode = false) {
  JSON.parse(raw)
  const forward = new Map(names.map((name, index) => [name, '@' + index.toString(36)]))
  const reverse = new Map([...forward].map(([name, token]) => [token, name]))
  let at = 0, start = 0, result = ''
  while (at < raw.length) {
    if (raw[at] !== '"') { at++; continue }
    const from = at++
    for (; at < raw.length; at++) {
      if (raw[at] === '\\') { at++; continue }
      if (raw[at] === '"') { at++; break }
    }
    const to = at
    let next = at
    while (next < raw.length && /\s/.test(raw[next])) next++
    if (raw[next] !== ':') continue
    const key = JSON.parse(raw.slice(from, to))
    let changed = key
    if (decode) {
      if (key.startsWith('~')) changed = key.slice(1)
      else if (key.startsWith('@')) { assert(reverse.has(key)); changed = reverse.get(key) }
    } else if (forward.has(key)) changed = forward.get(key)
    else if (key.startsWith('@') || key.startsWith('~')) changed = '~' + key
    if (changed !== key) { result += raw.slice(start, from) + JSON.stringify(changed); start = to }
  }
  return result + raw.slice(start)
}

async function main() {
  const [output, probe, accepted, expectedSha, mode] = process.argv.slice(2)
  assert(mode === undefined || mode === '--property-frame-v1')
  const propertyMode = mode === '--property-frame-v1'
  for (const value of [output, probe, accepted]) assert(path.isAbsolute(value))
  execFileSync(process.env.L12_PWSH, ['-NoProfile', '-Command',
    `. '${repo}/ops/windows/L12GeneratedStorage.ps1'; $null=Assert-L12GeneratedPath -Path '${output.replaceAll("'", "''")}' -Purpose 'offline actual terminal fields'`],
  { stdio: 'pipe', timeout: 15000, windowsHide: true })
  const reportFile = path.join(output, 'wire-attribution.json')
  assert(!fs.existsSync(reportFile))
  assert.equal(hash(fs.readFileSync(path.join(accepted, 'GrandUMIServer.dll'))), expectedSha)
  const commit = execFileSync('git', ['-C', repo, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim()
  execFileSync('git', ['-C', repo, 'diff', '--exit-code', commit, '--', '服务端WebSocket', 'opcgpro-vue/src/l12'], { stdio: 'pipe' })
  const sources = ['opcgpro-vue/src/l12/types.ts', 'opcgpro-vue/src/l12/net.ts',
    '服务端WebSocket/TwelveLegions/Models.cs',
    'opcgpro-vue/src/l12/handArrayDelta.ts', 'opcgpro-vue/src/l12/gameStateCardFrame.ts',
    'opcgpro-vue/src/l12/gameStatePropertyFrame.ts', 'opcgpro-vue/src/l12/gameStatePropertyNamesV1.json',
    'opcgpro-vue/scripts/property-frame-source-module.mjs',
    'ops/qa/TerminalWireProbe/Program.cs', 'ops/qa/TerminalWireProbe/TerminalWireProbe.csproj',
    'ops/qa/Invoke-L12TerminalWireProbe.ps1', 'opcgpro-vue/scripts/measure-terminal-wire-fields.mjs']
  const sourceHashes = Object.fromEntries(sources.map(name => [name, hash(fs.readFileSync(path.join(repo, name)))]))
  const publicNames = new Set(['type', 'state', 'payload', 'requestId', 'cardSnapshots', 'eventCardRefs',
    'lastActionCardRefs', 'baseRevision', 'changes', 'path', 'value', 'remove', 'eventWindow', 'handSplices', 'edits'])
  const typeSource = fs.readFileSync(path.join(repo, 'opcgpro-vue/src/l12/types.ts'), 'utf8')
  const visit = node => {
    if (ts.isPropertySignature(node) && node.name && (ts.isIdentifier(node.name) || ts.isStringLiteral(node.name))) publicNames.add(node.name.text)
    ts.forEachChild(node, visit)
  }
  visit(ts.createSourceFile('types.ts', typeSource, ts.ScriptTarget.Latest, true))
  // TS intentionally declares only part of the public card projection. Include
  // fixed, source-declared public model property names as well, never live keys
  // or a packet-dependent vocabulary. This remains a bounded offline prototype.
  const modelSource = fs.readFileSync(path.join(repo, '服务端WebSocket/TwelveLegions/Models.cs'), 'utf8')
  for (const match of modelSource.matchAll(/\bpublic\s+[^\r\n=;{}]+?\s+(\w+)\s*\{\s*get\b/g))
    publicNames.add(match[1][0].toLowerCase() + match[1].slice(1))
  const names = [...publicNames].sort()
  const loadTs = file => {
    const body = ts.transpileModule(fs.readFileSync(path.join(repo, file), 'utf8'),
      { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText
    const exports = {}; new Function('exports', body)(exports); return exports
  }
  const hands = loadTs('opcgpro-vue/src/l12/handArrayDelta.ts')
  const cards = loadTs('opcgpro-vue/src/l12/gameStateCardFrame.ts')
  const propertyDecoder = await import(propertyFrameSourceModuleUrl())
  const net = fs.readFileSync(path.join(repo, 'opcgpro-vue/src/l12/net.ts'), 'utf8')
  const start = net.indexOf('function rebuildDeltaGameState('), end = net.indexOf('function clearReconnectTimer(', start)
  assert(start > 0 && end > start)
  const netBody = ts.transpileModule(net.slice(start, end), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  const rebuild = new Function('lastGameStateEnvelope', 'negotiatedEventWindowDelta', 'negotiatedHandArrayDelta',
    'restoreHandSplices', 'validHandDeltaChangePath', 'delta', netBody + ';return rebuildDeltaGameState(delta)')
  // Reuse the frozen original workload selector, not a newly easier policy.
  inspectOwnedWorkloadSources()
  const selectorPath = ownedWorkloadFile('selector')
  const parentPath = path.resolve(path.dirname(selectorPath), '../large-terminal-public/large-terminal-contract.mjs')
  assert.equal(hash(fs.readFileSync(selectorPath)), '94222135D91627B18C1D13067A4A141C6AAA901A090C79980C882B5ED5449AF1')
  assert.equal(hash(fs.readFileSync(parentPath)), 'D328933729E54167078F123329A0880981C8C5A55771E32865F77B9DEAE1B456')
  const { selectLegalCommandWithAllBottom: select } = await import(pathToFileURL(selectorPath))
  const cases = [], fields = new Map(), propertyCosts = new Map(), commands = {}, rows = []
  const child = spawn('dotnet', [probe, path.join(accepted, 'TwelveLegions', 'Data'), expectedSha, ...(propertyMode ? ['property-frame-v1'] : [])],
    { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] })
  let stderr = '', pending = null, exited = false
  const exit = new Promise((resolve, reject) => {
    child.once('error', reject)
    child.once('exit', (code, signal) => { exited = true; if (pending) pending.reject(new Error('Probe exited before reply')); resolve({ code, signal }) })
  })
  child.stderr.on('data', data => { stderr = (stderr + data.toString()).slice(-10000) })
  createInterface({ input: child.stdout }).on('line', line => {
    if (!pending) { child.kill(); return }
    const current = pending; pending = null
    try { current.resolve(JSON.parse(line)) } catch (error) { current.reject(error) }
  })
  const timer = setTimeout(() => child.kill(), 120000)
  const ask = value => new Promise((resolve, reject) => {
    assert.equal(pending, null); assert.equal(exited, false); pending = { resolve, reject }
    child.stdin.write(JSON.stringify(value) + '\n')
  })
  const totals = { frames: 0, wireBytes: 0, logicalBytes: 0, fixedNamePrototypeBytes: 0, terminalWireBytes: 0,
    terminalFrames: 0, deltaFrames: 0, reconstructionChecks: 0, maximumLogicalBytes: 0, maximumPrepareMs: 0,
    comparisonWireBytes: 0, terminalComparisonWireBytes: 0, propertyFrameMessages: 0, comparisonIdentityChecks: 0 }
  const costs = { prepareMs: [], prepareAllocatedBytes: [], comparisonPrepareMs: [], comparisonPrepareAllocatedBytes: [],
    propertyRestoreMs: [] }
  function walk(value) {
    if (!value || typeof value !== 'object') return
    if (Array.isArray(value)) { value.forEach(walk); return }
    for (const [key, item] of Object.entries(value)) {
      const label = publicNames.has(key) ? key : '[other-property]'
      const old = propertyCosts.get(label) ?? { occurrences: 0, propertyNameBytes: 0 }
      old.occurrences++; old.propertyNameBytes += Buffer.byteLength(JSON.stringify(key)) + 1
      propertyCosts.set(label, old); walk(item)
    }
  }
  let currentSeed, baseline
  function inspect(reply, retainRow = false) {
    const snapshots = []
    for (const item of reply.snapshots) {
      const raw = Buffer.from(item.wire, 'base64').toString('utf8'), originalWire = JSON.parse(raw)
      let logicalSource = raw
      const propertyStarted = performance.now()
      const wire = originalWire.type === propertyDecoder.GAME_STATE_PROPERTY_FRAME_TYPE
        ? propertyDecoder.restoreGameStatePropertyFrame(originalWire, raw, source => { logicalSource = source }) : originalWire
      if (originalWire.type === propertyDecoder.GAME_STATE_PROPERTY_FRAME_TYPE) costs.propertyRestoreMs.push(performance.now() - propertyStarted)
      for (const name of ['prepareMs', 'prepareAllocatedBytes', 'comparisonPrepareMs', 'comparisonPrepareAllocatedBytes'])
        if (Number.isFinite(item[name])) costs[name].push(item[name])
      assert(wire, 'Actual property frame decoder rejected the producer')
      const comparisonRaw = item.comparisonWire ? Buffer.from(item.comparisonWire, 'base64').toString('utf8') : raw
      if (propertyMode) {
        assert(item.comparisonWire, 'Actual comparison codec packet missing')
        assert(isDeepStrictEqual(wire, JSON.parse(comparisonRaw)), 'Property coding changed the original actual codec envelope')
        totals.comparisonIdentityChecks++
      }
      if (originalWire.type === propertyDecoder.GAME_STATE_PROPERTY_FRAME_TYPE) totals.propertyFrameMessages++
      const logical = JSON.parse(Buffer.from(item.logical, 'base64').toString('utf8'))
      const restored = wire.type === 'gameStateDelta'
        ? rebuild(baseline[item.viewer], true, true, hands.restoreHandSplices, hands.validHandDeltaChangePath, wire)
        : wire.type === cards.GAME_STATE_CARD_FRAME_TYPE ? cards.restoreGameStateCardFrame(structuredClone(wire), logicalSource) : wire
      assert(isDeepStrictEqual(restored, logical), `Actual frontend reconstruction differs at seed=${currentSeed}, command=${reply.commands}, viewer=${item.viewer}`)
      totals.reconstructionChecks++
      baseline[item.viewer] = logical
      const packed = fixedPropertyTransform(comparisonRaw, names), unpacked = fixedPropertyTransform(packed, names, true)
      assert(isDeepStrictEqual(JSON.parse(unpacked), wire), 'Offline fixed-property semantic reconstruction differs')
      const before = Buffer.byteLength(raw), after = Buffer.byteLength(packed) + 96 // Conservative framing allowance; not a product envelope.
      totals.frames++; totals.wireBytes += before; totals.logicalBytes += Buffer.from(item.logical, 'base64').length
      totals.comparisonWireBytes += Buffer.byteLength(comparisonRaw)
      totals.fixedNamePrototypeBytes += Math.min(Buffer.byteLength(comparisonRaw), after)
      totals.maximumLogicalBytes = Math.max(totals.maximumLogicalBytes, Buffer.from(item.logical, 'base64').length)
      totals.maximumPrepareMs = Math.max(totals.maximumPrepareMs, item.prepareMs)
      if (wire.type === 'gameStateDelta') {
        totals.deltaFrames++
        const exact = deltaFieldRanges(comparisonRaw)
        assert.equal(exact.changes.length, wire.changes.length)
        for (const [index, change] of wire.changes.entries()) {
          const parts = change.path.map(part => typeof part === 'number' ? '[]' : publicNames.has(part) ? part : '*')
          const key = parts.slice(0, parts[1] === 'players' ? 4 : 2).join('/')
          const old = fields.get(key) ?? { count: 0, changeBytes: 0, valueBytes: 0 }
          old.count++; old.changeBytes += exact.changes[index].changeBytes; old.valueBytes += exact.changes[index].valueBytes
          fields.set(key, old)
        }
        if (wire.eventWindow) {
          const old = fields.get('eventWindow') ?? { count: 0, changeBytes: 0, valueBytes: 0 }
          old.count++; old.changeBytes += exact.eventWindowBytes; old.valueBytes += exact.eventWindowBytes
          fields.set('eventWindow', old)
        }
      }
      const terminal = logical.state.phase === 'GameOver'
      if (terminal) { totals.terminalWireBytes += before; totals.terminalComparisonWireBytes += Buffer.byteLength(comparisonRaw); totals.terminalFrames++ }
      walk(wire)
      if (retainRow || terminal) rows.push({ seed: currentSeed, viewer: item.viewer, commands: reply.commands,
        terminal, wireType: originalWire.type, logicalWireType: wire.type, wireBytes: before,
        comparisonWireBytes: Buffer.byteLength(comparisonRaw), logicalBytes: Buffer.from(item.logical, 'base64').length,
        fixedNamePrototypeBytes: Math.min(Buffer.byteLength(comparisonRaw), after), sha256: hash(raw), prepareMs: item.prepareMs,
        prepareAllocatedBytes: item.prepareAllocatedBytes })
      snapshots[item.viewer] = logical.state
    }
    return snapshots
  }
  try {
    for (const seed of [90210, 90211, 90212]) {
      currentSeed = seed; baseline = [null, null, null]
      let reply = await ask({ op: 'start', seed }), snapshots = inspect(reply, true)
      const room = { playedTurns: new Set() }
      let largest = 0
      for (let index = 0; index < 160 && snapshots[0].phase !== 'GameOver'; index++) {
        let selected, player
        for (const candidate of [0, 1]) {
          const next = select({ playerIndex: candidate }, snapshots[candidate], room)
          if (next.command) { selected = next; player = candidate; break }
        }
        assert(selected?.command, 'Original workload selector cannot advance synthetic state')
        if (selected.turnKey) room.playedTurns.add(selected.turnKey)
        reply = await ask({ op: 'command', player, command: selected.command })
        commands[selected.command.type] = (commands[selected.command.type] ?? 0) + 1
        snapshots = inspect(reply)
        largest = Math.max(largest, ...reply.snapshots.slice(0, 2).map(item => Buffer.from(item.logical, 'base64').length))
        // This is component attribution, not a replacement for the online payload-floor check.
        if (largest >= 180 * 1024 && snapshots[0].phase === 'Main' && index >= 32) break
      }
      if (snapshots[0].phase !== 'GameOver') {
        reply = await ask({ op: 'command', player: 0, command: { type: 'surrender' } })
        commands.surrender = (commands.surrender ?? 0) + 1; snapshots = inspect(reply)
      }
      assert.equal(snapshots[0].phase, 'GameOver')
      cases.push({ seed, acceptedCommands: reply.commands, largestPlayerLogicalBytes: largest })
    }
    child.stdin.end(JSON.stringify({ op: 'stop' }) + '\n')
    const ended = await exit
    assert.equal(ended.code, 0, stderr)
    assert.equal(ended.signal, null)
    for (const [name, sha] of Object.entries(sourceHashes)) assert.equal(hash(fs.readFileSync(path.join(repo, name))), sha)
    const distribution = samples => {
      const sorted = [...samples].sort((a, b) => a - b)
      const quantile = p => sorted.length ? sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * p) - 1)] : null
      return { count: sorted.length, total: sorted.reduce((a, b) => a + b, 0), p50: quantile(.5), p95: quantile(.95),
        p99: quantile(.99), max: sorted.at(-1) ?? null }
    }
    const report = { schema: 1, status: 'CURRENT_ASSEMBLY_OFFLINE_ATTRIBUTION_COMPLETE', syntheticOnly: true,
      commit, serverSha256: expectedSha, sourceHashes, propertyMode, originalSelectorPins: { selector: hash(fs.readFileSync(selectorPath)), parent: hash(fs.readFileSync(parentPath)) },
      cases, commands, totals, rows, fieldByteMethod: 'Exact source JSON node spans converted to UTF-8 bytes, excluding outer array commas; not parsed-value reserialization.',
      costs: Object.fromEntries(Object.entries(costs).map(([name, values]) => [name, distribution(values)])),
      propertyVocabulary: { count: names.length, sha256: hash(JSON.stringify(names)), adaptive: false },
      fields: [...fields].map(([field, value]) => ({ field, ...value })).sort((a, b) => b.changeBytes - a.changeBytes),
      propertyCosts: [...propertyCosts].map(([field, value]) => ({ field, ...value })).sort((a, b) => b.propertyNameBytes - a.propertyNameBytes),
      networkCalls: 0, databaseReadsOrWrites: 0, productSourceChanges: 0, onlineCapacityAccepted: false,
      childExited: true, boundary: propertyMode
        ? 'Current actual production codec property framing and original-codec control on identical in-memory commands/recipient projections; actual frontend restores both and checks complete envelope equality. Component costs include QA reflection, control is sequential not a concurrent server load. No room clock/ACK envelope, real sockets, representative cadence, production data, FPS or online latency claim. Separate fixed-name potential still includes a hypothetical96-byte header and is not the actual measured wire total.'
        : 'In-memory actual commands and three independent recipient codecs from the accepted test DLL; actual frontend reducers reconstruct every frame. No room clock or request ACK envelope, real sockets, production data, representative wall-clock cadence or FPS. Fixed-name encoding is an offline potential with 96 bytes assumed envelope overhead, not an accepted wire protocol or proven latency cure.' }
    fs.writeFileSync(reportFile, JSON.stringify(report, null, 2), { flag: 'wx' })
    console.log(JSON.stringify({ status: report.status, cases, commands, totals, topFields: report.fields.slice(0, 8), output: reportFile }, null, 2))
  } finally {
    clearTimeout(timer)
    if (!exited) { child.kill(); await exit }
    if (stderr) fs.writeFileSync(path.join(output, 'probe-stderr.log'), stderr, { flag: 'wx' })
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { await main() } catch (error) { console.error(error.stack); process.exitCode = 1 }
}
