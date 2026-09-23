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
const entry = "import '/src/l12/mobileViewport.css';\n" + fixtureSetup + `
// Visual QA may run in a desktop browser with a phone-sized viewport.  This flag
// only affects the synthetic preview, allowing it to exercise the production
// touch-landscape branch without changing application runtime detection.
if(params.has('mobile')){
 const nativeMatchMedia=window.matchMedia.bind(window)
 window.matchMedia=query=>query==='(pointer: coarse) and (hover: none)'
  ? {matches:true,media:query,addEventListener(){},removeEventListener(){},addListener(){},removeListener(){},dispatchEvent(){return false}}
  : nativeMatchMedia(query)
}
l12State.gmEnabled=params.has('gm')
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
const fieldFixture=params.get('field')
if(fieldFixture==='empty')for(const player of players)player.field=[[null,null,null],[null,null,null]]
if(fieldFixture==='full'||fieldFixture==='tapped')for(const player of players){
 player.field=[0,1].map(row=>[0,1,2].map((_,slot)=>({...card(legions[(player.playerIndex*2+row+slot)%legions.length],'b3-field-'+player.playerIndex+'-'+row+'-'+slot),tapped:fieldFixture==='tapped'})))
}
const pileFixture=params.get('piles')
if(pileFixture!==null){
 const count=Math.max(0,Math.min(40,Number(pileFixture)||0))
 for(const player of players){
  player.libraryCount=count
  player.libraryTop=count?card(legions[0],'b3-library-'+player.playerIndex):null
  player.graveyard=count?Array.from({length:count},(_,index)=>card(legions[index%legions.length],'b3-grave-'+player.playerIndex+'-'+index)):[]
  player.graveyardCount=count
 }
}
const markerFixture=params.get('markers')
if(markerFixture!==null){
 const count=Math.max(0,Math.min(5,Number(markerFixture)||0))
 for(const player of players){
  player.faction=player.playerIndex===0?'taiyangcheng':'gaotianyuan'
  player.specialZones.canopicTrack=Array.from({length:count},(_,index)=>({...card(legions[index%legions.length],'b3-marker-'+player.playerIndex+'-'+index),completed:index<Math.ceil(count/2)}))
 }
}
const runeFixture=params.get('runes')
if(runeFixture!==null){
 const count=Math.max(0,Math.min(5,Number(runeFixture)||0))
 players[0].faction='otherworld'
 players[0].specialZones.runes=count
}
if(params.has('modalFixture')){
 for(const player of players){
  player.factionEffect={
   cardId:'S02-06S1', imageUrl:'/assets/l12/card-back-disaster.png', name:'太阳城的长篇阵营效果验证标题',
   effectText:'这是用于移动端安全弹框验证的完整长文本。它包含多行说明、触发条件、选择限制与结算结果。\\n在小屏横向视口中，卡图、标题、正文与按钮必须分别拥有可读空间；正文过长时只能在弹框内部滚动，不能遮住关闭、最小化或返回按钮。\\n再次补充一段文本，用于验证滚动末端与操作区仍保持明确间距。',
   abilities:[
    { id:'fixture-one', label:'发动第一段完整阵营效果：选择一张军团并获得增益。' },
    { id:'fixture-two', label:'发动第二段完整阵营效果：支付士气后公开额外信息。' },
    { id:'fixture-three', label:'发动第三段完整阵营效果：确认后进入后续选择。' },
   ],
  }
  player.master.effectText='这是主宰弹框的长文本验证内容。主宰能力说明必须在内部滚动，卡图与血量信息不可被控制轨遮挡。\\n第二段：确认主宰弹框在电话和平板横屏中的关闭、最小化和恢复操作。'
  player.graveyard=Array.from({length:8},(_,index)=>card(legions[index%legions.length],player.playerIndex+'-grave-modal-'+index))
  player.graveyardCount=player.graveyard.length
 }
 const abilityFixture=players[0].field.flat().find(Boolean)
 if(abilityFixture){
  abilityFixture.name='移动端长篇卡牌效果验证军团'
  abilityFixture.effectText='这是用于验证真实卡牌效果弹框的长文本。卡图、标题、正文、发动按钮以及关闭和最小化按钮必须各自保持可读、可点，并且不得超出安全边界。'
  abilityFixture.abilities=[
   {id:'fixture-card-one',label:'发动第一段完整卡牌效果：选择一个合法目标并继续结算。'},
   {id:'fixture-card-two',label:'发动第二段完整卡牌效果：支付费用后查看后续选项。'},
   {id:'fixture-card-three',label:'发动第三段完整卡牌效果：确认后进入目标选择。'},
  ]
 }
}
if(params.has('support')){
 const attacker=players[1].field[0][0],target=players[0].field[0][0]
 players[0].field[1][0]={...card(legions[1], 'fixture-support-back-row'), tapped:false, troops:Math.max(4000,legions[1].troops||0), activeKeywords:['协防']}
 l12State.game.phase='Defense';l12State.game.activePlayer=1;l12State.game.prompts=[]
 l12State.game.pendingDefense={attackerPlayer:1,attackerInstanceId:attacker.instanceId,target:{type:'legion',instanceId:target.instanceId},stage:'DefenseChoice',attackValue:attacker.troops||3000}
}
if(params.has('defense')){
 const attacker=players[1].field[0][0]
 l12State.game.phase='Defense';l12State.game.activePlayer=1;l12State.game.prompts=[]
 l12State.game.pendingDefense={attackerPlayer:1,attackerInstanceId:attacker.instanceId,target:{type:'master'},stage:'DefenseChoice',attackValue:attacker.troops||3000}
}
if(params.has('combat-stage')){
 const attacker=players[1].field[0][0],target=players[0].field[0][0]
 const stage=params.get('combat-stage')||'AttackerAttackTiming'
 l12State.game.phase='Defense';l12State.game.activePlayer=1;l12State.game.prompts=[]
 l12State.game.pendingDefense={attackerPlayer:1,attackerInstanceId:attacker.instanceId,target:{type:'legion',instanceId:target.instanceId},stage,attackValue:4500}
 const nextSequence=Math.max(0,...l12State.game.recentEvents.map(event=>event.sequence))+1
 l12State.game.recentEvents=[...l12State.game.recentEvents,
  {sequence:nextSequence,type:'attack',playerIndex:1,text:'〈'+attacker.name+'〉4500 vs 〈'+target.name+'〉'+target.troops,cards:[attacker,target]},
  {sequence:nextSequence+1,type:'combat',playerIndex:1,text:'进攻者以冻结进攻值 4500 造成 4500 点战斗伤害',cards:[attacker,target]},
 ]
}
if(params.has('game-over')){
 l12State.game.phase='GameOver';l12State.game.winner=params.get('game-over')==='loss'?1:0
 l12State.game.winnerReason='达成胜利条件。结果将保留在此处，点击返回后才离开本局。双方都离开后关闭房间，最长保留30分钟。服务器保留对局状态。'
 l12State.rankedSettlement={matchId:l12State.game.matchId,accountId:'fixture-account',faction:'太阳城',won:true,placement:false,placementPlayed:5,placementRequired:5,before:1680,after:1718,delta:38,tierBefore:'辉曜 III',tierAfter:'辉曜 II',components:[{kind:'result',label:'胜负结果',value:24},{kind:'initiative',label:'先后手修正',value:6},{kind:'opponent',label:'对手强度',value:8}],settledAt:new Date().toISOString(),rewardStatus:'applied'}
}
if(params.has('card-choice')){
 const choiceCount=Math.max(1,Math.min(20,Number(params.get('choice-count')||6)))
 const choiceCards=Array.from({length:choiceCount},(_,index)=>legions[index%legions.length])
 const choices=choiceCards.map((cardDefinition,index)=>'choice-'+index)
 const data={uiPattern:'card-choice'}
 for(const [index,cardDefinition] of choiceCards.entries()){const key='choice-'+index;data[key+':cardId']=cardDefinition.id;data[key+':name']=(index%3===0?'完整长卡名·': '')+cardDefinition.nameZh;data[key+':cardType']=cardDefinition.cardType;data[key+':effect']=cardDefinition.effect||''}
 l12State.game.prompts=[{promptId:'fixture-card-choice',playerIndex:0,kind:'option',text:'从候选卡牌中选择 1 张',validChoices:choices,minChoose:1,maxChoose:1,choiceLabels:Object.fromEntries(choices.map((choice,index)=>[choice,choiceCards[index].nameZh])),data,createdRevision:1,controller:0}]
}
if(params.has('morale-payment')){
 const runeChoices=Array.from({length:Math.max(0,Math.min(players[0].specialZones.runes||0,Number(params.get('rune-usable')||0)))},(_,index)=>'rune:'+(index+1))
 const validChoices=[...runeChoices,...players[0].morale.slice(0,5).map(item=>item.instanceId)]
 l12State.game.prompts=[{promptId:'fixture-morale-payment',playerIndex:0,kind:'resource-payment',text:'选择2枚士气支付',validChoices,minChoose:2,maxChoose:2,choiceLabels:{},data:{choiceMode:'resource-payment'},createdRevision:1,controller:0}]
}
if(params.has('action-fixture')){
 l12State.game.phase='Main';l12State.game.activePlayer=0;l12State.game.prompts=[]
 const attacker=players[0].field[0][0]
 const target=players[1].field[0][0]
 if(attacker){attacker.tapped=false;attacker.summonRound=1;attacker.abilities=[{id:'fixture-active',label:'发动：获得测试增益',enabled:true}]}
 players[0].field[1][2]=null
 const handCard=players[0].hand[0]
 if(handCard){handCard.cost=0;handCard.currentCost=0;handCard.playCost=0;handCard.playBlockedReason=''}
 l12State.game.legalAttackTargets=attacker&&target?{[attacker.instanceId]:[target.instanceId,'master']}:{ }
}
if(params.has('disaster-chain')){
 const current={...card(disasters[0],'fixture-disaster-current'),name:'黯陨晨星灾变',hidden:false}
 const incoming={...card(disasters[1]||disasters[0],'fixture-disaster-incoming'),hidden:false}
 l12State.game.activeDisaster=current
 l12State.game.sessionDisasters=[current,incoming,...disasters.slice(2,4).map((definition,index)=>({...card(definition,'fixture-disaster-rest-'+index),hidden:false}))]
 l12State.game.removedDisasters=[]
 let disasterSequence=Math.max(0,...l12State.game.recentEvents.map(event=>event.sequence))
 const setPrompt=visible=>{
  l12State.game.prompts=visible?[{
   promptId:'fixture-disaster-obstruction',playerIndex:0,kind:'option',text:'天灾结算前的选择',validChoices:['yes','no'],minChoose:1,maxChoose:1,
   choiceLabels:{yes:'确认',no:'不发动'},data:{uiPattern:'effect-decision',effectText:'这是用于验证天灾动画等待与中断的真实交互弹框。'},createdRevision:1,controller:0,
  }]:[]
 }
 window.__disasterFixture={
  currentId:current.instanceId,
  incomingId:incoming.instanceId,
  openPrompt(){setPrompt(true)},
  closePrompt(){setPrompt(false)},
  emitReveal(triggered=false){
   l12State.game.activeDisaster=incoming
   const reveal={sequence:++disasterSequence,type:'disaster-reveal',playerIndex:null,text:'公开天灾〈'+incoming.name+'〉',cards:[incoming]}
   const events=[reveal]
   if(triggered)events.push({sequence:++disasterSequence,type:'effect-trigger',playerIndex:null,text:'〈'+incoming.name+'〉的天灾效果触发',effectText:'天灾效果开始结算。',cards:[incoming],effectResultStatus:'resolved'})
   l12State.game.recentEvents=[...l12State.game.recentEvents,...events]
   return events.map(event=>event.sequence)
  },
 }
}
if(params.has('board-target')){
 const choices=players.flatMap(player=>player.field.flat()).filter(Boolean).slice(0,2).map(card=>card.instanceId)
 l12State.game.prompts=[{promptId:'fixture-board-target',playerIndex:0,kind:'target',text:'选择 1–2 个战场目标',validChoices:[...choices,'skip'],minChoose:1,maxChoose:2,choiceLabels:{skip:'不发动'},data:{choiceMode:'board-target'},createdRevision:1,controller:0}]
}
if(params.has('board-slot'))l12State.game.prompts=[{promptId:'fixture-board-slot',playerIndex:0,kind:'slot',text:'选择我方空格位',validChoices:['0:1','1:2','skip'],minChoose:1,maxChoose:1,choiceLabels:{skip:'取消'},data:{choiceMode:'board-slot',targetPlayerIndex:'0'},createdRevision:1,controller:0}]
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
  // Avoid the bundled config temporary-file path.  That directory can be held by
  // a concurrent local dev server during visual QA on Windows.
  configLoader: 'runner',
  server: { host: '127.0.0.1', port, strictPort: true },
  plugins: [{
    name: 'l12-battle-layout-preview',
    resolveId(id) { if (id === '/__l12_battle_preview__.js') return id },
    load(id) { if (id === '/__l12_battle_preview__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (!request.url?.match(/^\/__l12_battle_preview__(\?|$)/)) { next(); return }
        response.setHeader('Content-Type', 'text/html; charset=utf-8')
        response.end('<div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__l12_battle_preview__.js"></script>')
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
