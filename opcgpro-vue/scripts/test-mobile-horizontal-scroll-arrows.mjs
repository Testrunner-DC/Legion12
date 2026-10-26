import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8')
const geometrySource = read('../src/l12/horizontalScrollGeometry.ts')
const javascript = ts.transpileModule(geometrySource, { compilerOptions: { target: ts.ScriptTarget.ES2022,
  module: ts.ModuleKind.ES2022 } }).outputText
const { horizontalScrollState: state, horizontalScrollDestination: move, clipScrollRect: clip,
  scrollControlFrame: frame } = await import(`data:text/javascript;base64,${Buffer.from(javascript).toString('base64')}`)
let checks = 0
function check(name, run) { run(); checks++; console.log(`PASS ${name}`) }
check('empty and fitting content have no scroll actions', () => {
  for (const p of [{left:0,width:300,contentWidth:0},{left:0,width:300,contentWidth:300},{left:0,width:0,contentWidth:100}]) {
    assert.equal(state(p).overflowing,false)
  }
})
check('first, middle and final edge flags', () => {
  assert.deepEqual(state({left:0,width:100,contentWidth:300}),{left:0,maximum:200,overflowing:true,previous:false,next:true})
  assert.equal(state({left:100,width:100,contentWidth:300}).previous,true)
  assert.equal(state({left:100,width:100,contentWidth:300}).next,true)
  assert.equal(state({left:200,width:100,contentWidth:300}).next,false)
})
check('overscroll, stale offsets and nonfinite values clamp safely', () => {
  assert.equal(state({left:-40,width:100,contentWidth:300}).left,0)
  assert.equal(state({left:900,width:100,contentWidth:300}).left,200)
  assert.equal(state({left:NaN,width:100,contentWidth:NaN}).overflowing,false)
})
check('two-pixel tolerances do not create false overflow or endless actions', () => {
  assert.equal(state({left:0,width:100,contentWidth:102}).overflowing,false)
  assert.equal(state({left:1.5,width:100,contentWidth:300}).previous,false)
  assert.equal(state({left:199,width:100,contentWidth:300}).next,false)
})
check('arrow steps preserve context and clamp at both ends', () => {
  assert.equal(move({left:0,width:100,contentWidth:300},1),80)
  assert.equal(move({left:170,width:100,contentWidth:300},1),200)
  assert.equal(move({left:20,width:100,contentWidth:300},-1),0)
  assert.equal(move({left:0,width:NaN,contentWidth:300},1),0)
  assert.equal(move({left:0,width:300,contentWidth:900,stepWidth:224},1),179.20000000000002)
})
check('repeated real-range steps reach the final item and return to the start', () => {
  for (const width of [100,320,568,844]) {
    let left=0
    for(let i=0;i<30;i++)left=move({left,width,contentWidth:width*9+13},1)
    assert.equal(left,width*8+13)
    for(let i=0;i<30;i++)left=move({left,width,contentWidth:width*9+13},-1)
    assert.equal(left,0)
  }
})
check('clipping is axis-specific and accepts a fully hidden empty intersection', () => {
  const rect={left:10,top:20,right:300,bottom:200},cut={left:40,top:40,right:200,bottom:100}
  assert.deepEqual(clip(rect,cut),cut)
  assert.deepEqual(clip(rect,cut,false,true),{left:10,top:40,right:300,bottom:100})
  assert.equal(frame(clip(rect,{left:400,top:0,right:500,bottom:200}),rect,
    {width:290,height:180,borderLeft:0,borderTop:0,scrollLeft:0,scrollTop:0}),null)
})
check('host border, scroll offsets and board scale are accounted once', () => {
  assert.deepEqual(frame({left:110,top:80,right:310,bottom:180},{left:10,top:20,right:510,bottom:220},
    {width:1000,height:400,borderLeft:2,borderTop:3,scrollLeft:40,scrollTop:60}),
    {left:238,top:277,width:400,buttonWidth:76,buttonHeight:76})
})
check('invisible and too-small regions are not claimed as reachable controls', () => {
  const metrics={width:100,height:100,borderLeft:0,borderTop:0,scrollLeft:0,scrollTop:0}
  const host={left:0,top:0,right:100,bottom:100}
  assert.equal(frame({left:0,top:0,right:100,bottom:20},host,metrics),null)
  assert.equal(frame(host,host,{...metrics,width:0}),null)
})

