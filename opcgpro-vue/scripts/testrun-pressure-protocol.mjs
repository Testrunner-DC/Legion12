import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {execFileSync} from 'node:child_process'
import {createRequire} from 'node:module'
import {pathToFileURL} from 'node:url'

export const REPO = path.resolve(import.meta.dirname, '../..')
export const EVIDENCE_ROOT = 'D:/GPT/Legion12/artifacts/representative-load-current-20261022'
export const CACHE_ROOT = 'D:/GPT/Legion12/cache/primary/representative-load-current-20261022'
export const FROZEN_CORE = 'D:/GPT/Legion12/artifacts/performance-measurement-recovery-20261020/preparation-cadence-r6/pressure-load-r7-approved-preparation-cadence-r6.mjs'
export const FROZEN_SHA = 'F737052DCBFDA4A29FF53BA5DFAD76E771774C1C89C993275FFAFEAA023684FD'
export const KNOWN_HOSTS = 'D:/GPT/Legion12/artifacts/security-reinstall-prep-20261009/ssh-reinstall-pinned-20261011.known_hosts'
export const KNOWN_HOSTS_SHA = '8D0F71B88C9488400B7157CC84B77774D68BFA4E15F79E259656C81C6C2FC0C9'
export const IDENTITY = 'C:/Users/neptu/.ssh/id_ed25519'
export const PLAN = Object.freeze({stages: Object.freeze([10, 20, 30]), stageSeconds: 60,
  outsideUsers: 10, playerAccounts: 60, outerAccounts: 9, sessions: 70,
  actionIntervalMs: 2000, httpIntervalMs: 2000, wsIntervalMs: 2000,
  wsConfirmationMs: 3000, requestTimeoutMs: 10000, terminalFrames: 60,
  minimumLogicalBytes: 180 * 1024, maximumLogicalBytes: 300 * 1024,
  terminalMarkers: 6, guardStaleMs: 5000, passiveTailMs: 10000})
