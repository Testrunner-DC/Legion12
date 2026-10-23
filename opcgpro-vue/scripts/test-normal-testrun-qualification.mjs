import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {planQualificationSessionRecovery} from './qualify-normal-testrun-lifecycle.mjs'
import {createSshAdmission} from './testrun-pressure-runtime.mjs'
import {EVIDENCE_ROOT,CACHE_ROOT,assertGeneratedDirectories,digest} from './testrun-pressure-protocol.mjs'
const id=crypto.randomUUID().replaceAll('-',''),output=path.join(EVIDENCE_ROOT,'qualification-unit-'+id),cache=path.join(CACHE_ROOT,'qualification-unit-'+id)
assertGeneratedDirectories(output,cache);fs.mkdirSync(output,{recursive:true});fs.mkdirSync(cache,{recursive:true})
const empty=()=>({original:{intent:false,cipherSha256:null},recovery:{intent:false,cipherSha256:null}})
const current=()=>({protectedSessions:[],sessionAttempts:[]})
const attempt=kind=>({kind,phase:'acquire-requested'})
const results=[];const check=(name,fn)=>{fn();results.push({name,status:'passed'})}
check('no-acquisition-request-is-not-pending',()=>assert.equal(planQualificationSessionRecovery(current(),empty()).allAcquisitionsAccountedFor,true))
check('flushed-DPAPI-with-lost-ACK-is-recovered',()=>{
 const report=current(),slots=empty();report.sessionAttempts.push(attempt('original'));slots.original={intent:true,cipherSha256:'A'.repeat(64)}
 const plan=planQualificationSessionRecovery(report,slots);assert.deepEqual(plan.recover,[{kind:'original',cipherSha256:'A'.repeat(64)}]);assert.deepEqual(plan.pending,[])
})
check('durable-intent-even-with-lost-report-includes-cipher',()=>{
 const slots=empty();slots.recovery={intent:true,cipherSha256:'B'.repeat(64)};assert.equal(planQualificationSessionRecovery(current(),slots).recover[0].kind,'recovery')
})
check('acquisition-response-missing-is-pending',()=>{
 const report=current();report.sessionAttempts.push(attempt('recovery'));const plan=planQualificationSessionRecovery(report,empty());assert.equal(plan.allAcquisitionsAccountedFor,false);assert.equal(plan.pending[0].kind,'recovery')
})
check('intent-file-without-cipher-is-pending',()=>{
 const slots=empty();slots.original.intent=true;assert.equal(planQualificationSessionRecovery(current(),slots).pending[0].kind,'original')
})
check('already-exactly-revoked-401-without-cipher-accounted',()=>{
 const report=current();report.sessionAttempts.push(attempt('original'));report.revocation={results:[{kind:'original',afterStatus:401}]};assert.equal(planQualificationSessionRecovery(report,empty()).allAcquisitionsAccountedFor,true)
})
check('legacy-does-not-invent-no-attempt-proof',()=>{
 const report={protectedSessions:[{kind:'original',cipherSha256:'A'.repeat(64)}]},slots=empty();slots.original.cipherSha256='A'.repeat(64)
 const plan=planQualificationSessionRecovery(report,slots);assert.equal(plan.recover.length,1);assert.equal(plan.allAcquisitionsAccountedFor,false);assert.equal(plan.pending[0].kind,'recovery')
})
for(const [name,mutate] of [['changed-cipher',r=>r.protectedSessions.push({kind:'original',cipherSha256:'B'.repeat(64)})],['unknown-kind',r=>r.sessionAttempts.push(attempt('formal'))],['wrong-phase',r=>r.sessionAttempts.push({kind:'original',phase:'complete'})],['duplicate-protected',r=>r.protectedSessions.push(...[0,1].map(()=>({kind:'original',cipherSha256:'A'.repeat(64)})))]])
 check('reject-'+name,()=>{const report=current(),slots=empty();slots.original.cipherSha256='A'.repeat(64);mutate(report);assert.throws(()=>planQualificationSessionRecovery(report,slots))})
const source=fs.readFileSync(path.join(import.meta.dirname,'qualify-normal-testrun-lifecycle.mjs'),'utf8')
check('independent-live-identity-not-replaced-with-static-ready',()=>assert.ok(source.includes('const value=assertRuntimeIdentity(await observeIdentity(binding),binding)')))
check('acquisition-intent-persisted-before-remote-write',()=>assert.ok(source.indexOf("durableJson(path.join(cache,kind+'-acquire-intent.json')")<source.indexOf('const session=await acquireTestSession(binding)')))
let clock=0;const sleeps=[],starts=[],admission=createSshAdmission({now:()=>clock,wait:async value=>{sleeps.push(value);clock+=value}})
for(let index=0;index<10;index++){await admission.take();starts.push(clock)}
check('own-new-SSH-controls-at-least-eight-seconds-apart',()=>{assert.equal(starts[0],0);assert.ok(starts.slice(1).every((time,index)=>time-starts[index]===8000));assert.ok(sleeps.every(value=>value===8000))})
clock+=1000;admission.noteStart();await admission.take()
check('persistent-guard-start-also-reserves-ingress-slot',()=>assert.equal(clock,starts.at(-1)+9000))
const proof={schema:1,status:'passed',cases:results.length,results,networkCalls:0,onlineCapacityAccepted:false,sources:['qualify-normal-testrun-lifecycle.mjs','test-normal-testrun-qualification.mjs','testrun-pressure-runtime.mjs'].map(file=>({file,sha256:digest(fs.readFileSync(path.join(import.meta.dirname,file)))})),boundary:'Pure recovery-plan and owned SSH pacing checks only. No Linux session, guard lifecycle or 30+10 acceptance.'}
fs.writeFileSync(path.join(output,'qualification-tests.json'),JSON.stringify(proof,null,2),{flag:'wx'});console.log(JSON.stringify({status:proof.status,cases:proof.cases,output,networkCalls:0}))
