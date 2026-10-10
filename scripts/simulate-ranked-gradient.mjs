#!/usr/bin/env node

const SAMPLE_COUNT = 20_000
const SEASON_MATCHES = 84
const LONG_HORIZON_MATCHES = 360
const WIN_RATES = [0.45, 0.50, 0.55, 0.60]

const shared = {
  placementMatches: 5,
  placementMaximum: 29_999,
  placementRating: 1_500,
  placementRatingBase: 1_000,
  placementRatingScale: 15,
  placementRatingCap: 12_000,
  placementWinValue: 3_500,
}

const gradients = {
  current: {
    label: '现行梯度',
    tiers: [
      tier('初阶', 0, 200, 100, 50, 50, 0),
      tier('进阶', 15_000, 400, 200, 100, 100, 200),
      tier('精英', 30_000, 800, 400, 200, 200, 400),
      tier('统领', 60_000, 1_500, 750, 380, 380, 750),
      tier('冠冕', 100_000, 2_500, 1_250, 630, 630, 1_250),
    ],
  },
  proposed: {
    label: '建议梯度',
    tiers: [
      tier('初阶', 0, 3_800, 3_300, 3_500, 600, 100),
      tier('进阶', 15_000, 4_000, 2_900, 3_400, 500, 150),
      tier('精英', 30_000, 4_200, 2_400, 3_100, 400, 200),
      tier('统领', 60_000, 4_700, 1_600, 2_400, 250, 300),
      tier('冠冕', 100_000, 1_200, 100, 0, 120, 400),
    ],
  },
}

function tier(name, minimum, baseDelta, winStreakCap, lossProtectionCap, ratingGapCap,
  streakTerminationReward) {
  return { name, minimum, baseDelta, winStreakCap, lossProtectionCap, ratingGapCap,
    streakTerminationReward }
}

function mulberry32(seed) {
  return () => {
    let value = seed += 0x6D2B79F5
    value = Math.imul(value ^ value >>> 15, value | 1)
    value ^= value + Math.imul(value ^ value >>> 7, value | 61)
    return ((value ^ value >>> 14) >>> 0) / 4_294_967_296
  }
}

function tierIndex(tiers, value) {
  let result = 0
  for (let index = 1; index < tiers.length; index += 1) {
    if (value >= tiers[index].minimum) result = index
  }
  return result
}

function quantile(values, ratio) {
  if (!values.length) return null
  const ordered = [...values].sort((a, b) => a - b)
  return ordered[Math.floor((ordered.length - 1) * ratio)]
}

function simulateOne(gradient, winRate, random, placementRating = shared.placementRating) {
  const reachedAt = [0, null, null, null, null]
  let sevenValue = 0
  let highestFloor = 0
  let hiddenRating = placementRating
  let placementWins = 0
  let winStreak = 0
  let lossStreak = 0
  let valueAtSeasonEnd = 0

  for (let game = 1; game <= LONG_HORIZON_MATCHES; game += 1) {
    const won = random() < winRate
    winStreak = won ? winStreak + 1 : 0
    lossStreak = won ? 0 : lossStreak + 1

    if (game <= shared.placementMatches) {
      if (won) placementWins += 1
      if (game === shared.placementMatches) {
        const ratingPart = Math.max(0, Math.min(shared.placementRatingCap,
          Math.round((hiddenRating - shared.placementRatingBase) * shared.placementRatingScale)))
        sevenValue = Math.min(shared.placementMaximum,
          ratingPart + placementWins * shared.placementWinValue)
      }
      // 权威结算先使用本场前隐藏分完成定级，再按同强度对手的 50% 预期胜率更新 ±12。
      hiddenRating = Math.max(500, Math.min(2_500,
        hiddenRating + 24 * ((won ? 1 : 0) - 0.5)))
    } else {
      const tier = gradient.tiers[tierIndex(gradient.tiers, sevenValue)]
      const base = won ? tier.baseDelta : -tier.baseDelta
      const winBonus = won
        ? Math.min(tier.winStreakCap,
          Math.round(Math.max(0, winStreak - 1) * (tier.winStreakCap / 10)))
        : 0
      const protection = won
        ? 0
        : Math.min(tier.lossProtectionCap,
          Math.round(Math.max(0, lossStreak - 1) * (tier.lossProtectionCap / 5)))

      // 基准画像按同七曜对手模拟，所以分差修正为 0。终结奖励依赖对手连胜这一外部事件，
      // 为避免把偶发奖励当作常态爬分速度，核心结果不计入；两项上限仍在参数表中单独验收。
      sevenValue = Math.max(highestFloor, Math.max(0, sevenValue + base + winBonus + protection))
    }

    highestFloor = Math.max(highestFloor,
      gradient.tiers[tierIndex(gradient.tiers, sevenValue)].minimum)
    const currentTier = tierIndex(gradient.tiers, sevenValue)
    for (let index = 1; index <= currentTier; index += 1) {
      if (reachedAt[index] === null) reachedAt[index] = game
    }
    if (game === SEASON_MATCHES) valueAtSeasonEnd = sevenValue
  }

  return { reachedAt, valueAtSeasonEnd }
}

