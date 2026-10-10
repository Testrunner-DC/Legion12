import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import {
  chunkRecoveryStorageKey,
  decideChunkRecovery,
  isChunkLoadFailure,
} from '../src/chunkRecoveryCore.mjs'

const root = dirname(dirname(fileURLToPath(import.meta.url)))
const [mainSource, routerSource, recoverySource] = await Promise.all([
  readFile(join(root, 'src/main.ts'), 'utf8'),
  readFile(join(root, 'src/router/index.ts'), 'utf8'),
  readFile(join(root, 'src/chunkRecovery.ts'), 'utf8'),
])

const chunkFailures = [
  new TypeError('Failed to fetch dynamically imported module: https://example.test/assets/Page-old.js'),
  new Error('Unable to preload CSS for /testrun/assets/Page-old.css'),
  Object.assign(new Error('Loading chunk 42 failed'), { name: 'ChunkLoadError' }),
  new Error('Failed to load module script: Expected a JavaScript module script but the server responded with MIME type text/html.'),
]
for (const error of chunkFailures) {
  assert.equal(isChunkLoadFailure(error), true, error.message)
  assert.equal(decideChunkRecovery({ error, alreadyRecovered: false }), 'reload')
  assert.equal(decideChunkRecovery({ error, alreadyRecovered: true }), 'show-error')
}

const ordinaryErrors = [
  new Error('HTTP 401 Unauthorized'), new Error('HTTP 403 Forbidden'), new Error('HTTP 409 Conflict'),
  new Error('HTTP 422 Unprocessable Entity'), new Error('HTTP 429 Too Many Requests'), new Error('HTTP 500 Internal Server Error'),
  new Error('ordinary promise rejection'), new Error('component render failed'), new TypeError('Failed to fetch /api/decks'),
]
for (const error of ordinaryErrors) {
  assert.equal(isChunkLoadFailure(error), false, error.message)
  assert.equal(decideChunkRecovery({ error, alreadyRecovered: false }), 'ignore')
}

const key = chunkRecoveryStorageKey('release-a', '/cards')
assert.equal(key, chunkRecoveryStorageKey('release-a', '/cards'))
assert.notEqual(key, chunkRecoveryStorageKey('release-b', '/cards'))
assert.notEqual(key, chunkRecoveryStorageKey('release-a', '/rules'))

assert.ok(mainSource.indexOf('installChunkRecovery()') < mainSource.indexOf('initializeAuth().catch'), 'chunk recovery must install before auth/bootstrap and app mount')
assert.match(routerSource, /beginRouteNavigation\(to\.fullPath\)/)
assert.match(routerSource, /router\.afterEach\(to => \{ finishRouteNavigation\(to\.fullPath\) \}\)/)
assert.match(routerSource, /if \(!handleChunkLoadError\(error, 'router', to\.fullPath\)\) showRouteNavigationError\(to\.fullPath\)/)
assert.match(recoverySource, /window\.addEventListener\('vite:preloadError'/)
assert.doesNotMatch(recoverySource, /unhandledrejection/)
assert.match(recoverySource, /sessionStorage\.setItem/)
assert.match(recoverySource, /版本已更新，正在重新加载/)
assert.match(recoverySource, /系统已停止自动重试，避免反复刷新/)
assert.match(recoverySource, /window\.setTimeout\(\(\) => window\.location\.replace\(recoveryDestination\), 120\)/)
assert.match(recoverySource, /window\.name === `\$\{windowMarkerPrefix\}\$\{key\}`/)
assert.match(recoverySource, /routeLoadingTimer = window\.setTimeout/)
assert.match(recoverySource, /routeTimeoutTimer = window\.setTimeout/)
assert.match(recoverySource, /正在打开页面/)
assert.match(recoverySource, /页面加载时间过长/)
assert.match(recoverySource, /页面未能打开/)
assert.match(recoverySource, /if \(retryDestination\) window\.location\.replace\(retryDestination\)/)
assert.match(recoverySource, /return `\$\{base\.replace\(\/\\\/\$\/, ''\)\}\$\{targetPath\}`/)

console.log(`[route recovery] ${chunkFailures.length} chunk failures, ${ordinaryErrors.length} ordinary errors, loading/timeout/error visibility and one-shot recovery passed.`)
