import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { compileTemplate, parse } from '@vue/compiler-sfc'
import {
  battleActionActorPresentation,
  battleActionDirectSubmitStatus,
  battleActionSelectionRange,
  battleActionSelectionStatus,
} from '../src/l12/game/battleActionPresentation.ts'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const sources = new Map([
  ['GameBoard.vue', fs.readFileSync(path.join(root, 'src/l12/game/GameBoard.vue'), 'utf8')],
  ['PromptOverlay.vue', fs.readFileSync(path.join(root, 'src/l12/game/PromptOverlay.vue'), 'utf8')],
  ['PlayerMat.vue', fs.readFileSync(path.join(root, 'src/l12/game/PlayerMat.vue'), 'utf8')],
  ['GameActions.vue', fs.readFileSync(path.join(root, 'src/l12/game/GameActions.vue'), 'utf8')],
])

for (const [filename, source] of sources) {
  const { descriptor, errors } = parse(source, { filename })
  assert.deepEqual(errors, [], `${filename} SFC parse failed`)
  const result = compileTemplate({
    filename,
    id: `e2-${filename}`,
    source: descriptor.template?.content ?? '',
  })
  assert.deepEqual(result.errors, [], `${filename} template compilation failed`)
}

assert.deepEqual(battleActionActorPresentation(0, 0), { label: '你的操作', state: 'self' })
assert.deepEqual(battleActionActorPresentation(1, 0), { label: '对手操作', state: 'opponent' })
assert.deepEqual(battleActionActorPresentation(1, 0, true), { label: '当前玩家正在选择', state: 'neutral' })
assert.equal(battleActionSelectionRange(2, 2), '需选择 2 项')
assert.equal(battleActionSelectionRange(0, 2), '需选择 0 至 2 项')
assert.equal(battleActionSelectionStatus(1, 2), '已选择 1/2')
assert.equal(battleActionDirectSubmitStatus(), '点击绿色高亮格位即提交')

const board = sources.get('GameBoard.vue')
const overlay = sources.get('PromptOverlay.vue')
const mat = sources.get('PlayerMat.vue')
const actions = sources.get('GameActions.vue')
assert.match(board, /inline-prompt-operation/)
assert.match(board, /inline-prompt-legal/)
assert.match(board, /inline-prompt-pending/)
assert.match(board, /direct-submit/)
assert.match(board, /data-payment-status/)
assert.doesNotMatch(board, /点击空格即提交/)
assert.match(overlay, /prompt-task-context/)
assert.match(overlay, /promptPendingSelection/)
assert.match(overlay, /props\.readOnly/)
assert.doesNotMatch(overlay, /promptActor\.label \}\} · \{\{ promptTitle/, 'overlay title must not be repeated in actor badge')
for (const redundant of ['操作要求', '费用状态', '提交结果']) assert.ok(!overlay.includes(redundant), `overlay must avoid redundant label: ${redundant}`)
assert.match(mat, /actionGuidanceActive/)
assert.match(mat, /action-guidance/)
for (const actor of ['你的操作', '你在防守', '我方阶段', '对手操作']) assert.ok(actions.includes(actor), `GameActions actor fact missing: ${actor}`)
assert.match(actions, /game\.prompts\?\.\[0\]\?\.playerIndex \?\? game\.waitingPrompt\?\.playerIndex/)
assert.match(actions, /me\.mulliganDone \? '对手操作' : '你的操作'/)

const guidanceCss = mat.match(/\.l12-player-mat\.action-guidance[\s\S]*?(?=\.morale-orb\{|<\/style>)/)?.[0] ?? ''
assert.ok(guidanceCss, 'action guidance CSS missing')
assert.doesNotMatch(guidanceCss, /\b(?:animation|transform)\s*:/, 'action guidance must not animate or transform geometry')

console.log('E2 action hierarchy focused checks passed')
