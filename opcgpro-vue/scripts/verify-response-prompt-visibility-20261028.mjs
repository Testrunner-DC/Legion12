import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { createServer } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import sharp from 'sharp'

// A loopback-only native-browser fixture for CUA. Launch only while holding the
// shared browser/Vite slot. It does not connect to a game/API or drive a browser.
const front = path.resolve(import.meta.dirname, '..'), repo = path.dirname(front)
const output = process.env.L12_QA_OUT, cache = process.env.L12_QA_CACHE
assert(output && cache && path.isAbsolute(output) && path.isAbsolute(cache), 'explicit governed output/cache required')
const quote = value => "'" + value.replaceAll("'", "''") + "'"
execFileSync(process.env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-Command',
  `. ${quote(path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'))}; Assert-L12GeneratedPath -Path ${quote(output)} | Out-Null; Assert-L12GeneratedPath -Path ${quote(cache)} | Out-Null`])
const boardRef = process.argv.find(arg => arg.startsWith('--board-ref='))?.slice('--board-ref='.length)
if (boardRef) assert.match(boardRef, /^[0-9a-f]{7,40}$/i)
const boardPath = path.join(front, 'src/l12/game/GameBoard.vue')
const boardSource = boardRef ? execFileSync('git', ['--no-lazy-fetch', '-c', `safe.directory=${repo}`, '-C', repo,
  'show', `${boardRef}:opcgpro-vue/src/l12/game/GameBoard.vue`], { encoding: 'utf8' }) : fs.readFileSync(boardPath, 'utf8')
