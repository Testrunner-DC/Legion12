import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileTemplate } from '@vue/compiler-sfc'
import * as Vue from 'vue'
import { renderToString } from '@vue/server-renderer'

// Render the actual GameActions template, without a socket or browser session.
const source = readFileSync(new URL('../src/l12/game/GameActions.vue', import.meta.url), 'utf8')
const { descriptor } = parse(source)
const compiled = compileTemplate({
  source: descriptor.template.content,
  filename: 'GameActions.vue', id: 'defense-owner-test',
  compilerOptions: { mode: 'function' },
})
assert.deepEqual(compiled.errors, [])
const render = new Function('Vue', compiled.code)(Vue)
async function view({ turn, attacker, seat, target = 'legion', stage = 'DefenseChoice', busy = false }) {
  const state = {
    game: { phase: 'Defense', activePlayer: turn, you: seat, pendingDefense: { attackerPlayer: attacker, stage } },
    me: { playerIndex: seat }, defenseTargetType: target, defenseCount: 1,
    supportIds: ['support'], canSupport: true, supportReady: true, busy,
    l12State: { rankedClock: null },
  }
  return renderToString(Vue.createSSRApp({ setup: () => state, render }))
}
let checks = 0
for (const turn of [0, 1]) for (const attacker of [0, 1]) for (const seat of [0, 1]) {
  for (const target of ['legion', 'master']) {
    const html = await view({ turn, attacker, seat, target })
    const expected = seat === 1 - attacker
    assert.equal(html.includes(target === 'master' ? '不抵挡' : '不支援'), expected,
      `turn=${turn}, attacker=${attacker}, seat=${seat}, target=${target}`)
    checks++
  }
}
for (const stage of ['AttackTriggers', 'Support', 'LethalReplacement', 'Finished']) {
  const html = await view({ turn: 1, attacker: 1, seat: 0, stage })
  assert.ok(!html.includes('不支援') && !html.includes('不抵挡'))
  checks++
}
const busyHtml = await view({ turn: 0, attacker: 1, seat: 0, busy: true })
assert.match(busyHtml, /disabled[^>]*>确认支援/)
assert.match(busyHtml, /disabled[^>]*>不支援/)
checks++
// Parent view must still keep observers/replays from mounting action controls.
const board = readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')
assert.match(board, /pendingDefense\?\.stage === 'DefenseChoice' && !readOnly/)
assert.match(board, /if \(props\.game\.phase === 'Defense' && props\.game\.pendingDefense\?\.stage === 'DefenseChoice'\)\s+return 1 - props\.game\.pendingDefense\.attackerPlayer/)
checks++
assert.match(board, /props\.game\.pendingDefense\?\.stage === 'DefenseChoice' && controlledPlayerIndex\.value === 1 - props\.game\.pendingDefense\.attackerPlayer && defenseTargetType\.value === 'master'/,
  'master defense hand candidates must use the current combat defender, including counterattacks')
checks++
console.log(`Defense action ownership: ${checks}/${checks} checks passed`)
