import fs from 'node:fs'
import assert from 'node:assert/strict'
import ts from 'typescript'
import { compile, createSSRApp } from 'vue'
import { renderToString } from 'vue/server-renderer'

const source = fs.readFileSync('src/l12/game/battlefieldTargetPresentation.ts', 'utf8')
const javascript = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const targetPresentation = await import(`data:text/javascript;base64,${Buffer.from(javascript).toString('base64')}`)

const hidden = { instanceId: 'covered-counter', name: '不应泄露的反击战术', hidden: true }
const firstGuard = { instanceId: 'guard-front', name: '陵墓守卫', hidden: false }
const secondGuard = { instanceId: 'guard-back', name: '陵墓守卫', hidden: false }
const game = {
  players: [
    { playerIndex: 0, field: [[null, firstGuard, null], [null, null, secondGuard]] },
    { playerIndex: 1, field: [[null, null, null], [hidden, null, null]] },
  ],
}

const ids = targetPresentation.battlefieldTargetIds(game, ['covered-counter', 'missing'])
if (JSON.stringify(ids) !== JSON.stringify(['covered-counter'])) throw new Error('盖伏战场实例被目标投影过滤')
for (const viewer of [0, 1]) {
  for (const player of game.players) {
    for (const row of [0, 1]) {
      for (const slot of [0, 1, 2]) {
        const expected = `${player.playerIndex === viewer ? '我方' : '对方'}${row === 0 ? '前排' : '后排'}${['左格', '中格', '右格'][slot]}`
        if (targetPresentation.battlefieldSlotLabel(player.playerIndex === viewer ? 'self' : 'opponent', row, slot) !== expected)
          throw new Error(`观看者 ${viewer} 的 ${expected} 与实际格位不一致`)
      }
    }
  }
}
if (targetPresentation.battlefieldTargetLabel(game, 0, hidden.instanceId, hidden.name) !== '对方后排左格')
  throw new Error('盖伏目标泄露身份或缺少稳定格位')
if (targetPresentation.battlefieldTargetLabel(game, 1, hidden.instanceId, hidden.name) !== '不应泄露的反击战术 · 我方后排左格')
  throw new Error('己方盖伏目标不应对其拥有者隐藏身份')
if (targetPresentation.battlefieldTargetLabel(game, 0, firstGuard.instanceId, firstGuard.name) !== '陵墓守卫 · 我方前排中格')
  throw new Error('同名公开目标缺少前排格位')
if (targetPresentation.battlefieldTargetLabel(game, 0, secondGuard.instanceId, secondGuard.name) !== '陵墓守卫 · 我方后排右格')
  throw new Error('同名公开目标缺少后排格位')
if (targetPresentation.battlefieldTargetLabel(game, 0, firstGuard.instanceId, '陵墓守卫 · 我方前排第2格') !== '陵墓守卫 · 我方前排中格')
  throw new Error('旧检查点的格位编号被带入当前候选')
if (targetPresentation.battlefieldTargetLabel(game, 0, firstGuard.instanceId, '陵墓守卫 · 我方前排左格') !== '陵墓守卫 · 我方前排中格')
  throw new Error('提示创建后目标移动，旧方位被带入当前候选')

const playerMat = fs.readFileSync('src/l12/game/PlayerMat.vue', 'utf8')
const prompt = fs.readFileSync('src/l12/game/PromptOverlay.vue', 'utf8')
const board = fs.readFileSync('src/l12/game/GameBoard.vue', 'utf8')
if (!playerMat.includes('battlefieldSlotLabel') || playerMat.includes('card && !card.hidden && card.instanceId && props.responseTargetIds'))
  throw new Error('PlayerMat 仍将目标位置错误绑定到卡牌身份可见性')
if (!prompt.includes('battlefieldTargetIds') || !prompt.includes('battlefieldTargetLabel'))
  throw new Error('PromptOverlay 未消费共享战场目标投影')
