import fs from 'node:fs'
import ts from 'typescript'

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
if (targetPresentation.battlefieldTargetLabel(game, 0, hidden.instanceId, hidden.name) !== '对方后排第1格')
  throw new Error('盖伏目标泄露身份或缺少稳定格位')
if (targetPresentation.battlefieldTargetLabel(game, 1, hidden.instanceId, hidden.name) !== '不应泄露的反击战术 · 我方后排第1格')
  throw new Error('己方盖伏目标不应对其拥有者隐藏身份')
if (targetPresentation.battlefieldTargetLabel(game, 0, firstGuard.instanceId, firstGuard.name) !== '陵墓守卫 · 我方前排第2格')
  throw new Error('同名公开目标缺少前排格位')
if (targetPresentation.battlefieldTargetLabel(game, 0, secondGuard.instanceId, secondGuard.name) !== '陵墓守卫 · 我方后排第3格')
  throw new Error('同名公开目标缺少后排格位')

const playerMat = fs.readFileSync('src/l12/game/PlayerMat.vue', 'utf8')
const prompt = fs.readFileSync('src/l12/game/PromptOverlay.vue', 'utf8')
const board = fs.readFileSync('src/l12/game/GameBoard.vue', 'utf8')
if (!playerMat.includes('battlefieldSlotLabel') || playerMat.includes('card && !card.hidden && card.instanceId && props.responseTargetIds'))
  throw new Error('PlayerMat 仍将目标位置错误绑定到卡牌身份可见性')
if (!prompt.includes('battlefieldTargetIds') || !prompt.includes('battlefieldTargetLabel'))
  throw new Error('PromptOverlay 未消费共享战场目标投影')
if (!board.includes('boardTargetSelectionSummary') || !board.includes('battlefieldTargetLabel'))
  throw new Error('棋盘确认区未显示共享格位标签')

console.log('battlefield target presentation tests passed')
