#!/usr/bin/env node
// Payload-floor r2: direct CLI stays disabled; remote use is admitted only by the pinned v3 thin wrapper.

import {ORIGINAL_WORKLOAD,validateOriginalWorkloadPlan} from 'file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/full-workload-contract-r7.mjs'
import {createPressureMeasurementCheckpoint, persistPressureMeasurementCheckpoint} from 'file:///D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/scripts/lib/l12-pressure-checkpoint.mjs'
import crypto from 'node:crypto'
import fs from 'node:fs'
import { execFileSync } from 'node:child_process'
import path from 'node:path'
import { performance } from 'node:perf_hooks'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'
import { cleanupRoomProtocol, isLegionBlockedByVisibleDisaster } from 'file:///C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/load-v2-r2/pressure-pure-selftest.mjs'

import { WsBurstObserver, finalizeClientObservationStatus } from 'file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/ws-burst-r7.mjs'
import { initializeCandidateProtocol, reconstructCandidateDelta as reduceEnvelope, toolTestHooks as cohort }
 from 'file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/cohort30-terminal-v2-r6.mjs'
import {FIXED,loadCandidateBinding,sha256File,requireContract,frameSizeValid,wireSizeValid,
 validateManifest,validateOwnedReceipt,validateServerEvidence,validateHealthPair,validateRemovalReceipt}
 from 'file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/large-terminal-contract-notes-base-r3.mjs'
import {AccountLedgerClient,NamedPipeLedgerTransport}
 from 'file:///C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/load-v2-r2/../cohort30-terminal-v2/ledger-r5/account-ledger-client-r2.mjs'
import { LoadV2Markers } from 'file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/load-r7-markers.mjs'
import {selectLegalCommandWithAllBottom} from 'file:///C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/cohort30-terminal-v2/placement-selector-r1.mjs'
requireContract(sha256File('C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/cohort30-terminal-v2/placement-selector-r1.mjs')==='94222135D91627B18C1D13067A4A141C6AAA901A090C79980C882B5ED5449AF1','payload_floor_selector_changed')


export function assertR7GeneratedRoot() {
 execFileSync('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
  ['-NoProfile','-Command',". 'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/ops/windows/L12GeneratedStorage.ps1'; $null=Assert-L12GeneratedPath -Path 'D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7' -Purpose 'r7 approved local workload tool'"],
  {windowsHide:true,stdio:'pipe',timeout:15000})
}
requireContract(sha256File('D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-candidate-frame-r6/cohort30-terminal-v2-r6.mjs')==='D0A3E0AA64156E95DC60C40BA197339B5A315316A2E5BDC5FAD3DAA9E76C4531',
 'r6_shared_protocol_source_changed')
let productionCodec=null
let createPresenceAdapter=null
let deployedNetSha=null
export async function initializeR7Protocol(bindingPath,bindingSha,{syntheticValidationOnly=false}={}) {
 assertR7GeneratedRoot()
 loadCandidateBinding(bindingPath,bindingSha,{allowSynthetic:syntheticValidationOnly})
 productionCodec=await initializeCandidateProtocol()
 deployedNetSha=FIXED.deployedNetSha256
 initializePresenceConsumer()
 return productionCodec
}

// This successor remains an R7 workload core: output and ledger-ready paths stay under the
// original governed workload root even though this source file is stored with its acceptance evidence.
const TOOL_ROOT = path.dirname(fileURLToPath('file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/pressure-load-r7-approved-preparation-durable-r5.mjs'))
const PUBLIC_BASE = 'https://legion-12.com/testrun/'
const STAGE_ROOMS = Object.freeze([10, 20, 30])
const REQUIRED_PLAYERS = 69
const MATCH_PLAYERS = 60
const OUTSIDE_USERS = 10
const MAX_IDENTITIES = 70
const MAX_WALL_MS = 900_000
const PRESSURE_DEADLINE_MS = 720_000
const STOP_POLL_MS = 500
const ACTION_INTERVAL_MS = 2_000
const ACTION_CONFIRMATION_MS = 3_000
const DEFAULTS = Object.freeze({
  stageSeconds: 60,
  httpIntervalMs: 2_000,
  wsIntervalMs: 2_000,
  requestTimeoutMs: 10_000,
})

class SafeError extends Error {
  constructor(code, message = code) {
    super(message)
    this.name = 'SafeError'
    this.code = code
  }
}

class StopController {
  constructor() {
    this.controller = new AbortController()
    this.reason = null
  }

  request(reason) {
    if (this.controller.signal.aborted) return
    this.reason = reason
    this.controller.abort(new DOMException(reason, 'AbortError'))
  }
}

const round = value => Math.round(value * 100) / 100

function percentile(values, percent) {
  if (values.length === 0) return null
  const ordered = [...values].sort((left, right) => left - right)
  return round(ordered[Math.max(0, Math.ceil(percent * ordered.length) - 1)])
}

function distribution(values) {
  if (values.length === 0) return { count: 0, p50: null, p95: null, p99: null, max: null }
  return {
    count: values.length,
    p50: percentile(values, 0.50),
    p95: percentile(values, 0.95),
    p99: percentile(values, 0.99),
    max: round(Math.max(...values)),
  }
}

class Metrics {
  constructor() {
    this.http = new Map()
    this.ws = []
    this.pongs = []
    this.commands = []
    this.commandSkips = []
    this.currentStage = 'setup'
    this.httpRows = []
    this.reconstructionErrors = 0
    this.presenceReconstructionErrors = 0
    this.presenceRecoveries = 0
    this.presenceDeltaApplied = 0
    this.presenceNegotiatedClients = 0
    this.outsideUsersByStage = new Map()
    this.cleanupTimings = []
    this.clientFrameTimings = []
    this.eventLoopLagMs = []
    this.burstObserver = new WsBurstObserver()
  }

  recordHttp(name, durationMs, status, error, trace = null) {
    this.httpRows.push({ name, durationMs, status, error, stage: this.currentStage, trace })
    const bucket = this.http.get(name) ?? { durations: [], statuses: new Map(), errors: new Map() }
    bucket.durations.push(durationMs)
    if (status !== null) bucket.statuses.set(String(status), (bucket.statuses.get(String(status)) ?? 0) + 1)
    if (error) bucket.errors.set(error, (bucket.errors.get(error) ?? 0) + 1)
    this.http.set(name, bucket)
  }

  recordWs(type, bytes, logicalJsonBytes=null) {
    this.ws.push({ type, bytes, logicalJsonBytes, stage: this.currentStage })
  }

  recordPong(durationMs) {
    this.pongs.push({ durationMs, stage: this.currentStage })
  }

  recordCommand(type, outcome, durationMs, revisionAdvanced, phaseAdvanced) {
    this.commands.push({
      type, outcome, durationMs, revisionAdvanced, phaseAdvanced, stage: this.currentStage,
    })
  }

  recordCommandSkip(reason) {
    this.commandSkips.push({ reason, stage: this.currentStage })
  }

  httpSummary(skips) {
    const names = new Set([...this.http.keys(), ...Object.keys(skips)])
    return Object.fromEntries([...names].sort().map(name => {
      if (skips[name]) return [name, { skipped: true, reason: skips[name] }]
      const bucket = this.http.get(name) ?? { durations: [], statuses: new Map(), errors: new Map() }
      const statuses = Object.fromEntries([...bucket.statuses.entries()].sort(([a], [b]) => a.localeCompare(b)))
      const errorsByKind = Object.fromEntries([...bucket.errors.entries()].sort(([a], [b]) => a.localeCompare(b)))
      const errors = [...bucket.errors.values()].reduce((sum, count) => sum + count, 0)
        + [...bucket.statuses.entries()].filter(([status]) => !status.startsWith('2'))
          .reduce((sum, [, count]) => sum + count, 0)
      return [name, {
        skipped: false,
        requests: bucket.durations.length,
        successes: bucket.durations.length - errors,
        errors,
        statuses,
        errorsByKind,
        latencyMs: distribution(bucket.durations),
      }]
    }))
  }

  wsSummary(stageDurations) {
    const wanted = ['pong', 'gameState', 'gameStateCardFrameV1', 'gameStateDelta', 'presenceSnapshot', 'presenceDelta', 'roomState', 'roomLeft',
      'roomClosed', 'session', 'recoveryComplete', 'resourceVersions', 'resourceChanged', 'error', 'actionRejected', 'other']
    const messageTypes = Object.fromEntries(wanted.map(type => {
      const rows = this.ws.filter(row => type === 'other' ? !wanted.slice(0,-1).includes(row.type) : row.type === type)
      return [type, { messages: rows.length, totalPayloadBytes:rows.reduce((n,row)=>n+row.bytes,0), payloadBytes: distribution(rows.map(row => row.bytes)) }]
    }))
    const stages = Object.fromEntries(stageDurations.map(stage => {
      const rows = this.ws.filter(row => row.stage === stage.name)
      const full = rows.filter(row => ['gameState','gameStateCardFrameV1'].includes(row.type))
      const delta = rows.filter(row => row.type === 'gameStateDelta')
      const pongs = this.pongs.filter(row => row.stage === stage.name)
      return [stage.name, {
        configuredRooms: stage.rooms,
        actualDurationMs: round(stage.durationMs),
        allMessages: rows.length,
        allPayloadBytes:rows.reduce((n,row)=>n+row.bytes,0),
        allMessagesPerSecond: round(rows.length * 1_000 / Math.max(1, stage.durationMs)),
        pongMessages: rows.filter(row => row.type === 'pong').length,
        pongLatencyMs: distribution(pongs.map(row => row.durationMs)),
        gameState: {
          fullMessages: full.length,
          deltaMessages: delta.length,
          fullPayloadBytes: full.reduce((sum, row) => sum + row.bytes, 0),
          logicalFullJsonBytes: full.reduce((sum,row)=>sum+(row.logicalJsonBytes??0),0),
          deltaPayloadBytes: delta.reduce((sum, row) => sum + row.bytes, 0),
          messagesPerSecond: round((full.length + delta.length) * 1_000 / Math.max(1, stage.durationMs)),
        },
      }]
    }))
    const full = this.ws.filter(row => ['gameState','gameStateCardFrameV1'].includes(row.type))
    const delta = this.ws.filter(row => row.type === 'gameStateDelta')
    return {
      byteBasis: 'utf8-json-payload-excluding-websocket-framing',
      messageTypes,
      cleanupMessageTypes:Object.fromEntries(wanted.map(type=>{
        const rows=this.ws.filter(row=>row.stage==='cleanup' && (type==='other' ? !wanted.slice(0,-1).includes(row.type) : row.type===type))
        return [type,{messages:rows.length,totalPayloadBytes:rows.reduce((n,row)=>n+row.bytes,0)}]
      })),
      pongLatencyMs: distribution(this.pongs.map(row => row.durationMs)),
      gameState: {
        fullMessages: full.length,
        deltaMessages: delta.length,
        fullPayloadBytes: full.reduce((sum, row) => sum + row.bytes, 0),
          logicalFullJsonBytes: full.reduce((sum,row)=>sum+(row.logicalJsonBytes??0),0),
        deltaPayloadBytes: delta.reduce((sum, row) => sum + row.bytes, 0),
      },
      stages,
    }
  }

