import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {execFileSync} from 'node:child_process'
import {fileURLToPath} from 'node:url'
import ts from 'typescript'

export const QA_REPO=path.resolve(import.meta.dirname,'../..')
export const QA_MANIFEST='ops/qa/toolchain-sources.json'
const sourceExtensions=new Set(['.mjs','.py','.ps1','.cs','.csproj'])
const allowedSource=value=>typeof value==='string'&&value.length>0&&!value.includes('\\')
  &&!path.isAbsolute(value)&&!value.split('/').some(part=>part==='..'||part==='.'||!part)
  &&(value.startsWith('opcgpro-vue/scripts/')||value.startsWith('ops/qa/')
    ||value==='scripts/lib/l12-pressure-checkpoint.mjs')&&sourceExtensions.has(path.extname(value))

export function inspectOwnedGuardPins({frozen,normal,pressureRuntime,terminalRuntime,terminalGuard,qualification}) {
  const sha=bytes=>crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()
  const pin=(source,pattern)=>{const matches=[...source.matchAll(pattern)];assert.equal(matches.length,1,'QA_GUARD_PIN_ANCHOR_CHANGED');return matches[0][1]}
  for(const bytes of [frozen,normal])assert.ok(Buffer.isBuffer(bytes)&&bytes.length>0&&bytes.length<=131072,'QA_OWNED_GUARD_BYTES_INVALID')
  const frozenSha256=sha(frozen),normalSha256=sha(normal)
  assert.equal(frozenSha256,pin(pressureRuntime,/export const FROZEN_GUARD_SHA='([A-F0-9]{64})'/g),'QA_OWNED_FROZEN_GUARD_DRIFT')
  const normalPins=[pin(terminalRuntime,/const NORMAL_GUARD_SHA='([A-F0-9]{64})'/g),
    pin(terminalGuard,/^NORMAL_SHA = "([A-F0-9]{64})"\r?$/gm),
    pin(qualification,/assertGuardReady\(ready,binding,'([A-F0-9]{64})'\)/g)]
  for(const expected of normalPins)assert.equal(normalSha256,expected,'QA_OWNED_NORMAL_GUARD_DRIFT')
  return {frozenSha256,normalSha256,normalPinConsumers:normalPins.length}
}

