import assert from 'node:assert/strict'
import { performance } from 'node:perf_hooks'
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { buildReplayIndex, createReplayIndexAccumulator } from '../src/l12/replayIndex.ts'
import { parseReplayPayload, replayGameAt } from '../src/l12/replayModel.ts'
import { projectLog } from '../src/l12/game/logViewModel.ts'

const card = (name, hidden = false) => ({ InstanceId: 'source', CardId: 'CARD', Name: name, Hidden: hidden })
const event = (sequence, type = 'play', extra = {}) => ({ Sequence: sequence, Type: type, Cards: [card('公开卡')], ...extra })
const frame = (round, active, events, extra = {}) => ({ state: { Round: round, ActivePlayer: active, Phase: 'Main', Events: events, ...extra }, command: { sourceCardId: 'SECRET-COMMAND' }, accepted: true })
const detail = commands => ({ viewerPlayerIndex: 0, commands, match: {} })
const checks = []
const metrics = {}
const check = (name, run) => {
  try { run(); checks.push({ name, passed: true }) }
  catch (error) { checks.push({ name, passed: false, error: error.message }) }
}
check('existing privacy, legacy, positioning and bounded 10000-frame regressions', () => {
  const first = event(1)
  const result = buildReplayIndex(detail([frame(1, 0, [first]), frame(1, 0, [first, event(2, 'cost')]), frame(2, 1, [first, event(2, 'cost'), event(3, 'effect-result', { EffectResultStatus: 'negated' })])]))
  assert.deepEqual(result.rounds.map(x => [x.step, x.round, x.activePlayer]), [[0, 1, 0], [2, 2, 1]])
  assert.deepEqual(result.events.map(x => [x.step, x.category]), [[0, 'action'], [1, 'cost'], [2, 'effect']])
  assert.match(result.events[2].label, /被无效/)
  const secrets = buildReplayIndex(detail([
    frame(1, 0, [event(4, 'play', { Cards: [card('未来才公开', true)], Text: '隐藏名称私密正文' }), event(5, 'cost', { PrivateTo: 1, Text: '不应见到' }), event(6, 'effect-result', { EffectResultStatus: 'failed', Cards: [], PlayerLogSemantic: { SourceName: '秘密' } })]),
    frame(2, 1, [event(4, 'play', { Cards: [card('未来才公开')] })]),
  ]))
  assert(!JSON.stringify(secrets).includes('未来才公开'))
  assert(!JSON.stringify(secrets).includes('私密正文'))
  assert(!JSON.stringify(secrets).includes('秘密'))
  assert(!JSON.stringify(secrets).includes('SECRET-COMMAND'))
  assert.equal(secrets.events.length, 2)
  const privileged = buildReplayIndex(detail([frame(1, 0, [event(7, 'counter-set', { Cards: [card('盖伏卡', true)] })])]), true)
  assert.match(privileged.events[0].label, /盖伏卡/)
  const legacyA = event(0, 'play')
  const legacyB = event(0, 'cost')
  const legacy = buildReplayIndex(detail([frame(1, 0, [legacyA]), frame(1, 0, [legacyA, legacyB]), frame(2, 1, [legacyB, legacyA])]))
  assert.deepEqual(legacy.events.map(x => x.step), [0, 1, 2], 'rolling legacy windows retain a new identical action, not a global text dedupe')
  assert.deepEqual(buildReplayIndex(detail([{ state: {} }, frame(1, 0, [], { Round: undefined })])).rounds, [])
  const camel = buildReplayIndex(detail([{ state: { round: 3, activePlayer: 0, events: [{ sequence: 1, type: 'attack', cards: [] }] } }]))
  assert.equal(camel.rounds[0].round, 3)
  assert.equal(camel.events.length, 1)
  for (const phase of [10, 'GameOver']) assert.equal(buildReplayIndex(detail([frame(1, 0, [], { Phase: phase })])).rounds.length, 0, 'terminal frame alone is not a turn start')
  const rejected = buildReplayIndex(detail([frame(1, 0, [event(10)]), { ...frame(1, 0, [event(10)]), accepted: false, error: 'SECRET-ERROR' }]))
  assert.equal(rejected.events.length, 1)
  const many = Array.from({ length: 10000 }, (_, step) => frame(1 + Math.floor(step / 20), step % 2, Array.from({ length: Math.min(64, step + 1) }, (_, offset) => event(step - Math.min(63, step) + offset + 1))))
  const started = performance.now()
  const large = buildReplayIndex(detail(many))
  const elapsed = performance.now() - started
  assert.equal(large.events.length, 10000)
  assert(elapsed < 2500, `10000-frame index exceeded budget: ${elapsed.toFixed(1)}ms`)
  metrics.largeFramesMs = elapsed
})

