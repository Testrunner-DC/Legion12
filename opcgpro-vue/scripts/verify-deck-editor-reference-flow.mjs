import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
import jsQR from 'jsqr'
import sharp from 'sharp'

const root = path.resolve(import.meta.dirname, '..')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = path.resolve(root, '../artifacts/deck-editor-reference-flow')
fs.mkdirSync(out, { recursive: true })
const entry = `
import {createApp} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import Editor from '/src/l12/L12DeckEditor.vue'
import {loadDeckCatalog,loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {platformState,publicDeckApi,alternateArtApi} from '/src/l12/platform.ts'
import '/src/style.css'
const [cards,presets]=await Promise.all([loadDeckCatalog(),loadOfficialPresetDecks()])
const deck=presets.find(item=>item.cardIds.length>=40)
if(!deck)throw Error('fixture deck missing')
const fixtureMaster=cards.find(item=>item.id===deck.masterId)
const fixtureTrial=cards.find(item=>item.cardType==='trial')
if(fixtureMaster&&fixtureTrial)fixtureTrial.faction=fixtureMaster.faction
const bench=deck.cardIds.slice(0,2)
const saved={...deck,publicationId:'qa-public',publicationVersion:1,name:'含超长名称与编号截断验收的编辑流程牌库',benchIds:bench,updatedAt:new Date().toISOString()}
platformState.account={id:'author',username:'验收作者',role:'player',createdAt:'2026-09-24',publicHistory:true}
alternateArtApi.mine=async()=>[]
const initialDetails={guide:{buildIdea:'这是一段用于验证宽屏与移动端长内容输入的构筑思路。'.repeat(10),opening:'优先保留低费军团与互动战术。',keyCards:'关键牌与配合说明。',commonSequence:'第一回合建立前排，随后根据对手资源调整。',substitutions:'环境变化时替换对应功能牌。'},matchups:[],contentRevision:4,contentUpdatedAt:'2026-09-24T08:00:00Z',versions:[],matches:[],matchBindingStatus:'unavailable',matchBindingMessage:'暂无关联对局'}
let details=JSON.parse(localStorage.getItem('qa-public-details')||JSON.stringify(initialDetails))
publicDeckApi.get=async()=>({id:'qa-public',ownerId:'author',author:'验收作者',deck:saved,views:1,likes:0,copies:0,liked:false,createdAt:'',updatedAt:'',details})
publicDeckApi.updateContent=async(_id,guide,matchups)=>{details={...details,guide,matchups,contentRevision:details.contentRevision+1,contentUpdatedAt:new Date().toISOString()};localStorage.setItem('qa-public-details',JSON.stringify(details));return details}
localStorage.setItem('l12-custom-decks-v1:author',JSON.stringify({[saved.name]:saved}))
if(new URL(location.href).searchParams.has('private')){delete saved.publicationId;delete saved.publicationVersion;localStorage.setItem('l12-custom-decks-v1:author',JSON.stringify({[saved.name]:saved}))}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/deck-editor',component:Editor},{name:'public-deck-detail',path:'/decks/:deckId',component:{template:'<div></div>'}}]})
await router.push('/deck-editor?deck='+encodeURIComponent(saved.name)+'&published=qa-public')
createApp(Editor).use(router).mount('#app')
`

const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'deck-editor-reference-fixture',
  resolveId(id) { if (id === '/__deck_editor_reference__.js') return id },
  load(id) { if (id === '/__deck_editor_reference__.js') return entry },
  configureServer(vite) { vite.middlewares.use((request, response, next) => {
    if (request.url?.startsWith('/__deck_editor_reference__') && !request.url.includes('.js')) {
      response.setHeader('Content-Type', 'text/html')
      response.end('<div id="app"></div><script type="module" src="/__deck_editor_reference__.js"></script>')
      return
    }
    next()
  }) },
}] })