export function inspectOwnedLedgerPins({sink,client,reader,execution,recovery,localQualifier,normalReader,localReader,executionTest}) {
  const sha=bytes=>crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()
  const pin=(source,pattern)=>{const matches=[...source.matchAll(pattern)];assert.equal(matches.length,1,'QA_LEDGER_PIN_ANCHOR_CHANGED');return matches[0][1]}
  for(const bytes of [sink,client,reader])assert.ok(Buffer.isBuffer(bytes)&&bytes.length>0&&bytes.length<=131072,'QA_OWNED_LEDGER_BYTES_INVALID')
  const sinkSha256=sha(sink),clientSha256=sha(client),readerSha256=sha(reader)
  // The first two are byte-for-byte historical sources. The last file contains
  // only the four original reader ASTs, never the historical test runner.
  assert.equal(sinkSha256,'B74A6359DA15536248EE660F01B33DFD8A59376AF6D930DDC0F2BC54CC94DA81','QA_OWNED_LEDGER_SINK_DRIFT')
  assert.equal(clientSha256,'88BB6ACB68C3867CEC7A5E3DBA5A2B429A672FE4CDEF94C8D47D5921EA38B7F9','QA_OWNED_LEDGER_CLIENT_DRIFT')
  assert.equal(readerSha256,'7ABD52F25FFB11B9DEDBD11B6C6E4FF592F72DA0900D46133BC41E34AE1EBE25','QA_OWNED_LEDGER_READER_DRIFT')
  const pins=[
    [sinkSha256,pin(execution,/const SINK_SHA='([A-F0-9]{64})'/g)],
    [clientSha256,pin(execution,/const CLIENT_SHA='([A-F0-9]{64})'/g)],
    [sinkSha256,pin(recovery,/const SINK_SHA = '([A-F0-9]{64})'/g)],
    [clientSha256,pin(recovery,/const CLIENT_SHA = '([A-F0-9]{64})'/g)],
    [sinkSha256,pin(localQualifier,/const LEDGER_SINK_SHA = '([A-F0-9]{64})'/g)],
    [clientSha256,pin(localQualifier,/const LEDGER_CLIENT_SHA = '([A-F0-9]{64})'/g)],
    [readerSha256,pin(normalReader,/\$readerSha='([A-F0-9]{64})'/g)],
    [readerSha256,pin(localReader,/-cne\s*\r?\n\s*'([A-F0-9]{64})'/g)],
    [readerSha256,pin(recovery,/frozenReaderSha256: '([A-F0-9]{64})'/g)],
  ]
  for(const [actual,expected]of pins)assert.equal(actual,expected,'QA_OWNED_LEDGER_CONSUMER_PIN_DRIFT')
  const paths=[
    [execution,"const LEDGER=path.join(REPO,'ops/qa/ledger')"],
    [recovery,"const LEDGER_SOURCES = path.join(REPO, 'ops/qa/ledger')"],
    [normalReader,"$source=Join-Path $PSScriptRoot 'ledger/AccountLedgerReader.ps1'"],
    [localReader,"$source = Join-Path $PSScriptRoot 'ledger/AccountLedgerReader.ps1'"],
    [localQualifier,"from '../../ops/qa/ledger/account-ledger-client-r2.mjs'"],
    [localQualifier,"const ledgerSource = path.join(repo, 'ops/qa/ledger/account-ledger-sink-r2.ps1')"],
    [executionTest,"await import('../../ops/qa/ledger/account-ledger-client-r2.mjs')"],
  ]
  for(const [source,expected]of paths)assert.ok(source.includes(expected),'QA_LEDGER_CONSUMER_NOT_REPOSITORY_BOUND')
  for(const source of [execution,recovery,localQualifier,normalReader,localReader,executionTest])
    assert.ok(!source.includes('/ledger-r5/'),'QA_LEDGER_CONSUMER_STILL_USES_HISTORICAL_CODE')
  return {sinkSha256,clientSha256,readerSha256,consumerPins:pins.length,repositoryPaths:paths.length}
}

