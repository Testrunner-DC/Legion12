import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/batch296-effect-scene-editor'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(out, { recursive: true })

const entry = String.raw`
import { createApp, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import AdminPage from '/src/l12/site/AdminPage.vue'
import { adminApi, authState, platformState } from '/src/l12/platform.ts'
import { l12State } from '/src/l12/net.ts'
import '/src/style.css'

const baseScene = {
  cardId: 'QA-0001', abilityId: 'QA-0001:ability:1', trigger: 'play',
  overrideText: undefined, overridden: false, eventType: 'effect', placeholders: [],
}
const scenes = [
  {
    ...baseScene, sceneId: 'QA-0001:ability:1:presentation:generic', label: '旧通用场景',
    defaultText: '旧场景默认文案', effectiveText: '旧场景默认文案',
  },
  {
    ...baseScene, eventType: 'reveal', sceneId: 'QA-0001:ability:1:presentation:generic-null', label: '非效果事件空元数据场景',
    flow: null, segmentIndex: null, segmentCount: null, branchLabel: null, requiredChoices: null,
    defaultText: '旧服务端输出 null 时仍按通用场景显示。', effectiveText: '旧服务端输出 null 时仍按通用场景显示。',
  },
  {
    ...baseScene, sceneId: 'QA-0001:ability:1:presentation:branch:draw', label: '分支效果',
    flow: 'qa-branch', branchLabel: '抽取两张卡牌', requiredChoices: { mode: 'mode:draw' },
    defaultText: '选择抽牌分支：抽取2张牌。', effectiveText: '选择抽牌分支：抽取2张牌。',
  },
  {
    ...baseScene, sceneId: 'QA-0001:ability:1:presentation:branch:heal', label: '分支效果',
    flow: 'qa-branch', branchLabel: '恢复主宰生命', requiredChoices: { mode: 'mode:heal' },
    defaultText: '选择恢复分支：我方主宰恢复2点生命。', effectiveText: '选择恢复分支：我方主宰恢复2点生命。',
  },
  {
    ...baseScene, sceneId: 'QA-0001:ability:1:presentation:flow:search', label: '查看与展示',
    flow: 'qa-search', segmentIndex: 1, segmentCount: 3,
    defaultText: '查看牌库顶部3张牌。', effectiveText: '查看牌库顶部3张牌。',
  },
  {
    ...baseScene, sceneId: 'QA-0001:ability:1:presentation:flow:choose', label: '选择分支目标',
    flow: 'qa-choose', segmentIndex: 2, segmentCount: 3, branchLabel: '选择军团',
    requiredChoices: { mode: 'mode:legion', targetKind: 'public-legion' },
    defaultText: '选择其中1张军团并公开展示。', effectiveText: '选择其中1张军团并公开展示。',
  },
  {
    ...baseScene, sceneId: 'QA-0001:ability:1:presentation:flow:resolve', label: '加入手牌并处理其余牌',
    flow: 'qa-resolve-with-a-deliberately-long-public-flow-identifier-for-containment',
    segmentIndex: 3, segmentCount: 3,
    defaultText: '将所选军团加入手牌。\n其余卡牌以原顺序放回牌库底部。\n这是一段用于验证窄屏换行、中文长句和容器安全边距的较长公开动效文案。',
    effectiveText: '将所选军团加入手牌。\n其余卡牌以原顺序放回牌库底部。\n这是一段用于验证窄屏换行、中文长句和容器安全边距的较长公开动效文案。',
  },
  {
    ...baseScene, sceneId: 'QA-0001:ability:1:presentation:invalid', label: '异常兼容场景',
    flow: 'qa-invalid', segmentIndex: 0, segmentCount: 2,
    defaultText: '异常元数据仍可按稳定 sceneId 编辑。', effectiveText: '异常元数据仍可按稳定 sceneId 编辑。',
  },
]
const ability = {
  abilityId: 'QA-0001:ability:1', cardId: 'QA-0001', sequence: 1,
  text: '打出时 依次处理三个段落，并根据公开选择进入对应分支。', trigger: 'play',
  atoms: [], migrationStatus: 'verified', hasLegacyFallback: false,
  mappingSource: 'fixture', confidence: 1, executionModel: 'fixture-only',
  reviewStatus: 'confirmed', reviewSource: 'fixture', presentations: scenes,
}
const genericOnlyAbility = {
  ...ability, abilityId: 'QA-0001:ability:2', sequence: 2,
  text: '没有分段场景的能力继续显示整体效果文案。',
  presentations: [{
    ...baseScene, abilityId: 'QA-0001:ability:2', sceneId: 'QA-0001:ability:2:presentation:generic', label: '仅整体效果',
    defaultText: '没有分段时保留整体效果。', effectiveText: '没有分段时保留整体效果。',
  }],
}
const fixture = {
  cardId: 'QA-0001', name: '隔离动效编辑测试卡', product: 'QA', faction: '测试', cardType: 'tactic',
  effectText: '隔离 fixture，不属于正式卡池。', abilities: [ability, genericOnlyAbility], migrationStatus: 'verified',
  atomCount: 0, executableAtomCount: 0, legacyAtomCount: 0, atomKinds: [],
  reviewStatus: 'confirmed', reviewSource: 'fixture',
}
const coverage = {
  totalCards: 1, cardsWithText: 1, totalAbilities: 2, totalAtoms: 0,
  declarativeReadyAbilities: 0, verifiedAbilities: 1, legacyBackedAbilities: 0,
  byStatus: { verified: 1 }, byAtomKind: {},
}

window.__effectSceneSaveCalls = []
platformState.token = 'isolated-effect-scene-fixture'
platformState.account = {
  id: 'fixture-admin', username: '隔离验收管理员', role: 'admin', createdAt: new Date().toISOString(),
  publicHistory: false, permissions: ['admin.effects.read', 'admin.effects.review'],
}
authState.initialized = true
authState.verified = true
authState.refreshing = false
l12State.status = 'offline'
adminApi.effects = async () => ({ items: [structuredClone(fixture)], total: 1, page: 1, pageSize: 50, coverage })
adminApi.effectAtoms = async () => []
adminApi.effect = async () => structuredClone(fixture)
adminApi.saveEffectPresentation = async (cardId, sceneId, text) => {
  window.__effectSceneSaveCalls.push({ cardId, sceneId, text })
  const scene = scenes.find(item => item.sceneId === sceneId)
  return { ...scene, overrideText: text, effectiveText: text, overridden: true }
}
adminApi.restoreEffectPresentation = async (cardId, sceneId) => scenes.find(item => item.sceneId === sceneId)

const router = createRouter({ history: createMemoryHistory(), routes: [
  { path: '/', component: { render: () => h('div', 'fixture home') } },
  { path: '/admin', component: AdminPage }, { path: '/me', component: { render: () => h('div', 'fixture profile') } },
] })
const app = createApp({ render: () => h(AdminPage) })
app.use(router)
await router.push('/admin')
await router.isReady()
app.mount('#app')
`

