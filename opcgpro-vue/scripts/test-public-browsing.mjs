import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
const read = file => fs.readFileSync(new URL(`../${file}`, import.meta.url), 'utf8')
const platform = read('src/l12/platform.ts')
const router = read('src/router/index.ts')
const syntax = ts.createSourceFile('router.ts', router, ts.ScriptTarget.Latest, true)
const routes = new Map()
const visit = node => {
  if (ts.isObjectLiteralExpression(node)) {
    const properties = new Map(node.properties.filter(ts.isPropertyAssignment).map(p => [p.name.getText(syntax).replace(/['"]/g,''), p.initializer]))
    const route = properties.get('path')
    if (route && ts.isStringLiteral(route)) routes.set(route.text, properties.get('meta')?.getText(syntax) || '')
  }
  ts.forEachChild(node, visit)
}
visit(syntax)
for (const path of ['/battle/tournaments','/battle/tournaments/:code','/battle/rankings']) { assert.ok(routes.has(path), `${path} must exist`); assert.ok(!/requires(?:Account|Admin)/.test(routes.get(path)),`${path} must be public`) }
for (const path of ['/battle','/battle/friends','/battle/records','/battle/records/replay/json','/battle/records/replay/:matchId']) assert.match(routes.get(path),/requiresAccount: true/,`${path} must stay authenticated`)
assert.match(routes.get('/admin'),/requiresAdmin: true/)
const body = platform.slice(platform.indexOf('export async function platformRequest<T>'),platform.indexOf('\nfunction remember(',platform.indexOf('export async function platformRequest<T>')))
const js = ts.transpileModule(body.replace('export async function','async function'),{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText
let checks = 9
const platformState={token:'admin-token'}; const authState={verified:true}; const requests=[]
let forgotten=0;let refreshed=0;let status=200
class PlatformRequestError extends Error { constructor(message,status){super(message);this.status=status} }
class RequestDeadlineError extends Error {}
const scope={platformState,authState,platformSessionVersion:1,PlatformRequestError,RequestDeadlineError,
 PLATFORM_UPLOAD_TIMEOUT_MS:10000,PLATFORM_READ_TIMEOUT_MS:10000,PLATFORM_MUTATION_TIMEOUT_MS:10000,PLATFORM_READ_MAX_ATTEMPTS:1,
 requestFingerprint:async()=>undefined,retryableReadFailure:()=>false,retryAfterMilliseconds:()=>0,apiBase:()=> 'https://fixture.invalid',
 forgetAccount:()=>{forgotten++},refreshCurrentAccount:async()=>{refreshed++},platformRequestCoordinator:{execute:options=>options.run(new AbortController().signal)}}
const request = new Function(...Object.keys(scope),`${js};return platformRequest`)(...Object.values(scope))
const originalFetch=globalThis.fetch
globalThis.fetch=async(url,init)=>{requests.push({url,init});return new Response(JSON.stringify(status===200?{ok:true}:{message:'denied'}),{status})}
try{
 await request('/api/public/tournaments/summaries',{credentials:'include',cache:'force-cache',headers:{Authorization:'Bearer explicit-token'}})
 assert.equal(requests.at(-1).init.headers.get('Authorization'),null);checks++
 assert.equal(requests.at(-1).init.credentials,'omit');checks++
 assert.equal(requests.at(-1).init.cache,'no-store');checks++
 for(const code of [401,403,404]){status=code;await assert.rejects(request('/api/public/tournaments/code/private'));checks++}
 assert.equal(forgotten,0);assert.equal(refreshed,0);assert.equal(authState.verified,true);checks+=3
 status=200;await request('/api/tournaments/summaries');assert.equal(requests.at(-1).init.headers.get('Authorization'),'Bearer admin-token');checks++
 await request('/api/public/tournaments/summaries',{method:'POST',body:'{}'});assert.equal(requests.at(-1).init.headers.get('Authorization'),'Bearer admin-token');checks++
 status=401;await assert.rejects(request('/api/tournaments/summaries'));assert.equal(forgotten,1);checks++
 platformState.token='';status=200;await request('/api/public/tournaments/summaries');assert.equal(requests.at(-1).init.headers.get('Authorization'),null);checks++
}finally{globalThis.fetch=originalFetch}
console.log(`C3 public browsing route/actual request-handler tests passed: ${checks}`)
