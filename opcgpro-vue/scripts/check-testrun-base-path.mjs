import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

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

console.log('[testrun base path] frontend route, API, WebSocket, and data contracts passed')
