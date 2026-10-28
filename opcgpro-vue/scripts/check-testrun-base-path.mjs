import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import assert from 'node:assert/strict'
import ts from 'typescript'
import { deploymentSourceModuleUrl } from './deployment-source-module.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const expect = (condition, message) => { if (!condition) throw new Error(message) }

const vite = read('vite.config.ts')
const router = read('src/router/index.ts')
const deployment = read('src/l12/deploymentBase.ts')
const net = read('src/l12/net.ts')
const platform = read('src/l12/platform.ts')
const cardLoader = read('src/data/CardLoader.ts')
const decks = read('src/l12/decks.ts')

expect(vite.includes("mode === 'testrun' ? '/testrun/' : '/'"), 'testrun Vite base is missing')
expect(router.includes('createWebHistory(import.meta.env.BASE_URL)'), 'router ignores deployment base')
expect(deployment.includes("url.pathname.replace(/\\/ws\\/?$/, '')"), 'HTTP base does not preserve the WebSocket mount path')
expect(net.includes('deploymentWebSocketPath()'), 'WebSocket endpoint ignores deployment base')
expect(platform.includes('endpointHttpBase(l12State.endpoint)'), 'platform API drops the mounted test path')
expect(cardLoader.match(/deploymentPath\(/g)?.length >= 3, 'card data does not follow deployment base')
function assertDeckDataPaths(source) {
  const tree = ts.createSourceFile('decks.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  assert.equal(tree.parseDiagnostics.length, 0, 'deck source must parse')
  const paths = []
  const noStore = new Set(['/data/l12/cards.s1.json', '/data/l12/cards.lookup.json', '/data/l12/cards.st.json'])
  function visit(node) {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'fetch') {
      const path = node.arguments[0]
      assert.ok(path && ts.isCallExpression(path) && ts.isIdentifier(path.expression)
        && path.expression.text === 'deploymentPath' && path.arguments.length === 1
        && ts.isStringLiteral(path.arguments[0]), 'each deck fetch must bind its literal path through deploymentPath')
      const value = path.arguments[0].text
      paths.push(value)
      if (noStore.has(value)) {
        const options = node.arguments[1]
        assert.ok(options && ts.isObjectLiteralExpression(options), `${value} must preserve request options`)
        const cache = options.properties.filter(item => ts.isPropertyAssignment(item)
          && (ts.isIdentifier(item.name) || ts.isStringLiteral(item.name)) && item.name.text === 'cache')
        assert.equal(cache.length, 1, `${value} must have one explicit cache policy`)
        assert.ok(ts.isStringLiteral(cache[0].initializer) && cache[0].initializer.text === 'no-store',
          `${value} must preserve no-store refresh semantics`)
      } else assert.equal(node.arguments.length, 1, `${value} must preserve its default fetch policy`)
    }
    ts.forEachChild(node, visit)
  }
  visit(tree)
  assert.deepEqual(paths.sort(), [...noStore, '/data/l12/preset-decks.s1.json', '/data/l12/preset-decks.s2.json'].sort(),
    'all five unique deck data sources must remain, without a second raw lookup fetch')
  const functions = new Map(tree.statements.filter(ts.isFunctionDeclaration).map(node => [node.name?.text, node]))
  const helper = functions.get('loadLookupCatalog')
  assert.ok(helper, 'shared raw lookup loader must exist')
  const helperPaths = []
  const inspectHelper = node => {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'fetch')
      helperPaths.push(node.arguments[0].arguments[0].text)
    ts.forEachChild(node, inspectHelper)
  }
  inspectHelper(helper)
  assert.deepEqual(helperPaths, ['/data/l12/cards.lookup.json'], 'raw lookup fetch must belong to its shared loader')
  for (const name of ['loadDeckCatalog', 'loadCardArchiveCatalog']) {
    const loader = functions.get(name)
    assert.ok(loader, `${name} must exist`)
    let calls = 0
    const inspect = node => {
      if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'loadLookupCatalog') calls++
      ts.forEachChild(node, inspect)
    }
    inspect(loader)
    assert.equal(calls, 1, `${name} must consume the same raw lookup loader once`)
  }
}
assertDeckDataPaths(decks)
const deckPathMutations = [
  ['lookup deployment prefix removed', source => source.replace("deploymentPath('/data/l12/cards.lookup.json')", "'/data/l12/cards.lookup.json'")],
  ['lookup redirected externally', source => source.replace("'/data/l12/cards.lookup.json'", "'https://example.invalid/cards.lookup.json'")],
  ['lookup path changed', source => source.replace("'/data/l12/cards.lookup.json'", "'/data/l12/cards.st.json'")],
  ['lookup cache weakened', source => source.replace("{ cache: 'no-store' })\n    .then", "{ cache: 'force-cache' })\n    .then")],
  ['playable loader bypasses helper', source => source.replace('    loadLookupCatalog(),', '    Promise.resolve([]),')],
  ['archive loader bypasses helper', source => source.replace(/(export async function loadCardArchiveCatalog\(\)[\s\S]*?)loadLookupCatalog\(\)/, '$1Promise.resolve([])')],
]
for (const [name, mutate] of deckPathMutations) {
  const mutated = mutate(decks.replaceAll('\r\n', '\n'))
  assert.notEqual(mutated, decks.replaceAll('\r\n', '\n'), `${name} mutation must hit the real source`)
  assert.throws(() => assertDeckDataPaths(mutated), undefined, name)
}

