import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const fixtures = JSON.parse(fs.readFileSync('../artifacts/player-prompt-copy/engine-fixtures.json', 'utf8'))
assert.equal(fixtures.length, 14, 'Require actual named engine snapshots, not browser-authored replacements')
const base = process.argv[2] || 'http://127.0.0.1:5267/__l12_battle_preview__'
const out = path.resolve('../artifacts/player-prompt-copy/engine-browser')
fs.mkdirSync(out, {recursive:true})
const results=[]
const browser=await chromium.launch({headless:true,channel:'msedge'})
try {
  for(const [width,height,mobile] of [[1440,900,false],[390,844,true],[568,320,true]]) {
    const context=await browser.newContext({viewport:{width,height},hasTouch:mobile})
    const page=await context.newPage()
    page.setDefaultTimeout(8000)
    const errors=[];page.on('pageerror',error=>errors.push(error.message))
    await page.route('**/*',route=>['127.0.0.1','localhost'].includes(new URL(route.request().url()).hostname)?route.continue():route.abort())
    for(const fixture of fixtures) {
      await page.goto(`${base}?canvas=1&information-contract=1${mobile?'&mobile=1':''}`)
      await page.locator('.prompt-choice-panel').waitFor()
      await page.evaluate(fixture=>{
        // Preview keeps the initial reactive object alive on a timer. Replace
        // its contents, not its identity; retain no synthetic gameplay fields.
        const target=window.__l12State.game
        for(const key of Object.keys(target)) if(!(key in fixture.game)) delete target[key]
        Object.assign(target,fixture.game)
        window.__promptEngineInput=JSON.stringify(fixture.game.prompts)
      },fixture)
      await page.evaluate(()=>new Promise(requestAnimationFrame))
      let panel=page.locator('.prompt-choice-panel:visible')
      if(!await panel.count()) {
        const inline=page.locator('.inline-prompt-controls:visible').first()
        await inline.waitFor()
        if(mobile) {await inline.getByRole('button',{name:'任务说明'}).click();panel=page.locator('.inline-prompt-info-body:visible')}
        else panel=inline.locator('.inline-prompt-copy')
      }
      await panel.waitFor()
      const text=await panel.innerText()
      const prompt=fixture.game.prompts[0]
      assert(text.includes(prompt.presentation.title),`${fixture.name}: source name missing; rendered=${text}; errors=${JSON.stringify(errors)}`)
      assert.doesNotMatch(text,/权威处理|权威结算|不发动本次可选效果|已支付：已支付/)
      if(fixture.name.startsWith('response-')) {assert.match(text,/已选目标：你的前排中格/);assert.equal(text.split('已选目标：').length,2)}
      // Remove only the server narrative's complete lifecycle preamble. Every
      // substantive fact thereafter remains verbatim (not a helper-derived oracle).
      const facts=prompt.presentation.situation.replace(/^〈[^〉]+〉的(?:登场时|晋升登场|阵亡时|进攻时)效果(?:正在结算|可以选择是否发动|正在选择登场对象|正在选择墓地回收对象)。/,'')
      assert(text.replace(/\s/g,'').includes(facts.replace(/\s/g,'')),`${fixture.name}: changed situation facts`)
      assert.doesNotMatch(text,/效果正在结算|结束本(?:次登场时)?效果|或选择[“「]不发动[”」]|不发动时/)
      if(/^(oddr-optional|joan-hand-cost|ring-optional|erik-grave)-/.test(fixture.name))
        assert.equal((text.match(/不发动/g)||[]).length,1,`${fixture.name}: decline must appear only on its button`)
      assert.equal(await page.evaluate(()=>JSON.stringify(window.__l12State.game.prompts)===window.__promptEngineInput),true)
      const rect=await panel.boundingBox()
      assert(rect && rect.x>=-1&&rect.y>=-1&&rect.x+rect.width<=width+1&&rect.y+rect.height<=height+1,`${fixture.name}: unsafe viewport geometry`)
      await page.screenshot({path:path.join(out,`${width}x${height}-${fixture.name}.png`)})
      results.push({viewport:`${width}x${height}`,scenario:fixture.name,kind:prompt.kind,rect})
    }
    assert.deepEqual(errors,[]);await context.close()
  }
  fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2))
  console.log(`Actual engine prompt browser matrix: ${results.length} scenes passed`)
} finally {await browser.close()}
