import assert from 'node:assert/strict'
import { projectLog } from '../src/l12/game/logViewModel.ts'

// Deterministic phase-0 evidence refreshed after Stages 4A, 4B-2, 4B-3A/B, and 4C-1.
// Closed gaps must remain visible; unfinished legacy/status gaps remain
// explicit; legacy combat audit text must never become a public reason.
const card = (name, instanceId = name) => ({
  instanceId, cardId: `ID-${instanceId}`, name, cardType: 'legion',
  faction: '测试', cost: 2, baseTroops: 6000, troops: 6000, disasterLevel: 0,
  hidden: false,
})
const source = card('来源', 'source')
const target = card('目标', 'target')
const event = (sequence, type, text, cards = [source], extra = {}) => ({
  sequence, type, text, cards, playerIndex: 0, ...extra,
})
function rendered(events) {
  return projectLog(events, 0, ['我方玩家', '对方玩家']).map(row => ({
    kind: row.kind,
    sequence: row.sequence,
    summary: row.kind === 'line'
      ? row.parts.map(part => part.text).join('') + row.badges.map(badge => badge.value).join('')
      : row.kind === 'combat' ? row.result : row.side,
    detail: row.kind === 'combat' || row.kind === 'line'
      ? (row.detail ?? []).map(line => line.parts.map(part => part.text).join('')).join(' / ')
      : '',
  }))
}
const group = {
  playerLogGroupId: 'effect:source:1',
  playerLogTiming: 'active',
}

