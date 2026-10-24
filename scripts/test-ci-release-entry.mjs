import assert from 'node:assert/strict'
import fs from 'node:fs'

const workflow = fs.readFileSync(process.argv[2] || new URL('../.github/workflows/verify-release.yml', import.meta.url), 'utf8').replace(/\r\n/g, '\n')
const standard = fs.readFileSync(new URL('../ops/windows/verify-l12.ps1', import.meta.url), 'utf8')
const deploy = fs.readFileSync(new URL('../ops/windows/deploy-l12-testrun.ps1', import.meta.url), 'utf8')

function check(source) {
  assert.doesNotMatch(source, /tar\s+-[a-zA-Z]*c|dotnet\s+publish|actions\/upload-artifact|releaseSha256\s*"?\s*:/,
    'CI must not publish a second hand-assembled legacy deployment artifact')
  for (const command of ['node scripts/check-l12-architecture-lock.mjs', 'node scripts/test-ci-release-entry.mjs',
    'npm ci --prefer-offline --no-audit', 'npm run build', 'node scripts/test-release-status.mjs',
    './scripts/invoke-l12-tests.ps1 -Project ./TwelveLegions.Tests/TwelveLegions.Tests.csproj',
    './scripts/invoke-l12-tests.ps1 -Project ./TwelveLegions.Platform.Tests/TwelveLegions.Platform.Tests.csproj'])
    assert.equal(source.includes(command), true, `CI must preserve ${command}`)
  assert.ok(source.indexOf('npm run build') < source.indexOf('./scripts/invoke-l12-tests.ps1'),
    'cheap frontend rejection must precede long backend suites')
  assert.match(source, /^        run: bash scripts\/test-l12-runtime-backup-retention\.sh ops\/server\/deploy-l12-testrun-release\.sh "\$RUNNER_TEMP"$/m,
    'CI must execute the real synthetic backup-retention boundary suite')
  for (const command of ['python3 -I -B ops/ci/test-l12-card-assets-input.py --work-root "$RUNNER_TEMP/l12-card-input"',
    'python3 -I -B ops/ci/l12-card-assets-input.py check --lock ops/ci/l12-card-assets-input.lock.json'])
    assert.equal(source.includes(command), true, `CI must preserve trusted asset boundary: ${command}`)
  for (const name of ['L12_WORK_CACHE', 'L12_DEPENDENCY_CACHE', 'L12_ARTIFACT_ROOT', 'L12_ARCHIVE_ROOT'])
    assert.match(source, new RegExp(`^      ${name}: \\$\\{\\{ runner\\.temp \\}\\}/l12-ci/`, 'm'),
      `CI must bind ${name} outside its source checkout`)
  for (const command of ['& ./ops/windows/Initialize-L12BuildEnvironment.ps1 | Out-Null',
    '& ./scripts/test-l12-build-host.ps1 -WorkRoot $env:L12_WORK_CACHE',
    'node scripts/test-ci-host-step.mjs',
    "@('L12_PWSH','TEMP','TMP','TMPDIR','DOTNET_CLI_HOME','NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH','npm_config_cache','COREPACK_HOME')",
    'npm run build 2>&1 | Tee-Object -FilePath $log', '$buildExit = $LASTEXITCODE',
    'if ($buildExit -ne 0)', 'exit $buildExit',
    '$tail = $tail.Replace(\'%\',\'%25\').Replace("`r",\'%0D\').Replace("`n",\'%0A\')'])
    assert.ok(source.includes(command), `CI must retain host/failure binding: ${command}`)
  assert.ok(source.indexOf('& ./ops/windows/Initialize-L12BuildEnvironment.ps1') < source.indexOf('npm ci '),
    'CI must bind host and all generated roots before dependency install and frontend execution')
  assert.equal((source.match(/^          npm run build 2>&1 \| Tee-Object -FilePath \$log$/gm) || []).length, 1,
    'CI still owns exactly one standard frontend build')
}

check(workflow)
for (const marker of ['.deployment-drain-capability.json', 'dist-testrun', 'testrun-shared-files.txt']) {
  assert.ok(standard.includes(marker), `standard package retains ${marker}`)
  assert.ok(deploy.includes(marker), `deployer still requires ${marker}`)
}
assert.ok(standard.includes('schema = 3'))
assert.ok(standard.includes('runtimeSourceCompatibility'))
assert.ok(standard.includes('cardAssetsSha256'))
assert.throws(() => check(workflow + '\n      - run: tar -czf old.tar.gz .'), /second hand-assembled/)
assert.throws(() => check(workflow.replace('npm run build', 'echo skipped')), /preserve npm run build/)
assert.throws(() => check(workflow.replace('./scripts/invoke-l12-tests.ps1 -Project ./TwelveLegions.Platform.Tests/TwelveLegions.Platform.Tests.csproj', 'echo skipped')),
  /preserve.*Platform.Tests/)
assert.throws(() => check(workflow.replace('run: bash scripts/test-l12-runtime-backup-retention.sh', 'run: echo skipped-retention')),
  /synthetic backup-retention/)
assert.throws(() => check(workflow.replace('python3 -I -B ops/ci/test-l12-card-assets-input.py', 'echo skipped-assets')), /trusted asset boundary/)
assert.throws(() => check(workflow.replace('check --lock ops/ci/l12-card-assets-input.lock.json', 'check --lock other.json')), /trusted asset boundary/)
assert.throws(() => check(workflow.replace('& ./ops/windows/Initialize-L12BuildEnvironment.ps1 | Out-Null', 'echo skipped-host')), /host\/failure binding/)
assert.throws(() => check(workflow.replace('L12_WORK_CACHE: ${{ runner.temp }}', 'L12_WORK_CACHE: ${{ github.workspace }}')), /bind L12_WORK_CACHE/)
assert.throws(() => check(workflow.replace('exit $buildExit', 'exit 0')), /host\/failure binding/)
assert.throws(() => check(workflow.replace('.Replace("`n",\'%0A\')', '')), /host\/failure binding/)
console.log('CI single release entry: current workflow and ten negative mutations passed; canonical package, trusted assets, synthetic retention and native failure propagation retained.')
