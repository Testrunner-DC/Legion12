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
console.log('CI single release entry: current workflow and four negative mutations passed; canonical package and synthetic retention requirements retained.')
