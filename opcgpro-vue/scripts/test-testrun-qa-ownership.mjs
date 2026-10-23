import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {inspectQaOwnership,readQaOwnership,QA_REPO,QA_MANIFEST} from './check-testrun-qa-ownership.mjs'

const actual=readQaOwnership(),manifest=JSON.parse(fs.readFileSync(path.join(QA_REPO,QA_MANIFEST)))
let cases=1
const fixture={schema:1,formalMutationsAllowed:false,normalConfirmationMs:3000,normalLifecycleMs:900000,
  sources:['opcgpro-vue/scripts/probe.mjs'],entries:[{path:'opcgpro-vue/scripts/probe.mjs',mode:'test-only',target:'https://legion-12.com/testrun/'}],immutableInputs:[],completeLegacyCodeClosure:false}
const fakeTracked=new Set(fixture.sources)
for(const [name,mutate]of[
 ['unversioned source',value=>value.sources.push('ops/qa/missing.py')],
 ['escaping path',value=>value.sources.push('ops/qa/../secret.py')],
 ['duplicate source',value=>value.sources.push(value.sources[0])],
 ['formal mutation',value=>{value.formalMutationsAllowed=true}],
 ['changed confirmation',value=>{value.normalConfirmationMs=3001}],
 ['changed lifecycle',value=>{value.normalLifecycleMs=901000}],
 ['formal target',value=>{value.entries[0].target='https://legion-12.com/'}],
 ['unowned entry',value=>{value.entries[0].path='ops/qa/unowned.ps1'}],
 ['claiming legacy closure',value=>{value.completeLegacyCodeClosure=true}],
 ['bad input hash',value=>value.immutableInputs.push({path:'D:/fixture.mjs',sha256:'wrong'})],
]) {
 const value=structuredClone(fixture);mutate(value)
 assert.throws(()=>inspectQaOwnership(value,{tracked:fakeTracked,read:()=>Buffer.from('export const valid=true')}),name);cases++
}
assert.throws(()=>inspectQaOwnership(fixture,{tracked:fakeTracked,read:()=>Buffer.from("import x from './untracked.mjs'")}));cases++
const privateHeader=['-----BEGIN','OPENSSH PRIVATE KEY-----'].join(' ')
assert.throws(()=>inspectQaOwnership(fixture,{tracked:fakeTracked,read:()=>Buffer.from(privateHeader)}));cases++
const pinned=structuredClone(fixture);pinned.immutableInputs=[{path:'D:/fixture.mjs',sha256:crypto.createHash('sha256').update('expected').digest('hex').toUpperCase()}]
assert.throws(()=>inspectQaOwnership(pinned,{tracked:fakeTracked,read:()=>Buffer.from('different'),verifyLocalInputs:true}));cases++
const fixtureGenerator=fs.readFileSync(path.join(QA_REPO,'ops/qa/TestAnalyticsHistory/Program.cs'),'utf8')
assert.match(fixtureGenerator,/args\.Length != 4/)
assert.match(fixtureGenerator,/deployment-commit"\)\.Trim\(\) != expectedCommit/)
assert.match(fixtureGenerator,/Assembly\.Location[\s\S]*?!= expectedSha/)
assert.match(fixtureGenerator,/Path\.Combine\("\/tmp", "l12-analytics-fixture-"/)
assert.match(fixtureGenerator,/Existing fixture is never reused/)
assert.doesNotMatch(fixtureGenerator,/BA157FD1|131989a65/)
cases++

// Execute the actual pre-browser initialization blocks with a guard stub. A
// child's PowerShell environment must never be mistaken for its parent Node's.
for(const [relative,start,end]of[
 ['opcgpro-vue/scripts/qa-tutorial-candidate-images-20261023.mjs','const out = process.env.L12_TUTORIAL_UI_OUTPUT','const report = {'],
 ['opcgpro-vue/scripts/test-master-art-editor-grant-refresh-20261023.mjs','  const out = process.env.L12_QA_OUT','  const expectedDll ='],
]) {
 const source=fs.readFileSync(path.join(QA_REPO,relative),'utf8'),a=source.indexOf(start),b=source.indexOf(end,a)
 assert.ok(a>=0&&b>a)
 const execute=new Function('process','fs','path','assert','repo','execFileSync',source.slice(a,b))
 const cache=path.resolve('synthetic-governed-cache'),out=path.resolve('synthetic-evidence'),host=path.resolve('synthetic-host')
 const make=()=>({env:{TEMP:'C:/unsafe-temp',TMP:'C:/unsafe-temp',TMPDIR:'C:/unsafe-temp',
   L12_QA_OUT:out,L12_WORK_CACHE:cache,L12_MASTER_HOST:host,L12_MASTER_ART_ASSETS:path.resolve('synthetic-assets'),
   L12_TUTORIAL_UI_OUTPUT:out,L12_TUTORIAL_UI_CACHE:cache}})
 let writes=0,guards=0;const process=make()
 const io={existsSync:()=>false,readdirSync:()=>[],mkdirSync(){writes++;for(const key of ['TEMP','TMP','TMPDIR'])assert.equal(process.env[key],cache)}}
 execute(process,io,path,assert,QA_REPO,()=>{guards++;return Buffer.alloc(0)})
 assert.equal(guards,1);assert.equal(writes,2);cases++
 const refused=make();writes=0
 assert.throws(()=>execute(refused,io,path,assert,QA_REPO,()=>{throw new Error('PHYSICAL_ROOT_REFUSED')}),/PHYSICAL_ROOT_REFUSED/)
 assert.equal(writes,0);assert.equal(refused.env.TEMP,'C:/unsafe-temp');cases++
}
console.log(JSON.stringify({status:'passed',cases,ownedSources:actual.sourceCount,actualNodeEntryDataflow:true,networkCalls:0,onlineCapacityAccepted:false}))
