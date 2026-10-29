import type { AlternateArtAwardRule } from '../platformAlternateArtContracts'

type RuleExplanationInput = Pick<AlternateArtAwardRule, 'kind' | 'seasonId' | 'eventId' | 'minimumTierIndex' | 'masterId' | 'active'>

/** Describe the existing server triggers only; never determine award eligibility here. */
export function alternateArtRuleExplanation(rule: RuleExplanationInput, masterName?: string) {
  const season = rule.seasonId.trim() ? `赛季 ${rule.seasonId.trim()}` : '所选赛季'
  const tier = Number.isInteger(rule.minimumTierIndex) && rule.minimumTierIndex >= 0 && rule.minimumTierIndex <= 4
    ? `第 ${rule.minimumTierIndex + 1} 档及以上` : '所选最低段位及以上'
  let condition: string
  let timing: string
  switch (rule.kind) {
    case 'rank-reached':
      condition = `${season}至少完成一场排位结算，且当前权威段位达到${tier}；不要求完成定级，不按历史最高段位或榜单前 50 名判断。`
      timing = '启用并保存时，立即检查当前达标玩家并补发缺失权益；之后每场排位结算后自动检查。'
      break
    case 'season-final':
      condition = `参与过${season}排位的玩家，在归档时最终七曜值对应的段位达到${tier}；不是赛季中曾达到过该段位即可。`
      timing = '在后台归档该旧赛季、完成赛季结算时自动派发；保存规则本身不会发放。'
      break
    case 'master-champion-season-final':
      condition = `${season}归档时，列入服务端最终最强主宰得主名单${rule.masterId?.trim() ? `，且主宰为「${masterName || rule.masterId.trim()}」` : '（所有主宰的得主）'}。`
      timing = '在后台归档该旧赛季、完成赛季结算时自动派发；赛季中暂时获得称号或保存规则本身不会发放。'
      break
    case 'event':
      condition = `由管理员确认${rule.eventId.trim() ? `活动 ${rule.eventId.trim()}` : '该活动'}的玩家名单；不会按段位自动选人。`
      timing = '保存并启用规则后，仍需在下方活动名单中选择玩家并执行派发；不会自动定时发放。'
      break
    default:
      condition = '未知规则类型，请核对原规则。'
      timing = '无法确认发放时机；请勿据此说明执行派发。'
  }
  return { condition, timing: rule.active ? timing : `当前已停用，不会派发。重新启用并保存后：${timing}`, prerequisite: '规则须启用，异画也须保持启用并可派发；同一账号已有相同异画、来源和赛季／活动的有效权益时，不会重复新增。' }
}
