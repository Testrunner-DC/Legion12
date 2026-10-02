import assert from 'node:assert/strict'
import fs from 'node:fs'

const read = relative => fs.readFileSync(new URL(relative, import.meta.url), 'utf8')
const ranked = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.Ranked.cs')
const rankedTests = read('../../TwelveLegions.Platform.Tests/RankedAnalyticsRangeTests.cs')
const platform = read('../src/l12/platform.ts')
const scope = read('../src/l12/site/StatisticsScope.vue')
const rankings = read('../src/l12/site/RankingsPage.vue')
const matrix = read('../src/l12/site/MasterMatchupMatrix.vue')
const publicDeck = read('../src/l12/site/PublicDeckDetailPage.vue')
const adminMaster = read('../src/l12/site/AdminMasterAnalyticsPanel.vue')
const adminCard = read('../src/l12/site/AdminCardAnalyticsPanel.vue')

const checks = [
  ['排行只追加权威范围元数据', ranked.includes('DateTimeOffset? FromUtc = null')
    && ranked.includes('DateTimeOffset? UntilUtc = null')
    && ranked.includes('string? SeasonId = null, string? SeasonName = null')
    && ranked.includes('var scopeFrom = rangeStart == DateTimeOffset.MinValue')
    && ranked.includes('var scopeUntil = rangeEnd < now ? rangeEnd : now')],
  ['前端兼容旧排行响应', platform.includes('fromUtc?: string | null')
    && platform.includes('untilUtc?: string | null')
    && rankings.includes('服务端未返回精确起止时间')],
  ['窗口专项锁定滚动与赛季字段', rankedTests.includes('Assert.Equal(now.AddDays(-7), seven.FromUtc)')
    && rankedTests.includes('Assert.Equal(now.AddDays(-30), thirty.FromUtc)')
    && rankedTests.includes('Assert.Equal(operations.Config.Season.Name, season.SeasonName)')],
  ['口径组件仅负责折叠展示', scope.includes('defineProps') && scope.includes('<details v-if="items.length">')
    && scope.includes('.statistics-scope details[open]{grid-column:1/-1}')
    && !scope.includes('position:absolute') && !scope.includes('fetch(') && !scope.includes('platformRequest')],
  ['排行榜显示服务端窗口与统一排除范围', rankings.includes('<StatisticsScope')
    && rankings.includes('parseScopeDate(analytics.value.fromUtc)')
    && rankings.includes("analytics.value.range === 'season'")
    && rankings.includes('tab !== \'history\' && hasAnalytics')
    && rankings.includes('暂扣、作废、系统异常')
    && rankings.includes('未按运营规则版本或卡效版本拆分')],
  ['公开主宰低样本不着强弱色', rankings.includes('const publicMasterSampleMinimum = 30')
    && rankings.includes(':minimum-sample="publicMasterSampleMinimum"')
    && rankings.includes('crediblePercent(row.winRate, row.games)')
    && rankings.includes('leftSamples < publicMasterSampleMinimum ? rightSamples - leftSamples')
    && rankings.includes('场是展示提醒，不是结算门槛')],
  ['主宰矩阵低样本悬浮说明不泄露胜率方向', matrix.includes('value.samples < props.minimumSample')
    && matrix.includes('不足 ${props.minimumSample} 场，仅显示样本')
    && matrix.includes("initiativeTitle('先手', value.firstWins, value.firstSamples)")
    && matrix.includes('samples >= props.minimumSample')],
  ['近30日最强称号独立标注', rankings.includes('最强玩家<small>近30日</small>')
    && rankings.includes('最强玩家称号另按近 30 日独立口径产生')],
  ['公开牌库显示可公开样本但不补低样本', publicDeck.includes('<StatisticsScope')
    && publicDeck.includes('可展示 ${details.value.matchStatistics.games} 场')
    && publicDeck.includes('低于门槛的组不会返回场次、胜负或胜率')],
  ['后台主宰口径绑定成功响应', adminMaster.includes('<StatisticsScope')
    && adminMaster.includes('appliedScope.value = nextScope')
    && adminMaster.includes('v-if="report && appliedScope"')
    && adminMaster.includes('当前卡效版本') && adminMaster.includes('30 份仅为展示提醒')],
  ['后台单卡口径绑定成功响应', adminCard.includes('<StatisticsScope')
    && adminCard.includes('listAppliedScope.value = nextScope')
    && adminCard.includes('detailAppliedScope.value = nextScope')
    && adminCard.includes('先后手：${query.initiative')
    && adminCard.includes('服务端未返回样本汇总')
    && adminCard.includes('不把低样本相关性写成因果')],
]

assert.deepEqual(checks.filter(([, passed]) => !passed).map(([label]) => label), [])
console.log(`统计可信度契约通过：${checks.length}/${checks.length} 项`)
