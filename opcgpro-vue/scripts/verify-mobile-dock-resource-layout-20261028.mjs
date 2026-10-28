import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { createServer } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import sharp from 'sharp'

// This fixture serves real SFCs to CUA; it never launches or drives a browser.
const front = path.resolve(import.meta.dirname, '..'), repo = path.dirname(front)
const output = process.env.L12_QA_OUT, cache = process.env.L12_QA_CACHE
assert(output && cache && path.isAbsolute(output) && path.isAbsolute(cache), 'explicit D output/cache required')
const quote = value => "'" + value.replaceAll("'", "''") + "'"
execFileSync(process.env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-Command',
  `. ${quote(path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'))}; Assert-L12GeneratedPath -Path ${quote(output)} | Out-Null; Assert-L12GeneratedPath -Path ${quote(cache)} | Out-Null`])
fs.mkdirSync(output, { recursive: true }); fs.mkdirSync(cache, { recursive: true })
const art = await sharp(Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="300" height="420"><rect width="300" height="420" fill="#164b50"/><text x="25" y="210" fill="white" font-size="24">SYNTHETIC QA</text></svg>')).png().toBuffer()
const manifest = JSON.stringify({ schemaVersion: 3, catalogVersion: 'synthetic-dock', assetVersion: 'synthetic-dock', basePath: '/',
  cards: Object.fromEntries(['S02-06M2', 'ST06-M1', 'S02-0609'].map(cardId => [cardId, { cardId, contentHash: 'synthetic-' + cardId,
    width: 300, height: 420, orientation: 'portrait', variants: { thumbWebp: '/__dock-art.png', boardWebp: '/__dock-art.png', detailWebp: '/__dock-art.png' } }])) })
const entry = `
import { createApp, h, ref } from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import { l12State } from '/src/l12/net.ts'
import { useLandscapeViewport } from '/src/l12/mobileViewport.ts'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
import '/src/l12/motion.css'
const query = new URLSearchParams(location.search), count = Number(query.get('count') || 0)
const mobile = ref(query.get('desktop') !== '1'), sent = ref([]), errors = ref([])
const mixed = query.get('mixed') === '1', payment = query.get('payment') === '1'
const player = i => ({playerIndex:i,name:i?'合成对手':'合成我方',deckName:'合成布局验收',faction:'otherworld',
 master:{masterId:i?'ST06-M1':'S02-06M2',masterName:'合成主宰',masterImageUrl:'/__dock-art.png',hp:8,maxHp:8,abilities:[]},
 libraryCount:20,hand:[],handCount:0,morale:Array.from({length:count},(_,j)=>({instanceId:'morale-'+i+'-'+j,
 tapped:j%3===2,isGodPower:mixed&&j===0,faction:'otherworld'})),temporaryMorale:mixed?2:0,graveyard:[],
 field:[[null,null,null],[null,null,null]],mulliganDone:true,specialZones:{runes:0,trialLevel:0,godPower:[],trials:[],canopicTrack:[]}})
const players=[player(0),player(1)]
const game={matchId:'synthetic-dock-layout',roomCode:'SYNTHETIC',you:0,revision:1,activePlayer:0,firstPlayer:0,diceWinner:0,
 initiativeRolls:[6,3],phase:'Main',round:3,turnSerial:5,disasterMode:'none',disasterValue:0,players,sessionDisasters:[],
 prompts:payment?[{promptId:'synthetic-payment',playerIndex:0,kind:'resource-payment',text:'合成支付：选择1个资源',
 validChoices:players[0].morale.filter(m=>!m.tapped).map(m=>m.instanceId),minChoose:1,maxChoose:1,data:{choiceMode:'resource-payment'}}]:[],
 waitingPrompt:null,effectStack:[],playerBadges:[],recentEvents:[{sequence:1,type:'turn-start',playerIndex:0,text:'合成我方回合',cards:[]}]}
l12State.gmEnabled=false;l12State.spectating=false;l12State.status='online';l12State.pendingAction=false
l12State.socket={readyState:WebSocket.OPEN,send:raw=>sent.value.push(JSON.parse(raw))}
l12State.room={roomCode:'SYNTHETIC',yourPlayerIndex:0,players:[0,1].map(i=>({playerIndex:i,name:'合成玩家',connected:true,ready:true})),started:true,decks:[]}
l12State.game=game
window.addEventListener('error',e=>errors.value.push(String(e.message)))
window.addEventListener('unhandledrejection',e=>errors.value.push(String(e.reason)))
createApp({setup(){useLandscapeViewport(mobile);return()=>h('div',{class:mobile.value?'l12-landscape-surface':''},[
 h(GameBoard,{game:l12State.game,readOnly:false}),
 h('output',{id:'dock-qa-state',style:'position:fixed;left:40%;top:0;z-index:2147483647;font:8px sans-serif;color:white;pointer-events:none'},
  JSON.stringify({count,mixed,payment,mobile:mobile.value,sent:sent.value,errors:errors.value}))
])}}).mount('#app')
`
const html = '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>L12 移动右栏与士气布局</title></head><body><div id="app"></div><div id="l12-landscape-teleports"></div><script type="module" src="/__dock-entry.js"></script></body></html>'
const server = await createServer({ configFile: false, root: front, cacheDir: path.join(cache, 'vite'),
  plugins: [vue(), tailwindcss(), {name:'mobile-dock-resource-fixture',
    resolveId(id){if(id==='/__dock-entry.js')return '\0dock-entry'},
    load(id){if(id==='\0dock-entry')return entry},
    configureServer(instance){instance.middlewares.use((request,response,next)=>{
      const url=new URL(request.url,'http://127.0.0.1')
      if(url.pathname==='/__dock-layout'){
        response.setHeader('content-type','text/html;charset=utf-8')
        response.setHeader('Content-Security-Policy',"default-src 'self' data: blob:; connect-src 'self' ws://127.0.0.1:*; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline' 'unsafe-eval'")
        response.end(html);return
      }
      if(url.pathname.startsWith('/api/')){response.setHeader('content-type','application/json');response.end('[]');return}
      if(url.pathname.endsWith('manifest.json')){response.setHeader('content-type','application/json');response.end(manifest);return}
      // Vite's ?import image requests are JavaScript modules, not image responses.
      if(url.pathname==='/__dock-art.png'){response.setHeader('content-type','image/png');response.end(art);return}
      next()
    })}
  }], resolve:{alias:{'@':path.join(front,'src')}},server:{host:'127.0.0.1',port:5179,strictPort:true},
  define:{'import.meta.env.VITE_APP_VERSION':JSON.stringify('synthetic-dock-layout')} })
await server.listen()
const pins=Object.fromEntries(['src/l12/game/GameBoard.vue','src/l12/game/PlayerMat.vue','src/l12/game/GameBoard.mobile.css','src/l12/game/MobileBattleDock.css']
  .map(file=>[file,createHash('sha256').update(fs.readFileSync(path.join(front,file))).digest('hex')]))
fs.writeFileSync(path.join(output,'fixture-server.json'),JSON.stringify({pid:process.pid,url:'http://127.0.0.1:5179/__dock-layout',pins,syntheticOnly:true},null,2))
console.log('CUA fixture ready: http://127.0.0.1:5179/__dock-layout')
const stop=async()=>{await server.close();process.exit(0)}
process.once('SIGINT',stop);process.once('SIGTERM',stop)
