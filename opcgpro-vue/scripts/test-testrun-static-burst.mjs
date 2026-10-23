import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import { terminalBurstSizes, boundTerminalBurst } from './measure-testrun-static-burst.mjs'
const fixture=()=>({expectedCommit:{value:'2eda18c115b0810296cb9c35cfebf60c35b120e5',source:'exact-normal-binding-and-actual-running-process'},target:{baseUrl:'https://legion-12.com/testrun/',productionTouched:false},
  terminalFrames:Array.from({length:60},(_,numericClientSlot)=>({numericClientSlot,wireBytes:160000+numericClientSlot,terminal:true}))})
let cases=0
assert.equal(terminalBurstSizes(fixture()).length,60);cases++
for(const mutate of [
 value=>value.terminalFrames.pop(),
 value=>value.terminalFrames.push({...value.terminalFrames[0]}),
 value=>value.terminalFrames[0].numericClientSlot=1,
 value=>value.terminalFrames[0].numericClientSlot=60,
 value=>value.terminalFrames[0].wireBytes=318437,
 value=>value.terminalFrames[0].wireBytes=0,
 value=>value.terminalFrames[0].wireBytes=1.5,
 value=>value.terminalFrames[0].terminal=false,
 value=>value.target='https://legion-12.com/',
 value=>value.target.productionTouched=true,
 value=>value.expectedCommit='0'.repeat(40),
 value=>value.terminalFrames.forEach(row=>row.wireBytes=318436),
]){const bad=fixture();mutate(bad);assert.throws(()=>terminalBurstSizes(bad));cases++}
assert.throws(()=>boundTerminalBurst(Buffer.from(JSON.stringify(fixture()))),'a plausible rewritten capture is still not the frozen original');cases++
if(process.argv[2]){assert.equal(boundTerminalBurst(readFileSync(process.argv[2])).length,60);cases++}
console.log(JSON.stringify({status:'passed',cases,actualNetworkCalls:0,generatedOutputs:0}))