const positionFunction = prompt.match(/function positionFor\(id: string\) \{[\s\S]*?\n\}/)?.[0]
assert(positionFunction, '卡牌候选格位必须复用当前公开战场位置，不解析旧标签')
assert(prompt.includes(':position-label="positionFor(choice)"'), '真实候选必须传递格位显示标记')
for (const viewer of [0, 1]) {
  const positionFor = new Function('props', 'sandboxActorIndex', 'findBattlefieldTarget',
    `${positionFunction.replace('(id: string)', '(id)')};return positionFor`)(
      { game }, { value: viewer }, targetPresentation.findBattlefieldTarget)
  assert.equal(positionFor(firstGuard.instanceId), `${viewer === 0 ? '我方' : '对方'}前排中格`)
  assert.equal(positionFor(secondGuard.instanceId), `${viewer === 0 ? '我方' : '对方'}后排右格`)
  assert.equal(positionFor(hidden.instanceId), `${viewer === 1 ? '我方' : '对方'}后排左格`)
  assert.equal(positionFor('not-on-field'), '', '私区/非战场候选不得伪造战场格位')
}
const candidate = fs.readFileSync('src/l12/game/PromptCardCandidate.vue', 'utf8')
const mobileCss = fs.readFileSync('src/l12/mobileViewport.css', 'utf8')
const metadataTemplate = candidate.match(/<small class="prompt-card-candidate__meta"[\s\S]*?<\/small>/)?.[0]
assert(metadataTemplate, '真实候选元信息模板缺失')
const metadataRender = compile(metadataTemplate)
for (const positionLabel of ['', '我方后排右格']) {
  const html = await renderToString(createSSRApp({ data: () => ({ meta: '原有元信息', positionLabel }), render: metadataRender }))
  assert(html.includes('原有元信息'), '桌面原元信息不能被删除')
  assert.equal(html.includes('class="prompt-card-candidate__position"'), Boolean(positionLabel), '仅战场候选生成位置行')
  if (positionLabel) assert(html.includes(positionLabel))
}
assert(candidate.includes("'has-battlefield-position': Boolean(positionLabel)"))
assert(candidate.includes('.prompt-card-candidate.has-battlefield-position .prompt-card-candidate__meta{flex-shrink:0;font-size:var(--l12-board-meta,11px);line-height:1.3}'), '战场格位字形行高不得再次裁切')
assert(mobileCss.includes('.prompt-choice-panel .prompt-card-candidate__meta { display: none !important; }'), '非战场紧凑元信息规则保持')
assert(mobileCss.includes('.prompt-choice-panel .prompt-card-candidate.has-battlefield-position:not(.size-featured)'))
assert(mobileCss.includes('.prompt-choice-panel .has-battlefield-position .prompt-card-candidate__position { display: block; }'), '移动战场格位不能被通用隐藏覆盖')
if (!board.includes('boardTargetSelectionSummary') || !board.includes('battlefieldTargetLabel'))
  throw new Error('棋盘确认区未显示共享格位标签')

// Render the actual empty-slot expression and accessibility function, not a
// separately maintained label table. Both sides and rows share this template.
const emptySlot = playerMat.match(/<span v-else>\{\{ (.+?) \}\}<\/span>/)?.[1]
const accessibility = playerMat.match(/function slotAccessibilityLabel\(row: number, slot: number, card: Card \| null\) \{[\s\S]*?\n\}/)?.[0]
assert(emptySlot && accessibility, '战场空格位与可访问名称入口必须保持可检测')
assert.match(playerMat, /:aria-label="slotAccessibilityLabel\(row, slot, player\.field\[row\]\[slot\]\)"/)
const renderedSlot = compile(`<div class="formation-slot" role="button" :aria-label="slotAccessibilityLabel(row, slot, null)"><span>${'{{ ' + emptySlot + ' }}'}</span></div>`)
for (const side of ['my', 'opponent']) {
  const props = { side }
  const slotAccessibilityLabel = new Function('props', 'battlefieldSlotLabel', 'isResponseTarget',
    `${accessibility.replace('(row: number, slot: number, card: Card | null)', '(row, slot, card)')}; return slotAccessibilityLabel`)(props, targetPresentation.battlefieldSlotLabel, () => false)
  for (const row of [0, 1]) {
    for (const slot of [0, 1, 2]) {
      const shortLabel = ['左格', '中格', '右格'][slot]
      const fullLabel = `${side === 'my' ? '我方' : '对方'}${row === 0 ? '前排' : '后排'}${shortLabel}`
      const html = await renderToString(createSSRApp({
        data: () => ({ row, slot }),
        methods: { slotAccessibilityLabel },
        render: renderedSlot,
      }))
      assert.match(html, new RegExp(`aria-label="${fullLabel}"`))
      assert.match(html, new RegExp(`<span>${shortLabel}</span>`))
      assert(!html.includes(`<span>${fullLabel}</span>`), `格内不可重复展示${fullLabel}`)
    }
  }
}

console.log('battlefield target presentation tests passed')