// This is a read-only source-ownership check, not remote execution permission or
// a substitute for the normal runtime/identity/workload/cleanup verifiers.
export function inspectQaOwnership(manifest,{repo=QA_REPO,tracked,read=file=>fs.readFileSync(file),verifyLocalInputs=false,sourceMirror}={}) {
  assert.equal(manifest.schema,1)
  assert.equal(manifest.formalMutationsAllowed,false)
  assert.equal(manifest.normalConfirmationMs,3000);assert.equal(manifest.normalLifecycleMs,900000)
  assert.ok(Array.isArray(manifest.sources)&&manifest.sources.length>0&&manifest.sources.length<=80)
  assert.ok(tracked instanceof Set)
  if(sourceMirror){
    assert.ok(path.isAbsolute(sourceMirror.root)&&path.resolve(sourceMirror.root)!==path.resolve(repo),'QA_STAGED_SOURCE_ROOT_INVALID')
    assert.match(sourceMirror.commit,/^[a-f0-9]{40}$/)
    assert.equal(sourceMirror.head,sourceMirror.commit,'QA_STAGED_SOURCE_COMMIT_CHANGED')
    assert.equal(sourceMirror.clean,true,'QA_STAGED_SOURCE_NOT_CLEAN')
    assert.ok(read(path.join(repo,QA_MANIFEST)).equals(read(path.join(sourceMirror.root,QA_MANIFEST))),'QA_STAGED_MANIFEST_DIFFERS')
  }
  const equalMirror=(relative,bytes)=>{
    if(sourceMirror)assert.ok(bytes.equals(read(path.join(sourceMirror.root,relative))),'QA_STAGED_SOURCE_DIFFERS: '+relative)
  }
  const seen=new Set(),hashes={},importTargets=new Set()
  for(const relative of manifest.sources) {
    assert.ok(allowedSource(relative),'QA_SOURCE_PATH_INVALID')
    assert.ok(!seen.has(relative),'QA_SOURCE_DUPLICATE');seen.add(relative)
    assert.ok(tracked.has(relative),'UNVERSIONED_QA_SOURCE: '+relative)
    const absolute=path.resolve(repo,relative),bytes=read(absolute)
    assert.ok(Buffer.isBuffer(bytes)&&bytes.length>0&&bytes.length<=256*1024,'QA_SOURCE_SIZE_INVALID')
    const source=bytes.toString('utf8')
    equalMirror(relative,bytes)
    assert.ok(!/-----BEGIN (?:OPENSSH|RSA|EC|DSA) PRIVATE KEY-----/.test(source),'PRIVATE_KEY_IN_QA_SOURCE')
    hashes[relative]=crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()
    if(!relative.endsWith('.mjs'))continue
    const ast=ts.createSourceFile(relative,source,ts.ScriptTarget.Latest,true,ts.ScriptKind.JS)
    for(const node of ast.statements)if(ts.isImportDeclaration(node)&&ts.isStringLiteral(node.moduleSpecifier)) {
      const specifier=node.moduleSpecifier.text
      if(!specifier.startsWith('.'))continue
      const target=path.relative(repo,path.resolve(path.dirname(absolute),specifier)).replaceAll('\\','/')
      assert.ok(!target.startsWith('../')&&!path.isAbsolute(target),'QA_IMPORT_ESCAPES_REPO')
      assert.ok(tracked.has(target),'UNVERSIONED_QA_IMPORT: '+target)
      if(sourceMirror)equalMirror(target,read(path.join(repo,target)))
      importTargets.add(target)
    }
  }
  assert.ok(Array.isArray(manifest.entries)&&manifest.entries.length>0)
  for(const entry of manifest.entries) {
    assert.ok(seen.has(entry.path),'QA_ENTRY_NOT_OWNED')
    assert.ok(['local-browser','test-only','read-only-static','offline-preparer','private-ledger'].includes(entry.mode),'QA_ENTRY_SCOPE_INVALID')
    if(entry.mode==='test-only')assert.equal(entry.target,'https://legion-12.com/testrun/')
  }
  assert.ok(Array.isArray(manifest.legacyExecutableInputs)&&Array.isArray(manifest.immutableEvidenceInputs))
  const executableInputs=new Set(),evidenceInputs=new Set()
  for(const input of manifest.legacyExecutableInputs) {
    assert.ok(typeof input.path==='string'&&/^[CDE]:\//.test(input.path)&&!input.path.split('/').includes('..')
      &&/\.(mjs|ps1|py)$/.test(input.path),'QA_EXECUTABLE_INPUT_PATH_INVALID')
    assert.ok(!executableInputs.has(input.path),'QA_EXECUTABLE_INPUT_DUPLICATE');executableInputs.add(input.path)
    assert.match(input.sha256,/^[A-F0-9]{64}$/);assert.ok(typeof input.role==='string'&&input.role.length>=8)
    assert.ok(['adapted-module','module-import','child-process','compiled-exec','function-extraction'].includes(input.execution),'QA_EXECUTABLE_MODE_INVALID')
    if(verifyLocalInputs)assert.equal(crypto.createHash('sha256').update(read(input.path)).digest('hex').toUpperCase(),input.sha256,'PINNED_QA_EXECUTABLE_DRIFT')
  }
  for(const input of manifest.immutableEvidenceInputs) {
    assert.ok(!Object.hasOwn(input,'execution'),'QA_EVIDENCE_EXECUTION_FORBIDDEN')
    assert.ok(typeof input.path==='string'&&/^[CDE]:\//.test(input.path)&&!input.path.split('/').includes('..')
      &&/\.(mjs|ps1|py|json|log)$/.test(input.path),'QA_EVIDENCE_INPUT_PATH_INVALID')
    assert.ok(!evidenceInputs.has(input.path)&&!executableInputs.has(input.path),'QA_INPUT_CLASS_OVERLAP');evidenceInputs.add(input.path)
    assert.match(input.sha256,/^[A-F0-9]{64}$/);assert.ok(typeof input.role==='string'&&input.role.length>=8)
    if(verifyLocalInputs)assert.equal(crypto.createHash('sha256').update(read(input.path)).digest('hex').toUpperCase(),input.sha256,'PINNED_QA_EVIDENCE_DRIFT')
  }
  assert.equal(manifest.completeLegacyCodeClosure,executableInputs.size===0,'QA_LEGACY_CLOSURE_CLAIM_INVALID')
  return {status:'passed',sourceCount:seen.size,relativeImportTargets:importTargets.size,
    sources:hashes,legacyExecutableInputs:executableInputs.size,immutableEvidenceInputs:evidenceInputs.size,
    immutableInputsVerified:verifyLocalInputs?executableInputs.size+evidenceInputs.size:0,
    completeLegacyCodeClosure:manifest.completeLegacyCodeClosure,
    stagedSourceBound:Boolean(sourceMirror),networkCalls:0,remoteExecutionAuthorized:false,onlineCapacityAccepted:false}
}

