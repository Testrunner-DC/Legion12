import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const source = readFileSync(join(root, 'opcgpro-vue', 'src', 'l12', 'MatchRecords.vue'), 'utf8')

assert.match(source, /platformRequest<MatchSummary\[]>\('\/api\/matches\?limit=30'\)/,
  'player replay entry must request exactly the server-retained 30-match window')
assert.doesNotMatch(source, /\/api\/matches\?limit=(?:[4-9]\d|[1-9]\d{2,})/,
  'player replay entry must not request more than the retained window')
assert.match(source, /最近 30 场回放/,
  'player replay entry must explain the bounded server replay window')
assert.match(source, /打开 JSON 回放/,
  'local JSON replay import must remain available independently of server retention')

console.log('L12 player replay UI contract checks passed.')
