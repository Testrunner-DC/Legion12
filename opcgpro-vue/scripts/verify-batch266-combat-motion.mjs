import path from 'node:path'
import fs from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const output = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/batch266-combat-motion'
fs.mkdirSync(output, { recursive: true })

const previous = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const setup = previous.slice(previous.indexOf('const entry = `') + 'const entry = `'.length, previous.indexOf('const isPicker='))
const entry = setup + `
const trialDef=catalog.find(c=>c.cardType==='trial')
players[0].field[0][0].abilities=[{id:'trialAdvance',label:'试炼',enabled:true}]
players[0].specialZones.trials=[{...card(trialDef,'qa-current-trial'),trialProgress:2,trialCompleted:false}]
l12State.game.legalAttackTargets={[players[0].field[0][0].instanceId]:[{type:'legion',instanceId:players[1].field[0][0].instanceId}]}
const params=new URLSearchParams(location.search)
const scenario=params.get('scenario')||'actions'
function removeDefender(){
 const defeated={...players[1].field[0][0],troops:0}
 players[1].field[0][0]=null
 players[1].graveyard.push(defeated)
 return defeated
}
function emitEvents(...items){l12State.game.recentEvents=[...l12State.game.recentEvents,...items]}
if(scenario!=='actions')setTimeout(()=>{
 const attacker=players[0].field[0][0]
 if(scenario==='combat'){
  const defeated=removeDefender()
  emitEvents(
   {sequence:101,type:'leave',playerIndex:1,text:defeated.name+'阵亡（等待触发完成后进入墓地）',cards:[defeated]},
   {sequence:102,type:'combat',playerIndex:0,text:'进攻者以冻结进攻值 3000 造成 3000 点战斗伤害；防守军团以当前兵力 2000 反击',cards:[attacker,defeated]})
 }
 if(scenario==='effect'){
  const defeated=removeDefender()
  emitEvents({sequence:101,type:'leave',playerIndex:1,text:defeated.name+'被神妙行军击杀',cards:[defeated]})
 }
 if(scenario==='return'){
  const returned=removeDefender()
  emitEvents({sequence:101,type:'leave',playerIndex:1,text:returned.name+'因自身效果返回手牌',cards:[returned]})
 }
 if(scenario==='combat-return'){
  const returned=removeDefender()
  emitEvents(
   {sequence:101,type:'combat',playerIndex:0,text:'进攻者以冻结进攻值 3000 造成 3000 点战斗伤害；防守军团以当前兵力 2000 反击',cards:[attacker,returned]},
   {sequence:102,type:'leave',playerIndex:1,text:returned.name+'因自身效果返回手牌',cards:[returned]})
 }
 if(scenario==='replacement'){
  const protectedCard=players[1].field[0][0]
  emitEvents({sequence:101,type:'leave',playerIndex:1,text:protectedCard.name+'即将阵亡，由友军代替承受致命结果',cards:[protectedCard]})
 }
 if(scenario==='duplicate'){
  const defeated=removeDefender()
  emitEvents(
   {sequence:101,type:'leave',playerIndex:1,text:defeated.name+'因兵力不高于0阵亡',cards:[defeated]},
   {sequence:102,type:'leave',playerIndex:1,text:defeated.name+'阵亡',cards:[defeated]})
 }
 if(scenario==='match-switch'){
  const defeated=removeDefender()
  emitEvents({sequence:101,type:'leave',playerIndex:1,text:defeated.name+'被效果击杀',cards:[defeated]})
  setTimeout(()=>{l12State.game.matchId='synthetic-batch266-next'},230)
 }
},800)
const app=createApp({render:()=>h(GamePage)})
app.use(createRouter({history:createMemoryHistory(),routes:[]}));app.mount('#app')
`

let browser
const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'batch266-combat-motion', resolveId(id) { if (id === '/__qa__.js') return id }, load(id) { if (id === '/__qa__.js') return entry },
  configureServer(instance) { instance.middlewares.use((request, response, next) => {
    if (request.url?.match(/^\/__qa__(\?|$)/)) { response.setHeader('Content-Type', 'text/html'); response.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__qa__.js"></script>'); return }
    next()
  }) },
}] })

try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const open = scenario => page.goto(`http://127.0.0.1:${port}/__qa__?scenario=${scenario}`)

  await open('actions')
  const ownCard = page.locator('.side-my .formation-slot .card-tile').first()
  await ownCard.click()
  await page.getByRole('button', { name: '进攻', exact: true }).waitFor()
  await page.getByRole('button', { name: '试炼', exact: true }).waitFor()
  await page.screenshot({ path: path.join(output, 'attack-and-trial-actions.png') })
  await page.getByRole('button', { name: '进攻', exact: true }).click()
  const modeHint = page.locator('[data-ui-contract="cancel-local-attack-selection"]')
  await modeHint.waitFor()
  await page.screenshot({ path: path.join(output, 'attack-cancel-visible.png') })
  await modeHint.getByRole('button', { name: '取消', exact: true }).click()
  if (await modeHint.count()) throw new Error('attack cancel did not clear local selection')

  await open('combat')
  const combatGhost = page.locator('.l12-combat-defeat-ghost')
  await combatGhost.waitFor()
  const combatLabel = await combatGhost.locator('.l12-defeat-damage').textContent()
  const combatGhosts = await combatGhost.count()
  await page.screenshot({ path: path.join(output, 'combat-leave-then-authoritative-damage.png') })

  await open('effect')
  const effectGhost = page.locator('.l12-combat-defeat-ghost')
  await effectGhost.waitFor()
  const effectLabel = await effectGhost.locator('.l12-defeat-damage').textContent()
  await page.screenshot({ path: path.join(output, 'effect-kill-nonnumeric.png') })

  await open('return')
  await page.waitForTimeout(1150)
  const returnGhosts = await page.locator('.l12-combat-defeat-ghost').count()

  await open('combat-return')
  await page.waitForTimeout(1150)
  const combatReturnGhosts = await page.locator('.l12-combat-defeat-ghost').count()

  await open('replacement')
  await page.waitForTimeout(1150)
  const replacementGhosts = await page.locator('.l12-combat-defeat-ghost').count()

  await open('duplicate')
  await page.waitForTimeout(930)
  const duplicateGhosts = await page.locator('.l12-combat-defeat-ghost').count()

  await open('match-switch')
  await page.waitForTimeout(900)
  const beforeMatchSwitch = await page.locator('.l12-combat-defeat-ghost').count()
  await page.waitForTimeout(260)
  const afterMatchSwitch = await page.locator('.l12-combat-defeat-ghost').count()

  const report = {
    errors, attackAndTrial: true, localCancelCleared: true,
    combat: { ghosts: combatGhosts, label: combatLabel },
    effect: { label: effectLabel }, returnGhosts, combatReturnGhosts, replacementGhosts, duplicateGhosts,
    matchSwitch: { before: beforeMatchSwitch, after: afterMatchSwitch },
  }
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify(report, null, 2))
  if (errors.length || combatGhosts !== 1 || combatLabel !== '-3000' || effectLabel !== '击杀'
    || returnGhosts !== 0 || combatReturnGhosts !== 0 || replacementGhosts !== 0 || duplicateGhosts !== 1
    || beforeMatchSwitch !== 1 || afterMatchSwitch !== 0) throw new Error('batch266 combat motion behavior failed')
} finally {
  await browser?.close()
  await server.close()
}
