import { localDateBoundary, type AdminDisasterAnalyticsReport } from './platform'
import type { DisasterAnalyticsScope } from './disasterAnalytics'

export interface DisasterStrategyComparison {
  status: string; comparableOpportunities: number; totalOpportunities: number; referenceOpportunities: number
  matchedStrata: number; missingStrata: number; targetRate: number | null; referenceRate: number | null; difference: number | null
}
export interface DisasterStrategyItem {
  party: 'self' | 'opponent'; step: number; action: string; origin: string; cardId: string
  opportunities: number; selections: number; distinctMatches: number; selectionRate: number | null
  comparison: DisasterStrategyComparison
}
export interface DisasterStrategyCondition {
  id: string; party: 'self' | 'opponent'; step: number; origin: string; effectVersion: string; poolId: string
  priorPublicBans: Array<{ step: number; cardId: string }>; knownPublicCardId: string | null
  candidateCardIds: string[]; samples: number; distinctMatches: number
  choices: Array<{ cardId: string; selections: number; selectionRate: number | null }>
}
export interface DisasterStrategyReport {
  masterId: string | null; opponentMasterId: string | null; initiative: string | null; status: string
  coverage: AdminDisasterAnalyticsReport['coverage']; pools: AdminDisasterAnalyticsReport['pools']
  items: DisasterStrategyItem[]; conditions: DisasterStrategyCondition[]; mode: string | null; poolId: string | null; origin: string
  privatePickGroupsHidden: boolean; conditionsTruncated: boolean; poolsTruncated: boolean
  minimumOpportunities: number; comparisonMethod: string; mechanismStatus: string
}

export function disasterStrategyPath(scope: DisasterAnalyticsScope) {
  if (!scope.masterId || !scope.opponentMasterId || !scope.initiative) return ''
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(scope)) {
    if (value === undefined || value === '') continue
    if (key === 'from' || key === 'to') query.set(key === 'from' ? 'fromUtc' : 'toUtc', localDateBoundary(String(value), key === 'to')!)
    else query.set(key, String(value))
  }
  return `/api/admin/analytics/disasters/strategy?${query}`
}

export const strategyStepName = (step: number) => ({ 0: '先手第 1 禁', 1: '后手禁用', 2: '先手第 2 禁', 4: '先手选择 · 三选一', 5: '后手选择 · 二选一' } as Record<number, string>)[step] || '未知步骤'

export function strategyRanking(items: DisasterStrategyItem[], minimum: number) {
  const chosen = items.filter(item => item.selections > 0)
  const enough = chosen.filter(item => item.opportunities >= minimum)
  return [...(enough.length ? enough : chosen)].sort((a, b) => enough.length
    ? (b.selectionRate ?? -1) - (a.selectionRate ?? -1) || b.opportunities - a.opportunities || a.cardId.localeCompare(b.cardId)
    : b.opportunities - a.opportunities || b.selections - a.selections || a.cardId.localeCompare(b.cardId)).slice(0, 3)
}
