# 天灾禁选统计接入实施合同

状态：2026-10-17 Main已接收并采用“卡效对抗测试”的DSC-01只读合同。代码审查基线f334127b60f8223bed919be5c01d3d6c4679ee37；本文件不是已实现、验收或部署证明。来源对话01a0dbc8-0071-7bc0-a1bd-f8f5ff5dd543，Main承担实现分批、验收及发布。

## 目标与分期

1. 首期：新局权威准备事实、原子持久化、幂等/恢复、留存隔离、内部只读查询。
2. 第二期：现有后台主宰分析、对局明细与先后手对照；不另造后台页面。
3. 第三期：卡牌与牌库统计复用同一范围、覆盖说明和缓存键。
4. 第四期：个人、公开与赛事的按权限薄投影；公共低样本与差分查询隐私另验。

扩展P1/P2/P3既有端口及事实体系，不改P0结算、P4页面方向、排位积分或历史赛果；不是第二套结算/统计真相。首期无公开接口，不延长录像期限。

## 数据与写入

- 现有MatchRecorder.CardFacts.InsertFactAsync与CardAnalytics的eligible SQL只收ranked；其压缩/细节清理不适合直接承载多模式禁选。默认新增MatchRecorder.DisasterFacts.cs及三张小关联表，复用同一matches主记录/命令事务，不把赛事混入现有排位样本。
- capture按match_id唯一：版本、原生/可证回填来源、冻结引擎行为版本、真实初始卡池、模式、公开牌、无序最终构成及来源、准备状态、覆盖等级、完成命令序号。身份/赛季/主宰/先后手/结果关联已有matches和participants，不复制昵称、牌库或手牌。
- decision按(match_id,step)唯一：逻辑步0/1/2禁用、4/5选择，操作者、合法提供/解决的命令序号与revision、选中卡及可信来源；提供时未决，成功后一次补结果。
- candidate按(match_id,step,card_id)唯一，只记真实合法提供的身份，不代表浏览器收到或人眼看见。不存完整Prompt、图片、长文案、全状态或秘密洗牌顺序；单局增长随准备步骤/池大小有界，不随回合增长。
- L12PromptsAndSetup从真实BuildDisasterPool、PromptDisasterBan/Pick、ResolveDisasterBan/Pick、FinishDisasterPreparation观察现有结果，不二次抽样。固定湮灭、随机公开、随机模式与自主选择分别标来源；真正触发天灾不是最终构成，首期不提供触发因果分析。
- 小型内部观察缓冲不进入公开投影、State/Journal/hash、既有CardFactSignalSequence或RNG；权威仍是原成功结算，不增加玩家ActionEvent。原生初始步骤须被捕获；只传state/晚起录制/旧局无capture不得标exact。
- StartCoreAsync、AppendWithCardFactsAsync和CompleteAsync/排位终态沿既有事务落库；提交成功才释放观察缓冲。重复序号同值幂等且核对事实，不得用INSERT OR IGNORE吞掉同键不同候选/操作者/结果。拒绝、重复requestId与回滚不增加样本。
- RegisterRecoveredEngine清除重放观察缓冲；恢复重建不写历史事实，下次Append不得重计。检查点/尾部恢复与直跑的最终事实集合必须一致。
- timeout来源必须由L12RankedClock真实超时入口通过内部调用传入；用户入口固定player。command.Destination仅是当前日志字段，不可信作统计来源。公开确认超时不算选择。

## 迁移、留存与历史

- 仅增表/索引，沿现有match_recorder_schema登记独立disaster-setup-analytics/version1；DDL与标记原子且可重入，不改变原CardFact/Journal/状态版本。
- 首期原生新局覆盖ranked/casual/friendly/tournament；sandbox/custom/skipPreparation不进自主偏好。旧运行中局不冒充新原生完整记录。
- PlayerReplayRetention/TestRunRetention的PurgeReplayPayloadAsync与卡事实压缩/清理不删除新事实；清理前后查询一致，不能为统计保全部录像。整局删除显式处理子表，不假定所有连接启用了外键。
- 默认不回填旧库。旧无capture为unknown，不是未禁/未选；后续只有当时真实候选/映射/事件足够证明时才单独verified-backfill，缺失不以当前规则/卡池反算。不得索取或公开玩家隐藏牌库。

## 查询口径与隐私

- 已结束、结果明确、无error、准备完成且相关指标exact才进对应偏好/胜率子样本；同时返回总体、可用、未知、未完成及不适用数，不静默改写原总战绩。
- 禁用率=选中禁用次数/该卡合法禁用机会，选择率=选中次数/该卡合法候选机会；分别报告独立对局数、先后手/逻辑步、主动/超时。先手两禁、后手一禁及3/2候选曝光不得共用错误分母。未决中断另计，零分母为null。
- random/none/固定牌与公开随机牌不进入自主偏好分母，不能混分子/分母的来源范围。
- 内部一致只读快照关联已提交排除集；排除集不可用时失败关闭，不当空集。首期不新增HTTP/WS公共入口或投影字段。
- 第二期AdminMatchesRead沿既有赛后权限给逐局摘要；活跃局无私密候选，AdminAnalyticsRead仅聚合不能顺带取matchId。普通玩家/观战者赛后也不自动获得对方未公开候选；需新增公开产品规则时另审，不暗扩裁判权限。

## 组件接入

- 第二期GET /api/admin/analytics/masters → CardAnalyticsQuery/ReadMasterAnalyticsAsync → AdminMasterAnalyticsReport，复用AdminMasterAnalyticsPanel的StatisticsScope，增加紧凑禁/选分布、先后手与覆盖说明；MasterMatchupMatrix展开明细复用该查询，不塞满矩阵格。
- adminApi.masterAnalytics和服务端同步校验opponent/initiative/卡池/范围参数，不能只做前端筛选。查询范围必须纳入缓存键、合并请求、失效与迟到回复边界。
- GET /api/admin/matches/{id} → GetAdminMatchAsync → AdminMatchDetail/AdminMatchesPanel，追加独立于录像载荷的准备摘要；includeReplay=false或录像已过期仍可读。纯展示组件只接已授权薄DTO，不能先发原始候选再隐藏。
- 后续卡牌/热门构筑、Profile、MatchRecords、公开牌库及赛事复用指标服务，不将相关性说成因果；赛事按真实recordedMatchId，轮空/人工判罚/未开局不造禁选样本。

## 首期退出门槛

- TwelveLegions.Tests验证真实先后手交换、逻辑步骤/实际候选、all/season/random/none、固定牌与RNG/hash/旧fact序号不变。
- TwelveLegions.Platform.Tests验证初始化重入/迁移中断、Start/Append/末次pick原子故障、同值重试和冲突拒绝、旧提示/错人/重复请求、各节点checkpoint+tail恢复、不重复计数及有界缓冲。
- 新旧局/晚起录制覆盖等级正确，真实服务端timeout与伪造Destination不混，准备中断/未知/不适用及零分母不误计。
- 录像/测试服/卡事实清理前后事实一致，原清理期限不变，沙盒删除不留孤行；只读查询/排除集失败/权限及活跃隐私守卫有效。
- 合成查询计划命中有限必要索引，准备外长局不增加禁选行或全状态复制，不外推真实公网容量。
- Main统一跑受影响的完整规则/平台与既有架构门禁；首期无前端改动复用未失效前端证据，不重复未变线上采样。完成首期不能冒称UI已接入、旧数据全量补齐、正式上线或所有统计完成。
