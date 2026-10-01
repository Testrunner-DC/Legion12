# 赛季内部编号归一化迁移设计

状态：本地候选已完成比赛库、平台库与跨库可恢复 orchestrator；尚未完成 Main 全量验收、发布和生产执行授权，当前不可部署执行

范围：旧内部 `S01`（实际第 0 赛季）→ `S00`，旧内部 `T01`（实际第 1 赛季）→ `S01`

硬约束：本文不授权生产写入、推送、部署或对未授权玩家权益数据的查询。

## 1. 目标与不变量

1. 第 0 赛季的规范内部编号为 `S00`，第 1 赛季为 `S01`，以后按明确的赛季序号递增。
2. 内部编号不是玩家文案。当前赛季、历史荣誉、个人页、排行、赛季总结和玩家通知只能显示自然名称或“第 N 赛季”，不得显示 `S00`、`S01` 等内部键。
3. `DefinitionId`、`ArchiveId`、比赛 ID、玩家账号 ID 等稳定身份不变；只迁移明确类型化的赛季引用。
4. 旧赛季的历史、奖励、总结、排位事实和最强称号事实不丢失、不重复发放。
5. `$rolling-master-title-720h-v1` 是跨赛季最强称号的特殊桶，不是赛季编号，不得迁移。
6. 卡牌 ID 中大量存在 `S01-*`；卡牌 ID 及任意非类型化文本不得参与赛季编号替换。
7. 已带校验和的原始审计、回放和结算投递证据保留原字符串；迁移只改变实时关联键，并用映射记录解释旧证据。
8. B0“过渡期排位作废并确定真正开赛点”必须先完成。S0 不得绕过 B0，也不得让 B0 在规范化后的新 `S01` 上再次执行清零。

## 2. 已知生产拓扑与执行顺序

截至 2026-10-01 的已授权只读聚合证据显示：

- 旧 `S01` 是实际第 0 赛季，已有 2,439 场排位完整性审计；最后一场在旧赛季截止前完成。
- 当前 `T01` 是实际第 1 赛季；B0 预览时必须重新读取全部过渡期事实，不能把目前观察到的场数写死。
- B0 会保存真正开赛点、被豁免的过渡期比赛集合、事实计数、指纹和操作者审计。

固定顺序：

1. 部署同时理解旧编号和迁移阶段的新版本，但仍使用旧实时编号。
2. 在旧 `T01` 上执行 B0 preview/apply，并确认唯一成功 marker 已持久化。
3. 关闭新排位入口，确认所有权威在途状态归零。
4. 执行 S0 preview，核对双库计数和指纹。
5. 分阶段迁移比赛库，再迁移平台库；进程崩溃后从 marker 恢复。
6. 验证规范编号、引用完整性、玩家端无内部编号和 B0 不可重入后再开放排位。

若 B0 尚未完成，S0 必须失败关闭。现有 B0 API 硬编码 `T01`，所以绝不能先改名后再尝试 B0。

## 3. 全引用清单

### 3.1 平台状态 JSON：必须迁移的类型化字段

平台库的 `platform_state` 是带 SHA 校验和的整份 JSON。以下字段是实时赛季引用，必须在同一个平台事务内按映射精确迁移：

- `OperationsConfig.Season.Id`
- 每条 `OperationsConfigHistory[].Config.Season.Id`
- `RankedPendingGradient.AfterSeasonId`
- `SeasonDefinitions[].SeasonId`
- `SeasonDefinitions[].PreviousSeasonId`
- `SeasonDefinitions[].NextSeasonId`
- `SeasonArchives[].SeasonId`
- `SeasonArchives[].PreviousSeasonId`
- `SeasonArchives[].NextSeasonId`
- `RankedProfiles[].SeasonId`
- `RankedSeasonResetRepairs[].SeasonId`
- `RankedSeasonResetRepairs[].PreviousSeasonId`
- `RankedProfileHistory[].SeasonId`
- `RankedSettlements[].SeasonId`
- `RankedMasterRecords[].SeasonId`，但排除 `$rolling-master-title-720h-v1`
- `RankedIntegrityAudits[].SeasonId`
- `RankedHeldRewards[].SeasonId`
- `RankedSettlementProfileFacts` 内四个 `RankedProfileSnapshotRow.SeasonId`
- held reward 内四个 `RankedProfileSnapshotRow.SeasonId`
- `RankedIntegrityCorrections.Before/After` 内的快照 `SeasonId`
- `AlternateArtAwardRules[].SeasonId`

