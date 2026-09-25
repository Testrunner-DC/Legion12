import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5197/__l12_battle_preview__'
const output = process.env.L12_DIALOG_EQUAL_OUT || path.resolve('artifacts', 'battle-dialog-option-consistency')
fs.mkdirSync(output, { recursive: true })

const viewports = [
  { name:'desktop-1280x720', width:1280, height:720, mobile:false },
  { name:'desktop-1920x1080', width:1920, height:1080, mobile:false },
  { name:'desktop-3440x1440', width:3440, height:1440, mobile:false },
  { name:'mobile-667x375', width:667, height:375, mobile:true },
  { name:'mobile-844x390', width:844, height:390, mobile:true },
  { name:'mobile-1024x576', width:1024, height:576, mobile:true },
]
const report = { target, assertions:0, screenshots:[], states:[], status:'running' }
const ok = (value, message) => { assert.ok(value, message); report.assertions += 1 }
const closeEnough = (a,b) => Math.abs(a-b) <= 1

const browser = await chromium.launch({ headless:true, channel:'msedge' })
try {
  const context = await browser.newContext()
  const page = await context.newPage()
  page.setDefaultTimeout(12_000)
  await page.route('**/*', route => ['127.0.0.1','localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())

  async function load(viewport, query) {
    await page.setViewportSize(viewport)
    const mobile = viewport.mobile ? 'mobile=1&canvas=1&' : ''
    await page.goto(`${target}?${mobile}field=full&markers=5&piles=40&hand=12&modalFixture=1&${query}`, { waitUntil:'domcontentloaded' })
    if (viewport.mobile) await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
    await page.waitForTimeout(120)
  }

  async function boxes(selector) {
    return page.locator(selector).evaluateAll(nodes => nodes.filter(node => {
      const r=node.getBoundingClientRect(),s=getComputedStyle(node)
      return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden'
    }).map(node => {
      const r=node.getBoundingClientRect(),s=getComputedStyle(node)
      return { width:r.width,height:r.height,left:r.left,top:r.top,right:r.right,bottom:r.bottom,overflow:s.overflow,text:(node.textContent||'').trim().replace(/\s+/g,' '),selected:node.classList.contains('selected')||node.getAttribute('aria-pressed')==='true',disabled:node.matches(':disabled') }
    }))
  }

  async function assertEqualGroup(label, selector, minimum=2, allowHorizontalOverflow=false) {
    const values=await boxes(selector)
    ok(values.length>=minimum, `${label}: expected at least ${minimum} peers, got ${values.length}`)
    const [first,...rest]=values
    ok(rest.every(item=>closeEnough(item.width,first.width)&&closeEnough(item.height,first.height)), `${label}: peers changed dimensions ${JSON.stringify(values)}`)
    if (!allowHorizontalOverflow) ok(values.every(item=>item.left>=-1&&item.top>=-1&&item.right<=page.viewportSize().width+1&&item.bottom<=page.viewportSize().height+1), `${label}: peer leaves viewport ${JSON.stringify(values)}`)
    report.states.push({label,selector,boxes:values})
    return values
  }

  for (const viewport of viewports) {
    await load(viewport, 'option-fixture=1')
    const optionsBefore=await assertEqualGroup(`${viewport.name} text options`, '.effect-option-list>button', 4)
    ok(optionsBefore.some(item=>item.disabled), `${viewport.name}: disabled peer fixture missing`)
    await page.locator('.effect-option-list>button:not(:disabled)').first().click()
    const optionsAfter=await assertEqualGroup(`${viewport.name} selected text options`, '.effect-option-list>button', 4)
    ok(optionsAfter.some(item=>item.selected), `${viewport.name}: selected state missing`)
    await assertEqualGroup(`${viewport.name} prompt footer`, '.prompt-action-footer>button', 2)

    await load(viewport, 'card-choice=1&choice-count=8&mixed-availability=1&mixed-orientation=1')
    const cardsBefore=await assertEqualGroup(`${viewport.name} mixed card options`, '.prompt-card-candidate:not(.size-featured)', 8, true)
    ok(await page.locator('.prompt-choice-body').evaluate(body => {
      const parent=body.getBoundingClientRect()
      return [...body.querySelectorAll('.prompt-card-candidate:not(.size-featured)')].every(node=>{const item=node.getBoundingClientRect();return item.top>=parent.top-1&&item.bottom<=parent.bottom+1})
    }), `${viewport.name}: card option is vertically clipped by its dialog body`)
    ok((await page.locator('.prompt-card-candidate.horizontal').count())>0 && (await page.locator('.prompt-card-candidate:not(.horizontal)').count())>0, `${viewport.name}: mixed orientation fixture missing`)
    ok(cardsBefore.some(item=>item.disabled===false), `${viewport.name}: selectable card missing`)
    await page.locator('.prompt-card-candidate:not(.unavailable)').first().click()
    await assertEqualGroup(`${viewport.name} selected mixed card options`, '.prompt-card-candidate:not(.size-featured)', 8, true)

    await load(viewport, 'response-fixture=1')
    await assertEqualGroup(`${viewport.name} response targets`, '.response-target-row', 3)

    for (const mode of ['support','defense']) {
      await load(viewport, `${mode}=1`)
      await assertEqualGroup(`${viewport.name} ${mode} actions`, '.combat-resolution-panel .l12-actions>button', 2)
    }

    await load(viewport, '')
    await page.getByRole('button',{name:'打开对局工具'}).click()
    await page.locator('.tool-menu').waitFor()
    await assertEqualGroup(`${viewport.name} tool menu`, '.tool-menu>button', 4, viewport.mobile)

    if (viewport.mobile) {
      await load(viewport, 'morale-payment=1&runes=5&rune-usable=2&payment-actions=1')
      if (await page.locator('.prompt-minimize').count()) await page.locator('.prompt-minimize').click()
      await page.locator('.my-half .resource-morale-summary:visible').evaluate(node=>node.click())
      await page.locator('.mobile-morale-overlay').waitFor()
      await assertEqualGroup(`${viewport.name} morale resources`, '.mobile-morale-choice', 2)
      await assertEqualGroup(`${viewport.name} morale footer`, '.mobile-morale-actions>button', 2)
    }

    if (['desktop-1280x720','mobile-667x375','mobile-844x390'].includes(viewport.name)) {
      const file=`${viewport.name}-mixed-card-options.png`
      await load(viewport, 'card-choice=1&choice-count=8&mixed-availability=1&mixed-orientation=1')
      await page.screenshot({path:path.join(output,file)})
      report.screenshots.push(file)
    }
  }

  // Static coverage keeps less common placement/master/faction/payment groups
  // on the same geometry contract even when a fixture cannot lawfully open
  // every authoritative prompt in one synthetic game state.
  const sources = [
    fs.readFileSync(new URL('../src/l12/game/PromptOverlay.vue',import.meta.url),'utf8'),
    fs.readFileSync(new URL('../src/l12/game/GameActions.vue',import.meta.url),'utf8'),
    fs.readFileSync(new URL('../src/l12/game/MasterOverlay.vue',import.meta.url),'utf8'),
    fs.readFileSync(new URL('../src/l12/game/PlayerMat.vue',import.meta.url),'utf8'),
    fs.readFileSync(new URL('../src/l12/game/GameBoard.vue',import.meta.url),'utf8'),
    fs.readFileSync(new URL('../src/l12/game/BattleUtilityDock.vue',import.meta.url),'utf8'),
  ].join('\n')
  for (const contract of ['equal-card-option-group','equal-option-group','equal-action-group','equal-combat-action-group'])
    ok(sources.includes(contract), `static contract missing: ${contract}`)
  for (const token of ['.placement-buttons button{box-sizing:border-box;height:44px','.faction-effect-actions{grid-auto-rows:96px','.master-abilities{grid-auto-rows:96px','.board-target-controls button{box-sizing:border-box;width:112px'])
    ok(sources.includes(token), `static equal-size rule missing: ${token}`)

  report.status='passed'
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2))
  console.log(`Battle dialog option consistency passed: ${report.assertions} assertions, ${report.screenshots.length} screenshots`)
} finally {
  await browser.close()
}
