import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const source = readFileSync(join(root, 'opcgpro-vue', 'src', 'l12', 'MatchRecords.vue'), 'utf8')
const profile = readFileSync(join(root, 'opcgpro-vue', 'src', 'l12', 'site', 'ProfilePage.vue'), 'utf8')

assert.match(source, /platformRequest<MatchSummary\[]>\('\/api\/matches\?limit=10'\)/,
  'player replay entry must request exactly the server-retained 10-match window')
assert.doesNotMatch(source, /\/api\/matches\?limit=(?:1[1-9]|[2-9]\d|[1-9]\d{2,})/,
  'player replay entry must not request more than the retained window')
assert.match(source, /7 天内最近 10 场回放/,
  'player replay entry must explain both the age and count bounds')
assert.match(profile, /platformRequest<Match\[]>\('\/api\/matches\?limit=10'\)/,
  'profile history must use the same retained match window')
assert.match(source, /打开 JSON 回放/,
  'local JSON replay import must remain available independently of server retention')

const model = readFileSync(join(root, 'opcgpro-vue', 'src', 'l12', 'replayModel.ts'), 'utf8')
assert.match(model, /compatibilityVersion:\s*replayCompatibilityVersion/,
  'exported JSON must declare the playback compatibility version')
assert.match(model, /throw new Error\('版本已更新'\)/,
  'incompatible imported JSON must use the requested concise player message')
assert.match(source, /JSON\.stringify\(exportReplayPayload\(detail\)\)/,
  'player JSON must omit presentation whitespace to minimize the saved file')

console.log('L12 player replay UI contract checks passed.')