B0 marker 的 `SeasonId`、`PreviousSeasonId` 应改为规范值，但其原始证据指纹必须保持不变，并另存 legacy→canonical 映射或原始编号，避免把迁移后的 marker 误判为新一次修复。

### 3.2 异画来源：只迁移已知编码

`AlternateArtGrants[].SourceReference` 是多用途字符串，只能按 `SourceKind` 解析以下已知格式：

| SourceKind | 格式 | 迁移方式 |
| --- | --- | --- |
| `rank-reached` | 精确赛季 ID | 精确映射 |
| `season-final` | 精确赛季 ID | 精确映射 |
| `master-champion-season-final` | `${seasonId}:${masterId}` | 只替换第一个经验证的赛季片段 |
| `ranked-participants` | `ranked-participants:${seasonId}` | 解析前缀后映射赛季片段 |

`manual`、活动来源和未知来源不改。生产异画实际数据未获单独读取授权；实现只能用本地 schema/fixture 构造 preview，并在遇到未知且看似关联目标赛季的来源时失败关闭，不能猜测或静默略过。

### 3.3 平台 SQLite 协调表

`season_finalization_coordination` 以 `(definition_id, season_id)` 为主键。应以稳定 `definition_id` 精确定位旧行，并把赛季键迁移到规范值，同时保留：

- `status`
- lease/owner 状态（执行前必须不存在有效执行租约）
- `completed_at_utc`
- `completed_storage_revision`

目标键存在时不得覆盖或合并，必须失败关闭。迁移后的 `completed_storage_revision` 仍指向原结算事实所在的平台 revision；S0 自身的新 revision 另由迁移 marker 记录。

### 3.4 比赛记录库：实时索引与不可变证据分开处理

必须迁移：

- `matches.season_id`：排行、分析和管理筛选的权威结构化键。
- 赛季键缓存或物化统计：优先失效并按规范键重建，不做字符串替换。

不改写：

- 已 applied 的 `ranked_settlement_outbox.payload_json` 与 `payload_hash`。
- 历史 runtime/checkpoint/initial state/action state/event JSON 及其 hash。
- 已封存回放和原始审计载荷。

这些历史证据中的旧编号通过 migration mapping 解释。执行前必须确认无 pending outbox；否则不能证明平台事实与 outbox 一致，必须拒绝迁移。

### 3.5 不迁移的相似字符串

- 所有卡牌 ID（例如 `S01-*`）。
- `$rolling-master-title-720h-v1`。
- `admin_audit_events` 的既有 payload 与 checksum。
- 已 sealed/applied 的回放、checkpoint、event 和 outbox payload/hash。
- 任意自由文本、原因、描述、标题、文章和通知正文。

## 4. 编号模型

不能永久实现通用别名 `S01 → S00`，因为迁移后 `S01` 同时是第 1 赛季的规范 ID。通用 `SeasonIdsEqual` 别名会把两个不同赛季错误视为同一赛季。

应给赛季定义/档案增加不可变 `SeasonOrdinal`（0、1、2……），并由它生成规范 ID：

- 0 → `S00`
- 1 → `S01`
- 2 → `S02`

兼容层必须是“迁移批次 + 稳定 definition/archive 身份 + 阶段”限定的映射，只在 S0 未完成期间生效。迁移完成后，所有实时数据只使用规范键；历史原始证据通过 migration marker 显式解释，而不是走全局等价比较。

新草稿编号由已持久化的最大 `SeasonOrdinal` 派生；不得根据名称、开始时间或字符串前缀猜测。编号冲突、序号断裂、同一序号多 definition 都必须失败关闭。

## 5. 迁移状态机与事务边界

平台库与比赛库是两个 SQLite 数据库，不能声称存在跨库原子提交。采用可恢复的分阶段状态机：

`previewed → recorder_committed → platform_committed → verified`

marker 至少记录：

- migration ID/version
- 显式映射 `S01→S00`、`T01→S01`
- 两个稳定 definition ID / archive ID 与 ordinal
- B0 marker ID、competitiveStartAt 和证据指纹
- preview 时两库 revision/schema version、精确计数和 SHA-256 指纹
- 每阶段开始/完成时间、owner、结果
- recorder/platform 提交后的 revision/checksum
- 操作者、原因和 verification 结果

### 5.1 全局前置门禁

执行 apply 前重新获取事实，不能复用旧 preview：

