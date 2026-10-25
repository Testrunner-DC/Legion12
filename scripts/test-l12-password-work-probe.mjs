import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

export function validatePasswordWorkReport(report) {
  assert.ok(['l12-password-work-v1', 'l12-password-work-v2'].includes(report.schema));
  assert.equal(report.scope, 'local-synthetic-pure-helper-only');
  for (const key of ['cpuQuotaEmulated', 'productionCapacityQualified', 'serverHttpLatencyMeasured', 'persistentMigrationPerformed']) assert.equal(report[key], false, key);
  assert.ok(Number.isInteger(report.processorCount) && report.processorCount > 0);
  assert.equal(report.queued, 8);
  assert.equal(report.maximumQueueWaitMs, 250);
  assert.equal(report.readers.currentWriterIterations, 120000);
  assert.equal(report.readers.currentReaderAcceptsCandidate, report.schema === 'l12-password-work-v2');
  if (report.schema === 'l12-password-work-v2') {
    assert.equal(report.readers.maximumReadIterations, 600000);
    assert.equal(report.readers.legacyReaderAcceptsCandidate, false);
  }
  for (const key of ['currentReaderAcceptsLegacy', 'compatibleReaderAcceptsLegacy', 'compatibleReaderAcceptsCandidate', 'rollbackRequiresCompatibleReader']) assert.equal(report.readers[key], true, key);
  const operations = ['create-120k', 'verify-120k', 'create-600k', 'verify-600k', 'verify-legacy-then-create-600k'];
  assert.deepEqual(report.serial.map(row => row.operation), operations);
  assert.equal(report.bursts.length, 6);
  const finite = value => assert.ok(Number.isFinite(value) && value >= 0);
  function distribution(value, count) {
    assert.equal(value.samples, count);
    if (count === 0) { for (const key of ['p50', 'p95', 'maximum']) assert.equal(value[key], null); return; }
    for (const key of ['p50', 'p95', 'maximum']) finite(value[key]);
    assert.ok(value.p50 <= value.p95 && value.p95 <= value.maximum);
  }
  for (const row of report.serial) {
    assert.ok(Number.isInteger(row.samples) && row.samples >= 3 && row.samples <= 20);
    distribution(row.workMs, row.samples); finite(row.processCpuMs);
  }
  const seen = new Set();
  for (const row of report.bursts) {
    assert.ok([1, 2].includes(row.workers));
    assert.ok(operations.filter(name => !name.startsWith('create-')).includes(row.operation));
    const key = `${row.workers}:${row.operation}`;
    assert.ok(!seen.has(key)); seen.add(key);
    assert.ok(Number.isInteger(row.requests) && row.requests >= 3 && row.requests <= 64);
    for (const name of ['completed', 'busy', 'maximumActive']) assert.ok(Number.isInteger(row[name]) && row[name] >= 0);
    assert.equal(row.completed + row.busy, row.requests);
    assert.ok(row.maximumActive <= row.workers && row.maximumActive <= row.completed);
    for (const name of ['waitMs', 'workMs', 'totalMs']) distribution(row[name], row.completed);
    distribution(row.busyResponseMs, row.busy); finite(row.wallMs); finite(row.processCpuMs);
  }
  function noSecrets(value) {
    if (!value || typeof value !== 'object') return;
    for (const [key, child] of Object.entries(value)) {
      assert.ok(!/^(password|passwordhash|salt|token|credential|temporarypassword)$/i.test(key), 'Credential field is forbidden');
      noSecrets(child);
    }
  }
  noSecrets(report);
  assert.ok(!JSON.stringify(report).includes('synthetic-probe-only-20261025'));
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [dotnet, assembly, reportPath] = process.argv.slice(2);
  assert.ok(dotnet && assembly && reportPath, 'Require explicit runtime, probe assembly and numeric report paths.');
  const report = JSON.parse(readFileSync(reportPath, 'utf8').replace(/^\uFEFF/, ''));
  validatePasswordWorkReport(report);
  let checks = 1;
  for (const args of [
    ['--samples', '0'], ['--samples', '21'], ['--burst', '0'], ['--burst', '65'],
    ['--unknown', '3'], ['--samples'], ['--samples', '3', '--samples', '4'], ['--self-test', '--samples', '3'],
  ]) {
    const result = spawnSync(dotnet, [assembly, ...args], { encoding: 'utf8', timeout: 15_000, windowsHide: true });
    assert.ifError(result.error); assert.equal(result.status, 1); assert.equal(result.stdout.trim(), ''); checks++;
  }
  const self = spawnSync(dotnet, [assembly, '--self-test'], { encoding: 'utf8', timeout: 15_000, windowsHide: true });
  assert.ifError(self.error); assert.equal(self.status, 0);
  const current = JSON.parse(self.stdout);
  assert.equal(current.schema, 'l12-password-work-selftest-v2');
  assert.deepEqual(current.readers, {
    currentReaderAcceptsLegacy: true, currentReaderAcceptsCandidate: true,
    compatibleReaderAcceptsLegacy: true, compatibleReaderAcceptsCandidate: true,
    currentWriterIterations: 120000, maximumReadIterations: 600000,
    legacyReaderAcceptsCandidate: false, rollbackRequiresCompatibleReader: true,
  }); checks++;
  // Schema validation fixture only: historical timings are not a new v2 measurement.
  const v2Fixture = structuredClone(report);
  v2Fixture.schema = 'l12-password-work-v2';
  v2Fixture.readers = current.readers;
  validatePasswordWorkReport(v2Fixture); checks++;
  for (const mutate of [
    r => { r.productionCapacityQualified = true; }, r => { r.cpuQuotaEmulated = true; },
    r => { r.readers.currentReaderAcceptsCandidate = !r.readers.currentReaderAcceptsCandidate; }, r => { r.readers.currentWriterIterations = 600000; },
    r => { r.bursts[0].maximumActive = 3; }, r => { r.bursts[0].busy++; },
    r => { r.serial[0].workMs.p95 = -1; }, r => { r.serial[0].workMs.samples++; },
    r => { r.passwordHash = 'forbidden'; }, r => { r.bursts[1] = structuredClone(r.bursts[0]); },
  ]) {
    for (const valid of [report, v2Fixture]) {
      const broken = structuredClone(valid); mutate(broken);
      assert.throws(() => validatePasswordWorkReport(broken)); checks++;
    }
  }
  for (const mutate of [
    r => { r.readers.maximumReadIterations = 600001; },
    r => { r.readers.legacyReaderAcceptsCandidate = true; },
    r => { r.readers.rollbackRequiresCompatibleReader = false; },
  ]) {
    const broken = structuredClone(v2Fixture); mutate(broken);
    assert.throws(() => validatePasswordWorkReport(broken)); checks++;
  }
  console.log(`Password work qualification passed: ${checks} checks; no production-capacity or upgrade activation claim.`);
}
