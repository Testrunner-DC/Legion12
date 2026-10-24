import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {execFileSync,spawnSync} from 'node:child_process'
import ts from 'typescript'
import {inspectQaOwnership,inspectOwnedGuardPins,readQaOwnership,QA_REPO,QA_MANIFEST} from './check-testrun-qa-ownership.mjs'

const actual=readQaOwnership(),manifest=JSON.parse(fs.readFileSync(path.join(QA_REPO,QA_MANIFEST)))
let cases=1
const fixture={schema:1,formalMutationsAllowed:false,normalConfirmationMs:3000,normalLifecycleMs:900000,
  sources:['opcgpro-vue/scripts/probe.mjs'],entries:[{path:'opcgpro-vue/scripts/probe.mjs',mode:'test-only',target:'https://legion-12.com/testrun/'}],
  legacyExecutableInputs:[],immutableEvidenceInputs:[],completeLegacyCodeClosure:true}
const fakeTracked=new Set(fixture.sources)
for(const [name,mutate]of[
 ['unversioned source',value=>value.sources.push('ops/qa/missing.py')],
 ['escaping path',value=>value.sources.push('ops/qa/../secret.py')],
 ['duplicate source',value=>value.sources.push(value.sources[0])],
 ['formal mutation',value=>{value.formalMutationsAllowed=true}],
 ['changed confirmation',value=>{value.normalConfirmationMs=3001}],
 ['changed lifecycle',value=>{value.normalLifecycleMs=901000}],
 ['formal target',value=>{value.entries[0].target='https://legion-12.com/'}],
 ['unowned entry',value=>{value.entries[0].path='ops/qa/unowned.ps1'}],
 ['claiming legacy closure',value=>value.legacyExecutableInputs.push({path:'D:/legacy.mjs',sha256:'A'.repeat(64),execution:'module-import',role:'executed legacy fixture'})],
 ['bad executable hash',value=>{value.completeLegacyCodeClosure=false;value.legacyExecutableInputs.push({path:'D:/legacy.mjs',sha256:'wrong',execution:'module-import',role:'executed legacy fixture'})}],
 ['bad evidence hash',value=>value.immutableEvidenceInputs.push({path:'D:/fixture.json',sha256:'wrong',role:'immutable receipt'})],
 ['input overlap',value=>{const row={path:'D:/legacy.mjs',sha256:'A'.repeat(64),execution:'module-import',role:'fixture source'};value.completeLegacyCodeClosure=false;value.legacyExecutableInputs.push(row);value.immutableEvidenceInputs.push(row)}],
]) {
 const value=structuredClone(fixture);mutate(value)
 assert.throws(()=>inspectQaOwnership(value,{tracked:fakeTracked,read:()=>Buffer.from('export const valid=true')}),name);cases++
}
assert.throws(()=>inspectQaOwnership(fixture,{tracked:fakeTracked,read:()=>Buffer.from("import x from './untracked.mjs'")}));cases++
const privateHeader=['-----BEGIN','OPENSSH PRIVATE KEY-----'].join(' ')
assert.throws(()=>inspectQaOwnership(fixture,{tracked:fakeTracked,read:()=>Buffer.from(privateHeader)}));cases++
const pinned=structuredClone(fixture);pinned.immutableEvidenceInputs=[{path:'D:/fixture.json',sha256:crypto.createHash('sha256').update('expected').digest('hex').toUpperCase(),role:'immutable receipt'}]
assert.throws(()=>inspectQaOwnership(pinned,{tracked:fakeTracked,read:()=>Buffer.from('different'),verifyLocalInputs:true}));cases++
const declared=structuredClone(fixture);declared.completeLegacyCodeClosure=false
declared.legacyExecutableInputs=[{path:'D:/legacy.mjs',sha256:crypto.createHash('sha256').update('legacy').digest('hex').toUpperCase(),execution:'module-import',role:'executed legacy fixture'}]
assert.equal(inspectQaOwnership(declared,{tracked:fakeTracked,read:file=>Buffer.from(file==='D:/legacy.mjs'?'legacy':'export const valid=true')}).completeLegacyCodeClosure,false);cases++
assert.equal(manifest.legacyExecutableInputs.length,14);assert.equal(manifest.immutableEvidenceInputs.length,5)
assert.ok(manifest.immutableEvidenceInputs.some(row=>row.path.endsWith('/fixture-import-r2/testrun-owned-analytics-import.py')));cases++
const guardInputs={frozen:fs.readFileSync(path.join(QA_REPO,'ops/qa/testrun-frozen-resource-guard.py')),
 normal:fs.readFileSync(path.join(QA_REPO,'ops/qa/testrun-normal-resource-guard.py')),
 pressureRuntime:fs.readFileSync(path.join(QA_REPO,'opcgpro-vue/scripts/testrun-pressure-runtime.mjs'),'utf8'),
 terminalRuntime:fs.readFileSync(path.join(QA_REPO,'opcgpro-vue/scripts/terminal-recipient-runtime.mjs'),'utf8'),
 terminalGuard:fs.readFileSync(path.join(QA_REPO,'ops/qa/testrun-terminal-resource-guard.py'),'utf8'),
 qualification:fs.readFileSync(path.join(QA_REPO,'opcgpro-vue/scripts/qualify-normal-testrun-lifecycle.mjs'),'utf8')}
