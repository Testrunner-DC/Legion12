import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const source = readFileSync(fileURLToPath(new URL('../src/l12/MatchRecords.vue', import.meta.url)), 'utf8')

assert.match(source,
  /selectedSummary\.value\?\.endedUtc\s*&&\s*selectedSummary\.value\.commandCount\s*>\s*0/,
  'server replay actions must require at least one recorded command')
assert.match(source,
  /selected\.value\?\.endedUtc\s*&&\s*selected\.value\.commandCount\s*>\s*0/,
  'route navigation must not bypass the zero-command replay guard')
assert.equal((source.match(/:disabled="!canUseSelectedReplay"/g) ?? []).length, 2,
  'both playback and JSON export must be disabled when replay payload is unavailable')
assert.match(source, /selectedSummary\.commandCount === 0[^]*回放载荷已清理/,
  'retained match summaries must explain why playback is unavailable')

console.log('L12 replay-retention UI checks passed (4 assertions).')
