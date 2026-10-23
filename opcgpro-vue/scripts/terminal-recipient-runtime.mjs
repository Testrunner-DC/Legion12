import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {digest,REPO} from './testrun-pressure-protocol.mjs'
import {IDENTITY_CODE,remoteJson,startTerminalGuard,terminalDiagnosticProfile,HOST,FROZEN_GUARD_SHA} from './testrun-pressure-runtime.mjs'
import {openTerminalDiagnosticControl} from './testrun-pressure-control.mjs'
export {bindOwnedControl,testRequest,durableJson,pause,publicFailure} from './testrun-pressure-runtime.mjs'
export {remoteJson}
const NORMAL_GUARD_SHA='2CD7EBC3F06A4E1845537EB16A71EBB6EBF296079491494C8516097F237E86BD'
let setting=null
export function configureTerminalRuntime(value){terminalDiagnosticProfile(value);assert.equal(setting,null);setting=value}
export function terminalSetting(){assert.notEqual(setting,null,'TERMINAL_RUNTIME_NOT_CONFIGURED');return setting}
export function openTerminalControl(options){return openTerminalDiagnosticControl({...options,diagnosticSetting:terminalSetting()})}

const changes=[
 [" assert not env.get(b'L12_TESTRUN_TRANSPORT_DIAGNOSTICS')", " assert env.get(b'L12_TESTRUN_TRANSPORT_DIAGNOSTICS',b'')==(p['diagnosticSetting'].encode() if name=='test' else b'')"],
 ["'diagnosticsOff':True,'restartCount'", "'diagnosticsOff':name!='test','diagnosticSetting':p['diagnosticSetting'] if name=='test' else '','restartCount'"],
]
let source=IDENTITY_CODE
for(const [old,replacement]of changes){assert.equal(source.split(old).length,2);source=source.replace(old,replacement)}
let reverse=source
for(const [old,replacement]of changes.toReversed())reverse=reverse.replace(replacement,old)
assert.equal(reverse,IDENTITY_CODE)
export const TERMINAL_IDENTITY_CODE=source
export const identityDerivation={exactReplacements:2,reverseExact:true,normalSourceSha256:digest(IDENTITY_CODE),diagnosticSourceSha256:digest(source)}
// The shared exact-owned Journal query deliberately imports normal/OFF
// identity. Adapt only that complete verified prefix for this separate profile;
// never relax its read-only database, alias, ownership or request predicates.
export function terminalJournalRemote(code,parameters,options){
 assert.equal(typeof code,'string');assert.ok(code.startsWith(IDENTITY_CODE),'EXACT_NORMAL_JOURNAL_IDENTITY_PREFIX_REQUIRED')
 return remoteJson(source+code.slice(IDENTITY_CODE.length),{...parameters,diagnosticSetting:terminalSetting()},options)
}
export const observeTerminalIdentity=binding=>remoteJson(source+'\nprint(json.dumps(result,separators=(",",":")))',{...binding,diagnosticSetting:terminalSetting()})
export function assertTerminalIdentity(value,binding){
 for(const name of ['test','formal']){
  const item=value[name],expected=binding[name]
  for(const key of ['pid','commit','dllSha256'])assert.equal(item[key],expected[key])
  assert.ok(Number.isSafeInteger(item.pid)&&item.pid>0);assert.match(item.dllSha256,/^[A-F0-9]{64}$/)
  assert.equal(item.status,'ok');assert.equal(item.maintenance,false)
  assert.equal(item.diagnosticsOff,name==='formal');assert.equal(item.diagnosticSetting,name==='test'?terminalSetting():'')
  assert.ok(Number.isSafeInteger(item.restartCount)&&item.restartCount>=0)
 }
 assert.notEqual(value.test.pid,value.formal.pid)
 assert.deepEqual(value.resourcePolicy,{cpuWeight:10,ioWeight:10,nice:10,oomScoreAdjust:750,memoryHighBytes:768*1024**2,memoryMaxBytes:896*1024**2})
 return value
}
export async function acquireTerminalSession(binding){
 return remoteJson(source+String.raw`
import re
values=[line.split('=',1)[1].strip().strip('"').strip("'") for line in pathlib.Path('/etc/legion12-testrun.env').read_text().splitlines() if line.startswith('L12_ADMIN_PASSWORD=')]
assert len(values)==1 and re.fullmatch('[0-9a-f]{64}',values[0])
request=urllib.request.Request('http://127.0.0.1:8084/api/auth/login',data=json.dumps({'username':'Admin','password':values[0]}).encode(),headers={'Content-Type':'application/json'},method='POST')
with urllib.request.urlopen(request,timeout=10) as response:login=json.load(response)
token=login.get('token','');assert re.fullmatch('[0-9a-f]{64}',token) and login.get('account',{}).get('role')=='admin'
print(json.dumps({'token':token},separators=(',',':')))
`,{...binding,diagnosticSetting:terminalSetting()})
}
export async function installTerminalGuardFiles(runId,guardFile,frozenFile){
 assert.match(runId,/^[a-f0-9]{32}$/)
 assert.equal(path.resolve(guardFile),path.resolve(REPO,'ops/qa/testrun-terminal-resource-guard.py'))
 const files=[['terminal-guard.py',fs.readFileSync(guardFile)],['normal-guard.py',fs.readFileSync(path.join(REPO,'ops/qa/testrun-normal-resource-guard.py'))],
  ['frozen-guard.py',fs.readFileSync(frozenFile)],['diagnostic-profile.json',Buffer.from(JSON.stringify({schema:1,setting:terminalSetting()}))]]
 assert.equal(digest(files[1][1]),NORMAL_GUARD_SHA);assert.equal(digest(files[2][1]),FROZEN_GUARD_SHA)
 const payload=files.map(([name,bytes])=>{assert.ok(bytes.length<131072);return {name,sha256:digest(bytes),base64:bytes.toString('base64')}})
 const result=await remoteJson(String.raw`
import base64,hashlib,json,pathlib,os,socket
assert os.geteuid()==0 and socket.gethostname()=='ser550468033481'
assert len(p['runId'])==32 and all(c in '0123456789abcdef' for c in p['runId'])
root=pathlib.Path('/tmp/l12-normal-pressure-'+p['runId']);assert not os.path.lexists(root)
root.mkdir(mode=0o700);assert root.resolve()==root
allowed={'terminal-guard.py','normal-guard.py','frozen-guard.py','diagnostic-profile.json'}
assert len(p['files'])==4 and {item['name'] for item in p['files']}==allowed
for item in p['files']:
 data=base64.b64decode(item['base64'],validate=True);assert len(data)<131072 and hashlib.sha256(data).hexdigest().upper()==item['sha256']
 with (root/item['name']).open('xb') as out:os.fchmod(out.fileno(),0o600);out.write(data);out.flush();os.fsync(out.fileno())
print(json.dumps({'schema':1,'root':str(root),'files':[{'name':item['name'],'sha256':item['sha256']} for item in p['files']]}))
`,{runId,files:payload},{timeoutMs:30000})
 assert.equal(result.root,'/tmp/l12-normal-pressure-'+runId)
 assert.deepEqual(result.files,payload.map(({name,sha256})=>({name,sha256})))
 return {...result,sourceSha256:payload[0].sha256,profileSha256:terminalDiagnosticProfile(terminalSetting()).sha256}
}
export function startTerminalResourceGuard(binding,installed,options){return startTerminalGuard(binding,installed,{...options,diagnosticSetting:terminalSetting()})}
export async function removeTerminalGuardFiles(installed,proof){
 assert.equal(proof.transportExitCode,143);assert.equal(proof.completion.allOwnedChildrenReaped,true)
 assert.equal(proof.diagnosticOnly,true);assert.equal(proof.onlineCapacityAccepted,false)
 return remoteJson(String.raw`
import hashlib,json,pathlib,os,socket,stat
assert os.geteuid()==0 and socket.gethostname()=='ser550468033481'
root=pathlib.Path(p['installed']['root']);assert str(root).startswith('/tmp/l12-normal-pressure-') and len(root.name)==52 and root.resolve()==root
ready=p['ready'];proc=pathlib.Path('/proc/'+str(ready['supervisorPid']))
if proc.exists():assert proc.joinpath('stat').read_text().rsplit(')',1)[1].split()[19]!=str(ready['processStartTicks'])
expected={entry['name']:entry['sha256'] for entry in p['installed']['files']}
assert len(expected)==4 and {entry.name for entry in root.iterdir()}==set(expected)
for name,sha in expected.items():
 item=root/name;st=item.lstat();assert stat.S_ISREG(st.st_mode) and st.st_uid==0 and not item.is_symlink()
 assert hashlib.sha256(item.read_bytes()).hexdigest().upper()==sha
for name in expected:(root/name).unlink()
root.rmdir();print('{"schema":1,"ownedFilesRemoved":4,"ownedDirectoryRemoved":true,"serviceChanges":0}')
`,{installed,ready:proof.ready},{timeoutMs:15000})
}
export async function readTerminalServerRows(binding){
 return remoteJson(source+String.raw`
import re
drain_values=[item.split(b'=',1)[1] for item in pathlib.Path('/proc/'+str(p['test']['pid'])+'/environ').read_bytes().split(b'\0') if item.startswith(b'L12_DEPLOYMENT_DRAIN=')]
assert len(drain_values)<=1
drain_value=drain_values[0].decode('ascii').strip().lower() if drain_values else ''
assert drain_value in ('','true','false')
deployment_drain_enabled=drain_value=='true'
pattern=r'^(\{"recipient":[01],"schema":4,"kind":1|\{"schema":4,"kind":2)'
child=subprocess.run(['journalctl','--no-pager','-o','cat','-u','legion12-testrun.service','_PID='+str(p['test']['pid']),'--grep='+pattern],capture_output=True,text=True,timeout=8)
assert child.returncode in (0,1) and not child.stderr and len(child.stdout.encode())<=131072
rows=[json.loads(line) for line in child.stdout.splitlines() if line]
assert len(rows)<=65
for row in rows:
 assert type(row) is dict and all(type(value) is int and 0<=value<=9007199254740991 for value in row.values())
 assert row.get('schema')==4 and row.get('kind') in (1,2)
print(json.dumps({'schema':1,'testPid':p['test']['pid'],'rows':rows,'deploymentDrainEnabled':deployment_drain_enabled,'numericOnly':True,'otherApplicationLogsRead':False},separators=(',',':')))
`,{...binding,diagnosticSetting:terminalSetting()},{timeoutMs:15000})
}
export async function prepareTerminalAnalytics(binding,runId){
 assert.match(runId,/^[a-f0-9]{32}$/)
 const file='D:/GPT/Legion12/artifacts/stage-goals-20261023/test-release/fixture-import-r2/testrun-owned-analytics-import.py'
 const original=fs.readFileSync(file,'utf8')
 const prior=JSON.parse(fs.readFileSync('D:/GPT/Legion12/artifacts/stage-goals-20261023/test-release/fixture-import-binding-r2.json'))
 assert.equal(digest(original),prior.derivedSha256);assert.equal(prior.functionsUnchanged,true)
 let adapted=original
 const edits=[
  ['616457, TEST_COMMIT, TEST_DLL',binding.test.pid+', TEST_COMMIT, TEST_DLL'],
  ['577290, FORMAL_COMMIT, FORMAL_DLL',binding.formal.pid+', FORMAL_COMMIT, FORMAL_DLL'],
  ['RECEIPTS = Path("/tmp/l12-analytics-import-" + RUN)','RECEIPTS = Path("/tmp/l12-analytics-import-" + RUN + "-terminal-'+runId+'")'],
  ['require(not environ.get(b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS"), "DIAGNOSTICS_CHANGED")',
   'require(environ.get(b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS", b"") == ('+JSON.stringify(terminalSetting())+'.encode() if name == "test" else b""), "DIAGNOSTICS_CHANGED")'],
 ]
 for(const [before,after] of edits){assert.equal(adapted.split(before).length,2);adapted=adapted.replace(before,after)}
 let reverse=adapted;for(const [before,after]of edits.toReversed())reverse=reverse.replace(after,before);assert.equal(reverse,original)
 const result=await remoteJson(source+String.raw`
import base64,sqlite3
raw=base64.b64decode(p['source'],validate=True);assert len(raw)<50000 and hashlib.sha256(raw).hexdigest().upper()==p['sha256']
root=pathlib.Path('/tmp/l12-analytics-fixture-5f1a2343200a4fa680d7bd10ef1f8ded');assert root.resolve()==root and root.is_dir() and not root.is_symlink()
with sqlite3.connect('file:/opt/legion12-testrun-runtime/matches.db?mode=ro',uri=True) as db:
 assert db.execute('SELECT COUNT(*) FROM matches').fetchone()[0]==0,'DIAGNOSTIC_MATCH_FIXTURE_TARGET_NOT_EMPTY'
entry=root/('terminal-import-'+p['runId']+'.py');assert not os.path.lexists(entry)
with entry.open('xb') as out:os.fchmod(out.fileno(),0o600);out.write(raw);out.flush();os.fsync(out.fileno())
child=subprocess.run(['python3','-I','-B',str(entry),'--apply'],cwd=root,capture_output=True,text=True,timeout=40)
assert len(child.stdout)<131072 and len(child.stderr)<8192
value=json.loads(child.stdout) if child.stdout.strip() else None
assert child.returncode==0 and not child.stderr and value['status']=='COMMITTED_VERIFIED' and value['matches']==16,'DIAGNOSTIC_FIXTURE_NOT_VERIFIED'
print(json.dumps({'schema':1,'result':value,'diagnosticOnly':True,'onlineCapacityAccepted':False,'formalWrites':0},separators=(',',':')))
`,{...binding,diagnosticSetting:terminalSetting(),runId,source:Buffer.from(adapted).toString('base64'),sha256:digest(adapted)},{timeoutMs:50000})
 return {...result,derivation:{sourceSha256:digest(original),adaptedSha256:digest(adapted),reverseExact:true,exactPolicyEdits:4,appendFunctionUnchanged:true}}
}