assert.equal(inspectOwnedGuardPins(guardInputs).normalPinConsumers,3);cases++
for(const key of ['frozen','normal']){
 const changed=Buffer.from(guardInputs[key]);changed[0]^=1
 assert.throws(()=>inspectOwnedGuardPins({...guardInputs,[key]:changed}),/QA_OWNED_.*_GUARD_DRIFT/);cases++
}
for(const [key,label]of [['pressureRuntime','FROZEN_GUARD_SHA'],['terminalRuntime','NORMAL_GUARD_SHA'],['terminalGuard','NORMAL_SHA'],['qualification','assertGuardReady']]){
 const changed=guardInputs[key].replace(new RegExp('('+label+"[^\\n]*?)[A-F0-9]{64}"),'$1'+'F'.repeat(64))
 assert.notEqual(changed,guardInputs[key]);assert.throws(()=>inspectOwnedGuardPins({...guardInputs,[key]:changed}));cases++
}
const attributes=fs.readFileSync(path.join(process.env.L12_QA_SOURCE_REPO||QA_REPO,'.gitattributes'),'utf8')
for(const file of ['testrun-frozen-resource-guard.py','testrun-normal-resource-guard.py']){
 assert.ok(attributes.split(/\r?\n/).includes('/ops/qa/'+file+' -text'))
 const bytes=fs.readFileSync(path.join(QA_REPO,'ops/qa',file));assert.ok(!bytes.includes(13)&&bytes[0]!==0xef);cases++
}
const originalGuard=manifest.immutableEvidenceInputs.find(row=>row.sha256===actual.ownedGuardPins.frozenSha256)
assert.ok(originalGuard&&!Object.hasOwn(originalGuard,'execution'))
assert.ok(!manifest.legacyExecutableInputs.some(row=>row.path===originalGuard.path));cases++
const missingEvidence=structuredClone(fixture)
missingEvidence.immutableEvidenceInputs=[originalGuard]
const withoutOld=file=>{if(file===originalGuard.path)throw new Error('HISTORICAL_GUARD_UNAVAILABLE');return Buffer.from('export const valid=true')}
assert.equal(inspectQaOwnership(missingEvidence,{tracked:fakeTracked,read:withoutOld}).status,'passed');cases++
assert.throws(()=>inspectQaOwnership(missingEvidence,{tracked:fakeTracked,read:withoutOld,verifyLocalInputs:true}),/HISTORICAL_GUARD_UNAVAILABLE/);cases++
const mislabeled=structuredClone(missingEvidence);mislabeled.immutableEvidenceInputs[0].execution='compiled-exec'
assert.throws(()=>inspectQaOwnership(mislabeled,{tracked:fakeTracked,read:withoutOld}),/QA_EVIDENCE_EXECUTION_FORBIDDEN/);cases++
// Execute the actual Python default loader while denying all reads of its
// former artifact input. No Linux services, network, or generated files exist.
const defaultLoader=spawnSync(process.env.L12_PYTHON||'python',['-I','-B','-c',String.raw`
import hashlib,json,os,pathlib,sys,types
root=pathlib.Path(sys.argv[1]);old=sys.argv[2]
def deny_old(event,args):
 if event=='open' and args and isinstance(args[0],(str,bytes,os.PathLike)) and os.fsdecode(args[0]).replace('\\','/')==old:
  raise PermissionError('HISTORICAL_GUARD_UNAVAILABLE')
sys.addaudithook(deny_old)
source=root/'ops/qa/testrun-normal-resource-guard.py'
normal=types.ModuleType('owned_guard_loader_test');normal.__file__=str(source)
exec(compile(source.read_bytes(),str(source),'exec'),normal.__dict__)
assert isinstance(normal.LEGACY_PATH,str)
assert normal.LEGACY_PATH==str((source.parent/'testrun-frozen-resource-guard.py').resolve())
legacy=normal.load_frozen_guard()
assert legacy.EXPECTED_HOST=='ser550468033481' and legacy.ROUND_BUDGET_SECONDS==3.75
print(json.dumps({'loadedRepositoryGuard':True,'historicalArtifactRead':False}))
`,QA_REPO,originalGuard.path],{encoding:'utf8',windowsHide:true,timeout:15000})
assert.equal(defaultLoader.status,0,defaultLoader.stderr);assert.equal(JSON.parse(defaultLoader.stdout).historicalArtifactRead,false);cases++
const fixtureGenerator=fs.readFileSync(path.join(QA_REPO,'ops/qa/TestAnalyticsHistory/Program.cs'),'utf8')
assert.match(fixtureGenerator,/args\.Length != 4/)
assert.match(fixtureGenerator,/deployment-commit"\)\.Trim\(\) != expectedCommit/)
assert.match(fixtureGenerator,/Assembly\.Location[\s\S]*?!= expectedSha/)
assert.match(fixtureGenerator,/Path\.Combine\("\/tmp", "l12-analytics-fixture-"/)
assert.match(fixtureGenerator,/Existing fixture is never reused/)
assert.doesNotMatch(fixtureGenerator,/BA157FD1|131989a65/)
cases++

// The standard release build has no .git of its own. It must bind its exact
// copied sources to a separately verified clean commit, not waive ownership.
const stagedRepo=path.resolve(QA_REPO,'synthetic-staged-qa'),sourceRoot=path.resolve(QA_REPO,'synthetic-source-qa')
const sourceMirror={root:sourceRoot,commit:'a'.repeat(40),head:'a'.repeat(40),clean:true}
const mirrorRead=file=>Buffer.from(file.endsWith(QA_MANIFEST)?JSON.stringify(fixture):'export const valid=true')
assert.equal(inspectQaOwnership(fixture,{repo:stagedRepo,tracked:fakeTracked,sourceMirror,read:mirrorRead}).stagedSourceBound,true);cases++
for(const changes of [{root:'relative'},{root:stagedRepo},{commit:'wrong'},{head:'b'.repeat(40)},{clean:false}]){
 assert.throws(()=>inspectQaOwnership(fixture,{repo:stagedRepo,tracked:fakeTracked,sourceMirror:{...sourceMirror,...changes},read:mirrorRead}));cases++
}
for(const target of [QA_MANIFEST,fixture.sources[0]]){
 assert.throws(()=>inspectQaOwnership(fixture,{repo:stagedRepo,tracked:fakeTracked,sourceMirror,
  read:file=>file===path.join(sourceRoot,target)?Buffer.from('different'):mirrorRead(file)}));cases++
}
const imported='opcgpro-vue/scripts/imported.mjs'
assert.throws(()=>inspectQaOwnership(fixture,{repo:stagedRepo,tracked:new Set([...fakeTracked,imported]),sourceMirror,
 read:file=>file.endsWith(QA_MANIFEST)?Buffer.from(JSON.stringify(fixture))
  :file.endsWith('probe.mjs')?Buffer.from("import './imported.mjs'")
  :Buffer.from(file.startsWith(sourceRoot)?'different':'export const valid=true')}));cases++
const releaseEntry=fs.readFileSync(path.join(QA_REPO,'ops/windows/verify-l12.ps1'),'utf8')
assert.match(releaseEntry,/\$qaSources\.schema -ne 1/)
assert.match(releaseEntry,/foreach \(\$relative in @\(\$qaSources\.sources\) \+ @\('ops\/qa\/toolchain-sources\.json'\)\)/)
assert.match(releaseEntry,/\$env:L12_QA_SOURCE_REPO = \$repoRoot/)
assert.match(releaseEntry,/\$env:L12_QA_SOURCE_COMMIT = \$commit/)
assert.match(releaseEntry,/finally \{[\s\S]*\$env:L12_QA_SOURCE_REPO = \$previousQaSourceRepo[\s\S]*\$env:L12_QA_SOURCE_COMMIT = \$previousQaSourceCommit/);cases++

// Execute the actual pre-browser initialization blocks with a guard stub. A
// child's PowerShell environment must never be mistaken for its parent Node's.
for(const [relative,start,end]of[
 ['opcgpro-vue/scripts/qa-tutorial-candidate-images-20261023.mjs','const out = process.env.L12_TUTORIAL_UI_OUTPUT','const report = {'],
 ['opcgpro-vue/scripts/test-master-art-editor-grant-refresh-20261023.mjs','  const out = process.env.L12_QA_OUT','  const expectedDll ='],
]) {
 const source=fs.readFileSync(path.join(QA_REPO,relative),'utf8'),a=source.indexOf(start),b=source.indexOf(end,a)
 assert.ok(a>=0&&b>a)
 const execute=new Function('process','fs','path','assert','repo','execFileSync',source.slice(a,b))
 const cache=path.resolve('synthetic-governed-cache'),out=path.resolve('synthetic-evidence'),host=path.resolve('synthetic-host')
 const make=()=>({env:{TEMP:'C:/unsafe-temp',TMP:'C:/unsafe-temp',TMPDIR:'C:/unsafe-temp',
   L12_QA_OUT:out,L12_WORK_CACHE:cache,L12_MASTER_HOST:host,L12_MASTER_ART_ASSETS:path.resolve('synthetic-assets'),
   L12_TUTORIAL_UI_OUTPUT:out,L12_TUTORIAL_UI_CACHE:cache}})
 let writes=0,guards=0;const process=make()
 const io={existsSync:()=>false,readdirSync:()=>[],mkdirSync(){writes++;for(const key of ['TEMP','TMP','TMPDIR'])assert.equal(process.env[key],cache)}}
 execute(process,io,path,assert,QA_REPO,()=>{guards++;return Buffer.alloc(0)})
 assert.equal(guards,1);assert.equal(writes,2);cases++
 const refused=make();writes=0
 assert.throws(()=>execute(refused,io,path,assert,QA_REPO,()=>{throw new Error('PHYSICAL_ROOT_REFUSED')}),/PHYSICAL_ROOT_REFUSED/)
 assert.equal(writes,0);assert.equal(refused.env.TEMP,'C:/unsafe-temp');cases++
}
const args=process.argv.slice(2)
assert.ok(args.length===0||(args.length===2&&args[0]==='--stage-probe'))
let stagingProbe=null
if(args.length){
 const root=args[1];assert.ok(path.isAbsolute(root)&&!fs.existsSync(root),'STAGING_PROBE_REQUIRES_NEW_ABSOLUTE_ROOT')
 const dependencyRoot=fs.realpathSync(path.join(QA_REPO,'opcgpro-vue/node_modules'))
 execFileSync(process.env.L12_PWSH||'pwsh',['-NoProfile','-Command',
  '. $env:L12_PROBE_STORAGE; $null=Assert-L12GeneratedPath -Path $env:L12_PROBE_ROOT -Purpose "QA staging probe"; $null=Assert-L12GeneratedPath -Path $env:L12_PROBE_DEPENDENCIES -Purpose "shared QA dependencies"'],
  {env:{...process.env,L12_PROBE_STORAGE:path.join(QA_REPO,'ops/windows/L12GeneratedStorage.ps1'),L12_PROBE_ROOT:root,L12_PROBE_DEPENDENCIES:dependencyRoot},windowsHide:true})
 const sourceHead=execFileSync('git',['-C',QA_REPO,'rev-parse','HEAD'],{encoding:'utf8',windowsHide:true}).trim()
 assert.equal(execFileSync('git',['-C',QA_REPO,'status','--porcelain=v1','--untracked-files=all'],{encoding:'utf8',windowsHide:true}).trim(),'','STAGING_PROBE_SOURCE_MUST_BE_COMMITTED_AND_CLEAN')
 const paths=new Set([...manifest.sources,QA_MANIFEST])
 for(const relative of manifest.sources)if(relative.endsWith('.mjs')){
  const ast=ts.createSourceFile(relative,fs.readFileSync(path.join(QA_REPO,relative),'utf8'),ts.ScriptTarget.Latest,true,ts.ScriptKind.JS)
  for(const node of ast.statements)if(ts.isImportDeclaration(node)&&ts.isStringLiteral(node.moduleSpecifier)&&node.moduleSpecifier.text.startsWith('.'))
   paths.add(path.relative(QA_REPO,path.resolve(QA_REPO,path.dirname(relative),node.moduleSpecifier.text)))
 }
 fs.mkdirSync(root)
 const dependencyLink=path.join(root,'opcgpro-vue/node_modules')
 try{
  for(const relative of paths){const target=path.join(root,relative);fs.mkdirSync(path.dirname(target),{recursive:true});fs.copyFileSync(path.join(QA_REPO,relative),target)}
  fs.symlinkSync(dependencyRoot,dependencyLink,process.platform==='win32'?'junction':'dir')
  const entry=path.join(root,'opcgpro-vue/scripts/check-testrun-qa-ownership.mjs'),currentBytes=fs.readFileSync(entry)
  const env={...process.env,L12_QA_SOURCE_REPO:QA_REPO,L12_QA_SOURCE_COMMIT:sourceHead}
  const child=()=>spawnSync(process.execPath,[entry],{encoding:'utf8',env,windowsHide:true,timeout:15000})
  const noGit=spawnSync('git',['-C',root,'rev-parse','--show-toplevel'],{encoding:'utf8',windowsHide:true,timeout:15000})
  assert.notEqual(noGit.status,0);assert.match(noGit.stderr,/not a git repository/)
  fs.writeFileSync(entry,execFileSync('git',['-C',QA_REPO,'show','7a4217aed105fbc174eb8d70f244fafa833efb98:opcgpro-vue/scripts/check-testrun-qa-ownership.mjs'],{windowsHide:true}))
  // The historical checker now stops earlier: it only recognizes the former
  // literal artifact path. Keep that exact failure separate from the explicit
  // no-Git probe above; do not claim its cause is a Git lookup it never reached.
  const red=child();assert.notEqual(red.status,0);assert.match(red.stderr,/QA_PIN_ANCHOR_CHANGED/)
  fs.writeFileSync(entry,currentBytes)
  const green=child();assert.equal(green.status,0,green.stderr)
  const accepted=JSON.parse(green.stdout);assert.equal(accepted.stagedSourceBound,true);assert.equal(accepted.sourceCount,actual.sourceCount)
  const changed=path.join(root,'opcgpro-vue/scripts/run-testrun-representative-pressure.mjs')
  fs.appendFileSync(changed,'\n// synthetic changed staged source\n')
  const corrupted=child();assert.notEqual(corrupted.status,0);assert.match(corrupted.stderr,/QA_STAGED_SOURCE_DIFFERS/)
  stagingProbe={stagedRootHasNoGit:true,oldEntryRejectedWithCurrentDependency:true,oldEntryFirstRejection:'QA_PIN_ANCHOR_CHANGED',currentBoundEntryPassed:true,changedCopyRejected:true,sourceHead,copiedFiles:paths.size,networkCalls:0,cleanup:'pending'}
 }finally{
  if(fs.existsSync(dependencyLink)){assert.ok(fs.lstatSync(dependencyLink).isSymbolicLink());if(process.platform==='win32')fs.rmdirSync(dependencyLink);else fs.unlinkSync(dependencyLink)}
  // Only the exact new fixture root and the files copied above are reclaimed.
  const reclaim=directory=>{for(const item of fs.readdirSync(directory,{withFileTypes:true})){const selected=path.join(directory,item.name);assert.ok(!fs.lstatSync(selected).isSymbolicLink());if(item.isDirectory())reclaim(selected);else fs.unlinkSync(selected)}fs.rmdirSync(directory)}
  reclaim(root);if(stagingProbe)stagingProbe.cleanup='removed-exact-owned-fixture'
 }
}
console.log(JSON.stringify({status:'passed',cases,ownedSources:actual.sourceCount,actualNodeEntryDataflow:true,stagingProbe,networkCalls:0,onlineCapacityAccepted:false}))
