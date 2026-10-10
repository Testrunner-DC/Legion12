export function samplePublicDeckOpeningHand(cardIds: readonly string[], random: () => number = Math.random, size = 6) {
  const pool = [...cardIds]
  for (let index = pool.length - 1; index > 0; index -= 1) {
    const swapWith = Math.floor(Math.max(0, Math.min(0.999999999, random())) * (index + 1))
    ;[pool[index], pool[swapWith]] = [pool[swapWith], pool[index]]
  }
  return pool.slice(0, Math.min(Math.max(0, size), pool.length))
}
