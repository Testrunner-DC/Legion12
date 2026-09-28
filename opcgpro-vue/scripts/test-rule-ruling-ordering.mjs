import assert from 'node:assert/strict'
import {
  compareCardNumbers,
  compareCardRulingsByNumber,
  compareRulingsByScoreAndDate,
  rulingCardSortKey,
} from '../src/l12/site/ruleRulingOrdering.ts'

const numbers = new Map([
  ['nine', 'S01-9'],
  ['ten', 'S01-10'],
  ['ninety-nine', 'S01-99'],
  ['hundred', 'S01-100'],
])
const resolveCardNumber = cardId => numbers.get(cardId)
const row = (id, cardIds, recordedAt = '2026-01-01') => ({ id, cardIds, recordedAt })

assert.deepEqual(['S01-100', 'S01-9', 'S01-99', 'S01-10'].sort(compareCardNumbers),
  ['S01-9', 'S01-10', 'S01-99', 'S01-100'], 'card-number comparison must be numeric-aware')

const multi = row('multi-highest', ['nine', 'hundred'], '2026-03-01')
assert.equal(rulingCardSortKey(multi, resolveCardNumber), 'S01-100', 'multi-card ruling must use its highest card number')

const ordered = [
  row('no-cards', [], '2099-01-01'),
  row('unknown-card', ['missing'], '2099-01-01'),
  row('single-nine', ['nine']),
  row('single-ten', ['ten']),
  row('single-ninety-nine', ['ninety-nine']),
  row('single-hundred', ['hundred'], '2026-02-01'),
  multi,
].sort((left, right) => compareCardRulingsByNumber(left, right, resolveCardNumber))
assert.deepEqual(ordered.map(item => item.id), [
  'multi-highest', 'single-hundred', 'single-ninety-nine', 'single-ten', 'single-nine', 'unknown-card', 'no-cards',
], 'known card rulings must sort descending, followed by unknown-card and unlinked rows')

const ties = [row('tie-b', ['hundred']), row('tie-a', ['hundred'])]
  .sort((left, right) => compareCardRulingsByNumber(left, right, resolveCardNumber))
assert.deepEqual(ties.map(item => item.id), ['tie-a', 'tie-b'], 'equal card/date rows need a stable ID tie-break')

const higherScoreOlder = row('higher-score', [], '2025-01-01')
const lowerScoreNewer = row('lower-score', [], '2026-01-01')
assert(compareRulingsByScoreAndDate(higherScoreOlder, lowerScoreNewer, 2, 1) < 0,
  'general rulings must keep search score ahead of date')
assert(compareRulingsByScoreAndDate(higherScoreOlder, lowerScoreNewer, 1, 1) > 0,
  'general rulings with equal scores must keep newest date first')

console.log('Rule ruling ordering passed: numeric 9/10/99/100, multi-card maximum, tail groups, ties, and general score/date')
