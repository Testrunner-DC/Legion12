import assert from 'node:assert/strict'
import path from 'node:path'
import fs from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/referee-admin-visibility')
fs.mkdirSync(out, { recursive: true })
const previous = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const setup = previous.slice(previous.indexOf('const entry = `') + 'const entry = `'.length, previous.indexOf('const isPicker='))
const entry = setup + `
import ReplayPage from '/src/l12/ReplayPage.vue'
import {RouterView} from 'vue-router'
import {ref} from 'vue'
import {useLandscapeViewport} from '/src/l12/mobileViewport.ts'
import {adminApi} from '/src/l12/platform.ts'
import {rememberImportedReplay,replayGameAt} from '/src/l12/replayModel.ts'
const mode=params.get('mode')||'referee',count=Number(params.get('count')||6)
const covered=(i,known)=>({...battleCard(battleArtwork[(i+1)%3],i+'covered'),hidden:true,identityKnown:known})
for(const i of [0,1]){
 players[i].hand=Array.from({length:count},(_,j)=>battleCard(battleArtwork[j%3],i+'hand'+j))
 players[i].handCount=count
 players[i].field[0][1]=covered(i,mode==='referee')
}
const rawPlayers=players.map((player,i)=>({
 PlayerIndex:i,Name:player.name,MasterId:player.master.masterId,MasterName:player.master.masterName,Hp:7,MaxHp:9,
 Library:[],Hand:player.hand.map(card=>({...card,Hidden:false})),
 Field:player.field.map(row=>row.map(card=>card?{...card,Hidden:card.hidden||false,IdentityKnown:false}:null)),
 Morale:[],Graveyard:[],SpecialZones:{Runes:0,TrialLevel:0,GodPower:[],Trials:[]},MulliganDone:true
}))
const rawState={MatchId:'synthetic-admin',RoomCode:'QA',Phase:'Main',Round:3,ActivePlayer:0,FirstPlayer:0,
 Players:rawPlayers,Events:[],DisasterMode:'all',DisasterValue:0}
const summary={matchId:'synthetic-admin',status:'completed',players:[{displayName:players[0].name,result:'win'},{displayName:players[1].name,result:'lose'}],
 startedUtc:'2026-09-29T00:00:00Z',endedUtc:'2026-09-29T01:00:00Z',commandCount:1}
const command={sequence:1,receivedUtc:'2026-09-29T00:00:00Z',playerIndex:0,command:{type:'begin'},accepted:true,revision:1,stateHash:'qa',state:rawState}
window.__apiCalls=0
adminApi.match=async()=>{window.__apiCalls++;return {summary}}
adminApi.replayPage=async()=>{window.__apiCalls++;return {items:[command],totalCommands:1,nextCursor:null}}
const detail={match:{matchId:summary.matchId,roomCode:'QA',player0:players[0].name,player1:players[1].name,deck0:'',deck1:'',startedUtc:summary.startedUtc,commandCount:1},commands:[command],viewerPlayerIndex:0}
window.__rawCoveredIdentityKnown=rawPlayers.every(player=>player.Field[0][1].IdentityKnown===false)
window.__ordinaryMappedIdentityKnown=replayGameAt(detail,0)?.players.every(player=>player.field[0][1]?.identityKnown===false)
if(mode==='ordinary')rememberImportedReplay(detail)
if(mode==='ordinary-match'){
 const realFetch=window.fetch.bind(window)
 window.fetch=(input,init)=>String(input).includes('/api/matches/synthetic-admin')
  ? Promise.resolve(new Response(JSON.stringify(detail),{status:200,headers:{'Content-Type':'application/json'}}))
  : realFetch(input,init)
}
if(mode==='public'){
 for(const player of players){player.hand=[];player.field[0][1]={instanceId:player.playerIndex+'covered',cardId:'hidden-card',name:'盖伏卡牌',cardType:'legion',hidden:true,identityKnown:false}}
}
if(mode==='referee'||mode==='public'){
 l12State.spectating=true;l12State.observerView=mode;l12State.game.you=0;l12State.game.prompts=[];l12State.game.legalAttackTargets={}
}
if(mode==='ordinary'||mode==='ordinary-match'){
 // Leaving the live table is not required before opening a replay route.
 l12State.spectating=true;l12State.observerView='referee'
}
const routes=[{path:'/live',name:'live',component:GamePage},{path:'/admin/:matchId',name:'admin-match-replay',component:ReplayPage},
 {path:'/ordinary',name:'json-replay',component:ReplayPage},
 {path:'/match/:matchId',name:'match-replay',component:ReplayPage}]
const router=createRouter({history:createMemoryHistory(),routes})
const app=createApp({setup(){useLandscapeViewport(ref(true));return()=>h('div',null,[h('div',{id:'l12-landscape-teleports'}),h(RouterView)])}})
app.use(router)
await router.push(mode==='admin'?'/admin/synthetic-admin':mode==='ordinary'?'/ordinary':mode==='ordinary-match'?'/match/synthetic-admin':'/live')
await router.isReady()
app.mount('#app')
`
const server = await createServer({root,server:{host:'127.0.0.1',port:0},plugins:[{
  name:'referee-admin-visibility-qa',resolveId(id){if(id==='/__qa__.js')return id},load(id){if(id==='/__qa__.js')return entry},
  configureServer(s){s.middlewares.use((req,res,next)=>{
    if(req.url?.match(/^\/__qa__(\?|$)/)){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/\@vite/client"></script><script type="module" src="/__qa__.js"></script>');return}next()
  })},
}]})
let browser
try {
  await server.listen()
  const port=server.httpServer.address().port
  browser=await chromium.launch({headless:true,channel:'msedge'})
  const page=await browser.newPage()
  const errors=[],reports=[]
  page.on('pageerror',error=>errors.push(error.message))
  await page.route('**/*',route=>new URL(route.request().url()).hostname==='127.0.0.1'?route.continue():route.abort())
  for(const [mode,width,height,counts] of [
    ['referee',1920,1080,[1,6,12,20]],['referee',844,390,[1,6,12,20]],
    ['admin',1920,1080,[1,6,12,20]],['admin',844,390,[1,6,12,20]],
    ['public',1920,1080,[6]],['public',844,390,[6]],['ordinary',1920,1080,[6]],['ordinary-match',1920,1080,[6]],
  ])for(const count of counts){
    if(process.argv[2]&&process.argv[2]!==mode+'-'+width+'-'+count)continue
    await page.setViewportSize({width,height})
    await page.goto('http://127.0.0.1:'+port+'/__qa__?mode='+mode+'&count='+count)
    if(mode==='admin'&&width===844)await page.locator('.replay-mobile-blocked').waitFor()
    else await page.locator('.board-center>.l12-hand').first().waitFor({state:'attached',timeout:8000}).catch(async error=>{
      console.error(JSON.stringify({mode,width,height,count,errors,body:(await page.locator('body').innerText()).slice(0,1200)}))
      throw error
    })
    await page.waitForTimeout(250)
    const result=await page.evaluate(({mode,count,width})=>{
      const rectangle=element=>{const {left,right,top,bottom,width,height}=element.getBoundingClientRect();return {left,right,top,bottom,width,height}}
      const hands=[...document.querySelectorAll('.board-center>.l12-hand')].map(hand=>({
        faces:hand.querySelectorAll('.hand-card-wrap .card-tile img:not(.covered-card-back)').length,
        backs:hand.querySelectorAll('.card-back').length,
        total:hand.querySelectorAll('.hand-card-wrap,.card-back').length,
        width:hand.getBoundingClientRect().width,scrollWidth:hand.scrollWidth,
        rect:rectangle(hand),
        overflowing:hand.classList.contains('overflowing'),
      }))
      const covered=[...document.querySelectorAll('.formation-slot')].filter(slot=>slot.querySelector('[data-card-instance-id$="covered"]')).map(slot=>{
        const tile=slot.querySelector('[data-card-instance-id$="covered"]'),rect=rectangle(tile)
        const top=document.elementFromPoint(rect.left+rect.width/2,rect.top+rect.height/2)
        return {face:!!slot.querySelector('.card-tile img:not(.covered-card-back)'),back:!!slot.querySelector('.covered-card-back'),
          dormant:slot.classList.contains('hidden-dormant'),rect,hit:tile===top||tile.contains(top)}
      })
      return {mode,count,width,hands,covered,readOnly:!!document.querySelector('.read-only-board'),
        actionButtons:document.querySelectorAll('.hand-actions button,.field-actions button,.action-panel button').length,
        gmPanel:!!document.querySelector('.gm-panel'),commands:window.__sentCommands.length,
        blocked:!!document.querySelector('.replay-mobile-blocked'),apiCalls:window.__apiCalls,
        field:document.querySelector('.felt-board')?rectangle(document.querySelector('.felt-board')):null,
        rightRail:document.querySelector('.right-rail')?rectangle(document.querySelector('.right-rail')):null,
        actionPanel:document.querySelector('.action-panel')?rectangle(document.querySelector('.action-panel')):null,
        pageWidth:document.documentElement.scrollWidth,viewportWidth:innerWidth,
        battleZones:[...document.querySelectorAll('.battlefield-half')].map(half=>({
          half:rectangle(half),formation:rectangle(half.querySelector('.formation')),
          slots:[...half.querySelectorAll('.formation-slot')].map(rectangle),
          master:rectangle(half.querySelector('.mini-master')),
          relic:rectangle(half.querySelector('.relic-zone')),
        })),
        rawCoveredHidden:window.__rawCoveredIdentityKnown,ordinaryIdentityFalse:window.__ordinaryMappedIdentityKnown}
    },{mode,count,width})
    reports.push(result)
    console.log(JSON.stringify({mode,width,height,count,hands:result.hands.map(hand=>({faces:hand.faces,backs:hand.backs,rect:hand.rect,scrollWidth:hand.scrollWidth})),covered:result.covered,blocked:result.blocked}))
    await page.screenshot({path:path.join(out,mode+'-'+width+'-'+count+'.png')})
    assert.equal(result.rawCoveredHidden,true)
    assert.equal(result.ordinaryIdentityFalse,true)
    if(mode==='admin'&&width===844){assert.equal(result.blocked,true);assert.equal(result.apiCalls,0);assert.equal(result.hands.length,0);continue}
    assert.equal(result.readOnly,true)
    assert.equal(result.actionButtons,0)
    assert.equal(result.gmPanel,false)
    assert.equal(result.commands,0)
    assert.equal(result.hands.length,2)
    assert.equal(result.covered.length,2)
    if(mode==='referee'||mode==='admin'){
      assert(result.hands.every(hand=>hand.faces===count&&hand.backs===0))
      assert(result.covered.every(card=>card.face&&!card.back&&card.dormant))
      assert(result.covered.every(card=>card.hit&&card.rect.width>=30&&card.rect.height>=40))
      assert(result.hands.every(hand=>hand.width>0&&hand.scrollWidth>=hand.width))
      await page.locator('.board-center>.l12-hand:last-child .hand-card-wrap:last-child .card-tile').click({force:true})
      await page.locator('.formation-slot [data-card-instance-id$="covered"]').first().click()
      assert.equal(await page.evaluate(()=>window.__sentCommands.length),0)
      assert.equal(await page.locator('.hand-actions button,.field-actions button,.gm-panel').count(),0)
      if(mode==='referee'&&width===844){
        const [opponent,self]=result.hands.map(hand=>hand.rect)
        assert(opponent.height>=44&&self.height>=44)
        assert(opponent.bottom<=result.field.top+1)
        assert(self.top>=result.field.bottom-1)
        assert(opponent.bottom<=self.top)
        assert(opponent.right<=result.rightRail.left+1)
        assert(self.right<=result.rightRail.left+1)
        assert(result.pageWidth<=result.viewportWidth+1)
        assert.equal(result.battleZones.length,2)
        for(const zone of result.battleZones){
          assert.equal(zone.slots.length,6)
          for(const slot of zone.slots)assert(slot.width>=30&&slot.height>=30&&slot.left>=result.field.left-5&&slot.right<=result.field.right+5)
          for(const card of [zone.master,zone.relic])assert(card.width>=15&&card.height>=20&&card.left>=result.field.left-5&&card.right<=result.field.right+5)
        }
        assert(result.battleZones[0].formation.bottom<=result.battleZones[1].formation.top+1)
        const lastCards=await page.evaluate(()=>[...document.querySelectorAll('.board-center>.l12-hand')].map(hand=>{
          hand.scrollLeft=hand.scrollWidth
          const card=hand.querySelector('.hand-card-wrap:last-child .card-tile')
          const outer=hand.getBoundingClientRect(),last=card.getBoundingClientRect()
          return {visible:last.right>outer.left&&last.left<outer.right,lastHeight:last.height,scrollLeft:hand.scrollLeft}
        }))
        assert(lastCards.every(card=>card.visible&&card.lastHeight>=40))
      }
    }else{
      assert.equal(result.hands[0].faces,0)
      assert(result.hands[0].backs>=count)
      assert(result.covered.every(card=>!card.face&&card.back))
      if(mode.startsWith('ordinary')){
        assert.equal(await page.locator('.referee-both-hands').count(),0)
        assert.equal(result.hands[1].faces,count)
      }
      if(mode==='public'&&width===844)assert.equal(await page.locator('.referee-both-hands').count(),0)
    }
  }
  fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({errors,reports},null,2))
  assert.deepEqual(errors,[])
  console.log(JSON.stringify({cases:reports.length,errors,report:path.join(out,'report.json')}))
}finally{await browser?.close();await server.close()}
