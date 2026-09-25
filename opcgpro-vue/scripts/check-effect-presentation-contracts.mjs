import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const backendRoot = new URL('../../服务端WebSocket/TwelveLegions/', import.meta.url)
const backendFiles = readdirSync(backendRoot)
  .filter(name => name.endsWith('.cs'))
  .map(name => ({ name, source: read(`../../服务端WebSocket/TwelveLegions/${name}`) }))
const backend = backendFiles.map(file => file.source).join('\n')
const admin = read('../src/l12/site/AdminEffectWorkbenchPanel.vue')
const board = `${read('../src/l12/game/GameBoard.vue')}\n${read('../src/l12/game/GameBoard.mobile.css')}`
const eventLog = read('../src/l12/game/BattleEventLog.vue')
const eventLogViewModel = read('../src/l12/game/logViewModel.ts')
const actionLayer = read('../src/l12/game/ActionPresentationLayer.vue')
const actionPresentation = read('../src/l12/game/actionPresentation.ts')
const zoneMovement = read('../src/l12/game/ZoneMovementPresentationLayer.vue')
const combatMotion = read('../src/l12/game/CombatMotionPresentationLayer.vue')
const phasePlayback = read('../src/l12/game/PhasePlayback.vue')
const visualTransitionProjection = read('../src/l12/game/visualTransitionProjection.ts')
const platform = read('../src/l12/platform.ts')
const store = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.EffectPresentations.cs')
const model = read('../../服务端WebSocket/TwelveLegions/EffectPresentationTexts.cs')
const operations = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.Operations.cs')

const runtimePresentation = `${board}\n${visualTransitionProjection}`
for (const contract of [
  'data-ui-contract="effect-workbench"',
  'data-ui-contract="effect-workbench-preview"',
  'draft.scenes.length',
  'v-model="scene.text"',
  'v-model="scene.styleId"',
  'v-model="scene.publicLevel"',
  "performAction(kind: 'validate' | 'review' | 'publish')",
  'publishEffectWorkbench',
  'rollbackEffectWorkbench',
  'white-space:pre-wrap',
]) assert(admin.includes(contract), `Admin effect workbench is missing ${contract}`)
assert(platform.includes('presentations?: EffectPresentationScene[]'))
for (const metadataField of [
  'flow?: string | null',
  'segmentIndex?: number | null',
  'segmentCount?: number | null',
  'branchLabel?: string | null',
  'requiredChoices?: Record<string, string> | null',
]) assert(platform.includes(metadataField), `Effect presentation scene type is missing ${metadataField}`)
assert(platform.includes('/presentations/${encodeURIComponent(sceneId)}'))
assert(platform.includes('/presentations/${encodeURIComponent(sceneId)}/restore'))
assert(admin.includes('maxlength="2000"') && admin.includes('cloneDraft(result.draft)'),
  'The workbench must preserve authoritative multiline scene drafts without flattening them')
assert([board, actionLayer, actionPresentation, zoneMovement, combatMotion, phasePlayback]
  .every(source => !source.includes('effect-announced')),
  'Recorded whole-effect announcements must not enter any frontend animation queue')
assert(eventLogViewModel.includes("'effect-announced'") && eventLogViewModel.includes('PLAYER_LOG_HIDDEN_TYPES'),
  'Recorded whole-effect announcements must remain authoritative history while the player projection explicitly hides them')

const overrideGuard = board.indexOf('if (override) {')
const oiranFallback = board.indexOf('/花魁的馈赠/.test(text)')
assert(overrideGuard >= 0 && overrideGuard < oiranFallback,
  'Authoritative override must win before the legacy Oiran compatibility fallback')
assert(board.includes('.public-reveal-animation strong{') && board.includes('white-space:pre-wrap;overflow-wrap:anywhere'))
assert(eventLog.includes('.event-effect{') && eventLog.includes('white-space:pre-wrap'))
assert(actionLayer.includes('.l12-action-presentation .action-copy strong{white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-word;text-overflow:clip}'),
  '对局动效公告必须保留后台手动换行，并对超长单行安全折行')
for (const contract of [
  "event.type === 'effect-result'",
  "event.effectResultStatus !== 'declared'",
  "event.effectResultStatus === 'negated'",
  "event.effectResultStatus === 'skipped'",
  "event.effectResultStatus === 'failed'",
]) assert(runtimePresentation.includes(contract), `Battle animation settlement projection is missing ${contract}`)
assert(eventLogViewModel.includes("'effect-result'") && eventLogViewModel.includes("'effect-declined'")
  && eventLogViewModel.includes('PLAYER_LOG_HIDDEN_TYPES'),
  'Player log must explicitly suppress settlement internals and declined effects while authority history remains intact')
assert(backend.includes('"effect-declined" => "declined"'),
  'Backend event and stack projections must share the declined result status')
