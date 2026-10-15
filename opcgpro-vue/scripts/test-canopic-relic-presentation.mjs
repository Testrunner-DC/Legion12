import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { createRequire } from 'node:module'

const root = path.resolve(import.meta.dirname, '..')
const browserMode = process.argv.includes('--browser')
const redMode = process.argv.includes('--red')
if (!redMode) {
  const { canopicRelicPresentation } = await import('../src/l12/game/canopicRelicPresentation.ts')
  const jar = { instanceId: 'real-jar', cardId: 'authoritative-id', name: 'renamed' }
  const ordinary = { instanceId: 'ordinary', cardId: 'ordinary-id', name: '卡诺匹斯罐' }
  for (const isis of [false, true]) {
    const player = { relic: ordinary, extraRelics: [jar, jar], specialZones: {
      canopicProgress: [jar, jar], canopicTrack: isis ? [{ completed: true }] : [],
    } }
    const before = JSON.stringify(player)
    const view = canopicRelicPresentation(player)
    assert.equal(view.primary, ordinary)
    assert.deepEqual(view.retained.map(c => c.instanceId), isis ? [] : ['real-jar'])
    assert.equal(JSON.stringify(player), before, 'display grouping must not mutate the snapshot')
    player.relic = jar
    assert.equal(canopicRelicPresentation(player).primary, null, 'legacy primary cannot occupy the ordinary slot')
    assert.deepEqual(canopicRelicPresentation(player).retained.map(c => c.instanceId), isis ? [] : ['real-jar'])
  }
  assert.deepEqual(canopicRelicPresentation({ relic: ordinary }), { primary: ordinary, retained: [] })
  console.log('Canopic display: authoritative membership, instance deduplication, legacy primary and pure grouping passed')
}

