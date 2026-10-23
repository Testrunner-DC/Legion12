import assert from 'node:assert/strict'
import fs from 'node:fs'
import {spawnSync} from 'node:child_process'
import {createTerminalRecipientRunner} from './terminal-recipient-runner.mjs'
const result=createTerminalRecipientRunner()
const syntax=spawnSync(process.execPath,['--input-type=module','--check'],{input:result.source,encoding:'utf8',windowsHide:true})
assert.equal(syntax.status,0,syntax.stderr)
for(const value of [
 'stageSeconds:60,httpIntervalMs:2000,wsIntervalMs:2000',
 'executionDeadlineMs:workloadDeadlineMs',
 'startedUtcMs+900000','startedUtcMs+1260000',
 "if(guard)try{",'recipientAdapter.bindRoom(args[0])',
 'assert.equal(recipientReport?.trace_complete,1)',
 "status:validated?'TERMINAL_RECIPIENT_TRACE_CAPTURED':'DIAGNOSTIC_INCOMPLETE'",
 'onlineCapacityAccepted:false,traceCaptured:validated',
 "createTerminalRecipientSessionManager as createNormalSessionManager",
 "observeTerminalIdentity as observeIdentity",
])assert.ok(result.source.includes(value),value)
assert.ok(result.source.indexOf('guardProof=await guard.finish()')<result.source.indexOf('if(controlPath&&recovery)'))
assert.ok(!result.source.includes('inspectNormalPressureEvidence(evidence,binding)'))
assert.ok(!result.source.includes('from \'./'))
assert.equal(result.evidence.reverseExact,true);assert.equal(result.evidence.originalWorkloadUnchanged,true)
console.log(JSON.stringify({status:'passed',cases:17,generatedSourceParsed:true,normalSourceUnchanged:true,remoteCalls:0,onlineCapacityAccepted:false}))
