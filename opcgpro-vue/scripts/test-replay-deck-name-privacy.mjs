import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { compile } from '@vue/compiler-dom'
import { parse } from '@vue/compiler-sfc'
import * as Vue from 'vue'
import { exportReplayPayload, parseReplayPayload } from '../src/l12/replayModel.ts'

const privateDeckNames = ['我的私密构筑名', '对手的私密构筑名']
const alternateDeckNames = ['替换后的我方私密名', '替换后的对方私密名']

function player(index, deckName, casing = 'pascal') {
  const core = { PlayerIndex: index, Name: index ? '乙' : '甲', Faction: index ? 'tianting' : 'otherworld' }
  return casing === 'pascal' ? { ...core, DeckName: deckName } : {
    playerIndex: index, name: core.Name, faction: core.Faction, deckName,
  }
}

function command(sequence, state) {
  return {
    sequence, receivedUtc: '2026-10-08T08:00:00.000Z', playerIndex: sequence % 2,
    command: { type: 'passPriority', marker: `command-${sequence}` }, accepted: true,
    revision: sequence, stateHash: `hash-${sequence}`, state,
  }
}

function fixture(deckNames = privateDeckNames) {
  const preservedNested = { marker: 'same nested state reference' }
  return {
    detail: {
      match: {
        matchId: 'privacy-fixture', roomCode: 'PRIVATE', player0: '甲', player1: '乙',
        deck0: deckNames[0], deck1: deckNames[1], startedUtc: '2026-10-08T08:00:00.000Z',
        endedUtc: '2026-10-08T08:10:00.000Z', winner: 0, finalHash: 'final', commandCount: 3,
      },
      commands: [
        command(1, { Round: 1, Players: [player(0, deckNames[0]), player(1, deckNames[1])], preservedNested }),
        command(2, { round: 1, players: [player(0, deckNames[0], 'camel'), player(1, deckNames[1], 'camel')], preservedNested }),
        command(3, { Round: 2, Players: [{ PlayerIndex: 0, Name: '甲' }, { PlayerIndex: 1, Name: '乙' }], preservedNested }),
      ],
      viewerPlayerIndex: 0,
    },
    preservedNested,
  }
}

function assertNoPrivateDeckNames(value) {
  const serialized = JSON.stringify(value)
  for (const name of privateDeckNames) assert.equal(serialized.includes(name), false, `payload leaked ${name}`)
}

function assertSanitizedDetail(sanitized, original, originalText, preservedNested) {
  assert.notStrictEqual(sanitized, original, 'privacy projection must return a defensive detail copy')
  assert.notStrictEqual(sanitized.match, original.match, 'privacy projection must copy match metadata')
  assert.equal(sanitized.match.deck0, '')
  assert.equal(sanitized.match.deck1, '')
  assert.notStrictEqual(sanitized.commands, original.commands, 'privacy projection must own its command array')
  assert.notStrictEqual(sanitized.commands[0], original.commands[0])
  assert.notStrictEqual(sanitized.commands[0].state, original.commands[0].state)
  assert.strictEqual(sanitized.commands[0].command, original.commands[0].command,
    'non-state command payload must retain its reference')
  assert.strictEqual(sanitized.commands[0].state.preservedNested, preservedNested,
    'unrelated nested state must not be deep-cloned')
  assert.equal(sanitized.commands[0].state.Players[0].DeckName, '')
  assert.equal(sanitized.commands[0].state.Players[1].DeckName, '')
  assert.equal(sanitized.commands[1].state.players[0].deckName, '')
  assert.equal(sanitized.commands[1].state.players[1].deckName, '')
  assert.strictEqual(sanitized.commands[2], original.commands[2],
    'a frame without deck-name fields must retain its command object')
  assert.equal(sanitized.viewerPlayerIndex, original.viewerPlayerIndex)
  assert.equal(sanitized.match.winner, original.match.winner)
  assert.equal(sanitized.commands[0].stateHash, original.commands[0].stateHash)
  assert.equal(JSON.stringify(original), originalText, 'privacy projection must not mutate its input')
  assertNoPrivateDeckNames(sanitized)
}

function compileMatchRecordsRender() {
  const filename = fileURLToPath(new URL('../src/l12/MatchRecords.vue', import.meta.url))
  const descriptor = parse(readFileSync(filename, 'utf8'), { filename }).descriptor
  assert(descriptor.template, 'MatchRecords.vue must retain a template')
  const result = compile(descriptor.template.content, { mode: 'function', filename })
  return new Function('Vue', result.code)(Vue)
}