const commandsFor = (left, right, sameFrame = false) => sameFrame
  ? [frame(1, 0, [left, right], { MatchId: 'synthetic-hr05' })]
  : [frame(1, 0, [left], { MatchId: 'synthetic-hr05' }), frame(1, 0, [right], { MatchId: 'synthetic-hr05' })]
const claims = {
  type: [event(41, 'play', { PlayerIndex: 0, Text: '合成打出' }), event(41, 'attack', { PlayerIndex: 0, Text: '合成进攻' })],
  actor: [event(41, 'play', { PlayerIndex: 0 }), event(41, 'play', { PlayerIndex: 1 })],
  text: [event(41, 'play', { Text: '合成完成' }), event(41, 'play', { Text: '合成未完成' })],
  outcome: [event(41, 'effect-result', { EffectResultStatus: 'resolved' }), event(41, 'effect-result', { EffectResultStatus: 'negated' })],
  source: [event(41, 'play', { Cards: [card('合成甲')] }), event(41, 'play', { Cards: [{ ...card('合成乙'), InstanceId: 'other', CardId: 'OTHER' }] })],
}
for (const [kind, pair] of Object.entries(claims)) {
  for (const reverse of [false, true]) {
    const [left, right] = reverse ? [...pair].reverse() : pair
    for (const sameFrame of [true, false]) check(`${kind} conflict ${reverse ? 'reverse' : 'forward'} ${sameFrame ? 'same frame' : 'cross frame'}`, () => {
      assert.deepEqual(buildReplayIndex(detail(commandsFor(left, right, sameFrame))).events, [])
    })
    check(`${kind} conflict ${reverse ? 'reverse' : 'forward'} late page retracts without resurrection`, () => {
      const builder = createReplayIndexAccumulator(0)
      const commands = commandsFor(left, right)
      builder.append(commands.slice(0, 1), 0)
      const exposedIndex = builder.index
      assert.equal(exposedIndex.events.length, 1)
      builder.append(commands.slice(1), 1)
      assert.deepEqual(exposedIndex.events, [], 'the already-exposed accumulator must retract a late conflicting claim')
      builder.append([frame(2, 1, [left], { MatchId: 'synthetic-hr05' })], 2)
      assert.deepEqual(exposedIndex.events, [], 'an old agreeing copy must not resurrect an ambiguous sequence')
      assert.deepEqual(exposedIndex, buildReplayIndex(detail([...commands, frame(2, 1, [left], { MatchId: 'synthetic-hr05' })])))
    })
  }
}
for (const reverse of [false, true]) check(`actual import -> replayGameAt -> projectLog consumer ${reverse ? 'reverse' : 'forward'}`, () => {
  const pair = reverse ? [...claims.type].reverse() : claims.type
  const imported = parseReplayPayload({ format: 'legion12-replay', version: 1,
    detail: { ...detail(commandsFor(...pair, true)), match: { matchId: 'synthetic-hr05' } } })
  const game = replayGameAt(imported, 0)
  assert.equal(game.recentEvents.length, 2, 'the real parser accepts both copies; this is not prefiltered test input')
  assert.deepEqual(projectLog(game.recentEvents, 0, ['合成甲', '合成乙']), [])
  assert.deepEqual(buildReplayIndex(imported).events, [], 'the index must not advertise either action that the current battle-log consumer suppresses')
})
for (const reverse of [false, true]) check(`consistent duplicate and known/unknown field enrichment ${reverse ? 'reverse' : 'forward'}`, () => {
  const base = event(51, 'play', { PlayerIndex: 0, Text: '合成打出' })
  const rich = { sequence: 51, type: 'play', playerIndex: 0, text: '合成打出', cards: [{ instanceId: 'source', cardId: 'CARD', name: '公开卡', hidden: false, unrelatedExtension: 'new' }],
    effectText: '新增说明', playerLogGroupId: 'new-group', effectResultStatus: 'resolved', unrelatedExtension: 'new',
    playerLogSemantic: { actionLabel: '发动效果', outcomeLabel: '完成', sourceInstanceId: 'source' } }
  const pair = reverse ? [rich, base] : [base, rich]
  const commands = commandsFor(...pair)
  const whole = buildReplayIndex(detail(commands))
  assert.equal(whole.events.length, 1)
  assert.equal(whole.events[0].step, 0, 'retain original frame position, not a later enriched copy')
  const builder = createReplayIndexAccumulator(0)
  commands.forEach((command, step) => builder.append([command], step))
  assert.deepEqual(builder.index, whole)
  assert.equal(buildReplayIndex(detail(commandsFor(base, structuredClone(base), true))).events.length, 1)
})
for (const reverse of [false, true]) check(`structured semantic settles old/new wording ${reverse ? 'reverse' : 'forward'}`, () => {
  const old = event(61, 'play', { PlayerIndex: 0, Text: '旧版表述' })
  const rich = event(61, 'play', { PlayerIndex: 0, Text: '新版表述', PlayerLogSemantic: { ActionLabel: '打出', OutcomeLabel: '完成', SourceInstanceId: 'source' } })
  const commands = commandsFor(...(reverse ? [rich, old] : [old, rich]), true)
  assert.equal(buildReplayIndex(detail(commands)).events.length, 1, 'a valid structured receipt settles wording; arbitrary JSON inequality is not a conflict')
})
check('same sequence in distinct matches is isolated, including A/B/A late conflict', () => {
  const builder = createReplayIndexAccumulator(0)
  builder.append([frame(1, 0, [event(71)], { MatchId: 'synthetic-A' })], 0)
  builder.append([frame(1, 0, [event(71, 'attack')], { matchId: 'synthetic-B' })], 1)
  assert.deepEqual(builder.index.events.map(item => [item.step, item.category]), [[0, 'action'], [1, 'action']])
  assert.deepEqual(builder.index.rounds.map(item => item.step), [0, 1], 'a different match does not inherit the previous turn marker')
  builder.append([frame(1, 0, [event(71, 'move')], { MatchId: 'synthetic-A' })], 2)
  assert.deepEqual(builder.index.events.map(item => item.step), [1], 'only A is ambiguous; B remains visible')
})
check('partial source metadata, extension changes and future public identity do not invent conflict or leak', () => {
  const partial = event(81, 'play', { Cards: [{ CardId: 'CARD', Name: '公开卡' }] })
  const richer = event(81, 'play', { Cards: [{ ...card('公开卡'), ImageUrl: 'synthetic.png', Troops: 10 }] })
  for (const pair of [[partial, richer], [richer, partial]]) assert.equal(buildReplayIndex(detail(commandsFor(...pair))).events.length, 1)
  const concealed = event(82, 'play', { Cards: [{ CardId: 'hidden-card', Name: '隐藏卡牌', Hidden: true }] })
  const shown = event(82, 'play', { Cards: [card('未来才公开')] })
  const index = buildReplayIndex(detail(commandsFor(concealed, shown)))
  assert.equal(index.events.length, 1)
  assert(!JSON.stringify(index).includes('未来才公开'))
})
for (const reverse of [false, true]) check(`structured movement settles old/new wording ${reverse ? 'reverse' : 'forward'}`, () => {
  const old = event(91, 'move', { Text: '旧版位移文字' })
  const rich = event(91, 'move', { Text: '新版位移文字', PlayerBattlefieldMovement: { Facts: [{
    InstanceId: 'source', BattlefieldPlayerIndex: 0, FromRow: 0, FromSlot: 0, ToRow: 1, ToSlot: 2,
    UnknownExtension: 'ignored',
  }] } })
  const pair = reverse ? [rich, old] : [old, rich]
  for (const sameFrame of [true, false]) assert.equal(buildReplayIndex(detail(commandsFor(...pair, sameFrame))).events.length, 1)
  const builder = createReplayIndexAccumulator(0)
  commandsFor(...pair).forEach((command, step) => builder.append([command], step))
  assert.equal(builder.index.events.length, 1)
  const invalid = { ...rich, PlayerBattlefieldMovement: { Facts: [{ InstanceId: 'source' }] } }
  assert.equal(buildReplayIndex(detail(commandsFor(old, invalid, true))).events.length, 0, 'an empty or malformed fact cannot settle contradictory wording')
  const unnamed = { ...rich, Cards: [card('')] }
  assert.equal(buildReplayIndex(detail(commandsFor({ ...old, Cards: [card('')] }, unnamed, true))).events.length, 0, 'an explicitly unnamed card is not a usable movement fact in the current log projection')
})
check('late structured wording receipt restores only the original row, then incompatible facts retract it', () => {
  const first = event(101, 'play', { Text: '旧版' })
  const second = event(101, 'play', { Text: '新版' })
  const fact = { ActionLabel: '打出', OutcomeLabel: '完成', SourceInstanceId: 'source' }
  const commands = [frame(1, 0, [first, event(102)]), frame(1, 0, [second]),
    frame(1, 0, [{ ...second, PlayerLogSemantic: fact }]),
    frame(1, 0, [{ ...second, PlayerLogSemantic: { ...fact, OutcomeLabel: '未完成' } }])]
  const builder = createReplayIndexAccumulator(0)
  builder.append(commands.slice(0, 2), 0)
  assert.deepEqual(builder.index.events.map(item => item.id), ['event-0-1'])
  builder.append(commands.slice(2, 3), 2)
  assert.deepEqual(builder.index.events.map(item => item.id), ['event-0-0', 'event-0-1'])
  builder.append(commands.slice(3), 3)
  assert.deepEqual(builder.index.events.map(item => item.id), ['event-0-1'])
  assert.deepEqual(builder.index, buildReplayIndex(detail(commands)))
})
check('public source order and unknown card/event extensions do not create a conflict', () => {
  const first = event(111, 'play', { Cards: [card('甲'), { ...card('乙'), InstanceId: 'other', CardId: 'OTHER' }] })
  const second = { ...first, Cards: first.Cards.map(item => ({ ...item, UnknownExtension: 'new' })).reverse(), UnknownExtension: 'new' }
  assert.equal(buildReplayIndex(detail(commandsFor(first, second, true))).events.length, 1)
})
check('mixed opaque/public card enrichment keeps the old safe label, while authorized hidden identities cannot conflict', () => {
  const first = event(115, 'play', { Cards: [card('公开卡'), { CardId: 'hidden-card', Name: '隐藏卡牌', Hidden: true }] })
  const shown = event(115, 'play', { Cards: [card('公开卡'), { ...card('未来才公开'), CardId: 'OTHER', InstanceId: 'other' }] })
  const index = buildReplayIndex(detail(commandsFor(first, shown)))
  assert.equal(index.events.length, 1)
  assert.match(index.events[0].label, /公开卡/)
  assert(!JSON.stringify(index).includes('未来才公开'))
  const left = event(116, 'counter-set', { Cards: [card('盖伏甲', true)] })
  const right = event(116, 'counter-set', { Cards: [{ ...card('盖伏乙', true), InstanceId: 'other', CardId: 'OTHER' }] })
  for (const pair of [[left, right], [right, left]]) {
    assert.deepEqual(buildReplayIndex(detail(commandsFor(...pair, true)), true).events, [])
    assert.equal(buildReplayIndex(detail(commandsFor(...pair, true))).events.length, 1, 'unseen identities do not create public index assertions')
  }
})
for (const reverse of [false, true]) check(`omitted match identity is legacy enrichment ${reverse ? 'reverse' : 'forward'}, not a second match`, () => {
  const commands = [frame(1, 0, [event(121)]), frame(1, 0, [event(121)], { MatchId: 'synthetic-A' })]
  if (reverse) commands.reverse()
  const builder = createReplayIndexAccumulator(0)
  commands.forEach((command, step) => builder.append([command], step))
  assert.equal(builder.index.events.length, 1)
  assert.equal(builder.index.rounds.length, 1)
  builder.append([frame(1, 0, [event(121, 'attack')])], 2)
  assert.equal(builder.index.events.length, 0, 'an omitted ID must not evade conflict with its known surrounding match')
})
check('8192-card compatible/conflicting repeated copies stay within a separate bounded matching budget', () => {
  const count = 8192
  const full = Array.from({ length: count }, (_, position) => position % 2
    ? { ...card('合成乙'), InstanceId: 'other', CardId: 'OTHER' } : card('合成甲'))
  const partial = Array.from({ length: count }, () => ({ CardId: 'CARD' }))
  const enriched = Array.from({ length: count }, () => card('合成甲'))
  const conflicting = [...enriched.slice(0, -1), { ...card('合成矛盾'), InstanceId: 'other', CardId: 'OTHER' }]
  const ambiguous = [...Array.from({ length: count - 1 }, () => ({})), { CardId: 'CARD' }]
  const rerouted = [...Array.from({ length: count - 1 }, () => ({ CardId: 'CARD' })), { CardId: 'OTHER' }]
  const capacityLeft = Array.from({ length: count }, (_, position) => position < count / 2 ? { CardId: 'CARD' } : {})
  const capacityRight = Array.from({ length: count }, (_, position) => ({ CardId: position < count / 4 ? 'CARD' : 'OTHER' }))
  const pairs = [[full, [...full].reverse(), 1], [partial, enriched, 1],
    [enriched, conflicting, 0], [partial, conflicting, 0], [ambiguous, rerouted, 1], [capacityLeft, capacityRight, 0]]
  const started = performance.now()
  try {
    for (const [leftCards, rightCards, expected] of pairs) for (const reverse of [false, true]) {
      const pair = [event(131, 'play', { Cards: leftCards }), event(131, 'play', { Cards: rightCards })]
      if (reverse) pair.reverse()
      const builder = createReplayIndexAccumulator(0)
      const commands = commandsFor(...pair)
      builder.append(commands.slice(0, 1), 0)
      builder.append(commands.slice(1), 1)
      assert.equal(builder.index.events.length, expected)
      builder.append([frame(1, 0, [pair[0]])], 2)
      assert.equal(builder.index.events.length, expected, 'repeated old copies do not resurrect conflicting large claims')
    }
    const elapsed = performance.now() - started
    assert(elapsed < 2000, `8192-card duplicate matching exceeded 2000ms budget: ${elapsed.toFixed(1)}ms`)
  } finally { metrics.largeCardCopiesMs = performance.now() - started }
})
check('counted identity matching agrees with a bounded independent exhaustive-pairing oracle', () => {
  const fields = ['InstanceId', 'CardId', 'Name']
  const profiles = Array.from({ length: 27 }, (_, value) => Object.fromEntries(fields.flatMap((field, position) => {
    const digit = Math.floor(value / 3 ** position) % 3
    return digit ? [[field, `${field}-${digit}`]] : []
  })))
  const agrees = (left, right) => Object.entries(left).every(([key, value]) => right[key] == null || right[key] === value)
  function possible(left, right) {
    if (!left.length) return true
    return right.some((candidate, position) => agrees(left[0], candidate)
      && possible(left.slice(1), right.filter((_, index) => index !== position)))
  }
  const cases = profiles.flatMap(left => profiles.map(right => [[left], [right]]))
  let seed = 0x48523035
  const next = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed }
  for (let attempt = 0; attempt < 512; attempt++) {
    const length = next() % 4 + 1
    cases.push([Array.from({ length }, () => profiles[next() % profiles.length]),
      Array.from({ length }, () => profiles[next() % profiles.length])])
  }
  for (const [left, right] of cases) {
    const expected = possible(left, right) ? 1 : 0
    for (const pair of [[left, right], [right, left]]) {
      const events = pair.map(Cards => event(141, 'play', { Cards }))
      assert.equal(buildReplayIndex(detail(commandsFor(...events, true))).events.length, expected,
        `identity matching disagreed with exhaustive recorded-field compatibility: ${JSON.stringify(pair)}`)
    }
  }
  metrics.cardMatchingOracleCases = cases.length * 2
})
const hash = path => createHash('sha256').update(readFileSync(new URL(path, import.meta.url))).digest('hex').toUpperCase()
const report = { suite: 'replay-index-hr05', sourceSha256: hash('../src/l12/replayIndex.ts'), testSha256: hash('./test-replay-index.mjs'),
  passed: checks.filter(check => check.passed).length, failed: checks.filter(check => !check.passed).length, metrics, checks }
console.log(JSON.stringify(report, null, 2))
if (report.failed) process.exitCode = 1
