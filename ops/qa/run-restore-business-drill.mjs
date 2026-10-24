import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {execFileSync} from 'node:child_process'
import {fileURLToPath} from 'node:url'
import {REPO, digest} from '../../opcgpro-vue/scripts/testrun-pressure-protocol.mjs'
import {remoteJson, IDENTITY_CODE, durableJson} from '../../opcgpro-vue/scripts/testrun-pressure-runtime.mjs'

const COMMIT='3a376f17e5a6f9431303b35775977fabd2083b6a'
const DLL='BAEFF29E79B3DE4AF855151D166ABD89EB415D3426D40762E1860A8DA5FFF171'
const PACKAGE='69C8E3F81E599D4135DC308D13D510ADF975C0577E400A654CC6E0C30C4ECE30'
const outputRoot='D:/GPT/Legion12/artifacts/restore-business-20261024'
const cache='D:/GPT/Legion12/cache/primary/t/rb24'
const archive='D:/GPT/Legion12/artifacts/stage-goals-20261024/property-frame/package/'+COMMIT+'/l12-release-'+COMMIT+'.tar.gz'
const imageRoot='D:/GPT/Legion12/cache/primary/trusted-input-web-20261024/full-origin'
const imagePath='cards/20260912-batch308/S01-0001/929104a5b67079c1ebfe/thumb-240.webp'
const imageSha='c7ba8947adf8c4987c53c40b34ecc9048fc767411f043492fce0f9fe56cb5cb2'

export function normalizeSha256(value){
  assert.equal(typeof value,'string','SHA256_STRING_REQUIRED')
  assert.match(value,/^[0-9a-fA-F]{64}$/,'EXACT_SHA256_REQUIRED')
  return value.toUpperCase()
}
export function bindCurrentServices(binding,summary){
  assert.equal(summary.completed,true);assert.equal(summary.applicationCommit,COMMIT)
  assert.equal(normalizeSha256(summary.dllSha256),DLL)
  assert.equal(binding.test.commit,COMMIT);assert.equal(normalizeSha256(binding.test.dllSha256),DLL)
  assert.equal(summary.test.pid,binding.test.pid)
  for(const value of [summary.formal.pid,summary.test.pid])assert(Number.isSafeInteger(value)&&value>0,'EXACT_LIVE_PID_REQUIRED')
  assert.notEqual(summary.formal.pid,summary.test.pid)
  return {...binding,test:{...binding.test,dllSha256:DLL},formal:{commit:COMMIT,pid:summary.formal.pid,dllSha256:DLL}}
}