const replay = read('../src/l12/replayModel.ts')
for (const field of [
  'EffectText', 'EffectSceneId', 'EffectAbilityId', 'EffectSegmentId', 'EffectSegmentIndex',
  'EffectSegmentCount', 'EffectBranchId', 'EffectBranchLabel', 'EffectResultStatus', 'PlayerLogSemantic',
]) assert(replay.includes(field), `Replay projection is missing ${field}`)
for (const contract of [
  'AddEffectResultEvent(item, resultStatus)',
  'effectResultPublished',
  'effectResultStatus',
  'BuildEffectEventMetadata(configured, resultStatus)',
]) assert(backend.includes(contract), `Backend settlement-result contract is missing ${contract}`)

assert(model.includes('.Where(scene => scene.Overridden)'), 'Only manual overrides may be frozen into a match')
assert(store.includes('.GroupBy(row => row.SceneId'), 'Malformed legacy duplicate rows must not break reads')
assert(operations.includes('OptionalOperationsMultilineText(item.Content, 1000)'))
assert(operations.includes("character != '\\n' && char.IsControl(character)"))

const directRevealCount = (backend.match(/AddEvent\(\s*"reveal"/g) ?? []).length
assert.equal(directRevealCount, 3,
  'Every new card-producing reveal must use AddPresentationEvent; update the explicit scene catalog or exemption list')
for (const exemption of [
  '刘备检索未命中，向对手展示牌库',
  '〈荣耀之路〉查看牌库但未找到【奥林匹斯】卡牌',
  '在触发效果入栈时翻开',
]) assert(backend.includes(exemption), `Documented non-card-text reveal exemption disappeared: ${exemption}`)
assert.equal((backend.match(/AddEvent\(\s*"disaster-reveal"/g) ?? []).length, 1,
  'Disaster reveal is the sole generic system animation exemption')
assert.equal((backend.match(/AddEvent\(\s*"(?:search|hidden-reveal|effect-activation|effect-trigger|effect-response)"/g) ?? []).length, 0,
  'Card animation events must resolve an explicit presentation scene')

function callsOf(source, functionName) {
  const calls = []
  let start = 0
  while ((start = source.indexOf(`${functionName}(`, start)) >= 0) {
    let depth = 0
    let quoted = false
    let escaped = false
    let cursor = start + functionName.length
    for (; cursor < source.length; cursor += 1) {
      const character = source[cursor]
      if (quoted) {
        if (escaped) escaped = false
        else if (character === '\\') escaped = true
        else if (character === '"') quoted = false
      } else if (character === '"') quoted = true
      else if (character === '(') depth += 1
      else if (character === ')' && --depth === 0) {
        calls.push(source.slice(start, cursor + 1))
        cursor += 1
        break
      }
    }
    start = cursor
  }
  return calls
}
const publisherCalls = backendFiles.flatMap(file => [
  ...callsOf(file.source, 'AddPresentationEvent'),
  ...callsOf(file.source, 'PubliclyRevealThenAddCardToHandByEffect'),
  ...callsOf(file.source, 'PubliclyRevealThenMoveLibraryCardToHandByEffect'),
].map(call => ({ file: file.name, call })))
const delegatedPublishers = new Map([
  ['S01-02D1|search-hit', { file: 'L12S1FactionEffects.cs', producer: '"S01-02D1", "top-three"', scene: 'item.SourceCardId, "search-hit"' }],
  ['S02-0514|search-hit', { producerFile: 'L12S2FactionEffects.cs', sceneFile: 'L12S1FactionEffects.cs', producer: 'BeginFactionTopSearch(item, 3, "olympus", "S02-0514"', scene: 'item.SourceCardId, "search-hit"' }],
])
const explicitPairs = [...model.matchAll(/Scene\("([^"]+)",\s*(?:null|"[^"]+"),\s*"([^"]+)"/g)]
  .map(match => ({ cardId: match[1], sceneKey: match[2] }))
assert(explicitPairs.length >= 40, 'Full-card scan unexpectedly lost explicit animation scenes')
for (const { cardId, sceneKey } of explicitPairs) {
  if (publisherCalls.some(({ call }) => call.includes(`"${cardId}"`) && call.includes(`"${sceneKey}"`))) continue
  const evidence = delegatedPublishers.get(`${cardId}|${sceneKey}`)
  assert(evidence, `Explicit scene ${cardId}/${sceneKey} has no matching authoritative publisher call`)
  const producerSource = backendFiles.find(file => file.name === (evidence.producerFile ?? evidence.file))?.source ?? ''
  const sceneSource = backendFiles.find(file => file.name === (evidence.sceneFile ?? evidence.file))?.source ?? ''
  assert(producerSource.includes(evidence.producer) && sceneSource.includes(evidence.scene),
    `Delegated publisher evidence is stale for ${cardId}/${sceneKey}`)
}

assert(board.includes("resourceSelectionPrompt.data?.cancel ?? '取消打出'"),
  'Resource payment cancellation must use the authoritative label in the existing footer')
assert(backend.includes('excludedResourceIds, allowCancel: true);'),
  'Active morale payment must retain its pre-stack cancellation escape')
assert(backend.includes('continuation == "active-return-choice"') && backend.includes('data["cancel"] = "不发动";'),
  'Active morale return must distinguish cancellation from mandatory effect-stage return')

console.log(`Effect presentation contracts passed: ${explicitPairs.length} explicit card/scene publishers, system exemptions bounded, multiline UI/runtime preserved.`)