export const EXECUTION_GAPS = Object.freeze([
  'live-test-only-session-and-effective-F2-object-preflight-not-integrated',
  'bounded-normal-guard-supervision-and-shared-window-admission-not-integrated',
  'dual-durable-ledger-cold-reader-and-independent-recovery-session-not-integrated',
  'interrupted-registration-and-cleanup-recovery-not-integrated',
  'capture-before-teardown-and-final-dual-environment-readback-not-integrated',
  'actual-remote-lifecycle-has-not-been-verified',
])
export const digest = bytes => crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()
const keys = (value, names) => assert.deepEqual(Object.keys(value ?? {}).sort(), [...names].sort(), 'exact fields required')
const hash = value => assert.match(value, /^[0-9A-F]{64}$/)
const commit = value => assert.match(value, /^[0-9a-f]{40}$/)
const pid = value => assert.ok(Number.isSafeInteger(value) && value > 0)
const normalized = value => path.resolve(value).toLowerCase()
function readBounded(file, maximum = 1048576) {
  assert.ok(path.isAbsolute(file), 'absolute input required')
  const stat = fs.lstatSync(file)
  assert.ok(stat.isFile() && !stat.isSymbolicLink() && stat.size > 0 && stat.size <= maximum, 'bounded plain input required')
  const bytes = fs.readFileSync(file)
  assert.ok(bytes.length > 0 && bytes.length <= maximum)
  return bytes
}
function digestArchive(file) {
  const stat = fs.lstatSync(file)
  assert.ok(stat.isFile() && !stat.isSymbolicLink() && stat.size > 0 && stat.size <= 4294967296, 'bounded release archive required')
  const selected = crypto.createHash('sha256'), buffer = Buffer.alloc(1048576)
  const descriptor = fs.openSync(file, 'r')
  try {
    let count
    while ((count = fs.readSync(descriptor, buffer, 0, buffer.length, null)) > 0) selected.update(buffer.subarray(0, count))
  } finally {fs.closeSync(descriptor)}
  return selected.digest('hex').toUpperCase()
}
export function assertTestrunTarget(value, websocket = false) {
  const expected = websocket ? 'wss://legion-12.com/testrun/ws' : 'https://legion-12.com/testrun/'
  assert.equal(value, expected, 'only the exact isolated public testrun target is allowed')
  const selected = new URL(value)
  assert.equal(selected.username + selected.password + selected.search + selected.hash, '')
  return selected.href
}
export function strictSshArguments() {
  assert.equal(digest(readBounded(KNOWN_HOSTS, 8192)), KNOWN_HOSTS_SHA, 'pinned independent host record changed')
  assert.ok(fs.statSync(IDENTITY).isFile(), 'explicit existing identity required')
  return ['-i', IDENTITY, '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'StrictHostKeyChecking=yes',
    '-o', 'GlobalKnownHostsFile=NUL', '-o', `UserKnownHostsFile=${KNOWN_HOSTS}`,
    '-o', 'HostName=154.201.80.91', '-o', 'HostKeyAlias=154.201.80.91',
    '-o', 'ConnectTimeout=10', 'root@legion-12.com']
}
export function validateProtocolBinding(client) {
  keys(client, ['netSourcePath', 'netSha256', 'cardFrameSourcePath', 'cardFrameSha256', 'handArraySourcePath', 'handArraySha256'])
  for (const [fileKey, shaKey, relative, maximum] of [
    ['netSourcePath', 'netSha256', 'opcgpro-vue/src/l12/net.ts', 1048576],
    ['cardFrameSourcePath', 'cardFrameSha256', 'opcgpro-vue/src/l12/gameStateCardFrame.ts', 65536],
    ['handArraySourcePath', 'handArraySha256', 'opcgpro-vue/src/l12/handArrayDelta.ts', 65536],
  ]) {
    hash(client[shaKey])
    assert.equal(normalized(client[fileKey]), normalized(path.join(REPO, relative)), 'client source must be the authoritative leaf')
    assert.equal(digest(readBounded(client[fileKey], maximum)), client[shaKey], 'client source SHA mismatch')
  }
  return client
}
export function validateBinding(binding) {
  keys(binding, ['schema', 'kind', 'baseUrl', 'wsUrl', 'test', 'formal', 'client'])
  assert.equal(binding.schema, 1); assert.equal(binding.kind, 'normal-testrun-pressure-v1')
  assertTestrunTarget(binding.baseUrl); assertTestrunTarget(binding.wsUrl, true)
  keys(binding.test, ['commit', 'pid', 'dllSha256', 'manifestPath', 'manifestSha256', 'releaseSha256'])
  keys(binding.formal, ['commit', 'pid', 'dllSha256'])
  for (const identity of [binding.test, binding.formal]) {commit(identity.commit); pid(identity.pid); hash(identity.dllSha256)}
  assert.notEqual(binding.test.pid, binding.formal.pid, 'two isolated processes are required even for the same package')
  for (const field of ['manifestSha256', 'releaseSha256']) hash(binding.test[field])
  validateProtocolBinding(binding.client)
  const manifestBytes = readBounded(binding.test.manifestPath, 65536)
  assert.equal(digest(manifestBytes), binding.test.manifestSha256, 'manifest SHA mismatch')
  const manifest = JSON.parse(manifestBytes)
  assert.equal(manifest.schema, 3); assert.equal(manifest.commit, binding.test.commit)
  assert.equal(String(manifest.releaseSha256).toUpperCase(), binding.test.releaseSha256)
  // releaseBaseCommit describes package history, not the currently running formal environment.
  assert.ok(typeof manifest.releaseBaseCommit === 'string' &&
    (manifest.releaseBaseCommit === '' || /^[0-9a-f]{40}$/.test(manifest.releaseBaseCommit)))
  const archive = path.join(path.dirname(binding.test.manifestPath), `l12-release-${binding.test.commit}.tar.gz`)
  assert.equal(digestArchive(archive), binding.test.releaseSha256, 'release archive SHA mismatch')
  candidateSourceClosure(binding)
  return binding
}
export function candidateSourceClosure(binding) {
  const closure = []
  for (const [file, key] of [['net.ts', 'netSourcePath'], ['gameStateCardFrame.ts', 'cardFrameSourcePath'],
    ['handArrayDelta.ts', 'handArraySourcePath']]) {
    const bytes = execFileSync('git', ['-C', REPO, 'show', `${binding.test.commit}:opcgpro-vue/src/l12/${file}`],
      {windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], timeout: 15000, maxBuffer: 1048576})
    const current = readBounded(binding.client[key])
    const canonical = value => Buffer.from(value.toString('utf8').replaceAll('\r\n', '\n'))
    assert.equal(digest(canonical(bytes)), digest(canonical(current)), 'client content must match the candidate commit; only CRLF/LF is normalized')
    closure.push({path: `opcgpro-vue/src/l12/${file}`, sourceFileSha256: digest(current),
      committedBlobSha256: digest(bytes), lfContentSha256: digest(canonical(current))})
  }
  return closure
}

