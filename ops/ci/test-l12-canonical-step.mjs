// Execute the real adapter/host in a synthetic repository. The owner and Python
// tools are inert fixtures: these tests prove orchestration, never a real package.
import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { randomUUID } from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { execFileSync, spawnSync } from 'node:child_process'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const pwsh = process.env.L12_PWSH, base = process.env.L12_ARTIFACT_ROOT
assert(pwsh && path.isAbsolute(pwsh) && base && path.isAbsolute(base), 'initialize governed host/output first')
const root = path.join(base, 'canonical-step-' + randomUUID())
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '. $env:L12_SYNTHETIC_GUARD; $null=Assert-L12GeneratedPath -Path $env:L12_SYNTHETIC_ROOT -Purpose "canonical adapter synthetic tests"'],
{ env: { ...process.env, L12_SYNTHETIC_GUARD: path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'),
  L12_SYNTHETIC_ROOT: root }, windowsHide: true, timeout: 15000 })
const mirror = path.join(root, 'repo')
for (const name of ['ops/ci/invoke-l12-canonical-package.ps1', 'ops/windows/Initialize-L12BuildEnvironment.ps1', 'ops/windows/L12GeneratedStorage.ps1']) {
  const target = path.join(mirror, name)
  fs.mkdirSync(path.dirname(target), { recursive: true })
  fs.copyFileSync(path.join(repo, name), target)
}
fs.writeFileSync(path.join(mirror, 'ops/ci/l12-card-assets-input.lock.json'), '{}')
fs.writeFileSync(path.join(mirror, 'ops/ci/l12-card-assets-input.py'), `import json,os,sys
from pathlib import Path
mode=os.environ['L12_SYNTHETIC_MODE']
command=sys.argv[1]
with open(os.environ['L12_SYNTHETIC_TRACE'],'a',encoding='utf8') as out: out.write(json.dumps({'step':'input','command':command,'args':sys.argv[2:]})+'\\n')
if mode=='corrupt': sys.exit(21)
if command=='prepare': Path(sys.argv[sys.argv.index('--output-root')+1]).mkdir()
print('synthetic input only')
`)
fs.writeFileSync(path.join(mirror, 'ops/ci/l12-canonical-artifacts.py'), `import json,os,sys
with open(os.environ['L12_SYNTHETIC_TRACE'],'a',encoding='utf8') as out: out.write(json.dumps({'step':'qualify','args':sys.argv[1:]})+'\\n')
if os.environ['L12_SYNTHETIC_MODE']=='invalid-output': sys.exit(23)
root=sys.argv[sys.argv.index('--output-root')+1]
print(json.dumps({key:root+'/'+key for key in ['manifest','releaseArchive','cardAssetsArchive']}))
`)
fs.writeFileSync(path.join(mirror, 'ops/windows/verify-l12.ps1'), `param([string]$OutputDirectory,[string]$CacheRoot,[string]$CardAssetDirectory)
$ErrorActionPreference='Stop'
@{step='owner';output=$OutputDirectory;cache=$CacheRoot;input=$CardAssetDirectory;host=[Environment]::ProcessPath}|ConvertTo-Json -Compress|Add-Content -LiteralPath $env:L12_SYNTHETIC_TRACE
Write-Host 'synthetic canonical stdout; not a package'
if($env:L12_SYNTHETIC_MODE -eq 'owner-failure'){[Console]::Error.WriteLine('synthetic native failure');exit 37}
exit 0
`)
const git = args => execFileSync('git', ['-C', mirror, ...args], { encoding: 'utf8', windowsHide: true, timeout: 15000, stdio: ['ignore','pipe','pipe'] })
git(['init', '-q']); git(['add', '.'])
git(['-c','user.name=Legion12 synthetic QA','-c','user.email=synthetic@example.invalid','commit','-qm','synthetic adapter fixtures'])
const head = git(['rev-parse', 'HEAD']).trim()
let cases = 0
for (const mode of ['cold','warm','corrupt','owner-failure','invalid-output','existing-output','wrong-commit']) {
  const item = path.join(root, mode), cache = path.join(item,'cache'), input = path.join(cache,'card-input'), output = path.join(item,'packages')
  fs.mkdirSync(item, { recursive: true })
  if (mode !== 'cold') fs.mkdirSync(input, { recursive: true })
  if (mode === 'existing-output') fs.mkdirSync(output)
  const trace = path.join(item,'trace.jsonl'), github = path.join(item,'outputs.txt')
  const env = { ...process.env, L12_WORK_CACHE:cache, L12_DEPENDENCY_CACHE:path.join(item,'dependencies'),
    L12_ARTIFACT_ROOT:path.join(item,'evidence'), L12_ARCHIVE_ROOT:path.join(item,'archives'),
    GITHUB_OUTPUT:github, L12_SYNTHETIC_TRACE:trace, L12_SYNTHETIC_MODE:mode }
  const result = spawnSync(pwsh, ['-NoProfile','-NonInteractive','-File',path.join(mirror,'ops/ci/invoke-l12-canonical-package.ps1'),
    '-Commit', mode === 'wrong-commit' ? '0'.repeat(40) : head, '-CardAssetRoot',input,'-OutputRoot',output],
  { cwd:mirror, env, windowsHide:true, timeout:30000, encoding:'utf8', maxBuffer:100000 })
  assert.ifError(result.error)
  const success = mode === 'cold' || mode === 'warm'
  assert.equal(result.status === 0, success, `${mode}: ${result.stdout}\n${result.stderr}`)
  const entries = fs.existsSync(trace) ? fs.readFileSync(trace,'utf8').replaceAll('\uFEFF','').trim().split(/\r?\n/).map(line=>JSON.parse(line)) : []
  if (mode === 'existing-output' || mode === 'wrong-commit') assert.equal(entries.length,0, 'preflight must precede input/build')
  else {
    assert.equal(entries[0].command, mode === 'cold' ? 'prepare' : 'verify')
    assert.equal(entries.filter(row=>row.step==='owner').length, Number(mode !== 'corrupt'))
    assert.equal(entries.filter(row=>row.step==='qualify').length, Number(!['corrupt','owner-failure'].includes(mode)))
  }
  const owner = entries.find(row=>row.step==='owner')
  if (owner) {
    assert.equal(path.resolve(owner.input), input)
    assert.equal(path.resolve(owner.cache), cache)
    assert.equal(path.resolve(owner.output), output)
    assert.equal(path.resolve(owner.host).toLowerCase(), path.resolve(pwsh).toLowerCase(), 'fresh canonical child must use bound actual host')
  }
  assert.equal(fs.existsSync(github),success,'failure cannot export successful output paths')
  if (success) assert.equal(fs.readFileSync(github,'utf8').trim().split(/\r?\n/).length,3)
  else if (mode !== 'existing-output') assert.match(result.stdout,/::error title=L12 canonical actual failure::/)
  fs.writeFileSync(path.join(item,'captured.log'),result.stdout+'\n'+result.stderr)
  cases++
}
let policyCases = 0
const commit = () => { git(['add','.']); git(['-c','user.name=Legion12 synthetic QA','-c','user.email=synthetic@example.invalid','commit','-qm','synthetic source policy']); return git(['rev-parse','HEAD']).trim() }
const eventFile = path.join(root,'event.json')
function realPolicy(before, after, expected, eventName='push') {
  fs.writeFileSync(eventFile, JSON.stringify({ref:'refs/heads/main',before,after,deleted:false}))
  const result = spawnSync(process.execPath,[path.join(repo,'ops/ci/l12-package-policy.mjs'),eventName,eventFile,after],
    {cwd:mirror,encoding:'utf8',windowsHide:true,timeout:15000})
  assert.ifError(result.error)
  assert.equal(result.status,0,result.stderr)
  assert.equal(JSON.parse(result.stdout).package,expected)
  policyCases++
}
fs.mkdirSync(path.join(mirror,'docs'))
fs.writeFileSync(path.join(mirror,'docs/说明.md'),'synthetic docs only')
const docsHead = commit()
realPolicy(head,docsHead,false)
fs.mkdirSync(path.join(mirror,'opcgpro-vue/src'),{recursive:true})
fs.writeFileSync(path.join(mirror,'opcgpro-vue/src/main.ts'),'// synthetic source')
const sourceHead = commit()
realPolicy(docsHead,sourceHead,true)
fs.renameSync(path.join(mirror,'docs/说明.md'),path.join(mirror,'docs/新说明.md'))
const renamedHead = commit()
realPolicy(sourceHead,renamedHead,false)
realPolicy(sourceHead,renamedHead,false,'pull_request')
git(['-c','user.name=Legion12 synthetic QA','-c','user.email=synthetic@example.invalid','tag','-a','v-synthetic','-m','synthetic annotated tag'])
const tagObject = git(['rev-parse','refs/tags/v-synthetic']).trim()
fs.writeFileSync(eventFile,JSON.stringify({ref:'refs/tags/v-synthetic',before:'0'.repeat(40),after:tagObject,deleted:false}))
const tagged = spawnSync(process.execPath,[path.join(repo,'ops/ci/l12-package-policy.mjs'),'push',eventFile,tagObject],
  {cwd:mirror,encoding:'utf8',windowsHide:true,timeout:15000})
assert.ifError(tagged.error)
assert.equal(tagged.status,0,tagged.stderr)
assert.equal(JSON.parse(tagged.stdout).commit,renamedHead)
assert.equal(JSON.parse(tagged.stdout).package,true)
policyCases++
console.log(`Canonical adapter: ${cases} real fresh-host synthetic cases plus ${policyCases} real Git/CLI policy cases passed; failures stop in order. No real release built or uploaded. Evidence: ${root}`)
