import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { playerLogContainsForbiddenTerms, projectLog } from '../src/l12/game/logViewModel.ts'
import { replayGameAt } from '../src/l12/replayModel.ts'

const probe = fileURLToPath(new URL('./probe-battle-log-qianyang.ps1', import.meta.url))
const repoRoot = fileURLToPath(new URL('../..', import.meta.url))
const testProject = path.join(repoRoot, 'TwelveLegions.Tests', 'TwelveLegions.Tests.csproj')
const configuration = process.env.L12_BATTLE_LOG_CONFIGURATION ?? 'Release'
assert(['Debug', 'Release'].includes(configuration), 'configuration must be Debug or Release')
const commit = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repoRoot, encoding: 'utf8' }).trim()
const framework = execFileSync('dotnet', ['msbuild', testProject,
  '-getProperty:TargetFramework', `-p:Configuration=${configuration}`], { encoding: 'utf8' }).trim()
assert(framework, 'the test project must declare a target framework')
execFileSync('dotnet', ['build', testProject, '--no-restore', '--no-incremental', '-c', configuration, '-v:q'],
  { cwd: repoRoot, encoding: 'utf8' })
const assemblyDirectory = path.join(repoRoot, 'TwelveLegions.Tests', 'bin', configuration, framework)
function scenario(name) {
  return JSON.parse(execFileSync('pwsh', ['-NoProfile', '-File', probe, '-Scenario', name,
    '-Configuration', configuration, '-AssemblyDirectory', assemblyDirectory, '-ExpectedCommit', commit],
    { encoding: 'utf8' }).trim())
}
const results = events => events.filter(event => event.type === 'effect-result')
const words = row => row.parts.map(part => part.text).join('')

const empty = scenario('empty-draw')
assert.equal(empty.moraleAfter, 2, 'the real second segment must actually return one of three morale')
const emptyResults = results(empty.owner)
assert.deepEqual(emptyResults.map(event => event.effectResultStatus), ['skipped', 'resolved'])
assert.deepEqual(emptyResults.map(event => event.effectSegmentIndex), [1, 2])
assert.equal(emptyResults[0].playerLogGroupId, emptyResults[1].playerLogGroupId,
  'the committed continuation must retain the authoritative root group')
assert(emptyResults[1].playerLogSemantic?.outcomeLabel.includes('已支付费用：返还1士气'))

for (const [view, you] of [[empty.owner, 0], [empty.opponent, 1], [empty.spectator, -1]]) {
  const rows = projectLog(view, you, [])
  const group = rows.find(row => row.kind === 'line' && row.detail?.some(detail =>
    words(detail).includes('第2/2段完成')))
  assert(group, 'each public viewer must retain the real two-segment group')
  assert.match(words(group), /1段跳过、1段完成/)
  assert.equal(group.detail.filter(detail => words(detail).includes('已支付费用：返还1士气')).length, 1)
  assert.deepEqual(playerLogContainsForbiddenTerms(rows), [])
}
const incomplete = projectLog(empty.owner.filter(event => event.sequence <= emptyResults[0].sequence), 0, [])
assert(incomplete.some(row => row.kind === 'line' && words(row).includes('第1/2段跳过')),
  'a still-incomplete group must name its segment rather than claim the whole effect skipped')
const replayed = projectLog([...empty.owner.slice().reverse(), ...empty.owner], 0, [])
assert.equal(replayed.filter(row => row.kind === 'line' && row.detail?.some(detail =>
  words(detail).includes('第2/2段完成'))).length, 1,
'reconnect duplicates and out-of-order replay must keep one two-segment group')
const legacy = projectLog(empty.owner.map(event => event.type === 'effect-result'
  ? { ...event, effectResultStatus: undefined, playerLogSemantic: undefined } : event), 0, [])
assert(legacy.some(row => row.kind === 'line' && words(row).includes('打出')))
assert(!legacy.some(row => row.kind === 'line' && words(row).includes('已支付费用：')),
  'older replay events without result metadata must not have payment invented from nearby changes')
const replay = replayGameAt({
  match: { matchId: 'stage4a-real', roomCode: 'STAGE4A' },
  commands: [{ state: { MatchId: 'stage4a-real', RoomCode: 'STAGE4A',
    Players: [], Events: empty.owner }, revision: 1 }],
  viewerPlayerIndex: 0,
}, 0)
assert(replay)
assert(projectLog(replay.recentEvents ?? [], 0, []).some(row => row.kind === 'line'
  && row.detail?.some(detail => words(detail).includes('已支付费用：返还1士气'))),
'the persisted replay parser must retain the real terminal receipt and group')

const target = scenario('target-decline')
const targetResults = results(target.owner)
assert.equal(targetResults[0].effectResultStatus, 'resolved')
assert.equal(targetResults[0].playerLogSemantic?.targetInstanceId, 'stage4a-public-target')
assert.equal(targetResults[1].playerLogSemantic?.targetInstanceId == null, true,
  'the later declined segment must not inherit the first segment target')
for (const [view, you] of [[target.owner, 0], [target.opponent, 1], [target.spectator, -1]]) {
  const rows = projectLog(view, you, [])
  assert(rows.some(row => row.kind === 'line' && row.detail?.some(detail =>
    words(detail).includes(`实际处理目标：〈${targetResults[0].playerLogSemantic.targetName}〉`))),
  'only the target actually processed by the engine may appear for public viewers')
  assert.deepEqual(playerLogContainsForbiddenTerms(rows), [])
}
const targetReplay = replayGameAt({
  match: { matchId: 'stage4a-target', roomCode: 'STAGE4A' },
  commands: [{ state: { MatchId: 'stage4a-target', RoomCode: 'STAGE4A',
    Players: [], Events: target.owner }, revision: 1 }],
  viewerPlayerIndex: 0,
}, 0)
assert(targetReplay && projectLog(targetReplay.recentEvents ?? [], 0, []).some(row =>
  row.kind === 'line' && row.detail?.some(detail =>
    words(detail).includes(`实际处理目标：〈${targetResults[0].playerLogSemantic.targetName}〉`))),
'the existing replay semantic fields must retain the actual public target')

const hidden = scenario('hidden-draw')
for (const view of [hidden.owner, hidden.opponent, hidden.spectator]) {
  assert(results(view).every(event => !event.playerLogSemantic?.targetInstanceId
    && !event.playerLogSemantic?.targetName),
  'a hidden or private-zone card must not become a processed public target receipt')
  assert(!JSON.stringify(results(view)).includes('stage4a-private-target'),
    'the hidden target instance must not be serialized into the new terminal facts')
}
console.log('battle log engine integration: real composite grouping, payment, target and privacy passed')