function summarize(gradient, winRate, placementRating = shared.placementRating) {
  const seed = 0x12_2026 + Math.round(winRate * 10_000) + placementRating
  const random = mulberry32(seed)
  const seasonValues = []
  const crownGames = []
  const reachedByTier = gradient.tiers.map(() => [])
  let crownBySeasonEnd = 0
  let crownByLongHorizon = 0

  for (let sample = 0; sample < SAMPLE_COUNT; sample += 1) {
    const result = simulateOne(gradient, winRate, random, placementRating)
    seasonValues.push(result.valueAtSeasonEnd)
    result.reachedAt.forEach((game, index) => {
      if (game !== null) reachedByTier[index].push(game)
    })
    const crownAt = result.reachedAt[4]
    if (crownAt !== null) {
      crownGames.push(crownAt)
      if (crownAt <= SEASON_MATCHES) crownBySeasonEnd += 1
      crownByLongHorizon += 1
    }
  }

  return {
    gradient: gradient.label,
    winRate: `${Math.round(winRate * 100)}%`,
    crownBy84: `${(crownBySeasonEnd / SAMPLE_COUNT * 100).toFixed(1)}%`,
    crownBy360: `${(crownByLongHorizon / SAMPLE_COUNT * 100).toFixed(1)}%`,
    crownMedian: quantile(crownGames, 0.5) ?? '—',
    final84Median: quantile(seasonValues, 0.5),
    tierMedianGames: reachedByTier.map(values => quantile(values, 0.5) ?? '—').join(' / '),
  }
}

console.log(`固定种子蒙特卡洛：${SAMPLE_COUNT.toLocaleString()} 个赛季/画像；目标赛季 ${SEASON_MATCHES} 场；长期观察 ${LONG_HORIZON_MATCHES} 场。`)
console.log('假设：初始隐藏分 1500、同强度/同七曜对手、5 场定级、保留段位保底；核心画像不计偶发终结连胜奖励。')
console.log('段位到达场次顺序：初阶 / 进阶 / 精英 / 统领 / 冠冕。\n')

const rows = Object.values(gradients).flatMap(gradient =>
  WIN_RATES.map(winRate => summarize(gradient, winRate)))
console.table(rows)

console.log('\n建议梯度在 55% 胜率下的隐藏分敏感性：')
console.table([1_300, 1_500, 1_700].map(rating => {
  const row = summarize(gradients.proposed, 0.55, rating)
  return { hiddenRating: rating, crownBy84: row.crownBy84, crownBy360: row.crownBy360,
    crownMedian: row.crownMedian,
    final84Median: row.final84Median }
}))

console.log('\n建议参数：')
console.table(gradients.proposed.tiers)
