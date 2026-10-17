import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const snapshotRoot = process.env.L12_HERCULES_RESPONSE_SNAPSHOT_ROOT
const outputRoot = process.env.L12_QA_OUT
assert.ok(snapshotRoot, 'L12_HERCULES_RESPONSE_SNAPSHOT_ROOT is required')
assert.ok(outputRoot, 'L12_QA_OUT is required')
const snapshots = [
  ['promotion-enter', '时点：晋升登场', '击杀对方1张费用不高于展示军团其费用的军团。'],
  ['enter', '时点：登场时', '可对双方主宰各造成1点非致命伤害。'],
].map(([trigger, timing, effect]) => {
  const file = path.join(snapshotRoot, `recipient-${trigger}.json`)
  const bytes = fs.readFileSync(file)
  const game = JSON.parse(bytes)
  const prompt = game.prompts?.[0]
  assert.equal(game.you, 1, `${trigger}: fixture must be the real responder projection`)
  assert.equal(game.prompts?.length, 1, `${trigger}: projection must contain one actionable prompt`)
  assert.equal(prompt.kind, 'response', `${trigger}: must remain an ordinary independent response window`)
  assert.match(prompt.text, /赫拉克勒斯·晋升/)
  assert.ok(prompt.text.includes(timing), `${trigger}: authoritative timing label is missing`)
  assert.ok(prompt.text.includes(effect), `${trigger}: authoritative effect text is missing`)
  const choice = prompt.validChoices.find(id => prompt.data?.[`${id}:cardId`] === 'S01-0016')
  assert.ok(choice, `${trigger}: projected prompt must carry the real Absolute Defense instance`)
  assert.equal(prompt.data?.sourceCardId, 'S02-0501')
  return {
    trigger, timing, effect, file, sha256: createHash('sha256').update(bytes).digest('hex'),
    game, promptId: prompt.promptId, choice,
  }
})
assert.notEqual(snapshots[0].promptId, snapshots[1].promptId,
  'the two independent engine windows must retain different prompt identities')

fs.mkdirSync(outputRoot, { recursive: true })
const browserRuntime = process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
const { chromium } = createRequire(import.meta.url)(browserRuntime)
const serialized = JSON.stringify(snapshots.map(item => item.game))
const entry = `
import {createApp,h,markRaw,nextTick,reactive,ref} from 'vue'
import PromptOverlay from '/src/l12/game/PromptOverlay.vue'
import {l12State} from '/src/l12/net.ts'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
import '/src/l12/motion.css'
const fixtures=${serialized}
const game=ref(reactive(structuredClone(fixtures[0])))
window.__sent=[]
window.__focus=[]
window.__targets=[]
l12State.gmEnabled=false
l12State.spectating=false
l12State.pendingAction=false
l12State.status='online'
l12State.socket=markRaw({readyState:WebSocket.OPEN,send:value=>window.__sent.push(JSON.parse(value))})
window.__load=async index=>{
  l12State.pendingAction=false
  game.value=reactive(structuredClone(fixtures[index]))
  await nextTick()
}
createApp({render:()=>h(PromptOverlay,{game:game.value,onFocusCard:card=>window.__focus.push(card.instanceId),onResponseTargetsChange:ids=>window.__targets.push(ids)})}).mount('#app')
`
const cacheDir = path.join(process.env.L12_WORK_CACHE || path.join(outputRoot, '.vite-cache'),
  'vite', 'hercules-response-consumer')
const server = await createServer({
  root: projectRoot,
  cacheDir,
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'hercules-response-consumer-fixture',
    resolveId(id) { if (id === '/__hercules_response__.js') return id },
    load(id) { if (id === '/__hercules_response__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if (request.url !== '/__hercules_response__') return next()
        response.setHeader('Content-Type', 'text/html; charset=utf-8')
        response.end('<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__hercules_response__.js"></script></body></html>')
      })
    },
  }],
})

