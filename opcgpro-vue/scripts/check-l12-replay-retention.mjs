import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const source = readFileSync(fileURLToPath(new URL('../src/l12/MatchRecords.vue', import.meta.url)), 'utf8')
const replayPage = readFileSync(fileURLToPath(new URL('../src/l12/ReplayPage.vue', import.meta.url)), 'utf8')
const replayModel = readFileSync(fileURLToPath(new URL('../src/l12/replayModel.ts', import.meta.url)), 'utf8')
const gameBoard = readFileSync(fileURLToPath(new URL('../src/l12/game/GameBoard.vue', import.meta.url)), 'utf8')
const zonePresentation = readFileSync(fileURLToPath(new URL('../src/l12/game/ZoneMovementPresentationLayer.vue', import.meta.url)), 'utf8')
const combatPresentation = readFileSync(fileURLToPath(new URL('../src/l12/game/CombatMotionPresentationLayer.vue', import.meta.url)), 'utf8')

assert.match(source,
  /selected\.value\?\.endedUtc\s*&&\s*selected\.value\.commandCount\s*>\s*0/,
  'server replay actions must require at least one recorded command')
assert.match(source,
  /selected\.value\?\.endedUtc\s*&&\s*selected\.value\.commandCount\s*>\s*0/,
  'route navigation must not bypass the zero-command replay guard')
assert.equal((source.match(/:disabled="!canUseSelectedReplay"/g) ?? []).length, 2,
  'both playback and JSON export must be disabled when replay payload is unavailable')
assert.match(source, /selected\.commandCount === 0[^]*回放载荷已清理/,
  'retained match summaries must explain why playback is unavailable')
assert.match(source, /<h1>对局回放<\/h1>/, 'replay history must use the player-facing title')
assert.match(source, /仅保存7天内最近10场回放，历史回放文件可能随版本更新失效。/,
  'replay history must explain the retention and compatibility window')
assert.match(source, /const detail = parseReplayPayload[^]*rememberImportedReplay\(detail\)[^]*router\.push\(\{ name: 'json-replay' \}\)/,
  'opening a JSON replay must navigate directly into playback')
assert.doesNotMatch(source, /imported-record|consumeImportedReplay|selectedSummary\.deck[01]/,
  'opened JSON replays must not join the history list and replay details must not show deck names')
assert.match(source, /anchor\.download = `\$\{replayFileDate\(detail\.match\.startedUtc\)\}-\$\{detail\.match\.matchId\}\.json`/,
  'downloaded replay JSON must be named with the match date and match ID only')
assert.match(replayModel, /deck0:\s*'',\s*\n\s*deck1:\s*''/,
  'admin replay metadata must not expose deck names')
assert.match(replayModel, /deckName:\s*'',\s*faction:/,
  'all replay board states must hide deck names')
assert.doesNotMatch(replayPage, /source:\s*['"]json['"]/,
  'returning from a JSON replay must not add it to the replay history')
assert.match(replayPage, /:replay-playback-speed="playbackSpeed"[^]*@replay-presentation-change="replayPresentationBusy = \$event"/,
  'replay speed and presentation completion must be connected to the board')
assert.doesNotMatch(replayPage, /setInterval|clearInterval/,
  'automatic replay must not advance on an interval that can overrun card presentation')
assert.match(replayPage, /if \(replayPresentationBusy\.value\)[^]*setTimeout[^]*return[^]*selectedStep\.value = target/,
  'automatic replay must wait until card presentation is idle before advancing')
assert.match(gameBoard, /if \(!props\.replayPlaybackSpeed\) return l12AnimationDuration\(3000, 700\)[^]*l12AnimationDuration\(1600, 420\) \/ props\.replayPlaybackSpeed/,
  'only replay card reveals may use the accelerated duration')
assert.match(zonePresentation, /if \(!props\.playbackSpeed\) return l12AnimationDuration\(standardMs, liveMinimumMs\)[^]*l12AnimationDuration\(standardMs, replayMinimumMs\) \/ props\.playbackSpeed/,
  'zone-card motion must preserve live timing and scale only in replay')
assert.match(combatPresentation, /if \(!props\.playbackSpeed\) return l12AnimationDuration\(standardMs, liveMinimumMs\)[^]*l12AnimationDuration\(standardMs, replayMinimumMs\) \/ props\.playbackSpeed/,
  'combat-card motion must preserve live timing and scale only in replay')

console.log('L12 replay-retention UI checks passed (18 assertions).')