- 服务 maintenance / 排位入口关闭，且实例持有单写者迁移 lease。
- 当前赛季仍是旧 `T01`，前一档案仍是旧 `S01`，稳定 ID/链接与 preview 一致。
- B0 正好存在一个成功 marker，且它覆盖当前时刻之前全部过渡期排位事实。
- ranked active、pending、reconciliation、quarantine、held readiness 和权威 runtime 均为 0。
- ranked settlement outbox pending 为 0。
- 不存在 armed、waiting、executing 的赛季激活计划；若存在必须显式撤销/重绑，不能静默改 `IntentKey`。
- 不存在有效 finalization lease，两个目标 season coordination 均无冲突目标键。
- `S00` 不存在任何未由该 migration marker 拥有的目标数据。
- 当前时间早于当前赛季 `EndsAt`，且迁移不会改变 `StartsAt`、`EndsAt` 或 `ActivatedAt`。
- preview 总量不超过各集合明确的安全上限；超限只拒绝，不截断。

任何计数、revision、checksum、definition linkage 或 B0 fingerprint 漂移都使 apply 失败；必须重新 preview。

### 5.2 阶段一：比赛库

在 `BEGIN IMMEDIATE` 内：

1. 复核 recorder revision、门禁和 preview fingerprint。
2. 通过迁移专用临时 sentinel 做精确交换，避免 `S01` 目标与即将迁入的 `T01` 冲突。
3. 只更新 `matches.season_id`；不改不可变 JSON/hash。
4. 失效受赛季键影响的分析缓存。
5. 写 `recorder_committed` marker、每个旧值/新值更新条数和提交 checksum。

事务失败则全部回滚。进程在提交后崩溃，重启依靠 recorder marker 跳过已完成阶段；不得再次交换。

### 5.3 阶段二：平台库

在平台 SQLite `BEGIN IMMEDIATE`（或现有等价的 `ExecuteAdminTransaction` + 最新 state 重载）内：

1. 读取并验证事务内最新 `platform_state`、checksum 和 storage revision。
2. 复核 recorder marker 已完成、B0 marker 和全部 readiness 门禁仍成立。
3. 按第 3 节清单迁移所有类型化字段。
4. 迁移 `season_finalization_coordination` 的精确主键行。
5. 保存新的 platform state/checksum/revision。
6. 写独立 append-only 管理审计与 `platform_committed` marker。

platform state、协调表键和完成 marker 必须在同一 SQLite 事务提交。不得先改内存再单独 `Save()`，也不得从旧 `_data` 覆盖数据库最新 state。

### 5.4 崩溃与回滚

- recorder 提交前失败：双库无变化，可安全重试。
- recorder 已提交、platform 未提交：服务保持 maintenance，兼容读取仅根据 migration marker 接受“recorder 新键 + platform 旧键”；重启继续平台阶段。
- platform 事务失败：平台变化全部回滚，仍从 `recorder_committed` 恢复。
- platform 已提交：禁止整库降级回旧 ID，因为之后可能已有规范键新写入。只能通过经新 preview 的补偿迁移处理。
- 完成 verification 前不得恢复新排位入口。

备份应在所有 writer 停止或锁内进行，使用 SQLite backup API / 一致性 checkpoint；不能复制单个 `.db` 而遗漏 WAL。备份路径、文件 hash、schema/user version 和恢复演练结果写入审计，但备份不替代事务门禁。

## 6. B0 与 legacy 空 SeasonId

B0 处理的过渡期 settlement 可能是旧版本写出的空 `SeasonId`。S0 不应按时间范围猜测所有空值归属。仅可对 B0 marker 中已锁定的 match IDs / settlement facts 做确定性关联：

- 若业务决定补齐实时类型化引用，可把 marker 明确覆盖的空 `SeasonId` 填为规范 `S01`，同时保留原值、B0 指纹和豁免状态。
- 若 settlement 被视为不可变原始证据，则保留空值，并由 B0 marker 关联；所有玩家可见/排行/奖励查询必须排除 waived transition facts。

实现前需在这两种策略中作一次明确裁定。无论选择哪一种，均不得按 `SettledAt >= ActivatedAt` 批量归属未知空 settlement；该规则只能作为 preview 阻断证据，不能作为静默改写依据。

迁移后 B0 API 必须：

- 对同一个已完成 marker 返回幂等已完成状态；
- 对规范 `S01` 的任何新请求失败关闭；
- 绝不再次清空第 1 赛季真正开赛后的新比赛。

