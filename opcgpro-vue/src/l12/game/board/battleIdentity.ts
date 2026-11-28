// B5 阶段 1（第二批）：从 GameBoard.vue 下沉的排位身份展示工具。
// 纯函数，不读组件状态；与排行榜/个人页共用同一套 RankedIdentityBadge 判定口径。
const absentIdentityLabels = new Set(['未定级', '暂无段位', '无段位', '未评级', '暂无称号', '无称号', '未获得称号', '暂无'])

export type BattleIdentityBadge = {
  rank?: number | null
  tier?: string | null
  placementTitle?: string | null
  highestTier?: boolean | null
}

export function identityLabel(value: string | null | undefined) {
  const label = value?.trim() ?? ''
  return absentIdentityLabels.has(label) ? '' : label
}

export function battleRank(badge: BattleIdentityBadge | null | undefined) {
  return badge?.highestTier === true ? badge?.rank ?? null : null
}

export function battleTierLabel(badge: BattleIdentityBadge | null | undefined) {
  const label = identityLabel(badge?.tier)
  // A placement title is the more specific player-facing identity. Players
  // without one keep their faction-specific tier name on its own row.
  if (identityLabel(badge?.placementTitle)) return ''
  return label
}
