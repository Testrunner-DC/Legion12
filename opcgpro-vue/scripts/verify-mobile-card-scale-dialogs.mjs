import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5190/__l12_battle_preview__'
const output = process.env.L12_B3_OUT || path.resolve('..', 'artifacts', 'batch-mobile-card-scale-dialogs-b3')
const fixedViewports = [
  [568, 320], [640, 360], [667, 375], [720, 360], [740, 360], [780, 360], [812, 375], [844, 390],
  [852, 393], [896, 414], [915, 412], [932, 430], [956, 440], [1024, 600], [1024, 768],
].map(([width, height]) => ({ width, height }))
const dialogViewports = [[667, 375], [844, 390], [932, 430], [1024, 768]].map(([width, height]) => ({ width, height }))
const quick = process.env.L12_B3_QUICK === '1'
const safeOnly = process.env.L12_B3_SAFE_ONLY === '1'
const dialogOnly = process.env.L12_B3_DIALOG_ONLY === '1'
const scanOnly = process.env.L12_B3_SCAN_ONLY?.trim() ?? ''
const activeFixed = safeOnly || dialogOnly || scanOnly ? [] : quick ? fixedViewports.slice(0, 1) : fixedViewports
const activeDialogs = safeOnly || scanOnly ? [] : quick ? dialogViewports.slice(0, 1) : dialogViewports
const safeAreaProfiles = [
  { name:'left-59-bottom-21', viewport:{ width:667,height:375 }, insets:{ top:0,right:0,bottom:21,left:59 } },
  { name:'right-59-bottom-21', viewport:{ width:844,height:390 }, insets:{ top:0,right:59,bottom:21,left:0 } },
  { name:'both-44-bottom-21', viewport:{ width:932,height:430 }, insets:{ top:0,right:44,bottom:21,left:44 } },
]
const safeAreaScenarios = [
  ['full','field=full&markers=1&piles=40&hand=5&rankedClock=1&gm=1','full'],
  ['hand-20','field=full&markers=5&piles=40&hand=20&rankedClock=1','hand-20'],
  ['markers-5','field=full&markers=5&piles=40&hand=5&rankedClock=1','markers-5'],
  ['card-detail','field=full&markers=5&piles=40&hand=10&rankedClock=1','inspector-open'],
  ['card-choice-12','field=full&markers=5&piles=40&hand=10&rankedClock=1&card-choice=1&choice-count=12','card-choice-12'],
  ['morale-payment','field=full&markers=5&piles=40&hand=10&rankedClock=1&morale-payment=1','morale-payment'],
]
const zeroInsets = { top:0,right:0,bottom:0,left:0 }
const interactiveSelector = 'button,a[href],input,select,textarea,summary,[role="button"],[tabindex]:not([tabindex="-1"])'
const criticalSelectors = [
  '.board-stage', '.left-rail', '.felt-board', '.right-rail',
  '.battle-route-controls', '.battle-route-controls button', '.gm-open', '.gm-panel',
  '.mobile-card-inspector-handle-global', '.mobile-card-inspector',
  '.mobile-extra-zone', '.session-disaster-panel', '.current-disaster-panel', '.session-disaster-strip button',
  '.phase-column', '.battlefield-half .mobile-hand-count', '.battlefield-half .mini-master',
  '.battlefield-half .relic-zone .card-tile', '.battlefield-half .formation-slot',
  '.battlefield-half .mat-piles .pile-card', '.battlefield-half .resource-zone',
  '.mobile-record-trigger', '.mobile-timed-clocks', '.action-panel button', '.card-context-actions',
  '.board-center > .l12-hand:last-child', '.combat-versus', '.resource-payment-controls', '.prompt-minimized-bar',
]
const overlaySelectors = [
  '.prompt-panel', '.waiting-panel',
  '.mobile-record-overlay', '.mobile-morale-overlay', '.mobile-card-inspector', '.game-over',
  '.master-dialog', '.graveyard-window', '.faction-effect-dialog', '.zone-card-movement',
  '.battle-dialog', '.l12-settings-modal', '.gm-panel',
]
const states = [
  ['empty', 'field=empty&markers=0&piles=0&hand=1'],
  ['full', 'field=full&markers=1&piles=1&hand=5'],
  ['tapped', 'field=tapped&markers=5&piles=40&hand=10'],
  ['markers-0', 'field=full&markers=0&piles=1&hand=5'],
  ['markers-1', 'field=full&markers=1&piles=1&hand=5'],
  ['markers-5', 'field=full&markers=5&piles=1&hand=5'],
  ['piles-0', 'field=full&markers=1&piles=0&hand=5'],
  ['piles-1', 'field=full&markers=1&piles=1&hand=5'],
  ['piles-40', 'field=full&markers=1&piles=40&hand=5'],
  ...[1, 5, 10, 15, 20].map(count => [`hand-${count}`, `field=full&markers=5&piles=40&hand=${count}`]),
  ['clock-off', 'field=full&markers=5&piles=40&hand=10'],
  ['clock-on', 'field=full&markers=5&piles=40&hand=10&rankedClock=1'],
  ['inspector-closed', 'field=full&markers=5&piles=40&hand=10'],
  ['inspector-open', 'field=full&markers=5&piles=40&hand=10'],
  ['support', 'field=full&markers=5&piles=40&hand=10&support=1'],
  ['defense', 'field=full&markers=5&piles=40&hand=10&defense=1'],
  ['board-target', 'field=full&markers=5&piles=40&hand=10&board-target=1'],
  ['board-slot', 'field=empty&markers=5&piles=40&hand=10&board-slot=1'],
  ['status-indicators', 'field=full&markers=5&piles=40&hand=10&fieldIndicators=1'],
  ['action-dock', 'field=full&markers=5&piles=40&hand=10&action-fixture=1'],
  ['morale-overview', 'field=full&markers=5&piles=40&hand=10'],
  ['morale-payment', 'field=full&markers=5&piles=40&hand=10&morale-payment=1'],
  ['morale-payment-minimized', 'field=full&markers=5&piles=40&hand=10&morale-payment=1'],
  ['runes-0-view', 'field=full&markers=0&piles=40&hand=10&runes=0'],
  ['runes-1-view', 'field=full&markers=0&piles=40&hand=10&runes=1'],
  ['runes-5-view', 'field=full&markers=0&piles=40&hand=10&runes=5'],
  ['runes-5-payment', 'field=full&markers=0&piles=40&hand=10&runes=5&morale-payment=1&rune-usable=1'],
  ['disaster-current', 'field=full&markers=5&piles=40&hand=10&disaster-chain=1'],
  ['disaster-animation', 'field=full&markers=5&piles=40&hand=10&disaster-chain=1'],
  ['disaster-waiting', 'field=full&markers=5&piles=40&hand=10&disaster-chain=1'],
  ['disaster-interrupted', 'field=full&markers=5&piles=40&hand=10&disaster-chain=1'],
  ['settlement-win', 'field=full&markers=5&piles=40&hand=10&game-over=win'],
  ['settlement-loss', 'field=full&markers=5&piles=40&hand=10&game-over=loss'],
]
const activeStates = scanOnly ? [] : quick ? states.filter(([name]) => ['full', 'markers-5', 'hand-20', 'status-indicators', 'action-dock', 'inspector-open'].includes(name)) : states
const dialogCases = [
  ['card-detail', 'field=full&markers=5&piles=40&hand=10', 'inspector'],
  ['master', 'field=full&markers=5&piles=40&hand=10&modalFixture=1', 'master'],
  ['graveyard-empty', 'field=full&markers=5&piles=0&hand=10&modalFixture=1', 'graveyard'],
  ['graveyard-full', 'field=full&markers=5&piles=40&hand=10&modalFixture=1', 'graveyard'],
  ['faction-effect', 'field=full&markers=5&piles=40&hand=10&modalFixture=1', 'faction'],
  ['card-effect', 'field=full&markers=5&piles=40&hand=10&modalFixture=1', 'ability'],
  ['card-choice-1', 'field=full&markers=5&piles=40&hand=10&card-choice=1&choice-count=1', 'prompt'],
  ['card-choice-6', 'field=full&markers=5&piles=40&hand=10&card-choice=1&choice-count=6', 'prompt'],
  ['card-choice-12', 'field=full&markers=5&piles=40&hand=10&card-choice=1&choice-count=12', 'prompt'],
  ['card-choice-20', 'field=full&markers=5&piles=40&hand=10&card-choice=1&choice-count=20', 'prompt'],
  ['card-choice-mixed-12', 'field=full&markers=5&piles=40&hand=10&card-choice=1&choice-count=12&mixed-availability=1', 'prompt'],
  ['disaster-ban-mixed-12', 'field=full&markers=5&piles=40&hand=10&disaster-choice=1&choice-count=12&mixed-availability=1', 'prompt'],
  ['disaster-pick-20', 'field=full&markers=5&piles=40&hand=10&disaster-choice=1&disaster-pick=1&choice-count=20', 'prompt'],
  ['disaster-card', 'field=full&markers=5&piles=40&hand=10&disaster-chain=1', 'disaster'],
  ['morale-payment', 'field=full&markers=5&piles=40&hand=10&morale-payment=1', 'morale'],
]
fs.mkdirSync(output, { recursive: true })
const manifest = { generatedAt: new Date().toISOString(), target, fixedViewports, acceptance: { safeViewport:'all visible interactive controls, battle-critical regions and overlays inside visualViewport minus CDP safe-area insets', safeAreaAudit:{ interactiveSelector, criticalSelectors, overlaySelectors }, handCounts:'exactly one PlayerMat hand count per player; no floating third entry; full text visible and clear of master/relic/markers/field', cardRatio:'5:7 ±5%; horizontal cards keep native ratio', formation:'six square slots, <=2px uniform gaps', cardScale:'field and master share one scale; relic/master about .9; pile/master about .7', localScale:'status icons, keywords, master health, pile counts, morale summary and action buttons remain contained and scale continuously from card/logical viewport tokens', spaceUtilization:'for empty/full at every fixed viewport, record commander+battle+piles+resource union; width>=640 requires >=75% occupation or both margins <=12%, left/right difference <=3pp, and every structural gap <=1.5 card widths', continuity:'1px-neighbor and safe-inset layout proportions change <=10 percentage points', scrolling:'page root fixed; horizontal scroll only in declared hand/card-list containers' }, randomScan: [], states: [], dialogs: [], safeArea: [], desktop: [], status: 'running' }

