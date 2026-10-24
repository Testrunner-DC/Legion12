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
const stdoutLine = 'native fixture stdout'
function assertCapturedNativeLines(log, stderr) {
  const lines = log.replace(/\r\n/g, '\n').split('\n')
  if (lines.at(-1) === '') lines.pop()
  assert.equal(lines.filter(line => line === stdoutLine).length, 1, 'native stdout captured exactly once')
  // PowerShell merges two independent native streams; stdout may occur between
  // stderr lines. Require every stderr line verbatim, in order, without assuming
  // a cross-stream order that the native process does not guarantee.
  const captured = lines.filter(line => line !== stdoutLine), expected = stderr.split('\n')
  assert(captured.length === expected.length && captured.every((line, index) => line === expected[index]),
    `raw stderr lines retained; expected=${stderr.length}; log=${log.length}; synthetic tail=${JSON.stringify(log.slice(-240))}`)
}
let cases = 0
try {
  for (const name of ['test-tutorial-guidance.mjs', 'test-tutorial-progress-recovery.mjs', 'test-tutorial-response-guidance.mjs']) {
    const url = new URL('../opcgpro-vue/scripts/' + name, import.meta.url)
    const source = fs.readFileSync(url, 'utf8')
    const start = source.indexOf(name === 'test-tutorial-response-guidance.mjs' ? 'const front =' : 'const scriptDir =')
    const end = source.indexOf('fs.mkdirSync(output,', start)
    assert(start >= 0 && end > start)
    const prefix = source.slice(start, end).replaceAll('import.meta.url', JSON.stringify(url.href))
    for (const rejected of [false, true]) {
      let calls = 0, writes = 0
      const run = () => new Function('path', 'process', 'execFileSync', 'fs', 'assert', 'fileURLToPath',
        prefix + ';fs.mkdirSync(output,{recursive:true})')(path,
        { platform: process.platform, env: { L12_ARTIFACT_ROOT: root, L12_PWSH: pwsh, SystemRoot: 'C:/must-not-select-legacy-host' } },
        (executable, args, options) => {
          calls++
          assert.equal(executable, pwsh, `${name} must prefer the initialized absolute host, not legacy Windows discovery`)
          assert(args.at(-1).includes('Assert-L12GeneratedPath'))
          assert(Object.values(options.env).some(value => value === root || String(value).startsWith(root + path.sep)))
          if (rejected) throw new Error('SYNTHETIC_PHYSICAL_ROOT_REFUSED')
        }, { mkdirSync: () => writes++ }, assert, fileURLToPath)
      if (rejected) assert.throws(run, /SYNTHETIC_PHYSICAL_ROOT_REFUSED/)
      else run()
      assert.equal(calls, 1)
      assert.equal(writes, Number(!rejected), 'refused physical root must precede every evidence write')
    }
    cases++
  }
  for (const log of ['native fixture stdout\nfirst\nsecond\n', 'first\nnative fixture stdout\nsecond\n', 'first\nsecond\nnative fixture stdout\n'])
    assertCapturedNativeLines(log, 'first\nsecond')
  for (const log of ['first\nnative fixture stdout\n', 'first\nnative fixture stdout\nsecon\n', 'second\nnative fixture stdout\nfirst\n'])
    assert.throws(() => assertCapturedNativeLines(log, 'first\nsecond'))
  cases++
  const initialization = workflow.split('      - name: 生成目录绑定\n')[1]?.split('\n      - ')[0]
    ?.split('        run: |\n')[1]?.trimEnd().split('\n')
  assert(initialization?.length && initialization.every(line => line.startsWith('          ')))
  const hostDirectory = path.join(root, 'host')
  fs.mkdirSync(hostDirectory)
  const hostRoots = {
    L12_WORK_CACHE: path.join(hostDirectory, 'cache'),
    L12_DEPENDENCY_CACHE: path.join(hostDirectory, 'dependencies'),
    L12_ARTIFACT_ROOT: path.join(hostDirectory, 'evidence'),
    L12_ARCHIVE_ROOT: path.join(hostDirectory, 'archives'),
  }
  const environmentFile = path.join(hostDirectory, 'github-env.txt')
  const initialized = spawnSync(pwsh, ['-NoProfile', '-NonInteractive', '-EncodedCommand',
    Buffer.from(initialization.map(line => line.slice(10)).join('\n'), 'utf16le').toString('base64')], {
    cwd: repo, env: { ...process.env, ...hostRoots, GITHUB_ENV: environmentFile },
    encoding: 'utf8', windowsHide: true, timeout: 20000, maxBuffer: 100000,
  })
  assert.ifError(initialized.error)
  assert.equal(initialized.status, 0, initialized.stderr || initialized.stdout)
  const exported = Object.fromEntries(fs.readFileSync(environmentFile, 'utf8').replace(/^\uFEFF/, '').trim().split(/\r?\n/)
    .map(line => { const at = line.indexOf('='); assert(at > 0); return [line.slice(0, at), line.slice(at + 1)] }))
  assert.equal(Object.keys(exported).length, 13, 'all step-local roots and native child bindings must reach later steps')
  for (const [name, value] of Object.entries(hostRoots)) assert.equal(exported[name], value)
  assert.equal(exported.L12_PWSH, pwsh)
  const laterStep = spawnSync(process.execPath, ['-e',
    'process.stdout.write(JSON.stringify(Object.fromEntries(["L12_WORK_CACHE","L12_DEPENDENCY_CACHE","L12_ARTIFACT_ROOT","L12_ARCHIVE_ROOT","L12_PWSH","TEMP","TMP","TMPDIR"].map(k=>[k,process.env[k]]))))'], {
    env: { ...process.env, ...exported }, encoding: 'utf8', windowsHide: true, timeout: 15000,
  })
  assert.ifError(laterStep.error); assert.equal(laterStep.status, 0)
  const later = JSON.parse(laterStep.stdout)
  for (const name of Object.keys(later)) assert.equal(later[name], exported[name], `fresh later step lost ${name}`)
  cases++
  const boundaryLines = workflow.split('      - name: 构建宿主边界验证\n')[1]?.split('\n      - ')[0]
    ?.split('        run: |\n')[1]?.trimEnd().split('\n')
  assert(boundaryLines?.length && boundaryLines.every(line => line.startsWith('          ')))
  const actualHostCall = '& ./scripts/test-l12-build-host.ps1 -WorkRoot $env:L12_WORK_CACHE 2>&1 | Tee-Object -FilePath $hostLog'
  const boundaryBody = boundaryLines.map(line => line.slice(10)).join('\n')
  assert(boundaryBody.includes(actualHostCall))
  const failureBody = boundaryBody.replace(actualHostCall, "throw 'synthetic host 7% failure'")
  const boundaryFailure = spawnSync(pwsh, ['-NoProfile', '-NonInteractive', '-EncodedCommand',
    Buffer.from(failureBody, 'utf16le').toString('base64')], {
    cwd: repo, env: { ...process.env, L12_ARTIFACT_ROOT: path.join(root, 'host-failure') },
    encoding: 'utf8', windowsHide: true, timeout: 15000, maxBuffer: 100000,
  })
  assert.ifError(boundaryFailure.error)
  assert.equal(boundaryFailure.status, 1)
  assert(boundaryFailure.stdout.includes('::error title=L12 host boundary actual failure::'))
  assert(boundaryFailure.stdout.includes('synthetic host 7%25 failure'))
  assert(boundaryFailure.stdout.split(/\r?\n/).find(line => line.startsWith('::error title=L12 host boundary actual failure::'))?.includes('%0A'))
  cases++
  for (const scenario of [{ code: 0, tail: 'synthetic success' },
    { code: 7, tail: 'synthetic 7% failure\nsecond line' },
    { code: 13, tail: 'X'.repeat(10000) + '\nsynthetic 13% stderr' },
    { code: 17, tail: '界'.repeat(4000) + '\nsynthetic 17% 中文' }]) {
    const directory = path.join(root, String(scenario.code))
    const payload = Buffer.from(scenario.tail).toString('base64')
    // Only npm is replaced. The real workflow body runs in its own actual
    // PowerShell process and consumes a native Node exit, stdout and stderr.
    // Pipe writes can be asynchronous on POSIX. Let the child flush them before
    // exiting, so this fixture tests the wrapper rather than truncating its input.
    const native = `process.stdout.write('native fixture stdout\\n');process.stderr.write(Buffer.from(process.env.L12_STEP_PAYLOAD,'base64').toString());process.exitCode=${scenario.code}`
    const program = `function npm { & ${quote(process.execPath)} -e ${quote(native)} }\n` + actual
    const result = spawnSync(pwsh, ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(program, 'utf16le').toString('base64')], {
      env: { ...process.env, L12_ARTIFACT_ROOT: directory, L12_STEP_PAYLOAD: payload }, encoding: 'utf8', windowsHide: true, timeout: 20000, maxBuffer: 100000,
    })
    assert.ifError(result.error)
    assert.equal(result.status, scenario.code, result.stderr || result.stdout)
    const log = fs.readFileSync(path.join(directory, 'frontend-native.log'), 'utf8')
    assertCapturedNativeLines(log, scenario.tail)
    const annotation = result.stdout.split(/\r?\n/).find(line => line.startsWith('::error title=L12 frontend actual failure::'))
    if (scenario.code === 0) assert.equal(annotation, undefined)
    else {
      assert(annotation?.includes(`synthetic ${scenario.code}%25`), 'actual failure annotation must preserve escaped error')
      assert(annotation.includes('%0A'), 'actual annotation must escape newline')
      const decoded = annotation.split('::').slice(2).join('::').replaceAll('%0D', '\r').replaceAll('%0A', '\n').replaceAll('%25', '%')
      assert(Buffer.byteLength(decoded, 'utf8') <= 3000 && !decoded.includes('\uFFFD'), 'UTF-8 summary fits below actual annotation truncation without splitting a character')
      assert(annotation.length < 25000 && log.length >= scenario.tail.length, 'summary bounded without truncating raw log')
    }
    cases++
  }
} finally {
  for (const directory of fs.readdirSync(root)) {
    assert(['host', 'host-failure', '0', '7', '13', '17'].includes(directory))
    const child = path.join(root, directory)
    assert(fs.lstatSync(child).isDirectory() && !fs.lstatSync(child).isSymbolicLink())
    if (directory === 'host-failure') {
      for (const name of fs.readdirSync(child)) {
        assert.equal(name, 'host-native.log')
        const log = path.join(child, name)
        assert(fs.lstatSync(log).isFile() && !fs.lstatSync(log).isSymbolicLink()); fs.unlinkSync(log)
      }
      fs.rmdirSync(child); continue
    }
    if (directory === 'host') {
      const removeHost = current => {
        for (const name of fs.readdirSync(current)) {
          const item = path.join(current, name), stat = fs.lstatSync(item)
          assert(!stat.isSymbolicLink())
          if (stat.isDirectory()) removeHost(item)
          else { assert.equal(item, path.join(child, 'github-env.txt')); assert(stat.isFile()); fs.unlinkSync(item) }
        }
        fs.rmdirSync(current)
      }
      removeHost(child)
      continue
    }
    assert.deepEqual(fs.readdirSync(child), ['frontend-native.log'])
    const log = path.join(child, 'frontend-native.log')
    assert(fs.lstatSync(log).isFile() && !fs.lstatSync(log).isSymbolicLink())
    fs.unlinkSync(log); fs.rmdirSync(child)
  }
  fs.rmdirSync(root)
}
console.log(`Actual CI step bodies passed: ${cases} groups (3 real tutorial QA prefixes bound-host/physical refusal;native stream interleaving/loss guards;13 bindings/fresh later step;host-error annotation;4 native exit/stdout/stderr cases including UTF-8 summary); standard npm mocked, no dependency install/product load; fixtures removed.`)