// The original loopback loader and its assertions remain unchanged. This loader is
// separately admitted to exactly one test URL and consumes the same current client functions.
export async function createTestrunProtocol(client) {
  validateProtocolBinding(client)
  const ts = createRequire(import.meta.url)('typescript')
  const compile = text => {
    const result = ts.transpileModule(text, {reportDiagnostics: true,
      compilerOptions: {target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext}})
    assert.ok(!(result.diagnostics ?? []).some(row => row.category === ts.DiagnosticCategory.Error))
    return result.outputText
  }
  const net = readBounded(client.netSourcePath).toString('utf8').replaceAll('\r\n', '\n')
  const start = net.indexOf('function rebuildDeltaGameState('), end = net.indexOf('function clearReconnectTimer()')
  assert.ok(start >= 0 && end > start)
  assert.equal(net.indexOf('function rebuildDeltaGameState(', start + 1), -1)
  const hand = await import('data:text/javascript;base64,' + Buffer.from(compile(readBounded(client.handArraySourcePath, 65536).toString('utf8'))).toString('base64'))
  assert.equal(hand.HAND_ARRAY_DELTA_CAPABILITY, 'handArrayDeltaV1')
  assert.equal(typeof hand.restoreHandSplices, 'function'); assert.equal(typeof hand.validHandDeltaChangePath, 'function')
  const raw = new Function('lastGameStateEnvelope', 'delta', 'negotiatedEventWindowDelta', 'negotiatedHandArrayDelta',
    'restoreHandSplices', 'validHandDeltaChangePath', compile(net.slice(start, end)) + ';return rebuildDeltaGameState(delta);')
  const reduce = (baseline, delta, eventWindow, negotiatedHand = false) => raw(baseline, delta, eventWindow,
    negotiatedHand === true, hand.restoreHandSplices, hand.validHandDeltaChangePath)
  const codec = await import('data:text/javascript;base64,' + Buffer.from(compile(readBounded(client.cardFrameSourcePath, 65536).toString('utf8'))).toString('base64'))
  assert.equal(typeof codec.restoreGameStateCardFrame, 'function'); assert.equal(typeof codec.GameStateCardFrameRecovery, 'function')
  return {reduce, codec}
}

