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
