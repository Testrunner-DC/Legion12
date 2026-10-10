import assert from 'node:assert/strict'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5174/__l12_battle_preview__?special=1&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1&hand=15'
const output = process.env.L12_BATTLE_QA_OUT || path.join(os.tmpdir(), 'l12-battle-responsive')
const viewports = [
  { width: 3440, height: 1440 },
  { width: 2560, height: 1440 },
  { width: 1920, height: 1080 },
  { width: 1920, height: 1200 },
  { width: 1600, height: 900 },
  { width: 1680, height: 1050 },
  { width: 1536, height: 864 },
  { width: 1440, height: 900 },
  { width: 1366, height: 768 },
  { width: 1280, height: 720 },
  { width: 1280, height: 800 },
  { width: 2560, height: 1080 },
]
const desktopStates = [
  { name:'target', query:'field=full&board-target=1', selector:'.board-target-controls' },
  { name:'support', query:'field=full&support=1', selector:'.combat-resolution-panel' },
  { name:'defense', query:'field=full&defense=1', selector:'.combat-resolution-panel' },
  { name:'gm', query:'field=full&gm=1', selector:'.gm-panel', separateFromStage:true },
  { name:'settlement', query:'field=full&game-over=win', selector:'.game-over' },
  { name:'candidate-minimize', query:'field=full&card-choice=1&choice-count=12', selector:'.prompt-panel', minimize:true },
  { name:'card-detail', query:'field=full&modalFixture=1', selector:'.card-inspector', openCard:true },
]

function stateUrl(query) {
  const url = new URL(target)
  for (const [name,value] of new URLSearchParams(query)) url.searchParams.set(name,value)
  return url.toString()
}