let browser
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname !== '127.0.0.1') return route.abort()
    if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion: 3, catalogVersion: 'qa', assetVersion: 'qa', basePath: '/', cards: {} } })
    return route.continue()
  })
  const port = server.httpServer.address().port
  const profiles = [
    ['wide-1920', 1920, 1080], ['wide-1440', 1440, 900], ['wide-1280', 1280, 720],
    ['phone-390', 390, 844], ['phone-360', 360, 780], ['short-landscape', 844, 390],
  ]
  for (const [name, width, height] of profiles) {
    await page.setViewportSize({ width, height })
    await page.goto(`http://127.0.0.1:${port}/__deck_editor_reference__`)
    await page.locator('.deck-builder-grid').waitFor()
    assert.equal(await page.locator('.saved-decks-panel').count(), 1)
    assert.equal(await page.locator('.saved-decks-panel select').isVisible(), true)
    await page.screenshot({ path: path.join(out, `${name}-saved-decks.png`), fullPage: true })
    const narrow = width <= 820
    if (narrow) {
      await page.getByRole('button', { name: '更多操作', exact: true }).click()
      assert.equal(await page.locator('.secondary-actions').isVisible(), true, `${name} 次要操作菜单未展开`)
      await page.getByRole('button', { name: '更多操作', exact: true }).click()
      await page.getByRole('button', { name: /牌表/ }).click()
      await page.locator('[data-deck-section="bench"]').waitFor()
      assert.equal(await page.locator('.deck-center-column').isVisible(), false)
      await page.screenshot({ path: path.join(out, `${name}-deck.png`), fullPage: true })
      await page.getByRole('button', { name: '统计 / 起手' }).click()
      await page.getByRole('button', { name: '起手', exact: true }).click()
      assert.equal(await page.locator('[data-editor-workspace="hand"]').isVisible(), true)
      assert.equal(await page.locator('.editor-opening-hand article').count(), 6)
      await page.screenshot({ path: path.join(out, `${name}-hand.png`), fullPage: true })
      await page.getByRole('button', { name: '卡池' }).click()
      await page.locator('.catalog-tabs').getByRole('button', { name: '主牌库' }).click()
      const visibleFilters = page.locator('.catalog-filter-bar')
      assert.equal(await visibleFilters.isVisible(), true, `${name} 常驻筛选未显示`)
      assert.equal(await visibleFilters.locator('label').count(), 8, `${name} 常驻筛选数量错误`)
      await page.locator('.pool-selector-trigger').click()
      const productPopup = page.locator('.product-filter')
      const productButton = productPopup.getByRole('button', { name: 'S01', exact: true })
      if (await productButton.count()) await productButton.click()
      const popup = await productPopup.boundingBox()
      assert.ok(popup && popup.x >= 0 && popup.x + popup.width <= width + 1, `${name} 卡池选择器横向越界`)
      await page.screenshot({ path: path.join(out, `${name}-filters.png`), fullPage: true })
      await productPopup.getByRole('button', { name: '完成', exact: true }).click()
    } else {
      await page.getByRole('button', { name: '统计', exact: true }).click()
      assert.equal(await page.locator('[data-editor-workspace="stats"]').isVisible(), true)
      assert.equal(await page.locator('.deck-list').isVisible(), true)
      await page.screenshot({ path: path.join(out, `${name}-stats.png`), fullPage: true })
      await page.getByRole('button', { name: '起手', exact: true }).click()
      assert.equal(await page.locator('.editor-opening-hand article').count(), 6)
      const clippedHandContent = await page.locator('.editor-opening-hand article').evaluateAll(cards => cards.some(card => [...card.children].some(child => child.scrollWidth > child.clientWidth + 1 || child.scrollHeight > child.clientHeight + 1)))
      assert.equal(clippedHandContent, false, `${name} 起手名称、编号或概率说明被裁切`)
      await page.screenshot({ path: path.join(out, `${name}-hand.png`), fullPage: true })
      if (name === 'wide-1440') {
        await page.getByRole('button', { name: '卡池', exact: true }).click()
        await page.locator('.catalog-tabs').getByRole('button', { name: '额外卡牌', exact: true }).click()
        const horizontalCard = page.locator('.deck-card.landscape-thumbnail').first()
        if (await horizontalCard.count()) {
          const imageBox = await horizontalCard.locator('.card-image').boundingBox()
          assert.ok(imageBox && imageBox.width > imageBox.height, '横置卡牌缩略图没有保持自然横向')
          await page.screenshot({ path: path.join(out, `${name}-horizontal.png`), fullPage: true })
        }
        assert.equal(await page.locator('.saved-decks-panel').count(), 1, '已保存牌库区必须唯一')
        assert.equal(await page.locator('.saved-decks-panel').isVisible(), true)
        await page.screenshot({ path: path.join(out, `${name}-saved-decks.png`), fullPage: true })
        await page.locator('.detail-panel-heading').getByRole('button', { name: '收起', exact: true }).click()
        assert.equal(await page.locator('.deck-builder-grid').getAttribute('class').then(value => value?.includes('detail-collapsed')), true)
        await page.locator('.detail-panel-heading').getByRole('button', { name: '展开', exact: true }).click()
      }
    }
    await page.getByRole('button', { name: '公开内容', exact: true }).first().click()
    await page.locator('[data-editor-workspace="public-content"]').waitFor()
    await page.screenshot({ path: path.join(out, `${name}-content.png`), fullPage: true })
    if (name === 'wide-1440') {
      const savedBuildIdea = '保存闭环：修改公开内容后重新打开编辑器仍然保留。'
      const revisionBeforeSave = await page.locator('.content-status b').textContent()
      await page.getByLabel('构筑思路').fill(savedBuildIdea)
      await page.getByRole('button', { name: '保存公开内容', exact: true }).click()
      await page.waitForFunction(previous => document.querySelector('.content-status b')?.textContent !== previous, revisionBeforeSave)
      await page.reload()
      await page.locator('.deck-builder-grid').waitFor()
      await page.getByRole('button', { name: '公开内容', exact: true }).first().click()
      await page.locator('[data-editor-workspace="public-content"]').waitFor()
      assert.equal(await page.getByLabel('构筑思路').inputValue(), savedBuildIdea, '公开内容保存后重开未恢复')
      await page.getByRole('button', { name: '生成牌库图', exact: true }).click()
      await page.locator('.deck-image-dialog').waitFor({ timeout: 15000 })
      await page.screenshot({ path: path.join(out, `${name}-public-qr.png`), fullPage: true })
      await page.locator('.deck-image-dialog footer').getByRole('button', { name: '关闭', exact: true }).click()
    }
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)
    assert.equal(overflow, false, `${name} 出现页面级横向溢出`)
  }
  const qrScreenshot = path.join(out, 'wide-1440-public-qr.png')
  const qrPixels = await sharp(qrScreenshot).ensureAlpha().raw().toBuffer({ resolveWithObject: true })
  const decodedQr = jsQR(new Uint8ClampedArray(qrPixels.data), qrPixels.info.width, qrPixels.info.height)
  assert.equal(decodedQr?.data, `http://127.0.0.1:${port}/decks/qa-public`, '牌库图内嵌二维码无法从最终截图扫描')
  await page.setViewportSize({width:1440,height:900})
  await page.goto(`http://127.0.0.1:${port}/__deck_editor_reference__`)
  await page.locator('.deck-builder-grid').waitFor()
  // A copied/local deck has no publication provenance even if the route still contains a publication id.
  await page.evaluate(() => { const key='l12-custom-decks-v1:author'; const rows=JSON.parse(localStorage.getItem(key)); Object.values(rows).forEach(deck=>{delete deck.publicationId;delete deck.publicationVersion});localStorage.setItem(key,JSON.stringify(rows)); window.__qaPrivate=true })
  await page.locator('.saved-decks-panel select').selectOption({index:1})
  // Reload the fixture in explicitly private mode so its local seed cannot add provenance back.
  await page.goto(`http://127.0.0.1:${port}/__deck_editor_reference__?private=1`)
  await page.locator('.deck-builder-grid').waitFor()
  await page.getByRole('button', {name:'生成牌库图',exact:true}).click()
  await page.locator('.deck-image-dialog').waitFor()
  const privateShot=path.join(out,'wide-1440-private-no-qr.png')
  await page.screenshot({path:privateShot,fullPage:true})
  const raw=await sharp(privateShot).ensureAlpha().raw().toBuffer({resolveWithObject:true})
  assert.equal(jsQR(new Uint8ClampedArray(raw.data),raw.info.width,raw.info.height),null,'未公开牌库不得含二维码')
  assert.deepEqual(errors, [])
  console.log(JSON.stringify({ status: 'passed', viewports: profiles.length, screenshots: fs.readdirSync(out).filter(name => name.endsWith('.png')).length, output: out }))
} finally {
  await browser?.close()
  await server.close()
}
