import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { assertTerminalServerTrace } from './terminal-recipient-trace-contract.mjs'

const trace = enabled => ({numericOnly:true, deploymentDrainEnabled:enabled, rows:[
  ...Array.from({length:60}, (_, index) => ({schema:4, kind:1, slot:Math.floor(index/2), recipient:index%2,
    status:1, receive_valid:1, receipt_valid:1, receipt_after_send_valid:1, lease_valid:enabled?1:0,
    lease_start_rel_us:enabled?1:0, lease_end_rel_us:enabled?2:0})),
  {schema:4, kind:2, expected_commands:30, accepted_commands:30, expected_recipients:60,
    observed_recipients:60, completed:60, missing:0, deadline:0, duplicate:0, invalid:0,
    receipts:60, receipt_missing:0, receipt_duplicate:0},
]})
let cases=0
assert.equal(assertTerminalServerTrace(trace(true)).leaseTimeline, 'measured');cases++
assert.equal(assertTerminalServerTrace(trace(false)).leaseTimeline, 'not-applicable');cases++
for (const [label, mutate] of [
  ['unknown configuration', value => {delete value.deploymentDrainEnabled}],
  ['unknown string configuration', value => {value.deploymentDrainEnabled='false'}],
  ['disabled but apparently measured', value => {value.rows[0].lease_valid=1}],
  ['unmeasured lease with fabricated timestamp', value => {value.rows[0].lease_end_rel_us=3}],
  ['missing recipient', value => {value.rows.splice(0,1)}],
  ['duplicate recipient', value => {value.rows[1]={...value.rows[0]}}],
  ['unknown room slot', value => {value.rows[0].slot=30}],
  ['missing receive boundary', value => {value.rows[0].receive_valid=0}],
  ['missing receipt', value => {value.rows[0].receipt_valid=0}],
  ['summary still incomplete', value => {value.rows.at(-1).missing=4}],
]) {
  const value=trace(false);mutate(value);assert.throws(()=>assertTerminalServerTrace(value),label);cases++
}
const absent=trace(true);absent.rows[0].lease_valid=0
assert.throws(()=>assertTerminalServerTrace(absent),'enabled lease timing may not silently disappear');cases++
const legacy=trace(false)
delete legacy.deploymentDrainEnabled
legacy.rows.splice(0,4)
Object.assign(legacy.rows.at(-1), {accepted_commands:28,observed_recipients:56,completed:56,missing:4,receipts:56})
assert.throws(()=>assertTerminalServerTrace(legacy),'old incomplete evidence remains rejected');cases++
assert.throws(()=>assertTerminalServerTrace({...legacy,deploymentDrainEnabled:false}),
  'even hypothetical applicability cannot make the missing four recipients complete');cases++
if(process.argv[2]) {
  const raw=readFileSync(process.argv[2])
  assert.equal(createHash('sha256').update(raw).digest('hex').toUpperCase(),
    'B7BAA68EE92440B277EAD0EBEF106DA421EEA1F33E77D78D52F6C5013D77A76F')
  const actual=JSON.parse(raw)
  assert.throws(()=>assertTerminalServerTrace(actual));cases++
  assert.throws(()=>assertTerminalServerTrace({...actual,deploymentDrainEnabled:false}));cases++
}
console.log(JSON.stringify({status:'passed',cases,networkCalls:0,originalCapacityGateChanged:false,oldResultReclassified:false}))
