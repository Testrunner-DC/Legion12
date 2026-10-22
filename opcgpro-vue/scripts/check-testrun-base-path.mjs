import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import assert from 'node:assert/strict'
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
expect(decks.match(/fetch\(deploymentPath\(/g)?.length >= 6, 'deck data does not follow deployment base')

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