  commandSummary(stageDurations) {
    const acceptedOutcome = outcome => outcome === 'accepted'
    const summarize = rows => {
      const outcomes = Object.fromEntries(['accepted', 'advanced-without-ack', 'rejected', 'timed-out',
        'cancelled', 'transport-error'].map(outcome =>
        [outcome, rows.filter(row => row.outcome === outcome).length]))
      return {
        attempted: rows.length,
        accepted: rows.filter(row => acceptedOutcome(row.outcome)).length,
        outcomes,
        latencyMs: distribution(rows.map(row => row.durationMs)),
        acceptedLatencyMs: distribution(rows.filter(row => acceptedOutcome(row.outcome))
          .map(row => row.durationMs)),
        revisionAdvances: rows.filter(row => row.revisionAdvanced).length,
        phaseChanges: rows.filter(row => row.phaseAdvanced).length,
      }
    }
    const byType = Object.fromEntries(['resolvePrompt', 'mulligan', 'playCard', 'attack', 'resolveDefense', 'endTurn']
      .map(type => [type, summarize(this.commands.filter(row => row.type === type))]))
    const byStage = Object.fromEntries(stageDurations.map(stage => {
      const rows = this.commands.filter(row => row.stage === stage.name)
      const skips = this.commandSkips.filter(row => row.stage === stage.name)
      return [stage.name, {
        ...summarize(rows),
        skips: Object.fromEntries([...new Set(skips.map(row => row.reason))].sort()
          .map(reason => [reason, skips.filter(row => row.reason === reason).length])),
      }]
    }))
    return {
      cadenceMs: ACTION_INTERVAL_MS,
      authoritativeFullSyncBeforeSelection: false,
      stateSource: 'Exact deployed frontend delta reconstruction; full only on initial/recovery boundary',
      overall: summarize(this.commands),
      byType,
      byStage,
    }
  }
}

function safeFailure(error) {
  if (error instanceof SafeError) return error.code
  if (error?.name === 'TimeoutError') return 'deadline-exceeded'
  if (error?.name === 'AbortError') return 'aborted'
  if (error instanceof SyntaxError) return 'invalid-json'
  return 'unexpected-failure'
}

function combinedSignal(parent, timeoutMs) {
  const timeout = AbortSignal.timeout(Math.max(1, Math.floor(timeoutMs)))
  return parent ? AbortSignal.any([parent, timeout]) : timeout
}

function remainingMs(deadline, cap) {
  return Math.max(1, Math.min(cap, deadline - Date.now()))
}

