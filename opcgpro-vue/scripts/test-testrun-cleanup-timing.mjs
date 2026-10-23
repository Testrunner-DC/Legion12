import assert from 'node:assert/strict'
import {cleanupOwnedTestrunAccounts} from './testrun-pressure-protocol.mjs'

// Deterministic scheduling proof of the actual cleanup helper, not a server
// benchmark. Virtual time advances only for its own waits and mock responses.
const originalNow=Date.now, originalFetch=globalThis.fetch
let networkCalls=0
globalThis.fetch=()=>{networkCalls++;throw new Error('NETWORK_FORBIDDEN')}
const accounts=Array.from({length:69},(_,index)=>({id:'synthetic-'+index,username:'synthetic-'+index,
  token:(index+3).toString(16).padStart(64,'0'),createdAt:'2026-10-23T00:00:00Z',preflightAbsent:true,registrationOutcome:'confirmed'}))
async function measure(responseMs,budgetMs,fault=null) {
  let clock=0,requests=0,waits=0,adminRevoked=false
  const removed=new Set(),revoked=new Set(),writeTimes=[]
  Date.now=()=>clock
  const request=async(_base,route,_deadline,options={})=>{
    assert.ok(clock<budgetMs,'no request may begin after the cleanup deadline')
    requests++;clock+=responseMs
    if(options.method)writeTimes.push(clock-responseMs)
    if(route==='health')return {status:'ok',serverVersion:'a'.repeat(40),maintenance:false}
    if(route==='api/auth/sessions/current'){adminRevoked=true;return}
    if(route==='api/auth/me'){
      assert.deepEqual(options.expected,[401])
      if(options.token==='b'.repeat(64))assert.equal(adminRevoked,true)
      else{const account=accounts.find(row=>row.token===options.token);assert.ok(account);assert.ok(revoked.has(account.id)&&fault!=='token-still-valid')}
      return
    }
    if(route.endsWith('/delete')){assert.equal(options.body.expectedVersion,7);const id=route.split('/')[4];removed.add(id);if(fault!=='stale-session')revoked.add(id);if(fault==='delete-ack-loss')throw new Error('SYNTHETIC_ACK_LOST');return}
    if(route.endsWith('/sessions')){
      const id=route.split('/')[3]
      if(options.method){revoked.add(id);return}
      if(fault==='sessions-unreadable')throw new Error('SYNTHETIC_UNREADABLE')
      return revoked.has(id)?[]:[{id:'synthetic-active-session'}]
    }
    const account=accounts.find(item=>item.id===route.split('/')[3]);assert.ok(account)
    return {...account,createdAt:removed.has(account.id)&&fault==='wrong-created-at'?'2000-01-01T00:00:00Z':account.createdAt,
      role:fault==='wrong-role'?'admin':'player',permissionVersion:7,deleted:removed.has(account.id),
      username:removed.has(account.id)?'deleted-'+account.id:account.username}
  }
  const result=await cleanupOwnedTestrunAccounts({local:false,baseUrl:'https://legion-12.com/testrun/',
    expectedCommit:'a'.repeat(40),runId:'c'.repeat(32),cleanupDeadline:budgetMs},
    {token:'b'.repeat(64)},accounts,request,async ms=>{assert.ok(ms>0&&ms<=1300);waits++;clock+=ms})
  return {responseMs,budgetMs,elapsedMs:clock,requests,waits,writeCount:writeTimes.length,
    minimumWriteGapMs:writeTimes.length>1?Math.min(...writeTimes.slice(1).map((value,index)=>value-writeTimes[index])):null,...result}
}
try {
  const zeroLatency=await measure(0,1000000)
  assert.equal(zeroLatency.failures,0)
  assert.equal(zeroLatency.writeCount,70,'atomic deletion already revokes sessions; do not send another 69 mutations')
  assert.equal(zeroLatency.waits,69)
  assert.equal(zeroLatency.elapsedMs,89700,'preserve cadence while removing the redundant revocation mutation')
  assert.equal(zeroLatency.requests,555);assert.ok(zeroLatency.minimumWriteGapMs>=1300)
  const reservedWindow=await measure(0,180000)
  assert.equal(reservedWindow.failures,0);assert.equal(reservedWindow.adminSessionRevoked,1)
  const responseControl=await measure(100,1000000)
  assert.equal(responseControl.failures,0);assert.equal(responseControl.elapsedMs,104000)
  assert.equal(responseControl.requests,555);assert.equal(responseControl.syntheticAccountsDeleted,69)
  assert.ok(responseControl.minimumWriteGapMs>=1300)
  const shortWindow=await measure(100,1000)
  assert.ok(shortWindow.failures>0);assert.equal(shortWindow.adminSessionRevoked,0)
  assert.ok(shortWindow.elapsedMs<=1000,'an expired cleanup must stop even read-only requests')
  const expiredHealth=await measure(100,250)
  assert.equal(expiredHealth.writeCount,0,'a pre-write health response crossing the deadline must not admit a mutation')
  const adversarial=[]
  for(const fault of ['stale-session','token-still-valid','sessions-unreadable','wrong-created-at','wrong-role','delete-ack-loss']){
    const rejected=await measure(0,1000000,fault)
    assert.equal(rejected.failures,69,fault)
    assert.equal(rejected.playerSessionsRevoked,0,fault+' must not claim an unverified revocation')
    assert.equal(rejected.syntheticAccountsDeleted,0,fault+' must not upgrade an uncertain cleanup')
    adversarial.push({fault,failures:rejected.failures,playerSessionsRevoked:rejected.playerSessionsRevoked})
  }
  assert.equal(networkCalls,0)
  console.log(JSON.stringify({schema:1,status:'passed',realCleanupFunction:true,virtualClock:true,
    originalPressureCeilingMs:720000,originalLifecycleCeilingMs:900000,reservedCleanupMs:180000,
    minimumCompletionAtPressureCeilingMs:720000+zeroLatency.elapsedMs,
    zeroLatency,reservedWindow,responseControl,shortWindow,expiredHealth,adversarial,networkCalls,
    boundary:'Scheduling lower-bound proof only. Original 900-second lifecycle and server behavior are unchanged; no capacity acceptance or remote cleanup.'}))
} finally {Date.now=originalNow;globalThis.fetch=originalFetch}