function vnodeText(value) {
  if (value == null || value === false) return ''
  if (typeof value === 'string' || typeof value === 'number') return String(value)
  if (Array.isArray(value)) return value.map(vnodeText).join('')
  if (typeof value !== 'object') return ''
  return vnodeText(value.children)
}

function renderedSurface(value, output = []) {
  if (value == null || value === false) return output
  if (Array.isArray(value)) {
    for (const child of value) renderedSurface(child, output)
    return output
  }
  if (typeof value !== 'object') return output
  const type = typeof value.type === 'string' ? value.type : String(value.type)
  const props = Object.fromEntries(Object.entries(value.props ?? {})
    .filter(([key, item]) => !key.startsWith('on') && key !== 'ref' && typeof item !== 'function')
    .map(([key, item]) => [key, String(item)]))
  output.push({ type, props, text: vnodeText(value.children) })
  renderedSurface(value.children, output)
  return output
}

function renderRecords(render, deckNames) {
  const summary = {
    matchId: 'privacy-fixture', roomCode: 'PRIVATE', player0: '甲', player1: '乙',
    deck0: deckNames[0], deck1: deckNames[1], startedUtc: '2026-10-08T08:00:00.000Z',
    endedUtc: '2026-10-08T08:10:00.000Z', winner: 0, commandCount: 3,
  }
  const noop = () => {}
  return render({
    matches: [summary], selected: summary, loading: false, error: '', fileInput: null,
    canUseSelectedReplay: true, mobileReplayBlocked: false,
    importReplay: noop, openReplayImport: noop, exportReplay: noop, loadMatches: noop,
    selectMatch: noop, playSelected: noop, dateLabel: () => '10月08日 16:00',
    durationLabel: () => '10:00', resultLabel: () => '甲 胜',
  }, [])
}

const results = []
function check(name, body) {
  try {
    body()
    results.push({ name, status: 'passed' })
  } catch (error) {
    results.push({ name, status: 'failed', error: error instanceof Error ? error.message : String(error) })
  }
}

check('export payload is a narrow non-mutating player privacy projection', () => {
  const { detail, preservedNested } = fixture()
  const originalText = JSON.stringify(detail)
  const payload = exportReplayPayload(detail)
  assert.equal(payload.format, 'legion12-replay')
  assert.equal(payload.version, 1)
  assert.equal(payload.compatibilityVersion, 1)
  assertSanitizedDetail(payload.detail, detail, originalText, preservedNested)
})

check('legacy local import is sanitized without rewriting unrelated replay data', () => {
  const { detail, preservedNested } = fixture()
  const envelope = {
    format: 'legion12-replay', version: 1, compatibilityVersion: 1,
    exportedAt: '2026-10-08T08:11:00.000Z', detail,
  }
  const originalText = JSON.stringify(detail)
  const parsed = parseReplayPayload(envelope)
  assertSanitizedDetail(parsed, detail, originalText, preservedNested)
  assert.equal(envelope.detail.match.deck0, privateDeckNames[0], 'legacy input must remain untouched')
})

check('deck-name projection preserves the existing permissive null-state parse boundary', () => {
  const { detail } = fixture()
  detail.commands = [{ ...detail.commands[0], state: null }]
  const parsed = parseReplayPayload({
    format: 'legion12-replay', version: 1, compatibilityVersion: 1, detail,
  })
  assert.equal(parsed.commands[0].state, null)
  assert.equal(parsed.match.deck0, '')
  assert.equal(parsed.match.deck1, '')
  assert.equal(detail.match.deck0, privateDeckNames[0])
})

check('rendered player replay entries are independent of private deck names', () => {
  const render = compileMatchRecordsRender()
  const first = renderedSurface(renderRecords(render, privateDeckNames))
  const second = renderedSurface(renderRecords(render, alternateDeckNames))
  assert.deepEqual(first, second, 'changing only private deck names changed the rendered records surface')
  const rendered = JSON.stringify(first)
  for (const name of [...privateDeckNames, ...alternateDeckNames])
    assert.equal(rendered.includes(name), false, `rendered text/title/aria leaked ${name}`)
  assert.match(rendered, /甲/)
  assert.match(rendered, /乙/)
  assert.match(rendered, /甲 胜/)
  assert.match(rendered, /10月08日 16:00/)
})

const failures = results.filter(result => result.status === 'failed')
for (const result of results) {
  const detail = result.status === 'passed' ? 'PASS' : `FAIL: ${result.error}`
  console.log(`${detail} — ${result.name}`)
}
if (failures.length) {
  console.error(`Replay deck-name privacy failed: ${failures.length}/${results.length} cases.`)
  process.exitCode = 1
} else console.log(`Replay deck-name privacy passed: ${results.length} behavioral cases.`)
