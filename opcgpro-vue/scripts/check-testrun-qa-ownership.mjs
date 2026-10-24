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
  const ledger=exact(execution,/const LEDGER='([^']+)'/g)
  const expected=[
    [exact(protocol,/export const FROZEN_CORE = '([^']+)'/g),exact(protocol,/export const FROZEN_SHA = '([A-F0-9]{64})'/g)],
    [exact(execution,/export const FROZEN_GUARD='([^']+)'/g),exact(runtime,/export const FROZEN_GUARD_SHA='([A-F0-9]{64})'/g)],
    [ledger+'/account-ledger-sink-r2.ps1',exact(execution,/const SINK_SHA='([A-F0-9]{64})'/g)],
    [ledger+'/account-ledger-client-r2.mjs',exact(execution,/const CLIENT_SHA='([A-F0-9]{64})'/g)],
  ]
  const declared=new Map(manifest.legacyExecutableInputs.map(value=>[value.path,value.sha256]))
  for(const [file,sha256]of expected)assert.equal(declared.get(file),sha256,'QA_DECLARED_EXECUTABLE_DIFFERS_FROM_RUNTIME_PIN: '+file)
  const names=git(['-c','core.quotepath=false','ls-files'])
  const tracked=new Set(names.trim().split(/\r?\n/))
  assert.ok(tracked.has(QA_MANIFEST),'QA_MANIFEST_NOT_VERSIONED')
  return inspectQaOwnership(manifest,{tracked,verifyLocalInputs,sourceMirror})
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
  assert.ok(process.argv.length===2||(process.argv.length===3&&process.argv[2]==='--verify-local-inputs'),'Only optional local immutable-input verification is supported')
  const result=readQaOwnership({verifyLocalInputs:process.argv[2]==='--verify-local-inputs'})
  console.log(JSON.stringify({...result,sources:undefined}))
}
