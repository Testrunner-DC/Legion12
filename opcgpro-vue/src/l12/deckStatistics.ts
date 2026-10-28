/** Main-deck copies, including cards exempt from construction count; missing cost is not zero. */
export function buildDeckCostStatistics(entries: readonly { card: { cost?: number | null }; count: number }[]) {
  const curve = Array<number>(9).fill(0)
  let unknown = 0
  for (const { card, count } of entries) {
    const cost = card.cost
    if (typeof cost !== 'number' || !Number.isFinite(cost) || !Number.isInteger(cost) || cost < 0) {
      unknown += count
      continue
    }
    curve[Math.min(8, cost)]! += count
  }
  return { curve, unknown }
}