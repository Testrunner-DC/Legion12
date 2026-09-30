import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2] || 'http://127.0.0.1:5267/__l12_battle_preview__'
const out = path.resolve('../artifacts/player-prompt-copy/matrix')
fs.mkdirSync(out, { recursive: true })
const profiles = [[1920,1080,false],[1366,768,false],[390,844,true],[320,568,true],[568,320,true],[844,390,true]]
const cases = ['optional-short','optional-long','response-target','response-cost','mandatory','cancel-paid','information','legacy']
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []
try {
  for (const [width,height,mobile] of profiles) {
    const context = await browser.newContext({ viewport:{width,height}, hasTouch:mobile })
    const page = await context.newPage()
    page.setDefaultTimeout(8000)
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => ['localhost','127.0.0.1'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())
    await page.goto(`${base}?canvas=1&information-contract=1&field=full${mobile?'&mobile=1':''}`)
    await page.locator('.prompt-choice-panel').waitFor()
    for (const scenario of cases) {
      await page.evaluate(scenario => {
        const game = window.__l12State.game
        game.waitingPrompt = null
        const responseContext = '对手使用〈天诛〉。\n已选目标：你的后排右格〈陵墓守卫〉；声明时当前费用2。\n是否响应？'
        const long = Array.from({length:8}, (_,i) => `第${i+1}项：公开1张〈军团${i+1}〉，本回合兵力+1000；结束阶段失效。`).join('\n')
        const prompt = { promptId:scenario, playerIndex:game.you, kind:'optional', text:'〈帕西瓦尔〉：是否发动？', validChoices:['mode:use','mode:none'], minChoose:1,maxChoose:1,
          choiceLabels:{'mode:use':'发动','mode:none':'不发动'},data:{uiPattern:'effect-decision',choiceMode:'instant'},
          presentation:{title:'帕西瓦尔',situation:'弃置1张手牌：本回合，此军团兵力+2000。',instruction:'请选择一种处理方式。',waitingSummary:'对手正在决定是否发动效果',choiceConsequences:{'mode:use':'弃置1张手牌；本回合兵力+2000。','mode:none':'不发动本次可选效果。'}} }
        if(scenario==='optional-long') {
          prompt.presentation.situation += '\n' + long
          prompt.presentation.choiceConsequences['mode:use'] = '弃置1张手牌后，选择对方前排左格或后排右格的1张军团；本回合兵力+2000，结束阶段失效。已支付费用不返还。'
        }
        if(scenario.startsWith('response-')) {
          prompt.kind = scenario==='response-cost' ? 'discard-cost' : 'response'
          prompt.data = {responseContext, choiceMode:'instant'}
          prompt.presentation.title = scenario==='response-cost' ? '绝对防御' : '是否响应'
          prompt.presentation.situation = scenario==='response-cost' ? '弃置1张手牌，发动〈绝对防御〉。' : responseContext
          prompt.presentation.instruction = scenario==='response-cost' ? '选择1张手牌。' : '选择响应牌，或不响应。'
          prompt.validChoices=['respond','pass']; prompt.choiceLabels={respond:'发动',pass:'不响应'}
          prompt.presentation.choiceConsequences={respond:'响应所选效果。',pass:'不打出响应牌。'}
        }
        if(scenario==='mandatory' || scenario==='cancel-paid') {
          prompt.kind='option';prompt.data={};prompt.presentation.title='费用与目标'
          prompt.validChoices=scenario==='mandatory'?['a','b']:['a','cancel'];prompt.choiceLabels={a:'选择军团',b:'选择士气',cancel:'取消整次发动'}
          prompt.presentation.situation='从公开候选中选择1项。';prompt.presentation.instruction='请选择1项。'
          prompt.presentation.paymentStatus='paid';prompt.presentation.paymentSummary='已支付2士气'
          prompt.presentation.choiceConsequences={a:'选择我方前排左格〈陵墓守卫〉；下个结束阶段失效。',b:'返还1张士气。',cancel:'不发动；已支付2士气不返还。'}
        }
        if(scenario==='information') {
          prompt.kind='disaster-reveal';prompt.data={};prompt.validChoices=['confirm'];prompt.choiceLabels={confirm:'确认信息'}
          prompt.presentation.title='公开天灾';prompt.presentation.situation='本局公开〈诸神黄昏〉。双方确认后继续。';prompt.presentation.instruction='确认公开信息。';prompt.presentation.choiceConsequences={}
        }
        if(scenario==='legacy') {delete prompt.presentation; prompt.data.effectText='〈帕西瓦尔〉：弃置1张手牌，本回合兵力+2000。'}
        game.prompts=[prompt]
        window.__promptCopyInput=JSON.stringify(prompt)
      }, scenario)
      const panel = page.locator('.prompt-choice-panel:visible')
      await panel.waitFor()
      const text = await panel.innerText()
      assert.doesNotMatch(text,/权威处理|不发动本次可选效果|已支付：已支付/)
      if(scenario.startsWith('response-')) {
        assert.match(text,/已选目标：你的后排右格〈陵墓守卫〉；声明时当前费用2/)
        assert.equal(text.split('已选目标：').length,2)
      }
      if(scenario==='cancel-paid') assert.match(text,/已支付2士气不返还/)
      if(scenario==='optional-long') assert.match(text,/第8项.*结束阶段失效/s)
      const geometry = await panel.evaluate(panel => {
        const rect = panel.getBoundingClientRect()
        const options = [...panel.querySelectorAll('.prompt-choices > button')].map(node => {
          const r=node.getBoundingClientRect();return {text:node.textContent.trim(),width:r.width,height:r.height,clipped:node.scrollHeight>node.clientHeight+2}
        })
        const body=panel.querySelector('.prompt-choice-body'); const before=body.scrollTop;body.scrollTop=body.scrollHeight;const scrollable=body.scrollHeight>body.clientHeight+2;const scrolled=body.scrollTop>before;body.scrollTop=0
        const prose=panel.querySelector('.effect-decision-text');const proseScrollable=prose.scrollHeight>prose.clientHeight+2;prose.scrollTop=prose.scrollHeight;const proseScrolled=prose.scrollTop>0;prose.scrollTop=0
        const footer=[...panel.querySelectorAll('.prompt-action-footer > button')].map(node=>{const r=node.getBoundingClientRect();return {width:r.width,height:r.height,bottom:r.bottom}})
        return {x:rect.x,y:rect.y,right:rect.right,bottom:rect.bottom,options,footer,scrollable,scrolled,proseScrollable,proseScrolled}
      })
      assert(geometry.x>=-1&&geometry.y>=-1&&geometry.right<=width+1&&geometry.bottom<=height+1,`${width} ${scenario}: panel escaped`)
      for(const option of geometry.options) assert(!option.clipped,`${width} ${scenario}: clipped consequence: ${option.text}`)
      if(geometry.options.length>1) assert(geometry.options.every(item=>Math.abs(item.width-geometry.options[0].width)<1&&Math.abs(item.height-geometry.options[0].height)<1),`${width} ${scenario}: unequal option sizes`)
      if(geometry.footer.length>1) assert(geometry.footer.every(item=>Math.abs(item.width-geometry.footer[0].width)<1&&item.bottom<=height+1),`${width} ${scenario}: footer size/bounds`)
      if(geometry.scrollable) assert(geometry.scrolled,`${width} ${scenario}: inaccessible scroll body`)
      if(geometry.proseScrollable) assert(geometry.proseScrolled,`${width} ${scenario}: inaccessible long situation`)
      assert.equal(await page.evaluate(()=>JSON.stringify(window.__l12State.game.prompts[0])===window.__promptCopyInput),true,'display mutated authoritative prompt')
      await page.screenshot({path:path.join(out,`${width}x${height}-${scenario}.png`)})
      results.push({viewport:`${width}x${height}`,scenario,geometry})
    }
    await page.evaluate(()=>{
      const game=window.__l12State.game;game.prompts=[]
      game.matchGovernance={drawRequest:{id:'copy-draw',viewerCanRespond:true,status:'pending',requesterName:'对手',reason:'双方确认对局出现异常，申请以平局结束。\n'.repeat(15)}}
    })
    const draw=page.locator('.response-dialog:visible');await draw.waitFor()
    assert.match(await draw.innerText(),/接受：本局以平局结束。拒绝：继续对局。/)
    assert.doesNotMatch(await draw.innerText(),/权威|服务器|对局治理/)
    await draw.getByRole('button',{name:'接受平局',exact:true}).scrollIntoViewIfNeeded()
    const drawButtons=await draw.locator('footer button').evaluateAll(nodes=>nodes.map(node=>{const r=node.getBoundingClientRect();return {width:r.width,height:r.height,x:r.x,y:r.y,right:r.right,bottom:r.bottom}}))
    assert.equal(drawButtons.length,2)
    assert(drawButtons.every(button=>Math.abs(button.width-drawButtons[0].width)<1&&Math.abs(button.height-drawButtons[0].height)<1&&button.x>=-1&&button.y>=-1&&button.right<=width+1&&button.bottom<=height+1),`${width}: draw buttons unsafe/unequal`)
    await page.screenshot({path:path.join(out,`${width}x${height}-draw.png`)})
    results.push({viewport:`${width}x${height}`,scenario:'draw-confirmation',buttons:drawButtons})
    assert.deepEqual(errors,[])
    await context.close()
  }
  fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2))
  console.log(`Prompt copy browser matrix: ${results.length} scenes passed`)
} finally {await browser.close()}
