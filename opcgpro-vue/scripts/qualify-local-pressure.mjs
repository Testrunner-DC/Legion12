import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {spawn, execFileSync} from 'node:child_process'
import {pathToFileURL} from 'node:url'
import {adaptFrozenPressureCore, assertLocalPressureTarget, cleanupLocalQualificationAccounts, digest} from './local-pressure-protocol.mjs'
import {summarizeLocalPressureCost} from './local-pressure-cost.mjs'
import {AccountLedgerClient, NamedPipeLedgerTransport, registrationFingerprint} from '../../ops/qa/ledger/account-ledger-client-r2.mjs'
import {OWNED_FROZEN_CORE} from './owned-workload-sources.mjs'

const repo = path.resolve(import.meta.dirname, '../..')
const LEDGER_SINK_SHA = 'B74A6359DA15536248EE660F01B33DFD8A59376AF6D930DDC0F2BC54CC94DA81'
const LEDGER_CLIENT_SHA = '88BB6ACB68C3867CEC7A5E3DBA5A2B429A672FE4CDEF94C8D47D5921EA38B7F9'
assert.equal(digest(fs.readFileSync(path.join(repo, 'ops/qa/ledger/account-ledger-sink-r2.ps1'))), LEDGER_SINK_SHA)
assert.equal(digest(fs.readFileSync(path.join(repo, 'ops/qa/ledger/account-ledger-client-r2.mjs'))), LEDGER_CLIENT_SHA)
assert.ok(process.argv.slice(2).every(value => ['--host-probe', '--cleanup-probe', '--notification-probe', '--hand-array-delta', '--wire-cost'].includes(value)), 'only local probe switches are accepted')
const hostProbeOnly = process.argv.includes('--host-probe')
const cleanupProbeOnly = process.argv.includes('--cleanup-probe')
const notificationProbeOnly = process.argv.includes('--notification-probe')
const handArrayDeltaV1 = process.argv.includes('--hand-array-delta')
const wireCost = process.argv.includes('--wire-cost')
const accepted = process.env.L12_ACCEPTED_SERVER_ROOT
const hostAssembly = process.env.L12_LOCAL_PRESSURE_HOST_DLL
const cacheParent = process.env.L12_LOCAL_PRESSURE_CACHE
const evidenceParent = process.env.L12_LOCAL_PRESSURE_EVIDENCE
for (const value of [accepted, hostAssembly, cacheParent, evidenceParent]) assert.ok(value && path.isAbsolute(value))
const pwsh = process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
const guardEnv = {...process.env, L12_LOCAL_QA_CACHE: cacheParent, L12_LOCAL_QA_EVIDENCE: evidenceParent,
  L12_LOCAL_QA_ACCEPTED: accepted, L12_LOCAL_QA_HOST: path.dirname(hostAssembly)}
execFileSync(pwsh, ['-NoProfile', '-Command', `. '${repo}/ops/windows/L12GeneratedStorage.ps1'; `
  + '@($env:L12_LOCAL_QA_CACHE,$env:L12_LOCAL_QA_EVIDENCE,$env:L12_LOCAL_QA_ACCEPTED,$env:L12_LOCAL_QA_HOST,$env:TEMP) | ForEach-Object { $null=Assert-L12GeneratedPath -Path $_ -Purpose "local real pressure" }'],
{env: guardEnv, stdio: 'pipe', windowsHide: true})
const runId = crypto.randomUUID().replaceAll('-', ''), root = path.join(cacheParent, runId)
const output = path.join(evidenceParent, runId)
fs.mkdirSync(root, {recursive: false}); fs.mkdirSync(output, {recursive: false})
const nonce = crypto.randomBytes(16).toString('hex')
const owner = {schema: 1, kind: 'l12-local-real-pressure-v1', runId, cacheParent,
  serverSha256: digest(fs.readFileSync(path.join(accepted, 'GrandUMIServer.dll')))}