const server = await createServer({
  root,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'effect-scene-editor-fixture',
    resolveId(id) { if (id === '/__effect_scene_editor__.js') return id },
    load(id) { if (id === '/__effect_scene_editor__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__effect_scene_editor__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__effect_scene_editor__.js"></script>')
          return
        }
        next()
      })
    },
  }],
  appType: 'spa',
})

let browser
const report = []
try {
  await server.listen()
  const address = server.httpServer.address()
  assert(address && typeof address !== 'string')
  browser = await chromium.launch({ headless: true, channel: 'msedge' })

  for (const viewport of [
    { width: 1440, height: 1000 },
    { width: 760, height: 1000 },
    { width: 390, height: 844 },
  ]) {
    const page = await browser.newPage({ viewport })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1'
      ? route.continue() : route.abort())
    await page.goto(`http://127.0.0.1:${address.port}/__effect_scene_editor__`)
    await page.getByRole('button', { name: /卡效原子化/ }).click()
    await page.getByRole('button', { name: /QA-0001/ }).click()
    await page.locator('[data-ui-contract="effect-presentation-editor"]').first().waitFor()

    assert.equal(await page.locator('.presentation-scene').count(), 8)
    assert.equal(await page.locator('[data-ui-contract="effect-presentation-branch"]').count(), 3)
    assert.equal(await page.locator('[data-ui-contract="effect-presentation-segment"]').count(), 3)
    assert.equal(await page.getByText('分段元数据异常', { exact: true }).count(), 1)
    assert.equal(await page.getByText('第 1/3 段', { exact: true }).count(), 1)
    assert.equal(await page.getByText('第 2/3 段', { exact: true }).count(), 1)
    assert.equal(await page.getByText('第 3/3 段', { exact: true }).count(), 1)

    assert.equal(await page.getByText('旧通用场景', { exact: true }).count(), 0)
    assert.equal(await page.getByText('仅整体效果', { exact: true }).count(), 1)
    const nullableOldScene = page.locator('.presentation-scene').first()
    assert.equal(await nullableOldScene.locator('[data-ui-contract="effect-presentation-segment"]').count(), 0)
    assert.equal(await nullableOldScene.locator('[data-ui-contract="effect-presentation-branch"]').count(), 0)
    assert.equal(await nullableOldScene.getByText('分段元数据异常', { exact: true }).count(), 0)
    await nullableOldScene.scrollIntoViewIfNeeded()
    const overviewScreenshotPath = path.join(out, `effect-scene-editor-${viewport.width}-overview.png`)
    await page.screenshot({ path: overviewScreenshotPath })

    const multilineScene = page.locator('.presentation-scene').nth(5)
    const multilineEditor = multilineScene.locator('textarea')
    assert.match(await multilineEditor.inputValue(), /加入手牌。\n其余卡牌/)
    const whiteSpace = await multilineScene.locator('.presentation-preview strong')
      .evaluate(element => getComputedStyle(element).whiteSpace)
    assert.equal(whiteSpace, 'pre-wrap')

    await multilineEditor.fill('第一行动效')
    await multilineEditor.press('End')
    await multilineEditor.press('Enter')
    await multilineEditor.type('第二行动效')
    assert.equal(await multilineEditor.inputValue(), '第一行动效\n第二行动效')
    assert.deepEqual(await page.evaluate(() => window.__effectSceneSaveCalls), [])

    const containment = await page.evaluate(() => {
      const rect = element => {
        const box = element.getBoundingClientRect()
        return { left: box.left, right: box.right, top: box.top, bottom: box.bottom,
          width: box.width, scrollWidth: element.scrollWidth, clientWidth: element.clientWidth }
      }
      const editor = document.querySelector('[data-ui-contract="effect-presentation-editor"]')
      return {
        viewportWidth: innerWidth,
        documentScrollWidth: document.documentElement.scrollWidth,
        editor: rect(editor),
        scenes: [...document.querySelectorAll('.presentation-scene')].map(rect),
        inputs: [...document.querySelectorAll('.presentation-scene textarea')].map(rect),
        headings: [...document.querySelectorAll('.presentation-heading')].map(rect),
      }
    })
    assert(containment.editor.left >= -1 && containment.editor.right <= viewport.width + 1,
      `Editor must remain in viewport at ${viewport.width}px`)
    for (const [name, items] of [['scene', containment.scenes], ['textarea', containment.inputs], ['heading', containment.headings]]) {
      for (const item of items) {
        assert(item.left >= containment.editor.left - 1 && item.right <= containment.editor.right + 1,
          `${name} must remain inside editor at ${viewport.width}px`)
        assert(item.scrollWidth <= item.clientWidth + 1,
          `${name} must not have hidden horizontal overflow at ${viewport.width}px`)
      }
    }
    assert(containment.documentScrollWidth <= containment.viewportWidth + 1,
      `Admin effect fixture must not create page-level horizontal overflow at ${viewport.width}px`)
    assert.deepEqual(errors, [])

    const multilineScreenshotPath = path.join(out, `effect-scene-editor-${viewport.width}-multiline.png`)
    await page.screenshot({ path: multilineScreenshotPath })
    report.push({ viewport, overviewScreenshotPath, multilineScreenshotPath, containment,
      enterValue: await multilineEditor.inputValue(), errors })
    await page.close()
  }

  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2))
  console.log(`Effect scene editor visual checks passed at 1440/760/390. Screenshots: ${out}`)
} finally {
  await browser?.close()
  await server.close()
}
