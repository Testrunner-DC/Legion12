import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const fixtureSource = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const fixtureStart = fixtureSource.indexOf('const entry = `') + 'const entry = `'.length
const fixtureEnd = fixtureSource.indexOf('const isPicker=', fixtureStart)
if (fixtureStart < 'const entry = `'.length || fixtureEnd < fixtureStart)
  throw new Error('Unable to locate the sanitized GamePage fixture in verify-batch253-visual.mjs')

const fixtureSetup = fixtureSource.slice(fixtureStart, fixtureEnd)
const entry = fixtureSetup + `
const trialCount=Math.max(0,Math.min(2,Number(params.get('trials')||0)))
const myTrialCount=Math.max(0,Math.min(1,Number(params.get('myTrials')||0)))
const showcaseHandCount=Math.max(1,Math.min(40,Number(params.get('hand')||6)))
for(const player of players){
 player.hand=Array.from({length:showcaseHandCount},(_,index)=>card(legions[index%legions.length],player.playerIndex+'-hand-showcase-'+index))
 player.handCount=showcaseHandCount
}
const trialCards=catalog.filter(cardDefinition=>cardDefinition.cardType==='trial')
const relicCards=catalog.filter(cardDefinition=>cardDefinition.cardType==='artifact')
if(relicCards.length){
 players[0].relic=card(relicCards[0],'relic-showcase-my')
 players[1].relic=card(relicCards[1]||relicCards[0],'relic-showcase-opponent')
}
if(trialCount){
 players[1].specialZones.trials=Array.from({length:trialCount},(_,index)=>({
  ...card(trialCards[index]||trial,'trial-showcase-'+index),
  trialProgress:index+1,
  trialCompleted:false,
 }))
}
if(myTrialCount)players[0].specialZones.trials=[{
 ...card(trialCards[trialCount]||trialCards[0]||trial,'trial-showcase-my'),
 trialProgress:1,
 trialCompleted:false,
}]
if(params.has('fieldIndicators')){
 for(const player of players){
  const unit=player.field.flat().find(Boolean)
  if(!unit)continue
  unit.activeKeywords=player.playerIndex===0?['冲锋','协防']:['冲锋','协防','强攻','支援','护卫','追击']
  unit.statusIcons=['shield','power-up','power-down','discard-end']
 }
}
if(params.has('rankedClock')){
 const receivedAtMs=Date.now()
 l12State.rankedClock={
  serverUtcMs:receivedAtMs,
  receivedAtMs,
  totalLimitMs:1200000,
  operationLimitMs:90000,
  reconnectLimitMs:120000,
  players:[
   {playerIndex:0,totalRemainingMs:754000,operationRemainingMs:68000,acting:true,connected:true},
   {playerIndex:1,totalRemainingMs:821000,operationRemainingMs:90000,acting:false,connected:true},
  ],
 }
}
const deathMode=params.get('death')
if(deathMode!==null)setTimeout(()=>{
 const attacker=players[0].field[0][0]
 const defeated={...players[1].field[0][0],troops:0}
 players[1].field[0][0]=null
 players[1].graveyard.push(defeated)
 const leaveText=deathMode==='effect'?defeated.name+'被效果击杀':defeated.name+'阵亡（等待触发完成后进入墓地）'
 const events=[{sequence:101,type:'leave',playerIndex:1,text:leaveText,cards:[defeated]}]
 if(deathMode!=='effect')events.push({sequence:102,type:'combat',playerIndex:0,text:'进攻者以冻结进攻值 3000 造成 3000 点战斗伤害；防守军团以当前兵力 2000 反击',cards:[attacker,defeated]})
 l12State.game.recentEvents=[...l12State.game.recentEvents,...events]
},800)
const previewState={game:l12State.game,room:l12State.room,socket:l12State.socket,rankedClock:l12State.rankedClock}
if(import.meta.hot){
 let previewReloadScheduled=false
 import.meta.hot.on('vite:beforeUpdate',()=>{
  if(previewReloadScheduled)return
  previewReloadScheduled=true
  window.setTimeout(()=>window.location.reload(),80)
 })
}
window.setInterval(()=>{
 l12State.game=previewState.game
 l12State.room=previewState.room
 l12State.socket=previewState.socket
 l12State.status='online'
 l12State.pendingAction=false
 if(previewState.rankedClock){
  const now=Date.now()
  previewState.rankedClock.receivedAtMs=now
  previewState.rankedClock.serverUtcMs=now
  l12State.rankedClock=previewState.rankedClock
 }
},200)
const app=createApp({render:()=>h(GamePage)})
app.use(createRouter({history:createMemoryHistory(),routes:[]}))
app.mount('#app')
`

const portArgument = process.argv.find((value, index, values) => values[index - 1] === '--port')
const port = Number(portArgument || process.env.L12_PREVIEW_PORT || 5174)
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error(`Invalid preview port: ${port}`)

const server = await createServer({
  root,
  server: { host: '127.0.0.1', port, strictPort: true },
  plugins: [{
    name: 'l12-battle-layout-preview',
    resolveId(id) { if (id === '/__l12_battle_preview__.js') return id },
    load(id) { if (id === '/__l12_battle_preview__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (!request.url?.match(/^\/__l12_battle_preview__(\?|$)/)) { next(); return }
        response.setHeader('Content-Type', 'text/html; charset=utf-8')
        response.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__l12_battle_preview__.js"></script>')
      })
    },
  }],
})

await server.listen()
const baseUrl = `http://127.0.0.1:${port}/__l12_battle_preview__`
console.log(`L12 battle layout preview: ${baseUrl}`)
console.log('Queries: ?morale=12&active=8&special=1&trial=1&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1&hand=15&moraleLock=1&slot=1&effect=cost2&death=combat')

async function close() {
  await server.close()
  process.exit(0)
}
process.once('SIGINT', close)
process.once('SIGTERM', close)
