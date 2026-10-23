import assert from 'node:assert/strict'
import {createNormalSessionManager,createTerminalRecipientSessionManager} from './testrun-pressure-recovery.mjs'

const setting='terminal-recipients-v4:30:'+'2'.repeat(32)
const binding={kind:'normal-testrun-pressure-v1',baseUrl:'https://legion-12.com/testrun/',
 test:{pid:101,commit:'a'.repeat(40),dllSha256:'B'.repeat(64)},formal:{pid:102,commit:'c'.repeat(40),dllSha256:'D'.repeat(64)}}
let cases=0
function simulation(mode,{runtime={},storage={},duplicate=false,requestStatus=null}={}) {
  let active='';const tokens=new Map(),requests=[]
  const observed={baseUrl:binding.baseUrl,testPid:101,testCommit:binding.test.commit,testDllSha256:binding.test.dllSha256,
    matchStorage:'ephemeral',diagnosticsOff:mode==='normal',...(mode==='diagnostic'?{diagnosticSetting:setting}:{}),...runtime}
  const options={binding,diagnosticSetting:setting,deadlineMs:Date.now()+60000,observeIdentity:async()=>observed,
    acquireSession:async({kind})=>{
      active=duplicate?'1'.repeat(64):(kind==='original'?'1':'2').repeat(64)
      if(!tokens.has(active))tokens.set(active,{id:'session-'+kind,revoked:false})
      return {token:active}
    },
    request:async(base,route,options={})=>{
      assert.equal(base,binding.baseUrl);requests.push({route,method:options.method??'GET'})
      if(requestStatus!==null)return {status:requestStatus,body:null}
      if(route==='health')return {status:200,body:{status:'ok',serverVersion:binding.test.commit,maintenance:false}}
      const token=tokens.get(options.token)
      if(!token||token.revoked)return {status:401,body:null}
      if(route==='api/auth/me')return {status:200,body:{id:'synthetic-admin',role:'admin',deleted:false}}
      if(route==='api/auth/sessions')return {status:200,body:[{id:token.id,current:true}]}
      if(route==='api/admin/storage/private-deck-persistence')return {status:200,body:{configuredEnabled:true,effectiveMode:'object',writable:true,...storage}}
      if(route==='api/auth/sessions/current'&&options.method==='DELETE'){token.revoked=true;return {status:204,body:null}}
      return {status:200,body:{ok:true}}
    }}
  const manager=(mode==='normal'?createNormalSessionManager:createTerminalRecipientSessionManager)(options)
  return {manager,observed,requests,tokens}
}
for(const mode of ['normal','diagnostic']) {
  const test=simulation(mode),one=await test.manager.prepareOriginal(),two=await test.manager.prepareRecovery(one)
  assert.notEqual(one.privateSessionHandle,two.privateSessionHandle)
  assert.equal((await test.manager.request(one,'api/admin/anything')).status,200)
  assert.equal((await test.manager.revoke(one)).afterStatus,401)
  assert.equal((await test.manager.revoke(two)).afterStatus,401)
  assert.ok([...test.tokens.values()].every(row=>row.revoked));cases++
}
for(const [mode,runtime] of [
 ['normal',{diagnosticsOff:false,diagnosticSetting:setting}],
 ['diagnostic',{diagnosticsOff:true}],
 ['diagnostic',{diagnosticSetting:setting.slice(0,-1)+'3'}],
 ['diagnostic',{diagnosticSetting:'terminal-recorder-v3:6:'+'2'.repeat(32)}],
 ['diagnostic',{matchStorage:'persistent'}],
 ['diagnostic',{testPid:999}],
 ['diagnostic',{testCommit:'f'.repeat(40)}],
 ['diagnostic',{testDllSha256:'F'.repeat(64)}],
]) {
 const test=simulation(mode,{runtime});await assert.rejects(()=>test.manager.prepareOriginal(),/SESSION_RUNTIME_BINDING_CHANGED/)
 assert.equal(test.tokens.size,0);cases++
}
for(const bad of ['terminal-recipients-v4:1:'+'2'.repeat(32),'terminal-recipients-v4:31:'+'2'.repeat(32),
 'terminal-recipients-v4:30:'+'A'.repeat(32),setting+':extra','',null]) {
 assert.throws(()=>createTerminalRecipientSessionManager({diagnosticSetting:bad}),/EXACT_TERMINAL_RECIPIENT_PROFILE_REQUIRED/);cases++
}
for(const mode of ['normal','diagnostic']) {
 const test=simulation(mode),one=await test.manager.prepareOriginal()
 if(mode==='normal')test.observed.diagnosticsOff=false
 else test.observed.diagnosticSetting=setting.slice(0,-1)+'3'
 const before=test.requests.length
 await assert.rejects(()=>test.manager.request(one,'api/admin/anything',{method:'POST'}),/SESSION_RUNTIME_BINDING_CHANGED/)
 assert.equal(test.requests.length,before);cases++
 const duplicate=simulation(mode,{duplicate:true}),first=await duplicate.manager.prepareOriginal()
 await assert.rejects(()=>duplicate.manager.prepareRecovery(first),/SESSION_NOT_INDEPENDENT/);cases++
 const wrongF2=simulation(mode,{storage:{effectiveMode:'inline'}})
 await assert.rejects(()=>wrongF2.manager.prepareOriginal(),/SESSION_F2_OBJECT_WRITABLE_REQUIRED/);cases++
}
console.log(JSON.stringify({status:'passed',cases,normalStillRequiresDiagnosticsOff:true,
 diagnosticRequiresExact30RecipientProfile:true,sharedSessionOwnershipAndRevocation:true,networkCalls:0,capacityClaim:false}))
