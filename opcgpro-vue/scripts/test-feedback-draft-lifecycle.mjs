import assert from 'node:assert/strict'
import fs from 'node:fs'
import { execFileSync } from 'node:child_process'
import { parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import { ref, reactive, watch } from 'vue'

const source = process.argv.includes('--baseline')
  ? execFileSync('git', ['show', 'HEAD:opcgpro-vue/src/l12/site/GlobalBugFeedback.vue'], { encoding: 'utf8' })
  : fs.readFileSync(new URL('../src/l12/site/GlobalBugFeedback.vue', import.meta.url), 'utf8')
const script = parse(source).descriptor.scriptSetup?.content
assert(script, 'Execute the actual feedback component script, not a duplicate implementation')
const parsedScript = ts.createSourceFile('feedback.ts', script, ts.ScriptTarget.ES2022, true)
const captureImports = parsedScript.statements.filter(node => ts.isImportDeclaration(node)
  && node.moduleSpecifier.text === '@/l12/net'
  && node.importClause?.namedBindings && ts.isNamedImports(node.importClause.namedBindings)
  && node.importClause.namedBindings.elements.some(item => item.name.text === 'captureBugClientDiagnostic'
    && (!item.propertyName || item.propertyName.text === 'captureBugClientDiagnostic')))
assert.equal(captureImports.length, 1, 'Use the existing diagnostic whitelist exporter')
const prepared = ts.transform(ts.createSourceFile('feedback.ts', script, ts.ScriptTarget.ES2022, true), [context => node => {
  const visit = current => {
    if (ts.isImportDeclaration(current)) return undefined
    if (ts.isPropertyAccessExpression(current) && current.name.text === 'env' && ts.isMetaProperty(current.expression)) return ts.factory.createIdentifier('testEnv')
    return ts.visitEachChild(current, visit, context)
  }
  return ts.visitNode(node, visit)
}]).transformed[0]
const compiled = ts.transpileModule(ts.createPrinter().printFile(prepared), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.None } }).outputText
const create = new Function('exports', 'ref', 'reactive', 'watch', 'onMounted', 'onBeforeUnmount', 'useRoute', 'captureBugClientDiagnostic', 'platformState', 'submitBug', 'l12State', 'window', 'testEnv', `${compiled}\nreturn {submit,open,form,busy,message}`)
const deferred = () => { let resolve, reject; const promise = new Promise((a,b) => { resolve=a; reject=b }); return {promise,resolve,reject} }
let checks = 0
for (const scenario of ['success', 'failure', 'reopen-success', 'reopen-failure', 'edit-pending', 'identity-before-post', 'token-before-post', 'identity-roundtrip-before-post', 'context-before-post', 'identity-after-post', 'token-after-post', 'unmount-before-post', 'unmount-after-post', 'identity-roundtrip', 'edit-roundtrip']) {
  const diagnostic = deferred(), post = deferred(), writes = [], stops = [], dispose = []
  const route = reactive({path:'/me'})
  const platformState = reactive({account:{id:'synthetic-self'},token:''})
  const l12State = reactive({room:{roomCode:'synthetic-room-old'},game:{matchId:'synthetic-match-old'}})
  const component = create({},ref,reactive,(...args)=>{const stop=watch(...args);stops.push(stop);return stop},
    fn=>fn(),fn=>dispose.push(fn),()=>route,async page=>{await diagnostic.promise;return {currentRoute:page,roomCode:l12State.room.roomCode,matchId:l12State.game.matchId}},
    platformState,body=>{writes.push(body);return post.promise},l12State,{addEventListener(){},removeEventListener(){}},{VITE_APP_VERSION:'synthetic-version'})
  component.open.value=true
  component.form.bugDescription='原始合成反馈'
  const pending=component.submit()
  const duplicate=component.submit()
  assert.equal(component.busy.value,true,'busy covers diagnostics and POST');checks++
  if(scenario==='identity-before-post')platformState.account={id:'synthetic-next'}
  if(scenario==='token-before-post')platformState.token='synthetic-renewed-token'
  if(scenario==='identity-roundtrip-before-post'){platformState.account={id:'synthetic-next'};platformState.account={id:'synthetic-self'};component.form.bugDescription='切回账号的新草稿'}
  if(scenario==='unmount-before-post')dispose.forEach(fn=>fn())
  if(scenario==='context-before-post'){route.path='/elsewhere';l12State.room.roomCode='synthetic-room-new';l12State.game.matchId='synthetic-match-new'}
  diagnostic.resolve()
  // Flush the actual diagnostic await and submit continuation, without elapsed-time sleeps.
  await Promise.resolve();await Promise.resolve();await Promise.resolve()
  if(scenario.endsWith('before-post')&&scenario!=='context-before-post'){
    await pending
    await duplicate
    assert.equal(writes.length,0,'changed identity or disposed component never posts old draft');checks++
    if(scenario!=='unmount-before-post'){
      assert.equal(component.busy.value,false,'canceled pre-POST request releases busy');checks++
      if(scenario==='token-before-post'){assert.equal(component.form.bugDescription,'原始合成反馈','same-account token renewal retains draft');checks++}
      if(scenario==='identity-roundtrip-before-post'){assert.equal(component.form.bugDescription,'切回账号的新草稿','account roundtrip does not revive old request');checks++}
    }
  }else{
    assert.equal(writes.length,1,'duplicate call must not send a second POST');checks++
    assert.equal(writes[0].page,'/me');assert.equal(writes[0].roomCode,'synthetic-room-old');assert.equal(writes[0].matchId,'synthetic-match-old');checks+=3
    assert.equal(writes[0].clientDiagnostic.roomCode,'synthetic-room-old');assert.equal(writes[0].clientDiagnostic.matchId,'synthetic-match-old');checks+=2
    assert.equal(writes[0].clientDiagnostic.currentRoute,'/me','diagnostic uses submitted route');checks++
    assert.deepEqual(Object.keys(writes[0].clientDiagnostic).sort(),['currentRoute','matchId','roomCode'],'do not add raw state, identity or arbitrary browser data to diagnostic');checks++
    if(scenario.startsWith('reopen')){component.open.value=false;component.open.value=true;component.form.bugDescription='新草稿'}
    if(scenario==='edit-pending')component.form.bugDescription='新草稿'
    if(scenario==='edit-roundtrip'){component.form.bugDescription='新草稿';component.form.bugDescription='原始合成反馈'}
    if(scenario==='identity-after-post'){platformState.account={id:'synthetic-next'};component.form.bugDescription='新账号草稿'}
    if(scenario==='token-after-post'){platformState.token='synthetic-renewed-token';component.form.bugDescription='新会话草稿'}
    if(scenario==='identity-roundtrip'){platformState.account={id:'synthetic-next'};platformState.account={id:'synthetic-self'};component.form.bugDescription='重登录草稿'}
    if(scenario==='unmount-after-post')dispose.forEach(fn=>fn())
    if(scenario.endsWith('failure')||scenario==='failure')post.reject(new Error('合成提交失败'))
    else post.resolve({id:'synthetic-report'})
    await pending
    await duplicate
    const expected=scenario==='success'||scenario==='context-before-post'?'':scenario==='identity-after-post'?'新账号草稿':scenario==='token-after-post'?'新会话草稿':scenario==='identity-roundtrip'?'重登录草稿':scenario==='failure'||scenario==='unmount-after-post'||scenario==='edit-roundtrip'?'原始合成反馈':'新草稿'
    assert.equal(component.form.bugDescription,expected,`${scenario}: completion cannot erase newer text`);checks++
    assert.equal(component.message.value,scenario==='success'||scenario==='context-before-post'?'已提交：synthetic-report':scenario==='failure'?'合成提交失败':'',`${scenario}: only the owning request writes UI`);checks++
    if(scenario!=='unmount-after-post'){assert.equal(component.busy.value,false,'finished request releases busy');checks++}
  }
  stops.forEach(stop=>stop())
}
console.log(`Feedback draft lifecycle passed: ${checks} actual-script assertions, 15 deterministic scenarios`)
export const usesWhitelistedBugDiagnostic = true
