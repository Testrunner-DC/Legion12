import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {execFileSync,spawn} from 'node:child_process'
import {fileURLToPath} from 'node:url'
import {REPO,strictSshArguments,digest} from '../../opcgpro-vue/scripts/testrun-pressure-protocol.mjs'
import {remoteJson,IDENTITY_CODE,durableJson} from '../../opcgpro-vue/scripts/testrun-pressure-runtime.mjs'

const root='D:/GPT/Legion12/artifacts/stage-goals-20261024/restore-startup'
const remote='/tmp/l12-restore-start-20261024-ba083305-r3'
const archive='E:/Legion12-ColdStorage/server-backups/stage-goals-20261023-2eda18c1/runtime-before-cd80bc08ca7e-20261019T131122Z.tar.gz'
const archiveSha='D12D8C9B51FC28FD9D21A43CFB101051880CE8929242FEF349AAFFD4D5981956'
const owner={schema:1,purpose:'isolated-startup-restore',application:'3a376f17e5a6f9431303b35775977fabd2083b6a',archive:archiveSha}
export async function runStartupRestore(){
  execFileSync('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
    ['-NoProfile','-Command',`. '${REPO}/ops/windows/L12GeneratedStorage.ps1'; Assert-L12GeneratedPath -Path '${root}' -Purpose 'isolated application restore evidence'; Assert-L12GeneratedPath -Path 'E:/Legion12-ColdStorage/server-backups/stage-goals-20261023-2eda18c1' -Purpose 'existing private cold source'`],{windowsHide:true,stdio:'pipe'})
  const output=root+'/startup-restore-r3.json'
  assert.equal(fs.existsSync(output),false,'PRESERVE_EXISTING_DRILL')
  assert.equal(digest(fs.readFileSync(archive)),archiveSha)
  const binding=JSON.parse(fs.readFileSync('D:/GPT/Legion12/artifacts/stage-goals-20261024/property-frame/normal-load-binding.json'))
  const before=await remoteJson(IDENTITY_CODE+String.raw`
target=pathlib.Path('/tmp/l12-restore-start-20261024-ba083305-r3')
assert not target.exists() and target.parent==pathlib.Path('/tmp')
target.mkdir(mode=0o700)
with (target/'owner.json').open('x') as stream:json.dump(p['owner'],stream)
print(json.dumps(result,separators=(',',':')))
`,{...binding,owner},{timeoutMs:20000})
  durableJson(root+'/identity-and-owned-root-r3.json',before)
  const ssh=strictSshArguments(),destination=ssh.pop()
  const transfer=await new Promise((resolve,reject)=>{
    const child=spawn('C:/Windows/System32/OpenSSH/scp.exe',['-q',...ssh,archive,destination+':'+remote+'/source.tar.gz'],{windowsHide:true,stdio:['ignore','pipe','pipe']})
    let size=0;child.stdout.on('data',chunk=>{size+=chunk.length});child.stderr.on('data',chunk=>{size+=chunk.length})
    const timer=setTimeout(()=>child.kill(),60000)
    child.on('error',reject);child.on('close',code=>{clearTimeout(timer);resolve({code,diagnosticBytes:size})})
  })
  assert.equal(transfer.code,0,'PRIVATE_ARCHIVE_TRANSFER_FAILED')
  const source=fs.readFileSync(path.join(REPO,'ops/qa/restore-startup-drill.py'),'utf8')
  const verifier=fs.readFileSync(path.join(REPO,'ops/server/verify-l12-runtime-backup.py'))
  assert.equal(digest(verifier),'FC37461942E342477EBC2708A0C6EA92F06FDE13063366847D2563FDBC52F6F0')
  const result=await remoteJson(source+'\nprint(json.dumps(run(p),separators=(",",":")))',
    {verifier:verifier.toString('base64')},{timeoutMs:50000})
  const after=await remoteJson(IDENTITY_CODE+'\nprint(json.dumps(result,separators=(",",":")))',binding,{timeoutMs:20000})
  assert.deepEqual(after,before)
  assert.equal(digest(fs.readFileSync(archive)),archiveSha)
  const proof={...result,sourceQaSha256:digest(Buffer.from(source)),orchestratorSha256:digest(fs.readFileSync(import.meta.filename)),
    originalEArchiveStillMatches:true,liveIdentityBeforeAndAfterEqual:true,formalWrites:0,serviceChanges:0,maintenanceWrites:0}
  durableJson(output,proof)
  console.log(JSON.stringify(proof))
  assert.equal(proof.status,'ISOLATED_APPLICATION_STARTUP_RESTORE_VERIFIED','PRESERVE_FAILED_PRIVATE_COPY_AND_INSPECT')
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  assert.deepEqual(process.argv.slice(2),['--run'])
  await runStartupRestore()
}
