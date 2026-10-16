import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT
if (!out) throw new Error('L12_QA_OUT_REQUIRED')
fs.mkdirSync(out, { recursive: true })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')

const sourceFixture = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const entryMarker = 'const entry = `'
const begin = sourceFixture.indexOf(entryMarker) + entryMarker.length
const end = sourceFixture.indexOf('const isPicker=', begin)
if (begin < entryMarker.length || end < begin) throw new Error('EXISTING_SANITIZED_GAMEPAGE_FIXTURE_REQUIRED')

const entry = sourceFixture.slice(begin, end) + `
import {ref as qaRef} from 'vue'
import {useLandscapeViewport} from '/src/l12/mobileViewport.ts'
import '/src/l12/mobileViewport.css'
const qaCase=params.get('case')||'normal'
const qaAbility=qaCase==='completed'
 ? {id:'skyCityDiscount',label:'天空之城减费',enabled:true}
 : {id:'completeTrial',label:'完成试炼',enabled:qaCase!=='normal',triggerOnly:qaCase==='trigger'}
const qaTrial=(instanceId,cardId,name,progress,completed,ability)=>({
 ...card(catalog.find(c=>c.id===cardId),instanceId),trialProgress:progress,trialCompleted:completed,abilities:[ability],
})
const qaFirst=()=>qaTrial('first','S02-06S6','十字军东征',8,true,{id:'crusadeAdvance',label:'十字军远征',enabled:true})
const qaSecond=(progress=0,completed=false,ability=qaAbility)=>qaTrial('second','ST06-S1','探寻天空之城',progress,completed,ability)
const qaInitialProgress=qaCase==='normal'?0:8
l12State.game.players[0].specialZones.trials=[qaFirst(),qaSecond(qaInitialProgress,qaCase==='completed')]
l12State.game.players[0].specialZones.trialCapacity=2
l12State.game.players[1].specialZones.trials=[{instanceId:'hidden-opponent-trial',cardId:'hidden-trial',name:'未揭示试炼',cardType:'trial',hidden:true,trialProgress:2,trialCompleted:false}]
l12State.game.activePlayer=qaCase==='not-main'?1:0
l12State.game.prompts=qaCase==='prompt'?[{promptId:'blocking',playerIndex:0,kind:'option',text:'阻塞提示',validChoices:['yes'],minChoose:1,maxChoose:1,createdRevision:1,controller:0}]:[]
l12State.spectating=qaCase==='spectator'
l12State.pendingAction=qaCase==='pending'
const qaCommands=[]
const qaOutput=document.getElementById('command-observation')
const qaUpdate=()=>{qaOutput.textContent=JSON.stringify({commands:qaCommands,progress:l12State.game.players[0].specialZones.trials[1]?.trialProgress??null})}
l12State.socket.send=payload=>{const envelope=JSON.parse(payload);qaCommands.push({type:envelope.type,command:envelope.command});qaUpdate()}
document.getElementById('advance-eight').addEventListener('click',()=>{
 l12State.game.players[0].specialZones.trials=[qaFirst(),qaSecond(8,false,{id:'completeTrial',label:'完成试炼',enabled:true})]
 l12State.game.revision++
 qaUpdate()
})
document.getElementById('remove-second').addEventListener('click',()=>{
 l12State.game.players[0].specialZones.trials=[qaFirst()]
 l12State.game.revision++
 qaUpdate()
})
const app=createApp({setup(){useLandscapeViewport(qaRef(true));return()=>h(GamePage)}})
app.use(createRouter({history:createMemoryHistory(),routes:[]}));app.mount('#app');qaUpdate()
`

const html = '<meta name="viewport" content="width=device-width,initial-scale=1">'
  + '<div id="l12-landscape-teleports"></div><div id="app"></div>'
  + '<section id="qa-controls" style="position:fixed;right:0;bottom:0;z-index:2147483647"><button id="advance-eight">模拟收到第二试炼进度8</button>'
  + '<button id="remove-second">模拟第二试炼离区</button>'
  + '<output id="command-observation">加载中</output></section>'
  + '<script type="module" src="/__mobile_trial_fixture.js"></script>'

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: process.env.L12_VITE_CACHE_DIR,
  logLevel: 'error',
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'mobile-trial-ability-entry',
    resolveId(id) { if (id === '/__mobile_trial_fixture.js') return id },
    load(id) { if (id === '/__mobile_trial_fixture.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if (!request.url?.match(/^\/__mobile_trial__(\?|$)/)) return next()
        response.setHeader('Content-Type', 'text/html; charset=utf-8')
        response.end(html)
      })
    },
  }],
})

