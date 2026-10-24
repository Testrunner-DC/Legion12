import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {execFileSync} from 'node:child_process'
import {pathToFileURL} from 'node:url'
import {QA_REPO,inspectOwnedLedgerPins,readQaOwnership} from './check-testrun-qa-ownership.mjs'

const text=relative=>fs.readFileSync(path.join(QA_REPO,relative),'utf8')
const inputs={
  sink:fs.readFileSync(path.join(QA_REPO,'ops/qa/ledger/account-ledger-sink-r2.ps1')),
  client:fs.readFileSync(path.join(QA_REPO,'ops/qa/ledger/account-ledger-client-r2.mjs')),
  reader:fs.readFileSync(path.join(QA_REPO,'ops/qa/ledger/AccountLedgerReader.ps1')),
  execution:text('opcgpro-vue/scripts/testrun-pressure-execution.mjs'),
  recovery:text('opcgpro-vue/scripts/testrun-pressure-recovery.mjs'),
  localQualifier:text('opcgpro-vue/scripts/qualify-local-pressure.mjs'),
  normalReader:text('ops/qa/Read-TestrunPressureLedgers.ps1'),
  localReader:text('ops/qa/Verify-LocalPressureLedgers.ps1'),
  executionTest:text('opcgpro-vue/scripts/test-testrun-pressure-execution.mjs'),
}
let cases=0
const check=work=>{work();cases++}
check(()=>assert.equal(inspectOwnedLedgerPins(inputs).consumerPins,9))
for(const key of ['sink','client','reader'])check(()=>{
  const changed=Buffer.from(inputs[key]);changed[0]^=1
  assert.throws(()=>inspectOwnedLedgerPins({...inputs,[key]:changed}),/QA_OWNED_LEDGER_.*_DRIFT/)
})
for(const [key,label]of [
  ['execution','SINK_SHA='],['execution','CLIENT_SHA='],
  ['recovery','SINK_SHA = '],['recovery','CLIENT_SHA = '],
  ['localQualifier','LEDGER_SINK_SHA = '],['localQualifier','LEDGER_CLIENT_SHA = '],
  ['normalReader',"$readerSha="],['localReader','-cne'],['recovery','frozenReaderSha256: '],
])check(()=>{
  const start=inputs[key].indexOf(label);assert.ok(start>=0)
  const rest=inputs[key].slice(start),match=rest.match(/[A-F0-9]{64}/);assert.ok(match)
  const changed=inputs[key].slice(0,start)+rest.replace(match[0],'F'.repeat(64))
  assert.throws(()=>inspectOwnedLedgerPins({...inputs,[key]:changed}),/QA_OWNED_LEDGER_CONSUMER_PIN_DRIFT/)
})
for(const [key,old]of [
  ['execution',"const LEDGER=path.join(REPO,'ops/qa/ledger')"],
  ['recovery',"const LEDGER_SOURCES = path.join(REPO, 'ops/qa/ledger')"],
  ['normalReader',"$source=Join-Path $PSScriptRoot 'ledger/AccountLedgerReader.ps1'"],
  ['localReader',"$source = Join-Path $PSScriptRoot 'ledger/AccountLedgerReader.ps1'"],
  ['localQualifier',"from '../../ops/qa/ledger/account-ledger-client-r2.mjs'"],
  ['localQualifier',"const ledgerSource = path.join(repo, 'ops/qa/ledger/account-ledger-sink-r2.ps1')"],
  ['executionTest',"await import('../../ops/qa/ledger/account-ledger-client-r2.mjs')"],
])check(()=>{
  assert.ok(inputs[key].includes(old))
  assert.throws(()=>inspectOwnedLedgerPins({...inputs,[key]:inputs[key].replace(old,'HISTORICAL_SOURCE')}),/QA_LEDGER_CONSUMER_NOT_REPOSITORY_BOUND/)
})

// Parse the actual repository library. Its exact original AST bodies, not a
// copied test runner or a hand-written approximation, form the reader contract.
const parsed=execFileSync(process.env.L12_PWSH||'pwsh',['-NoProfile','-NonInteractive','-Command',String.raw`
$ErrorActionPreference='Stop'
. $env:L12_LEDGER_STORAGE_GUARD
$null=Assert-L12GeneratedPath -Path $env:L12_LEDGER_TEST_CACHE -Purpose 'read-only ledger AST acceptance'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($env:L12_LEDGER_LIBRARY,[ref]$tokens,[ref]$errors)
if(@($errors).Count -or $null -ne $ast.ParamBlock -or $null -ne $ast.BeginBlock -or $null -ne $ast.ProcessBlock){throw 'READER_LIBRARY_NOT_FUNCTION_ONLY'}
$statements=@($ast.EndBlock.Statements)
if($statements.Count -ne 4){throw 'READER_LIBRARY_NOT_FOUR_FUNCTIONS'}
$result=[ordered]@{}
foreach($node in $statements){
 if($node -isnot [Management.Automation.Language.FunctionDefinitionAst]){throw 'READER_LIBRARY_HAS_EXECUTABLE_STATEMENT'}
 $result[$node.Name]=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($node.Extent.Text)))
}
$result | ConvertTo-Json -Compress
`],{encoding:'utf8',windowsHide:true,timeout:15000,maxBuffer:65536,env:{...process.env,
  L12_LEDGER_STORAGE_GUARD:path.join(QA_REPO,'ops/windows/L12GeneratedStorage.ps1'),
  L12_LEDGER_TEST_CACHE:process.env.L12_WORK_CACHE||'D:/GPT/Legion12/cache/primary',
  L12_LEDGER_LIBRARY:path.join(QA_REPO,'ops/qa/ledger/AccountLedgerReader.ps1')}})