const acceptedBindingPath = process.env.L12_ACCEPTED_SOURCE_BINDING || 'D:/GPT/Legion12/artifacts/server-shutdown-20261021/platform-final-source-binding.json'
assert.ok(path.isAbsolute(acceptedBindingPath))
const acceptedBindingBytes = fs.readFileSync(acceptedBindingPath)
const acceptedBinding = JSON.parse(acceptedBindingBytes)
assert.equal(acceptedBinding.serverDll, owner.serverSha256)
assert.ok(Array.isArray(acceptedBinding.sources) && acceptedBinding.sources.length > 0)
for (const leaf of acceptedBinding.sources) {
  assert.ok(!path.isAbsolute(leaf.path) && !leaf.path.split(/[\\/]/).includes('..'))
  assert.equal(digest(fs.readFileSync(path.join(repo, leaf.path))), leaf.sha256)
}
fs.writeFileSync(path.join(root, 'owner.json'), JSON.stringify(owner), {flag: 'wx'})
const originalCore = OWNED_FROZEN_CORE
const originalBytes = fs.readFileSync(originalCore)
assert.equal(digest(originalBytes), 'F737052DCBFDA4A29FF53BA5DFAD76E771774C1C89C993275FFAFEAA023684FD')
const adaptedPath = path.join(root, 'local-core.mjs')
const adaptedCore=adaptFrozenPressureCore(originalBytes.toString('utf8'),
  pathToFileURL(path.join(import.meta.dirname, 'local-pressure-protocol.mjs')).href,{withEvidence:true})
fs.writeFileSync(adaptedPath, adaptedCore.source, {flag: 'wx'})
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
const processes = [], ledgers = []
const report = {schema: 1, status: 'starting', runId, scope: 'local Windows real HTTP/WS qualification only',
  productionTouched: false, testServiceTouched: false, onlineCapacityAccepted: false, runtimeRoot: root,
  handArrayDeltaRequested: handArrayDeltaV1,
  wireCostRequested: wireCost,
  originalCoreSha256: digest(originalBytes), adaptedCoreSha256: digest(fs.readFileSync(adaptedPath)), sourceRelocation:adaptedCore.evidence,
  hostAssemblySha256: digest(fs.readFileSync(hostAssembly)), owner, ownedProcesses: [],
  acceptedServerSourceEvidence: {path: acceptedBindingPath, sha256: digest(acceptedBindingBytes),
    sourceBefore: acceptedBinding.head, sourceLeavesVerified: acceptedBinding.sources.length,
    boundary: 'Source-level accepted DLL; its health metadata is not a fresh release or current HEAD claim.'}}
