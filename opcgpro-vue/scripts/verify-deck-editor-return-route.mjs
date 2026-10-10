import assert from 'node:assert/strict'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry = `
import {createApp,h} from 'vue'
import {createRouter,createWebHistory,RouterView} from 'vue-router'
import Library from '/src/l12/site/DeckLibraryPage.vue'
import Editor from '/src/l12/L12DeckEditor.vue'
import Detail from '/src/l12/site/PublicDeckDetailPage.vue'
import {loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {platformState,publicDeckApi} from '/src/l12/platform.ts'
import '/src/style.css'

const preset=(await loadOfficialPresetDecks())[0]
const deck={...preset,name:'返回来源验收牌库',specialIds:preset.specialIds||[],updatedAt:'2026-10-01T00:00:00Z',publicationId:'public-route-qa',publicationVersion:1}
const published={id:'public-route-qa',publicCode:'PD000001',ownerId:'route-author',author:'来源验收作者',deck,views:1,likes:0,copies:0,liked:false,createdAt:deck.updatedAt,updatedAt:deck.updatedAt,seasonCompliant:true}
platformState.account={id:'route-author',username:'来源验收作者',role:'player'}
platformState.token='qa-token'
window.__qaDecks={[deck.name]:deck}
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
  const url=String(input)
  if(url.includes('/api/decks')){
    if(init?.method==='PUT'){
      const value=JSON.parse(String(init.body));window.__qaDecks[value.name]=value
      return new Response(JSON.stringify(value),{headers:{'Content-Type':'application/json'}})
    }
    return new Response(JSON.stringify(Object.values(window.__qaDecks)),{headers:{'Content-Type':'application/json'}})
  }
  if(url.includes('/api/alternate-arts'))return new Response('[]',{headers:{'Content-Type':'application/json'}})
  return originalFetch(input,init)
}
publicDeckApi.list=async()=>[published]
publicDeckApi.get=async()=>published
publicDeckApi.recordView=async()=>published
const router=createRouter({history:createWebHistory(),routes:[
  {path:'/decks',name:'decks',component:Library},
  {path:'/decks/:deckId',name:'public-deck-detail',component:Detail},
  {path:'/deck-editor',name:'deck-editor',component:Editor},
]})
window.__qaRouter=router
createApp({render:()=>h(RouterView)}).use(router).mount('#app')
await router.isReady()
`

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-deck-editor-return-route'),
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'deck-editor-return-route-fixture',
    resolveId(id) { if (id === '/__deck_editor_return_route__.js') return id },
    load(id) { if (id === '/__deck_editor_return_route__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        const pathname = new URL(request.url || '/', 'http://fixture.local').pathname
        if ((pathname === '/decks' || pathname.startsWith('/decks/') || pathname === '/deck-editor') && !pathname.includes('.')) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<meta name="viewport" content="width=device-width,initial-scale=1"><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__deck_editor_return_route__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

let browser
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  const activeTab = () => page.locator('.deck-tabs button.active')

  await page.goto(`${origin}/decks?tab=mine`)
  await page.getByRole('link', { name: '编辑', exact: true }).first().click()
  await page.getByRole('button', { name: '← 返回上一级', exact: true }).waitFor()
  assert.equal(new URL(page.url()).searchParams.get('returnTo'), '/decks?tab=mine')
  await page.getByRole('button', { name: '← 返回上一级', exact: true }).click()
  assert.equal((await activeTab().innerText()).trim(), '我的牌库')

  await page.getByRole('link', { name: '编辑', exact: true }).first().click()
  await page.getByRole('button', { name: '← 返回上一级', exact: true }).waitFor()
  await page.goBack()
  assert.equal((await activeTab().innerText()).trim(), '我的牌库', '浏览器返回必须恢复我的牌库来源')

  await page.goto(`${origin}/decks/PD000001?from=%2Fdecks%3Ftab%3Dplaza`)
  await page.getByRole('button', { name: '编辑牌库', exact: true }).click()
  await page.getByRole('button', { name: '← 返回上一级', exact: true }).waitFor()
  const publicSource = '/decks/PD000001?from=%2Fdecks%3Ftab%3Dplaza'
  assert.equal(new URL(page.url()).searchParams.get('returnTo'), publicSource)
  await page.reload()
  await page.getByRole('button', { name: '← 返回上一级', exact: true }).click()
  assert.equal(new URL(page.url()).pathname, '/decks/PD000001', '刷新后必须仍返回公开牌库详情来源')

  await page.goto(`${origin}/deck-editor`)
  await page.getByRole('button', { name: '← 返回上一级', exact: true }).click()
  assert.equal(new URL(page.url()).pathname + new URL(page.url()).search, '/decks?tab=mine', '直达编辑器必须合理兜底到我的牌库')
  assert.equal((await activeTab().innerText()).trim(), '我的牌库')

  assert.deepEqual(errors, [])
  console.log(JSON.stringify({ status: 'passed', scenarios: ['mine-return', 'browser-back', 'public-detail-refresh', 'direct-fallback'] }))
} finally {
  await browser?.close()
  await server.close()
}