## 7. 玩家端编号隐藏

后台可保留内部编号。玩家端 API/view model 应输出稳定自然名称与 `SeasonOrdinal`/显示序号，前端不得用 `seasonId` 作 fallback。

已发现的直接泄漏点包括：

- `opcgpro-vue/src/l12/site/RankingsPage.vue` 显式渲染 `row.seasonId`。
- `opcgpro-vue/src/l12/site/ProfilePage.vue` 在缺少名称时 fallback 到 `seasonId`。

后续 UI 批次还须逐页审计：`BattleHubPage.vue`、历史荣誉、我的排位、排行、赛季总结弹窗、站内通知/广播。缺少自然名称时只能按 ordinal 生成“第 N 赛季”；若 ordinal 也缺失，应隐藏或显示通用“赛季”，不能回退到内部代码。

操作后台、完整性审计和管理分析可显示内部 ID，但必须同时显示自然名称/definition identity，降低旧证据映射歧义。

## 8. 验证矩阵

实现前先用生产形状的脱敏 fixture 写红测，至少覆盖：

1. 旧 `S01` 档案 + 当前 `T01` + B0 marker 的完整双库迁移。
2. profiles/history/settlements/profile facts/master records/audits/held/corrections/award rules/grants/operations history 全引用计数与指纹。
3. 卡牌 ID `S01-*`、自由文本和 rolling-master 特殊桶完全不变。
4. `S00` 目标碰撞、缺 B0 marker、B0 漂移、definition 链接异常均拒绝。
5. active/pending/reconciliation/quarantine/held/outbox/runtime 任一非零均拒绝。
6. armed/waiting/executing activation plan 拒绝；无关 ops revision 变化不应被错误解释为赛季引用。
7. 双 Store / 双实例并发只有一个 migration lease owner。
8. recorder 事务前崩溃回滚；recorder 提交后崩溃可恢复；platform 事务中崩溃完整回滚。
9. 重复 apply 幂等，完成后永远不能再次交换编号。
10. operations history rollback 不会恢复旧实时赛季 ID。
11. analytics 按规范 ID 查询；缓存失效/重建后不存在双计数。
12. applied outbox、回放、checkpoint、admin audit 的原 hash 仍通过校验。
13. 最强称号跨赛季 720h facts、冠军、授予记录在迁移前后等价。
14. B0 legacy 空 SeasonId 事实按最终裁定处理，waived 数据不进入玩家排行、战绩、通知、奖励或主宰统计。
15. 历史荣誉使用冻结的历史事实和对外账号名，不用当前 active account 重算，不显示内部 season ID。
16. 所有玩家界面和通知 contract 测试证明 `S00`/`S01` 不可见；后台 contract 仍可追踪内部编号。

门禁顺序：focused migration tests → 完整平台测试 → recorder/analytics tests → 前端 type/build/UI/browser 视口 → Batch。任何 fixture 未覆盖的集合或未知 SourceKind 都是阻断项，不允许以 warning 继续。

## 9. 建议实现边界与文件租约

高概率需要的后端边界：

- `服务端WebSocket/TwelveLegions/L12PlatformStore.Seasons.cs`
- `服务端WebSocket/TwelveLegions/L12PlatformStore.Ranked.cs`
- `服务端WebSocket/TwelveLegions/L12PlatformStore.AlternateArts.cs`
- `服务端WebSocket/TwelveLegions/L12PlatformStore.TransactionalStorage.cs`
- `服务端WebSocket/TwelveLegions/L12PlatformStore.AdminControlPlane.cs`
- `服务端WebSocket/TwelveLegions/MatchRecorder.cs`
- 赛季筛选/缓存相关 `MatchRecorder.*.cs`
- 对应 platform、season activation、match recorder 测试

玩家端编号隐藏建议作为迁移后的独立 UI 批次，避免和历史荣誉 U1 交叉写：

- `opcgpro-vue/src/l12/site/RankingsPage.vue`
- `opcgpro-vue/src/l12/site/ProfilePage.vue`
- `opcgpro-vue/src/l12/site/BattleHubPage.vue`
- 相关 API types / player summary components 与 UI/browser tests

运营配置分区独立保存 C1 不属于 S0。本迁移不得顺手更改 C1 的 section revision / patch 语义；若 operations/activation 文件租约冲突，应由 Main 排序后再实施。

## 10. 已裁定事项与剩余实施阻断

Main 已裁定：