export async function runBusinessRestore(){
  assert.equal(process.platform,'win32','WINDOWS_LOCAL_ORCHESTRATOR_ONLY')
  const pwsh=process.env.L12_PWSH
  assert(pwsh&&path.isAbsolute(pwsh),'INITIALIZE_HOST_FIRST')
  execFileSync(pwsh,['-NoProfile','-NonInteractive','-Command',
    '. $env:L12_RESTORE_GUARD; foreach($p in @($env:L12_RESTORE_EVIDENCE,$env:L12_RESTORE_CACHE)){$null=Assert-L12GeneratedPath -Path $p -Purpose "nonempty synthetic restore"}'],
    {env:{...process.env,L12_RESTORE_GUARD:path.join(REPO,'ops/windows/L12GeneratedStorage.ps1'),L12_RESTORE_EVIDENCE:outputRoot,L12_RESTORE_CACHE:cache},windowsHide:true,timeout:15000})
  fs.mkdirSync(outputRoot,{recursive:true})
  const hasher=crypto.createHash('sha256')
  const archiveStat=fs.lstatSync(archive)
  assert(archiveStat.isFile()&&!archiveStat.isSymbolicLink()&&archiveStat.size<=150*1024*1024)
  for await(const chunk of fs.createReadStream(archive))hasher.update(chunk)
  assert.equal(hasher.digest('hex').toUpperCase(),PACKAGE,'ACCEPTED_ARCHIVE_CHANGED')
  const python=process.platform==='win32'?'E:/Tran/Miniconda3/python.exe':'python3'
  const manifest=JSON.parse(execFileSync(python,['-I','-B','-c',String.raw`
import hashlib,json,sys,tarfile
from pathlib import PurePosixPath
result={};total=0
with tarfile.open(sys.argv[1],'r:gz') as archive:
 for member in archive:
  p=PurePosixPath(member.name)
  if not p.parts or p.parts[0]!='publish':continue
  assert not p.is_absolute() and '..' not in p.parts
  if member.isdir():continue
  assert member.isfile() and len(p.parts)>1
  name='/'.join(p.parts[1:]);assert name not in result and 0<=member.size<=128*1024**2
  total+=member.size;assert total<=128*1024**2 and len(result)<1000
  with archive.extractfile(member) as stream:raw=stream.read(member.size+1)
  assert len(raw)==member.size
  result[name]={'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest()}
print(json.dumps(result,separators=(',',':')))
`,archive],{encoding:'utf8',windowsHide:true,timeout:30000,maxBuffer:262144}))
  assert.equal(manifest['GrandUMIServer.dll'].sha256.toUpperCase(),DLL)
  const image=fs.readFileSync(path.join(imageRoot,imagePath))
  assert.equal(image.length,20442);assert.equal(digest(image).toLowerCase(),imageSha)
  const summary=JSON.parse(fs.readFileSync('D:/GPT/Legion12/artifacts/formal-release-20261024/release-session-summary.json','utf8'))
  const binding=bindCurrentServices(JSON.parse(fs.readFileSync('D:/GPT/Legion12/artifacts/stage-goals-20261024/property-frame/normal-load-binding.json','utf8')),summary)
  const runId=crypto.randomUUID().replaceAll('-',''),destination=path.join(outputRoot,'restore-'+runId+'.json')
  assert.equal(fs.existsSync(destination),false)
  const tools={}
  for(const name of ['RestoreBusinessFixture.dll','RestoreBusinessFixture.deps.json','RestoreBusinessFixture.runtimeconfig.json']){
    const raw=fs.readFileSync(path.join(cache,'build/bin/RestoreBusinessFixture/release',name))
    assert(raw.length>0&&raw.length<=256*1024)
    tools[name]={sha256:digest(raw),base64:raw.toString('base64')}
  }
  const factsSource=fs.readFileSync(path.join(REPO,'ops/qa/restore-business-facts.py'))
  const controller=fs.readFileSync(path.join(REPO,'ops/qa/restore-business-drill.py'))
  const verifier=fs.readFileSync(path.join(REPO,'ops/server/verify-l12-runtime-backup.py'))
  assert.equal(digest(verifier),'FC37461942E342477EBC2708A0C6EA92F06FDE13063366847D2563FDBC52F6F0')
  const before=await remoteJson(IDENTITY_CODE+'\nprint(json.dumps(result,separators=(",",":")))',binding)
  const result=await remoteJson(String.raw`
import base64,hashlib,json,types
raw=base64.b64decode(p['controller'],validate=True)
assert hashlib.sha256(raw).hexdigest().upper()==p['controllerSha256']
module=types.ModuleType('owned_nonempty_restore_controller')
exec(compile(raw,'<owned-nonempty-restore-controller>','exec'),module.__dict__)
print(json.dumps(module.run(p),separators=(',',':')))
`,{runId,programManifest:manifest,tools,image:image.toString('base64'),imageSha256:imageSha,
    factsSource:factsSource.toString('base64'),factsSourceSha256:digest(factsSource),
    controller:controller.toString('base64'),controllerSha256:digest(controller),verifier:verifier.toString('base64')},
  {timeoutMs:50000})
  const after=await remoteJson(IDENTITY_CODE+'\nprint(json.dumps(result,separators=(",",":")))',binding)
  const proof={...result,runId,controllerSha256:digest(controller),factsSourceSha256:digest(factsSource),
    fixtureSourceSha256:digest(fs.readFileSync(path.join(REPO,'ops/qa/RestoreBusinessFixture/Program.cs'))),
    compiledFixtureSha256:tools['RestoreBusinessFixture.dll'].sha256,
    acceptedPackageSha256:PACKAGE,pinnedProgramFiles:Object.keys(manifest).length,
    liveIdentityBeforeAndAfterEqual:JSON.stringify(before)===JSON.stringify(after),
    originalReadOnlyProgramAndPublicImageInputs:true,productionSyntheticBusinessEvents:0,maintenanceWrites:0}
  durableJson(destination,proof)
  console.log(JSON.stringify(proof))
  assert.deepEqual(after,before,'LIVE_IDENTITIES_CHANGED')
  assert.equal(result.status,'NONEMPTY_SYNTHETIC_APPLICATION_RESTORE_VERIFIED','PRESERVE_PRIVATE_FAILED_FIXTURE')
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  assert.deepEqual(process.argv.slice(2),['--run'])
  await runBusinessRestore()
}