const expected = {
  'game/HandArea.vue':1,'game/GameBoard.vue':1,'game/GmPanel.vue':1,'game/GraveyardOverlay.vue':1,
  'game/PromptOverlay.vue':9,'SingleCardPicker.vue':1,'L12DeckEditor.vue':1,'site/NewsPage.vue':1,
  'site/RuleCenterPage.vue':1,'site/PublicDeckDetailPage.vue':1,'site/RankingsPage.vue':4,
  'site/TournamentAccountDetail.vue':1,'site/TournamentCreateWizard.vue':1,'site/TournamentHubPage.vue':1,
  'site/MasterMatchupMatrix.vue':1,'site/AdminMasterAnalyticsPanel.vue':2,
  'site/MasterDisasterStrategyPanel.vue':1,'site/ScopedDisasterAnalytics.vue':1,
  'site/AdminGlobalDataPanel.vue':1,'site/AdminGlobalAnalyticsSummary.vue':1,
  'site/AdminEffectWorkbenchPanel.vue':1,'site/AdminSiteContentPanel.vue':2,
  'site/AdminAccountDetailPage.vue':1,'site/AdminTournamentWorkbench.vue':1,
  'site/DeckLibraryPage.vue':1,
}
for(const [file,count] of Object.entries(expected))check(`explicit shared consumer ${file}`,()=>{
  const source=read(`../src/l12/${file}`),sfc=parse(source).descriptor
  assert.match(sfc.scriptSetup.content,/import\s*\{\s*vHorizontalScrollArrows(?:,\s*type HorizontalScrollArrowOptions)?\s*\}\s*from\s*['"][^'"]*horizontalScrollArrows['"]/)
  assert.equal((sfc.template.content.match(/v-horizontal-scroll-arrows=/g)||[]).length,count)
})
const directive=read('../src/l12/horizontalScrollArrows.ts')
check('native arrows preserve target DOM and original gestures',()=>{
  assert.match(directive,/insertBefore\(layer, target\)/)
  assert.doesNotMatch(directive,/appendChild\(target\)|append\(target\)|setInterval|document\.querySelectorAll/)
  assert.match(directive,/button\.type = 'button'/)
  assert.match(directive,/event\.stopPropagation\(\)/)
  assert.match(directive,/target\.scrollTo\(\{ left, behavior: 'instant' \}\)/)
})
check('overflow-hidden transform tracks require an explicit adapter',()=>{
  assert.match(directive,/if \(!adapter && !\/\^\(auto\|scroll\)\$\/\.test\(style\.overflowX\)\) \{ hideControls\(true\); return \}/)
  assert.match(directive,/adapter\?\.position\(\)/)
})
check('observer, frame, event and generated-id ownership is released',()=>{
  for(const contract of ['cancelAnimationFrame(frame)','children.disconnect()','resize.disconnect()','intersection?.disconnect()',
    "document.removeEventListener('scroll', onScroll, true)","target.removeEventListener('load', schedule, true)",
    'layer.remove()',"target.removeAttribute('id')",'controllers.delete(element)']) assert(directive.includes(contract),contract)
})
check('native arrow gutters disappear when their own padding caused the residual range',()=>{
  assert.match(directive,/contentWidth: native\.contentWidth - extraPadding/)
  assert.match(directive,/target\.removeAttribute\('data-l12-native-scroll-arrows'\)/)
  assert.match(directive,/if \(!inViewport\) \{ hideControls\(false\); return \}/)
})
check('hot rows adapt the original animation rather than adding a second track or timer',()=>{
  const source=read('../src/l12/site/DeckLibraryPage.vue')
  assert.match(source,/v-horizontal-scroll-arrows="hotDeckScrollOptions\(rowIndex\)"/)
  assert.match(source,/animation\.currentTime = duration \* \(rowIndex === 1 \? 1 - progress : progress\)/)
  assert.match(source,/circular: true/)
  assert.match(source,/hotDeckArrowPaused\.value\[rowIndex\]/)
  assert.match(source,/\.hot-deck-viewport\[data-hot-row-viewport="2"\]\{margin-top:8px\}/)
})
check('final hiding releases adapter interaction and keyboard activation is isolated',()=>{
  const hide=directive.split('function hideControls(')[1].split('function viewportChanged')[0]
  assert.match(hide,/hovered = false/)
  assert.match(hide,/interact\?\.\(false\)/)
  assert.match(directive,/button\.addEventListener\('keydown', isolateActivationKey\)/)
  assert.match(directive,/button\.addEventListener\('keyup', isolateActivationKey\)/)
  assert.match(directive,/event\.key === 'Enter' \|\| event\.key === ' '/)
})
check('unchanged viewport announcements do not clamp a focused end edge',()=>{
  assert.match(directive,/if \(current !== previousViewport\) refreshGutters = true/)
  assert.match(directive,/const preservedLeft = refreshGutters \? target\.scrollLeft : undefined/)
  assert.match(directive,/if \(!adapter && preservedLeft !== undefined\) target\.scrollLeft = preservedLeft/)
})
console.log(JSON.stringify({suite:'mobile horizontal scroll arrows',checks,explicitConsumers:Object.keys(expected).length,
  explicitTargets:38,hotRows:2,browserOrPhysicalDeviceVerified:false}))