export function adaptFrozenCore(source, adapterUrl) {
  assert.equal(digest(Buffer.from(source)), FROZEN_SHA, 'the workload must be the exact frozen F737 source')
  const replacements = [
    ['reconstructCandidateDelta as reduceEnvelope', 'reconstructCandidateDelta as historicReduceEnvelope'],
    ['let productionCodec=null', `import {createTestrunProtocol,assertTestrunTarget,cleanupOwnedTestrunAccounts} from ${JSON.stringify(adapterUrl)}\nlet representativeBinding=null\nlet reduceEnvelope=historicReduceEnvelope\nexport async function initializeNormalTestrunProtocol(binding){\n assertTestrunTarget(binding.baseUrl);assertTestrunTarget(binding.wsUrl,true)\n const loaded=await createTestrunProtocol(binding.client)\n representativeBinding=binding;productionCodec=loaded.codec;reduceEnvelope=loaded.reduce;deployedNetSha=binding.client.netSha256\n initializePresenceConsumer(binding.client)\n}\nlet productionCodec=null`],
    ['function initializePresenceConsumer() {', 'function initializePresenceConsumer(binding=FIXED) {'],
    ["const source=fs.readFileSync(FIXED.netSourcePath,'utf8')", "const source=fs.readFileSync(binding.netSourcePath,'utf8')"],
    ["if(sha256File(FIXED.netSourcePath)!==FIXED.deployedNetSha256)", "if(sha256File(binding.netSourcePath)!==(binding.netSha256??binding.deployedNetSha256))"],
    ["let negotiatedPresenceDelta=false,presenceRecoveryPending=false,presenceBaselineEpoch='';",
      "let negotiatedPresenceDelta=false,negotiatedPresenceOrderReuse=false,presenceRecoveryPending=false,presenceBaselineEpoch='';"],
    ["negotiatedPresenceDelta=message.capabilities?.presenceDeltaV1===true;",
      "negotiatedPresenceDelta=message.capabilities?.presenceDeltaV1===true;\n   negotiatedPresenceOrderReuse=negotiatedPresenceDelta&&message.capabilities?.presenceOrderReuseV1===true;"],
    ["reset(){negotiatedPresenceDelta=false;presenceRecoveryPending=false;",
      "reset(){negotiatedPresenceDelta=false;negotiatedPresenceOrderReuse=false;presenceRecoveryPending=false;"],
    ['async function runPressure(configuration) {', `async function runPressure(configuration) {\n // Deliberately fail closed until the independent remote lifecycle is implemented and verified.\n requireContract(false,'normal_pressure_execution_not_admitted')`],
    ['async connect(url) {', 'async connect(url) {\n    assertTestrunTarget(url instanceof URL ? url.href : url,true)'],
    ['this.negotiatedEventWindowDelta = false\n    this.presenceConsumer = null', 'this.negotiatedEventWindowDelta = false\n    this.negotiatedHandArrayDelta = false\n    this.presenceConsumer = null'],
    ['gameStateCardFrameV1: true },', 'gameStateCardFrameV1: true, handArrayDeltaV1: true, presenceOrderReuseV1: true },'],
    ["if (sessionMessage.capabilities?.gameStateCardFrameV1 !== true)", "if(!this.negotiatedHandArrayDelta)throw new SafeError('hand-array-capability-not-negotiated')\n    if (sessionMessage.capabilities?.gameStateCardFrameV1 !== true)"],
    ["if (type === 'session') {", "if (type === 'session') {\n      this.negotiatedHandArrayDelta=message.capabilities?.deltaGameState===true&&message.capabilities?.handArrayDeltaV1===true\n      if(this.negotiatedHandArrayDelta)this.metrics.currentHandArrayDelta.negotiatedSessions++\n      if(message.capabilities?.presenceDeltaV1===true&&message.capabilities?.presenceOrderReuseV1===true)this.metrics.currentPresenceOrderReuse.negotiatedSessions++"],
    ['const presenceResult = this.presenceConsumer?.handle(message)', "const orderReusePacket=type==='presenceDelta'&&Object.hasOwn(message,'orderFromBaseline')\n    if(orderReusePacket){this.metrics.currentPresenceOrderReuse.packets++;this.metrics.currentPresenceOrderReuse.wireBytes+=wireBytes}\n    const presenceResult = this.presenceConsumer?.handle(message)\n    if(orderReusePacket)this.metrics.currentPresenceOrderReuse[presenceResult?.applied?'applied':'rejected']++"],
    ['const rebuilt = reduceEnvelope(this.envelope, message, this.negotiatedEventWindowDelta)', "const handPacket=Object.hasOwn(message,'handSplices')\n      if(handPacket){this.metrics.currentHandArrayDelta.packets++;this.metrics.currentHandArrayDelta.wireBytes+=wireBytes}\n      const rebuilt = reduceEnvelope(this.envelope, message, this.negotiatedEventWindowDelta,this.negotiatedHandArrayDelta)\n      if(handPacket)this.metrics.currentHandArrayDelta[rebuilt?'applied':'rejected']++"],
    ['this.negotiatedEventWindowDelta = false\n    for (const waiter', 'this.negotiatedEventWindowDelta = false\n    this.negotiatedHandArrayDelta = false\n    for (const waiter'],
    ['const metrics = new Metrics()', 'const metrics = new Metrics()\n  metrics.currentHandArrayDelta={negotiatedSessions:0,packets:0,wireBytes:0,applied:0,rejected:0}\n  metrics.currentPresenceOrderReuse={negotiatedSessions:0,packets:0,wireBytes:0,applied:0,rejected:0}'],
    ['expectedCommit: { value: configuration.expectedCommit,', 'currentHandArrayDelta:{...metrics.currentHandArrayDelta},\n    currentPresenceOrderReuse:{...metrics.currentPresenceOrderReuse},\n    expectedCommit: { value: configuration.expectedCommit,'],
    ['export async function cleanupDualLedgerAccounts(context,adminIdentity,provisioning) {', 'export async function cleanupDualLedgerAccounts(context,adminIdentity,provisioning) {\n if(representativeBinding!==null)return cleanupOwnedTestrunAccounts(context,adminIdentity,provisioning.owned??[],cohortRequest(context))'],
  ]
  let adapted = source
  const changes = []
  for (const [before, after] of replacements) {
    assert.equal(adapted.split(before).length, 2, 'each frozen adaptation anchor must occur exactly once')
    adapted = adapted.replace(before, after)
    changes.push({beforeSha256: digest(before), afterSha256: digest(after)})
  }
  let restored = adapted
  for (const [before, after] of replacements.toReversed()) restored = restored.replace(after, before)
  assert.equal(restored, source, 'reverse restoration must preserve every byte of the original workload and cleanup')
  return {source: adapted, evidence: {schema: 1, frozenSha256: FROZEN_SHA, adaptedSha256: digest(adapted),
    restoredSha256: digest(restored), byteExactRoundtrip: true, changes, executionEnabled: false}}
}