1. B0 legacy 空 `SeasonId` settlement 保留为不可变原始证据，只由 marker 的精确 match ID 关联，不按时间猜测补写。
2. `SeasonDefinition`/`SeasonArchive` 增加不可变 `SeasonOrdinal`；旧两季只按本迁移显式赋值 0/1，未来从持久化最大 ordinal 生成。
3. 双库使用分阶段 marker 和单 owner lease；半完成状态保持排位关闭并可恢复，不声称跨库原子。

本地候选已实现：

1. 比赛库仍只暴露内部原子换键原语；平台 orchestrator 在同一排位 cutover gate 内先排空 outbox、复核 readiness，再依次提交 recorder、平台和 verification。
2. 平台迁移从 SQLite 事务内最新 `platform_state` 反序列化；类型化引用、`season_finalization_coordination` 主键、平台快照/checksum、独立审计和完成 marker 在一个 `BEGIN IMMEDIATE` 写事务提交。
3. `platform_season_identity_migrations` 以固定 migration ID 提供单 owner 租约、过期接管、`platform_committed` 恢复和永久 `verified` 幂等标记；`executing`/`platform_committed` 阶段持续封闭新排位。
4. recorder 已提交而平台失败时，重启后以 recorder 完成指纹恢复；旧 owner、漂移指纹、目标碰撞、未知异画引用、B0 缺失/覆盖漂移和未清零 readiness 均失败关闭。
5. `SeasonOrdinal` 已写入 definition/archive；迁移只给旧两季赋 0/1，后续草稿从持久化最大 ordinal 生成 `S02` 等规范键，已赋 ordinal 不能改成其他内部键。

仍有发布阻断：

1. Main 尚未完成平台全量、前端/UI、Batch 与候选集成验收；本文不构成发布或生产执行授权。
2. 玩家端隐藏编号与 U1 历史荣誉/派系冻结值须同批完成，避免规范内部键在玩家界面外显。
3. 未获授权的生产异画权益仍未读取；实现只用本地 fixture 验证所有已知编码，并对未知疑似赛季引用失败关闭。

## 11. 受控执行与恢复步骤

以下只描述已实现接口；生产执行仍须单独授权，并且必须先按 B0 runbook 完成 preview/apply。

1. 启用即时维护，确认排位入口已关闭；等待 active、pending、reconciliation、quarantine、held/runtime 全部为 0，并排空 settlement outbox。
2. 对 `POST /api/admin/seasons/identity-normalization/preview` 发送当前 `expectedVersion`（或 `If-Match`）。只在 `canApply=true`、`blockingCodes=[]`，且人工核对 definition IDs、B0 fingerprint、双库 fingerprints 后继续。
3. 对 `POST /api/admin/seasons/identity-normalization` 发送 preview 返回的两个 fingerprint、8-80 位唯一 `idempotencyKey`、明确原因与同一当前 `expectedVersion`：

   ```json
   {
     "expectedPlatformFingerprint": "<preview.platformFingerprint>",
     "expectedRecorderFingerprint": "<preview.recorderFingerprint>",
     "reason": "归一第0/1赛季内部编号",
     "idempotencyKey": "season-id-normalization-s00-s01-v1-run-001",
     "expectedVersion": 0
   }
   ```

4. 成功结果必须为 `status=verified`。随后重新 preview，确认仍为 `verified`、双库无漂移、平台 marker 的 `completed_storage_revision` 精确等于包含迁移事实的平台 revision；核对旧 finalization 行只改 season key，原 `completed_storage_revision` 不变。
5. 保持维护，完成玩家 API/UI 无内部编号、B0 旧请求幂等重放、规范 `S01` 新请求拒绝、排行/历史/最强称号一致性验收后，才可由获授权操作者解除维护。

恢复规则：

- `executing` 且 recorder 未提交：租约到期后重新 preview/apply；不得手工交换键。
- recorder 为 `recorder_committed`、平台仍 `executing`：保持维护，使用新 preview 的 fingerprints 重新 apply；平台从事务内最新状态继续。
- 平台为 `platform_committed`：排位仍封闭；重复 apply 只完成 recorder 漂移校验和平台 verification，不重复换键。
- `verified`：任何重复 apply 都只能幂等返回；若 preview 报 completed-state drift，保持维护并停止，不得重跑或回滚整库。
- 任一步失败都保留双库 marker 与审计。不得删除 marker、改 applied outbox payload/hash，或用 JSON fallback 绕过 SQLite 协调表。
