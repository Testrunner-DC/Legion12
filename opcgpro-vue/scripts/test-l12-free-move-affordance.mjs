import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const mat = readFileSync(new URL('../src/l12/game/PlayerMat.vue', import.meta.url), 'utf8')
  .replaceAll('\r\n', '\n')
const extract = pattern => {
  const match = mat.match(pattern)
  assert.ok(match, `missing PlayerMat source: ${pattern}`)
  return match[0]
}
const freeMoveActionSource = extract(/function freeMoveAction\(card: Card\) \{[\s\S]*?\n\}/)
  .replace('(card: Card)', '(card)')
const canFreeMoveSource = extract(/function canFreeMove\(card: Card\) \{[\s\S]*?\n\}/)
  .replace('(card: Card)', '(card)')
const moveTargetSource = extract(/function isMoveTarget\(row: number, slot: number\) \{[\s\S]*?\n\}/)
  .replace('(row: number, slot: number)', '(row, slot)')
  .replaceAll(']!)', '])')
const canMoveSource = extract(/function canMove\(card: Card, row: number, slot: number\) \{[\s\S]*?\n\}/)
  .replace('(card: Card, row: number, slot: number)', '(card, row, slot)')

function affordance({
  targetKeys,
  occupied = [],
  morale = 0,
  tapped = false,
  hidden = false,
  controllable = true,
  actionsEnabled = true,
}) {
  const card = {
    instanceId: 'mover',
    tapped,
    hidden,
    ruleActions: targetKeys === undefined ? [] : [{ id: 'freeMove', targetKeys }],
  }
  const field = [[card, null, null], [null, null, null]]
  for (const key of occupied) {
    const [row, slot] = key.split(':').map(Number)
    field[row][slot] = { instanceId: `occupied-${key}` }
  }
  const props = {
    controllable,
    actionsEnabled,
    selectedId: card.instanceId,
    moveMode: false,
    freeMoveMode: true,
    cavalryMoveMode: false,
    player: { field },
  }
  return new Function('props', 'spendableMorale', 'cavalryMoveAction',
    `${freeMoveActionSource}\n${canFreeMoveSource}\n${moveTargetSource}\n${canMoveSource}\nreturn {
      freeButton: canFreeMove(props.player.field[0][0]),
      paidButton: canMove(props.player.field[0][0], 0, 0),
      target: (row, slot) => isMoveTarget(row, slot),
    }`)(props, { value: morale }, () => undefined)
}

let checks = 0
const tenka = affordance({ targetKeys: ['0:1', '1:0'], morale: 0 })
assert.equal(tenka.freeButton, true)
assert.equal(tenka.paidButton, false)
assert.equal(tenka.target(0, 1), true)
assert.equal(tenka.target(1, 0), true)
assert.equal(tenka.target(1, 1), false)
checks += 5

const paid = affordance({ targetKeys: undefined, morale: 1 })
assert.equal(paid.freeButton, false)
assert.equal(paid.paidButton, true)
checks += 2

for (const input of [
  { targetKeys: undefined }, // ordinary zero-resource move and a consumed/old snapshot
  { targetKeys: [] },
  { targetKeys: ['0:1'], tapped: true },
  { targetKeys: ['0:1'], hidden: true },
  { targetKeys: ['0:1'], controllable: false },
  { targetKeys: ['0:1'], actionsEnabled: false },
]) {
  const result = affordance(input)
  assert.equal(result.freeButton, false, JSON.stringify(input))
  checks++
}

const corruptEarth = affordance({ targetKeys: ['0:1'], morale: 0 })
assert.equal(corruptEarth.target(0, 1), true)
assert.equal(corruptEarth.target(1, 0), false)
checks += 2

const hippolyta = affordance({ targetKeys: ['1:0'], morale: 0 })
assert.equal(hippolyta.target(1, 0), true)
assert.equal(hippolyta.target(0, 1), false)
checks += 2

const occupied = affordance({ targetKeys: ['0:1'], occupied: ['0:1'], morale: 0 })
assert.equal(occupied.target(0, 1), false)
checks++

assert.ok(!mat.includes("cardId === 'S02-0510'"), 'free movement must not be inferred from a frontend card id')
assert.ok(mat.includes("action.id === 'freeMove'"), 'free movement must consume the authoritative rule action')
checks += 2

console.log(`L12 free-move affordance: ${checks}/${checks} checks passed`)
