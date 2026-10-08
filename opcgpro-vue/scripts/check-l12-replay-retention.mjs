import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { replayEntryTemplateContract } from './replay-entry-template-contract.mjs'
import './test-a3-replay-entry-template-contract.mjs'

const source = readFileSync(fileURLToPath(new URL('../src/l12/MatchRecords.vue', import.meta.url)), 'utf8')
const replayPage = readFileSync(fileURLToPath(new URL('../src/l12/ReplayPage.vue', import.meta.url)), 'utf8')
const replayModel = readFileSync(fileURLToPath(new URL('../src/l12/replayModel.ts', import.meta.url)), 'utf8')
const gameBoard = readFileSync(fileURLToPath(new URL('../src/l12/game/GameBoard.vue', import.meta.url)), 'utf8')
const zonePresentation = readFileSync(fileURLToPath(new URL('../src/l12/game/ZoneMovementPresentationLayer.vue', import.meta.url)), 'utf8')
const combatPresentation = readFileSync(fileURLToPath(new URL('../src/l12/game/CombatMotionPresentationLayer.vue', import.meta.url)), 'utf8')
const battleUtility = readFileSync(fileURLToPath(new URL('../src/l12/game/BattleUtilityDock.vue', import.meta.url)), 'utf8')
const mobileViewport = readFileSync(fileURLToPath(new URL('../src/l12/mobileViewport.css', import.meta.url)), 'utf8')
const replayTemplate = replayEntryTemplateContract({ matchRecords: source, replayPage, gameBoard })

assert.match(source,
  /selected\.value\?\.endedUtc\s*&&\s*selected\.value\.commandCount\s*>\s*0/,
  'server replay actions must require at least one recorded command')
assert.match(source,
  /selected\.value\?\.endedUtc\s*&&\s*selected\.value\.commandCount\s*>\s*0/,
  'route navigation must not bypass the zero-command replay guard')
assert.equal(replayTemplate.recordsHeaderAndImport, true,
  'replay history must keep its real title, retention note and JSON import entry')
assert.equal(replayTemplate.recordsReplayAvailability, true,
  'playback and JSON export must share the unavailable guard and explain cleaned payloads')
assert.match(source, /const detail = parseReplayPayload[^]*rememberImportedReplay\(detail\)[^]*router\.push\(\{ name: 'json-replay' \}\)/,
  'opening a JSON replay must navigate directly into playback')
assert.doesNotMatch(source, /imported-record|consumeImportedReplay|(?:match|selected)\.deck[01]|record-decks/,
  'opened JSON replays must not join the history list and replay details must not show deck names')
assert.match(source, /anchor\.download = `\$\{replayFileDate\(detail\.match\.startedUtc\)\}-\$\{detail\.match\.matchId\}\.json`/,
  'downloaded replay JSON must be named with the match date and match ID only')
assert.match(replayModel, /deck0:\s*'',\s*\n\s*deck1:\s*''/,
  'admin replay metadata must not expose deck names')
assert.match(replayModel, /deckName:\s*'',\s*faction:/,
  'all replay board states must hide deck names')
assert.doesNotMatch(replayPage, /source:\s*['"]json['"]/,
  'returning from a JSON replay must not add it to the replay history')
assert.equal(replayTemplate.replayBoardBinding, true,
  'replay speed and presentation completion must be connected to the unique read-only board')
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
assert.match(replayPage, /\.replay-controls\{position:fixed;z-index:var\(--l12-battle-fixed-controls-z,5100\);left:14px;bottom:14px/,
  'replay player controls must stay fixed at the left-bottom corner on the shared protected-control layer')
assert.match(mobileViewport, /--l12-battle-fixed-controls-z:5100/,
  'desktop protected controls must stay above full-screen battle dialogs')
assert.match(mobileViewport, /--l12-battle-fixed-controls-z:2147483632/,
  'mobile protected controls must stay above safe-canvas masks')
assert.equal(replayTemplate.replayControlTeleport, true,
  'replay controls must share the dedicated overlay host with teleported board presentation layers')
assert.doesNotMatch(replayPage, /:deep\(\.(?:prompt-overlay|battle-modal-mask|picker-mask|master-overlay|faction-effect-overlay)\)/,
  'replay must not use scoped descendant selectors that cannot reach teleported overlays')
assert.match(battleUtility, /\.battle-modal-mask\{position:fixed;z-index:4700/,
  'true blocking battle dialogs must remain above the replay controls')
assert.match(gameBoard, /\.public-reveal-animation\{z-index:903\}\.dice-reveal-animation\{z-index:904\}\.board-target-controls\{z-index:3000\}/,
  'public reveal, dice and target prompts must stay below the replay controls layer')
assert.match(replayPage, /\.replay-loading\{position:fixed;z-index:3300/,
  'loading and error fullscreen layers may still cover the player controls')

console.log('L12 replay-retention UI checks passed (24 assertions).')
