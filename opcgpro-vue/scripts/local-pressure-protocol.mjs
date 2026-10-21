import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {createRequire} from 'node:module'
export {createLocalWireCostObserver} from './local-pressure-wire-cost.mjs'

export function assertLocalPressureTarget(base) {
  const value = new URL(base)
  assert.equal(value.protocol, 'http:')
  assert.equal(value.hostname, '127.0.0.1')
  assert.ok(value.port && Number(value.port) > 0)
  assert.equal(value.username + value.password + value.search + value.hash, '')
  assert.equal(value.pathname, '/')
  return value.href
}

export const digest = bytes => crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()

// Only the fresh loopback host uses this cleanup; online cleanup and FIXED are untouched.
export async function cleanupLocalQualificationAccounts(context, admin, accounts, request, wait = ms => new Promise(resolve => setTimeout(resolve, ms))) {
  assertLocalPressureTarget(context.baseUrl)
  assert.equal(context.local, true)
  assert.ok(typeof context.expectedCommit === 'string' && context.expectedCommit.length > 0)
  assert.ok(Array.isArray(accounts) && accounts.length <= 69)
  const result = {playerSessionsRevoked: 0, adminSessionRevoked: 0, syntheticAccountsDeleted: 0, failures: 0}
  const seen = new Set()
  const admitMutation = async () => {
    if (Date.now() + 1300 >= context.cleanupDeadline) throw new Error('local-cleanup-budget-exhausted')
    await wait(1300)
    if (Date.now() >= context.cleanupDeadline) throw new Error('local-cleanup-budget-exhausted')
  }
  for (const account of accounts) {
    try {
      assert.equal(account.preflightAbsent, true)
      assert.equal(account.registrationOutcome, 'confirmed')
      assert.ok(typeof account.id === 'string' && account.id.length >= 8 && !seen.has(account.id))
      seen.add(account.id)
      assert.ok(typeof account.username === 'string' && typeof account.createdAt === 'string')
      const health = await request('unused-local-base', 'health', {}, {})
      assert.equal(health.status, 'ok'); assert.equal(health.serverVersion, context.expectedCommit)
      const route = 'api/admin/accounts/' + encodeURIComponent(account.id)
      const detail = await request('unused-local-base', route, {}, {token: admin.token})
      assert.equal(detail.id, account.id); assert.equal(detail.username, account.username)
      assert.equal(detail.createdAt, account.createdAt); assert.equal(detail.role, 'player')
      assert.equal(detail.deleted, false); assert.ok(Number.isInteger(detail.permissionVersion))
      await admitMutation()
      await request('unused-local-base', route + '/sessions', {},
        {method: 'DELETE', token: admin.token, expected: [200, 204], retain: false})
      result.playerSessionsRevoked++
      await admitMutation()
      await request('unused-local-base', 'api/admin/v1/accounts/' + encodeURIComponent(account.id) + '/delete', {},
        {method: 'POST', token: admin.token, body: {reason: 'owned local qualification cleanup',
          idempotencyKey: 'local-delete-' + crypto.randomUUID(), expectedVersion: detail.permissionVersion,
          dryRun: false}, expected: [200, 204], retain: false})
      const verified = await request('unused-local-base', route, {}, {token: admin.token})
      assert.equal(verified.id, account.id); assert.equal(verified.deleted, true)
      assert.notEqual(verified.username, account.username)
      result.syntheticAccountsDeleted++
    } catch { result.failures++ }
  }
  try {
    const health = await request('unused-local-base', 'health', {}, {})
    assert.equal(health.status, 'ok'); assert.equal(health.serverVersion, context.expectedCommit)
    await admitMutation()
    await request('unused-local-base', 'api/auth/sessions/current', {},
      {method: 'DELETE', token: admin.token, expected: [200, 204], retain: false})
    result.adminSessionRevoked = 1
  } catch { result.failures++ }
  return result
}
export async function createLocalQualificationProtocol(binding, {handArrayDeltaV1 = false, wireCost = false} = {}) {
  assert.equal(binding.kind, 'local-real-http-qualification-v1')
  assertLocalPressureTarget(binding.baseUrl)
  assert.equal(typeof handArrayDeltaV1, 'boolean')
  assert.equal(typeof wireCost, 'boolean')
  const sources = [[binding.netSourcePath, binding.deployedNetSha256, 1048576],
    [binding.cardFrameSourcePath, binding.cardFrameSha256, 65536]]
  if (handArrayDeltaV1 || binding.handArraySourcePath !== undefined || binding.handArraySha256 !== undefined)
    sources.push([binding.handArraySourcePath, binding.handArraySha256, 65536])
  const checkedBytes = new Map()
  for (const [file, hash, max] of sources) {
    assert.ok(path.isAbsolute(file) && fs.statSync(file).size <= max)
    const bytes = fs.readFileSync(file)
    assert.ok(bytes.length <= max)
    assert.equal(digest(bytes), hash)
    checkedBytes.set(file, bytes)
  }
  const source = checkedBytes.get(binding.netSourcePath).toString('utf8').replaceAll('\r\n', '\n')
  const start = source.indexOf('function rebuildDeltaGameState('), end = source.indexOf('function clearReconnectTimer()')
  assert.ok(start >= 0 && end > start)
  assert.equal(source.indexOf('function rebuildDeltaGameState(', start + 1), -1)
  const ts = createRequire(import.meta.url)('typescript')
  const compile = text => {
    const result = ts.transpileModule(text, {reportDiagnostics: true,
      compilerOptions: {target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext}})
    assert.ok(!(result.diagnostics ?? []).some(row => row.category === ts.DiagnosticCategory.Error))
    return result.outputText
  }
  const rawReduce = new Function('lastGameStateEnvelope', 'delta', 'negotiatedEventWindowDelta',
    'negotiatedHandArrayDelta', 'restoreHandSplices', 'validHandDeltaChangePath',
    compile(source.slice(start, end)) + ';return rebuildDeltaGameState(delta);')
  const hand = sources.length === 3 ? await import('data:text/javascript;base64,' + Buffer.from(compile(
    checkedBytes.get(binding.handArraySourcePath).toString('utf8'))).toString('base64')) : null
  if (hand) {
    assert.equal(hand.HAND_ARRAY_DELTA_CAPABILITY, 'handArrayDeltaV1')
    assert.equal(typeof hand.restoreHandSplices, 'function')
    assert.equal(typeof hand.validHandDeltaChangePath, 'function')
  }
  const reduce = (baseline, delta, eventWindow, negotiatedHand = false) => rawReduce(baseline, delta,
    eventWindow, handArrayDeltaV1 && negotiatedHand === true, hand?.restoreHandSplices, hand?.validHandDeltaChangePath)
  const codec = await import('data:text/javascript;base64,' + Buffer.from(compile(
    checkedBytes.get(binding.cardFrameSourcePath).toString('utf8'))).toString('base64'))
  assert.equal(typeof codec.restoreGameStateCardFrame, 'function')
  assert.equal(typeof codec.GameStateCardFrameRecovery, 'function')
  return {reduce, codec, handArrayDeltaV1, wireCost}
}