export function readQaOwnership({verifyLocalInputs=false}={}) {
  const mirrorRoot=process.env.L12_QA_SOURCE_REPO,mirrorCommit=process.env.L12_QA_SOURCE_COMMIT
  assert.equal(Boolean(mirrorRoot),Boolean(mirrorCommit),'QA_STAGED_SOURCE_BINDING_INCOMPLETE')
  const sourceRoot=mirrorRoot??QA_REPO
  assert.ok(path.isAbsolute(sourceRoot),'QA_STAGED_SOURCE_ROOT_INVALID')
  const git=args=>execFileSync('git',['-C',sourceRoot,...args],{encoding:'utf8',windowsHide:true,maxBuffer:4*1024*1024})
  const sourceMirror=mirrorRoot?{root:sourceRoot,commit:mirrorCommit,head:git(['rev-parse','HEAD']).trim(),
    clean:git(['status','--porcelain=v1','--untracked-files=all']).trim()===''}:undefined
  const manifest=JSON.parse(fs.readFileSync(path.join(QA_REPO,QA_MANIFEST),'utf8'))
  const code=relative=>fs.readFileSync(path.join(QA_REPO,relative),'utf8')
  const exact=(source,pattern)=>{const matches=[...source.matchAll(pattern)];assert.equal(matches.length,1,'QA_PIN_ANCHOR_CHANGED');return matches[0][1]}
  const protocol=code('opcgpro-vue/scripts/testrun-pressure-protocol.mjs')
  const runtime=code('opcgpro-vue/scripts/testrun-pressure-runtime.mjs')
  const execution=code('opcgpro-vue/scripts/testrun-pressure-execution.mjs')
  const qualification=code('opcgpro-vue/scripts/qualify-normal-testrun-lifecycle.mjs')
  const frozenRelative='ops/qa/testrun-frozen-resource-guard.py'
  assert.ok(manifest.sources.includes(frozenRelative),'QA_FROZEN_GUARD_NOT_OWNED')
  const repositoryGuard="path.join(REPO,'ops/qa/testrun-frozen-resource-guard.py')"
  assert.ok(execution.includes('export const FROZEN_GUARD='+repositoryGuard),'QA_EXECUTION_GUARD_NOT_REPOSITORY_BOUND')
  assert.ok(qualification.includes('const FROZEN_GUARD='+repositoryGuard),'QA_QUALIFICATION_GUARD_NOT_REPOSITORY_BOUND')
  const normal=fs.readFileSync(path.join(QA_REPO,'ops/qa/testrun-normal-resource-guard.py'))
  assert.ok(normal.toString('utf8').includes('LEGACY_PATH = str(pathlib.Path(__file__).with_name("testrun-frozen-resource-guard.py").resolve())'),'QA_DEFAULT_GUARD_NOT_REPOSITORY_BOUND')
  const guardPins=inspectOwnedGuardPins({frozen:fs.readFileSync(path.join(QA_REPO,frozenRelative)),normal,pressureRuntime:runtime,
    terminalRuntime:code('opcgpro-vue/scripts/terminal-recipient-runtime.mjs'),
    terminalGuard:code('ops/qa/testrun-terminal-resource-guard.py'),qualification})
  assert.ok(manifest.immutableEvidenceInputs.some(input=>input.sha256===guardPins.frozenSha256),'QA_ORIGINAL_GUARD_EVIDENCE_MISSING')
  const ledgerFiles=['account-ledger-sink-r2.ps1','account-ledger-client-r2.mjs','AccountLedgerReader.ps1']
  for(const name of ledgerFiles)assert.ok(manifest.sources.includes('ops/qa/ledger/'+name),'QA_LEDGER_SOURCE_NOT_OWNED')
  const ledgerPins=inspectOwnedLedgerPins({
    sink:fs.readFileSync(path.join(QA_REPO,'ops/qa/ledger/'+ledgerFiles[0])),
    client:fs.readFileSync(path.join(QA_REPO,'ops/qa/ledger/'+ledgerFiles[1])),
    reader:fs.readFileSync(path.join(QA_REPO,'ops/qa/ledger/'+ledgerFiles[2])),execution,
    recovery:code('opcgpro-vue/scripts/testrun-pressure-recovery.mjs'),
    localQualifier:code('opcgpro-vue/scripts/qualify-local-pressure.mjs'),
    normalReader:code('ops/qa/Read-TestrunPressureLedgers.ps1'),
    localReader:code('ops/qa/Verify-LocalPressureLedgers.ps1'),
    executionTest:code('opcgpro-vue/scripts/test-testrun-pressure-execution.mjs'),
  })
  for(const [name,sha256]of [[ledgerFiles[0],ledgerPins.sinkSha256],[ledgerFiles[1],ledgerPins.clientSha256],
    ['test-account-ledger-real-r4.ps1','C991435C5E18FC8A26D9F57264E25BC38F4D1B4D4CC79DAF395D1C0EFCC6BECE']]){
    const original=manifest.immutableEvidenceInputs.find(input=>input.path.endsWith('/ledger-r5/'+name)&&input.sha256===sha256)
    assert.ok(original&&!Object.hasOwn(original,'execution'),'QA_ORIGINAL_LEDGER_EVIDENCE_MISSING')
    assert.ok(!manifest.legacyExecutableInputs.some(input=>input.path===original.path),'QA_ORIGINAL_LEDGER_STILL_EXECUTED')
  }
  const expected=[
    [exact(protocol,/export const FROZEN_CORE = '([^']+)'/g),exact(protocol,/export const FROZEN_SHA = '([A-F0-9]{64})'/g)],
  ]
  const declared=new Map(manifest.legacyExecutableInputs.map(value=>[value.path,value.sha256]))
  for(const [file,sha256]of expected)assert.equal(declared.get(file),sha256,'QA_DECLARED_EXECUTABLE_DIFFERS_FROM_RUNTIME_PIN: '+file)
  const names=git(['-c','core.quotepath=false','ls-files'])
  const tracked=new Set(names.trim().split(/\r?\n/))
  assert.ok(tracked.has(QA_MANIFEST),'QA_MANIFEST_NOT_VERSIONED')
  return {...inspectQaOwnership(manifest,{tracked,verifyLocalInputs,sourceMirror}),ownedGuardPins:guardPins,ownedLedgerPins:ledgerPins}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
  assert.ok(process.argv.length===2||(process.argv.length===3&&process.argv[2]==='--verify-local-inputs'),'Only optional local immutable-input verification is supported')
  const result=readQaOwnership({verifyLocalInputs:process.argv[2]==='--verify-local-inputs'})
  console.log(JSON.stringify({...result,sources:undefined}))
}
