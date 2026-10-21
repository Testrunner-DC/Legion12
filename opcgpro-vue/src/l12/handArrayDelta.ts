export const HAND_ARRAY_DELTA_CAPABILITY = 'handArrayDeltaV1'
const MAX_HAND = 512
type ObjectValue = Record<string, any>
const object = (value: unknown): value is ObjectValue => value !== null && typeof value === 'object' && !Array.isArray(value)
const integer = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0
const keys = (value: unknown): string[] | null => {
  if (!Array.isArray(value) || value.length > MAX_HAND) return null
  const ids: string[] = []
  for (const card of value) {
    if (!object(card) || typeof card.instanceId !== 'string' || !card.instanceId.trim()
      || card.instanceId.length > 256 || ids.includes(card.instanceId)) return null
    ids.push(card.instanceId)
  }
  return ids
}
const same = (actual: string[] | null, expected: unknown) => actual !== null && Array.isArray(expected)
  && actual.length === expected.length && actual.every((id, index) => id === expected[index])

export function validHandDeltaChangePath(root: ObjectValue, path: unknown): boolean {
  if (!Array.isArray(path) || path.length === 0) return false
  let current: any = root
  for (let index = 0; index < path.length; index++) {
    const key = path[index]
    if (Array.isArray(current)) {
      if (!integer(key) || key >= current.length) return false
    } else if (!object(current) || typeof key !== 'string'
      || ['__proto__', 'constructor', 'prototype'].includes(key)) return false
    if (index < path.length - 1) {
      if (!Object.hasOwn(current, key)) return false
      current = Reflect.get(current, key)
    }
  }
  return true
}

/** Restore only on the private cloned envelope; callers publish nothing on any failure. */
export function restoreHandSplices(baseline: ObjectValue, rebuilt: ObjectValue, wire: unknown, changes: unknown): boolean {
  try {
    const sameOptional = (before: ObjectValue, after: ObjectValue, key: string) => object(before) && object(after)
      && Object.hasOwn(before, key) === Object.hasOwn(after, key) && before[key] === after[key]
    if (!object(wire) || Object.keys(wire).sort().join(',') !== 'baseStateHash,edits,resultStateHash'
      || !sameOptional(baseline, rebuilt, 'observerView') || !sameOptional(baseline.state, rebuilt.state, 'you')
      || typeof wire.baseStateHash !== 'string' || !wire.baseStateHash
      || typeof wire.resultStateHash !== 'string' || !wire.resultStateHash
      || baseline.state?.stateHash !== wire.baseStateHash || rebuilt.state?.stateHash !== wire.resultStateHash
      || !Array.isArray(wire.edits) || wire.edits.length < 1 || wire.edits.length > 2
      || !Array.isArray(changes) || !Array.isArray(baseline.state?.players) || baseline.state.players.length !== 2
      || !Array.isArray(rebuilt.state?.players) || rebuilt.state.players.length !== 2
      || changes.some(change => !validHandDeltaChangePath(rebuilt, change?.path))) return false
    const assignments: Array<{ player: ObjectValue, cards: ObjectValue[] }> = []
    const seen = new Set<number>()
    for (const edit of wire.edits) {
      if (!object(edit) || Object.keys(edit).sort().join(',') !== 'baseIds,insert,playerIndex,prefix,remove,resultIds'
        || !integer(edit.playerIndex) || edit.playerIndex > 1 || seen.has(edit.playerIndex)
        || !integer(edit.prefix) || !integer(edit.remove) || !Array.isArray(edit.insert)
        || keys(edit.insert) === null) return false
      seen.add(edit.playerIndex)
      const before = baseline.state?.players?.[edit.playerIndex]
      const after = rebuilt.state?.players?.[edit.playerIndex]
      if (!object(before) || !object(after) || before.playerIndex !== edit.playerIndex
        || after.playerIndex !== edit.playerIndex || !same(keys(before.hand), edit.baseIds)
        || edit.prefix > before.hand.length || edit.remove > before.hand.length - edit.prefix) return false
      for (const change of changes) {
        const path = change?.path
        if (!Array.isArray(path) || path.length === 0) return false
        if (path[0] === 'state' && (path.length === 1 || (path[1] === 'players'
          && (path.length === 2 || (path[2] === edit.playerIndex
            && (path.length === 3 || path[3] === 'hand')))))) return false
      }
      const cards = [...before.hand.slice(0, edit.prefix), ...edit.insert,
        ...before.hand.slice(edit.prefix + edit.remove)]
      if (!same(keys(cards), edit.resultIds)) return false
      assignments.push({ player: after, cards: JSON.parse(JSON.stringify(cards)) })
    }
    for (const assignment of assignments) assignment.player.hand = assignment.cards
    return true
  } catch { return false }
}
