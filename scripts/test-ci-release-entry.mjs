import assert from 'node:assert/strict'
import fs from 'node:fs'

const workflow = fs.readFileSync(process.argv[2] || new URL('../.github/workflows/verify-release.yml', import.meta.url), 'utf8').replace(/\r\n/g, '\n')
const standard = fs.readFileSync(new URL('../ops/windows/verify-l12.ps1', import.meta.url), 'utf8')
const deploy = fs.readFileSync(new URL('../ops/windows/deploy-l12-testrun.ps1', import.meta.url), 'utf8')
const canonicalCi = fs.readFileSync(new URL('../ops/ci/invoke-l12-canonical-package.ps1', import.meta.url), 'utf8')

export function check(source) {
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
    assert.match(source, new RegExp(`^          ${name}: \\$\\{\\{ runner\\.temp \\}\\}/l12-ci/`, 'm'),
      `CI must bind ${name} in step env outside its source checkout`)
  assert.doesNotMatch(source.split(/^    steps:/m)[0], /\$\{\{\s*runner\./,
    'runner context is unavailable before steps: never place runner.temp in workflow/job env')
  for (const command of ['& ./ops/windows/Initialize-L12BuildEnvironment.ps1 | Out-Null',
    '& ./scripts/test-l12-build-host.ps1 -WorkRoot $env:L12_WORK_CACHE',
    '::error title=L12 host boundary actual failure::',
    'node scripts/test-ci-host-step.mjs',
    "@('L12_WORK_CACHE','L12_DEPENDENCY_CACHE','L12_ARTIFACT_ROOT','L12_ARCHIVE_ROOT','L12_PWSH','TEMP','TMP','TMPDIR','DOTNET_CLI_HOME','NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH','npm_config_cache','COREPACK_HOME')",
    'npm run build 2>&1 | Tee-Object -FilePath $log', '$buildExit = $LASTEXITCODE',
    'if ($buildExit -ne 0)', 'exit $buildExit',
    '$tail = $tail.Replace(\'%\',\'%25\').Replace("`r",\'%0D\').Replace("`n",\'%0A\')'])
    assert.ok(source.includes(command), `CI must retain host/failure binding: ${command}`)
  for (const name of ['构建宿主边界验证', '前端生产构建']) {
    const step = source.split(`      - name: ${name}\n`)[1]?.split('\n      - ')[0] || ''
    assert.ok(step.includes('$tail = $tail.Replace(\'%\',\'%25\').Replace("`r",\'%0D\').Replace("`n",\'%0A\')'),
      `CI host/failure binding must escape each actual step: ${name}`)
    for (const marker of ['$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)',
      '$tailBytes = [Text.Encoding]::UTF8.GetBytes($tail)', '$tailBytes.Length -gt 3000',
      '$start = $tailBytes.Length - 3000', 'while (($tailBytes[$start] -band 0xC0) -eq 0x80)',
      '[Text.Encoding]::UTF8.GetString($tailBytes, $start, $tailBytes.Length - $start)'])
      assert.ok(step.includes(marker), `CI host/failure binding must bound UTF-8 tail in each step: ${name}`)
  }
  assert.ok(source.indexOf('& ./ops/windows/Initialize-L12BuildEnvironment.ps1') < source.indexOf('npm ci '),
    'CI must bind host and all generated roots before dependency install and frontend execution')
  assert.equal((source.match(/^          npm run build 2>&1 \| Tee-Object -FilePath \$log$/gm) || []).length, 1,
    'CI still owns exactly one standard frontend build')
  for (const name of ['安装前端依赖', '前端生产构建', 'L12 规则测试', '平台与控制面测试']) {
    const step = source.split(`      - name: ${name}\n`)[1]?.split('\n      - ')[0] || ''
    assert.match(step, /^        if: steps\.policy\.outputs\.verify == 'true'$/m,
      `verification-only lane must not repeat canonical gates: ${name}`)
  }
  const canonical = source.split('      - name: 唯一标准发布包\n')[1]?.split('\n      - ')[0] || ''
  for (const marker of ["if: steps.policy.outputs.package == 'true'", 'id: canonical',
    'CI_HEAD: ${{ steps.policy.outputs.commit }}',
    'run: ./ops/ci/invoke-l12-canonical-package.ps1 -Commit $env:CI_HEAD -CardAssetRoot "$env:L12_WORK_CACHE/card-input" -OutputRoot "$env:L12_ARTIFACT_ROOT/canonical"'])
    assert.ok(canonical.includes(marker), `single canonical lane missing: ${marker}`)
  assert.equal((source.match(/run: \.\/ops\/ci\/invoke-l12-canonical-package\.ps1/g) || []).length, 1, 'single canonical invocation')
  const cache = source.split('      - name: 可信卡图精确缓存\n')[1]?.split('\n      - ')[0] || ''
  for (const marker of ["if: steps.policy.outputs.package == 'true'", 'uses: actions/cache@v4',
    'path: ${{ env.L12_WORK_CACHE }}/card-input', "hashFiles('ops/ci/l12-card-assets-input.lock.json', 'ops/ci/l12-card-assets-input.py')"])
    assert.ok(cache.includes(marker), `exact canonical input cache missing: ${marker}`)
  assert.doesNotMatch(cache, /restore-keys:/, 'no partially matching trusted asset cache')
  const policy = source.split('      - name: 标准出包选择与边界\n')[1]?.split('\n      - ')[0] || ''
  for (const marker of ['id: policy', 'node ops/ci/test-l12-package-policy.mjs',
    'python3 -I -B ops/ci/test-l12-canonical-artifacts.py',
    'node ops/ci/l12-package-policy.mjs $env:CI_EVENT_NAME $env:GITHUB_EVENT_PATH $env:CI_HEAD',
    'if ($LASTEXITCODE -ne 0)', 'ConvertFrom-Json', '"commit=$($selection.commit)"', '"verify=$($selection.verify.ToString().ToLowerInvariant())"', '$env:GITHUB_OUTPUT'])
    assert.ok(policy.includes(marker), `canonical source selection boundary missing: ${marker}`)
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
assert.throws(() => check(workflow.replace('    steps:', '    env:\n      BAD: ${{ runner.temp }}\n    steps:')), /runner context is unavailable/)
assert.throws(() => check(workflow.replace("'L12_WORK_CACHE','L12_DEPENDENCY_CACHE','L12_ARTIFACT_ROOT','L12_ARCHIVE_ROOT','L12_PWSH'", "'L12_PWSH'")), /host\/failure binding/)
assert.throws(() => check(workflow.replace('$tailBytes.Length -gt 3000', '$tailBytes.Length -gt 8000')), /host\/failure binding/)
assert.throws(() => check(workflow.replace('while (($tailBytes[$start] -band 0xC0) -eq 0x80)', 'while ($false)')), /host\/failure binding/)
assert.throws(() => check(workflow.replace("if: steps.policy.outputs.verify == 'true'", 'if: always()')), /must not repeat/)
assert.throws(() => check(workflow.replace('run: ./ops/ci/invoke-l12-canonical-package.ps1', 'run: echo skipped')), /single canonical lane/)
assert.throws(() => check(workflow.replace("hashFiles('ops/ci/l12-card-assets-input.lock.json', 'ops/ci/l12-card-assets-input.py')", "hashFiles('global.json')")), /input cache/)
assert.throws(() => check(workflow.replace('      - name: 可信卡图精确缓存\n', '      - name: skipped-cache\n')), /input cache/)
assert.throws(() => check(workflow.replace('node ops/ci/l12-package-policy.mjs $env:CI_EVENT_NAME', 'echo skip $env:CI_EVENT_NAME')), /source selection/)
assert.throws(() => check(workflow.replace('id: policy', 'id: other')), /source selection/)
for (const marker of ['ops/windows/verify-l12.ps1', 'ops/ci/l12-canonical-artifacts.py',
  'verify --lock $lock --input-root $CardAssetRoot', 'prepare --lock $lock --output-root $CardAssetRoot --from-web',
  'if ($LASTEXITCODE -ne 0)', 'if ($canonicalExit -ne 0)', '-OutputDirectory $OutputRoot -CacheRoot $env:L12_WORK_CACHE -CardAssetDirectory $CardAssetRoot',
  'Assert-L12GeneratedPath', '::error title=L12 canonical actual failure::'])
  assert.ok(canonicalCi.includes(marker), `canonical adapter must retain ${marker}`)
assert.doesNotMatch(canonicalCi, /\btar\s+-|dotnet\s+publish|--filter|SkipTests|SkipBuild/, 'CI adapter cannot create a second packager or skip gates')
const ownerCall = canonicalCi.split('    & $env:L12_PWSH -NoProfile -NonInteractive -File ')[1]?.split('    $canonicalExit')[0]
assert(ownerCall && !/-Force\b/.test(ownerCall), 'canonical owner cannot be forced; New-Item directory creation is unrelated')
for (const project of ['TwelveLegions.Tests', 'TwelveLegions.Platform.Tests'])
  assert.ok(standard.includes(`'${project}/${project}.csproj'`), 'canonical owner retains both full suites')
console.log('CI single release entry: current workflow and twenty negative mutations passed; mutually exclusive full verification/canonical lanes, exact input cache and failure propagation retained.')