// Execute the immutable build-base functions, not a route/hostname heuristic.
for (const base of ['/', '/testrun/', '/preview/']) {
  const module = await import(deploymentSourceModuleUrl(base))
  assert.equal(module.usesSharedGallery, base === '/testrun/')
  assert.equal(module.sharedGalleryAdminPath, '/admin/content/alternate-arts')
  assert.equal(module.cardMediaPath('/api/site/media/art/desktop/card.webp'), base === '/testrun/'
    ? '/api/site/media/art/desktop/card.webp' : `${base}api/site/media/art/desktop/card.webp`)
  for (const isolated of ['/api/site/media-other/x', '/api/auth/me', '/api/me/alternate-arts', '/api/decks', '/ws']) {
    assert.equal(module.cardMediaPath(isolated), module.deploymentPath(isolated))
  }
  assert.equal(module.deploymentPath('/api/site/media/article'), `${base}api/site/media/article`)
  assert.equal(module.deploymentStorageKey('l12-auth-token'), base === '/' ? 'l12-auth-token' : `l12-auth-token@${base}`)
}
const nginx = read('../ops/server/legion12-testrun-path.nginx')
assert.equal((nginx.match(/proxy_pass http:\/\/127\.0\.0\.1:8083/g) ?? []).length, 0,
  'Shared-gallery routing must roll back with the frontend, not remain in a newer nginx fragment')
assert.ok(platform.includes('const requestBase = sharedGalleryRead ? location.origin : apiBase()'))
assert.match(nginx, /rewrite \^\/testrun\(\/api\/\.\*\)\$ \$1 break;\s+proxy_pass http:\/\/127\.0\.0\.1:8084;/)
assert.ok(read('src/l12/cardAssets.ts').includes('const deployed = cardMediaPath(normalized)'))
const galleryAdmin = read('src/l12/site/AdminAlternateArtsPanel.vue')
assert.ok(galleryAdmin.includes("usesSharedGallery ? 'rights' : 'gallery'"))
assert.ok(galleryAdmin.includes('v-else class="workspace-tabs"'))
assert.ok(galleryAdmin.includes(':href="sharedGalleryAdminPath" target="_blank" rel="noopener noreferrer"'))
assert.ok(galleryAdmin.includes('下方权益与派发仅属于测试服'))

console.log('[testrun base path] isolated account/API/WS/data and exact shared public gallery contracts passed')