function intersects(a, b, tolerance = .5) {
  return a.left < b.right - tolerance && a.right > b.left + tolerance && a.top < b.bottom - tolerance && a.bottom > b.top + tolerance
}
async function installMobile(page) {
  await page.addInitScript(() => {
    const native = window.matchMedia.bind(window)
    window.matchMedia = media => media.includes('pointer: coarse')
      ? ({ matches: true, media, addEventListener() {}, removeEventListener() {} }) : native(media)
  })
}

const browser = await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE
  ? { headless:true, executablePath:process.env.L12_CHROMIUM_EXECUTABLE }
  : { headless:true, channel:'msedge' })
try {
  const context = await browser.newContext()
  const page = await context.newPage()
  const cdp = await context.newCDPSession(page)
  // Older installed Edge channels may not expose the experimental safe-area
  // CDP command. In that case apply the same logical viewport variables that
  // the production hook derives from env(safe-area-inset-*).
  let nativeSafeAreaOverride = true
  page.setDefaultTimeout(10_000)
  await installMobile(page)
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())
  async function applyViewportInsets(insets = zeroInsets) {
    await page.evaluate(({ insets, nativeSafeAreaOverride }) => {
      const probe=document.createElement('div'); probe.style.cssText='position:fixed;visibility:hidden;pointer-events:none;padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)'; document.body.append(probe)
      const style=getComputedStyle(probe); const envValue=property=>Math.max(0,parseFloat(style.getPropertyValue(property))||0); const value=property=>nativeSafeAreaOverride?envValue(property):insets[property.replace('padding-','')]; const vv=visualViewport
      const left=(vv?.offsetLeft??0)+value('padding-left'); const top=(vv?.offsetTop??0)+value('padding-top'); const width=Math.max(1,(vv?.width??innerWidth)-value('padding-left')-value('padding-right')); const height=Math.max(1,(vv?.height??innerHeight)-value('padding-top')-value('padding-bottom'))
      const dialogWidth=Math.min(width*.75,height*.75*16/9),dialogHeight=dialogWidth*9/16
      const root=document.documentElement, priority=nativeSafeAreaOverride?'':'important'; root.dataset.l12Viewport='landscape'; root.dataset.l12Mobile='true'; root.style.setProperty('--l12-viewport-left',`${left}px`,priority); root.style.setProperty('--l12-viewport-top',`${top}px`,priority); root.style.setProperty('--l12-viewport-width',`${width}px`,priority); root.style.setProperty('--l12-viewport-height',`${height}px`,priority); root.style.setProperty('--l12-mobile-dialog-width',`${dialogWidth}px`,priority); root.style.setProperty('--l12-mobile-dialog-height',`${dialogHeight}px`,priority); probe.remove()
    }, { insets, nativeSafeAreaOverride })
  }
  async function load(viewport, query, insets = zeroInsets) {
    await page.setViewportSize(viewport)
    if (nativeSafeAreaOverride) {
      try {
        await cdp.send('Emulation.setSafeAreaInsetsOverride', { insets })
      } catch (error) {
        if (!String(error).includes('setSafeAreaInsetsOverride')) throw error
        nativeSafeAreaOverride = false
      }
    }
    await page.goto(`${target}?mobile=1&canvas=1&${query}`, { waitUntil: 'domcontentloaded' })
    await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
    // The standalone fixture mounts GameBoard without App.vue, so mirror the
    // production viewport hook after CDP has populated env(safe-area-inset-*).
    await applyViewportInsets(insets)
    if (process.env.L12_B3_SAFE_DEBUG === '1') console.log(await page.evaluate(() => {
      const probe=document.createElement('div'); probe.style.cssText='position:fixed;visibility:hidden;padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)'; document.body.append(probe)
      const style=getComputedStyle(probe); const root=getComputedStyle(document.documentElement); const result={env:[style.paddingTop,style.paddingRight,style.paddingBottom,style.paddingLeft],vars:[root.getPropertyValue('--l12-viewport-top'),root.getPropertyValue('--l12-viewport-right'),root.getPropertyValue('--l12-viewport-bottom'),root.getPropertyValue('--l12-viewport-left'),root.getPropertyValue('--l12-viewport-width'),root.getPropertyValue('--l12-viewport-height')]}; probe.remove(); return result
    }))
  }
  async function act(name) {
    if (name === 'inspector-open') {
      await page.locator('.formation-slot .card-tile').first().click()
      await page.locator('.mobile-card-inspector-handle').click()
      await page.locator('.mobile-card-inspector').waitFor()
      await page.waitForTimeout(220)
    } else if (name === 'morale-overview') {
      await page.locator('.my-half .resource-morale-summary:visible').click()
      await page.locator('.mobile-morale-overlay').waitFor()
    } else if (name === 'morale-payment' || name === 'morale-payment-minimized') {
      if (await page.locator('.prompt-minimize').count()) await page.locator('.prompt-minimize').click()
      // The minimized prompt restore control can share screen coordinates with
      // the board in narrow fixtures. Dispatch on the intended board control;
      // separate interaction-matrix coverage validates physical hit testing.
      await page.locator('.my-half .resource-morale-summary:visible').evaluate(element => element.click())
      await page.locator('.mobile-morale-overlay').waitFor()
      if (name.endsWith('minimized')) {
        await page.locator('.mobile-morale-overlay').getByRole('button', { name: '最小化', exact: true }).click()
        await page.locator('.mobile-morale-overlay').waitFor({ state: 'detached' })
      }
    } else if (name.startsWith('runes-')) {
      if (name.endsWith('payment') && await page.locator('.prompt-minimize').count()) await page.locator('.prompt-minimize').click()
      await page.locator('.my-half .resource-morale-summary:visible').click()
      await page.locator('.mobile-morale-overlay').waitFor()
    } else if (name === 'disaster-animation') {
      await page.evaluate(() => window.__disasterFixture.emitReveal(false))
      await page.locator('.zone-card-movement .disaster-reveal-card').waitFor()
    } else if (name === 'disaster-waiting') {
      await page.evaluate(() => { window.__disasterFixture.openPrompt(); window.__disasterFixture.emitReveal(false) })
      await page.locator('.prompt-panel').waitFor()
      assert.equal(await page.locator('.zone-card-movement').count(), 0)
    } else if (name === 'disaster-interrupted') {
      await page.evaluate(() => window.__disasterFixture.emitReveal(false))
      await page.locator('.zone-card-movement .disaster-reveal-card').waitFor()
      await page.evaluate(() => window.__disasterFixture.openPrompt())
      await page.locator('.prompt-panel').waitFor()
      await page.locator('.zone-card-movement').waitFor({ state: 'detached' })
    } else if (name === 'action-dock') {
      await page.locator('.my-half .formation-slot .card-tile').first().click()
      await page.locator('.card-context-actions').waitFor()
    }
  }
  async function assertCommon(label, insets = zeroInsets) {
    // Some overlays restore focus and synchronously refresh viewport state when
    // they close. Re-derive the logical safe viewport before every checkpoint
    // so fallback-mode verification has the same lifecycle as App.vue.
    await applyViewportInsets(insets)
    const result = await page.evaluate(({ safeInsets, auditSpec }) => {
      const visible = element => { const style = getComputedStyle(element); const rect = element.getBoundingClientRect(); return style.visibility !== 'hidden' && style.display !== 'none' && rect.width > 0 && rect.height > 0 }
      const rectOf = element => { const r=element.getBoundingClientRect(); return { left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height } }
      const identity = element => ({ tag:element.tagName.toLowerCase(), id:element.id||'', className:typeof element.className==='string'?element.className:'', text:(element.getAttribute('aria-label')||element.textContent||'').trim().replace(/\s+/g,' ').slice(0,80) })
      const clipped = [...document.querySelectorAll('button,b,small,span')].filter(visible).filter(element => element.scrollWidth > element.clientWidth + 1 && !['auto','scroll'].includes(getComputedStyle(element).overflowX)).slice(0,20).map(element => ({ className: element.className, text: element.textContent?.trim(), client: element.clientWidth, scroll: element.scrollWidth }))
      const vv=visualViewport; const visualLeft=vv?.offsetLeft??0; const visualTop=vv?.offsetTop??0; const visualWidth=vv?.width??innerWidth; const visualHeight=vv?.height??innerHeight
      const safe={left:visualLeft+safeInsets.left,top:visualTop+safeInsets.top,right:visualLeft+visualWidth-safeInsets.right,bottom:visualTop+visualHeight-safeInsets.bottom}
      const outsideSafe = r => r.left<safe.left-1||r.top<safe.top-1||r.right>safe.right+1||r.bottom>safe.bottom+1
      const auditGroup = selectors => {
        const counts={}; const records=[]; const seen=new Set()
        for(const selector of selectors){
          const elements=[...document.querySelectorAll(selector)].filter(visible); counts[selector]=elements.length
          for(const element of elements){if(seen.has(element))continue;seen.add(element);records.push({...identity(element),selector,rect:rectOf(element)})}
        }
        return { selectors, counts, count:records.length, outside:records.filter(item=>outsideSafe(item.rect)) }
      }
      const criticalAudit=auditGroup(auditSpec.criticalSelectors)
      const overlayAudit=auditGroup(auditSpec.overlaySelectors)
      const clippedVisibleRect = element => {
        const value=rectOf(element)
        for(let ancestor=element.parentElement;ancestor&&ancestor!==document.documentElement;ancestor=ancestor.parentElement){
          const style=getComputedStyle(ancestor)
          if(!/(auto|scroll|hidden|clip)/.test(`${style.overflow} ${style.overflowX} ${style.overflowY}`))continue
          const clip=rectOf(ancestor); value.left=Math.max(value.left,clip.left);value.top=Math.max(value.top,clip.top);value.right=Math.min(value.right,clip.right);value.bottom=Math.min(value.bottom,clip.bottom)
        }
        value.width=Math.max(0,value.right-value.left);value.height=Math.max(0,value.bottom-value.top);return value
      }
      const interactiveRecords=[...document.querySelectorAll(auditSpec.interactiveSelector)].filter(visible).map(element=>({element,rect:clippedVisibleRect(element)})).filter(({element,rect})=>{
        if(rect.width<=0||rect.height<=0)return false
        const top=document.elementFromPoint(rect.left+rect.width/2,rect.top+rect.height/2)
        return Boolean(top&&(element===top||element.contains(top)))
      }).map(({element,rect})=>({...identity(element),selector:auditSpec.interactiveSelector,rect}))
      const interactiveAudit={selector:auditSpec.interactiveSelector,count:interactiveRecords.length,outside:interactiveRecords.filter(item=>outsideSafe(item.rect))}
      const allowedScroll='.l12-hand,.prompt-card-strip,.graveyard-cards,.gm-hand,.event-list,.mobile-card-detail-body,.graveyard-window,.prompt-panel,.faction-effect-dialog,.master-dialog,.mobile-record-overlay,.mobile-rune-row>div'
      const rogueScroll=[...document.querySelectorAll('*')].filter(visible).filter(element=>element.scrollWidth>element.clientWidth+1&&['auto','scroll'].includes(getComputedStyle(element).overflowX)&&!element.matches(allowedScroll)).slice(0,10).map(element=>({tag:element.tagName,className:element.className,client:element.clientWidth,scroll:element.scrollWidth}))
      const overlaps=(a,b)=>a.left<b.right-.5&&a.right>b.left+.5&&a.top<b.bottom-.5&&a.bottom>b.top+.5
      const floatingEnemyCount=document.querySelectorAll('.mobile-enemy-hand-count').length
      const handCounts=[...document.querySelectorAll('.battlefield-half .mobile-hand-count')].filter(visible).map(element=>{
        const r=element.getBoundingClientRect(); const mat=element.closest('.l12-player-mat')
        const blockers=[...mat.querySelectorAll('.mini-master,.relic-zone,.master-marker-track .rune-orb,.master-marker-track .canopic-orb,.formation')].filter(visible).map(node=>{const box=node.getBoundingClientRect();return{className:node.className,left:box.left,top:box.top,right:box.right,bottom:box.bottom}})
        return { label:element.getAttribute('aria-label')??'', text:element.textContent?.replace(/\s+/g,''), left:r.left,top:r.top,right:r.right,bottom:r.bottom, clipped:element.scrollWidth>element.clientWidth+1, safe:r.left>=safe.left-1&&r.top>=safe.top-1&&r.right<=safe.right+1&&r.bottom<=safe.bottom+1, overlaps:blockers.filter(box=>overlaps(r,box)) }
      })
      const routeButton=document.querySelector('.battle-route-controls button[aria-label="返回大厅"]')
      const routeButtonRect=routeButton?rectOf(routeButton):null
      const routeLines=routeButton?[...routeButton.querySelectorAll('.route-label>span')].map(element=>({text:element.textContent??'',...rectOf(element)})):[]
      const promptHandSummary=document.querySelector('.mobile-target-hand-counts')
      return { safe, overflowX: document.documentElement.scrollWidth > innerWidth + 1, overflowY: document.documentElement.scrollHeight > innerHeight + 1, clipped, rogueScroll, floatingEnemyCount, handCounts, promptHandSummary:promptHandSummary?.getAttribute('aria-label')??'', routeButtonRect, routeLines, audits:{ interactive:interactiveAudit, critical:criticalAudit, overlays:overlayAudit } }
    }, { safeInsets:insets, auditSpec:{ interactiveSelector, criticalSelectors, overlaySelectors } })
    assert.equal(result.overflowX, false, `${label} has page horizontal overflow`)
    assert.equal(result.overflowY, false, `${label} has page vertical overflow`)
    const critical = result.clipped.filter(item => /结束回合|手牌|士气|阵营效果|牌库|墓地|确认|关闭|最小化|返回/.test(item.text ?? ''))
    assert.deepEqual(critical, [], `${label} clips critical copy: ${JSON.stringify(critical)}`)
    assert.deepEqual(result.audits.interactive.outside, [], `${label} places a visible interactive control outside the safe viewport: ${JSON.stringify(result.audits.interactive.outside)}`)
    assert.deepEqual(result.audits.critical.outside, [], `${label} places a critical battle region outside the safe viewport: ${JSON.stringify(result.audits.critical.outside)}`)
    assert.deepEqual(result.audits.overlays.outside, [], `${label} places an overlay or dialog outside the safe viewport: ${JSON.stringify(result.audits.overlays.outside)}`)
    assert.deepEqual(result.rogueScroll, [], `${label} creates an undeclared horizontal scroll container: ${JSON.stringify(result.rogueScroll)}`)
    assert.equal(result.floatingEnemyCount, 0, `${label} retains the removed floating enemy hand counter`)
    if (result.promptHandSummary) {
      assert.equal(result.handCounts.length, 0, `${label} target prompt must not leave a hand counter over a commander or relic: ${JSON.stringify(result.handCounts)}`)
      assert.match(result.promptHandSummary, /^对手手牌 \d+ 张；我方手牌 \d+ 张$/, `${label} target prompt must retain both hand counts: ${result.promptHandSummary}`)
    } else {
      assert.equal(result.handCounts.length, 2, `${label} must render exactly two PlayerMat hand counters: ${JSON.stringify(result.handCounts)}`)
      assert.equal(result.handCounts.filter(item=>/^对手手牌 \d+ 张$/.test(item.label)).length, 1, `${label} must render exactly one opponent hand counter`)
      assert.equal(result.handCounts.filter(item=>/^我方手牌 \d+ 张$/.test(item.label)).length, 1, `${label} must render exactly one own hand counter`)
      assert.equal(result.handCounts.every(item=>/^手牌\d+$/.test(item.text??'')&&!item.clipped&&item.safe&&item.overlaps.length===0), true, `${label} hand counter is clipped, unsafe, or overlaps protected board content: ${JSON.stringify(result.handCounts)}`)
    }
    // The persistent route control is intentionally suppressed while the
    // non-minimized settlement dialog owns the only return-to-lobby action.
    if (result.routeButtonRect) {
      assert.deepEqual(result.routeLines.map(item=>item.text),['返回','大厅'],`${label} return button must use a balanced 2+2 line break`)
      assert.ok(result.routeLines.every(item=>Math.abs((item.left+item.right-result.routeButtonRect.left-result.routeButtonRect.right)/2)<=1)&&result.routeLines[0].bottom<=result.routeLines[1].top+1,`${label} return button copy must be centered and vertically ordered: ${JSON.stringify({button:result.routeButtonRect,lines:result.routeLines})}`)
    }
    return { safe:result.safe, audits:result.audits }
  }
  async function assertBoardGeometry(label, expectedMarkers = null) {
    const value = await page.evaluate(() => {
      const rect = element => { const r = element.getBoundingClientRect(); return { left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height } }
      const shown = selector => [...document.querySelectorAll(selector)].filter(element => { const r=element.getBoundingClientRect(); return r.width>0&&r.height>0&&getComputedStyle(element).visibility!=='hidden' })
      const categories = {
        master: shown('.battlefield-half .mini-master .l12-card-image'),
        relic: shown('.battlefield-half .relic-zone .card-tile'),
        field: shown('.battlefield-half .formation-slot .card-tile'),
        piles: shown('.battlefield-half .mat-piles .pile-card'),
      }
      const cards = Object.entries(categories).flatMap(([category, elements]) => elements.map(element => ({ category, ...rect(element) })))
      const formations = shown('.battlefield-half .formation').map(formation => ({ formation:rect(formation), slots:[...formation.querySelectorAll('.formation-slot')].map(rect) }))
      const markers = shown('.battlefield-half .master-marker-track :is(.rune-orb,.canopic-orb)').map(element => { const atPoint=document.elementFromPoint(element.getBoundingClientRect().left+element.getBoundingClientRect().width/2,element.getBoundingClientRect().top+element.getBoundingClientRect().height/2); return ({ ...rect(element), round:Math.abs(element.getBoundingClientRect().width-element.getBoundingClientRect().height)<=1, topElement:atPoint?.closest('.rune-orb,.canopic-orb')===element, topClass:typeof atPoint?.className==='string'?atPoint.className:atPoint?.tagName }) })
      const blockers = shown('.battlefield-half :is(.formation,.relic-zone)').map(rect)
      return { cards, formations, markers, blockers, width:innerWidth, height:innerHeight }
    })
    if (value.cards.length) {
      for (const card of value.cards) {
        const normalized = Math.min(card.width, card.height) / Math.max(card.width, card.height)
        assert.ok(normalized >= (5/7)*.95 && normalized <= (5/7)*1.05, `${label} ${card.category} card ratio drift: ${JSON.stringify(card)}`)
      }
      const byCategory = new Map()
      for (const card of value.cards) if (!byCategory.has(card.category)) byCategory.set(card.category, Math.max(card.width, card.height))
      const master = byCategory.get('master'), field = byCategory.get('field'), relic = byCategory.get('relic'), piles = byCategory.get('piles')
      if (master && field) assert.ok(field/master >= .95 && field/master <= 1.08, `${label} field/master scale drift: ${JSON.stringify(Object.fromEntries(byCategory))}`)
      if (master && relic) assert.ok(relic/master >= .82 && relic/master <= .98, `${label} relic/master scale drift: ${JSON.stringify(Object.fromEntries(byCategory))}`)
      if (master && piles) assert.ok(piles/master >= .62 && piles/master <= .78, `${label} pile/master scale drift: ${JSON.stringify(Object.fromEntries(byCategory))}`)
    }
    for (const { slots } of value.formations) {
      assert.equal(slots.length, 6, `${label} formation must have six slots`)
      for (const slot of slots) assert.ok(Math.abs(slot.width-slot.height)<=2, `${label} formation slot must be square: ${JSON.stringify(slot)}`)
      const sortedRows = [slots.slice(0,3),slots.slice(3,6)]
      for (const row of sortedRows) for(let index=1;index<row.length;index++) assert.ok(row[index].left-row[index-1].right>=-1&&row[index].left-row[index-1].right<=2.5, `${label} formation horizontal gap is not contiguous`)
      for(let index=0;index<3;index++) assert.ok(slots[index+3].top-slots[index].bottom>=-1&&slots[index+3].top-slots[index].bottom<=2.5, `${label} formation vertical gap is not contiguous`)
    }
    if (expectedMarkers !== null) assert.equal(value.markers.length, expectedMarkers*2, `${label} must render ${expectedMarkers} markers for each player`)
    for (const marker of value.markers) {
      assert.ok(marker.round && marker.left>=0&&marker.top>=0&&marker.right<=value.width&&marker.bottom<=value.height&&marker.topElement, `${label} marker must be round, visible and uncovered: ${JSON.stringify(marker)}`)
      assert.equal(value.blockers.some(blocker => intersects(marker, blocker)), false, `${label} marker overlaps a field or relic: ${JSON.stringify({marker,blockers:value.blockers.filter(blocker=>intersects(marker,blocker))})}`)
    }
  }
  async function assertSpaceUtilization(label) {
    const metrics = await page.evaluate(() => {
      const rect = element => { const r=element.getBoundingClientRect(); return {left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height} }
      const visible = element => { const r=element.getBoundingClientRect(),s=getComputedStyle(element); return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden' }
      return [...document.querySelectorAll('.battlefield-half.l12-player-mat')].filter(visible).map((half,index) => {
        const halfRect=rect(half)
        const parts=['.commander-zone','.battle-zone','.mat-piles','.resource-zone'].map(selector=>half.querySelector(selector)).filter(element=>element&&visible(element)).map(element=>rect(element))
        const union={left:Math.min(...parts.map(item=>item.left)),top:Math.min(...parts.map(item=>item.top)),right:Math.max(...parts.map(item=>item.right)),bottom:Math.max(...parts.map(item=>item.bottom))}
        const ordered=[...parts].sort((a,b)=>a.left-b.left)
        const gaps=ordered.slice(1).map((item,gapIndex)=>Math.max(0,item.left-ordered[gapIndex].right))
        const card=half.querySelector('.mini-master')?.getBoundingClientRect()
        return {
          side:index===0?'opponent':'mine', half:halfRect, union,
          occupiedWidth:(union.right-union.left)/halfRect.width,
          occupiedHeight:(union.bottom-union.top)/halfRect.height,
          leftBlank:(union.left-halfRect.left)/halfRect.width,
          rightBlank:(halfRect.right-union.right)/halfRect.width,
          topBlank:(union.top-halfRect.top)/halfRect.height,
          bottomBlank:(halfRect.bottom-union.bottom)/halfRect.height,
          gaps, cardWidth:card?.width??0,
        }
      })
    })
    assert.equal(metrics.length,2,`${label} must expose two measurable battlefield halves`)
    for(const item of metrics){
      assert.ok(Math.abs(item.leftBlank-item.rightBlank)<=.03,`${label} ${item.side} horizontal blank is asymmetric: ${JSON.stringify(item)}`)
      assert.ok(item.occupiedWidth>=.75||(item.leftBlank<=.12&&item.rightBlank<=.12),`${label} ${item.side} wastes horizontal room: ${JSON.stringify(item)}`)
      assert.ok(item.gaps.every(gap=>gap<=item.cardWidth*1.5+1),`${label} ${item.side} structural gap exceeds 1.5 card widths: ${JSON.stringify(item)}`)
    }
    return metrics
  }
  async function assertAdaptiveLocalScale(label) {
    const metrics = await page.evaluate(() => {
      const visible = element => { const r=element.getBoundingClientRect(),s=getComputedStyle(element); return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden' }
      const rect = element => { const r=element.getBoundingClientRect(); return {left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height} }
      const contained = (inner,outer,tolerance=1) => inner.left>=outer.left-tolerance&&inner.top>=outer.top-tolerance&&inner.right<=outer.right+tolerance&&inner.bottom<=outer.bottom+tolerance
      const cardDetails=[...document.querySelectorAll('.battlefield-half .formation-slot .card-tile')].filter(visible).map(card=>{
        const cardRect=rect(card)
        const children=[...card.querySelectorAll('.card-cost,.card-power,.card-disaster,.card-status-icons,.card-keyword-stack')].filter(visible).map(element=>({className:element.className,rect:rect(element),font:parseFloat(getComputedStyle(element).fontSize)||0}))
        return {card:cardRect,children,contained:children.every(item=>contained(item.rect,cardRect)),statusSizes:[...card.querySelectorAll('.card-status-icon')].filter(visible).map(element=>rect(element).width),keywordFonts:[...card.querySelectorAll('.card-keyword')].filter(visible).map(element=>parseFloat(getComputedStyle(element).fontSize)||0)}
      })
      const masters=[...document.querySelectorAll('.battlefield-half .mini-master')].filter(visible).map(master=>{const outer=rect(master),badge=master.querySelector('.master-health');return badge&&visible(badge)?{outer,badge:rect(badge),font:parseFloat(getComputedStyle(badge).fontSize)||0,contained:contained(rect(badge),outer)}:null}).filter(Boolean)
      const piles=[...document.querySelectorAll('.battlefield-half .mat-piles .pile')].filter(visible).map(pile=>{const outer=rect(pile),badge=pile.querySelector('.pile-count');return badge&&visible(badge)?{outer,badge:rect(badge),font:parseFloat(getComputedStyle(badge).fontSize)||0,contained:contained(rect(badge),outer)}:null}).filter(Boolean)
      const resources=[...document.querySelectorAll('.battlefield-half .resource-zone')].filter(visible).map(zone=>{const outer=rect(zone),summary=zone.querySelector('.resource-morale-summary');return summary&&visible(summary)?{outer,summary:rect(summary),font:parseFloat(getComputedStyle(summary.querySelector('.resource-morale-count')).fontSize)||0,contained:contained(rect(summary),outer)}:null}).filter(Boolean)
      const actions=[...document.querySelectorAll('.card-context-actions button,.mobile-action-dock button,.right-rail .action-panel button')].filter(visible).map(button=>({rect:rect(button),font:parseFloat(getComputedStyle(button).fontSize)||0,text:button.textContent?.trim()||''}))
      const root=getComputedStyle(document.documentElement),logicalHeight=parseFloat(root.getPropertyValue('--l12-viewport-height'))||innerHeight
      return {cardDetails,masters,piles,resources,actions,logicalHeight}
    })
    assert.ok(metrics.cardDetails.length>=2,`${label} must expose field cards for local-scale verification`)
    assert.equal(metrics.cardDetails.every(item=>item.contained),true,`${label} card badges/statuses/keywords leave their card: ${JSON.stringify(metrics.cardDetails.filter(item=>!item.contained))}`)
    for(const item of metrics.cardDetails){
      assert.equal(item.statusSizes.every(size=>size>=8&&size<=item.card.width*.31+1),true,`${label} status icon is not proportional to its card: ${JSON.stringify(item)}`)
      assert.equal(item.keywordFonts.every(size=>size>=5.5&&size<=item.card.width*.19+1),true,`${label} keyword text is not proportional to its card: ${JSON.stringify(item)}`)
    }
    assert.equal(metrics.masters.every(item=>item.contained&&item.badge.height>=12&&item.badge.height<=item.outer.width*.48+1),true,`${label} master health badge is not locally scaled: ${JSON.stringify(metrics.masters)}`)
    assert.equal(metrics.piles.every(item=>item.contained&&item.badge.height>=11&&item.badge.height<=item.outer.width*.48+1),true,`${label} pile badge is not locally scaled: ${JSON.stringify(metrics.piles)}`)
    assert.equal(metrics.resources.every(item=>item.contained&&item.summary.height>=21&&item.summary.height<=item.outer.width*.43+1),true,`${label} morale summary is not locally scaled: ${JSON.stringify(metrics.resources)}`)
    assert.equal(metrics.actions.every(item=>item.rect.height>=29&&item.rect.height<=metrics.logicalHeight*.13+1&&item.font>=8.5),true,`${label} action button is not scaled from the logical viewport: ${JSON.stringify(metrics.actions)}`)
    return metrics
  }
  function assertSafeAreaInventory(label, state, audit) {
    const counts=audit.audits.critical.counts
    const requireCount=(selector,minimum,description)=>assert.ok((counts[selector]??0)>=minimum,`${label} is missing ${description}: ${selector}=${counts[selector]??0}`)
    requireCount('.battle-route-controls',1,'the session action dock')
    requireCount('.battle-route-controls button',2,'return and surrender controls')
    requireCount('.mobile-card-inspector-handle-global',1,'the persistent card-detail handle')
    requireCount('.current-disaster-panel',1,'the current-disaster panel')
    assert.equal(counts['.session-disaster-strip button'],4,`${label} must show all four round disaster buttons`)
    requireCount('.mobile-record-trigger',1,'the match-record button')
    requireCount('.mobile-timed-clocks',1,'the timed-match clock dock')
    requireCount('.action-panel button',1,'the end-turn action')
    requireCount('.board-center > .l12-hand:last-child',1,'the bottom hand lane')
    if(state==='full')requireCount('.gm-open',1,'the closed GM entry')
    if(state==='morale-payment')requireCount('.resource-payment-controls',1,'the bottom payment strip')
  }
  async function assertHand(label, insets = zeroInsets) {
    const cards = page.locator('.board-center > .l12-hand:last-child .hand-card-wrap')
    for (let index=0; index<await cards.count(); index++) {
      const card = cards.nth(index)
      await card.evaluate(element => element.scrollIntoView({ block:'nearest', inline:'center' }))
      const outcome = await card.evaluate((element, safeInsets) => {
        const tile=element.querySelector('.card-tile'); if(!tile)return { ok:false,reason:'missing tile' }
        const outer=tile.getBoundingClientRect()
        const badges=[...tile.querySelectorAll('.card-cost,.card-power,.card-disaster,.card-status-icons')].filter(node=>node.getBoundingClientRect().width>0).map(node=>{const r=node.getBoundingClientRect();return{left:r.left,top:r.top,right:r.right,bottom:r.bottom,text:node.textContent?.trim()}})
        const viewportBottom=(visualViewport?.offsetTop??0)+(visualViewport?.height??innerHeight)-safeInsets.bottom
        const powers=badges.filter(item=>item.text&&/\d/.test(item.text))
        return {
          ok:badges.every(r=>r.left>=outer.left-1&&r.right<=outer.right+1&&r.top>=outer.top-1&&r.bottom<=outer.bottom+1)
            && outer.bottom<=viewportBottom-2 && powers.every(item=>item.bottom<=viewportBottom-2),
          viewportBottom,outer:{left:outer.left,top:outer.top,right:outer.right,bottom:outer.bottom},badges,
        }
      }, insets)
      assert.equal(outcome.ok, true, `${label} hand card ${index+1} or its numeric badge crosses the visual viewport safe bottom: ${JSON.stringify(outcome)}`)
    }
  }
  async function assertOverlay(label, selector, insets = zeroInsets) {
    await page.waitForTimeout(260)
    const result = await page.locator(selector).evaluate(element => {
      const r=element.getBoundingClientRect(), root=getComputedStyle(document.documentElement), number=name=>parseFloat(root.getPropertyValue(name))||0
      const rect=node=>{const value=node.getBoundingClientRect();return{left:value.left,top:value.top,right:value.right,bottom:value.bottom,width:value.width,height:value.height}}
      const within=(inner,outer,tolerance=1)=>inner.left>=outer.left-tolerance&&inner.top>=outer.top-tolerance&&inner.right<=outer.right+tolerance&&inner.bottom<=outer.bottom+tolerance
      const cardEntries=[...element.querySelectorAll('.prompt-card-candidate,.graveyard-card-entry')].map(entry=>{
        const outer=rect(entry),image=entry.querySelector('.l12-card-image,.card-tile'),name=entry.querySelector('.prompt-card-candidate__name,.graveyard-card-name'),state=entry.querySelector('.prompt-card-candidate__state'),strip=entry.closest('.prompt-card-strip,.graveyard-cards'),imageRect=image?rect(image):null,nameRect=name?rect(name):null,stateRect=state&&state.textContent?.trim()?rect(state):null,stripRect=strip?rect(strip):null
        return{text:name?.textContent?.trim()||'',nameClipped:Boolean(name&&(name.scrollWidth>name.clientWidth+1||name.scrollHeight>name.clientHeight+1)),imageContained:Boolean(imageRect&&within(imageRect,outer)),nameContained:Boolean(nameRect&&within(nameRect,outer)),stateContained:!stateRect||(within(stateRect,outer)&&Boolean(stripRect&&stateRect.top>=stripRect.top-1&&stateRect.bottom<=stripRect.bottom+1)),outer,image:imageRect,name:nameRect,state:stateRect}
      })
      const strips=[...element.querySelectorAll('.prompt-card-strip,.graveyard-cards')].map(strip=>{
        const outer=rect(strip),items=[...strip.querySelectorAll(':scope > .prompt-card-candidate,:scope > .graveyard-card-entry')].map(rect),overflow=strip.scrollWidth>strip.clientWidth+1
        const union=items.length?{left:Math.min(...items.map(item=>item.left)),right:Math.max(...items.map(item=>item.right))}:null
        return{overflow,count:items.length,centreDelta:union?Math.abs((union.left+union.right-outer.left-outer.right)/2):0,width:outer.width,choiceStrip:strip.classList.contains('prompt-card-strip'),heightSpread:items.length?Math.max(...items.map(item=>item.height))-Math.min(...items.map(item=>item.height)):0}
      })
      const footer=element.querySelector('.prompt-action-footer,footer'),body=element.querySelector('[data-ui-contract="mobile-choice-scroll-body"]')
      const choiceStates=[...element.querySelectorAll('.prompt-card-candidate')].map(candidate=>({selected:candidate.classList.contains('selected'),unavailable:candidate.classList.contains('unavailable'),state:candidate.querySelector('.prompt-card-candidate__state')?.textContent?.trim()||'',pressed:candidate.getAttribute('aria-pressed'),disabled:candidate.getAttribute('aria-disabled')}))
      const primary=[...element.children].filter(child=>child.matches?.('.l12-card-image,.master-content,.faction-effect-content')).map(rect)
      const primaryUnion=primary.length?{left:Math.min(...primary.map(item=>item.left)),top:Math.min(...primary.map(item=>item.top)),right:Math.max(...primary.map(item=>item.right)),bottom:Math.max(...primary.map(item=>item.bottom))}:null
      const distribution=primaryUnion?{horizontal:Math.abs((primaryUnion.left+primaryUnion.right-r.left-r.right)/2),vertical:Math.abs((primaryUnion.top+primaryUnion.bottom-r.top-r.bottom)/2)}:null
      return {left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height,scrollWidth:element.scrollWidth,clientWidth:element.clientWidth,expectedDialog:{width:number('--l12-mobile-dialog-width'),height:number('--l12-mobile-dialog-height')},cardEntries,strips,choiceStates,footer:footer?rect(footer):null,body:body?rect(body):null,distribution}
    })
    const viewport = await page.evaluate(() => ({width:innerWidth,height:innerHeight}))
    assert.ok(result.left>=insets.left+7&&result.top>=insets.top+7&&result.right<=viewport.width-insets.right-7&&result.bottom<=viewport.height-insets.bottom-7, `${label} lacks an 8px inset-aware safe boundary: ${JSON.stringify({ result, insets })}`)
    assert.ok(result.scrollWidth<=result.clientWidth+1, `${label} scrolls horizontally`)
    if (['.prompt-panel','.waiting-panel','.master-dialog','.graveyard-window','.faction-effect-dialog','.mobile-record-overlay','.mobile-morale-overlay','.battle-dialog','.l12-settings-modal'].includes(selector)) {
      assert.ok(Math.abs(result.width-result.expectedDialog.width)<=1.5&&Math.abs(result.height-result.expectedDialog.height)<=1.5,`${label} does not use the shared 75% dialog frame: ${JSON.stringify(result)}`)
      assert.ok(Math.abs(result.width/result.height-16/9)<=.02,`${label} dialog aspect ratio drifted from 16:9: ${JSON.stringify(result)}`)
    }
    assert.equal(result.cardEntries.every(item=>item.text&&!item.nameClipped&&item.imageContained&&item.nameContained&&item.stateContained),true,`${label} must show every dialog card with complete art, name and state label: ${JSON.stringify(result.cardEntries.filter(item=>!item.text||item.nameClipped||!item.imageContained||!item.nameContained||!item.stateContained))}`)
    assert.equal(result.strips.filter(item=>!item.overflow&&item.count>0).every(item=>item.centreDelta<=Math.max(12,item.width*.05)),true,`${label} sparse card rows must be evenly centred: ${JSON.stringify(result.strips)}`)
    assert.equal(result.strips.filter(item=>item.choiceStrip&&item.count>1).every(item=>item.heightSpread<=1),true,`${label} choice cards must retain one aligned row height despite long names or state labels: ${JSON.stringify(result.strips)}`)
    if(result.footer&&result.body){assert.ok(result.body.bottom<=result.footer.top+1,`${label} scroll body overlaps the protected confirmation footer`);assert.ok(result.footer.bottom<=result.bottom+1,`${label} confirmation footer leaves the dialog`) }
    if(result.choiceStates.length){assert.equal(result.choiceStates.every(item=>item.pressed==='true'||item.pressed==='false'),true,`${label} choice cards must expose aria-pressed`);assert.equal(result.choiceStates.filter(item=>item.unavailable).every(item=>item.state.includes('不可选择')&&item.disabled==='true'),true,`${label} unavailable cards need an explicit state label`) }
    if(result.distribution) assert.ok(result.distribution.horizontal<=result.width*.12&&result.distribution.vertical<=result.height*.15,`${label} primary dialog content is crowded to one side: ${JSON.stringify(result.distribution)}`)
    const image = page.locator(selector).locator('.l12-card-image').first()
    if (await image.count() && await image.isVisible()) {
      const imageRect = await image.boundingBox(); const normalized=Math.min(imageRect.width,imageRect.height)/Math.max(imageRect.width,imageRect.height)
      assert.ok(normalized>=.62&&normalized<=.78, `${label} dialog card ratio is invalid`)
    }
    if(selector==='.mobile-card-inspector'){
      const detail=await page.locator(selector).evaluate(element=>{
        const rect=node=>{const r=node?.getBoundingClientRect();return r&&{left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height}}
        const root=getComputedStyle(document.documentElement),number=name=>parseFloat(root.getPropertyValue(name))||0,safe={left:number('--l12-viewport-left'),top:number('--l12-viewport-top')};safe.right=safe.left+number('--l12-viewport-width');safe.bottom=safe.top+number('--l12-viewport-height')
        const nodes={title:element.querySelector('header h2'),image:element.querySelector('.archive-detail-image'),copy:element.querySelector('.card-detail-copy'),effect:element.querySelector('.l12-effect-body'),close:element.querySelector('header button')}
        const geometry=Object.fromEntries(Object.entries(nodes).map(([name,node])=>[name,rect(node)])),outside=Object.entries(geometry).filter(([,r])=>r&&(r.left<safe.left-1||r.top<safe.top-1||r.right>safe.right+1||r.bottom>safe.bottom+1)),horizontal=Object.entries(nodes).filter(([,node])=>node&&node.scrollWidth>node.clientWidth+1).map(([name,node])=>({name,client:node.clientWidth,scroll:node.scrollWidth}))
        const drawer=element.getBoundingClientRect(),imageRect=geometry.image,copyRect=geometry.copy,effectRect=geometry.effect
        return{outside,horizontal,title:nodes.title?.textContent?.trim()||'',effect:nodes.effect?.textContent?.trim()||'',effectVisible:Boolean(effectRect&&copyRect&&effectRect.bottom>copyRect.top&&effectRect.top<copyRect.bottom),imageShare:imageRect?(imageRect.width*imageRect.height)/(drawer.width*drawer.height):1}
      })
      assert.deepEqual(detail.outside,[],`${label} card-detail child leaves the safe viewport: ${JSON.stringify(detail.outside)}`)
      assert.deepEqual(detail.horizontal,[],`${label} card-detail child clips horizontally: ${JSON.stringify(detail.horizontal)}`)
      assert.ok(detail.title&&detail.title!=='选择一张卡牌'&&detail.effect&&detail.effectVisible,`${label} must show a real card name and first-screen rules text`)
      assert.ok(detail.imageShare<=.30,`${label} reference image exceeds 30% of drawer area`)
    }
  }
  async function layoutRatios() {
    return page.evaluate(() => {
      const ratio=(selector,axis,total)=>{const r=document.querySelector(selector)?.getBoundingClientRect();return r?(axis==='x'?r.width:r.height)/total:0}
      return { left:ratio('.left-rail','x',innerWidth), field:ratio('.felt-board','x',innerWidth), right:ratio('.right-rail','x',innerWidth), hand:ratio('.board-center>.l12-hand:last-child','y',innerHeight) }
    })
  }

  for (const viewport of activeFixed) {
    const suffix = `${viewport.width}x${viewport.height}`
    for (const [name, query] of activeStates) {
      await load(viewport, query)
      await act(name)
      await assertCommon(`${name} ${suffix}`)
      if (!name.startsWith('settlement') && !name.startsWith('runes-') && !['inspector-open','morale-overview','morale-payment','morale-payment-minimized','board-target','board-slot','disaster-animation','disaster-waiting','disaster-interrupted'].includes(name)) {
        const markerMatch = /^markers-(\d)$/.exec(name)
        await assertBoardGeometry(`${name} ${suffix}`, markerMatch ? Number(markerMatch[1]) : name==='empty' ? 0 : null)
      }
      if (name.startsWith('hand-')) await assertHand(`${name} ${suffix}`)
      const spaceUtilization = ['empty','full'].includes(name) ? await assertSpaceUtilization(`${name} ${suffix}`) : undefined
      const adaptiveLocalScale = ['status-indicators','action-dock'].includes(name) ? await assertAdaptiveLocalScale(`${name} ${suffix}`) : undefined
      const file = `state-${name}-${suffix}.png`
      await page.screenshot({ path:path.join(output,file) })
      manifest.states.push({ state:name, viewport:suffix, file, ...(spaceUtilization ? {spaceUtilization} : {}), ...(adaptiveLocalScale ? {adaptiveLocalScale} : {}), assertions:'passed' })
    }
  }

  async function openDialog(kind) {
    if (kind === 'inspector') {
      await page.locator('.formation-slot .card-tile').first().click(); await page.locator('.mobile-card-inspector-handle').click(); await page.locator('.mobile-card-inspector').waitFor(); await page.waitForTimeout(220); return '.mobile-card-inspector'
    }
    if (kind === 'master') { await page.locator('.mini-master').last().click(); await page.locator('.master-dialog').waitFor(); return '.master-dialog' }
    if (kind === 'graveyard') { await page.locator('.graveyard').last().click(); await page.locator('.graveyard-window').waitFor(); return '.graveyard-window' }
    if (kind === 'faction') { await page.locator('.resource-faction-action').last().click(); await page.locator('.faction-effect-dialog').waitFor(); return '.faction-effect-dialog' }
    if (kind === 'ability') {
      await page.locator('.my-half .formation-slot .card-tile').first().click()
      await page.locator('.card-context-actions').getByRole('button', { name:'发动', exact:true }).click()
      await page.locator('.faction-effect-dialog').waitFor(); return '.faction-effect-dialog'
    }
    if (kind === 'prompt') { await page.locator('.prompt-panel').waitFor(); return '.prompt-panel' }
    if (kind === 'disaster') { await page.evaluate(()=>window.__disasterFixture.emitReveal(false)); await page.locator('.zone-card-movement .disaster-reveal-card').waitFor(); return '.zone-card-movement .disaster-reveal-card' }
    if (kind === 'morale') { if(await page.locator('.prompt-minimize').count())await page.locator('.prompt-minimize').click(); await page.locator('.resource-morale-summary').last().click(); await page.locator('.mobile-morale-overlay').waitFor(); return '.mobile-morale-overlay' }
    throw new Error(`unknown dialog kind ${kind}`)
  }
  for (const viewport of activeDialogs) {
    const suffix=`${viewport.width}x${viewport.height}`
    for (const [name,query,kind] of dialogCases) {
      await load(viewport,query)
      const selector=await openDialog(kind)
      await assertCommon(`${name} ${suffix}`)
      await assertOverlay(`${name} ${suffix}`,selector)
      const file=`dialog-${name}-${suffix}.png`
      await page.screenshot({path:path.join(output,file)})
      manifest.dialogs.push({state:name,viewport:suffix,file,assertions:'passed'})
      const minimize=page.locator(selector).getByRole('button',{name:/最小化/}).first()
      if(await minimize.count()&&await minimize.isVisible()) { await minimize.click(); const expand=page.getByRole('button',{name:/展开/}).first(); await expand.waitFor(); await expand.click() }
      const confirm=page.locator(selector).getByRole('button',{name:/确认|返回对局|收起|关闭/}).first()
      if(await confirm.count()&&await confirm.isVisible()) await confirm.click()
    }
  }

  for (const profile of dialogOnly || scanOnly ? [] : safeAreaProfiles) {
    await load(profile.viewport,'field=full&markers=5&piles=40&hand=20',zeroInsets)
    const baselineRatios=await layoutRatios()
    for (const [name,query,action] of safeAreaScenarios) {
      await load(profile.viewport,query,profile.insets)
      const label=`safe ${profile.name} ${name} ${profile.viewport.width}x${profile.viewport.height}`
      let gmPanelAudit=null
      if (query.includes('gm=1')) {
        await page.locator('.gm-panel').waitFor()
        gmPanelAudit=await assertCommon(`${label} GM panel`,profile.insets)
        await assertOverlay(`${label} GM panel`,'.gm-panel',profile.insets)
        const close=page.locator('.gm-panel>header button')
        if(await close.count())await close.evaluate(element => element.click())
        await page.locator('.gm-open').waitFor()
      }
      let overlaySelector=''
      if (action === 'inspector-open') {
        await act('inspector-open')
        overlaySelector='.mobile-card-inspector'
      } else if (action === 'card-choice-12') {
        await page.locator('.prompt-panel').waitFor()
        overlaySelector='.prompt-panel'
      } else if (action === 'morale-payment') {
        await act('morale-payment')
        overlaySelector='.mobile-morale-overlay'
      }
      const audit=await assertCommon(label,profile.insets)
      assertSafeAreaInventory(label,name,audit)
      if (action === 'full') await assertBoardGeometry(label,1)
      if (action === 'markers-5') await assertBoardGeometry(label,5)
      if (action === 'hand-20') { await assertBoardGeometry(label,5); await assertHand(label,profile.insets) }
      if (overlaySelector) await assertOverlay(label,overlaySelector,profile.insets)
      const insetRatios=await layoutRatios()
      for(const key of Object.keys(baselineRatios)) assert.ok(Math.abs(baselineRatios[key]-insetRatios[key])<=.1,`${label} ${key} layout proportion changes more than 10 percentage points under safe-area insets`)
      const file=`safe-${profile.name}-${name}-${profile.viewport.width}x${profile.viewport.height}.png`
      await page.screenshot({path:path.join(output,file)})
      manifest.safeArea.push({profile:profile.name,viewport:`${profile.viewport.width}x${profile.viewport.height}`,insets:profile.insets,state:name,file,ratios:insetRatios,baselineRatios,audit:audit.audits,gmPanelAudit:gmPanelAudit?.audits??null,assertions:'passed'})
    }
  }
  if (nativeSafeAreaOverride) {
    try { await cdp.send('Emulation.setSafeAreaInsetsOverride',{insets:zeroInsets}) }
    catch(error){if(!String(error).includes('setSafeAreaInsetsOverride'))throw error;nativeSafeAreaOverride=false}
  }

  if (!quick && !safeOnly && !dialogOnly) {
    const random = scanOnly
      ? scanOnly.split(',').map(value=>{const [width,height]=value.split('x').map(Number);assert.ok(width>0&&height>0,`invalid L12_B3_SCAN_ONLY viewport: ${value}`);return{width,height}})
      : []
    let seed = 73129
    // Random mobile-layout scans stay inside the same 4:3-or-wider aspect
    // contract used by automatic layout selection. Near-square viewports are
    // intentionally covered by desktop/isolation tests instead of being
    // forced into a layout the product would not select.
    if(!scanOnly){for (let index=0;index<30;index++) { seed=(seed*48271)%2147483647; const width=568+(seed%457); seed=(seed*48271)%2147483647; const maxHeight=Math.min(768,Math.floor(width*.75)); const height=320+(seed%(maxHeight-319)); random.push({width,height}) }
    random.push({width:568,height:320},{width:1024,height:768})}
    for (const viewport of random) {
      await load(viewport,'field=full&markers=5&piles=40&hand=20&rankedClock=1')
      await assertCommon(`scan ${viewport.width}x${viewport.height}`)
      try { await assertBoardGeometry(`scan ${viewport.width}x${viewport.height}`,5) }
      catch(error){await page.screenshot({path:path.join(output,`failed-scan-${viewport.width}x${viewport.height}.png`)});throw error}
      await assertHand(`scan ${viewport.width}x${viewport.height}`)
      const before=await layoutRatios()
      const neighborWidth=Math.min(1024,viewport.width+1)
      const neighbor={width:neighborWidth,height:Math.min(Math.floor(neighborWidth*.75),Math.min(768,viewport.height+1))}
      await load(neighbor,'field=full&markers=5&piles=40&hand=20&rankedClock=1')
      const after=await layoutRatios()
      for(const key of Object.keys(before))assert.ok(Math.abs(before[key]-after[key])<=.1,`scan ${viewport.width}x${viewport.height} ${key} layout proportion jumps more than 10%`)
      manifest.randomScan.push({viewport:`${viewport.width}x${viewport.height}`,neighbor:`${neighbor.width}x${neighbor.height}`,ratios:before,neighborRatios:after,assertions:'passed'})
    }
  }

  if (!safeOnly && !dialogOnly && !scanOnly) {
    const desktop = await browser.newContext()
    const desktopPage = await desktop.newPage()
    for (const viewport of [{width:1366,height:768},{width:1600,height:900},{width:1920,height:1080}]) {
      await desktopPage.setViewportSize(viewport); await desktopPage.goto(`${target}?field=full&markers=5&piles=40&hand=15`,{waitUntil:'domcontentloaded'})
      assert.equal(await desktopPage.locator('[data-l12-mobile-landscape="true"]').count(),0,`desktop ${viewport.width}x${viewport.height} entered mobile layout`)
      assert.equal(await desktopPage.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1||document.documentElement.scrollHeight>innerHeight+1),false,`desktop ${viewport.width}x${viewport.height} overflows`)
      manifest.desktop.push({viewport:`${viewport.width}x${viewport.height}`,assertions:'mobile root absent; overflow absent'})
    }
    await desktop.close()
  }
  manifest.status='passed'
  fs.writeFileSync(path.join(output,'manifest.json'),JSON.stringify(manifest,null,2))
  console.log(JSON.stringify({output,fixedScreenshots:manifest.states.length,dialogScreenshots:manifest.dialogs.length,safeAreaScreenshots:manifest.safeArea.length,randomScans:manifest.randomScan.length,desktop:manifest.desktop,status:'passed'},null,2))
} catch (error) {
  manifest.status='failed'; manifest.error=String(error?.stack||error)
  fs.writeFileSync(path.join(output,'manifest.json'),JSON.stringify(manifest,null,2))
  throw error
} finally { await browser.close() }
