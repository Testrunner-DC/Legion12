import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const front = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_ARTICLE_EMBEDS_OUTPUT
const cache = process.env.L12_ARTICLE_EMBEDS_CACHE
assert.ok(output && cache && process.env.L12_ARTICLE_EMBEDS_GUARDED === '1', 'Guard explicit D/E output and cache first')
fs.mkdirSync(output, { recursive: true })
const { chromium } = createRequire(import.meta.url)('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const watched = ['src/l12/site/articleBlocks.ts', 'src/l12/site/ArticleDocumentEditor.vue', 'src/l12/site/PublicDeckPicker.vue', 'src/l12/site/ArticleContentRenderer.vue', 'src/l12/site/ArticleBlockEditor.vue', 'scripts/verify-article-content-embeds.mjs']
const hashes = () => Object.fromEntries(watched.map(p => [p, crypto.createHash('sha256').update(fs.readFileSync(path.join(front, p))).digest('hex')]))
const catalog = JSON.parse(fs.readFileSync(path.join(front, 'public/data/l12/cards.s1.json'), 'utf8').replace(/^\uFEFF/, ''))
const card = catalog.find(c => c.id === 'S01-0001')
const master = catalog.find(c => c.cardType === 'master')
const coverIds = ['legion', 'tactic', 'artifact'].map(type => catalog.find(c => c.cardType === type).id)
const code = '23456789ABCD', nextCode = '23456789ABCF'
const body = blocks => JSON.stringify({ format: 'l12-blocks', version: 1, blocks })
const publicCurrent = reference => ({
  summary: { id: 'synthetic-public-' + reference, source: 'public', name: '资讯验收牌库', masterId: master.id,
    masterName: master.nameZh, faction: master.faction, author: '合成作者', publicCode: reference,
    publicationVersion: null, createdAt: '2026-10-19T00:00:00Z', updatedAt: '2026-10-19T00:00:00Z',
    counts: { main: 3, uncountedMain: 0, morale: 0, special: 0, bench: 0 }, legal: true, legalityReason: null,
    environment: { status: 'known', value: '2.5', reason: null }, views: 0, likes: 0, copies: 0,
    viewerLiked: false, canEdit: false, readToken: null, coverCardIds: coverIds },
  version: 1, deck: { name: '资讯验收牌库', masterId: master.id, cardIds: coverIds, moraleIds: [], specialIds: [],
    updatedAt: '2026-10-19T00:00:00Z', publicationId: null, publicationVersion: null },
  guide: { buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' }, matchups: [],
  contentRevision: 0, contentUpdatedAt: null, readToken: 'a'.repeat(64), catalogVersion: 'qa', policyVersion: 1,
})
const publicSummary = (reference, name, index = 0) => ({
  id: 'synthetic-summary-' + reference, source: 'public', name, masterId: master.id,
  masterName: master.nameZh, faction: master.faction, author: `合成作者 ${index + 1}`, publicCode: reference,
  publicationVersion: null, createdAt: '2026-10-19T00:00:00Z', updatedAt: '2026-10-19T00:00:00Z',
  counts: { main: 3, uncountedMain: 0, morale: 0, special: 0, bench: 0 }, legal: true, legalityReason: null,
  environment: { status: 'known', value: '2.5', reason: null }, views: 0, likes: 0, copies: 0,
  viewerLiked: false, canEdit: false, readToken: `${(index % 15 + 1).toString(16)}`.repeat(64), coverCardIds: coverIds,
})
const pickerReferences = [code, nextCode, ...Array.from({ length: 11 }, (_, index) => `23456789AB${(index + 16).toString(16).toUpperCase()}`)]
const pickerDecks = pickerReferences.map((reference, index) => publicSummary(reference,
  index === 0 ? '资讯验收牌库' : index === 1 ? '资讯替换牌库' : `公共牌库 ${index + 1}`, index))
const summaryPage = (url) => {
  const page = Number(url.searchParams.get('page') || '1'), pageSize = Number(url.searchParams.get('pageSize') || '30')
  const keyword = (url.searchParams.get('keyword') || '').trim().toLowerCase()
  const filtered = pickerDecks.filter(deck => !keyword || [deck.name, deck.author, deck.masterName].some(value => value.toLowerCase().includes(keyword)))
  return { items: filtered.slice((page - 1) * pageSize, page * pageSize), total: filtered.length, page, pageSize,
    generation: 'article-picker-qa', catalogVersion: 'qa', policyVersion: 1,
    sourceAvailability: { public: 'available', official: 'available' },
    facets: { sources: [], masters: [], factions: [], environments: [], cards: [], legal: filtered.length, illegal: 0 } }
}
const entry = `
import {createApp,h,ref} from 'vue';import {createRouter,createMemoryHistory} from 'vue-router'
import Editor from '/src/l12/site/ArticleDocumentEditor.vue';import Renderer from '/src/l12/site/ArticleContentRenderer.vue';import Legacy from '/src/l12/site/ArticleBlockEditor.vue';import '/src/style.css'
const body=ref(${JSON.stringify(body([{ id: 'text', type: 'paragraph', text: '资讯合成验收', align: 'left', marks: [] }]))}), readOnly=ref(false),legacy=ref(false)
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/',component:{render:()=>null}},{path:'/decks/:deckId',name:'public-deck-detail',component:{render:()=>null}}]});await router.push('/')
window.__articleQA={body,readOnly,legacy,router}
createApp({render:()=>h('main',{style:{maxWidth:'1000px',margin:'auto',padding:'10px'}},readOnly.value?h(Renderer,{body:body.value}):h(legacy.value?Legacy:Editor,{modelValue:body.value,'onUpdate:modelValue':v=>body.value=v}))}).use(router).mount('#app')
`
const server = await createServer({ root: front, configLoader: 'runner', cacheDir: cache, server: { host: '127.0.0.1', port: 0 },
  plugins: [{ name: 'article-embed-acceptance', resolveId(id) { if (id === '/__article__.js') return id },
    load(id) { if (id === '/__article__.js') return entry }, configureServer(v) { v.middlewares.use((req,res,next) => {
      if (new URL(req.url || '/', 'http://fixture').pathname !== '/__article__') return next()
      res.setHeader('Content-Type','text/html'); res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><body style="margin:0;background:#080d12;color:#eee"><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__article__.js"></script>')
    }) } }] })
const report = { sourceBefore: hashes(), cases: [], pageErrors: [], writes: [], limits: ['Synthetic loopback data only; browser viewports are not physical mobile devices.'] }
let browser
try {
  await server.listen(); browser = await chromium.launch({ channel: 'msedge', headless: true })
  for (const [width,height] of [[1440,1000],[390,844],[844,390]]) {
    const ctx = await browser.newContext({ viewport: { width,height }, hasTouch: width < 900,
      reducedMotion: width === 1440 ? 'no-preference' : 'reduce' })
    const reads = []
    let summaryRequests = 0
    await ctx.routeWebSocket(/.*/, s => s.close())
    await ctx.route('**/*', r => {
      const u = new URL(r.request().url())
      if (u.hostname !== '127.0.0.1') return r.abort()
      if (r.request().method() !== 'GET') { report.writes.push(u.pathname); return r.abort() }
      if (u.pathname === '/api/deck-library/summaries') {
        summaryRequests += 1
        if (summaryRequests === 1) return r.fulfill({ status: 400, json: { message: '合成目录首次失败' } })
        return r.fulfill({ json: summaryPage(u) })
      }
      const match = u.pathname.match(/^\/api\/public-decks\/([^/]+)\/current$/)
      if (match) { reads.push(match[1]); return match[1] === 'ZZZZZZZZZZZZ' ? r.fulfill({ status:404,json:{message:'公开牌库当前不可用'} }) : r.fulfill({ json:publicCurrent(match[1]) }) }
      if (u.pathname.startsWith('/api/')) return r.fulfill({ json: [] })
      if (u.pathname.startsWith('/card-assets/')) { const b=path.resolve('D:/L12-assets/published/current'), f=path.resolve(b,decodeURIComponent(u.pathname.slice(13))); assert.ok(f.startsWith(b+path.sep)); return fs.existsSync(f)?r.fulfill({path:f}):r.abort() }
      return r.continue()
    })
    const page = await ctx.newPage(); page.setDefaultTimeout(10000)
    page.on('pageerror', e => report.pageErrors.push(e.stack || e.message))
    await page.goto('http://127.0.0.1:'+server.httpServer.address().port+'/__article__')
    await page.locator('.editor-canvas p').filter({hasText:'资讯合成验收'}).waitFor()
    const beforeCardCancel = await page.evaluate(() => window.__articleQA.body.value)
    await page.getByRole('button',{name:'＋ 单卡',exact:true}).click()
    await page.getByPlaceholder('搜索卡名、编号或效果文字').fill(card.id)
    await page.locator('.single-card-result-card').filter({hasText:card.nameZh}).getByRole('button',{name:'选择',exact:true}).click()
    const newCardDialog = page.getByText('插入资讯单卡',{exact:true}).locator('..').locator('..')
    await page.getByLabel('单卡备注（可空）').fill('取消后不能写入')
    await newCardDialog.getByRole('button',{name:'取消',exact:true}).click()
    assert.equal(await page.evaluate(() => window.__articleQA.body.value),beforeCardCancel)
    await page.getByRole('button',{name:'＋ 单卡',exact:true}).click()
    await page.getByPlaceholder('搜索卡名、编号或效果文字').fill(card.id)
    const staleBody = body([{id:'stale-text',type:'paragraph',text:'选择期间正文已更新',align:'left',marks:[]}])
    await page.evaluate(value=>window.__articleQA.body.value=value,staleBody)
    await page.locator('.single-card-result-card').filter({hasText:card.nameZh}).getByRole('button',{name:'选择',exact:true}).click()
    assert.equal(await page.evaluate(() => window.__articleQA.body.value),staleBody)
    assert.equal(await page.locator('[data-article-embed="card"]').count(),0)
    await page.evaluate(value=>window.__articleQA.body.value=value,beforeCardCancel)
    await page.locator('.editor-canvas p').filter({hasText:'资讯合成验收'}).waitFor()
    await page.getByRole('button',{name:'＋ 单卡',exact:true}).click()
    await page.getByPlaceholder('搜索卡名、编号或效果文字').fill(card.id)
    await page.locator('.single-card-result-card').filter({hasText:card.nameZh}).getByRole('button',{name:'选择',exact:true}).click()
    await page.getByLabel('单卡备注（可空）').fill('关键回合再使用')
    await page.getByRole('button',{name:'插入单卡',exact:true}).click()
    await page.locator('[data-article-embed="card"]').waitFor()
    await page.getByRole('button',{name:'＋ 公共牌库',exact:true}).click()
    const insertDialog = page.getByRole('dialog',{name:'插入公共牌库',exact:true})
    await insertDialog.getByRole('button',{name:'重试',exact:true}).click()
    const pageOne = insertDialog.getByRole('button',{name:'选择公共牌库《资讯验收牌库》',exact:true})
    await pageOne.waitFor()
    assert.equal(await insertDialog.getByRole('button',{name:'下一页',exact:true}).isEnabled(),true)
    const pickerLayout = await insertDialog.evaluate(element => {
      const grid = element.querySelector('.public-deck-picker-grid'), option = element.querySelector('.public-deck-picker-option')
      return { horizontalOverflow: grid.scrollWidth > grid.clientWidth + 1,
        animationName: option ? getComputedStyle(option).animationName : '', animationDuration: option ? getComputedStyle(option).animationDuration : '' }
    })
    assert.equal(pickerLayout.horizontalOverflow,false)
    assert.ok(!pickerLayout.animationName || pickerLayout.animationName === 'none')
    assert.ok(!pickerLayout.animationDuration || pickerLayout.animationDuration === '0s')
    const beforeCancel = await page.evaluate(() => window.__articleQA.body.value)
    await insertDialog.getByRole('button',{name:'关闭',exact:true}).click()
    assert.equal(await page.evaluate(() => window.__articleQA.body.value),beforeCancel)
    await page.getByRole('button',{name:'＋ 公共牌库',exact:true}).click()
    const insertAgain = page.getByRole('dialog',{name:'插入公共牌库',exact:true})
    const firstOption = insertAgain.getByRole('button',{name:'选择公共牌库《资讯验收牌库》',exact:true})
    await firstOption.waitFor()
    if (width === 390) { await firstOption.focus(); await page.keyboard.press('Enter') }
    else await firstOption.click()
    const saved = await page.evaluate(() => JSON.parse(window.__articleQA.body.value))
    assert.equal(saved.blocks.filter(b=>b.type==='card')[0].cardId,card.id)
    assert.equal(saved.blocks.filter(b=>b.type==='card')[0].caption,'关键回合再使用')
    assert.equal(saved.blocks.filter(b=>b.type==='publicDeck')[0].reference,code)
    assert.ok(saved.blocks.some(b=>b.text==='资讯合成验收'))
    assert.equal(/readToken|ownerId|cardIds|from=news/.test(JSON.stringify(saved)),false)
    await page.getByRole('button',{name:'预览文章',exact:true}).click()
    await page.locator('.article-deck-embed .deck-cover').filter({hasText:'资讯验收牌库'}).waitFor()
    const cardButton = page.locator('.article-card-embed').first()
    assert.equal((await cardButton.textContent()).trim(),'')
    await page.getByText('关键回合再使用',{exact:true}).waitFor()
    if (width === 1440) {
      await cardButton.hover(); await page.waitForTimeout(220)
      assert.notEqual(await cardButton.evaluate(element => getComputedStyle(element).transform),'none')
      await page.mouse.move(0,0)
    } else {
      assert.equal(await cardButton.evaluate(element => getComputedStyle(element).transitionDuration),'0s')
    }
    if (width === 390) { await cardButton.focus(); await page.keyboard.press('Enter') }
    else await cardButton.click()
    await page.getByRole('dialog',{name:card.nameZh,exact:true}).waitFor()
    await page.getByRole('button',{name:'关闭卡牌详情',exact:true}).click()
    assert.equal(await page.locator('.catalog-detail-dialog').count(),0)
    const cover = await page.locator('.article-deck-embed .deck-cover').boundingBox()
    assert.ok(Math.abs(cover.width/cover.height-2.4)<.04)
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false)
    await page.screenshot({path:path.join(output,`article-${width}x${height}.png`),fullPage:true})
    await page.locator('.article-deck-embed').click()
    assert.equal(await page.evaluate(()=>window.__articleQA.router.currentRoute.value.path),'/decks/'+code)
    await page.getByRole('button',{name:'返回编辑',exact:true}).click()
    const beforeCaptionCancel = await page.evaluate(() => window.__articleQA.body.value)
    await page.locator('[data-article-embed="card"] [data-edit-embed]').click()
    const captionInput = page.getByLabel('单卡备注（可空）')
    assert.equal(await captionInput.inputValue(),'关键回合再使用')
    await captionInput.fill('取消的备注')
    await page.getByText('编辑资讯单卡',{exact:true}).locator('..').locator('..').getByRole('button',{name:'取消',exact:true}).click()
    assert.equal(await page.evaluate(() => window.__articleQA.body.value),beforeCaptionCancel)
    await page.locator('[data-article-embed="card"] [data-edit-embed]').click()
    await page.getByLabel('单卡备注（可空）').fill('更新后的备注')
    await page.getByRole('button',{name:'保存单卡',exact:true}).click()
    assert.equal((await page.evaluate(() => JSON.parse(window.__articleQA.body.value))).blocks.find(block=>block.type==='card').caption,'更新后的备注')
    await page.getByRole('button',{name:'↶ 撤销',exact:true}).click()
    assert.equal((await page.evaluate(() => JSON.parse(window.__articleQA.body.value))).blocks.find(block=>block.type==='card').caption,'关键回合再使用')
    await page.getByRole('button',{name:'↷ 重做',exact:true}).click()
    assert.equal((await page.evaluate(() => JSON.parse(window.__articleQA.body.value))).blocks.find(block=>block.type==='card').caption,'更新后的备注')
    await page.locator('[data-article-embed="card"] [data-edit-embed]').click()
    await page.getByLabel('单卡备注（可空）').fill('')
    await page.getByRole('button',{name:'保存单卡',exact:true}).click()
    assert.equal(Object.hasOwn((await page.evaluate(() => JSON.parse(window.__articleQA.body.value))).blocks.find(block=>block.type==='card'),'caption'),false)
    await page.getByRole('button',{name:'↶ 撤销',exact:true}).click()
    assert.equal((await page.evaluate(() => JSON.parse(window.__articleQA.body.value))).blocks.find(block=>block.type==='card').caption,'更新后的备注')
    await page.locator('[data-article-embed="publicDeck"] [data-edit-embed]').click()
    const replaceDialog = page.getByRole('dialog',{name:'更换公共牌库',exact:true})
    await replaceDialog.getByText('当前引用',{exact:true}).waitFor()
    await replaceDialog.getByLabel('搜索公共牌库').fill('资讯替换牌库')
    await replaceDialog.getByRole('button',{name:'搜索',exact:true}).click()
    await replaceDialog.getByRole('button',{name:'选择公共牌库《资讯替换牌库》',exact:true}).click()
    assert.equal(await page.locator('[data-article-embed="publicDeck"]').getAttribute('data-embed-value'),nextCode)
    // Preview flushes the existing merged input-history group. Delete/undo below
    // must not assume two structural clicks inside the debounce are two groups.
    await page.getByRole('button',{name:'预览文章',exact:true}).click()
    assert.equal(await page.locator('.article-deck-embed').getAttribute('href'),'/decks/'+nextCode)
    await page.getByRole('button',{name:'返回编辑',exact:true}).click()
    await page.locator('[data-article-embed="card"] [data-remove-embed]').click()
    assert.equal(await page.locator('[data-article-embed="card"]').count(),0)
    await page.getByRole('button',{name:'↶ 撤销',exact:true}).click()
    await page.locator('[data-article-embed="card"]').waitFor()
    await page.evaluate(()=>window.__articleQA.legacy.value=true)
    await page.locator('.block-editor textarea').first().fill('资讯合成验收：旧编辑器往返')
    const legacyBody=await page.evaluate(()=>JSON.parse(window.__articleQA.body.value))
    assert.equal(legacyBody.blocks.filter(b=>b.type==='card')[0].cardId,card.id)
    assert.equal(legacyBody.blocks.filter(b=>b.type==='card')[0].caption,'更新后的备注')
    assert.equal(legacyBody.blocks.filter(b=>b.type==='publicDeck')[0].reference,nextCode)
    assert.ok(legacyBody.blocks.some(b=>b.text==='资讯合成验收：旧编辑器往返'))
    const countBefore = reads.length
    const references = Array.from({length:13},(_,i)=>'23456789ABC'+'23456789ABCDE'[i])
    await page.evaluate(value=>{window.__articleQA.body.value=value;window.__articleQA.readOnly.value=true},body([...references, references[0]].map((reference,i)=>({id:'public-'+i,type:'publicDeck',reference}))))
    await page.getByText('单篇资讯最多读取 12 个公共牌库',{exact:true}).waitFor()
    assert.equal(reads.length-countBefore,12)
    assert.equal(new Set(reads.slice(countBefore)).size,12)
    await page.evaluate(value=>window.__articleQA.body.value=value,body([{id:'gone',type:'publicDeck',reference:'ZZZZZZZZZZZZ'}]))
    await page.locator('.article-deck-embed').filter({hasText:'没有找到这个公开牌库或版本。'}).waitFor()
    const galleryBlocks = [], galleryCounts = [1,3,4,6]
    galleryCounts.forEach((count, groupIndex) => {
      for (let index=0; index<count; index++) galleryBlocks.push({id:`gallery-${groupIndex}-${index}`,type:'card',cardId:card.id,
        ...(groupIndex===0 && index===0 ? {caption:'单张备注'} : {})})
      if (groupIndex<galleryCounts.length-1) galleryBlocks.push({id:`separator-${groupIndex}`,type:'paragraph',text:`分隔正文 ${groupIndex+1}`,align:'left',marks:[]})
    })
    await page.evaluate(value=>window.__articleQA.body.value=value,body(galleryBlocks))
    const galleryRows = page.locator('.article-card-row')
    await galleryRows.nth(3).waitFor()
    assert.deepEqual(await galleryRows.evaluateAll(rows=>rows.map(row=>row.querySelectorAll('.article-card-figure').length)),galleryCounts)
    assert.equal(await page.locator('.article-card-caption').count(),1)
    assert.equal(await page.getByText('单张备注',{exact:true}).count(),1)
      const expectedColumns = 3
      for (let index=0; index<galleryCounts.length; index++) {
        const galleryBox = await galleryRows.nth(index).boundingBox()
        assert.ok(galleryBox)
        const positions = await galleryRows.nth(index).locator('.article-card-figure').evaluateAll(cards=>cards.map(card=>{
          const rect=card.getBoundingClientRect(); return {x:rect.x,y:Math.round(rect.y),right:rect.right}
        }))
      assert.equal(new Set(positions.map(position=>position.y)).size,Math.ceil(galleryCounts[index]/expectedColumns))
        const rowYs=[...new Set(positions.map(position=>position.y))]
        for (const rowY of rowYs) {
          const row=positions.filter(position=>position.y===rowY).sort((left,right)=>left.x-right.x)
          assert.ok(row.length<=expectedColumns)
          const rowCenter=(row[0].x+row.at(-1).right)/2
          const galleryCenter=galleryBox.x+galleryBox.width/2
          assert.ok(Math.abs(rowCenter-galleryCenter)<=2,`gallery ${index+1} row is not centered`)
        }
    }
    assert.deepEqual(await page.locator('.article-content').evaluate(element=>Array.from(element.children).slice(0,7).map(child=>child.classList.contains('article-card-row')?'cards':child.tagName.toLowerCase())),
      ['cards','p','cards','p','cards','p','cards'])
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false)
    await page.screenshot({path:path.join(output,`article-cards-${width}x${height}.png`),fullPage:true})
    report.cases.push({width,height,pickerRetry:true,cancelLeavesBodyUnchanged:true,directSelection:true,
      keyboardSelection:width===390,searchReplace:true,noPickerHorizontalOverflow:true,noPickerAnimation:true,
      insert:true,staleCardSelectionFenced:true,cardCaptionInsertEditClearCancelUndoRedo:true,cardImageOnly:true,cardGalleryCounts:galleryCounts,
        cardGalleryColumns:expectedColumns,cardRowsCentered:true,hoverLift:width===1440,reducedMotion:width!==1440,
      serializedReferenceOnly:true,preview:true,cardDetails:true,coverRatio:cover.width/cover.height,
      existingRoute:true,replace:true,deleteAndUndo:true,legacyEditorRoundTrip:true,deduplicatedBoundedReads:12,unavailableVisible:true})
    await ctx.close()
  }
  assert.deepEqual(report.pageErrors,[]); assert.deepEqual(report.writes,[])
  console.log(JSON.stringify({cases:report.cases.length,pageErrors:0,writes:0}))
} catch(error) { report.fatal=error.stack||String(error); throw error }
finally { report.sourceAfter=hashes(); report.sourcesStable=JSON.stringify(report.sourceBefore)===JSON.stringify(report.sourceAfter); fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2)); await browser?.close(); await server.close(); if(!report.sourcesStable)process.exitCode=1 }