// A generated, local-only adapter; the frozen remote entry and its binding are never modified.
export function adaptFrozenPressureCore(source, adapterUrl) {
  const replacements = [
    ['reconstructCandidateDelta as reduceEnvelope', 'reconstructCandidateDelta as remoteReduceEnvelope'],
    ['let productionCodec=null', `import {createLocalQualificationProtocol,assertLocalPressureTarget,cleanupLocalQualificationAccounts,createLocalWireCostObserver} from ${JSON.stringify(adapterUrl)}\nlet reduceEnvelope=remoteReduceEnvelope\nlet localProtocolBase=null\nlet localHandArrayDelta=false\nlet localWireCostEnabled=false\nexport async function initializeLocalQualificationProtocol(binding,options={}){\n const loaded=await createLocalQualificationProtocol(binding,options)\n productionCodec=loaded.codec;reduceEnvelope=loaded.reduce;deployedNetSha=binding.deployedNetSha256\n initializePresenceConsumer(binding);localProtocolBase=assertLocalPressureTarget(binding.baseUrl)\n localHandArrayDelta=loaded.handArrayDeltaV1;localWireCostEnabled=loaded.wireCost\n}\nlet productionCodec=null`],
    ['function initializePresenceConsumer() {', 'function initializePresenceConsumer(binding=FIXED) {'],
    ["const source=fs.readFileSync(FIXED.netSourcePath,'utf8')", "const source=fs.readFileSync(binding.netSourcePath,'utf8')"],
    ["if(sha256File(FIXED.netSourcePath)!==FIXED.deployedNetSha256)", "if(sha256File(binding.netSourcePath)!==binding.deployedNetSha256)"],
    ['async function runPressure(configuration) {', "async function runPressure(configuration) {\n if(localProtocolBase!==null){assertLocalPressureTarget(configuration?.baseUrl);requireContract(configuration?.local===true&&configuration.baseUrl===localProtocolBase,'local_qualification_cannot_run_remote')}"],
    ['const metrics = new Metrics()', 'const metrics = new Metrics()\n  if(localProtocolBase!==null)metrics.localHandArrayDelta={enabled:localHandArrayDelta,negotiatedSessions:0,packets:0,wireBytes:0,applied:0,rejected:0}\n  if(localProtocolBase!==null&&localWireCostEnabled)metrics.localWireCost=createLocalWireCostObserver()'],
    ['this.negotiatedEventWindowDelta = false\n    this.presenceConsumer = null', 'this.negotiatedEventWindowDelta = false\n    this.negotiatedHandArrayDelta = false\n    this.presenceConsumer = null'],
    ['gameStateCardFrameV1: true },', 'gameStateCardFrameV1: true, ...(localProtocolBase!==null&&localHandArrayDelta?{handArrayDeltaV1:true}:{}) },'],
    ["if (sessionMessage.capabilities?.gameStateCardFrameV1 !== true)", "if(localProtocolBase!==null&&localHandArrayDelta&&!this.negotiatedHandArrayDelta)throw new SafeError('hand-array-capability-not-negotiated')\n    if (sessionMessage.capabilities?.gameStateCardFrameV1 !== true)"],
    ["if (type === 'session') {", "if (type === 'session') {\n      this.negotiatedHandArrayDelta=localProtocolBase!==null&&localHandArrayDelta&&message.capabilities?.deltaGameState===true&&message.capabilities?.handArrayDeltaV1===true\n      if(this.negotiatedHandArrayDelta)this.metrics.localHandArrayDelta.negotiatedSessions++"],
    ['const rebuilt = reduceEnvelope(this.envelope, message, this.negotiatedEventWindowDelta)', "const localHandPacket=localProtocolBase!==null&&Object.hasOwn(message,'handSplices')\n      if(localHandPacket){this.metrics.localHandArrayDelta.packets++;this.metrics.localHandArrayDelta.wireBytes+=wireBytes}\n      const rebuilt = reduceEnvelope(this.envelope, message, this.negotiatedEventWindowDelta, this.negotiatedHandArrayDelta)\n      if(localHandPacket)this.metrics.localHandArrayDelta[rebuilt?'applied':'rejected']++"],
    ['this.negotiatedEventWindowDelta = false\n    for (const waiter', 'this.negotiatedEventWindowDelta = false\n    this.negotiatedHandArrayDelta = false\n    for (const waiter'],
    ['expectedCommit: { value: configuration.expectedCommit,', '...(localProtocolBase!==null?{localHandArrayDelta:{...metrics.localHandArrayDelta}}:{}),\n    ...(metrics.localWireCost?{localWireCost:metrics.localWireCost.summarize(metrics.ws)}:{}),\n    expectedCommit: { value: configuration.expectedCommit,'],
    ['this.metrics.recordWs(wireType,wireBytes,null)', 'this.metrics.recordWs(wireType,wireBytes,null)\n      this.metrics.localWireCost?.observe({raw,wireType,wireBytes,stage:this.metrics.currentStage})'],
    ["if(this.metrics.currentStage==='cleanup' && type==='presenceSnapshot')this.metrics.burstObserver.observePresence(this.numericClientSlot,raw)", "if(this.metrics.currentStage==='cleanup' && type==='presenceSnapshot')this.metrics.burstObserver.observePresence(this.numericClientSlot,raw)\n    this.metrics.localWireCost?.observe({raw,wireType,wireBytes,stage:this.metrics.currentStage})"],
    ['export async function cleanupDualLedgerAccounts(context,adminIdentity,provisioning) {', 'export async function cleanupDualLedgerAccounts(context,adminIdentity,provisioning) {\n if(localProtocolBase!==null)return cleanupLocalQualificationAccounts(context,adminIdentity,provisioning.owned??[],cohortRequest(context))'],
    ['requestFn:cohortRequest(context),delayFn:context.local?async()=>{}:delay,\n     stopFile:', 'requestFn:cohortRequest(context),delayFn:localProtocolBase!==null?delay:context.local?async()=>{}:delay,\n     stopFile:'],
    ['{requestFn:cohortRequest(context),delayFn:context.local?async()=>{}:delay,existsFn:fs.existsSync})', '{requestFn:cohortRequest(context),delayFn:localProtocolBase!==null?delay:context.local?async()=>{}:delay,existsFn:fs.existsSync})'],
    ["requireContract((options.expected??[200]).includes(response.status),'cohort_bridge_http_status')", "if(localProtocolBase!==null&&!(options.expected??[200]).includes(response.status))context.localBridgeErrorFn?.({status:response.status,operation:relative==='api/auth/register'?'register':relative==='api/auth/login'?'login':relative.startsWith('api/admin/accounts/')?'account-control':'other'});\n  requireContract((options.expected??[200]).includes(response.status),'cohort_bridge_http_status')"],
    ['if (error.preparationEvidence) payloadPreparation=error.preparationEvidence', "if(localProtocolBase!==null)configuration.localErrorFn?.({code:typeof error?.code==='string'&&/^[A-Za-z0-9_-]{1,80}$/.test(error.code)?error.code:'unclassified',name:typeof error?.name==='string'&&/^[A-Za-z0-9_-]{1,80}$/.test(error.name)?error.name:'Error'});\n    if (error.preparationEvidence) payloadPreparation=error.preparationEvidence"],
  ]
  let result = source
  for (const [before, after] of replacements) {
    assert.equal(result.split(before).length, 2, 'unique frozen adapter anchor required')
    result = result.replace(before, after)
  }
  let roundtrip = result
  for (const [before, after] of replacements.toReversed()) roundtrip = roundtrip.replace(after, before)
  assert.equal(roundtrip, source, 'all original workload, cleanup and remote guards must survive')
  return result
}
