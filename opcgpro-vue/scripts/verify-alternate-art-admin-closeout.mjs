import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/alternate-art-admin-closeout')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,defineComponent,h,ref} from 'vue'
import '/src/style.css'
import AdminAccountPicker from '/src/l12/site/AdminAccountPicker.vue'
import {adminApi,authState,platformState} from '@/l12/platform'
const accounts=[
 {id:'player-0001-unique',username:'桐',role:'player',createdAt:'',publicHistory:false,permissions:[]},
 {id:'player-0002-unique',username:'桐',role:'player',createdAt:'',publicHistory:false,permissions:[]},
 {id:'player-0003-disabled',username:'桐禁用',role:'player',createdAt:'',publicHistory:false,permissions:[],disabled:true},
 {id:'player-0004-deleted',username:'桐删除',role:'player',createdAt:'',publicHistory:false,permissions:[],deleted:true},
 {id:'player-0005-other',username:'青禾',role:'player',createdAt:'',publicHistory:false,permissions:[]},
]
let reads=0
adminApi.accounts=async()=>{reads++;return accounts}
authState.verified=true
platformState.token='qa-token'
platformState.account={id:'admin-a',username:'AdminA',role:'admin',createdAt:'',publicHistory:false,permissions:['admin.accounts.read','admin.content.draft']}
window.__qa={reads:()=>reads,auth:()=>({verified:authState.verified,account:platformState.account})}
const App=defineComponent({setup(){
 const accountId=ref('')
 const secondAccountId=ref('')
 const switchAccount=()=>{platformState.account={...platformState.account,id:'admin-b',username:'AdminB'}}
 return()=>h('main',{class:'qa-altart'},[
  h('h1','异画派发玩家选择'),
  h(AdminAccountPicker,{modelValue:accountId.value,'onUpdate:modelValue':value=>accountId.value=value,label:'玩家账号'}),
  h(AdminAccountPicker,{modelValue:secondAccountId.value,'onUpdate:modelValue':value=>secondAccountId.value=value,label:'第二玩家账号'}),
  h('output',{id:'selected-id'},accountId.value),
  h('button',{id:'switch-account',onClick:switchAccount},'切换管理员账号'),
 ])
}})
createApp(App).mount('#app')
`

const galleryEntry = `
import {createApp} from 'vue'
import '/src/style.css'
import CardArchive from '/src/l12/CardArchive.vue'
import {alternateArtApi,authState,platformState} from '@/l12/platform'
const now=new Date().toISOString()
const product=(id,name)=>({id,name,active:true,createdAt:now,updatedAt:now,source:'catalog',categoryName:'卡牌系列'})
const rarity=(id,code,name)=>({id,code,name,active:true,createdAt:now,updatedAt:now})
alternateArtApi.gallery=async()=>[
 {id:'official-s01-0101b',artCode:'S01-0101b',baseCardId:'S01-0101',displayName:'吕布',mediaAssetId:'',imageUrl:'',thumbnailUrl:'',active:true,createdAt:now,updatedAt:now,cardImageId:'S01-0101b',builtIn:true,kind:'alternate-art',products:[product('p1','QA产品甲'),product('p2','QA产品乙')],rarities:[rarity('r1','UR','终极罕贵'),rarity('r2','SP','特别罕贵')]},
 {id:'official-old-fields',artCode:'S01-0104a',baseCardId:'S01-0104',displayName:'韩信',mediaAssetId:'',imageUrl:'',thumbnailUrl:'',active:true,createdAt:now,updatedAt:now,cardImageId:'S01-0104a',builtIn:true,kind:'alternate-art',productName:'旧字段产品'},
 {id:'official-explicit-clear',artCode:'S01-0117a',baseCardId:'S01-0117',displayName:'山河社稷图',mediaAssetId:'',imageUrl:'',thumbnailUrl:'',active:true,createdAt:now,updatedAt:now,cardImageId:'S01-0117a',builtIn:true,kind:'alternate-art',products:[],rarities:[]},
 {id:'custom-alt-metadata',artCode:'ALT-QA-MULTI',baseCardId:'S01-0101',displayName:'吕布',mediaAssetId:'qa',imageUrl:'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%22500%22 height=%22700%22/%3E',thumbnailUrl:'',active:true,createdAt:now,updatedAt:now,kind:'alternate-art',products:[product('dup-a','同名产品'),product('dup-b','同名产品')],rarities:[rarity('r1','UR','终极罕贵'),rarity('r2','SP','特别罕贵')]},
 {id:'gallery-collab-1',artCode:'COLLAB-QA-001',baseCardId:'',displayName:'联动画廊测试',mediaAssetId:'qa',imageUrl:'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%22500%22 height=%22700%22%3E%3Crect width=%22500%22 height=%22700%22 fill=%22%232b3850%22/%3E%3Ctext x=%2250%22 y=%22350%22 fill=%22white%22 font-size=%2240%22%3ECOLLAB%3C/text%3E%3C/svg%3E',thumbnailUrl:'',active:true,createdAt:now,updatedAt:now,kind:'gallery-card',effect:'这是独立撰写的联动卡效，但不会接入对局结算。',products:[product('p1','QA产品甲'),product('p2','QA产品乙')],rarities:[rarity('r1','UR','终极罕贵'),rarity('r2','SP','特别罕贵')]}
]
alternateArtApi.mine=async()=>[]
authState.verified=false
platformState.token=''
platformState.account=null
createApp(CardArchive).mount('#app')
`

const adminPanelEntry = `
import {createApp} from 'vue'
import '/src/style.css'
import AdminAlternateArtsPanel from '/src/l12/site/AdminAlternateArtsPanel.vue'
import {adminApi,authState,platformState} from '@/l12/platform'
const now=new Date().toISOString()
const products=[
 {id:'catalog:qa-a',name:'同名产品',active:true,createdAt:now,updatedAt:now,categoryName:'卡牌系列',source:'catalog',status:'active'},
 {id:'site:qa-b',name:'同名产品',active:true,createdAt:now,updatedAt:now,categoryName:'联动产品',source:'site',status:'published'},
]
const rarities=[
 {id:'rarity-ur',code:'UR',name:'终极罕贵',active:true,createdAt:now,updatedAt:now},
 {id:'rarity-sp',code:'SP',name:'特别罕贵',active:true,createdAt:now,updatedAt:now},
]
let builtIn={id:'catalog-alt:S01-0101b',artCode:'S01-0101b',baseCardId:'S01-0101',baseCardName:'吕布',displayName:'吕布',mediaAssetId:'',imageUrl:'',thumbnailUrl:'',active:true,createdAt:now,updatedAt:now,cardImageId:'S01-0101b',builtIn:true,kind:'alternate-art',products:[products[0]],rarities:[rarities[0]]}
const media={id:'media-gallery',kind:'card-art',altText:'联动卡图',focalX:.5,focalY:.5,originalFormat:'png',contentHash:'abcdef1234567890',desktopUrl:'',mobileUrl:'',thumbnailUrl:'',desktopWidth:500,desktopHeight:700,mobileWidth:500,mobileHeight:700,thumbnailWidth:250,thumbnailHeight:350,originalBytes:1,deliveryBytes:1,createdBy:'admin',createdAt:now,referenceCount:0,desktopAltText:'',mobileAltText:'',thumbnailAltText:'',independentVariants:false}
const calls={metadata:[],saveArt:[],grant:[],saveRarity:[],search:0}
let failNextGalleryMetadata=false
adminApi.alternateArtGrants=async()=>[]
adminApi.siteMedia=async()=>[media]
adminApi.alternateArtAwardRules=async()=>[]
adminApi.alternateArtProducts=async()=>products
adminApi.alternateArtRarities=async()=>rarities
adminApi.alternateArts=async()=>[builtIn]
adminApi.searchAlternateArts=async()=>{calls.search++;return {items:[builtIn],total:1,page:1,pageSize:20}}
adminApi.saveAlternateArtMetadata=async draft=>{calls.metadata.push(structuredClone(draft));if(draft.alternateArtId==='gallery-new'&&failNextGalleryMetadata){failNextGalleryMetadata=false;throw new Error('注入：元数据暂时保存失败')}const selectedProducts=draft.productIds.map(id=>products.find(item=>item.id===id)).filter(Boolean);const selectedRarities=draft.rarityIds.map(id=>rarities.find(item=>item.id===id)).filter(Boolean);if(draft.alternateArtId===builtIn.id){builtIn={...builtIn,products:selectedProducts,rarities:selectedRarities};return builtIn}return {...window.__lastSavedArt,products:selectedProducts,rarities:selectedRarities}}
adminApi.saveAlternateArt=async draft=>{if(!draft.id&&calls.saveArt.some(item=>item.artCode===draft.artCode))throw new Error('异画编号已被使用');calls.saveArt.push(structuredClone(draft));window.__lastSavedArt={...window.__lastSavedArt,...draft,id:draft.id||'gallery-new',imageUrl:'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%22500%22 height=%22700%22/%3E',thumbnailUrl:'',createdAt:now,updatedAt:now};return window.__lastSavedArt}
adminApi.saveAlternateArtRarity=async draft=>{calls.saveRarity.push(structuredClone(draft));return {...draft,id:'unexpected',createdAt:now,updatedAt:now}}
adminApi.grantAlternateArt=async draft=>{calls.grant.push(structuredClone(draft));throw new Error('gallery card must not grant')}
adminApi.accounts=async()=>[]
authState.verified=true
platformState.token='qa-token'
platformState.account={id:'admin-a',username:'AdminA',role:'admin',createdAt:'',publicHistory:false,permissions:['admin.accounts.read','admin.content.draft']}
window.__panelQa={calls,products,rarities,builtIn:()=>builtIn,failNextGalleryMetadata:()=>{failNextGalleryMetadata=true}}
createApp(AdminAlternateArtsPanel).mount('#app')
`

const inputFiles = [
  'src/l12/CardArchive.vue', 'src/l12/CardDetailContent.vue',
  'src/l12/site/AdminAlternateArtsPanel.vue', 'scripts/verify-alternate-art-admin-closeout.mjs',
]
const sourceHashes = () => Object.fromEntries(inputFiles.map(file => [file,
  createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')]))
const beforeSourceHashes = sourceHashes()

let browser
let finalPayload
const server = await createServer({
  root, configLoader: 'runner', cacheDir: process.env.L12_QA_CACHE || path.join(output, 'vite-cache'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'alternate-art-admin-closeout-fixture',
    resolveId(id) { if (['/__alternate_art_admin_closeout__.js','/__alternate_art_gallery_closeout__.js','/__alternate_art_panel_closeout__.js'].includes(id)) return id },
    load(id) { if (id === '/__alternate_art_admin_closeout__.js') return entry; if (id === '/__alternate_art_gallery_closeout__.js') return galleryEntry; if (id === '/__alternate_art_panel_closeout__.js') return adminPanelEntry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__alternate_art_admin_closeout__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#eee}.qa-altart{display:grid;gap:18px;width:min(720px,calc(100% - 32px));margin:auto;padding:24px 0}.qa-altart>button{min-height:44px}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__alternate_art_admin_closeout__.js"></script>')
          return
        }
        if (request.url?.match(/^\/__alternate_art_gallery_closeout__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#eee}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__alternate_art_gallery_closeout__.js"></script>')
          return
        }
        if (request.url?.match(/^\/__alternate_art_panel_closeout__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#eee}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__alternate_art_panel_closeout__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const viewports = [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 844, height: 390 }]
const suffix = viewport => `${viewport.width}x${viewport.height}`
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const pageErrors = []
  const consoleProblems = []
  page.on('pageerror', error => pageErrors.push(error.message))
  page.on('console', message => { if (['warning', 'error'].includes(message.type())) consoleProblems.push(message.text()) })
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const report = []
  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__alternate_art_admin_closeout__`)
    const input = page.locator('input[role="combobox"]').nth(0)
    const secondInput = page.locator('input[role="combobox"]').nth(1)
    await input.waitFor()
    await page.waitForTimeout(100)
    assert.ok(await page.evaluate(() => window.__qa.reads()) > 0,
      `account candidates were not requested at ${suffix(viewport)}: ${pageErrors.join(' | ')} · ${JSON.stringify(await page.evaluate(() => window.__qa.auth()))} · ${await page.locator('body').innerText()}`)
    const firstControls = await input.getAttribute('aria-controls')
    const secondControls = await secondInput.getAttribute('aria-controls')
    assert.ok(firstControls && secondControls && firstControls !== secondControls, `picker aria-controls ids collided at ${suffix(viewport)}`)
    await input.fill('桐')
    const options = page.getByRole('option')
    await page.waitForFunction(() => document.querySelectorAll('[role="option"]').length === 4)
    assert.equal(await options.count(), 4, `fragment search mismatch at ${suffix(viewport)}`)
    assert.equal(await options.filter({ hasText: 'player-0001-unique' }).count(), 1, `first duplicate id missing at ${suffix(viewport)}`)
    assert.equal(await options.filter({ hasText: 'player-0002-unique' }).count(), 1, `second duplicate id missing at ${suffix(viewport)}`)
    assert.equal(await options.filter({ hasText: '已禁用，不可选择' }).isDisabled(), true, `disabled state missing at ${suffix(viewport)}`)
    assert.equal(await options.filter({ hasText: '已删除，不可选择' }).isDisabled(), true, `deleted state missing at ${suffix(viewport)}`)
    await input.press('Enter')
    assert.equal(await page.locator('#selected-id').textContent(), 'player-0001-unique', `keyboard selection did not submit unique id at ${suffix(viewport)}`)
    await input.fill('不存在的玩家')
    assert.equal(await page.getByText('没有匹配的玩家账号').count(), 1, `empty state missing at ${suffix(viewport)}`)
    await input.fill('青禾')
    await input.press('Enter')
    assert.equal(await page.locator('#selected-id').textContent(), 'player-0005-other', `second selection failed at ${suffix(viewport)}`)
    const readsBeforeSwitch = await page.evaluate(() => window.__qa.reads())
    await page.locator('#switch-account').click()
    await page.waitForFunction(previous => window.__qa.reads() > previous, readsBeforeSwitch)
    assert.equal(await page.locator('#selected-id').textContent(), '', `account switch retained stale player id at ${suffix(viewport)}`)
    await input.fill('桐')
    const box = await page.locator('.admin-account-options').boundingBox()
    assert.ok(box && box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1,
      `candidate layer escaped viewport at ${suffix(viewport)}`)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `page overflow at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `account-picker-${suffix(viewport)}.png`), fullPage: true })
    report.push({ viewport, duplicateCandidates: 2, disabledStates: 2, accountSwitchReloaded: true })
  }
  const panelViewports = [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 844, height: 390 }]
  for (const viewport of panelViewports) {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__alternate_art_panel_closeout__`)
    await page.getByRole('heading', { name: '异画与联动画廊' }).waitFor()
    await page.getByRole('button', { name: '查看已登记异画' }).click()
    await page.locator('.registry-results').getByRole('button', { name: '编辑' }).click()
    const editor = page.locator('.art-editor')
    const secondProduct = editor.locator('input[type="checkbox"][value="site:qa-b"]')
    const secondRarity = editor.locator('input[type="checkbox"][value="rarity-sp"]')
    await secondProduct.check()
    await secondRarity.check()
    await editor.getByRole('button', { name: '保存并同步画廊' }).click()
    const confirm = page.locator('dialog.admin-risk-dialog')
    await confirm.getByRole('button', { name: '保存并同步画廊' }).click()
    await page.waitForFunction(() => window.__panelQa.calls.metadata.length === 1)
    const metadata = await page.evaluate(() => window.__panelQa.calls.metadata[0])
    assert.deepEqual(metadata, { alternateArtId: 'catalog-alt:S01-0101b', productIds: ['catalog:qa-a', 'site:qa-b'], rarityIds: ['rarity-ur', 'rarity-sp'] }, `built-in metadata request mismatch at ${suffix(viewport)}`)
    await page.getByRole('button', { name: '查看已登记异画' }).click()
    const registryText = await page.locator('.registry-results').innerText()
    assert.match(registryText, /同名产品、同名产品/, `same-name cross-source products were merged at ${suffix(viewport)}`)
    assert.match(registryText, /终极罕贵、特别罕贵/, `reused rarities were not re-read at ${suffix(viewport)}`)
    await page.getByRole('button', { name: '关闭' }).click()
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `admin panel overflow at ${suffix(viewport)}`)
    if (viewport.width === 1440) {
      await editor.getByRole('button', { name: '清空' }).click()
      await editor.getByLabel('条目类型').selectOption('gallery-card')
      await editor.getByLabel('编号').fill('COLLAB-PANEL-001')
      await editor.getByLabel('联动卡名称').fill('后台联动卡测试')
      await editor.getByLabel('联动卡卡效').fill('仅展示但不接入结算的联动卡效。')
      await editor.locator('input[type="checkbox"][value="catalog:qa-a"]').check()
      await editor.locator('input[type="checkbox"][value="site:qa-b"]').check()
      await editor.locator('input[type="checkbox"][value="rarity-ur"]').check()
      await editor.locator('input[type="checkbox"][value="rarity-sp"]').check()
      await editor.getByLabel('卡图素材').selectOption('media-gallery')
      await page.evaluate(() => window.__panelQa.failNextGalleryMetadata())
      await editor.getByRole('button', { name: '保存并同步画廊' }).click()
      const galleryConfirm = page.locator('dialog.admin-risk-dialog')
      await galleryConfirm.getByRole('button', { name: '保存并同步画廊' }).click()
      await page.waitForFunction(() => window.__panelQa.calls.saveArt.length === 1 && window.__panelQa.calls.metadata.length === 2)
      await galleryConfirm.getByText('注入：元数据暂时保存失败').waitFor()
      assert.equal(await editor.locator('input[type="checkbox"][value="catalog:qa-a"]').isChecked(), true, 'product selection was lost after metadata failure')
      assert.equal(await editor.locator('input[type="checkbox"][value="site:qa-b"]').isChecked(), true, 'second product selection was lost after metadata failure')
      assert.equal(await editor.locator('input[type="checkbox"][value="rarity-ur"]').isChecked(), true, 'rarity selection was lost after metadata failure')
      assert.equal(await editor.locator('input[type="checkbox"][value="rarity-sp"]').isChecked(), true, 'second rarity selection was lost after metadata failure')
      await galleryConfirm.getByRole('button', { name: '保存并同步画廊' }).click()
      await page.waitForFunction(() => window.__panelQa.calls.saveArt.length === 2 && window.__panelQa.calls.metadata.length === 3)
      const calls = await page.evaluate(() => window.__panelQa.calls)
      assert.equal(calls.saveArt[0].kind, 'gallery-card')
      assert.equal(calls.saveArt[0].baseCardId, '')
      assert.equal(calls.saveArt[0].effect, '仅展示但不接入结算的联动卡效。')
      assert.equal(calls.saveArt[0].id, undefined, 'first create unexpectedly had an id')
      assert.equal(calls.saveArt[1].id, 'gallery-new', 'retry did not update the stable id returned by the first save')
      assert.deepEqual(calls.metadata[1], { alternateArtId: 'gallery-new', productIds: ['catalog:qa-a', 'site:qa-b'], rarityIds: ['rarity-ur', 'rarity-sp'] })
      assert.deepEqual(calls.metadata[2], calls.metadata[1], 'retry lost the original product or rarity selections')
      assert.equal(calls.grant.length, 0, 'gallery card unexpectedly called grant API')
      assert.equal(calls.saveRarity.length, 0, 'reusing rarity unexpectedly created a new rarity definition')
      await page.screenshot({ path: path.join(output, 'admin-panel-native-flow-1440x900.png'), fullPage: true })
    }
    report.push({ viewport, adminPanelNativeFlow: true, builtInMetadataRoundTrip: true, galleryEffectSaved: viewport.width === 1440, metadataFailureRetryStableId: viewport.width === 1440 })
  }
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`http://127.0.0.1:${port}/__alternate_art_gallery_closeout__`)
  await page.getByRole('tab', { name: '画廊' }).click()
  const search = page.getByPlaceholder('卡名、编号或效果文字')
  await search.fill('联动画廊测试')
  const collaborationCard = page.locator('.archive-gallery-card').filter({ hasText: '联动画廊测试' })
  await collaborationCard.waitFor()
  await collaborationCard.press('Enter')
  const modal = page.locator('.archive-modal')
  await modal.waitFor()
  assert.equal(await modal.locator('[data-gallery-card-effect]').count(), 1, 'standalone collaboration effect was not rendered in the real detail component')
  assert.match(await modal.innerText(), /效果[\s\S]*独立撰写的联动卡效/, 'standalone collaboration effect text was not shown')
  await page.getByRole('button', { name: '关闭卡牌详情' }).click()
  await search.fill('QA产品甲')
  const historicalCard = page.locator('.archive-gallery-card').filter({ hasText: 'S01-0101b' })
  await historicalCard.waitFor()
  await historicalCard.click()
  const detail = page.locator('.archive-detail')
  assert.match(await detail.innerText(), /QA产品甲[\s\S]*QA产品乙/, 'historical built-in product overlay was not consumed by gallery detail')
  assert.match(await detail.innerText(), /终极罕贵[\s\S]*特别罕贵/, 'historical built-in rarity overlay was not consumed by gallery detail')
  await search.fill('ALT-QA-MULTI')
  const customCard = page.locator('.archive-gallery-card').filter({ hasText: 'ALT-QA-MULTI' })
  await customCard.waitFor()
  await customCard.click()
  assert.deepEqual(await detail.locator('.archive-decks p').allTextContents(), ['同名产品', '同名产品'], 'same-name product associations were merged or dropped')
  assert.match(await detail.innerText(), /终极罕贵 \/ 特别罕贵/, 'custom alternate-art rarities did not override the base rarity')
  await search.fill('旧字段产品')
  await page.locator('.archive-gallery-card').filter({ hasText: 'S01-0104a' }).click()
  assert.match(await detail.innerText(), /旧字段产品/, 'old single productName fallback was lost')
  assert.match(await detail.innerText(), /\bN\b/, 'missing rarity field incorrectly cleared the static archive rarity')
  await search.fill('S01-0117a')
  await page.locator('.archive-gallery-card').filter({ hasText: 'S01-0117a' }).click()
  assert.equal(await detail.locator('.archive-decks').count(), 0, 'explicit empty products failed to clear static archive products')
  assert.doesNotMatch(await detail.locator('.archive-tags').innerText(), /\bN\b/, 'explicit empty rarities failed to clear static archive rarity')
  await page.screenshot({ path: path.join(output, 'gallery-card-and-built-in-metadata-1440x900.png'), fullPage: true })
  report.push({ viewport: { width: 1440, height: 900 }, standaloneGalleryCard: true, displayOnlyEffect: true, builtInMetadataOverlay: true, customMetadataOverlay: true, oldFieldFallback: true, explicitClear: true, sameNameProductsRetained: true })
  assert.equal(pageErrors.length, 0, `page errors: ${pageErrors.join(' | ')}`)
  assert.equal(consoleProblems.filter(message => /duplicate keys|same key/i.test(message)).length, 0, `duplicate-key console warnings: ${consoleProblems.join(' | ')}`)
  const afterSourceHashes = sourceHashes()
  assert.deepEqual(afterSourceHashes, beforeSourceHashes, 'verification mutated its source inputs')
  finalPayload = { status: 'passed', report, pageErrors, consoleProblems, beforeSourceHashes, afterSourceHashes, apiMode: 'in-memory synthetic adminApi methods; no real HTTP persistence' }
} finally {
  await browser?.close()
  await server.close()
}
if (finalPayload) {
  finalPayload.lifecycle = { browserClosed: true, serverClosed: true }
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(finalPayload, null, 2))
  console.log(JSON.stringify({ status: 'passed', output, checks: finalPayload.report.length, lifecycle: finalPayload.lifecycle }, null, 2))
}