let browser
const pageErrors = []
const results = []
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1366, height: 844 } })
  page.on('pageerror', error => pageErrors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname === '127.0.0.1') return route.continue()
    if (route.request().resourceType() === 'image') return route.fulfill({
      contentType: 'image/svg+xml',
      body: '<svg xmlns="http://www.w3.org/2000/svg" width="96" height="134"><rect width="96" height="134" fill="#152326"/><text x="13" y="70" fill="#d9d8cf">L12 QA</text></svg>',
    })
    return route.abort()
  })
  const address = server.httpServer.address()
  assert.ok(address && typeof address !== 'string')
  await page.goto(`http://127.0.0.1:${address.port}/__hercules_response__`)
  await page.waitForFunction(() => typeof window.__load === 'function')

  for (let index = 0; index < snapshots.length; index++) {
    const expected = snapshots[index]
    await page.evaluate(value => window.__load(value), index)
    const panel = page.locator('.prompt-panel')
    await panel.waitFor({ state: 'visible' })
    const rendered = (await panel.textContent()) || ''
    assert.ok(rendered.includes(expected.timing), `${expected.trigger}: rendered prompt lost its timing label`)
    assert.ok(rendered.includes(expected.effect), `${expected.trigger}: rendered prompt lost its effect text`)
    const otherTiming = snapshots[1 - index].timing
    assert.ok(!rendered.includes(otherTiming), `${expected.trigger}: a separate response window leaked into this prompt`)
    const candidate = page.locator('.prompt-card-candidate').filter({ hasText: '绝对防御' })
    assert.equal(await candidate.count(), 1, `${expected.trigger}: Absolute Defense must be one real card choice`)
    const panelBox = await panel.boundingBox()
    const candidateBox = await candidate.boundingBox()
    assert.ok(panelBox && candidateBox)
    assert.ok(candidateBox.x >= panelBox.x - 1 && candidateBox.y >= panelBox.y - 1
      && candidateBox.x + candidateBox.width <= panelBox.x + panelBox.width + 1
      && candidateBox.y + candidateBox.height <= panelBox.y + panelBox.height + 1,
    `${expected.trigger}: the response choice must remain fully visible inside the actual prompt`)
    const before = await page.evaluate(() => window.__sent.length)
    await candidate.click()
    await page.waitForFunction(count => window.__sent.length === count + 1, before)
    const envelope = await page.evaluate(() => window.__sent.at(-1))
    assert.equal(envelope.type, 'gameAction')
    assert.equal(envelope.command.type, 'resolvePrompt')
    assert.equal(envelope.command.promptId, expected.promptId)
    assert.deepEqual(envelope.command.cardInstanceIds, [expected.choice])
    const screenshot = `${expected.trigger}.png`
    await page.screenshot({ path: path.join(outputRoot, screenshot), fullPage: true })
    results.push({
      trigger: expected.trigger,
      timing: expected.timing,
      promptId: expected.promptId,
      responseChoiceInstanceId: expected.choice,
      commandPromptId: envelope.command.promptId,
      commandCardInstanceIds: envelope.command.cardInstanceIds,
      screenshot,
    })
  }
  assert.deepEqual(pageErrors, [])
  const componentPath = path.join(projectRoot, 'src/l12/game/PromptOverlay.vue')
  const result = {
    status: 'PASS',
    scope: 'actual PromptOverlay SFC consuming two consecutive real engine recipient projections',
    engineFixtureCase: {
      test: 'HerculesPromotionResponseChoiceTests.AbsoluteDefenseSeparatesBothPromotedHeraclesEntryEffectsAcrossRestore',
      controller: 0,
      selectedTrigger: 'enter',
      declaredOrder: ['enter', 'promotion-enter'],
      observedResponseWindowOrder: ['promotion-enter', 'enter'],
      provenance: 'Both fixture files are emitted only by this one theory row, after restoring each consecutive prompt in the same game.',
    },
    viewport: { width: 1366, height: 844 },
    note: 'Stack item identity remains server-side for an ordinary response prompt; the client sends the exact projected promptId and real response-card instance. The engine regression independently proves the two bound StackItemIds differ.',
    snapshots: snapshots.map(({ trigger, file, sha256, promptId }) => ({ trigger, file, sha256, promptId })),
    promptOverlaySha256: createHash('sha256').update(fs.readFileSync(componentPath)).digest('hex'),
    results,
    pageErrors,
  }
  fs.writeFileSync(path.join(outputRoot, 'result.json'), `${JSON.stringify(result, null, 2)}\n`)
  console.log(`Hercules response consumer passed ${results.length}/${snapshots.length}`)
} finally {
  await browser?.close()
  await server.close()
}
