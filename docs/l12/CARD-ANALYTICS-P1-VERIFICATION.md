# 批次 U · 核验流程（2026-11-24 起）

U 批次的改动踩到过"台账说未开工、代码其实早已实现"的坑（批次 S 的 S1/S2/S3 就是这样）。为使改动不再出错，每个 U 改动点按下列五道关走，缺一道不提交。

## 0. 只读清点（先于任何实现）
对台账每一条，先在权威树里找证据并给出结论：**已实现 / 部分实现 / 未实现**，每条附 `file:line`。
台账行号是历史参考，落点必须在权威树重新定位。清点结论写回台账状态列，避免重复造轮子。

## 1. 冻结口径（改行为之前）
统计口径已由 BATCH-20260908-286 冻结。改动前必须先确认这些守卫是绿的，且改动后逐字段不比改动前更少：
- `AdminGovernanceStatisticsLinkTests`（分母/样本数/EligibleMatches）
- `CacheIdentityTests`（缓存键归一化：分页与排序不得互相复用）
- `CardAnalyticsFactsLoadedTests`（未读事实必须可辨识，不能被当成 0）

## 2. 契约先行（红 → 绿）
每个改动点先写一条会失败的测试，再改到绿。测试要能单独跑：
```powershell
. scripts/lib/l12-test-storage.ps1
Invoke-L12TestRun -Executable dotnet -Arguments @('test','TwelveLegions.Platform.Tests\TwelveLegions.Platform.Tests.csproj','--filter','FullyQualifiedName~<类名>','--artifacts-path','D:\GPT\Legion12\cache\primary\build-u1','-v','q','--nologo') -TemporaryBase 'D:/GPT/Legion12/cache/primary/t/u1' -EvidenceBase 'D:/GPT/Legion12/artifacts/test-runs' -Label 'u-<点>'
```

## 3. 等价性对照（仅"改执行、不改口径"的重构）
⑨（分组合并、TEMP 索引维度）属于此类：落库前必须给出**同一夹具下新旧输出逐字段相同**的证据（同一查询的 total、items 顺序与数值、detail 的 breakdown/分布）。只允许新增字段与耗时改善，不允许数值漂移。

## 4. 独立复核（只读代理，不写代码）
改完派一个只读复核，只回答三问：口径变没变（给 `file:line`）、并发/取消路径有无死锁或泄漏、新增字段是否真被消费。它的结论与本轮证据一起进台账。

### 已知假绿陷阱（2026-11-24 实测）
手工给 `-ChangedPaths` 传非 ASCII 路径（`服务端WebSocket/...`）时，git 输出的路径是转义形式，
分类阶段只匹配上 ASCII 项，日志出现 `Changed files: 1` 且**根本不跑平台/规则测试**——门禁 exit 0 是假绿。
判断方法：日志里必须出现 `[L12 Focused] Platform persistence focused tests`（或 `L12 focused rule tests`）。
正确做法：不传 `-ChangedPaths`，让门禁自己从工作树取变更集。

## 5. 门禁与验收（Lead 自己跑，不信转述）
`scripts/verify-l12-change.ps1 -Level Focused` 必过；动到服务端生产 `.cs` 时跑 `TwelveLegions.Platform.Tests` 全量（串行，不用过滤器）。前端改动另跑 `vue-tsc --noEmit` 与门禁里的 `check:ui-contracts` 全链（须由 `Initialize-L12BuildEnvironment` 提供存储根，否则 `test-tutorial-contract` 会假红）。测试证据必须对应最终源码；改完源码要重跑。

## 量测记录：TEMP 索引维度对齐（⑨③，2026-11-24）
分层比较按 `(master_id, opponent_master_id, effect_version)` 探 `temp.l12_analytics_eligible`，
而现有维度索引前缀是 `(mode_id, started_utc, ...)`——不匹配是结构性事实。但补一条对齐索引
（`ix_l12_analytics_eligible_strata`）后实测**更慢**，故不采用：

| 夹具 | 无对齐索引（5 轮冷启动中位） | 加对齐索引 | 结论 |
|---|---|---|---|
| 4 000 场 | 列表 431.5 ms / 详情 75.2 ms | 454.7 / 70.9 | 噪声内，混合 |
| 20 000 场 | 列表 2 552.5 ms / 详情 429.1 ms | 3 030.0 / 441.7 | 列表 **+18.7%**，详情无改善 |

复现：`L12_ANALYTICS_MEASURE_MATCHES=20000 L12_ANALYTICS_MEASURE_OUT=xxx.json` 后跑
`CardAnalyticsMeasurementHarness`（默认跳过，不影响门禁）。真正的大头是列表作用域本身
（20 000 场 2.55 s），属 P2「按 epoch 物化持久化作用域」，不在 ⑨ 范围内。

## 本轮清点结论（2026-11-24）
| 点 | 结论 | 证据 |
|---|---|---|
| ⑩ 缓存 per-key 单飞 + 确定性键 | **已实现** | `MatchRecorder.CardAnalyticsExecution.cs:181,195`（`_cardAnalyticsJobs`+`MarkSingleFlight`）；`CacheIdentityTests` 三项 |
| ⑧-a 列表 facts 可辨识 | **已补**（本轮） | `MatchAnalyticsModels.cs` 的 `FactsLoaded`；`CardAnalyticsFactsLoadedTests` |
| ⑧-b 详情样本不足 404→200+reason | **未实现** | `MatchRecorder.CardAnalytics.cs:172` 把"卡不存在"和"样本不足"并成一个 `null`；路由 `L12WebSocketServer.cs:581` 直接 `NotFound()` |
| ⑨ rows/total 一次分组、breakdown 合并、TEMP 索引对齐 | **未实现** | 列表与 total 各一次 `GROUP BY`（`CardAnalytics.cs:523`、`:549`）；breakdown 走 `ReadBreakdownsAsync` 逐切片（`Statistics.cs:83,101`） |

**残留风险**：⑨ 是"改执行不改口径"的重构，必须按第 3 道关给等价性对照；⑧-b 改的是 HTTP 契约，需前端同批消费 reason，否则面板仍把 404 当"卡不存在"。
