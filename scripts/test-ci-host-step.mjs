import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { randomUUID } from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { execFileSync, spawnSync } from 'node:child_process'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const workflow = fs.readFileSync(path.join(repo, '.github/workflows/verify-release.yml'), 'utf8').replace(/\r\n?/g, '\n')
const step = workflow.split('      - name: 前端生产构建\n')[1]?.split('\n      - name:')[0]
const lines = step?.split('        run: |\n')[1]?.trimEnd().split('\n')
assert(lines?.length && lines.every(line => line.startsWith('          ')), 'actual frontend run block must be unambiguous')
const actual = lines.map(line => line.slice(10)).join('\n')
assert(actual.includes('npm run build 2>&1 | Tee-Object -FilePath $log'))
const pwsh = process.env.L12_PWSH
assert(pwsh && path.isAbsolute(pwsh) && fs.statSync(pwsh).isFile(), 'bind actual host first')
const base = process.env.L12_ARTIFACT_ROOT
assert(base && path.isAbsolute(base), 'bind explicit evidence root first')
const root = path.join(base, 'ci-step-fixture-' + randomUUID())
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '. $env:L12_STEP_GUARD; $null=Assert-L12GeneratedPath -Path $env:L12_STEP_ROOT -Purpose "synthetic CI failure propagation"'],
{ env: { ...process.env, L12_STEP_ROOT: root, L12_STEP_GUARD: path.join(repo, 'ops/windows/L12GeneratedStorage.ps1') },
  windowsHide: true, timeout: 15000 })
assert(!fs.existsSync(root))
fs.mkdirSync(root, { recursive: true })
const quote = value => "'" + value.replaceAll("'", "''") + "'"
let cases = 0
try {
  for (const scenario of [{ code: 0, tail: 'synthetic success' },
    { code: 7, tail: 'synthetic 7% failure\nsecond line' },
    { code: 13, tail: 'X'.repeat(10000) + '\nsynthetic 13% stderr' }]) {
    const directory = path.join(root, String(scenario.code))
    const payload = Buffer.from(scenario.tail).toString('base64')
    // Only npm is replaced. The real workflow body runs in its own actual
    // PowerShell process and consumes a native Node exit, stdout and stderr.
    const native = `process.stdout.write('native fixture stdout\\n');process.stderr.write(Buffer.from(process.env.L12_STEP_PAYLOAD,'base64').toString());process.exit(${scenario.code})`
    const program = `function npm { & ${quote(process.execPath)} -e ${quote(native)} }\n` + actual
    const result = spawnSync(pwsh, ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(program, 'utf16le').toString('base64')], {
      env: { ...process.env, L12_ARTIFACT_ROOT: directory, L12_STEP_PAYLOAD: payload }, encoding: 'utf8', windowsHide: true, timeout: 20000, maxBuffer: 100000,
    })
    assert.ifError(result.error)
    assert.equal(result.status, scenario.code, result.stderr || result.stdout)
    const log = fs.readFileSync(path.join(directory, 'frontend-native.log'), 'utf8')
    assert(log.includes('native fixture stdout'))
    assert(log.replace(/\r\n/g, '\n').includes(scenario.tail), 'raw log retains all native lines; host newline convention is not data loss')
    const annotation = result.stdout.split(/\r?\n/).find(line => line.startsWith('::error title=L12 frontend actual failure::'))
    if (scenario.code === 0) assert.equal(annotation, undefined)
    else {
      assert(annotation?.includes(`synthetic ${scenario.code}%25`), 'actual failure annotation must preserve escaped error')
      assert(annotation.includes('%0A'), 'actual annotation must escape newline')
      assert(annotation.length < 25000 && log.length >= scenario.tail.length, 'summary bounded without truncating raw log')
    }
    cases++
  }
} finally {
  for (const directory of fs.readdirSync(root)) {
    assert(['0', '7', '13'].includes(directory))
    const child = path.join(root, directory)
    assert(fs.lstatSync(child).isDirectory() && !fs.lstatSync(child).isSymbolicLink())
    assert.deepEqual(fs.readdirSync(child), ['frontend-native.log'])
    const log = path.join(child, 'frontend-native.log')
    assert(fs.lstatSync(log).isFile() && !fs.lstatSync(log).isSymbolicLink())
    fs.unlinkSync(log); fs.rmdirSync(child)
  }
  fs.rmdirSync(root)
}
console.log(`Actual CI frontend wrapper passed: ${cases} native exit/stdout/stderr/annotation cases; standard npm build mocked, no dependency install or product load; fixtures removed.`)