check(()=>assert.deepEqual(JSON.parse(parsed),{
  'Throw-L12RealTest':'80CFA8D8C823B3A8EE85048D8953AE1EA66A3B77B78409EE6B677DB54973FDAA',
  'Assert-L12RegularFile':'E7C1669445B01E7301AFC7499A2F6F188A947C4F9570EE46DA76215D286ECCDA',
  'Test-L12ContainsBytes':'499BC2118CE5FD653B1512B3CE188A932F166F06B2554C3FB9D28F706B4E9FE5',
  'Read-L12ProtectedSnapshot':'00D98A2E7F132CD212A99E16A5455DA5DD3699808CD2E1A0E7CE35B476CD89E3',
}))
const attributes=fs.readFileSync(path.join(process.env.L12_QA_SOURCE_REPO||QA_REPO,'.gitattributes'),'utf8')
for(const name of ['account-ledger-sink-r2.ps1','account-ledger-client-r2.mjs','AccountLedgerReader.ps1'])check(()=>{
  assert.ok(attributes.split(/\r?\n/).includes('/ops/qa/ledger/'+name+' -text'))
  const bytes=fs.readFileSync(path.join(QA_REPO,'ops/qa/ledger',name));assert.ok(!bytes.includes(13)&&bytes[0]!==0xef)
})

// Actual default ownership inspection must not depend on the old ledger files.
// Explicit --verify-local-inputs remains a separate historical evidence audit.
const readFile=fs.readFileSync
let oldLedgerReads=0
try{
  fs.readFileSync=function(file,...args){
    if(String(file).replaceAll('\\','/').includes('/ledger-r5/')){oldLedgerReads++;throw new Error('OLD_LEDGER_SOURCE_UNAVAILABLE')}
    return readFile.call(this,file,...args)
  }
  check(()=>{assert.equal(readQaOwnership().ownedLedgerPins.repositoryPaths,7);assert.equal(oldLedgerReads,0)})
}finally{fs.readFileSync=readFile}

const originalFetch=globalThis.fetch
let networkCalls=0
globalThis.fetch=()=>{networkCalls++;throw new Error('NETWORK_FORBIDDEN')}
try{
  const {AccountLedgerClient,registerAccountWithLedger}=await import(pathToFileURL(path.join(QA_REPO,'ops/qa/ledger/account-ledger-client-r2.mjs')).href)
  const order=[];let releaseIntent
  const transport={request(event){
    order.push('ledger-'+event.type)
    const ack={ok:true,seq:event.sequence,count:1,cipherSha256:'a'.repeat(64)}
    return event.type==='intent'?new Promise(resolve=>{releaseIntent=()=>resolve(ack)}):Promise.resolve(ack)
  }}
  const ledger=new AccountLedgerClient({runId:'owned-ledger-qa-20261024',maxSlots:1,transport})
  const account={id:'synthetic-owned-account-0001',username:'OwnedQA',createdAt:new Date().toISOString()}
  const pending=registerAccountWithLedger({ledger,slot:0,username:account.username,password:'synthetic-only-password-20261024',
    registerFn:async()=>{order.push('register');return {account}}})
  await Promise.resolve()
  check(()=>assert.deepEqual(order,['ledger-intent']))
  releaseIntent();assert.deepEqual(await pending,{account})
  check(()=>assert.deepEqual(order,['ledger-intent','register','ledger-confirmed']))
  let registered=0,aborted=0
  const failed=new AccountLedgerClient({runId:'owned-ledger-bad-20261024',maxSlots:1,
    transport:{request:async()=>({ok:true,seq:99,count:1,cipherSha256:'a'.repeat(64)}),abort(){aborted++}}})
  await assert.rejects(()=>registerAccountWithLedger({ledger:failed,slot:0,username:'BadAckQA',password:'synthetic-only-password-20261024',
    registerFn:async()=>{registered++;return {account}}}),/ledger_ack_rejected/)
  check(()=>{assert.equal(registered,0);assert.ok(aborted>0);assert.equal(failed.failed,true)})
  assert.equal(networkCalls,0)
}finally{globalThis.fetch=originalFetch}
console.log(JSON.stringify({status:'passed',cases,originalReaderFunctionBodies:4,oldLedgerSourceReads:oldLedgerReads,
  actualClientAckOrder:true,networkCalls,actualDpapiExecuted:false,filesWritten:0,onlineCapacityAccepted:false}))
