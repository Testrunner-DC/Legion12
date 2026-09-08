import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { computed } from 'vue'

const mat = readFileSync(new URL('../src/l12/game/PlayerMat.vue', import.meta.url), 'utf8').replaceAll('\r\n', '\n')
const resourceSource = mat.match(/const spendableMorale = computed\([\s\S]*?(?=\ntype AbilityEntry)/)[0]
const moveSource = mat.match(/function canMove\(card: Card, row: number, slot: number\) \{[\s\S]*?\n\}/)[0]
  .replace('(card: Card, row: number, slot: number)', '(card, row, slot)')
function availability({ count, active = true, guard = true, tapped = false, hidden = false, controllable = true }) {
  const field = [[{ instanceId: 'mover' }, null, null], [null, null,
    guard ? { cardId: 'S01-0212', tapped, hidden } : null]]
  const props = { player: { field, spendableResourceCount: count }, active, controllable, actionsEnabled: true }
  return new Function('props', 'computed', 'activeMorale', `${resourceSource}\n${moveSource}\nreturn { count: spendableMorale.value, canMove: canMove({},0,0) }`)(props, computed, { value: 0 })
}
let checks = 0
for (const [input, expected] of [
  [{ count: 1 }, true],
  [{ count: 0 }, false],
  [{ count: 1, guard: false }, true], // server may include temporary morale or divine power
  [{ count: undefined, active: true }, true],
  [{ count: undefined, active: false }, false],
  [{ count: undefined, tapped: true }, false],
  [{ count: undefined, hidden: true }, false],
  [{ count: 1, controllable: false }, false],
]) {
  assert.equal(availability(input).canMove, expected, JSON.stringify(input))
  checks++
}
const board = readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')
assert.ok(board.includes('me.value.spendableResourceCount ??'))
assert.ok(!board.includes("me.value.faction === 'taiyangcheng'"))
assert.ok(board.includes('card.minimumPlayCost ?? card.playCost'))
checks++
console.log(`Action resource projection: ${checks}/${checks} checks passed`)
