import assert from 'node:assert/strict'
import {cleanupOwnedTestrunAccounts} from './testrun-pressure-protocol.mjs'

// Deterministic scheduling proof of the actual cleanup helper, not a server
// benchmark. Virtual time advances only for its own waits and mock responses.
const originalNow=Date.now, originalFetch=globalThis.fetch
let networkCalls=0
globalThis.fetch=()=>{networkCalls++;throw new Error('NETWORK_FORBIDDEN')}
const accounts=Array.from({length:69},(_,index)=>({id:'synthetic-'+index,username:'synthetic-'+index,
  createdAt:'2026-10-23T00:00:00Z',preflightAbsent:true,registrationOutcome:'confirmed'}))
async function measure(responseMs,budgetMs) {
  let clock=0,requests=0,waits=0,adminRevoked=false
  const removed=new Set(),writeTimes=[]
  Date.now=()=>clock
  const request=async(_base,route,_deadline,options={})=>{
    assert.ok(clock<budgetMs,'no request may begin after the cleanup deadline')
    requests++;clock+=responseMs
    if(options.method)writeTimes.push(clock-responseMs)
    if(route==='health')return {status:'ok',serverVersion:'a'.repeat(40),maintenance:false}
    if(route==='api/auth/sessions/current'){adminRevoked=true;return}
    if(route==='api/auth/me'){assert.equal(adminRevoked,true);assert.deepEqual(options.expected,[401]);return}
    if(route.endsWith('/delete')){assert.equal(options.body.expectedVersion,7);removed.add(route.split('/')[4]);return}
    if(route.endsWith('/sessions'))return
    const account=accounts.find(item=>item.id===route.split('/')[3]);assert.ok(account)
    return {...account,role:'player',permissionVersion:7,deleted:removed.has(account.id),
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
  assert.equal(zeroLatency.failures,0);assert.equal(zeroLatency.waits,138)
  assert.equal(zeroLatency.elapsedMs,179400,'cleanup cadence must count elapsed work, not add a fixed delay to every mutation')
  assert.equal(zeroLatency.requests,555);assert.ok(zeroLatency.minimumWriteGapMs>=1300)
  const reservedWindow=await measure(0,180000)
  assert.equal(reservedWindow.failures,0);assert.equal(reservedWindow.adminSessionRevoked,1)
  const responseControl=await measure(100,1000000)
  assert.equal(responseControl.failures,0);assert.equal(responseControl.elapsedMs,193700)
  assert.equal(responseControl.requests,555);assert.equal(responseControl.syntheticAccountsDeleted,69)
  assert.ok(responseControl.minimumWriteGapMs>=1300)
  const shortWindow=await measure(100,1000)
  assert.ok(shortWindow.failures>0);assert.equal(shortWindow.adminSessionRevoked,0)
  assert.ok(shortWindow.elapsedMs<=1000,'an expired cleanup must stop even read-only requests')
  const expiredHealth=await measure(100,250)
  assert.equal(expiredHealth.writeCount,0,'a pre-write health response crossing the deadline must not admit a mutation')
  assert.equal(networkCalls,0)
  console.log(JSON.stringify({schema:1,status:'passed',realCleanupFunction:true,virtualClock:true,
    originalPressureCeilingMs:720000,originalLifecycleCeilingMs:900000,reservedCleanupMs:180000,
    minimumCompletionAtPressureCeilingMs:720000+zeroLatency.elapsedMs,
    zeroLatency,reservedWindow,responseControl,shortWindow,expiredHealth,networkCalls,
    boundary:'Scheduling lower-bound proof only. Original 900-second lifecycle and server behavior are unchanged; no capacity acceptance or remote cleanup.'}))
} finally {Date.now=originalNow;globalThis.fetch=originalFetch}