fs.mkdirSync(output, { recursive: true })
let browser
try {
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const pageErrors = []
  page.on('pageerror', error => pageErrors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    return ['127.0.0.1', 'localhost'].includes(url.hostname) ? route.continue() : route.abort()
  })

  const results = []
  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    await page.goto(target, { waitUntil: 'networkidle' })
    await page.locator('.side-my .formation-slot').first().waitFor()
    await page.locator('.my-player-clock').waitFor()
    await page.waitForTimeout(250)
    const result = await page.evaluate(() => {
      const box = element => {
        if (!element) return null
        const rect = element.getBoundingClientRect()
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height, cx: rect.left + rect.width / 2 }
      }
      const intersects = (left, right) => Boolean(left && right && left.left < right.right - .5 && left.right > right.left + .5 && left.top < right.bottom - .5 && left.bottom > right.top + .5)
      const union = elements => {
        const boxes = elements.map(box).filter(Boolean)
        return boxes.length ? {
          left: Math.min(...boxes.map(item => item.left)),
          right: Math.max(...boxes.map(item => item.right)),
          top: Math.min(...boxes.map(item => item.top)),
          bottom: Math.max(...boxes.map(item => item.bottom)),
          cx: (Math.min(...boxes.map(item => item.left)) + Math.max(...boxes.map(item => item.right))) / 2,
        } : null
      }
      const myHandCards = [...document.querySelectorAll('.board-center > .l12-hand:last-child .hand-card-wrap')]
      const enemyHandCards = [...document.querySelectorAll('.opponent-hand .card-back')]
      const myHand = union(myHandCards)
      const enemyHand = union(enemyHandCards)
      const myHandContainer = box(document.querySelector('.board-center > .l12-hand:last-child'))
      const myExtra = box(document.querySelector('.my-half .special-lane'))
      const enemyExtra = box(document.querySelector('.opponent-half .special-lane'))
      const myClock = box(document.querySelector('.my-player-clock'))
      const enemyClock = box(document.querySelector('.opponent-player-clock'))
      const fieldSlots = [...document.querySelectorAll('.formation-slot')]
      const fieldCards = [...document.querySelectorAll('.formation-slot .card-tile')]
      const keywordRows = [...document.querySelectorAll('.opponent-half .card-keyword-row')]
      const myKeywordRows = [...document.querySelectorAll('.my-half .card-keyword-row')]
      const keywords = [...document.querySelectorAll('.formation-slot .card-keyword')]
      const statusIcons = [...document.querySelectorAll('.opponent-half .card-status-icon')]
      const statusCard = document.querySelector('.opponent-half .formation-slot .card-tile')
      const cost = statusCard?.querySelector('.card-cost')
      const centerFieldSlot = document.querySelector('.my-half .formation-slot:nth-child(2)')
      const middleMyCard = myHandCards[Math.floor(myHandCards.length / 2)]?.querySelector('.card-tile')
      const middleEnemyCard = enemyHandCards[Math.floor(enemyHandCards.length / 2)]
      const pileCard = document.querySelector('.pile-card')
      const masterCard = document.querySelector('.my-half .mini-master')
      const relicCard = document.querySelector('.my-half .relic-zone .card-tile')
      const fieldCard = document.querySelector('.my-half .formation-slot .card-tile')
      const felt = box(document.querySelector('.felt-board'))
      const phase = box(document.querySelector('.phase-column'))
      const playerPanel = box(document.querySelector('.player-panel'))
      const recordLog = box(document.querySelector('.record-log'))
      const stage = box(document.querySelector('.board-stage'))
      const zoneSelectors = ['.commander-zone','.battle-zone','.mat-piles','.resource-zone']
      const zoneGeometry = [...document.querySelectorAll('.battlefield-half')].map(mat => {
        const matBox=box(mat); const zones=zoneSelectors.map(selector=>({selector,rect:box(mat.querySelector(selector))})).filter(item=>item.rect)
        const overlap=[]
        for(let left=0;left<zones.length;left++)for(let right=left+1;right<zones.length;right++)if(intersects(zones[left].rect,zones[right].rect))overlap.push(`${zones[left].selector}/${zones[right].selector}`)
        const used=zones.length?Math.max(...zones.map(item=>item.rect.right))-Math.min(...zones.map(item=>item.rect.left)):0
        return {mat:matBox,zones,overlap,usedRatio:matBox?used/matBox.width:0}
      })
      const master=box(masterCard), relic=box(relicCard), field=box(fieldCard), pile=box(pileCard), hand=box(middleMyCard)
      return {
        stage,
        viewport: { width: innerWidth, height: innerHeight },
        fieldSquareDelta: Math.max(...fieldSlots.map(element => { const rect = element.getBoundingClientRect(); return Math.abs(rect.width - rect.height) })),
        fieldCardsInside: fieldCards.every(element => { const card = element.getBoundingClientRect(); const slot = element.closest('.formation-slot').getBoundingClientRect(); return card.left >= slot.left - 1 && card.right <= slot.right + 1 && card.top >= slot.top - 1 && card.bottom <= slot.bottom + 1 }),
        phaseAlignment: { top: Math.abs(phase.top - felt.top), bottom: Math.abs(phase.bottom - felt.bottom) },
        keywordRows: keywordRows.map(row => row.children.length),
        myKeywordRows: myKeywordRows.map(row => row.children.length),
        keywordOverflow: keywords.some(element => element.scrollWidth > element.clientWidth),
        keywordMeasurements: keywords.map(element => ({ text: element.textContent?.trim(), clientWidth: element.clientWidth, scrollWidth: element.scrollWidth })),
        statusCount: statusIcons.length,
        statusInside: statusIcons.every(element => { const icon = element.getBoundingClientRect(); const card = statusCard.getBoundingClientRect(); return icon.left >= card.left - 1 && icon.right <= card.right + 1 && icon.top >= card.top - 1 && icon.bottom <= card.bottom + 1 }),
        statusCostGap: statusIcons.length && cost ? statusIcons[0].getBoundingClientRect().top - cost.getBoundingClientRect().bottom : null,
        handCount: { mine: myHandCards.length, enemy: enemyHandCards.length },
        handCenterDelta: Math.abs(myHandContainer.cx - box(centerFieldSlot).cx),
        handVisualFanDrift: Math.abs(myHand.cx - myHandContainer.cx),
        handIntrusions: {
          myExtra: intersects(myHand, myExtra), myClock: intersects(myHand, myClock),
          enemyExtra: intersects(enemyHand, enemyExtra), enemyClock: intersects(enemyHand, enemyClock),
        },
        cardRatios: master ? { field:field?.width/master.width, relic:relic?.width/master.width, pile:pile?.width/master.width, hand:hand?.width/master.width, enemyHand:box(middleEnemyCard)?.width/master.width } : null,
        zoneGeometry,
        stageUtilization:{width:stage.width/innerWidth,height:stage.height/innerHeight,area:(stage.width*stage.height)/(innerWidth*innerHeight)},
        playerPanelHeight: playerPanel.height,
        recordLogHeight: recordLog.height,
        playerSummaryOverflow: [...document.querySelectorAll('.player-summary-primary,.player-summary-meta')].some(element => element.scrollWidth > element.clientWidth),
        playerSummaryTruncation: [...document.querySelectorAll('.player-summary-meta>.rank-badge>span,.player-summary-meta>.title-badge>span')].some(element => element.scrollWidth > element.clientWidth),
        playerSummaryMeasurements: [...document.querySelectorAll('.player-summary-meta>.rank-badge>span,.player-summary-meta>.title-badge>span')].map(element => ({ text: element.textContent?.trim(), clientWidth: element.clientWidth, scrollWidth: element.scrollWidth })),
        documentOverflow: { x: document.documentElement.scrollWidth > innerWidth + 1, y: document.documentElement.scrollHeight > innerHeight + 1 },
      }
    })
    await page.screenshot({ path: path.join(output, `battle-${viewport.width}x${viewport.height}.png`), fullPage: false })

    assert.equal(result.handCount.mine, 15, `my hand fixture changed at ${viewport.width}x${viewport.height}`)
    assert.equal(result.handCount.enemy, 15, `enemy hand fixture changed at ${viewport.width}x${viewport.height}`)
    assert.deepEqual(result.keywordRows, [3, 3], `opponent keyword wrapping changed at ${viewport.width}x${viewport.height}`)
    assert.deepEqual(result.myKeywordRows, [2], `my keyword comparison changed at ${viewport.width}x${viewport.height}`)
    assert.equal(result.keywordOverflow, false, `keyword text overflows at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.keywordMeasurements)}`)
    assert.equal(result.statusCount, 4, `status icon fixture changed at ${viewport.width}x${viewport.height}`)
    assert.equal(result.statusInside, true, `status icons leave the card at ${viewport.width}x${viewport.height}`)
    assert(result.statusCostGap >= 2, `status icons touch cost at ${viewport.width}x${viewport.height}: gap ${result.statusCostGap}`)
    assert(result.fieldSquareDelta <= 1, `battlefield cells are not square at ${viewport.width}x${viewport.height}`)
    assert.equal(result.fieldCardsInside, true, `field card leaves its cell at ${viewport.width}x${viewport.height}`)
    assert(result.phaseAlignment.top <= 1.5 && result.phaseAlignment.bottom <= 1.5, `phase track is not aligned with the felt board at ${viewport.width}x${viewport.height}`)
    assert(result.handCenterDelta <= 1.5, `hands are not centered to the middle field column at ${viewport.width}x${viewport.height}: delta ${result.handCenterDelta}`)
    assert(result.handVisualFanDrift <= 6, `visible hand fan drifts too far from its center axis at ${viewport.width}x${viewport.height}`)
    assert(Object.values(result.handIntrusions).every(value => value === false), `hand intrudes into an extra zone or clock at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.handIntrusions)}`)
    assert(result.cardRatios&&result.cardRatios.field>=.72&&result.cardRatios.field<=1.2,`field/master proportion is unreasonable at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.cardRatios)}`)
    assert(result.cardRatios.relic>=.65&&result.cardRatios.relic<=1.05,`relic/master proportion is unreasonable at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.cardRatios)}`)
    assert(result.cardRatios.pile>=.5&&result.cardRatios.pile<=.9,`pile/master proportion is unreasonable at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.cardRatios)}`)
    assert(result.cardRatios.hand>=.5&&result.cardRatios.hand<=1.05&&result.cardRatios.enemyHand>=.5&&result.cardRatios.enemyHand<=1.05,`hand/master proportion is unreasonable at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.cardRatios)}`)
    assert(result.zoneGeometry.every(item=>item.overlap.length===0),`battle zones intrude at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.zoneGeometry)}`)
    assert(result.zoneGeometry.every(item=>item.usedRatio>=.7),`battle zones leave excessive horizontal voids at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.zoneGeometry)}`)
    // Ultrawide screens necessarily letterbox a stable stage ratio, but must
    // still consume almost all usable height and a meaningful screen area.
    assert(result.stageUtilization.height>=.8&&result.stageUtilization.width>=.58&&result.stageUtilization.area>=.5,`stage underuses viewport at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.stageUtilization)}`)
    assert.equal(result.playerSummaryOverflow, false, `two-line player summary overflows at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.playerSummaryMeasurements)}`)
    assert.equal(result.playerSummaryTruncation, false, `rank and title badges truncate at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.playerSummaryMeasurements)}`)
    assert(result.recordLogHeight > result.playerPanelHeight * 2, `player summary did not yield enough record space at ${viewport.width}x${viewport.height}`)
    assert.equal(result.documentOverflow.x, false, `document overflows horizontally at ${viewport.width}x${viewport.height}`)
    assert.equal(result.documentOverflow.y, false, `document overflows vertically at ${viewport.width}x${viewport.height}`)
    results.push({ ...viewport, ...result })
  }
  for(const key of ['field','relic','pile','hand','enemyHand']){
    const values=results.map(item=>item.cardRatios[key])
    assert(Math.max(...values)-Math.min(...values)<=.12,`${key}/master proportion jumps across desktop ratios: ${JSON.stringify(values)}`)
  }
  const stateResults=[]
  for(const viewport of viewports){
    await page.setViewportSize(viewport)
    for(const state of desktopStates){
      await page.goto(stateUrl(state.query),{waitUntil:'domcontentloaded'})
      await page.locator('.side-my .formation-slot').first().waitFor()
      if(state.openCard) await page.locator('.my-half .formation-slot .card-tile').first().click()
      let panel=page.locator(state.selector).first()
      await panel.waitFor()
      if(state.minimize){
        await panel.locator('.prompt-minimize').click()
        const restore=page.locator('.prompt-minimized-bar button'); await restore.waitFor()
        const restoreInside=await restore.evaluate(element=>{const r=element.getBoundingClientRect();return r.left>=-1&&r.top>=-1&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1})
        assert.equal(restoreInside,true,`${state.name} restore leaves viewport at ${viewport.width}x${viewport.height}`)
        await restore.click(); panel=page.locator(state.selector).first(); await panel.waitFor()
      }
      const geometry=await panel.evaluate((element,separateFromStage)=>{
        const raw=element.getBoundingClientRect(), rect={left:raw.left,top:raw.top,right:raw.right,bottom:raw.bottom,width:raw.width,height:raw.height}
        const visible=node=>{const r=node.getBoundingClientRect(),s=getComputedStyle(node);return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden'}
        const clipped=node=>{const source=node.getBoundingClientRect(),value={left:source.left,top:source.top,right:source.right,bottom:source.bottom};for(let ancestor=node.parentElement;ancestor&&ancestor!==document.documentElement;ancestor=ancestor.parentElement){const s=getComputedStyle(ancestor);if(!/(auto|scroll|hidden|clip)/.test(`${s.overflow} ${s.overflowX} ${s.overflowY}`))continue;const clip=ancestor.getBoundingClientRect();value.left=Math.max(value.left,clip.left);value.top=Math.max(value.top,clip.top);value.right=Math.min(value.right,clip.right);value.bottom=Math.min(value.bottom,clip.bottom)}return value}
        const controls=[...element.querySelectorAll('button,a[href],input,select,textarea')].filter(visible).map(node=>({text:(node.getAttribute('aria-label')||node.textContent||'').trim().slice(0,36),rect:clipped(node)})).filter(control=>control.rect.right>control.rect.left&&control.rect.bottom>control.rect.top)
        const stage=document.querySelector('.board-stage')?.getBoundingClientRect(), intersects=(a,b)=>Boolean(a&&b&&a.left<b.right-.5&&a.right>b.left+.5&&a.top<b.bottom-.5&&a.bottom>b.top+.5)
        return {rect,controls,inside:rect.left>=-1&&rect.top>=-1&&rect.right<=innerWidth+1&&rect.bottom<=innerHeight+1,visibleShare:Math.max(0,Math.min(rect.right,innerWidth)-Math.max(rect.left,0))*Math.max(0,Math.min(rect.bottom,innerHeight)-Math.max(rect.top,0))/Math.max(1,rect.width*rect.height),horizontalOverflow:element.scrollWidth>element.clientWidth+1,stageOverlap:separateFromStage?intersects(rect,stage):false,documentOverflow:document.documentElement.scrollWidth>innerWidth+1||document.documentElement.scrollHeight>innerHeight+1}
      },state.separateFromStage??false)
      assert.equal(geometry.inside,true,`${state.name} leaves viewport at ${viewport.width}x${viewport.height}: ${JSON.stringify(geometry)}`)
      assert.ok(geometry.visibleShare>=.98,`${state.name} is clipped at ${viewport.width}x${viewport.height}: ${JSON.stringify(geometry)}`)
      assert.equal(geometry.horizontalOverflow,false,`${state.name} horizontally overflows at ${viewport.width}x${viewport.height}`)
      assert.equal(geometry.controls.every(control=>control.rect.left>=-1&&control.rect.top>=-1&&control.rect.right<=viewport.width+1&&control.rect.bottom<=viewport.height+1),true,`${state.name} has unreachable controls at ${viewport.width}x${viewport.height}: ${JSON.stringify(geometry.controls)}`)
      assert.equal(geometry.stageOverlap,false,`${state.name} intrudes into reserved stage at ${viewport.width}x${viewport.height}`)
      assert.equal(geometry.documentOverflow,false,`${state.name} causes document overflow at ${viewport.width}x${viewport.height}`)
      await page.screenshot({path:path.join(output,`battle-${state.name}-${viewport.width}x${viewport.height}.png`),fullPage:false})
      stateResults.push({viewport:`${viewport.width}x${viewport.height}`,state:state.name,...geometry})
    }
  }
  if (pageErrors.length) throw new Error(`Page errors: ${pageErrors.join(' | ')}`)
  console.log(JSON.stringify({ output, results, stateResults }, null, 2))
} finally {
  await browser?.close()
}
