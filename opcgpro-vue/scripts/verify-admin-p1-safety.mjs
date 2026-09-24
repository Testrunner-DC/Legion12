import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/admin-p1-safety')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,defineComponent,h,ref} from 'vue'
import '/src/style.css'
import AdminRiskActionDialog from '/src/l12/site/AdminRiskActionDialog.vue'
import {useAdminRiskAction} from '/src/l12/site/useAdminRiskAction.ts'

const App=defineComponent({
 setup(){
  const allowed=ref(true), secret=ref(false), attempts=ref(0)
  const risk=useAdminRiskAction()
  const openRisk=()=>risk.requestRiskAction({
   title:'确认禁用账号',target:'风险验收玩家',targetLabel:'账号',
   impact:'账号将立即禁止登录，全部有效会话会被撤销。',
   confirmLabel:'禁用并撤销会话',severity:'danger',
   run:async()=>{attempts.value++;await new Promise(resolve=>setTimeout(resolve,80));if(attempts.value===1)throw new Error('模拟服务端失败，请重试')}
  })
  return()=>h('main',{class:'qa-admin'},[
   h('h1','后台安全交互验收'),
   h('button',{id:'toggle-permission',onClick:()=>allowed.value=!allowed.value},'切换写权限'),
   allowed.value?h('button',{id:'danger-trigger',onClick:openRisk},'禁用账号'):h('p',{id:'readonly-note'},'当前账号只有读取权限'),
   h('button',{id:'secret-trigger',onClick:()=>secret.value=true},'显示一次性临时密码'),
   h('output',{id:'attempts'},String(attempts.value)),
   risk.riskAction.value?h(AdminRiskActionDialog,{
    title:risk.riskAction.value.title,target:risk.riskAction.value.target,targetLabel:risk.riskAction.value.targetLabel,
    impact:risk.riskAction.value.impact,confirmLabel:risk.riskAction.value.confirmLabel,severity:risk.riskAction.value.severity,
    busy:risk.riskBusy.value,error:risk.riskError.value,onCancel:risk.cancelRiskAction,onConfirm:risk.confirmRiskAction
   }):null,
   secret.value?h(AdminRiskActionDialog,{title:'一次性临时密码已生成',target:'风险验收玩家',targetLabel:'账号',
    impact:'该密码只显示这一次。',confirmLabel:'我已安全保存',severity:'warning',allowCancel:false,busy:false,
    onConfirm:()=>secret.value=false},{default:()=>h('code',{id:'temporary-password'},'7F1A5C9E2D4B8A6031CE97B5420D8F6A')}):null
  ])
 }
})
createApp(App).mount('#app')
`

let browser
const server = await createServer({
  root, configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-admin-p1-safety'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'admin-p1-safety-fixture',
    resolveId(id) { if (id === '/__admin_p1_safety__.js') return id },
    load(id) { if (id === '/__admin_p1_safety__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__admin_p1_safety__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#eee}.qa-admin{display:flex;flex-wrap:wrap;gap:16px;padding:24px}.qa-admin h1{width:100%}.qa-admin button{min-height:44px;padding:10px 16px}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__admin_p1_safety__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const viewports = [{ width: 1440, height: 900 }, { width: 1280, height: 720 }, { width: 390, height: 844 }, { width: 844, height: 390 }]
const suffix = value => `${value.width}x${value.height}`
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const pageErrors = []
  page.on('pageerror', error => pageErrors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const report = []
  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__admin_p1_safety__`)
    const trigger = page.locator('#danger-trigger')
    await trigger.focus()
    await trigger.click()
    const dialog = page.locator('dialog')
    await dialog.waitFor()
    assert.equal(await dialog.getAttribute('open') !== null, true, `risk dialog is not modal at ${suffix(viewport)}`)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `page overflows at ${suffix(viewport)}`)
    const confirm = dialog.getByRole('button', { name: '禁用并撤销会话' })
    await confirm.evaluate(element => { element.click(); element.click() })
    await dialog.getByRole('alert').waitFor()
    assert.equal(await page.locator('#attempts').textContent(), '1', `double submit was not locked at ${suffix(viewport)}`)
    await confirm.click()
    await dialog.waitFor({ state: 'detached' })
    assert.equal(await page.evaluate(() => document.activeElement?.id), 'danger-trigger', `focus was not returned at ${suffix(viewport)}`)
    await trigger.click()
    await page.keyboard.press('Escape')
    await dialog.waitFor({ state: 'detached' })
    await page.locator('#secret-trigger').click()
    await page.locator('#temporary-password').waitFor()
    assert.equal(await page.locator('dialog button').count(), 1, `one-time secret must not expose cancel at ${suffix(viewport)}`)
    await page.keyboard.press('Escape')
    assert.equal(await page.locator('#temporary-password').count(), 1, `one-time secret closed on Escape at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `admin-risk-${suffix(viewport)}.png`), fullPage: true })
    await page.getByRole('button', { name: '我已安全保存' }).click()
    await page.locator('#toggle-permission').click()
    assert.equal(await page.locator('#danger-trigger').count(), 0, `write action visible without permission at ${suffix(viewport)}`)
    assert.equal(await page.locator('#readonly-note').count(), 1, `read-only state missing at ${suffix(viewport)}`)
    report.push({ viewport, overflow: false, doubleSubmitAttempts: 1, secretLength: 32 })
  }
  assert.equal(pageErrors.length, 0, `page errors: ${pageErrors.join(' | ')}`)
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status: 'passed', viewports, report, pageErrors }, null, 2))
  console.log(JSON.stringify({ status: 'passed', output, viewports: viewports.length }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
