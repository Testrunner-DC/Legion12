import assert from 'node:assert/strict'
import { projectLog } from '../src/l12/game/logViewModel.ts'

// Deterministic phase-0 projection evidence. All five inputs include the
// claimed source fact; this script asserts that the current player projection
// omits or blurs it. It intentionally does not change product behavior.
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
    detail: row.kind === 'combat'
      ? row.detail.map(line => line.parts.map(part => part.text).join('')).join(' / ')
      : '',
  }))
}
const group = {
  playerLogGroupId: 'effect:source:1',
  playerLogTiming: 'active',
}

const cases = [
  {
    id: 'move-origin-destination',
    input: [
      event(1, 'move', '〈来源〉从我方前排左格移动到我方后排左格'),
    ],
    requiredSourceFacts: ['我方前排左格', '我方后排左格'],
    missingFromProjection: ['我方前排左格', '我方后排左格'],
  },
  {
    id: 'continuous-target-delta-duration',
    input: [
      event(1, 'continuous', '〈来源〉使〈目标〉本回合兵力由6000降至4000', [source, target]),
    ],
    requiredSourceFacts: ['目标', '6000', '4000', '本回合'],
    missingFromProjection: ['6000', '4000', '本回合'],
  },
  {
    id: 'skipped-segment',
    input: [
      event(1, 'effect', '〈来源〉发动主动效果', [source], group),
      event(2, 'effect-result', '〈来源〉没有合法处理对象，跳过该效果段', [source],
        { ...group, effectResultStatus: 'skipped', effectSegmentIndex: 1, effectSegmentCount: 2 }),
    ],
    requiredSourceFacts: ['跳过该效果段'],
    missingFromProjection: ['跳过'],
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
    missingFromProjection: ['2士气', '士气 −2'],
  },
  {
    id: 'defense-invalid-reason',
    input: [
      event(1, 'attack', '〈来源〉进攻〈目标〉', [source, target]),
      event(2, 'defense-invalid', '支援军团已离场，不能支援本次进攻', [target]),
    ],
    requiredSourceFacts: ['支援军团已离场'],
    missingFromProjection: ['支援军团已离场'],
  },
]

for (const item of cases) {
  const sourceText = item.input.map(entry => entry.text).join(' / ')
  for (const fact of item.requiredSourceFacts)
    assert(sourceText.includes(fact), `${item.id}: source fixture missing ${fact}`)
  item.projection = rendered(item.input)
  const visible = item.projection.map(row => row.summary + ' / ' + row.detail).join(' / ')
  for (const fact of item.missingFromProjection)
    assert(!visible.includes(fact), `${item.id}: baseline gap changed for ${fact}`)
}
console.log(JSON.stringify(cases.map(({ id, input, projection }) => ({
  id,
  source: input.map(entry => ({
    sequence: entry.sequence, type: entry.type, text: entry.text,
    effectResultStatus: entry.effectResultStatus,
  })),
  projection,
})), null, 2))