let browser
const cases = []
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  const page = await browser.newPage({ viewport: { width: 844, height: 390 } })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1'
    ? route.continue() : route.abort())

  const openCase = async mode => {
    await page.goto(`http://127.0.0.1:${port}/__mobile_trial__?case=${mode}`)
    const cards = page.locator('.mobile-extra-zone-my .mobile-extra-card')
    await cards.nth(1).waitFor()
    assert.equal(await cards.count(), 2, `${mode}: both trial instances must remain visible`)
    return cards
  }

  const normalCards = await openCase('normal')
  await normalCards.nth(1).click()
  const dialog = page.getByRole('dialog', { name: '试炼能力', exact: true })
  await dialog.waitFor()
  const complete = dialog.getByRole('button', { name: '完成试炼', exact: true })
  assert.equal(await complete.isDisabled(), true, 'progress 0 must expose but disable completion')
  await page.getByRole('button', { name: '模拟收到第二试炼进度8', exact: true }).click()
  const refreshedDialogCount = await dialog.count()
  assert.equal(refreshedDialogCount, 1, JSON.stringify({
    refreshedDialogCount,
    trialButtons: await page.locator('.mobile-trial-ability-dialog button').allTextContents(),
    observation: await page.locator('#command-observation').textContent(),
  }))
  await complete.waitFor({ state: 'visible' })
  assert.equal(await complete.isEnabled(), true, 'same open dialog must refresh from the latest instance snapshot')
  await dialog.getByRole('button', { name: '最小化试炼能力', exact: true }).click()
  const restore = page.getByRole('button', { name: '展开：探寻天空之城', exact: true })
  await restore.waitFor()
  await restore.click()
  await dialog.getByRole('button', { name: '关闭试炼能力', exact: true }).click()
  await normalCards.nth(1).click()
  await dialog.waitFor()
  await dialog.getByRole('button', { name: '完成试炼', exact: true }).click()
  const normalOutput = JSON.parse(await page.locator('#command-observation').textContent())
  assert.deepEqual(normalOutput, { commands: [{ type: 'gameAction', command: {
    type: 'activateAbility', cardInstanceId: 'second', ability: 'completeTrial',
  } }], progress: 8 })
  cases.push({ name: 'second-progress-refresh-and-exact-command', passed: true, output: normalOutput })
  await page.screenshot({ path: path.join(out, 'mobile-second-trial-complete.png') })

  const completedCards = await openCase('completed')
  await completedCards.nth(1).click()
  const completedDialog = page.getByRole('dialog', { name: '试炼能力', exact: true })
  const completedAction = completedDialog.getByRole('button', { name: '天空之城减费', exact: true })
  await completedAction.waitFor()
  await completedAction.click()
  const completedOutput = JSON.parse(await page.locator('#command-observation').textContent())
  assert.deepEqual(completedOutput.commands, [{ type: 'gameAction', command: {
    type: 'activateAbility', cardInstanceId: 'second', ability: 'skyCityDiscount',
  } }])
  cases.push({ name: 'completed-trial-active-ability', passed: true, output: completedOutput })

  const removedCards = await openCase('removed')
  await removedCards.nth(1).click()
  await page.getByRole('dialog', { name: '试炼能力', exact: true }).waitFor()
  await page.getByRole('button', { name: '模拟第二试炼离区', exact: true }).click()
  assert.equal(await page.getByRole('dialog', { name: '试炼能力', exact: true }).count(), 0)
  await page.getByRole('button', { name: '模拟收到第二试炼进度8', exact: true }).click()
  assert.equal(await page.getByRole('dialog', { name: '试炼能力', exact: true }).count(), 0,
    'removed source must not keep an invisible modal identity that reopens when the same ID returns')
  assert.deepEqual(JSON.parse(await page.locator('#command-observation').textContent()).commands, [])
  cases.push({ name: 'source-removal-clears-modal-identity', passed: true })

  for (const mode of ['not-main', 'pending', 'trigger']) {
    const cards = await openCase(mode)
    await cards.nth(1).click()
    const gatedDialog = page.getByRole('dialog', { name: '试炼能力', exact: true })
    await gatedDialog.waitFor()
    const action = gatedDialog.getByRole('button', { name: '完成试炼', exact: true })
    assert.equal(await action.isDisabled(), true, `${mode}: ability must remain disabled`)
    assert.deepEqual(JSON.parse(await page.locator('#command-observation').textContent()).commands, [])
    cases.push({ name: mode, passed: true })
  }

  for (const mode of ['spectator', 'prompt']) {
    const cards = await openCase(mode)
    await cards.nth(1).click({ force: true })
    assert.equal(await page.getByRole('dialog', { name: '试炼能力', exact: true }).count(), 0,
      `${mode}: no ability entry may open`)
    assert.deepEqual(JSON.parse(await page.locator('#command-observation').textContent()).commands, [])
    cases.push({ name: mode, passed: true })
  }

  const opponentHidden = page.locator('.mobile-extra-zone-opponent .mobile-extra-card')
  assert.equal(await opponentHidden.count(), 1, 'the fixture must actually contain one hidden opponent trial')
  assert.equal(await opponentHidden.first().isDisabled(), true, 'hidden opponent trial remains non-interactive')
  assert.equal(await opponentHidden.first().getAttribute('title'), '对手未揭示的试炼')
  assert.deepEqual(JSON.parse(await page.locator('#command-observation').textContent()).commands, [])
  cases.push({ name: 'hidden-opponent-trial-present-and-disabled', passed: true })
  assert.deepEqual(errors, [])
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify({ status: 'passed', cases }, null, 2))
  console.log(`Mobile trial ability entry passed: ${cases.length} cases.`)
} catch (error) {
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify({
    status: 'failed', cases, error: error instanceof Error ? error.stack : String(error),
  }, null, 2))
  throw error
} finally {
  await browser?.close()
  await server.close()
}