const cases = [
  {
    id: 'legacy-move-origin-destination',
    input: [
      event(1, 'move', '〈来源〉从我方前排左格移动到我方后排左格'),
    ],
    requiredSourceFacts: ['我方前排左格', '我方后排左格'],
    missingFromProjection: ['我方前排左格', '我方后排左格'],
    status: 'LEGACY: no authoritative endpoints',
  },
  {
    id: 'structured-move-origin-destination',
    input: [
      event(1, 'move', '〈来源〉从我方前排左格移动到我方后排左格', [source], {
        playerBattlefieldMovement: { facts: [{ instanceId: source.instanceId,
          battlefieldPlayerIndex: 0, fromRow: 0, fromSlot: 0, toRow: 1, toSlot: 0 }] },
      }),
    ],
    requiredSourceFacts: ['我方前排左格', '我方后排左格'],
    expectedProjectionFacts: ['我方前排左格', '我方后排左格'],
    status: 'CLOSED: 4B-2',
  },
  {
    id: 'trojan-horse-cross-owner-put',
    input: [event(1, 'put', '特洛伊木马跨玩家安置，秘密来源格不公开',
      [{ ...card('特洛伊木马', 'horse'), ownerIndex: 1, tapped: true }], {
        playerIndex: 1,
        playerPublicPlacement: { instanceId: 'horse', ownerPlayerIndex: 1,
          controllerPlayerIndex: 0, row: 1, slot: 2, tapped: true,
          durationCode: 'until-owner-next-turn-end' },
      })],
    requiredSourceFacts: ['跨玩家'],
    expectedProjectionFacts: ['我方后排右格', '由我方控制', '直到对方下个回合结束'],
    missingFromProjection: ['秘密来源格'],
    status: 'CLOSED: 4B-3A',
  },
  {
    id: 'continuous-target-delta-duration',
    input: [
      event(1, 'continuous', '〈来源〉使〈目标〉本回合兵力由6000降至4000', [source, target]),
    ],
    requiredSourceFacts: ['目标', '6000', '4000', '本回合'],
    missingFromProjection: ['6000', '4000', '本回合'],
    status: 'LEGACY: no authoritative modifier facts; not a current producer',
  },
  {
    id: 'structured-this-turn-troops-modifier',
    input: [event(1, 'troops-modifier', '秘密来源／伪造兵力+999999', [target], {
      playerTroopsModifier: { targetInstanceId: target.instanceId,
        targetControllerPlayerIndex: 0, troopsDelta: -2000, durationCode: 'this-turn' },
    })],
    requiredSourceFacts: ['秘密来源'],
    expectedProjectionFacts: ['目标', '本回合', '-2000'],
    missingFromProjection: ['秘密来源', '999999'],
    status: 'CLOSED: 4B-3B (current producer)',
  },
  {
    id: 'skipped-segment',
    input: [
      event(1, 'effect', '〈来源〉发动主动效果', [source], group),
      event(2, 'effect-result', '〈来源〉没有合法处理对象，跳过该效果段', [source],
        { ...group, effectResultStatus: 'skipped', effectSegmentIndex: 1, effectSegmentCount: 2 }),
    ],
    requiredSourceFacts: ['跳过该效果段'],
    expectedProjectionFacts: ['跳过'],
    status: 'CLOSED: 4A',
  },
  {
    id: 'negated-paid-cost',
    input: [
      event(1, 'effect', '〈来源〉发动主动效果', [source], group),
      event(2, 'cost', '〈来源〉支付2士气', [source], group),
      event(3, 'effect-result', '〈来源〉的效果被无效', [source],
        { ...group, effectResultStatus: 'negated' }),
    ],
    requiredSourceFacts: ['支付2士气', '被无效'],
    expectedProjectionFacts: ['被无效'],
    missingFromProjection: ['士气 −2'],
    status: 'OPEN: 4C/log association',
  },
  {
    id: 'structured-negated-paid-cost',
    input: [
      event(1, 'effect', '〈来源〉发动主动效果', [source], group),
      event(2, 'cost', '〈来源〉支付2士气', [source], group),
      event(3, 'effect-result', '〈来源〉的效果被无效', [source], {
        ...group, effectResultStatus: 'negated',
        playerLogSemantic: { sourceInstanceId: source.instanceId,
          actionLabel: '效果结果', outcomeLabel: '已支付费用：消耗2士气' },
      }),
    ],
    requiredSourceFacts: ['支付2士气', '被无效'],
    expectedProjectionFacts: ['被无效', '费用已支付', '已支付费用：消耗2士气'],
    status: 'CLOSED: 4C-1 (authoritative receipt only)',
  },
  {
    id: 'defense-invalid-reason',
    input: [
      event(1, 'attack', '〈来源〉进攻〈目标〉', [source, target]),
      event(2, 'defense-invalid', '支援军团已离场，不能支援本次进攻', [target]),
    ],
    requiredSourceFacts: ['支援军团已离场'],
    missingFromProjection: ['支援军团已离场'],
    status: 'LEGACY: no authoritative reason',
  },
  {
    id: 'structured-defense-invalid-reason',
    input: [
      event(1, 'attack', '〈来源〉进攻〈目标〉', [source, target], { playerCombat: {
        combatId: 'reason-example', eventKind: 'attack', outcomeCode: 'declared',
      } }),
      event(2, 'defense-invalid', '支援军团已离场，后台候选不可公开', [], { playerCombat: {
        combatId: 'reason-example', eventKind: 'defense-invalid', outcomeCode: 'invalid-support',
        publicReasonCode: 'choice-unavailable',
      } }),
    ],
    requiredSourceFacts: ['支援军团已离场'],
    expectedProjectionFacts: ['支援无效', '所选抵挡或支援已无法使用'],
    missingFromProjection: ['后台候选'],
    status: 'CLOSED: 4B-1',
  },
]

for (const item of cases) {
  const sourceText = item.input.map(entry => entry.text).join(' / ')
  for (const fact of item.requiredSourceFacts)
    assert(sourceText.includes(fact), `${item.id}: source fixture missing ${fact}`)
  item.projection = rendered(item.input)
  const visible = item.projection.map(row => row.summary + ' / ' + row.detail).join(' / ')
  for (const fact of item.missingFromProjection ?? [])
    assert(!visible.includes(fact), `${item.id}: baseline gap changed for ${fact}`)
  for (const fact of item.expectedProjectionFacts ?? [])
    assert(visible.includes(fact), `${item.id}: completed player fact missing: ${fact}`)
}
console.log(JSON.stringify(cases.map(({ id, status, input, projection }) => ({
  id, status,
  source: input.map(entry => ({
    sequence: entry.sequence, type: entry.type, text: entry.text,
    effectResultStatus: entry.effectResultStatus,
  })),
  projection,
})), null, 2))