if (browserMode) {
  const { createServer } = await import('vite')
  const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
    || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  assert.ok(process.env.L12_CANOPIC_FIXTURE_OUT, 'export real engine snapshots with the focused C# fixture first')
  assert.ok(process.env.L12_QA_OUT, 'use a dedicated external evidence directory')
  const output = process.env.L12_QA_OUT
  fs.mkdirSync(output, { recursive: true })
  const fixtureBytes = fs.readFileSync(process.env.L12_CANOPIC_FIXTURE_OUT)
  const fixtures = JSON.parse(fixtureBytes)
  assert.equal(fixtures.length, 160)
  const entry = `
import {createApp,h,nextTick,ref} from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import {l12State} from '/src/l12/net.ts'
import {useLandscapeViewport} from '/src/l12/mobileViewport.ts'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
window.fixtures=await (await fetch('/__canopic_fixtures')).json()
window.picked=[];window.mats=new Map();window.sent=[]
const version=ref('initial')
window.applyFixture=async (index,preserveInstance=false)=>{
 const f=window.fixtures[index],game=structuredClone(f.snapshot)
 l12State.game=game;l12State.status='online';l12State.pendingAction=false
 l12State.spectating=f.view==='spectator'||f.view==='referee';l12State.gmEnabled=f.view==='referee'
 l12State.room={roomCode:'LOCAL',yourPlayerIndex:game.you,players:game.players.map(p=>({...p,connected:true,ready:true,deckIndex:0})),decks:[],started:true}
 l12State.socket={readyState:WebSocket.OPEN,send:payload=>window.sent.push(JSON.parse(payload))}
 window.picked=[];if(!preserveInstance)version.value=f.id
 await nextTick();await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame)
 return [...window.mats.values()].map(vm=>({uid:vm.$.uid,owner:vm.$props.player.playerIndex,master:vm.$props.player.master.masterId,
  presence:vm.$props.player.specialZones.canopicProgress.map(c=>c.instanceId),
  primary:vm.$props.player.relic?.instanceId??null}))
}
await window.applyFixture(0)
const app=createApp({setup(){useLandscapeViewport(ref(true));return()=>h(GameBoard,{game:l12State.game,key:version.value})}})
// Observe the real PlayerMat event without replacing its player prop or listener.
app.mixin({beforeMount(){if(this.$options.__name==='PlayerMat'){
 const instance=this.$,old=instance.emit
 instance.emit=(event,...args)=>{if(event==='selectCard')window.picked.push(args[0].instanceId);return old(event,...args)}
}},mounted(){if(this.$options.__name==='PlayerMat')window.mats.set(this.$.uid,this);if(this.$options.__name==='GameBoard')window.boardUid=this.$.uid},
beforeUnmount(){window.mats.delete(this.$.uid)}})
app.mount('#app');window.ready=true
`
  const server = await createServer({ root, configLoader: 'runner',
    cacheDir: path.join(output, 'vite-cache'), server: { host: '127.0.0.1', port: 0 },
    plugins: [{ name: 'canopic-real-gameboard',
      resolveId(id) { if (id === '/__canopic_entry.js') return id },
      load(id) { if (id === '/__canopic_entry.js') return entry },
      configureServer(vite) { vite.middlewares.use((req, res, next) => {
        if (req.url === '/__canopic_fixtures') {res.setHeader('Content-Type', 'application/json');res.end(fixtureBytes);return}
        if (req.url === '/__canopic_acceptance') {
          res.setHeader('Content-Type', 'text/html')
          res.end('<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><div id="l12-landscape-teleports"></div><div id="app" class="l12-landscape-surface"></div><script type="module" src="/__canopic_entry.js"></script>')
          return
        }
        next()
      }) },
    }],
  })
  const report = { passed: false, scope: 'Actual GameBoard and PlayerMat SFC, engine-exported snapshots; local component acceptance, no production match',
    fixtureSha256: createHash('sha256').update(fixtureBytes).digest('hex'), results: [] }
  let browser, page
  try {
    await server.listen()
    browser = await chromium.launch({ headless: true, channel: 'msedge' })
    for (const viewport of [{ width: 1920, height: 1080 }, { width: 844, height: 390 }, { width: 390, height: 844 }]) {
      page = await browser.newPage({ viewport })
      const errors = []
      page.on('pageerror', e => errors.push(e.message))
      await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
      await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__canopic_acceptance`)
      await page.waitForFunction(() => window.ready, { timeout: 30000 })
      for (let i = 0; i < fixtures.length; i++) {
        const f = fixtures[i]
        report.current = { viewport, id: f.id }
        const actual = await page.evaluate(index => window.applyFixture(index), i)
        const owner = actual.find(p => p.owner === f.owner)
        assert.ok(owner, 'actual PlayerMat must receive the fixture before assertions')
        assert.equal(owner.master, f.master)
        assert.deepEqual(owner.presence.toSorted(), f.canopicIds.toSorted())
        report.current.snapshotReachedRealComponent = true
        const mat = page.locator(`.l12-player-mat[data-player-index="${f.owner}"]`)
        assert.equal(await mat.count(), 1)
        const isis = f.master === 'S01-02M1'
        assert.equal(await mat.locator('.canopic-orb.completed').count(), isis ? f.count : 0, f.id + ' markers')
        const rail = mat.locator('.retained-relics')
        const retained = await rail.locator('[data-card-instance-id]').evaluateAll(nodes => nodes.map(n => n.dataset.cardInstanceId))
        assert.deepEqual(retained.toSorted(), isis ? [] : f.canopicIds.toSorted(), f.id + ' retained instances')
        assert.equal(await mat.locator('.extra-relic').count(), 0, 'old overlapping cards must be retired')
        const normal = mat.locator('.relic-zone [data-card-instance-id]')
        assert.equal(await normal.count(), f.normalId ? 1 : 0, f.id + ' primary slot')
        if (!isis) {
          const zoneBox = await mat.locator('.relic-zone').boundingBox()
          const railBox = await rail.boundingBox()
          const rotated = await page.evaluate(() => document.documentElement.dataset.l12Rotated === 'true')
          assert.ok(rotated ? railBox.x + railBox.width <= zoneBox.x + 1 : railBox.y >= zoneBox.y + zoneBox.height - 1,
            'retained rail must be below the ordinary relic in the logical board coordinates')
          const fields = await mat.locator('.formation-slot').evaluateAll(nodes => nodes.map(n => {
            const r=n.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height}
          }))
          assert.equal(fields.length, 6)
          for(const field of fields) assert.ok(railBox.x + railBox.width <= field.x + 1 || field.x + field.width <= railBox.x + 1
            || railBox.y + railBox.height <= field.y + 1 || field.y + field.height <= railBox.y + 1,
            'retained rail must not overlap any of the six formation slots')
          for (const id of f.canopicIds) {
            const button = rail.locator(`[data-card-instance-id="${id}"]`)
            await button.evaluate(node => node.scrollIntoView({ block: 'nearest', inline: 'center' }))
            await button.click({ timeout: 3000 })
            assert.equal(await page.evaluate(() => window.picked.at(-1)), id, 'real retained-card click event')
          }
        }
        if (f.normalId) {
          assert.equal(await normal.getAttribute('data-card-instance-id'), f.normalId)
          const coverage = await normal.evaluate(node => {
            const r = node.getBoundingClientRect()
            return [[.15,.15],[.85,.15],[.5,.5],[.15,.85],[.85,.85]].map(([x,y])=>
              document.elementFromPoint(r.left+r.width*x,r.top+r.height*y)?.closest('.card-tile')?.dataset.cardInstanceId)
          })
          assert.deepEqual(coverage, Array(5).fill(f.normalId), f.id + ' ordinary relic must not be covered')
          await normal.click({ timeout: 3000 })
          assert.equal(await page.evaluate(() => window.picked.at(-1)), f.normalId, 'real ordinary-card click event')
        }
        assert.deepEqual(errors, [], 'actual component runtime errors')
        assert.deepEqual(await page.evaluate(() => window.sent), [], 'presentation must not send game commands')
        if (f.owner === 0 && f.view === 'owner' && !f.recover && f.scenario === 'five-extra')
          await page.screenshot({ path: path.join(output, `${viewport.width}x${viewport.height}-${isis ? 'isis' : 'sun-city'}.png`) })
        report.results.push({ viewport, id: f.id, snapshotReachedRealComponent: true, passed: true })
      }
      // Continuous prop update on the SAME board/mat instances, using another
      // unmodified real engine snapshot with this owner's relic zone empty.
      for(let i=0;i<fixtures.length;i++) {
        const f=fixtures[i]
        if(f.scenario!=='five-extra'||f.recover||!['owner','opponent'].includes(f.view))continue
        report.current={viewport,id:f.id,transition:'present-to-absent'}
        const before=await page.evaluate(index=>window.applyFixture(index),i)
        const oldUid=before.find(p=>p.owner===f.owner).uid
        const oldBoardUid=await page.evaluate(()=>window.boardUid)
        const absent=fixtures.findIndex(a=>a.master===f.master&&a.owner===1-f.owner&&a.scenario===f.scenario&&!a.recover
          &&a.view===(f.view==='owner'?'opponent':'owner'))
        assert.ok(absent>=0)
        assert.equal(fixtures[absent].snapshot.you,f.snapshot.you,'viewer identity must stay stable')
        assert.equal(fixtures[absent].snapshot.players[f.owner].specialZones.canopicProgress.length,0)
        const after=await page.evaluate(index=>window.applyFixture(index,true),absent)
        const updated=after.find(p=>p.owner===f.owner)
        assert.equal(updated.uid,oldUid,'same PlayerMat instance must receive the update')
        assert.equal(await page.evaluate(()=>window.boardUid),oldBoardUid,'same GameBoard instance')
        assert.deepEqual(updated.presence,[],'absence snapshot must reach the real PlayerMat prop')
        const mat=page.locator(`.l12-player-mat[data-player-index="${f.owner}"]`)
        assert.equal(await mat.locator('.canopic-orb.completed').count(),0,'marker must extinguish on removal')
        assert.equal(await mat.locator('.retained-relics').count(),0,'bottom rail must disappear on removal')
        assert.equal(await mat.locator('[data-card-instance-id^="ui-canopic-"]').count(),0)
        assert.deepEqual(errors,[])
        assert.deepEqual(await page.evaluate(()=>window.sent),[])
        report.results.push({viewport,id:f.id,transition:'present-to-absent',sameBoardAndMat:true,passed:true})
      }
      await page.close();page = null
      console.log(`Real GameBoard canopic cases: ${report.results.length} passed`)
    }
    delete report.current
    report.passed = true
    console.log(`Real GameBoard canopic presentation: ${report.results.length}/${report.results.length}`)
  } catch (error) {
    report.error = error.stack
    if (page) await page.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {})
    throw error
  } finally {
    fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
    await browser?.close();await server.close()
  }
}
