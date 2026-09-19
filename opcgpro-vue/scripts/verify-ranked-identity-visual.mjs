import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const output = process.env.L12_QA_OUT || path.join(process.env.TEMP || root, 'l12-ranked-identity-visual')
fs.mkdirSync(output, { recursive: true })

const entry = `
import { createApp, h } from 'vue'
import RankedIdentityBadge from '/src/l12/RankedIdentityBadge.vue'
import '/src/style.css'

const factions = [
  { key: 'order', name: '秩序', tier: '秩序精英', title: '秩序冠首' },
  { key: 'chaos', name: '混沌', tier: '混沌精英', title: '混沌冠首' },
  { key: 'fate', name: '命运', tier: '命运精英', title: '命运冠首' },
]

const badge = (variant, faction, label) => h(RankedIdentityBadge, { variant, faction, label, 'data-variant': variant })
createApp({
  render: () => h('main', { class: 'identity-qa' }, [
    h('header', [h('small', 'RANKED IDENTITY'), h('h1', '排位身份统一样式'), h('p', '排行榜、个人页与对战摘要共用此组件')]),
    ...factions.map(item => h('section', { class: 'identity-row', 'data-faction': item.key }, [
      h('b', item.name),
      h('div', [badge('tier', item.name, item.tier), badge('faction-title', item.name, item.title), badge('master-title', item.name, '最强银臂努阿达')]),
    ])),
  ]),
}).mount('#app')
`

let browser
const server = await createServer({
  root,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'ranked-identity-visual',
    resolveId(id) { if (id === '/__ranked_identity_qa__.js') return id },
    load(id) { if (id === '/__ranked_identity_qa__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if (!request.url?.match(/^\/__ranked_identity_qa__(\?|$)/)) return next()
        response.setHeader('Content-Type', 'text/html')
        response.end(`<style>
          *{box-sizing:border-box}body{margin:0;background:#070b0f;color:#edf2f2;font-family:'Microsoft YaHei',sans-serif}.identity-qa{width:min(820px,calc(100vw - 24px));margin:24px auto;padding:22px;border:1px solid #34434a;background:#0d151b}.identity-qa header{margin-bottom:18px}.identity-qa small{color:#62c9d0;font-weight:900;letter-spacing:.16em}.identity-qa h1{margin:5px 0;font-size:26px}.identity-qa p{margin:0;color:#879399}.identity-row{display:grid;grid-template-columns:70px minmax(0,1fr);align-items:center;gap:12px;padding:16px 0;border-top:1px solid #26343a}.identity-row>div{display:flex;min-width:0;flex-wrap:wrap;gap:10px}.identity-row>b{font-size:16px}@media(max-width:520px){.identity-qa{margin:12px auto;padding:14px}.identity-qa h1{font-size:21px}.identity-row{grid-template-columns:1fr;padding:13px 0}.identity-row>div{gap:7px}}
        </style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__ranked_identity_qa__.js"></script>`)
      })
    },
  }],
})

try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())

  for (const viewport of [{ width: 920, height: 620 }, { width: 390, height: 720 }]) {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__ranked_identity_qa__`)
    await page.locator('.ranked-identity-badge').first().waitFor()
    const report = await page.evaluate(() => {
      const styles = selector => [...document.querySelectorAll(selector)].map(element => getComputedStyle(element).backgroundImage)
      return {
        tierBackgrounds: styles('[data-variant="tier"]'),
        factionTitleBackgrounds: styles('[data-variant="faction-title"]'),
        masterBackgrounds: styles('[data-variant="master-title"]'),
        icons: [...document.querySelectorAll('.ranked-identity-badge>i')].map(element => element.textContent),
        overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
      }
    })
    if (new Set(report.tierBackgrounds).size !== 3) throw new Error('三派系段位没有使用三套主题色')
    if (new Set(report.factionTitleBackgrounds).size !== 3) throw new Error('三派系称号没有使用三套主题色')
    if (new Set(report.masterBackgrounds).size !== 1) throw new Error('最强主宰称号没有保持统一金色身份')
    if (!report.icons.includes('◆') || !report.icons.includes('♛')) throw new Error('派系称号与最强主宰称号缺少可辨识图形')
    if (report.overflow) throw new Error(`${viewport.width}px 视口出现横向溢出`)
    await page.screenshot({ path: path.join(output, `ranked-identity-${viewport.width}.png`), fullPage: true })
  }
  if (errors.length) throw new Error(errors.join('\n'))
  console.log(`排位身份视觉验收通过：${output}`)
} finally {
  await browser?.close()
  await server.close()
}
