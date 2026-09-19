import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const output = process.env.L12_QA_OUT || path.join(process.env.TEMP || root, 'l12-morale-resource-icons')
fs.mkdirSync(output, { recursive: true })

const entry = `
import { createApp, h } from 'vue'
import PlayerMat from '/src/l12/game/PlayerMat.vue'
import '/src/style.css'

const player = {
  playerIndex: 0, name: '图标验收', deckName: '图标验收', faction: 'otherworld',
  master: { masterId: 'ST06-M1', masterName: '银臂努阿达', hp: 9, maxHp: 9 },
  libraryCount: 30, hand: [], handCount: 0, graveyard: [], field: [[null, null, null], [null, null, null]],
  morale: [
    { instanceId: 'ordinary', cardId: 'ST06-C1', tapped: false },
    { instanceId: 'lotus', cardId: 'S02-0010', tapped: true },
  ],
  temporaryMorale: 1, mulliganDone: true,
  specialZones: { runes: 0, trialLevel: 0, godPower: [], trials: [] },
}

createApp({ render: () => h('main', { class: 'qa-stage' }, [
  h(PlayerMat, { player, side: 'my', controllable: true, active: true, paymentChoiceIds: ['ordinary', 'lotus', 'temporary-morale:1'] }),
]) }).mount('#app')
`

let browser
const server = await createServer({
  root,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'morale-resource-icon-visual',
    resolveId(id) { if (id === '/__morale_resource_qa__.js') return id },
    load(id) { if (id === '/__morale_resource_qa__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if (!request.url?.match(/^\/__morale_resource_qa__(\?|$)/)) return next()
        response.setHeader('Content-Type', 'text/html')
        response.end('<style>body{margin:0;background:#070b0f}.qa-stage{width:1180px;height:420px;padding:50px}.l12-player-mat{width:1080px!important;height:300px!important}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__morale_resource_qa__.js"></script>')
      })
    },
  }],
})

try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1280, height: 520 } })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  await page.goto(`http://127.0.0.1:${port}/__morale_resource_qa__`)
  await page.locator('.resource-morale-stack').waitFor()

  const report = await page.evaluate(() => ({
    temporary: document.querySelector('[data-ui-contract="temporary-morale-selectable-site-logo"] img')?.getAttribute('src'),
    lotus: document.querySelector('[data-ui-contract="black-lotus-morale"] .black-lotus-logo')?.getAttribute('src'),
    temporaryAlt: document.querySelector('[data-ui-contract="temporary-morale-selectable-site-logo"] img')?.getAttribute('alt'),
    lotusAlt: document.querySelector('[data-ui-contract="black-lotus-morale"] .black-lotus-logo')?.getAttribute('alt'),
  }))
  if (report.temporary !== '/favicon.png' || report.temporaryAlt !== '临时士气') throw new Error(`临时士气图标错误：${JSON.stringify(report)}`)
  if (!report.lotus?.endsWith('/logo/black-lotus.png') || report.lotusAlt !== '黑色莲花') throw new Error(`黑色莲花图标错误：${JSON.stringify(report)}`)
  if (errors.length) throw new Error(errors.join('\n'))
  await page.locator('.resource-zone').screenshot({ path: path.join(output, 'morale-resource-icons.png') })
  console.log(`士气资源图标验收通过：${output}`)
} finally {
  await browser?.close()
  await server.close()
}