function delay(ms, signal) {
  if (signal?.aborted) return Promise.reject(signal.reason)
  return new Promise((resolve, reject) => {
    const timer = setTimeout(done, ms)
    const onAbort = () => {
      clearTimeout(timer)
      signal.removeEventListener('abort', onAbort)
      reject(signal.reason)
    }
    function done() {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

function endpoint(baseUrl, relativePath, local) {
  const selectedBase=new URL(baseUrl)
  if(local===true){
    if(selectedBase.protocol!=='http:'||selectedBase.hostname!=='127.0.0.1'
      ||selectedBase.username||selectedBase.password)throw new SafeError('local-host-not-loopback')
  }else if(local!==false||selectedBase.href!==PUBLIC_BASE)throw new SafeError('remote-host-not-allowlisted')
  if (relativePath.startsWith('/') || relativePath.includes('://')) throw new SafeError('invalid-relative-endpoint')
  const url = new URL(relativePath, baseUrl)
  if (!local && (!url.href.startsWith(PUBLIC_BASE) || url.origin !== new URL(PUBLIC_BASE).origin))
    throw new SafeError('remote-host-not-allowlisted')
  return url
}

async function request(baseUrl, relativePath, options = {}) {
  const {
    method = 'GET', token, body, parseJson = false, signal, timeoutMs,
    metrics, metricName, local = false,
  } = options
  const url = endpoint(baseUrl, relativePath, local)
  const started = performance.now()
  let response
  try {
    response = await fetch(url, {
      method,
      redirect: 'error',
      headers: {
        Accept: 'application/json',
        'User-Agent': 'L12-ReadIsolationPressure/1',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      signal: combinedSignal(signal, timeoutMs),
    })
    const headerMs=performance.now()-started
    const appMatch=/\bapp;dur=([0-9]+(?:\.[0-9]+)?)(?:[ ,;]|$)/.exec(response.headers.get('server-timing') ?? '')
    const appMs=appMatch ? Number(appMatch[1]) : null
    const payload = parseJson ? await response.json() : await response.arrayBuffer()
    const totalMs=performance.now()-started
    metrics?.recordHttp(metricName, round(totalMs), response.status, null,
      {headerMs:round(headerMs),bodyReadMs:round(Math.max(0,totalMs-headerMs)),serverAppMs:Number.isFinite(appMs) && appMs>=0 && appMs<=900000 ? appMs : null})
    return { status: response.status, body: payload }
  } catch (error) {
    metrics?.recordHttp(metricName, round(performance.now() - started), null, safeFailure(error))
    throw error
  }
}

function credentialKind(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new SafeError('invalid-credential-entry')
  const token = typeof value.token === 'string' ? value.token.trim() : ''
  const username = typeof value.username === 'string' ? value.username : ''
  const password = typeof value.password === 'string' ? value.password : ''
  if (token && !username && !password) return { kind: 'token', token }
  if (!token && username && password) return { kind: 'login', username, password }
  throw new SafeError('credential-must-contain-token-or-login-pair')
}

async function mapLimit(values, limit, mapper) {
  const output = new Array(values.length)
  let cursor = 0
  let firstError = null
  await Promise.all(Array.from({ length: Math.min(limit, values.length) }, async () => {
    while (true) {
      if (firstError) return
      const index = cursor++
      if (index >= values.length) return
      try { output[index] = await mapper(values[index], index) }
      catch (error) { firstError ??= error }
    }
  }))
  if (firstError) throw firstError
  return output
}

async function authenticateCredential(value, context) {
  const credential = credentialKind(value)
  let token
  if (credential.kind === 'token') token = credential.token
  else {
    const login = await request(context.baseUrl, 'api/auth/login', {
      method: 'POST', body: { username: credential.username, password: credential.password },
      parseJson: true, signal: context.signal, timeoutMs: context.timeoutMs, local: context.local,
    })
    if (login.status !== 200 || typeof login.body?.token !== 'string' || !login.body.token)
      throw new SafeError('credential-login-rejected')
    token = login.body.token
  }
  const identity = { token, accountId: null }
  context.onSession?.(identity)
  const me = await request(context.baseUrl, 'api/auth/me', {
    token, parseJson: true, signal: context.signal, timeoutMs: context.timeoutMs, local: context.local,
  })
  if (me.status !== 200 || typeof me.body?.id !== 'string' || !me.body.id)
    throw new SafeError('credential-auth-me-rejected')
  identity.accountId = me.body.id
  return identity
}


// Reuse the accepted r6 intent/confirmation protocol twice. No core ledger can
// hold more than 60 accounts, and no room receives an identity before durable ACK.
function cohortRequest(context) {
 return async (_base,relative,_deadline,options={})=>{
  if(context.syntheticRequestFn) {
   requireContract(context.local===true,'synthetic_transport_public_forbidden')
   return context.syntheticRequestFn(relative,options)
  }
  const response=await request(context.baseUrl,relative,{
   ...options,parseJson:options.retain!==false,signal:null,
   timeoutMs:remainingMs(context.cleanupDeadline,context.requestTimeoutMs),local:context.local})
  requireContract((options.expected??[200]).includes(response.status),'cohort_bridge_http_status')
  return options.retain===false?undefined:response.body
 }
}
export async function provisionDualLedgerAccounts(context,adminIdentity,authContext,provisioning) {
 requireContract(Array.isArray(context.ledgerClients)&&context.ledgerClients.length===2
  && context.ledgerClients.every(value=>value instanceof AccountLedgerClient)
  && context.ledgerClients[0]!==context.ledgerClients[1]
  && context.ledgerClients[0].runId!==context.ledgerClients[1].runId,'two_independent_ledgers_required')
 provisioning.owned??=[];provisioning.privateValues??=new Set()
 provisioning.ledgerReceipts??=[{},{}]
 const result=[]
 try {
  for(const [index,count] of [60,9].entries()) {
   await context.beforeLedgerGroup?.(index)
   const owned=[]
   try {
    await cohort.registerAccounts(adminIdentity.token,count,{},provisioning.privateValues,owned,{
     ledger:context.ledgerClients[index],ledgerReceipt:provisioning.ledgerReceipts[index],
     requestFn:cohortRequest(context),delayFn:context.local?async()=>{}:delay,
     stopFile:context.stopFile,existsFn:fs.existsSync})
   }finally{
    context.ledgerClients[index].close()
    for(const account of owned) {
     provisioning.owned.push(account);provisioning.plannedNames.add(account.username)
     provisioning.registrationAttempts++
     if(account.id&&account.token) {
      provisioning.confirmed.push({id:account.id,username:account.username,createdAt:account.createdAt,
       preexistingIdAbsent:true,preexistingUsernameAbsent:true})
      authContext.onSession?.({token:account.token,accountId:account.id})
     } else provisioning.uncertainRegistrationResponses++
    }
   }
   for(const account of owned) {
    const identity={token:account.token,accountId:account.id}
    result.push(identity)
   }
  }
  requireContract(result.length===69&&new Set(result.map(value=>value.accountId)).size===69,
   'dual_ledger_identity_budget_invalid')
  return result
 }finally{for(const ledger of context.ledgerClients)ledger.close()}
}
async function registerSyntheticPlayers(context,adminIdentity,authContext,provisioning) {
 return provisionDualLedgerAccounts(context,adminIdentity,authContext,provisioning)
}
export async function cleanupDualLedgerAccounts(context,adminIdentity,provisioning) {
 return cohort.cleanup({rooms:[],accounts:provisioning.owned??[],adminToken:adminIdentity?.token,
  deadline:{remainingOrZero:()=>Math.max(0,context.cleanupDeadline-Date.now())},
  stopFile:context.stopFile,privateValues:provisioning.privateValues??new Set()},
  {requestFn:cohortRequest(context),delayFn:context.local?async()=>{}:delay,existsFn:fs.existsSync})
}

function initializePresenceConsumer() {
// This adapter executes the exact published19 consumer predicates/reducer and
// presence dispatch branches. It does not implement a second delta algorithm.
const source=fs.readFileSync(FIXED.netSourcePath,'utf8').replaceAll('\r\n','\n').replaceAll('\r','\n')
const digest=crypto.createHash('sha256').update(source).digest('hex').toUpperCase()
if(sha256File(FIXED.netSourcePath)!==FIXED.deployedNetSha256)throw new Error('CURRENT_DEPLOYED_NET_DRIFT')
const uniqueOffset=(anchor,label)=>{
 const first=source.indexOf(anchor)
 if(first<0||source.indexOf(anchor,first+anchor.length)>=0)throw new Error(`EXACT_${label}_ANCHOR_REQUIRED`)
 return first
}
const functionsStart=uniqueOffset('function acceptResourceRevision(','PRESENCE_FUNCTION_START')
const functionsEnd=uniqueOffset('function cancelResourceFallback()','PRESENCE_FUNCTION_END')
const dispatchStart=uniqueOffset("else if (message.type === 'effectiveOperationsPolicy')",'PRESENCE_DISPATCH_START')
const dispatchEnd=uniqueOffset("else if (message.type === 'operationsBlocked')",'PRESENCE_DISPATCH_END')
const resourceNameMatches=source.match(/^const resourceNames = .*$/gm)??[]
if(functionsStart>=functionsEnd||dispatchStart>=dispatchEnd||resourceNameMatches.length!==1)
 throw new Error('EXACT_PRESENCE_CONSUMER_BOUNDARY_ORDER_REQUIRED')
const functions=source.slice(functionsStart,functionsEnd)
const dispatch=source.slice(dispatchStart,dispatchEnd)
const resourceNames=resourceNameMatches[0]
const exactOnce=(text,anchor)=>text.split(anchor).length===2
if(!functions.startsWith('function acceptResourceRevision(')||!functions.endsWith('}\n\n')
 ||!['function requestPresenceRecovery(','function validPresenceItem(','function validPresenceItems(',
   'function validPresenceIdentity(','function rebuildPresenceDelta('].every(anchor=>exactOnce(functions,anchor))
 ||!["message.type === 'resourceVersions'","message.type === 'resourceChanged'",
   "message.type === 'presenceSnapshot'","message.type === 'presenceDelta'"].every(anchor=>exactOnce(dispatch,anchor)))
 throw new Error('EXACT_PRESENCE_CONSUMER_BOUNDARY_MISSING')
const ts=createRequire(import.meta.url)('D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/opcgpro-vue/node_modules/typescript/lib/typescript.js')
const body=`
const WebSocket={OPEN:1};
const l12State={socket,presence:[],resourceEpoch:'',resourceRevisions:{},operationsPolicy:null,connectionIssue:'none'};
${resourceNames}
let negotiatedPresenceDelta=false,presenceRecoveryPending=false,presenceBaselineEpoch='';
let invalidCalls=0,lastApplied=false;
function dispatchResourceChange(resource,detail){if(resource==='presence')lastApplied=true;}
${functions}
const originalRecovery=requestPresenceRecovery;
requestPresenceRecovery=function(socket){invalidCalls++;originalRecovery(socket);};
return {
 handle(message){
  lastApplied=false;const failuresBefore=invalidCalls,pendingBefore=presenceRecoveryPending;
  if(message.type==='session'){
   negotiatedPresenceDelta=message.capabilities?.presenceDeltaV1===true;
   presenceRecoveryPending=false;presenceBaselineEpoch='';
  }
  ${dispatch}
  return {invalid:invalidCalls>failuresBefore,recoveryRequested:!pendingBefore&&presenceRecoveryPending,applied:lastApplied};
 },
 reset(){negotiatedPresenceDelta=false;presenceRecoveryPending=false;presenceBaselineEpoch='';l12State.presence=[];l12State.resourceEpoch='';l12State.resourceRevisions={};},
 inspect(){return {items:l12State.presence,epoch:l12State.resourceEpoch,revision:l12State.resourceRevisions.presence||0,pending:presenceRecoveryPending,negotiated:negotiatedPresenceDelta};}
};`
const compiled=ts.transpileModule(body,{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText
const factory=new Function('socket',compiled)
createPresenceAdapter=socket=>factory(socket)
}

class WsClient {
  constructor(token, metrics, timeoutMs, runSignal, actionMarkers) {
    this.token = token
    this.metrics = metrics
    this.timeoutMs = timeoutMs
    this.runSignal = runSignal
    this.socket = null
    this.waiters = new Set()
    this.closed = false
    this.playerIndex = null
    this.envelope = null
    this.deltaInvalid = false
    this.cleanupTraces = new Map()
    this.numericClientSlot = null
    this.numericRoomSlot = null
    this.negotiatedEventWindowDelta = false
    this.presenceConsumer = null
    this.actionMarkers = actionMarkers
    requireContract(productionCodec,'candidate_protocol_not_initialized')
    this.cardFrameRecovery=new productionCodec.GameStateCardFrameRecovery()
    this.negotiatedCardFrame=false
    this.cardFrameDecodeFailures=0;this.packedFrames=0;this.plainFrames=0
    this.plainRecoveryRequests=0;this.terminalFrame=null;this.terminalTrace=null
    this.roomPeer=null
  }

  async connect(url) {
    if (typeof WebSocket !== 'function') throw new SafeError('native-websocket-unavailable-node-22-required')
    const socket = new WebSocket(url)
    this.socket = socket
    this.presenceConsumer = createPresenceAdapter(socket)
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => finish(new SafeError('websocket-open-timeout')), this.timeoutMs)
      const abort = () => finish(this.runSignal.reason ?? new SafeError('websocket-open-aborted'))
      const finish = error => {
        clearTimeout(timeout)
        this.runSignal.removeEventListener('abort', abort)
        socket.removeEventListener('open', opened)
        socket.removeEventListener('error', failed)
        if (error) reject(error); else resolve()
      }
      const opened = () => finish()
      const failed = () => finish(new SafeError('websocket-open-failed'))
      this.runSignal.addEventListener('abort', abort, { once: true })
      socket.addEventListener('open', opened, { once: true })
      socket.addEventListener('error', failed, { once: true })
    })
    socket.addEventListener('message', event => this.onMessage(event))
    socket.addEventListener('close', () => this.onClose())
    socket.addEventListener('error', () => this.onClose())

    const session = this.waitFor(message => message.type === 'session', 'session')
    const recovery = this.waitFor(message => message.type === 'recoveryComplete', 'recovery')
    this.send({
      type: 'hello', authToken: this.token,
      capabilities: { requestIds: true, deltaGameState: true, eventWindowDeltaV1: true, presenceDeltaV1: true, gameStateCardFrameV1: true },
    })
    const [sessionMessage] = await Promise.all([session, recovery])
    if (sessionMessage.capabilities?.gameStateCardFrameV1 !== true) throw new SafeError('card-frame-capability-not-negotiated')
    if (sessionMessage.capabilities?.deltaGameState !== true)
      throw new SafeError('delta-capability-not-negotiated')
    if (sessionMessage.capabilities?.eventWindowDeltaV1 !== true || sessionMessage.capabilities?.presenceDeltaV1 !== true)
      throw new SafeError('published19-capabilities-not-negotiated')
  }

  onMessage(event) {
    const arrivedAt = performance.now()
    const raw = typeof event.data === 'string' ? event.data : String(event.data)
    const parseStartedAt = performance.now()
    let message
    try { message = JSON.parse(raw) } catch {
      this.metrics.recordWs('invalidJson', Buffer.byteLength(raw))
      if(this.metrics.currentStage==='cleanup')this.metrics.burstObserver.observeFrame(this.numericClientSlot,'other',Buffer.byteLength(raw),false,arrivedAt)
      return
    }
    const parsedAt = performance.now()
    const wireType=typeof message?.type==='string'?message.type:'unknown'
    const wireBytes=Buffer.byteLength(raw)
    const wireWasPlain=wireType==='gameState'
    let restoreMs=0
    if(wireType==='session')this.negotiatedCardFrame=message.capabilities?.gameStateCardFrameV1===true
    if(wireType==='gameStateCardFrameV1') {
     const restoreStarted=performance.now()
     const restored=this.negotiatedCardFrame&&!this.cardFrameRecovery.disabled
      ?productionCodec.restoreGameStateCardFrame(message,raw):null
     restoreMs=performance.now()-restoreStarted
     if(!restored) {
      this.cardFrameDecodeFailures++;this.metrics.reconstructionErrors++;this.deltaInvalid=true
      this.metrics.recordWs(wireType,wireBytes,null)
      if(this.cardFrameRecovery.begin(this.terminalTrace?.requestId??'')) {
       this.negotiatedCardFrame=false;this.plainRecoveryRequests++
       try{this.send({type:'syncState',plainFull:true})}catch{}
      }
      this.metrics.requestStop?.('card-frame-reconstruction-failed')
      return
     }
     message=restored;this.packedFrames++
    }else if(wireWasPlain)this.plainFrames++
    const type=message?.type
    const logicalJsonBytes=type==='gameState'?Buffer.byteLength(JSON.stringify(message)):null
    const actionMarker = this.actionMarkers.observeParsed(this, {
      message, callbackAt: arrivedAt, parseStartedAt, parsedAt, bytes: Buffer.byteLength(raw),
    })
    this.metrics.recordWs(wireType, wireBytes, logicalJsonBytes)
    if (type === 'session') {
      this.negotiatedEventWindowDelta = message.capabilities?.deltaGameState === true && message.capabilities?.eventWindowDeltaV1 === true
      if (message.capabilities?.presenceDeltaV1 === true) this.metrics.presenceNegotiatedClients++
    }
    const presenceResult = this.presenceConsumer?.handle(message)
    if (presenceResult?.invalid) { this.metrics.presenceReconstructionErrors++; this.metrics.reconstructionErrors++ }
    if (presenceResult?.recoveryRequested) this.metrics.presenceRecoveries++
    if (type === 'presenceDelta' && presenceResult?.applied) this.metrics.presenceDeltaApplied++
    if (type === 'gameState') { this.envelope = message; this.deltaInvalid = false }
    if (type === 'gameStateDelta') {
      const rebuilt = reduceEnvelope(this.envelope, message, this.negotiatedEventWindowDelta)
      if (rebuilt) this.envelope = rebuilt
      else { this.deltaInvalid = true; this.metrics.reconstructionErrors++ }
    }
    for (const waiter of [...this.waiters]) {
      if (!waiter.predicate(message)) continue
      waiter.finish(null, message)
    }
    if(wireWasPlain&&this.cardFrameRecovery.active)this.cardFrameRecovery.recordPublishedPlainFrame(message)
    if(wireType==='recoveryComplete'&&this.cardFrameRecovery.active&&!this.cardFrameRecovery.finish().ready)
     this.metrics.reconstructionErrors++
    const dispatchedAt = performance.now()
    if(this.terminalTrace&&!this.terminalFrame&&type==='gameState'
     &&message.state?.phase==='GameOver'&&message.state?.matchId===this.terminalTrace.matchId) {
     const elapsed=arrivedAt-this.terminalTrace.sentAt
     this.terminalFrame={numericClientSlot:this.numericClientSlot,numericRoomSlot:this.numericRoomSlot,receiver:this.numericClientSlot%2,type:'gameState',terminal:true,delta:false,
      wireType,wireBytes,logicalJsonBytes,restoreMs,sendToCallbackMs:elapsed,
      parseMs:parsedAt-parseStartedAt,dispatchMs:dispatchedAt-parsedAt,exceededOriginal3s:Number(elapsed>3000)}
    }
    if (actionMarker) this.actionMarkers.afterDispatch(actionMarker, dispatchedAt)
    if (this.metrics.currentStage === 'cleanup' && this.metrics.clientFrameTimings.length < 4096)
      this.metrics.clientFrameTimings.push({type:['gameState','gameStateDelta','presenceSnapshot','presenceDelta','roomLeft','roomState','roomClosed','pong','actionRejected','error'].includes(type)?type:'other',bytes:wireBytes,wireType,logicalJsonBytes,restoreMs,parseMs:parsedAt-parseStartedAt,rebuildAndDispatchMs:dispatchedAt-parsedAt})
    const burstSequence=this.metrics.currentStage==='cleanup'
      ?this.metrics.burstObserver.observeFrame(this.numericClientSlot,wireType,wireBytes,type==='gameState' && message.state?.phase==='GameOver',arrivedAt):null
    const traceKey = type === 'roomLeft' ? 'leaveRoom' : message.requestId
    const trace = this.cleanupTraces.get(traceKey)
    if (trace && ['gameState','actionRejected','error','roomLeft'].includes(type)) {
      this.cleanupTraces.delete(traceKey)
      this.metrics.cleanupTimings.push({operation:trace.operation,responseType:type,
        ...(Number.isInteger(this.numericRoomSlot) ? {numericRoomSlot:this.numericRoomSlot,numericClientSlot:this.numericClientSlot} : {}),
        sendToRawArrivalMs:arrivedAt-trace.sentAt,parseMs:parsedAt-arrivedAt,
        dispatchMs:dispatchedAt-parsedAt,exceededOriginal3s:arrivedAt-trace.sentAt>3000,
        terminalGameOver:type==='gameState' && message.state?.phase==='GameOver'})
      if(trace.operation==='surrender')this.metrics.burstObserver.observeResponse(this.numericRoomSlot,this.numericClientSlot,burstSequence,arrivedAt,type==='gameState' && message.state?.phase==='GameOver')
    }
    // Arrival and original ack/dispatch timestamps above precede all optional hashing.
    if(this.metrics.currentStage==='cleanup' && type==='presenceSnapshot')this.metrics.burstObserver.observePresence(this.numericClientSlot,raw)
  }

  onClose() {
    if (this.closed) return
    this.closed = true
    this.presenceConsumer?.reset()
    this.negotiatedEventWindowDelta = false
    for (const waiter of [...this.waiters]) waiter.finish(new SafeError('websocket-closed'))
  }

  waitFor(predicate, label, timeoutMs = this.timeoutMs, ignoreRunAbort = false, overrideSignal = null) {
    if (!this.socket || this.closed) return Promise.reject(new SafeError('websocket-not-open'))
    return new Promise((resolve, reject) => {
      const signal = ignoreRunAbort ? null : (overrideSignal ?? this.runSignal)
      const timer = setTimeout(() => finish(new SafeError(`websocket-${label}-timeout`)), timeoutMs)
      const abort = () => finish(signal.reason ?? new SafeError(`websocket-${label}-aborted`))
      const finish = (error, value) => {
        clearTimeout(timer)
        signal?.removeEventListener('abort', abort)
        this.waiters.delete(waiter)
        if (error) reject(error); else resolve(value)
      }
      const waiter = { predicate, finish }
      this.waiters.add(waiter)
      if(signal?.aborted){abort();return}
      signal?.addEventListener('abort', abort, { once: true })
    })
  }

  send(payload) {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) throw new SafeError('websocket-not-open')
    if (this.metrics.currentStage === 'cleanup' && (payload.type === 'leaveRoom' || payload.command?.type === 'surrender')) {
      const key=payload.type === 'leaveRoom' ? 'leaveRoom' : payload.requestId
      const sentAt=performance.now()
      this.cleanupTraces.set(key,{sentAt,operation:payload.type==='leaveRoom'?'leaveRoom':'surrender'})
      if(payload.command?.type==='surrender') {
       for(const peer of [this,this.roomPeer].filter(Boolean))if(peer.terminalTrace)peer.terminalTrace.sentAt=sentAt
       this.metrics.burstObserver.observeSend(this.numericRoomSlot,this.numericClientSlot,sentAt)
      }
    }
    this.socket.send(JSON.stringify(payload))
  }

  close() {
    this.closed = true
    for (const waiter of [...this.waiters]) waiter.finish(new SafeError('websocket-closed'))
    if (this.socket && this.socket.readyState < WebSocket.CLOSING) this.socket.close(1000, 'pressure-complete')
  }
}

async function checkCommit(context, signal) {
  const health = await request(context.baseUrl, 'health', {
    parseJson: true, signal, timeoutMs: context.requestTimeoutMs, local: context.local,
  })
  if (health.status !== 200 || health.body?.serverVersion !== context.expectedCommit)
    throw new SafeError('expected-commit-mismatch')
  return health.body.serverVersion
}

async function prepareWorkloads(context, identities, metrics) {
  const skips = {}
  const workloads = [{ name: 'authMe', path: 'api/auth/me', token: () => identities.players[0].token }]

  if (!identities.admin) {
    skips.cardAnalytics = 'missing-admin-credential'
    skips.audit = 'missing-admin-credential'
  } else {
    for (const [name, endpointPath] of [['cardAnalytics', 'api/admin/analytics/cards'], ['audit', 'api/admin/v1/audit?limit=50']]) {
      const probe = await request(context.baseUrl, endpointPath, {
        token: identities.admin.token, signal: context.signal, timeoutMs: context.requestTimeoutMs,
        local: context.local,
      })
      if (probe.status === 200) workloads.push({ name, path: endpointPath, token: () => identities.admin.token })
      else if (probe.status === 401 || probe.status === 403) skips[name] = 'permission-denied'
      else throw new SafeError(`${name}-preflight-failed`)
    }
  }

  const reader = identities.tournamentReader ?? identities.admin ?? identities.players[0]
  const list = await request(context.baseUrl, 'api/tournaments', {
    token: reader.token, parseJson: true, signal: context.signal,
    timeoutMs: context.requestTimeoutMs, local: context.local,
  })
  if (list.status === 401 || list.status === 403) skips.tournamentDetail = 'permission-denied'
  else if (list.status !== 200) throw new SafeError('tournament-list-preflight-failed')
  else {
    const items = Array.isArray(list.body?.items) ? list.body.items : []
    const tournamentId = items.find(item => typeof item?.id === 'string' && item.id)?.id
    if (!tournamentId) skips.tournamentDetail = 'no-visible-tournament-data'
    else {
      const detailPath = `api/tournaments/${encodeURIComponent(tournamentId)}`
      const probe = await request(context.baseUrl, detailPath, {
        token: reader.token, signal: context.signal, timeoutMs: context.requestTimeoutMs,
        local: context.local,
      })
      if (probe.status === 200)
        workloads.push({ name: 'tournamentDetail', path: detailPath, token: () => reader.token })
      else if ([401, 403, 404].includes(probe.status)) skips.tournamentDetail = 'data-not-readable'
      else throw new SafeError('tournament-detail-preflight-failed')
    }
  }
  return { workloads, skips }
}

async function createRoomPair(context, hostIdentity, guestIdentity, metrics, roomRegistry) {
  const wsUrl = endpoint(context.baseUrl, 'ws', context.local)
  wsUrl.protocol = wsUrl.protocol === 'https:' ? 'wss:' : 'ws:'
  const host = new WsClient(hostIdentity.token, metrics, context.requestTimeoutMs, context.signal,
    context.actionMarkers)
  const guest = new WsClient(guestIdentity.token, metrics, context.requestTimeoutMs, context.signal,
    context.actionMarkers)
  const numericRoomSlot=roomRegistry.length
  host.numericRoomSlot=guest.numericRoomSlot=numericRoomSlot
  host.numericClientSlot=numericRoomSlot*2;guest.numericClientSlot=numericRoomSlot*2+1
  const tracked = {
    host, guest, started: false, ended: false, left: false,
    lastCommandAt: 0, attemptedRevisionKeys: new Set(), playedTurns: new Set(), acceptedTypes: new Set(),
  }
  roomRegistry.push(tracked)
  await Promise.all([host.connect(wsUrl), guest.connect(wsUrl)])

  const created = host.waitFor(message => message.type === 'roomState' && typeof message.roomCode === 'string', 'room-create')
  host.send({ type: 'createRoom' })
  const createdState = await created
  const roomCode = createdState.roomCode
  const joined = guest.waitFor(message => message.type === 'roomState', 'room-join')
  guest.send({ type: 'joinRoom', roomCode })
  const joinedState = await joined
  if (!Number.isInteger(createdState.yourPlayerIndex) || !Number.isInteger(joinedState.yourPlayerIndex)
      || createdState.yourPlayerIndex === joinedState.yourPlayerIndex)
    throw new SafeError('room-player-index-invalid')
  host.playerIndex = createdState.yourPlayerIndex
  guest.playerIndex = joinedState.yourPlayerIndex

  for(const [peer,deckIndex] of [[host,0],[guest,1]]) {
    const selected=peer.waitFor(message=>message.type==='roomState'||message.type==='deckRejected'||message.type==='error','preset-selection')
    peer.send({type:'selectDeck',deckIndex})
    if((await selected).type!=='roomState')throw new SafeError('payload-floor-preset-selection-rejected')
  }
  const hostGame = host.waitFor(message => message.type === 'gameState', 'game-start')
  const guestGame = guest.waitFor(message => message.type === 'gameState', 'game-start')
  host.send({ type: 'ready', ready: true })
  guest.send({ type: 'ready', ready: true })
  const started=await Promise.all([hostGame, guestGame])
  tracked.expectedMatchId=started[0].state?.matchId
  tracked.expectedRoomCode=roomCode
  if(typeof tracked.expectedMatchId!=='string'||!tracked.expectedMatchId
    ||started.some((message,index)=>message.state?.matchId!==tracked.expectedMatchId
      ||message.state?.roomCode!==roomCode||!Number.isSafeInteger(message.state?.revision)
      ||message.state?.revision!==started[0].state?.revision
      ||message.state?.you!==[host,guest][index].playerIndex))throw new SafeError('payload-floor-started-match-unbound')
  tracked.payloadFloorEvidence={exactAckMatches:0,peerConvergenceMatches:0,presetIndexes:[0,1]}
  tracked.started = true
  return tracked
}

async function pingWorker(client, until, context, metrics) {
  while (!context.signal.aborted && Date.now() < until) {
    const started = performance.now()
    try {
      const pong = client.waitFor(message => message.type === 'pong', 'pong', context.requestTimeoutMs, false, context.signal)
      client.send({ type: 'ping' })
      await pong
      metrics.recordPong(round(performance.now() - started))
    } catch (error) {
      if (context.signal.aborted) return
      throw error
    }
    await delay(Math.min(context.wsIntervalMs, Math.max(1, until - Date.now())), context.signal).catch(() => {})
  }
}

function exactDeltaValue(message, pathSegments) {
  if (!Array.isArray(message?.changes)) return undefined
  const change = message.changes.find(item => !item?.remove && Array.isArray(item.path)
    && item.path.length === pathSegments.length
    && item.path.every((segment, index) => segment === pathSegments[index]))
  return change?.value
}

function confirmationForRequest(message, requestId) {
  if (message?.type === 'gameState' && message.requestId === requestId) {
    return {
      accepted: true,
      revision: Number.isInteger(message.state?.revision) ? message.state.revision : null,
      phase: typeof message.state?.phase === 'string' ? message.state.phase : null,
    }
  }
  if (message?.type === 'gameStateDelta'
      && exactDeltaValue(message, ['requestId']) === requestId) {
    return {
      accepted: true,
      revision: Number.isInteger(message.revision) ? message.revision : null,
      phase: typeof exactDeltaValue(message, ['state', 'phase']) === 'string'
        ? exactDeltaValue(message, ['state', 'phase']) : null,
    }
  }
  if (message?.requestId === requestId) return { accepted: false, revision: null, phase: null }
  return null
}

async function authoritativeFullSnapshot(client, until, context) {
  if (Date.now() >= until) throw new SafeError('action-stage-ending')
  if (client.deltaInvalid) throw new SafeError('deployed-delta-reconstruction-failed')
  const snapshot = client.envelope?.state
  if (!snapshot || !Number.isInteger(snapshot.you) || snapshot.you !== client.playerIndex
      || !Number.isInteger(snapshot.revision) || !Array.isArray(snapshot.players))
    throw new SafeError('authoritative-push-snapshot-unavailable')
  return structuredClone(snapshot)
}

function commandFromSnapshot(client, snapshot, room) {
  const selected=selectLegalCommandWithAllBottom(client,snapshot,room,isLegionBlockedByVisibleDisaster)
  if(selected?.skip==='unsupported_authoritative_prompt')throw new SafeError('unsupported-authoritative-prompt')
  return selected
}

async function sendAuthoritativeCommand(room, client, snapshot, selected, until, context, metrics) {
  if (room.attemptedRevisionKeys.has(selected.revisionKey)) {
    metrics.recordCommandSkip('duplicate-revision')
    return
  }
  if (Date.now() - room.lastCommandAt < ACTION_INTERVAL_MS) {
    metrics.recordCommandSkip('cadence-limited')
    return
  }
  const available = until - Date.now()
  if (available < 250 || context.signal.aborted) {
    metrics.recordCommandSkip('stage-ending')
    return
  }

  let requestId = crypto.randomUUID()
  const timeout = Math.max(1, Math.min(ACTION_CONFIRMATION_MS, context.requestTimeoutMs, available))
  const started = performance.now()
  const confirmationDeadline=Date.now()+timeout
  let outcome = 'transport-error'
  let confirmedRevision = null
  let confirmedPhase = null
  room.attemptedRevisionKeys.add(selected.revisionKey)
  if (selected.turnKey) room.playedTurns.add(selected.turnKey)
  room.lastCommandAt = Date.now()
  const confirmationStop = new AbortController()
  const confirmationSignal = AbortSignal.any([context.signal,confirmationStop.signal].filter(Boolean))
  let confirmation = null
  let convergenceTasks = []
  try {
    confirmation = client.waitFor(
      message => confirmationForRequest(message, requestId) !== null,
      'command-confirmation', timeout, false, confirmationSignal)
    client.send({ type: 'gameAction', command: selected.command, requestId })
    const message = await confirmation
    const result = confirmationForRequest(message, requestId)
    if(!result.accepted)outcome='rejected'
    else {
      if(!Number.isInteger(result.revision)||result.revision<=snapshot.revision)throw new SafeError('payload-floor-ack-revision-unbound')
      const sameMatch=peer=>peer.envelope?.state?.matchId===room.expectedMatchId
        &&peer.envelope?.state?.roomCode===room.expectedRoomCode
        &&peer.envelope?.state?.you===peer.playerIndex
      const reached=peer=>sameMatch(peer)&&peer.envelope.state.revision>=result.revision
      convergenceTasks=[room.host,room.guest].map(async peer=>{
        if(reached(peer))return
        await peer.waitFor(message=>['gameState','gameStateDelta'].includes(message.type)&&reached(peer),
          'peer-convergence',remainingMs(confirmationDeadline,timeout),false,confirmationSignal)
      })
      await Promise.all(convergenceTasks)
      if(Date.now()>confirmationDeadline||[room.host,room.guest].some(peer=>!sameMatch(peer)
        ||peer.envelope.state.revision!==result.revision))throw new SafeError('payload-floor-peer-revision-unbound')
      room.payloadFloorEvidence.exactAckMatches++
      room.payloadFloorEvidence.peerConvergenceMatches++
      if (context.measuredCommand !== false) room.acceptedTypes.add(selected.command.type)
      outcome='accepted'
    }
    confirmedRevision = result.revision
    confirmedPhase = result.phase
  } catch (error) {
    const stageEnded = context.signal.aborted || Date.now() >= until
    if (stageEnded) outcome = 'cancelled'
    else if (error instanceof SafeError && error.code === 'websocket-command-confirmation-timeout') {
      outcome = 'timed-out'
      try {
        const reconciled = await authoritativeFullSnapshot(client, until, context)
        if (reconciled.revision > snapshot.revision) {
          // A timer or the other player may advance revision; that is not this request's acknowledgement.
          outcome = 'advanced-without-ack'
          confirmedRevision = reconciled.revision
          confirmedPhase = reconciled.phase
        }
      } catch {
        // The timeout remains explicit; do not retry an outcome-unknown command.
      }
    }
  }
  finally {
    confirmationStop.abort(new SafeError('command-waits-stopped'))
    await Promise.allSettled([...(confirmation ? [confirmation] : []),...convergenceTasks])
  }
  metrics.recordCommand(selected.command.type, outcome, round(performance.now() - started),
    Number.isInteger(confirmedRevision) && confirmedRevision > snapshot.revision,
    typeof confirmedPhase === 'string' && confirmedPhase !== snapshot.phase)
}

async function actionWorker(room, until, context, metrics) {
  while (!context.signal.aborted && Date.now() < until) {
    let selected = null
    let lastSkip = 'no-authoritative-command'
    for (const client of [room.host, room.guest]) {
      try {
        const snapshot = await authoritativeFullSnapshot(client, until, context)
        const candidate = commandFromSnapshot(client, snapshot, room)
        if (candidate.command) {
          selected = { client, snapshot, ...candidate }
          break
        }
        if (candidate.skip === 'unsupported-authoritative-prompt'
            || lastSkip !== 'unsupported-authoritative-prompt') lastSkip = candidate.skip
      } catch (error) {
        if(error instanceof SafeError&&error.code==='unsupported-authoritative-prompt')throw error
        if (context.signal.aborted || Date.now() >= until) {
          lastSkip = 'stage-ending'
          break
        }
        if (lastSkip !== 'unsupported-authoritative-prompt') lastSkip = safeFailure(error)
      }
    }
    if (selected)
      await sendAuthoritativeCommand(room, selected.client, selected.snapshot, selected, until, context, metrics)
    else metrics.recordCommandSkip(lastSkip)
    await delay(Math.min(ACTION_INTERVAL_MS, Math.max(1, until - Date.now())), context.signal).catch(() => {})
  }
}

const ordinaryReads = [
  ['authMe','api/auth/me'], ['deckSummaries','api/decks/summaries'],
  ['rankedProfile','api/ranked/me'], ['rankings','api/rankings?range=7d'],
  ['tournamentSummaries','api/tournaments/summaries'],
  ['publicDecks','api/public-decks'], ['matchHistory','api/matches?limit=10'],
]
async function httpWorker(lane, until, context, workloads, identities, metrics) {
  const person = lane === 0 ? identities.admin : identities.players[MATCH_PLAYERS + lane - 1]
  let iteration = 0
  while (!context.signal.aborted && Date.now() < until) {
    const items = lane === 0 ? workloads.filter(w => ['cardAnalytics','audit','tournamentDetail'].includes(w.name))
      : ordinaryReads.map(([name,path]) => ({name,path}))
    const workload = items[iteration++ % items.length]
    if (!metrics.outsideUsersByStage.has(metrics.currentStage)) metrics.outsideUsersByStage.set(metrics.currentStage,new Set())
    metrics.outsideUsersByStage.get(metrics.currentStage).add(person.accountId)
    try {
      await request(context.baseUrl, workload.path, {token:person.token,signal:context.signal,
        timeoutMs:context.requestTimeoutMs,metrics,metricName:workload.name,local:context.local})
    } catch {}
    await delay(Math.min(context.httpIntervalMs, Math.max(1,until-Date.now())),context.signal).catch(()=>{})
  }
}

async function runStage(roomCount, rooms, identities, context, workloads, metrics) {
  const players = identities.players
  const name = `rooms-${roomCount}`
  metrics.currentStage = name
  const started = performance.now()
  const startedUtcMs = Date.now()
  const until = Math.min(Date.now() + context.stageSeconds * 1_000, context.pressureDeadline)
  const activeRooms = rooms.slice(0, roomCount)
  for (const room of activeRooms) room.acceptedTypes.clear()
  const activePlayers = players.slice(0, roomCount * 2)
  const activeSamples = []
  const sampleActive = () => activeSamples.push(activeRooms.filter(r => r.host.envelope?.state?.phase !== 'GameOver').length)
  sampleActive()
  const activeTimer = setInterval(sampleActive,1000)
  const stageStop=new AbortController()
  const workerContext={...context,signal:AbortSignal.any([context.signal,stageStop.signal])}
  context.payloadFloorWorkersFrozen=false
  const workers = [
    ...activePlayers.map((_, index) => pingWorker(activeRooms[Math.floor(index / 2)][index % 2 === 0 ? 'host' : 'guest'], until, workerContext, metrics)),
    ...activeRooms.map(room => actionWorker(room, until, workerContext, metrics)),
    ...Array.from({ length: OUTSIDE_USERS }, (_, lane) => httpWorker(lane, until, workerContext, workloads, identities, metrics)),
  ]
  try { await Promise.all(workers) } finally {
    stageStop.abort(new SafeError('payload-floor-stage-workers-stopped'))
    await Promise.allSettled(workers)
    context.payloadFloorWorkersFrozen=true
    clearInterval(activeTimer);sampleActive()
  }
  return {name,rooms:roomCount,durationMs:performance.now()-started,startedUtcMs,endedUtcMs:Date.now(),
    minimumActiveRooms:Math.min(...activeSamples),maximumActiveRooms:Math.max(...activeSamples),
    endActiveRooms:activeSamples.at(-1),outsideUsers:OUTSIDE_USERS,
    measuredDistinctOutsideUsers:metrics.outsideUsersByStage.get(name)?.size ?? 0,
    roomsWithAcceptedPlay:activeRooms.filter(r=>r.acceptedTypes.has('playCard')).length,
    roomsWithAcceptedAttack:activeRooms.filter(r=>r.acceptedTypes.has('attack')).length}
}

export function inspectPayloadFloor(rooms,{workersFrozen,expectedRooms=30}={}) {
  const issues=[]
  const result={schema:1,status:'rejected',workersFrozen:workersFrozen===true,expectedRooms,
    observedRooms:Array.isArray(rooms)?rooms.length:0,minimumLogicalBytes:180*1024,maximumLogicalBytes:300*1024,
    originalTerminalMinimumBytes:169*1024,finalAccepted:false,rows:[],issues}
  if(workersFrozen!==true)issues.push('workers-not-frozen')
  if(!Array.isArray(rooms)||rooms.length!==expectedRooms||!Number.isInteger(expectedRooms)||expectedRooms<1||expectedRooms>30)
    issues.push('room-count-mismatch')
  for(const [slot,room] of (Array.isArray(rooms)?rooms:[]).entries()){
    const row={numericRoomSlot:slot,receivers:[],sameMatch:false,sameRoom:false,sameRevision:false,
      exactAckMatches:room?.payloadFloorEvidence?.exactAckMatches??0,
      peerConvergenceMatches:room?.payloadFloorEvidence?.peerConvergenceMatches??0}
    if(!Number.isSafeInteger(row.exactAckMatches)||row.exactAckMatches<1
      ||!Number.isSafeInteger(row.peerConvergenceMatches)||row.peerConvergenceMatches<1
      ||row.exactAckMatches!==row.peerConvergenceMatches)issues.push('command-ack-evidence-incomplete')
    try{
      const peers=[room.host,room.guest],states=peers.map(peer=>peer.envelope?.state)
      row.sameMatch=typeof room.expectedMatchId==='string'&&room.expectedMatchId.length>0
        &&states.every(state=>state?.matchId===room.expectedMatchId)
      row.sameRoom=typeof room.expectedRoomCode==='string'&&room.expectedRoomCode.length>0
        &&states.every(state=>state?.roomCode===room.expectedRoomCode)
      row.sameRevision=Number.isSafeInteger(states[0]?.revision)&&states[0].revision>=0
        &&states.every(state=>state?.revision===states[0].revision)
      if(!row.sameMatch)issues.push('projection-match-unbound')
      if(!row.sameRoom)issues.push('projection-room-unbound')
      if(!row.sameRevision)issues.push('projection-revision-diverged')
      if(new Set(peers.map(peer=>peer.playerIndex)).size!==2
        ||peers.some(peer=>![0,1].includes(peer.playerIndex)))issues.push('player-seat-binding-invalid')
      peers.forEach((peer,receiver)=>{
        const state=states[receiver]
        if(peer.deltaInvalid||peer.envelope?.type!=='gameState'||state?.you!==peer.playerIndex
          ||!Array.isArray(state?.players)||state.players.length!==2||state?.phase==='GameOver')
          issues.push('projection-not-complete-active-player-view')
        const logicalJsonBytes=Buffer.byteLength(JSON.stringify(peer.envelope))
        row.receivers.push({receiver,revision:state?.revision??null,logicalJsonBytes})
        if(logicalJsonBytes<180*1024||logicalJsonBytes>300*1024)issues.push('projection-payload-out-of-range')
      })
      if(JSON.stringify(room.payloadFloorEvidence?.presetIndexes)!=='[0,1]')issues.push('explicit-preset-binding-missing')
    }catch{issues.push('projection-invalid')}
    result.rows.push(row)
  }
  result.issues=[...new Set(issues)]
  if(result.issues.length===0)result.status='ready'
  return result
}

// Preparation is bounded by the existing pressure deadline and never changes measured stages.
// Only normal product commands are used; the existing exact ACK/convergence checks stay in force.
async function waitForPreparationCadence(room, until, signal) {
  for (;;) {
    signal.throwIfAborted()
    const now = Date.now()
    const remaining = room.lastCommandAt + ACTION_INTERVAL_MS - now
    if (remaining <= 0) return
    if (now + remaining + 250 >= until) throw new SafeError('preparation-time-budget-exhausted')
    await delay(remaining, signal)
  }
}

export async function preparePayloadFloor(rooms, context, metrics) {
  if (context.payloadFloorWorkersFrozen !== true) throw new SafeError('preparation-workers-not-frozen')
  const expectedRooms = rooms.length
  if (!Number.isInteger(expectedRooms) || expectedRooms < 1 || expectedRooms > 30 || rooms.length !== expectedRooms)
    throw new SafeError('preparation-room-count-invalid')
  if (!context.local && !STAGE_ROOMS.includes(expectedRooms))
    throw new SafeError('preparation-remote-stage-room-count-invalid')
  const until = Math.min(context.pressureDeadline, Date.now() + 180_000)
  if (!Number.isFinite(until) || until <= Date.now()) throw new SafeError('preparation-time-budget-exhausted')
  const stop = new AbortController()
  const signal = AbortSignal.any([context.signal, stop.signal])
  const workerContext = {...context, signal, measuredCommand:false}
  const preparationMetrics = new Metrics()
  preparationMetrics.currentStage = 'payload-preparation'
  metrics.currentStage = 'payload-preparation'
  const startedWsIndex = metrics.ws.length
  const report = {schema:1,status:'preparing',maximumAdditionalAcceptedCommandsPerRoom:96,
    deadlineWithinOriginalPressureBudget:true,measuredPerformance:false,finalAccepted:false,
    commandSkips:[],rows:[]}
  const started = performance.now()
  const timer = setTimeout(() => stop.abort(new SafeError('preparation-time-budget-exhausted')),
    Math.max(1, until - Date.now()))
  const workers = []
  const pongs = []
  context.payloadFloorWorkersFrozen = false
  const inspectRoom = room => {
    const result = inspectPayloadFloor([room],{workersFrozen:false,expectedRooms:1})
    const counters = room.payloadFloorEvidence
    const zeroAcks = counters?.exactAckMatches === 0 && counters?.peerConvergenceMatches === 0
    const invalid = result.issues.filter(issue => issue !== 'workers-not-frozen'
      && issue !== 'projection-payload-out-of-range'
      && !(issue === 'command-ack-evidence-incomplete' && zeroAcks))
    if (invalid.length) throw new SafeError('preparation-identity-or-baseline-invalid')
    const sizes = result.rows[0]?.receivers.map(receiver => receiver.logicalJsonBytes) ?? []
    if (sizes.length !== 2 || sizes.some(size => !Number.isSafeInteger(size) || size > 300*1024))
      throw new SafeError('preparation-payload-invalid-or-oversize')
    return {ready:!zeroAcks && sizes.every(size => size >= 180*1024), sizes}
  }
  try {
    for (const [slot,room] of rooms.entries()) workers.push((async () => {
      const initialCommands = room.payloadFloorEvidence?.exactAckMatches ?? 0
      for (;;) {
        signal.throwIfAborted()
        const inspection = inspectRoom(room)
        if (inspection.ready) {
          report.rows.push({numericRoomSlot:slot,additionalAcceptedCommands:
            room.payloadFloorEvidence.exactAckMatches-initialCommands,logicalViewBytes:inspection.sizes})
          return
        }
        if (room.payloadFloorEvidence.exactAckMatches-initialCommands >= 96)
          throw new SafeError('preparation-command-budget-exhausted')
        for (;;) {
          await waitForPreparationCadence(room, until, signal)
          let selected = null
          for (const client of [room.host,room.guest]) {
            const snapshot = await authoritativeFullSnapshot(client,until,workerContext)
            const candidate = commandFromSnapshot(client,snapshot,room)
            if (candidate.command) {selected={client,snapshot,...candidate};break}
          }
          if (!selected) throw new SafeError('preparation-no-legal-command')
          const before = room.payloadFloorEvidence.exactAckMatches
          // Per-room metric sink avoids interleaved workers mistaking another room's outcome for their ACK.
          const commandRows = []
          let skippedReason = null
          const sink = {currentStage:'payload-preparation',recordCommandSkip:reason=>{
            const safeReason = ['duplicate-revision','cadence-limited','stage-ending'].includes(reason)
              ? reason : 'unknown'
            report.commandSkips.push({numericRoomSlot:slot,reason:safeReason})
            if (safeReason === 'cadence-limited') {skippedReason=safeReason;return}
            throw new SafeError('preparation-command-skipped')
          },recordCommand:(...args)=>{
            commandRows.push({outcome:args[1]});preparationMetrics.recordCommand(...args)
          }}
          await sendAuthoritativeCommand(room,selected.client,selected.snapshot,selected,until,workerContext,sink)
          if (skippedReason === 'cadence-limited') continue
          if (commandRows.length !== 1 || commandRows[0].outcome !== 'accepted'
              || room.payloadFloorEvidence.exactAckMatches !== before+1
              || room.payloadFloorEvidence.peerConvergenceMatches !== before+1)
            throw new SafeError('preparation-command-not-exactly-confirmed')
          break
        }
      }
    })())
    await Promise.all(workers)
    // A pong orders queued state delivery without forcing a full snapshot or resetting any baseline.
    for (const peer of rooms.flatMap(room=>[room.host,room.guest])) pongs.push((async()=>{
      const pong=peer.waitFor(message=>message.type==='pong','preparation-flush',
        Math.max(1,Math.min(context.requestTimeoutMs,until-Date.now())),false,signal)
      try {peer.send({type:'ping'});await pong}
      catch(error){stop.abort(error);await pong.catch(()=>{});throw error}
    })())
    await Promise.all(pongs)
    context.payloadFloorWorkersFrozen = true
    report.barrier = inspectPayloadFloor(rooms,{workersFrozen:true,expectedRooms})
    if (report.barrier.status !== 'ready') throw new SafeError('preparation-final-barrier-rejected')
    report.status = 'ready'
    return report
  } catch(error) {
    report.status='rejected'
    report.failure=safeFailure(error)
    error.preparationEvidence=report
    throw error
  } finally {
    stop.abort(new SafeError('preparation-workers-stopped'))
    await Promise.allSettled([...workers,...pongs])
    context.payloadFloorWorkersFrozen = true
    clearTimeout(timer)
    report.durationMs=round(performance.now()-started)
    report.rows.sort((left,right)=>left.numericRoomSlot-right.numericRoomSlot)
    report.commandSkips.sort((left,right)=>left.numericRoomSlot-right.numericRoomSlot
      ||left.reason.localeCompare(right.reason))
    report.commands=preparationMetrics.commandSummary([])
    report.traffic={allPayloadBytes:metrics.ws.slice(startedWsIndex).filter(row=>row.stage==='payload-preparation')
      .reduce((sum,row)=>sum+row.bytes,0),
      boundary:'Preparation excluded from measured command/HTTP/pong stage results; overall WS totals include it. Original measured stage traffic stays separately reported.'}
  }
}

export const payloadFloorTestHooks=Object.freeze({
createRoomPair,commandFromSnapshot,confirmationForRequest,sendAuthoritativeCommand,actionWorker,runStage,inspectPayloadFloor,preparePayloadFloor,
})

async function cleanupRoom(room, context, cleanup, numericRoomSlot=null) {
  const timeout = Math.max(250, remainingMs(context.cleanupDeadline, Math.min(context.requestTimeoutMs, 3_000)))
  try {
    const outcome = await cleanupRoomProtocol(room, {
      timeoutMs: timeout,
      requestIdFactory: () => {
       const actor=[room.host,room.guest].find(peer=>!peer.closed)
       const requestId=context.actionMarkers.tryBegin({stage:'cleanup',client:actor,
        commandType:'surrender',roomSlot:numericRoomSlot,sentAt:performance.now()})??crypto.randomUUID()
       const matchId=room.host.envelope?.state?.matchId??room.guest.envelope?.state?.matchId
       for(const peer of [room.host,room.guest]) {
        peer.roomPeer=peer===room.host?room.guest:room.host
        peer.terminalTrace={requestId,matchId,sentAt:performance.now()}
       }
       return requestId
      },
    })
    cleanup.compliantSurrenders += outcome.compliantSurrenders
    cleanup.roomsLeft += outcome.roomsLeft
    cleanup.roomErrors.push(...outcome.errors)
    // Observe late responses without resending or converting the original 3s failure to success.
    if (outcome.errors.length && !context.signal.aborted)
      await delay(Math.min(10000,Math.max(1,context.cleanupDeadline-Date.now())))
  } catch (error) {
    cleanup.roomErrors.push(safeFailure(error))
  } finally {
    room.host.close()
    room.guest.close()
  }
}

async function logout(identity, context, cleanup) {
  try {
    const result = await request(context.baseUrl, 'api/auth/sessions/current', {
      method: 'DELETE', token: identity.token,
      timeoutMs: remainingMs(context.cleanupDeadline, context.requestTimeoutMs), local: context.local,
    })
    if (result.status >= 200 && result.status < 300) cleanup.sessionsRevoked++
    else cleanup.sessionErrors.push(`http-${result.status}`)
  } catch (error) {
    cleanup.sessionErrors.push(safeFailure(error))
  }
}

async function cleanupSyntheticAccounts(context, adminIdentity, provisioning, cleanup) {
  if (!adminIdentity || provisioning.confirmed.length === 0) return
  for (const evidence of provisioning.confirmed) {
    // Existing admin mutation policy is 60/account/minute; cleanup stays below it.
    if (!context.local) await delay(1300)
    try {
      await checkCommit(context, null)
      const detail = await request(context.baseUrl,
        `api/admin/accounts/${encodeURIComponent(evidence.id)}`, {
          token: adminIdentity.token, parseJson: true,
          timeoutMs: remainingMs(context.cleanupDeadline, context.requestTimeoutMs), local: context.local,
        })
      if (detail.status !== 200 || detail.body?.id !== evidence.id
          || detail.body?.username !== evidence.username
          || detail.body?.createdAt !== evidence.createdAt
          || evidence.preexistingIdAbsent !== true || evidence.preexistingUsernameAbsent !== true
          || detail.body?.role !== 'player' || detail.body?.deleted === true
          || !Number.isInteger(detail.body?.permissionVersion)) {
        cleanup.syntheticAccountErrors.push('ownership-evidence-mismatch')
        continue
      }
      let deletionError = null
      try {
        const deleted = await request(context.baseUrl,
          `api/admin/v1/accounts/${encodeURIComponent(evidence.id)}/delete`, {
            method: 'POST', token: adminIdentity.token,
            body: {
              reason: 'testrun pressure synthetic account cleanup',
              idempotencyKey: `pressure-delete-${crypto.randomUUID()}`,
              expectedVersion: detail.body.permissionVersion,
              dryRun: false,
            },
            timeoutMs: remainingMs(context.cleanupDeadline, context.requestTimeoutMs), local: context.local,
          })
        if (deleted.status < 200 || deleted.status >= 300) deletionError = `http-${deleted.status}`
      } catch (error) {
        deletionError = safeFailure(error)
      }
      const verified = await request(context.baseUrl,
        `api/admin/accounts/${encodeURIComponent(evidence.id)}`, {
          token: adminIdentity.token, parseJson: true,
          timeoutMs: remainingMs(context.cleanupDeadline, context.requestTimeoutMs), local: context.local,
        })
      if (verified.status === 200 && verified.body?.id === evidence.id
          && verified.body?.deleted === true && verified.body?.username !== evidence.username) {
        cleanup.syntheticAccountsDeleted++
      } else {
        cleanup.syntheticAccountErrors.push(deletionError ?? 'logical-delete-not-confirmed')
      }
    } catch (error) {
      cleanup.syntheticAccountErrors.push(safeFailure(error))
    }
  }
}

async function runPressure(configuration) {
  endpoint(configuration?.baseUrl,'health',configuration?.local)
  const startedAt = new Date().toISOString()
  const startedClock = Date.now()
  const stop = new StopController()
  const metrics = new Metrics()
  metrics.requestStop=reason=>stop.request(reason)
  validateOriginalWorkloadPlan(configuration)
  const emitProgress=configuration.progressFn??(row=>process.stdout.write(JSON.stringify(row)+'\n'))
  const actionMarkers = new LoadV2Markers({nonce:configuration.diagnosticNonce,onMarker:emitProgress})
  let expectedLoop=performance.now()+20
  const loopProbe=setInterval(()=>{const now=performance.now();const lag=Math.max(0,now-expectedLoop);if(metrics.eventLoopLagMs.length<50000)metrics.eventLoopLagMs.push({stage:metrics.currentStage,lagMs:lag});actionMarkers.recordLoopDrift(lag);expectedLoop=now+20},20)
  loopProbe.unref()
  const rooms = []
  const identities = { players: [], admin: null, tournamentReader: null }
  const allSessions = []
  const provisioning = {
    plannedNames: new Set(), confirmed: [], owned:[],privateValues:new Set(),ledgerReceipts:[{},{}],registrationAttempts: 0,
    uncertainRegistrationResponses: 0,
  }
  const cleanup = {
    compliantSurrenders: 0, roomsLeft: 0, roomErrors: [], sessionsRevoked: 0, sessionErrors: [],
    syntheticAccountsDeleted: 0, syntheticAccountErrors: [],
  }
  const stageDurations = []
  const skips = {}
  let observedCommit = null
  let failure = null
  let payloadFloorPrecheck=null
  let payloadPreparation=null
  const payloadPreparationStages=[]
  let stopMonitor = null
  let hardTimer = null

  const context = {
    ...configuration,
    actionMarkers,
    payloadFloorWorkersFrozen:true,
    signal: stop.controller.signal,
    pressureDeadline: startedClock + PRESSURE_DEADLINE_MS,
    cleanupDeadline: startedClock + MAX_WALL_MS,
  }

  const requestStopIfFileExists = () => {
    if (!configuration.stopFile) return
    try { if (fs.existsSync(configuration.stopFile)) stop.request('stop-file') }
    catch { stop.request('stop-file-check-failed') }
  }

  requestStopIfFileExists()
  if (!stop.reason) {
    stopMonitor = setInterval(requestStopIfFileExists, STOP_POLL_MS)
    hardTimer = setTimeout(() => stop.request('hard-deadline'), PRESSURE_DEADLINE_MS)
  }

  try {
    if (stop.reason) throw new SafeError(stop.reason)
    observedCommit = await checkCommit(context, context.signal)
    const authContext = {
      baseUrl: context.baseUrl, local: context.local, signal: context.signal,
      timeoutMs: context.requestTimeoutMs,
      onSession: identity => allSessions.push(identity),
    }
    if (configuration.provisionPlayers) {
      identities.admin = await authenticateCredential(configuration.credentials.admin, authContext)
      identities.players = await registerSyntheticPlayers(context, identities.admin, authContext, provisioning)
    } else {
      identities.players = await mapLimit(configuration.credentials.players, 4,
        credential => authenticateCredential(credential, authContext))
      if (configuration.credentials.admin)
        identities.admin = await authenticateCredential(configuration.credentials.admin, authContext)
      if (configuration.credentials.tournamentReader)
        identities.tournamentReader = await authenticateCredential(configuration.credentials.tournamentReader, authContext)
    }
    if (new Set(identities.players.map(identity => identity.accountId)).size !== REQUIRED_PLAYERS)
      throw new SafeError('player-accounts-must-be-distinct')
    if (new Set(allSessions.map(identity => identity.accountId)).size > MAX_IDENTITIES)
      throw new SafeError('identity-budget-exceeded')
    configuration.onProvisioned?.()

    const prepared = await prepareWorkloads(context, identities, metrics)
    Object.assign(skips, prepared.skips)
    for (const roomCount of STAGE_ROOMS) {
      metrics.currentStage = `setup-${roomCount}`
      while (rooms.length < roomCount) {
        const playerIndex = rooms.length * 2
        await createRoomPair(context, identities.players[playerIndex], identities.players[playerIndex + 1], metrics, rooms)
      }
      payloadPreparationStages.push(await preparePayloadFloor(rooms,context,metrics))
      stageDurations.push(await runStage(roomCount, rooms, identities, context, prepared.workloads, metrics))
      emitProgress({stage:roomCount,minimumActiveRooms:stageDurations.at(-1).minimumActiveRooms,
        maximumActiveRooms:stageDurations.at(-1).maximumActiveRooms,outsideUsers:OUTSIDE_USERS})
      if (context.signal.aborted) break
      await checkCommit(context, context.signal)
    }
    if (!context.signal.aborted) {
      payloadPreparation=await preparePayloadFloor(rooms,context,metrics)
      await checkCommit(context,context.signal)
    }
  } catch (error) {
    if (error.preparationEvidence) payloadPreparation=error.preparationEvidence
    if (!stop.reason) failure = safeFailure(error)
  } finally {
    if (stopMonitor) clearInterval(stopMonitor)
    if (hardTimer) clearTimeout(hardTimer)
    payloadFloorPrecheck=inspectPayloadFloor(rooms,{workersFrozen:context.payloadFloorWorkersFrozen,expectedRooms:30})
    if(payloadFloorPrecheck.status!=='ready'&&!failure)failure='payload-floor-precheck-failed'
    metrics.currentStage = 'cleanup'
    context.terminalWindow={startUtc:new Date().toISOString(),endUtc:null}
    metrics.burstObserver.begin(performance.now())
    await Promise.all(rooms.map((room,slot) => cleanupRoom(room, context, cleanup,slot)))
    context.terminalWindow.endUtc=new Date().toISOString()
    // Persist completed numeric observation before potentially slow account
    // teardown. This is not a final acceptance or a substitute for cleanup.
    try {
      const checkpoint = createPressureMeasurementCheckpoint({candidate: configuration.expectedCommit,
        coreSourceSha256: sha256File(fileURLToPath(import.meta.url)), metrics: {
          elapsedMs: Date.now()-startedClock, stages: stageDurations,
          terminalFrames: rooms.flatMap(room=>[room.host,room.guest].map(peer=>peer.terminalFrame).filter(Boolean)),
          actionBounds: actionMarkers.report(), http: metrics.httpSummary(skips),
          commands: metrics.commandSummary(stageDurations), websocket: metrics.wsSummary(stageDurations),
          payloadFloor: payloadFloorPrecheck, reconstructionErrors: metrics.reconstructionErrors,
          presenceReconstructionErrors: metrics.presenceReconstructionErrors,
        }})
      persistPressureMeasurementCheckpoint(configuration.measurementCheckpointPath, checkpoint)
    } catch { failure ??= 'measurement-checkpoint-unavailable' }
    if(identities.admin) {
     const accountCleanup=await cleanupDualLedgerAccounts(context,identities.admin,provisioning)
     cleanup.sessionsRevoked=accountCleanup.playerSessionsRevoked+accountCleanup.adminSessionRevoked
     cleanup.syntheticAccountsDeleted=accountCleanup.syntheticAccountsDeleted
     if(accountCleanup.failures)cleanup.syntheticAccountErrors.push('durable-account-cleanup-incomplete')
    }

  }

  const endedClock = Date.now()
  const http = metrics.httpSummary(skips)
  const commands = metrics.commandSummary(stageDurations)
  const sampled = name => http[name]?.skipped === false && (http[name]?.requests ?? 0) > 0
  const workloadErrors = Object.values(http).reduce((sum, item) => sum + (item.errors ?? 0), 0)
  const wsBurstClient=metrics.burstObserver.summary()
  const actionBoundsClient=actionMarkers.report()
  const terminalFrames=rooms.flatMap(room=>[room.host,room.guest].map(peer=>peer.terminalFrame).filter(Boolean))
  const frameProtocol=rooms.flatMap(room=>[room.host,room.guest]).reduce((summary,peer)=>{
   summary.decoderFailures+=peer.cardFrameDecodeFailures;summary.packedFrames+=peer.packedFrames
   summary.plainFrames+=peer.plainFrames;summary.plainRecoveryRequests+=peer.plainRecoveryRequests
   return summary
  },{capability:'gameStateCardFrameV1',decoderFailures:0,packedFrames:0,plainFrames:0,plainRecoveryRequests:0})
  let status = failure ? 'failed'
    : stop.reason ? 'stopped'
      : workloadErrors || metrics.reconstructionErrors
          || commands.overall.outcomes.rejected || commands.overall.outcomes['timed-out']
          || commands.overall.outcomes['transport-error'] || commands.overall.outcomes['advanced-without-ack']
          || cleanup.roomErrors.length || cleanup.sessionErrors.length
          || cleanup.syntheticAccountErrors.length
        ? 'completed-with-errors' : 'completed'
  status=finalizeClientObservationStatus(status,wsBurstClient)
  if (status === 'completed' && (actionBoundsClient.client_complete !== 1
   ||terminalFrames.length!==60||frameProtocol.decoderFailures!==0
   ||cleanup.sessionsRevoked!==70||cleanup.syntheticAccountsDeleted!==69
   ||cleanup.compliantSurrenders!==30||cleanup.roomsLeft!==30))status='inconclusive'
  return {
    schemaVersion: 1,
    status,
    failure,
    stopReason: stop.reason,
    target: { baseUrl: configuration.local ? 'local-protocol-substitute' : PUBLIC_BASE, productionTouched: false },
    expectedCommit: { value: configuration.expectedCommit, source: configuration.expectedCommitSource },
    observedCommit,
    artifactBinding: configuration.artifactBinding ?? { localProtocolSubstitute: true,
      deployedNetSha256: deployedNetSha },
    startedAt,
    endedAt: new Date().toISOString(),
    durationMs: endedClock - startedClock,
    budget: {
      roomStages: STAGE_ROOMS,
      stageSeconds:context.stageSeconds,
      maximumRooms: 30,
      playerIdentities: MATCH_PLAYERS,
      distinctOutsideUsers: OUTSIDE_USERS,
      syntheticIdentities: REQUIRED_PLAYERS,
      disjointMatchAndOutsideAccountPools: true,
      maximumIdentities: MAX_IDENTITIES,
      maximumWallMs: MAX_WALL_MS,
      stopFilePollMs: STOP_POLL_MS,
      httpLaneStages: STAGE_ROOMS.map(() => OUTSIDE_USERS),
      httpIntervalMs: context.httpIntervalMs,
      wsPingIntervalMs: context.wsIntervalMs,
      actionInspectionIntervalMs: ACTION_INTERVAL_MS,
      maximumCommandsPerRoomPerInterval: 1,
      requestTimeoutMs: context.requestTimeoutMs,
    },
    stages: stageDurations,
    http,
    outsideHttpByStage: Object.fromEntries(stageDurations.map(s => [s.name,
      Object.fromEntries([...new Set(metrics.httpRows.filter(x=>x.stage===s.name).map(x=>x.name))]
        .map(name => { const rows=metrics.httpRows.filter(x=>x.stage===s.name && x.name===name)
          return [name,{requests:rows.length,errors:rows.filter(x=>x.error || !(x.status>=200 && x.status<300)).length,
            latencyMs:distribution(rows.map(x=>x.durationMs)),
            timingBreakdown:{headerMs:distribution(rows.map(x=>x.trace?.headerMs).filter(Number.isFinite)),
              bodyReadMs:distribution(rows.map(x=>x.trace?.bodyReadMs).filter(Number.isFinite)),
              serverAppMs:distribution(rows.map(x=>x.trace?.serverAppMs).filter(Number.isFinite)),
              boundary:'Server-Timing app through response headers, not proxy/NIC/body time; no raw headers, identities or body exported.'}}] }))])),
    deltaReconstruction:{sourceSha256:deployedNetSha,errors:metrics.reconstructionErrors},
    presenceDeltaReconstruction:{sourceSha256:deployedNetSha,negotiatedClients:metrics.presenceNegotiatedClients,appliedDeltas:metrics.presenceDeltaApplied,errors:metrics.presenceReconstructionErrors,recoveryRequests:metrics.presenceRecoveries},
    cleanupPassiveTail:{originalFailureThresholdMs:3000,passiveOnlyMs:10000,retries:0,
      responseTimings:metrics.cleanupTimings,clientFrameTimings:metrics.clientFrameTimings,
      eventLoopLagByStage:Object.fromEntries(['setup','rooms-10','rooms-20','rooms-30','cleanup'].map(stage=>[stage,distribution(metrics.eventLoopLagMs.filter(x=>x.stage===stage).map(x=>x.lagMs))])),
      boundary:'Node client raw delivery onward; send-to-arrival includes all server/network queues, not attributed to one cause. Late frames remain failed; no raw request/account/card content exported.'},
    wsBurstClient,
    actionBoundsClient,
    terminalFrames,frameProtocol,payloadFloorPrecheck,payloadPreparation,payloadPreparationStages,ledgerReceipts:provisioning.ledgerReceipts,
    terminalWindow:context.terminalWindow,
    remoteApplyReady:false,completeOriginalWorkloadToolReady:false,
    websocket: metrics.wsSummary(stageDurations),
    commands,
    cleanup: {
      roomsCreated: rooms.length,
      compliantSurrenders: cleanup.compliantSurrenders,
      roomsLeft: cleanup.roomsLeft,
      roomErrors: cleanup.roomErrors,
      sessionsRevoked: cleanup.sessionsRevoked,
      sessionErrors: cleanup.sessionErrors,
      syntheticAccounts: {
        planned: provisioning.plannedNames.size,
        registrationAttempts: provisioning.registrationAttempts,
        confirmed: provisioning.confirmed.length,
        uncertainRegistrationResponses: provisioning.uncertainRegistrationResponses,
        logicalDeletesConfirmed: cleanup.syntheticAccountsDeleted,
        errors: cleanup.syntheticAccountErrors,
      },
    },
    coverage: {
      nonRankedRoomEntry: rooms.some(room => room.started),
      gameplayActions: 'normal-push-reconstructed-prompts-mulligan-affordable-front-legions-legal-legion-attacks-defense-endTurn-surrender',
      authoritativeCommandPressure: commands.overall.attempted > 0,
      commandTypesAccepted: Object.fromEntries(Object.entries(commands.byType)
        .map(([type, summary]) => [type, summary.accepted > 0])),
      playCard: (commands.byType.playCard?.accepted ?? 0) > 0,
      attack: (commands.byType.attack?.accepted ?? 0) > 0,
      fullGameplayActionSequence: false,
      fullSequenceBoundary: 'No master attacks or arbitrary tactic/ability/promotion strategy; no browser FPS measurement',
      cardAnalytics: sampled('cardAnalytics'),
      audit: sampled('audit'),
      authMe: sampled('authMe'),
      tournamentDetail: sampled('tournamentDetail'),
      tournamentSkipReason: skips.tournamentDetail ?? null,
      tournamentMatchCommittedAccess: false,
      tournamentMatchCoverageReason: 'no-tournament-match-created-or-entered',
      noTournamentDataCreated: true,
    },
    privacy: {
      credentialsEmitted: false,
      accountNamesEmitted: false,
      roomCodesEmitted: false,
      tournamentIdsEmitted: false,
      responseBodiesEmitted: false,
    },
  }
}

function parseInteger(value, name, minimum, maximum) {
  const parsed = Number(value)
  if (!Number.isInteger(parsed) || parsed < minimum || parsed > maximum)
    throw new SafeError(`${name}-out-of-range`)
  return parsed
}

function parseArguments(argv) {
  const args = {}
  for (let index = 0; index < argv.length; index++) {
    const key = argv[index]
    if (key === '--execute' || key === '--self-test' || key === '--help') { args[key.slice(2)] = true; continue }
    if (!['--binding','--binding-sha','--ledger-run-0','--ledger-pipe-0','--ledger-run-1','--ledger-pipe-1','--ledger-ready-1','--expected-commit', '--manifest', '--config-receipt', '--output',
      '--stage-seconds', '--http-interval-ms', '--ws-interval-ms', '--request-timeout-ms',
      '--stop-file'].includes(key))
      throw new SafeError('unknown-argument')
    if (index + 1 >= argv.length) throw new SafeError('argument-value-missing')
    args[key.slice(2)] = argv[++index]
  }
  return args
}

function ensureOutputPath(value) {
  if (!value) throw new SafeError('output-path-required')
  const resolved = path.resolve(value)
  const relative = path.relative(TOOL_ROOT, resolved)
  if (relative.startsWith('..') || path.isAbsolute(relative)) throw new SafeError('output-must-stay-in-tool-directory')
  if (!fs.existsSync(path.dirname(resolved))) throw new SafeError('output-parent-missing')
  const realRoot = fs.realpathSync(TOOL_ROOT)
  const realParent = fs.realpathSync(path.dirname(resolved))
  const realRelative = path.relative(realRoot, realParent)
  if (realRelative.startsWith('..') || path.isAbsolute(realRelative))
    throw new SafeError('output-parent-escapes-tool-directory')
  if (fs.existsSync(resolved)) throw new SafeError('output-refuses-overwrite')
  return resolved
}

function readBoundJson(fileArgument, label) {
  if (!fileArgument) throw new SafeError(`${label}-path-required`)
  const resolved = path.resolve(fileArgument)
  const stat = fs.lstatSync(resolved)
  if (!stat.isFile() || stat.isSymbolicLink() || stat.size < 2 || stat.size > 65_536)
    throw new SafeError(`${label}-must-be-small-regular-file`)
  const bytes = fs.readFileSync(resolved)
  let value
  try { value = JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/, '')) }
  catch { throw new SafeError(`${label}-json-invalid`) }
  if (!value || typeof value !== 'object' || Array.isArray(value))
    throw new SafeError(`${label}-json-invalid`)
  return { value, sha256: crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase() }
}

function remoteConfiguration(args) {
  if(args['expected-commit']!==FIXED.candidate)throw new SafeError('fixed-load-v2-candidate-required')
  if (!/^[a-f0-9]{40}$/.test(args['expected-commit'] ?? ''))
    throw new SafeError('expected-commit-must-be-40-lowercase-hex')
  const stageSeconds = parseInteger(args['stage-seconds'] ?? DEFAULTS.stageSeconds, 'stage-seconds', 60, 60)
  if (stageSeconds * STAGE_ROOMS.length * 1_000 > 480_000)
    throw new SafeError('pressure-stage-budget-exceeded')
  if (!args['stop-file']) throw new SafeError('stop-file-required')
  if (process.env.L12_PRESSURE_CREDENTIALS_FILE || process.env.L12_PRESSURE_CREDENTIALS_JSON)
    throw new SafeError('plaintext-credential-input-forbidden')
  const memoryAdminToken=process.env.L12_PRESSURE_ADMIN_TOKEN ?? null
  delete process.env.L12_PRESSURE_ADMIN_TOKEN
  if (!/^[a-f0-9]{64}$/.test(memoryAdminToken ?? ''))
    throw new SafeError('memory-admin-session-required')
  const diagnosticNonce=process.env.L12_SYNTHETIC_TRANSPORT_DIAGNOSTIC_NONCE ?? null
  delete process.env.L12_SYNTHETIC_TRANSPORT_DIAGNOSTIC_NONCE
  if (!/^[a-f0-9]{32}$/.test(diagnosticNonce ?? ''))
    throw new SafeError('synthetic-diagnostic-binding-invalid')
  const nonceSha256=crypto.createHash('sha256').update(diagnosticNonce,'utf8').digest('hex').toUpperCase()
  const manifestEvidence=readBoundJson(args.manifest,'release-manifest')
  const manifest=manifestEvidence.value
  if(manifestEvidence.sha256!==FIXED.manifestSha256 || manifest.schema!==3
      || manifest.commit!==FIXED.candidate || manifest.releaseSha256!==FIXED.releaseSha256.toLowerCase()
      || manifest.releaseBaseCommit!==FIXED.formalCandidate)
    throw new SafeError('exact-schema3-release-manifest-required')
  const receiptEvidence=readBoundJson(args['config-receipt'],'owned-config-receipt')
  const receipt=receiptEvidence.value
  if(receipt.schema!==3 || receipt.status!=='installed' || receipt.operation!=='install'
      || receipt.candidate!==FIXED.candidate || receipt.formalCandidate!==FIXED.formalCandidate
      || receipt.mode!=='terminal-bounds-v2' || receipt.expectedSamples!==6
      || String(receipt.nonceSha256).toUpperCase()!==nonceSha256 || receipt.ownedTestConfigInstalled!==true
      || receipt.startupExecEnvReady!==true || receipt.currentDiagnosticStillActive!==true
      || receipt.ephemeralTestMatches!==true || receipt.formalWrites!==0
      || !Number.isSafeInteger(receipt.testPidAfter) || receipt.testPidAfter<=0
      || !Number.isSafeInteger(receipt.formalPid) || receipt.formalPid<=0)
    throw new SafeError('active-owned-v2-receipt-required')
  return {
    local: false,
    baseUrl: PUBLIC_BASE,
    expectedCommit: args['expected-commit'],
    expectedCommitSource: 'schema3-manifest-owned-v2-receipt-memory-session-and-cli',
    credentials: { players: null, admin: { token: memoryAdminToken }, tournamentReader: null },
    provisionPlayers: true,
    diagnosticNonce,
    artifactBinding:{manifestSha256:manifestEvidence.sha256,releaseSha256:FIXED.releaseSha256.toLowerCase(),
      configReceiptSha256:receiptEvidence.sha256,configTestPid:receipt.testPidAfter,
      configFormalPid:receipt.formalPid,nonceSha256,deployedNetSha256:deployedNetSha,
      candidateBindingSha256:FIXED.bindingSha256,cardFrameSha256:FIXED.cardFrameSha256},
    output: ensureOutputPath(args.output),
    stopFile: path.resolve(args['stop-file']),
    stageSeconds,
    beforeLedgerGroup:index=>index===1?waitForSecondLedgerReader({readyPath:args['ledger-ready-1'],
     runId:args['ledger-run-1'],pipeName:args['ledger-pipe-1']}):Promise.resolve(),
    ledgerClients:[0,1].map(index=>{
     requireContract(/^[A-Za-z0-9_-]{8,96}$/.test(args['ledger-run-'+index]??'')
      &&/^[A-Za-z0-9_-]{16,96}$/.test(args['ledger-pipe-'+index]??''),'two_exact_ledger_pipe_inputs_required')
     return new AccountLedgerClient({runId:args['ledger-run-'+index],maxSlots:index===0?60:9,
      transport:new NamedPipeLedgerTransport({pipeName:args['ledger-pipe-'+index]})})
    }),
    httpIntervalMs: parseInteger(args['http-interval-ms'] ?? DEFAULTS.httpIntervalMs,
      'http-interval-ms', 2_000, 2_000),
    wsIntervalMs: parseInteger(args['ws-interval-ms'] ?? DEFAULTS.wsIntervalMs,
      'ws-interval-ms', 2_000, 2_000),
    requestTimeoutMs: parseInteger(args['request-timeout-ms'] ?? DEFAULTS.requestTimeoutMs,
      'request-timeout-ms', 1_000, 30_000),
  }
}


export {ORIGINAL_WORKLOAD,validateOriginalWorkloadPlan,validateFullCollectionInput,validateFullWorkloadFinal}
 from 'file:///D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/full-workload-contract-r7.mjs'
export const workloadTestHooks=Object.freeze({WsClient,Metrics,cleanupRoom,runPressure,request,
 registerSyntheticPlayers,cleanupDualLedgerAccounts,remoteConfiguration,parseArguments})

export async function waitForSecondLedgerReader({readyPath,runId,pipeName,emit= row=>process.stdout.write(JSON.stringify(row)+'\n'),timeoutMs=15000}) {
 requireContract(typeof readyPath==='string'&&path.isAbsolute(readyPath)
  &&path.relative(TOOL_ROOT,readyPath).split(path.sep)[0]!=='..','ledger_reader_ready_path_invalid')
 // The existing progress reader receives this only after the first ledger has
 // closed; it starts sink #2, then creates an exact, create-new handshake file.
 emit({schema:1,kind:4,ledgerIndex:1,firstLedgerClosed:1,readerPhase:0})
 const deadline=Date.now()+timeoutMs
 while(!fs.existsSync(readyPath)) {
  requireContract(Date.now()<deadline,'second_ledger_reader_ready_timeout')
  await delay(25)
 }
 const item=fs.lstatSync(readyPath)
 requireContract(item.isFile()&&!item.isSymbolicLink()&&item.size>0&&item.size<=2048,
  'second_ledger_reader_ready_file_invalid')
 const value=JSON.parse(fs.readFileSync(readyPath,'utf8'))
 requireContract(Object.keys(value).sort().join('|')==='ledgerIndex|pipeName|readerReady|runId|schema'
  &&value.schema===1&&value.ledgerIndex===1&&value.readerReady===true
  &&value.runId===runId&&value.pipeName===pipeName,'second_ledger_reader_ready_binding_invalid')
}

function printHelp() {
 process.stdout.write(JSON.stringify({status:'prepared-only',remoteApplyReady:false,
  completeOriginalWorkloadToolReady:false,networkCalls:0,workload:ORIGINAL_WORKLOAD})+'\n')
}
async function main() {
  assertR7GeneratedRoot()
  throw new SafeError('payload-floor-direct-entry-disabled')
}

const direct=process.argv[1]&&path.resolve(process.argv[1]).toLowerCase()===fileURLToPath(import.meta.url).toLowerCase()
if(direct)main().catch(error=>{
 process.stderr.write(JSON.stringify({status:'failed-before-report',error:safeFailure(error),networkCalls:null})+'\n')
 process.exitCode=1
})
