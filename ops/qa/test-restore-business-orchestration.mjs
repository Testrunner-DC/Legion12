import assert from 'node:assert/strict'
import {normalizeSha256,bindCurrentServices} from './run-restore-business-drill.mjs'

const commit='3a376f17e5a6f9431303b35775977fabd2083b6a'
const sha='BAEFF29E79B3DE4AF855151D166ABD89EB415D3426D40762E1860A8DA5FFF171'
const binding={test:{commit,pid:22,dllSha256:sha,manifestPath:'synthetic-preserved'},formal:{commit:'0'.repeat(40),pid:11,dllSha256:'0'.repeat(64)}}
const summary={completed:true,applicationCommit:commit,dllSha256:sha.toLowerCase(),formal:{pid:33},test:{pid:22}}
let cases=0
for(const value of [sha,sha.toLowerCase(),sha.slice(0,20).toLowerCase()+sha.slice(20)]){
  assert.equal(normalizeSha256(value),sha);cases++
}
for(const value of [null,true,64,' '+sha,sha+'\n',sha.slice(1),sha+'0',sha.replace('B','g')]){
  assert.throws(()=>normalizeSha256(value));cases++
}
const before=JSON.stringify(binding)
const actual=bindCurrentServices(binding,summary)
assert.equal(JSON.stringify(binding),before)
assert.deepEqual(actual.formal,{commit,pid:33,dllSha256:sha})
assert.equal(actual.test.manifestPath,'synthetic-preserved');cases++
for(const change of [{completed:false},{applicationCommit:'0'.repeat(40)},{dllSha256:'0'.repeat(64)},
  {formal:{pid:'33'}},{formal:{pid:0}},{formal:{pid:22}},{test:{pid:99}}]){
  assert.throws(()=>bindCurrentServices(binding,{...summary,...change}));cases++
}
assert.throws(()=>bindCurrentServices({...binding,test:{...binding.test,commit:'0'.repeat(40)}},summary));cases++
assert.throws(()=>bindCurrentServices({...binding,test:{...binding.test,dllSha256:'0'.repeat(64)}},summary));cases++
console.log(JSON.stringify({status:'passed',cases,scope:'exact SHA representation and completed-service receipt binding only',networkCalls:0,applicationStarts:0,formalWrites:0}))
