# T01 过渡期排位作废与真实开季迁移

状态：本地候选设计；禁止直接写生产数据库。适用范围仅限当前活动赛季 `T01`。

## 裁定与目标

本次 T01 的正式开始时间固定为北京时间 **2026-10-02 00:00**（UTC `2026-10-01T16:00:00Z`）。此前已经产生、并经预览核实的 T01 排位属于过渡期：保留后台原始赛果和完整性证据，但不再影响玩家档案、七曜、定级、胜负、隐藏匹配分、排行、统计、主宰统计、广播、排位奖励或玩家完整性通知。旧 S01 归档、奖励和赛季总结不得改变。

修复不是任意赛季重置接口。它只能执行一次；成功后产生的新 T01 对局必须正常计分，任何重试都只能返回第一次修复结果。

## 执行边界

1. 先由只读预览在平台快照中识别过渡期事实，并返回证据指纹、对局数、账本行数及玩家影响数。
2. 应用请求必须携带预览指纹、运营配置版本、幂等键和人工原因。
3. 应用在排位入场闸门内执行；闸门关闭新排位后，必须等待 active、pending、reconciliation-failure、quarantine 四项全部为零。
4. 闸门内重新计算事实指纹。与预览不完全一致时拒绝；不得自动吸收预览后新增的对局。
5. 单次最多处理 500 场。超过上限、出现孤儿事实或无法证明完整链时失败关闭，不拆成多个会留下中间可见状态的批次。
6. 预览与应用须同时传入 `competitiveStartAt=2026-10-01T16:00:00Z`；该目标时间进入证据指纹，应用时更换会拒绝。T01 的玩家可见 `StartsAt`、运行配置 `Season.StartsAt` 与 marker 的 `CompetitiveStartAt` 原子地设为此时间；`AppliedAt` 仍记录实际执行时间。原始 `ActivatedAt` 是激活审计事实，保持不变。既定 `EndsAt` 不延后；若目标时间后已有排位事实，或执行时已到赛季结束时间，失败关闭，不得静默作废正式开季后的对局。

## 权威过渡期集合

以 `RankedIntegrityAudits` 中 `SeasonId=T01` 且终局时间不晚于修复切点的 matchId 为主集合。集合中的事实必须满足：

- 每个 matchId 恰有一条完整性审计，双方不同且终局类型、胜者合法；
- 胜负或平局结算恰有双方两条账本；新版账本为 T01，旧版空 `SeasonId` 只在原 T01 激活时间之后接受；
- 胜负局恰有一条双方档案前后快照；平局没有档案变化；
- 结算账号、胜负、派系、时间和审计一致；同一账号的逐局 before/after 快照按时间形成连续链；
- 当前档案与最后一个实际已应用快照一致。被暂扣且从未应用的事实不得被当作当前档案变化；
- T01 主宰聚合可由未作废且已应用的审计逐项重建并与现值一致；
- 不允许未知赛季、孤儿结算、重复快照、缺快照、链断、已结算 T01、T01 历史档案或 season-final 奖励；
- 生效的 confirmed 限制或未决申诉不由本迁移静默解除，存在时拒绝并要求单独人工裁定。

证据指纹是上述排序后的稳定事实投影的 SHA-256，至少包含 matchId、审计双方/胜者/主宰/终局时间、两席结算核心字段、档案快照、暂扣/处置状态以及所有会被清理的派生事实标识。仅比较数量不构成授权。

## 原子变更

同一个平台存储事务内完成：

- 所有 T01 档案的七曜、定级、胜负、连胜负、保底、最高段标志和已选主宰称号归零；派系保留；
- 参加过渡局的账号把隐藏匹配分恢复到其第一场过渡局的权威 before 快照；其他账号隐藏分不变；
- 删除 T01 过渡期主宰聚合、matchId 对应广播和广播投递；
- 撤销 `rank-reached/T01` 与 `ranked-participants:T01` 的过渡期异画权益；
- 原始 settlement、profile fact、integrity audit/decision/correction/held reward 和滚动主宰事实不物理删除；
- 写入永久 marker，保存真实开季时间、过渡 matchId、证据指纹、原/新运营版本、影响计数、操作者和原因；
- 写入运营历史及管理审计。

marker 中的 matchId 进入统一排除口径：玩家/公开统计、排行、主宰称号、卡牌分析、公开牌库统计均忽略；结算投影显示 `voided` 且有效增量为零；暂扣冷却不再阻止排位；玩家完整性通知与申诉列表不显示这些过渡局，后台审计读取继续保留。

marker 必须在所有其他门禁前处理幂等重放。成功后即使已有新的 T01 对局或 readiness 非零，不同幂等键重试也不能再次清零。

## 上线、核验与回滚

正式执行必须保持维护或至少排位入场关闭，并在停写状态下生成 SQLite 一致快照。部署新制品后按“预览 → 人工核对指纹/数量 → 应用 → 只读核验 → 开放排位”执行。

在唯一生产实例或已确认只有一个实例可写平台存储的前提下，使用具有
`AdminOperationsWrite` 权限的管理 token 执行：

1. 读取当前运营配置版本，记为 `OPS_VERSION`。
2. `POST /api/admin/ranked/season-reset-repair/preview`，JSON 为
   `{"seasonId":"T01","expectedVersion":OPS_VERSION,"competitiveStartAt":"2026-10-01T16:00:00Z"}`。保存返回的
   `evidenceFingerprint`，并人工核对 `transitionMatches` / `settlementRows` /
   `profileFacts` / `profilesReset` / `masterRecordsToRemove` / `grantsToRevoke` 和 `competitiveStartAt`。
   数量与当次只读事实不一致时停止，不得由脚本自动确认。
3. `POST /api/admin/ranked/season-reset-repair`，JSON 为
   `{"seasonId":"T01","reason":"<change-ticket/reason>","idempotencyKey":"<unique-key>","expectedVersion":OPS_VERSION,"expectedEvidenceFingerprint":"<preview sha256>","competitiveStartAt":"2026-10-01T16:00:00Z"}`。
   应用返回的 `operationsVersionAfter` 必须等于 `OPS_VERSION + 1`，
   `replayed=false`；仅原请求安全重试可返回 `replayed=true`。
4. 任一预览/应用请求如果返回版本、readiness、指纹、事实链、季末或数量上限错误，
   都必须停止并重新只读调查，不得跳过门禁或手改 SQLite。

核验至少确认：marker 与管理审计各一条；运营版本增加一；T01 起点为北京时间 2026-10-02 00:00，执行时间另记且结束时间不变；过渡 matchId 全部进入排除集合；全部档案可见值为零；参赛者隐藏分等于首场 before；过渡广播、主宰聚合和相关权益不可见；S01 历史/奖励逐项未变。

事务提交失败由 `ExecuteAdminTransaction` 恢复内存与持久化状态。应用成功但尚未开放排位时，可停服务、恢复执行前快照并回滚制品。一旦开放且产生真实 T01 对局，禁止整库回滚，必须另做基于新事实的补偿迁移。
