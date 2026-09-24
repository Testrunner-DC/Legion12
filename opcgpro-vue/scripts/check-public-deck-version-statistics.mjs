import assert from 'node:assert/strict'
import fs from 'node:fs'

const read = relative => fs.readFileSync(new URL(relative, import.meta.url), 'utf8')
const models = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.cs')
const recorder = read('../../服务端WebSocket/TwelveLegions/MatchRecorder.PublicDecks.cs')
const server = read('../../服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')
const tests = read('../../TwelveLegions.Platform.Tests/PublicDeckMatchBindingTests.cs')
const platform = read('../src/l12/platform.ts')
const detail = read('../src/l12/site/PublicDeckDetailPage.vue')

const checks = [
  ['公开DTO只声明聚合统计', models.includes('L12PublicDeckVersionStatisticView') && models.includes('L12PublicDeckMatchStatisticsView') && models.includes('L12PublicDeckMatchStatisticsView MatchStatistics') && !models.includes('L12PublicDeckMatchView')],
  ['聚合键限定为版本和双方主宰', recorder.includes('GROUP BY b.version,player.master_id,opponent.master_id') && recorder.includes('COUNT(*)') && recorder.includes('SUM(CASE WHEN m.winner=b.player_index')],
  ['统计窗口固定为最近90天且小样本不返回', recorder.includes('PublicDeckStatisticsRecentDays = 90') && recorder.includes('PublicDeckStatisticsMinimumGroupGames = 3') && recorder.includes('m.ended_utc>=$from AND m.ended_utc<=$to') && recorder.includes('group.Games >= PublicDeckStatisticsMinimumGroupGames') && recorder.includes('"insufficient"')],
  ['查询结果不返回单局与录像', recorder.includes('PublicDeckVersionStatisticsAsync') && !recorder.includes('PublicDeckMatchesAsync') && !recorder.includes('viewerAccountId') && !recorder.includes('ReplayPath') && !recorder.includes('ListRecentPlayerReplayMatchesAsync')],
  ['公开接口对所有访问者复用同一匿名统计', server.includes('PublicDeckDetailsWithStatisticsAsync(id)') && !server.includes('PublicDeckDetailsWithMatchesAsync') && server.includes('MatchStatistics = statistics')],
  ['前端数据契约没有单局与录像字段', platform.includes('PublicDeckMatchStatistics') && platform.includes('PublicDeckVersionStatistic') && !platform.includes('matchId: string; version: number; playedAt') && !platform.includes('replayPath: string | null')],
  ['页面只显示聚合量与时间范围', detail.includes('details.matchStatistics.groups') && detail.includes('stat.games') && detail.includes('formatRate(stat.winRate)') && detail.includes('matchStatisticsRange')],
  ['页面没有单局与录像入口', !detail.includes('match.matchId') && !detail.includes('match.playedAt') && !detail.includes('match.result') && !detail.includes('replayPath')],
  ['后端测试验证序列化匿名性与时间窗口', tests.includes('AssertPublicStatisticsAreAnonymous') && tests.includes('Assert.Equal(90, expired.RecentDays)') && tests.includes('Assert.Empty(expired.Groups)')],
]

assert.deepEqual(checks.filter(([, passed]) => !passed).map(([label]) => label), [])
console.log(`公开牌库版本匿名统计契约通过：${checks.length}/${checks.length} 项`)
