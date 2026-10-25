import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'
import { createMemoryHistory, createRouter } from 'vue-router'

const read = path => fs.readFileSync(new URL(`../${path}`, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const routerSource = read('src/router/index.ts')
const compiledRouter = ts.transpileModule(
  routerSource.replace('import.meta.env.BASE_URL', "'/'"),
  { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } },
).outputText
const SyntheticPage = { render: () => null }

function createActualRouter({ verified = true, account = { id: 'synthetic-user' }, admin = true } = {}) {
  const authState = { verified }
  const platformState = { account }
  const canAccessAdmin = { value: admin }
  let refreshCalls = 0
  const module = { exports: {} }
  const syntheticRequire = id => {
    if (id === 'vue-router') return { createRouter, createWebHistory: createMemoryHistory }
    if (id === '@/l12/platform') return {
      authState,
      canAccessAdmin,
      platformState,
      refreshCurrentAccount: async () => { refreshCalls++ },
    }
    if (id === '@/chunkRecovery') return {
      beginRouteNavigation() {},
      finishRouteNavigation() {},
      handleChunkLoadError: () => false,
      showRouteNavigationError() {},
    }
    if (id === '@/l12/site/adminSections') return { adminSections: [] }
    if (id.startsWith('@/')) return { default: SyntheticPage }
    throw new Error(`Unexpected router dependency: ${id}`)
  }
  new Function('require', 'module', 'exports', 'window', compiledRouter)(
    syntheticRequire,
    module,
    module.exports,
    { setTimeout, clearTimeout },
  )
  return {
    router: module.exports.router,
    authState,
    platformState,
    canAccessAdmin,
    refreshCalls: () => refreshCalls,
  }
}

async function navigate(router, target) {
  await router.push(target)
  await router.isReady()
  return router.currentRoute.value
}

let checks = 0
const guest = createActualRouter({ verified: false, account: null, admin: false })
const unknownTarget = '/missing-page?source=shared#details'
assert.equal(guest.router.resolve(unknownTarget).name, 'not-found', 'unknown URL must match the actual named not-found route'); checks++
let route = await navigate(guest.router, unknownTarget)
assert.equal(route.name, 'not-found'); assert.equal(route.fullPath, unknownTarget); checks += 2
assert.equal(guest.refreshCalls(), 0, 'public not-found navigation must not invoke an account refresh'); checks++

const adminUnknownTarget = '/admin/private-area?ticket=secret#hidden'
route = await navigate(guest.router, adminUnknownTarget)
assert.equal(route.name, 'not-found', 'unknown admin-looking URLs must not reveal or enter an admin route'); checks++
assert.equal(route.fullPath, adminUnknownTarget, 'the router must retain navigation context without rendering it'); checks++
assert.equal(guest.refreshCalls(), 0, 'unknown admin-looking URLs must not invoke the admin/account guard'); checks++

const notFoundSource = read('src/l12/site/NotFoundPage.vue')
const parsedNotFound = parse(notFoundSource, { filename: 'NotFoundPage.vue' })
assert.deepEqual(parsedNotFound.errors, [], 'the actual not-found SFC must parse without errors'); checks++
assert.match(routerSource, /name: 'not-found', component: \(\) => import\('@\/l12\/site\/NotFoundPage\.vue'\)/); checks++
assert.match(notFoundSource, />页面不存在</); assert.match(notFoundSource, /to="\/"/); checks += 2
assert.doesNotMatch(notFoundSource, /useRoute|\$route|fullPath|pathMatch|route\.path/, 'the 404 page must not echo an untrusted URL'); checks++
assert.match(notFoundSource, /font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif/, 'the page must keep the shared Chinese sans-serif stack'); checks++

const authorized = createActualRouter()
const legacyRedirects = [
  ['/battle/lobby?from=old#lobby', '/battle?from=old#lobby'],
  ['/lobby?from=old#lobby', '/battle?from=old#lobby'],
  ['/tournaments?from=old#list', '/battle/tournaments?from=old#list'],
  ['/friends?from=old#friends', '/battle/friends?from=old#friends'],
  ['/rankings?from=old#rankings', '/battle/rankings?from=old#rankings'],
  ['/records?from=old#records', '/battle/records?from=old#records'],
]
for (const [from, expected] of legacyRedirects) {
  route = await navigate(authorized.router, from)
  assert.equal(route.fullPath, expected, `${from} must retain its legacy redirect, query and hash`); checks++
}

route = await navigate(authorized.router, '/news/public-article?from=share#content')
assert.equal(route.name, 'news-detail'); assert.equal(route.fullPath, '/news/public-article?from=share#content'); checks += 2
route = await navigate(authorized.router, '/decks/public-deck?from=share#detail')
assert.equal(route.name, 'public-deck-detail'); assert.equal(route.fullPath, '/decks/public-deck?from=share#detail'); checks += 2

const signedOut = createActualRouter({ verified: false, account: null, admin: false })
route = await navigate(signedOut.router, '/battle/friends?invite=one#friends')
assert.equal(route.name, 'me'); assert.equal(route.query.redirect, '/battle/friends?invite=one#friends'); checks += 2
const nonAdmin = createActualRouter({ admin: false })
route = await navigate(nonAdmin.router, '/admin/system/security?from=link#security')
assert.equal(route.name, 'me'); assert.equal(route.query.redirect, '/admin/system/security?from=link#security'); checks += 2
const administrator = createActualRouter()
route = await navigate(administrator.router, '/admin/system/security?from=link#security')
assert.equal(route.name, 'admin-security'); assert.equal(route.fullPath, '/admin/system/security?from=link#security'); checks += 2

console.log(`Not-found routing passed: ${checks} actual-router assertions across unknown, privacy, legacy redirect, sharing and guard scenarios`)