const art = await sharp(Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="300" height="420"><rect width="300" height="420" rx="14" fill="#164b50"/><rect x="10" y="10" width="280" height="400" rx="10" fill="none" stroke="#d7c483" stroke-width="3"/><text x="150" y="210" text-anchor="middle" fill="white" font-size="25">SYNTHETIC QA</text></svg>')).png().toBuffer()
const manifest = JSON.stringify({ schemaVersion: 3, catalogVersion: 'synthetic-response-visibility',
  assetVersion: 'synthetic-response-visibility', basePath: '/', cards: Object.fromEntries(
    ['S02-0604', 'S02-0609', 'S02-0618', 'S02-06M2', 'ST06-M1'].map(cardId => [cardId, { cardId,
      contentHash: 'synthetic-' + cardId, width: 300, height: 420, orientation: 'portrait',
      variants: { thumbWebp: '/__response-art.png', boardWebp: '/__response-art.png', detailWebp: '/__response-art.png' } }])) })
const entry = `
import { createApp, h, ref, nextTick } from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import { l12State } from '/src/l12/net.ts'
import { useLandscapeViewport } from '/src/l12/mobileViewport.ts'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
import '/src/l12/motion.css'
const owner=Number(new URLSearchParams(location.search).get('owner')||0)
const attackMovement=new URLSearchParams(location.search).get('movement')==='1'
const mobile=ref(new URLSearchParams(location.search).get('mobile')==='1')
const mountKey=ref(0), sent=ref([]), stage=ref('empty'), errors=ref([])
window.addEventListener('error',event=>errors.value.push(event.message))
window.addEventListener('unhandledrejection',event=>errors.value.push(String(event.reason)))
const card=(id,name,cardId='S02-0609')=>({instanceId:id,cardId,name,cardType:'legion',faction:'otherworld',cost:2,
 baseTroops:3000,troops:3000,disasterLevel:0,tapped:false,summonRound:-1,hidden:false,identityKnown:false,
 imageUrl:'/__response-art.png',effectText:'合成公开卡牌，仅测试提示呈现，不接卡效结算。'})
const oldCard=card('old-detail','上一提示的军团详情')
const entrant=card('entry-card','响应来源登场军团','S02-0604')
const publicAttacker={...card('public-attacker','合成公开攻击来源','S02-0618'),baseTroops:4000,troops:4000}
const player=i=>({playerIndex:i,name:i===owner?'合成我方':'合成对手',deckName:'合成验收',faction:'otherworld',
 master:{masterId:i?'ST06-M1':'S02-06M2',masterName:'合成主宰',masterImageUrl:'/__response-art.png',hp:8,maxHp:8,abilities:[]},
 libraryCount:20,hand:i===owner?[oldCard,entrant]:[],handCount:i===owner?2:0,morale:[],graveyard:[],
 field:[[null,null,null],[null,null,null]],mulliganDone:true,specialZones:{runes:0,trialLevel:0,godPower:[],trials:[],canopicTrack:[]}})
const initial=()=>({matchId:'synthetic-response-native',roomCode:'SYNTHETIC',you:owner,revision:1,activePlayer:1-owner,
 firstPlayer:0,diceWinner:0,initiativeRolls:[6,3],phase:'Main',round:3,turnSerial:5,disasterMode:'none',disasterValue:0,
 players:[player(0),player(1)],sessionDisasters:[],prompts:[],waitingPrompt:null,effectStack:[],playerBadges:[],
 recentEvents:[{sequence:1,type:'turn-start',playerIndex:1-owner,text:'合成对方回合',cards:[]}]})
const prompt=(id,source,kind='response',choices=['pass'])=>({promptId:id,playerIndex:owner,kind,
 text:kind==='response'?'请选择是否响应':'查看并选择上一提示的军团',validChoices:choices,minChoose:1,maxChoose:1,
 activationId:id+':a',sourceInstanceId:source,sourceCardId:source==='entry-card'?'S02-0604':'S02-0609',
 step:0,createdRevision:id==='previous-choice'?2:3,controller:owner,choiceLabels:{pass:'不响应'},
 data:{sourceInstanceId:source,...(kind==='response'?{choiceMode:'instant'}:{})}})
function clock(){l12State.rankedClock={receivedAtMs:Date.now(),operationLimitMs:30000,
 players:[0,1].map(playerIndex=>({playerIndex,connected:true,acting:playerIndex===owner,
 totalRemainingMs:60000,operationRemainingMs:30000,reconnectRemainingMs:null}))}}
function publish(game){l12State.game=game;l12State.pendingAction=false;clock()}
l12State.gmEnabled=false;l12State.spectating=false;l12State.status='online'
l12State.socket={readyState:WebSocket.OPEN,send:raw=>{sent.value.push(JSON.parse(raw))}}
l12State.room={roomCode:'SYNTHETIC',yourPlayerIndex:owner,players:[0,1].map(i=>({playerIndex:i,name:'合成玩家',connected:true,ready:true})),started:true,decks:[]}
publish(initial())
function previous(){const game=initial();game.revision=2;game.prompts=[prompt('previous-choice','old-detail','card',['old-detail'])];publish(game);stage.value='previous'}
function next(){const game=structuredClone(JSON.parse(JSON.stringify(l12State.game)));game.revision=3;
 game.players[owner].hand=game.players[owner].hand.filter(c=>c.instanceId!=='entry-card');game.players[owner].handCount=game.players[owner].hand.length;
 game.players[owner].field[0][0]=entrant;game.recentEvents.push({sequence:2,type:'play',playerIndex:owner,
 text:'合成军团登场',cards:[entrant],playerLogGroupId:'play:entry-card',playerLogTiming:'enter'});
 game.prompts=[prompt('next-response','entry-card')];publish(game);stage.value='next'}
function same(){publish(JSON.parse(JSON.stringify(l12State.game)));stage.value='same-revision'}
function remount(){mountKey.value++;l12State.pendingAction=false;stage.value='same-state-remount'}
function stable(){const game=initial();game.revision=4;game.prompts=[{...prompt('stable-selection','old-detail','card',['old-detail','entry-card']),
 minChoose:1,maxChoose:2,data:{sourceInstanceId:'old-detail'}}];publish(game);stage.value='stable'}
function revision(){const game=JSON.parse(JSON.stringify(l12State.game));game.revision++;publish(game);stage.value='clock-only-update'}
// Attack fixtures begin with an already public field card. Only the opt-in
// movement case changes its field slot; neither path creates an entry event.
function prepareAttack(){
 const game=initial();game.revision=250;game.round=18;game.turnSerial=17;
 game.players[1-owner].field[0][0]=publicAttacker;
 game.recentEvents=[{sequence:250,type:'phase',playerIndex:1-owner,text:'合成攻击前状态',cards:[]}];
 game.prompts=attackMovement?[{...prompt('previous-choice','old-detail','card',['old-detail']),createdRevision:250}]:[];
 publish(game);stage.value=attackMovement?'attack-ready-old-detail':'attack-ready';
}
function attackResponse(){
 const game=JSON.parse(JSON.stringify(l12State.game));
 if(game.revision!==250)throw Error('先点击“进攻前状态”，再进入本次合成进攻响应');
 const attacker=game.players[1-owner].field[0][0];
 if(attacker?.instanceId!=='public-attacker')throw Error('合成公开攻击来源必须已在战场，不能从手牌制造登场');
 if(attackMovement){
  game.players[1-owner].field[0][0]=null;game.players[1-owner].field[0][1]=attacker;
  game.recentEvents.push({sequence:251,type:'move',originZone:'field',playerIndex:1-owner,
   text:'合成公开军团在战场内位移',cards:[attacker]});
 }
 game.revision=253;game.phase='Defense';
 game.pendingDefense={attackerPlayer:1-owner,attackerInstanceId:attacker.instanceId,
  target:{type:'master'},stage:'DefenderAttackTiming',attackValue:4000};
 game.effectStack=[{stackItemId:'synthetic-attack-stack',controller:1-owner,sourceInstanceId:attacker.instanceId,
  sourceCardId:attacker.cardId,sourceName:attacker.name,trigger:'opponent-attack',text:'合成对方进攻时机',negated:false}];
 game.recentEvents.push({sequence:252,type:'attack',playerIndex:1-owner,text:'合成公开军团进攻主宰',cards:[attacker]},
  {sequence:253,type:'prompt',playerIndex:owner,text:'合成进攻响应机会',cards:[]});
 game.prompts=[{...prompt('defense-attack-response',attacker.instanceId),sourceCardId:attacker.cardId,createdRevision:253}];
 if(game.recentEvents.some(event=>['play','put','enter'].includes(event.type)))throw Error('进攻夹具不得携带登场事件');
 publish(game);stage.value=attackMovement?'attack-response-field-move':'attack-response';
}
function read(){return{stage:stage.value,owner,attackMovement,mountKey:mountKey.value,phase:l12State.game.phase,
 revision:l12State.game.revision,pendingDefense:JSON.parse(JSON.stringify(l12State.game.pendingDefense||null)),
 publicEvents:l12State.game.recentEvents.map(event=>({sequence:event.sequence,type:event.type,originZone:event.originZone})),
 prompts:JSON.parse(JSON.stringify(l12State.game.prompts)),sent:sent.value,errors:errors.value}}
window.__responseQa={previous,next,same,remount,stable,revision,prepareAttack,attackResponse,read}
createApp({setup(){useLandscapeViewport(mobile);return()=>h('div',[
 h('nav',{class:'response-qa-controls'},[
  ...[['上一个选择',previous],['下一响应',next],['同版重收',same],['同态重挂',remount],['保选测试',stable],['仅更新时钟',revision],['进攻前状态',prepareAttack],['进攻响应',attackResponse]].map(([label,action])=>h('button',{onClick:action},label)),
  h('output',{id:'response-qa-state'},JSON.stringify(read())),
 ]),h(GameBoard,{key:mountKey.value,game:l12State.game,readOnly:false})])}}).mount('#app')
window.__responseQa.ready=true
`
fs.mkdirSync(output, { recursive: true }); fs.mkdirSync(cache, { recursive: true })
let requestCount = 0
const server = await createServer({ configFile: false, root: front, cacheDir: path.join(cache, 'vite'),
  plugins: [{ name: 'response-visibility-fixed-board', enforce: 'pre',
    load(id) { if (id.replaceAll('\\', '/') === boardPath.replaceAll('\\', '/')) return boardSource } },
    vue(), tailwindcss(), { name: 'response-visibility-local-fixture',
      resolveId(id) { if (id === '/__response-entry.js') return '\0response-entry' },
      load(id) { if (id === '\0response-entry') return entry },
      configureServer(instance) { instance.middlewares.use((request, response, next) => {
        const url = new URL(request.url, 'http://127.0.0.1')
        if (requestCount++ < 250) response.on('finish', () => fs.appendFileSync(path.join(output, 'fixture-requests.jsonl'),
          JSON.stringify({ path: url.pathname + url.search, status: response.statusCode,
            destination: request.headers['sec-fetch-dest'] ?? '', contentType: response.getHeader('content-type') ?? '' }) + '\n'))
        if (url.pathname === '/__response-prompt') {
          response.setHeader('content-type', 'text/html; charset=utf-8')
          response.setHeader('Content-Security-Policy', "default-src 'self' data: blob:; connect-src 'self' ws://127.0.0.1:*; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline' 'unsafe-eval'")
          response.end('<!doctype html><html><head><meta charset="utf-8"><title>L12 响应弹框专项</title><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;background:#090f12}.response-qa-controls{position:fixed;z-index:2147483647;top:0;left:0;display:flex;flex-wrap:wrap;gap:4px;background:#101719;padding:4px;color:#fff;font:12px sans-serif}.response-qa-controls button{padding:6px;background:#18494f;color:#fff;border:1px solid #70d7df}.response-qa-controls output{display:block;max-width:100vw;max-height:45px;overflow:auto}#fixture-load-errors:not(:empty){position:fixed;bottom:0;z-index:2147483647;background:#400;color:#fff;white-space:pre-wrap}</style></head><body><div id="app"></div><pre id="fixture-load-errors"></pre><script>window.addEventListener("error",e=>{document.getElementById("fixture-load-errors").textContent+=String(e.message||("resource: "+e.target?.src))+"\\n"},true);window.addEventListener("unhandledrejection",e=>{document.getElementById("fixture-load-errors").textContent+=String(e.reason?.stack||e.reason)+"\\n"});</script><script type="module">import("/__response-entry.js").catch(error=>{document.getElementById("fixture-load-errors").textContent+=String(error.stack||error)})</script></body></html>')
          return
        }
        if (url.pathname.includes('manifest') && url.pathname.endsWith('.json')) {
          response.setHeader('content-type', 'application/json'); response.end(manifest); return
        }
        // Vite image imports (for example trial-back.png?import) are JavaScript
        // modules, not image fetches. Mock only our named fixture resource.
        if (url.pathname === '/__response-art.png') { response.setHeader('content-type', 'image/png'); response.end(art); return }
        if (url.pathname.startsWith('/api/')) {
          response.setHeader('content-type', 'application/json'); response.statusCode = 404
          response.end(JSON.stringify({ message: 'synthetic fixture: API unavailable' })); return
        }
        next()
      }) },
    }], resolve: { alias: { '@': path.join(front, 'src') } },
  server: { host: '127.0.0.1', port: Number(process.env.L12_QA_PORT || 0), strictPort: true }, logLevel: 'warn' })
await server.listen()
const receipt = { scope: 'actual GameBoard/PromptOverlay, synthetic public state and send sink; no engine/WS/ACK claim',
  boardRef: boardRef ?? 'working-tree', url: server.resolvedUrls.local[0] + '__response-prompt',
  sourceSha256: Object.fromEntries(['src/l12/game/GameBoard.vue', 'src/l12/game/PromptOverlay.vue',
    'scripts/verify-response-prompt-visibility-20261028.mjs'].map(leaf => [leaf,
    createHash('sha256').update(leaf.endsWith('GameBoard.vue') ? boardSource : fs.readFileSync(path.join(front, leaf))).digest('hex')])) }
fs.writeFileSync(path.join(output, 'fixture-server.json'), JSON.stringify(receipt, null, 2) + '\n')
console.log(JSON.stringify(receipt))
async function close() { await server.close(); process.exit(0) }
process.on('SIGINT', close); process.on('SIGTERM', close)