// Request is injected: this helper never opens a socket. The caller must retain
// the durable intents and use an independently obtained recovery session on failure.
export async function cleanupOwnedTestrunAccounts(context, admin, accounts, request, wait = ms => new Promise(resolve => setTimeout(resolve, ms))) {
  assertTestrunTarget(context.baseUrl); assert.equal(context.local, false)
  commit(context.expectedCommit); assert.match(admin.token, /^[a-f0-9]{64}$/)
  assert.ok(Array.isArray(accounts) && accounts.length <= 69)
  assert.match(context.runId, /^[a-f0-9]{32}$/)
  assert.ok(Number.isFinite(context.cleanupDeadline))
  const result = {playerSessionsRevoked: 0, adminSessionRevoked: 0, syntheticAccountsDeleted: 0, failures: 0}
  const seen = new Set()
  let nextAdmissionAt = Date.now()
  const call = (relative, options = {}) => {
    assert.ok(Date.now() < context.cleanupDeadline, 'cleanup deadline exhausted')
    if (options.method) {
      assert.ok(Date.now() >= nextAdmissionAt, 'cleanup mutation cadence violated')
      nextAdmissionAt = Date.now() + 1300
    }
    return request(context.baseUrl, relative, {}, options)
  }
  const checkHealth = async () => {
    const health = await call('health')
    assert.equal(health.status, 'ok'); assert.equal(health.serverVersion, context.expectedCommit)
    assert.equal(health.maintenance, false)
  }
  // This helper stays serial. Pace actual mutation admission rather than adding
  // another fixed delay after all intervening responses and ownership reads.
  // Every write still follows its own fresh identity/maintenance check, and
  // consecutive admissions remain at least 1300 ms apart.
  const admit = async () => {
    const waitMs = Math.max(0, nextAdmissionAt - Date.now())
    assert.ok(Date.now() + waitMs < context.cleanupDeadline, 'cleanup deadline exhausted')
    if (waitMs > 0) await wait(waitMs)
    assert.ok(Date.now() < context.cleanupDeadline, 'cleanup deadline exhausted')
    await checkHealth()
    assert.ok(Date.now() < context.cleanupDeadline, 'cleanup deadline exhausted')
  }
  const verifyOwner = (detail, account) => {
    assert.equal(detail.id, account.id); assert.equal(detail.username, account.username)
    assert.equal(detail.createdAt, account.createdAt); assert.equal(detail.role, 'player')
    assert.equal(detail.deleted, false); assert.ok(Number.isInteger(detail.permissionVersion) && detail.permissionVersion >= 0)
  }
  for (const [index, account] of accounts.entries()) {
    try {
      assert.equal(account.preflightAbsent, true); assert.equal(account.registrationOutcome, 'confirmed')
      assert.ok(typeof account.id === 'string' && account.id.length >= 8 && !seen.has(account.id))
      assert.ok(typeof account.username === 'string' && typeof account.createdAt === 'string')
      if(account.token!==undefined)assert.match(account.token,/^[a-f0-9]{64}$/)
      seen.add(account.id)
      const route = 'api/admin/accounts/' + encodeURIComponent(account.id)
      await checkHealth()
      verifyOwner(await call(route, {token: admin.token}), account)
      await admit()
      // Logical deletion atomically revokes this account's sessions. Recheck
      // ownership/CAS after pacing, then prove both postconditions instead of
      // performing an extra session-revocation mutation before every deletion.
      const detail = await call(route, {token: admin.token}); verifyOwner(detail, account)
      await call('api/admin/v1/accounts/' + encodeURIComponent(account.id) + '/delete',
        {method: 'POST', token: admin.token, body: {reason: 'owned testrun representative pressure cleanup',
          idempotencyKey: `normal-pressure-${context.runId}-${index}`, expectedVersion: detail.permissionVersion,
          dryRun: false}, expected: [200, 204], retain: false})
      const verified = await call(route, {token: admin.token})
      assert.equal(verified.id, account.id); assert.equal(verified.deleted, true)
      assert.equal(verified.createdAt,account.createdAt);assert.equal(verified.role,'player')
      assert.notEqual(verified.username, account.username)
      const sessions=await call(route+'/sessions',{token:admin.token})
      assert.ok(Array.isArray(sessions)&&sessions.length===0,'deleted account sessions must be verifiably inactive')
      if(account.token!==undefined)await call('api/auth/me',{token:account.token,expected:[401],retain:false})
      result.playerSessionsRevoked++
      result.syntheticAccountsDeleted++
    } catch {result.failures++}
  }
  try {
    await admit()
    await call('api/auth/sessions/current', {method: 'DELETE', token: admin.token, expected: [200, 204], retain: false})
    await call('api/auth/me', {token: admin.token, expected: [401], retain: false})
    result.adminSessionRevoked = 1
  } catch {result.failures++}
  return result
}