function startOwned(executable, args, env, name) {
  const log = fs.openSync(path.join(output, name + '.log'), 'wx')
  const child = spawn(executable, args, {env, cwd: root, windowsHide: true, stdio: ['ignore', log, log]})
  fs.closeSync(log)
  const exited = new Promise(resolve => {
    child.once('error', error => resolve({error: error.code}))
    child.once('exit', (code, signal) => resolve({code, signal}))
  })
  processes.push({child, exited, name}); report.ownedProcesses.push({pid: child.pid, name})
  return child
}
async function waitFor(file, child, milliseconds) {
  const until = Date.now() + milliseconds
  while (Date.now() < until) {
    if (child.exitCode !== null || child.signalCode !== null) throw new Error('owned-host-exited')
    if (fs.existsSync(file)) return JSON.parse(fs.readFileSync(file, 'utf8'))
    await delay(100)
  }
  throw new Error('owned-host-ready-timeout')
}
try {
  const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith('L12_')))
  const host = startOwned('C:/Program Files/dotnet/dotnet.exe', [hostAssembly], {...env,
    PATH: path.join(accepted, 'runtimes/win-x64/native') + path.delimiter + (env.PATH ?? env.Path ?? ''),
    L12_LOCAL_QUALIFICATION_ROOT: root, L12_ACCEPTED_SERVER_ROOT: accepted,
    L12_LISTEN_HOST: '127.0.0.1', L12_LOCAL_DIAGNOSTIC_NONCE: nonce}, 'host')
  const ready = await waitFor(path.join(root, 'ready.json'), host, 60000)
  assert.equal(ready.kind, 'local-real-http-ready'); assert.equal(ready.pid, host.pid)
  assert.equal(ready.serverSha256, owner.serverSha256)
  const baseUrl = assertLocalPressureTarget(ready.baseUrl)
  const read = async (route, options) => {
    assert.equal(new URL(route, baseUrl).origin, new URL(baseUrl).origin)
    const result = await fetch(new URL(route, baseUrl), {redirect: 'error', signal: AbortSignal.timeout(10000), ...options})
    assert.equal(result.status, 200, 'local HTTP preflight status')
    return result.json()
  }
  const health = await read('health')
  assert.equal(health.cards, 324); assert.equal(health.maintenance, false)
  const login = await read('api/auth/login', {method: 'POST', headers: {'content-type': 'application/json'},
    body: JSON.stringify({username: 'Admin', password: 'L12master'})})
  assert.match(login.token, /^[a-f0-9]{64}$/)
  const binding = Object.freeze({kind: 'local-real-http-qualification-v1', baseUrl,
    netSourcePath: path.join(repo, 'opcgpro-vue/src/l12/net.ts'),
    cardFrameSourcePath: path.join(repo, 'opcgpro-vue/src/l12/gameStateCardFrame.ts'),
    handArraySourcePath: path.join(repo, 'opcgpro-vue/src/l12/handArrayDelta.ts'),
    deployedNetSha256: digest(fs.readFileSync(path.join(repo, 'opcgpro-vue/src/l12/net.ts'))),
    cardFrameSha256: digest(fs.readFileSync(path.join(repo, 'opcgpro-vue/src/l12/gameStateCardFrame.ts'))),
    handArraySha256: digest(fs.readFileSync(path.join(repo, 'opcgpro-vue/src/l12/handArrayDelta.ts')))})
  Object.assign(report, {ready, observedHealth: health, protocolBinding: binding, status: 'ready'})
  const core = await import(pathToFileURL(adaptedPath).href)
  await core.initializeLocalQualificationProtocol(binding, {handArrayDeltaV1, wireCost})
  const ledgerSource = path.join(repo, 'ops/qa/ledger/account-ledger-sink-r2.ps1')
  for (const [index, maxSlots] of [60, 9].entries()) {
    const run = 'local-' + runId + '-' + index, pipe = 'l12-local-' + crypto.randomBytes(16).toString('hex')
    // Start #2 only when #1 is closed, preserving the original registration boundary.
    ledgers.push({run, pipe, maxSlots, source: ledgerSource, index,
      client: new AccountLedgerClient({runId: run, maxSlots, transport: new NamedPipeLedgerTransport({pipeName: pipe})})})
  }
  const startLedger = async index => {
    const item = ledgers[index]
    startOwned(pwsh, ['-NoProfile', '-File', item.source, '-Apply', '-RunId', item.run, '-PipeName', item.pipe,
      '-LedgerPath', path.join(root, 'ledger-' + index + '.dpapi'), '-MaxSlots', String(item.maxSlots),
      '-ConnectionTimeoutMs', '60000', '-FrameTimeoutMs', '30000'], env, 'ledger-' + index)
    const ready = startOwned(pwsh, ['-NoProfile', '-Command',
      "$until=[DateTime]::UtcNow.AddSeconds(15); while([DateTime]::UtcNow -lt $until){ if([IO.Directory]::EnumerateFiles('\\\\.\\pipe\\') -contains ('\\\\.\\pipe\\'+$env:L12_LOCAL_PIPE)){exit 0}; Start-Sleep -Milliseconds 25 }; exit 2"],
      {...env, L12_LOCAL_PIPE: item.pipe}, 'ledger-ready-' + index)
    const readiness = await processes.find(row => row.child === ready).exited
    assert.equal(readiness.code, 0, 'owned ledger must actually be listening before registration')
  }
  const durableLedgers = {schema: 1, kind: 'local-pressure-account-ledgers', runId,
    hostPid: host.pid, baseUrl, serverSha256: owner.serverSha256, observedVersion: health.serverVersion,
    protocolBinding: binding, groups: ledgers.map(item => ({runId: item.run, pipeName: item.pipe,
      maxSlots: item.maxSlots, ledgerPath: path.join(root, 'ledger-' + item.index + '.dpapi')}))}
  // Persist recovery ownership before the original core can register the first account.
  const controlPath = path.join(output, 'account-ledger-control.json')
  const controlFd = fs.openSync(controlPath, 'wx')
  try {fs.writeFileSync(controlFd, JSON.stringify(durableLedgers)); fs.fsyncSync(controlFd)} finally {fs.closeSync(controlFd)}
  assert.deepEqual(JSON.parse(fs.readFileSync(controlPath, 'utf8')), durableLedgers)
  report.accountLedgerControl = controlPath
  const authenticatedRead = route => read(route, {headers: {authorization: 'Bearer ' + login.token}})
  const analytics = await authenticatedRead('api/admin/analytics/cards')
  const audit = await authenticatedRead('api/admin/v1/audit?limit=50')
  const tournaments = await authenticatedRead('api/tournaments')
  const entries = value => Array.isArray(value) ? value.length : Array.isArray(value.items) ? value.items.length : 0
  report.preflight = {analyticsKeys: Object.keys(analytics), analyticsItems: entries(analytics),
    auditItems: entries(audit), tournamentItems: entries(tournaments)}
  assert.ok(entries(analytics) > 0 && entries(audit) > 0 && entries(tournaments) > 0,
    'real nonempty analytics/audit/tournament fixtures required')
  await authenticatedRead('api/tournaments/' + encodeURIComponent(tournaments.items[0].id))
  if (hostProbeOnly) {
    report.status = 'local-host-probe-finished'
    report.originalWorkloadExecuted = false
  } else if (cleanupProbeOnly || notificationProbeOnly) {
    await startLedger(0)
    const accounts = []
    for (let index = 0; index < 2; index++) {
      const username = 'l' + runId.slice(0, 8) + index, password = crypto.randomBytes(24).toString('base64url')
      const requestedAtUtc = new Date().toISOString()
      await ledgers[0].client.persistIntent({slot: index, username, password, requestedAtUtc,
        preflightAbsent: true, requestFingerprint: registrationFingerprint(username, password)})
      const registered = await read('api/auth/register', {method: 'POST', headers: {'content-type': 'application/json'},
        body: JSON.stringify({username, password})})
      await ledgers[0].client.persistConfirmed({slot: index, id: registered.account.id, createdAtUtc: registered.account.createdAt})
      accounts.push({username, requestedAtUtc, preflightAbsent: true, registrationOutcome: 'confirmed',
        id: registered.account.id, createdAt: registered.account.createdAt,
        token: registered.token, accountView: registered.account})
    }
    ledgers[0].client.close()
    let notificationError
    if (notificationProbeOnly) {
      const {verifyNotificationServerChain} = await import('./verify-notification-real-chain.mjs')
      try {report.notificationProbe = await verifyNotificationServerChain({baseUrl, identities: accounts, outputRoot: output, cacheRoot: root})}
      catch (error) {notificationError = error}
    }
    const request = async (_unusedBase, route, _deadline, options) => {
      assert.equal(new URL(route, baseUrl).origin, new URL(baseUrl).origin)
      const response = await fetch(new URL(route, baseUrl), {method: options.method ?? 'GET',
        headers: {authorization: 'Bearer ' + (options.token ?? ''), 'content-type': 'application/json'},
        body: options.body ? JSON.stringify(options.body) : undefined, redirect: 'error', signal: AbortSignal.timeout(10000)})
      assert.ok((options.expected ?? [200]).includes(response.status), 'local cleanup probe HTTP status')
      return options.retain === false ? undefined : response.json()
    }
    report.cleanupProbe = await cleanupLocalQualificationAccounts({local: true, baseUrl,
      expectedCommit: health.serverVersion, cleanupDeadline: Date.now() + 30000}, {token: login.token}, accounts, request)
    assert.deepEqual(report.cleanupProbe, {playerSessionsRevoked: 2, adminSessionRevoked: 1, syntheticAccountsDeleted: 2, failures: 0})
    if (notificationError) throw notificationError
    report.status = notificationProbeOnly ? 'local-notification-probe-finished' : 'local-cleanup-probe-finished'
    report.originalWorkloadExecuted = false
  } else {
  const result = await core.workloadTestHooks.runPressure({local: true, baseUrl,
    expectedCommit: health.serverVersion, expectedCommitSource: 'actual-owned-process-health-and-DLL-not-release-receipt',
    credentials: {players: null, admin: {token: login.token}, tournamentReader: null}, provisionPlayers: true,
    diagnosticNonce: nonce, ledgerClients: ledgers.map(item => item.client), beforeLedgerGroup: startLedger,
    localErrorFn: error => {report.localFailureDiagnostic = error},
    localBridgeErrorFn: error => {(report.localBridgeFailures ??= []).push(error)},
    stageSeconds: 60, httpIntervalMs: 2000, wsIntervalMs: 2000, requestTimeoutMs: 10000,
    stopFile: path.join(root, 'stop-load'), measurementCheckpointPath: path.join(output, 'measurement-before-account-cleanup.json'),
    progressFn: row => process.stdout.write(JSON.stringify(row) + '\n')})
  fs.writeFileSync(path.join(output, 'pressure-report.json'), JSON.stringify(result), {flag: 'wx'})
  Object.assign(report, {status: result.status === 'completed' ? 'local-run-finished' : 'local-run-not-qualified', clientStatus: result.status, failure: result.failure,
    stages: result.stages, terminalFrames: result.terminalFrames.length, coverage: result.coverage,
    handArrayDeltaObservation: {...result.localHandArrayDelta,
      scope: 'All received packets across preparation, measured stages and cleanup; not a measured-stage traffic rate.',
      wireBytesScope: 'Entire raw received handSplices packets, including rejected packets; not saved bytes or a traffic-benefit comparison.'},
    fullOriginalFinalAcceptance: false})
  if (wireCost) {
    report.wireCostObservation = result.localWireCost
    assert.ok(result.localWireCost?.reconciled === true && result.localWireCost.failures === 0,
      'diagnostic attribution must reconcile original numeric rows without missing bytes')
    report.diagnosticOnlyNoNewCapacityClaim = true
  }
  if (result.status !== 'completed') process.exitCode = 2
  }
} catch (error) {
  report.status = 'failed'
  report.error = String(error.code ?? error.message ?? error.name).slice(0, 160)
  process.exitCode = 1
} finally {
  for (const item of ledgers) item.client.close()
  fs.writeFileSync(path.join(root, 'stop'), '', {flag: 'a'})
  for (const item of processes.toReversed()) {
    let timer
    const exited = await Promise.race([item.exited, new Promise(resolve => {timer = setTimeout(() => resolve(null), 15000)})])
    clearTimeout(timer)
    if (!exited) {item.child.kill(); report.ownedProcesses.find(row => row.pid === item.child.pid).forcedStop = true; await item.exited}
    report.ownedProcesses.find(row => row.pid === item.child.pid).exit = exited ?? await item.exited
  }
  report.allOwnedProcessesExited = processes.every(item => item.child.exitCode !== null || item.child.signalCode !== null)
  report.gracefulHostStop = fs.existsSync(path.join(root, 'stopped.json'))
  for (const name of ['numeric-process-samples.json', 'stopped.json']) {
    if (fs.existsSync(path.join(root, name))) fs.copyFileSync(path.join(root, name), path.join(output, name), fs.constants.COPYFILE_EXCL)
  }
  if (report.accountLedgerControl) {
    try {
      report.coldLedgerRead = JSON.parse(execFileSync(pwsh, ['-NoProfile', '-File',
        path.join(repo, 'ops/qa/Verify-LocalPressureLedgers.ps1'), '-ControlPath', report.accountLedgerControl,
        '-ExpectedServerSha256', owner.serverSha256], {env: process.env, windowsHide: true, timeout: 15000, encoding: 'utf8', stdio: 'pipe'}))
      const expectedCounts = hostProbeOnly ? [0, 0] : cleanupProbeOnly || notificationProbeOnly ? [2, 0] : [60, 9]
      const actual = report.coldLedgerRead.groups
      report.coldLedgerReadComplete = expectedCounts.every((count, index) => {
        const group = actual.find(row => row.group === index)
        return count === 0 ? !group : group?.count === count && group.confirmed === count && group.sequence === 2 * count
      }) && actual.length === expectedCounts.filter(count => count > 0).length
      assert.equal(report.coldLedgerReadComplete, true, 'cold read must match the actual mode, not silently skip missing ledgers')
    } catch { report.coldLedgerReadFailed = true; report.status = 'local-cleanup-failed'; process.exitCode = 2 }
  }
  if (!report.allOwnedProcessesExited || !report.gracefulHostStop || report.ownedProcesses.some(item => item.forcedStop || item.exit?.code !== 0)) {
    report.cleanupFailed = true; report.status = 'local-cleanup-failed'; process.exitCode = 2
  }
  // Interpret already captured numbers only after every owned process exited.
  // This does not rerun pressure, repair its result, or claim online acceptance.
  if (report.allOwnedProcessesExited && fs.existsSync(path.join(output, 'pressure-report.json'))
      && fs.existsSync(path.join(output, 'numeric-process-samples.json'))) {
    try {
      const cost = summarizeLocalPressureCost({
        samples: JSON.parse(fs.readFileSync(path.join(output, 'numeric-process-samples.json'), 'utf8')),
        pressure: JSON.parse(fs.readFileSync(path.join(output, 'pressure-report.json'), 'utf8')),
        hostLog: fs.readFileSync(path.join(output, 'host.log'), 'utf8')})
      const costPath = path.join(output, 'numeric-cost-summary.json')
      fs.writeFileSync(costPath, JSON.stringify(cost, null, 2), {flag: 'wx'})
      report.offlineNumericCost = {path: costPath, sha256: digest(fs.readFileSync(costPath)),
        onlineCapacityAccepted: false}
    } catch (error) {
      report.offlineNumericCostFailed = true
      report.offlineNumericCostError = {
        name: typeof error?.name === 'string' && /^[A-Za-z]{1,80}$/.test(error.name) ? error.name : 'Error',
        message: typeof error?.message === 'string' ? error.message.slice(0, 300) : 'Offline numeric analysis failed',
      }
      if (report.status === 'local-run-finished') report.status = 'local-cost-analysis-failed'
      if (!process.exitCode) process.exitCode = 2
    }
  }
  fs.writeFileSync(path.join(output, 'local-result.json'), JSON.stringify(report, null, 2), {flag: 'wx'})
  console.log(JSON.stringify({status: report.status, clientStatus: report.clientStatus, error: report.error,
    output, onlineCapacityAccepted: false, processesExited: report.allOwnedProcessesExited}))
}
