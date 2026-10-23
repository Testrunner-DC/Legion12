import assert from 'node:assert/strict'

export function assertTerminalServerTrace(trace) {
  assert.equal(typeof trace?.deploymentDrainEnabled, 'boolean', 'EXACT_LEASE_APPLICABILITY_REQUIRED')
  assert.equal(trace.numericOnly, true)
  const rows = trace.rows.filter(row => row.kind === 1)
  const summaries = trace.rows.filter(row => row.kind === 2)
  assert.equal(trace.rows.length, 61)
  assert.equal(rows.length, 60)
  assert.equal(summaries.length, 1)
  assert.equal(new Set(rows.map(row => `${row.slot}:${row.recipient}`)).size, 60)
  for (const row of rows) {
    assert.equal(row.schema, 4)
    assert.ok(Number.isSafeInteger(row.slot) && row.slot >= 0 && row.slot < 30)
    assert.ok(row.recipient === 0 || row.recipient === 1)
    for (const flag of ['status', 'receive_valid', 'receipt_valid', 'receipt_after_send_valid']) assert.equal(row[flag], 1)
    assert.equal(row.lease_valid, trace.deploymentDrainEnabled ? 1 : 0)
    if (trace.deploymentDrainEnabled) {
      assert.ok(Number.isSafeInteger(row.lease_start_rel_us) && row.lease_start_rel_us >= 0)
      assert.ok(Number.isSafeInteger(row.lease_end_rel_us) && row.lease_end_rel_us >= row.lease_start_rel_us)
    } else {
      // A disabled startup feature cannot supply a lease timing. Its absence
      // does not erase the independently measured receive/send boundaries.
      assert.equal(row.lease_start_rel_us, 0)
      assert.equal(row.lease_end_rel_us, 0)
    }
  }
  for (const [field, expected] of Object.entries({schema:4, kind:2, expected_commands:30,
    accepted_commands:30, expected_recipients:60, observed_recipients:60, completed:60,
    missing:0, deadline:0, duplicate:0, invalid:0, receipts:60, receipt_missing:0, receipt_duplicate:0}))
    assert.equal(summaries[0][field], expected, field)
  return {recipients:60, leaseTimeline:trace.deploymentDrainEnabled ? 'measured' : 'not-applicable', capacityClaim:false}
}