export function assertGeneratedDirectories(output, cache) {
  const pwsh = process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
  const env = {...process.env, L12_NORMAL_OUTPUT: output, L12_NORMAL_CACHE: cache,
    L12_NORMAL_EVIDENCE_ROOT: EVIDENCE_ROOT, L12_NORMAL_CACHE_ROOT: CACHE_ROOT, L12_NORMAL_STORAGE: path.join(REPO, 'ops/windows/L12GeneratedStorage.ps1')}
  execFileSync(pwsh, ['-NoProfile', '-Command', '. $env:L12_NORMAL_STORAGE; '
    + '$pairs=@(@($env:L12_NORMAL_OUTPUT,$env:L12_NORMAL_EVIDENCE_ROOT),@($env:L12_NORMAL_CACHE,$env:L12_NORMAL_CACHE_ROOT)); '
    + 'foreach($pair in $pairs){$selected=Assert-L12GeneratedPath -Path $pair[0] -Purpose "normal pressure selected output"; '
    + '$root=Assert-L12GeneratedPath -Path $pair[1] -Purpose "normal pressure leased root"; '
    + 'if(-not $selected.StartsWith($root.TrimEnd([char]92)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "NORMAL_PRESSURE_OUTPUT_OUTSIDE_LEASE"}}'],
  {env, windowsHide: true, stdio: 'pipe', timeout: 15000})
}
export async function prepareOnly({bindingPath, bindingSha256, output, cache}) {
  assertGeneratedDirectories(output, cache)
  hash(bindingSha256)
  const bindingBytes = readBounded(bindingPath, 65536)
  assert.equal(digest(bindingBytes), bindingSha256)
  const binding = validateBinding(JSON.parse(bindingBytes))
  const sshArguments = strictSshArguments()
  await createTestrunProtocol(binding.client)
  const frozen = readBounded(FROZEN_CORE)
  const adapted = adaptFrozenCore(frozen.toString('utf8'), pathToFileURL(path.join(import.meta.dirname, 'testrun-pressure-protocol.mjs')).href)
  const generated = path.join(cache, 'normal-core-prepared-only.mjs')
  fs.writeFileSync(generated, adapted.source, {flag: 'wx'})
  const report = {schema: 1, status: 'prepared-only', executionEnabled: false, remoteApplyReady: false,
    onlineCapacityAccepted: false, networkCalls: 0, sshCalls: 0, businessRequests: 0,
    serviceWrites: 0, formalWrites: 0, serviceRestarts: 0, maintenanceWrites: 0,
    bindingSha256, packageChecksumsAndClientContentVerifiedOffline: true,
    candidateClientClosure: candidateSourceClosure(binding),
    sourceNormalization: 'Only CRLF to LF for Git-content comparison; raw runtime-loaded source SHA is retained independently.',
    liveIdentityVerified: false, test: binding.test, formal: binding.formal, client: binding.client,
    plan: PLAN, adaptation: adapted.evidence, generatedCore: generated,
    strictSsh: {knownHostsSha256: KNOWN_HOSTS_SHA, destination: sshArguments.at(-1), connectionAttempted: false},
    missingExecutionComponents: [...EXECUTION_GAPS], legacyR11AcceptanceClaimed: false,
    boundary: 'Offline preparation only. No process identity, resource budget, effective storage mode, cleanup or capacity was observed.'}
  fs.writeFileSync(path.join(output, 'prepared-only.json'), JSON.stringify(report, null, 2), {flag: 'wx'})
  return report
}
if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(import.meta.filename)) {
  const args = process.argv.slice(2)
  assert.equal(args.shift(), '--prepare', 'only --prepare exists; remote execution is not implemented')
  const values = {}
  while (args.length) {const name = args.shift(); assert.ok(['--binding', '--binding-sha', '--output', '--cache'].includes(name)); assert.ok(!Object.hasOwn(values, name)); values[name] = args.shift(); assert.equal(typeof values[name], 'string')}
  assert.equal(Object.keys(values).length, 4)
  const report = await prepareOnly({bindingPath: values['--binding'], bindingSha256: values['--binding-sha'], output: values['--output'], cache: values['--cache']})
  console.log(JSON.stringify({status: report.status, remoteApplyReady: false, onlineCapacityAccepted: false, networkCalls: 0, missingExecutionComponents: report.missingExecutionComponents}))
}
