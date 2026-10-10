# LC-03B｜Journal V2 损坏、失败关闭与安全回退

状态：L12-main 已认可 B0 并批准 B1；B1 候选实现与本地专项验收完成，待 Main 独立验收、整合、提交与推送。未部署。

基线：`b24c83da1a7e63040dad8fa3a4bcffd0d66dcbb1`；分支 `codex/lc03b-journal-failure`；独立工作树起始状态干净。

## 1. Main 对齐结论

- LC-03B 是当前恢复路线最高优先级；本阶段只用全合成 SQLite 副本验证 Journal V2 的物理损坏、回退条件、失败关闭和同库隔离。
- 唯一恢复体系保持为 `MatchRecorder`、Journal V2、`L12PersistenceContract`、既有检查点、请求去重和状态哈希；禁止测试影子恢复器、禁止用当前规则猜测坏记录。
- 允许对测试生成的隔离 SQLite 数据库进行 SQL/BLOB/文件字节级注入；禁止读取、复制或提交任何测试服、正式服或真实玩家数据库。
- 最新检查点损坏时，只有从更早有效检查点到最新已提交边界之间的命令全部存在、连续、可解析，且逐步 accepted/revision/hash 一致，才允许回退；否则整场失败关闭。
- B0 红灯回传前不得修改产品代码。B1 候选租约仅可能包含 `MatchRecorder.Journal.cs`，必要时再申请 `MatchRecorder.RankedPersistence.cs` 或 `L12PersistenceContract.cs`。

## 2. 分目标

### G1｜检查点物理完整性

用序号 0、32、64 三层真实 Brotli 检查点建立恢复链，分别攻击最新检查点的 BLOB、编码长度、哈希和随机状态。确认损坏不能被当作有效状态；符合完整尾链条件时记录当前实现是否能回退到序号 32。

### G2｜命令尾链真实性

攻击检查点后的真实 `match_events`：半 JSON、缺少/非法 type、accepted、revision、state_hash 和 sequence。恢复只能按存档结果逐步重放，任何分叉都失败关闭。

### G3｜重复与已提交边界

证明数据库主键拒绝真实重复 sequence；同 requestId 的逻辑重复不得形成第二条已提交命令；失败事务不能改变恢复出来的权威命令序号和请求去重基线。

### G4｜重复恢复、继续写入与证据保全

同一健康副本连续恢复两次必须得到相同完整状态、随机状态、命令序号和请求去重集合。恢复后继续追加必须使用更高序号，且不得删除或覆盖既有检查点、命令或人为损坏的较旧证据。

### G5｜隔离与整体失败关闭

同一可读数据库中的坏对局不得阻塞健康对局恢复。整 SQLite 文件尾部截断只用于证明数据库整体失败关闭，不用于宣称同库行级隔离。

## 3. 合成夹具

- 每个测试从固定卡表、固定种子和 GM `setLife` 命令生成两个完全合成的 v2 对局；不含外部账号、凭据、真实房间或玩家数据。
- 主目标对局生成 65 条命令，形成 sequence 0、32、64 检查点与 sequence 65 尾命令；健康对局保留独立命令链。
- 使用 SQLite `BackupDatabase` 复制干净模板，再在副本上注入单一故障。副本位于测试临时目录，失败消息打印矩阵 ID 与路径；不提交数据库二进制。
- 每项攻击在修改前记录目标行的 hash、尺寸、序号和 SQL；需要验证证据保全时比较修改后的原始 BLOB/字段字节。

## 4. B0 攻击矩阵

| ID | 攻击 | 期望产品语义 | 修复前实测 |
| --- | --- | --- | --- |
| CP-01 | 最新 checkpoint BLOB 尾部截断 1 字节 | 尾链完整则回退上一有效 checkpoint 并重放到最新 | **红：长度校验失败，未尝试较早 checkpoint** |
| CP-02 | 最新 checkpoint BLOB 截为半帧 | 尾链完整则回退；不得吞掉损坏证据 | **红：长度校验失败，未尝试较早 checkpoint** |
| CP-03 | `uncompressed_bytes` 小于真实值 | 失败关闭或安全回退，绝不接受 | 绿 |
| CP-04 | `uncompressed_bytes` 大于真实值 | 失败关闭或安全回退，绝不接受 | 绿 |
| CP-05 | 最新 checkpoint `state_hash` 错误 | 尾链完整则安全回退，否则失败关闭 | **红：哈希校验失败，未尝试较早 checkpoint** |
| CP-06 | 最新 checkpoint `revision` 错误 | 尾链完整则安全回退，否则失败关闭 | **红：哈希校验失败，未尝试较早 checkpoint** |
| CP-07 | `random_state_blob` 截断 | 失败关闭或安全回退，绝不重新播种 | 绿 |
| CP-08 | `random_state_version` 不支持 | 失败关闭或安全回退，绝不降级随机源 | 绿 |
| CMD-01 | 尾命令 JSON 截为半记录 | 整场失败关闭 | 绿 |
| CMD-02 | 尾命令缺少 `type` | 整场失败关闭 | 绿 |
| CMD-03 | 尾命令 `type` 非法 | 重放 accepted/revision/hash 不符并失败关闭 | 绿 |
| CMD-04 | 尾命令 `accepted` 翻转 | 失败关闭 | 绿 |
| CMD-05 | 尾命令 `revision` 篡改 | 失败关闭 | 绿 |
| CMD-06 | 尾命令 `state_hash` 篡改 | 失败关闭 | 绿 |
| CMD-07 | 删除 sequence 65 尾命令，但保留其 request 提交记录 | 失败关闭，不以较旧 sequence 继续 | **红：静默接受 sequence 64 的旧状态** |
| DUP-01 | 直接插入重复 `(match_id, sequence)` | SQLite schema 拒绝 | 绿 |
| DUP-02 | 相同 requestId 以更高 sequence 再写 | 事务拒绝；恢复仍只有首次请求 | 绿 |
| REC-01 | 同一健康库连续恢复两次 | 状态、随机状态、sequence、请求集合完全一致 | 绿 |
| REC-02 | 恢复后追加更高 sequence，较旧损坏证据保持原字节 | 新命令可恢复且证据未被覆盖/删除 | 绿 |
| ISO-01 | 同库一场命令损坏、另一场健康 | 坏场失败，健康场不受影响 | 绿 |
| DB-01 | 整个 SQLite 文件尾部截断 | 数据库整体失败关闭 | 绿 |

共 21 项。CP-01/02/05/06 共享同一缺口但分别约束压缩、哈希和 Revision，不以一个异常代表全部。

## 5. B0 实测结果

- 专项矩阵：`16/21` 通过，`5/21` 按目标语义为红；总耗时约 0.7 秒。
- 含既有 `LatencyAndPersistenceRegressionTests`、`RankedPersistenceRecoveryTests` 的完整专项门禁：`69/74` 通过，仍只有同样 5 项红；脚本总耗时 9.9 秒，低于 120 秒预算。
- CP-01、CP-02、CP-05、CP-06 均证明：当前恢复器只取最新 checkpoint。该行物理或逻辑损坏时，即使 sequence 32 到 65 的命令完整、连续且可验证，也不会尝试较早有效 checkpoint。
- CMD-07 证明：删除 sequence 65 的 `match_events` 后，sequence 65 对应的 `match_action_requests` 提交记录仍存在，但恢复器没有对齐跨表已提交边界，因而把 sequence 64 静默当成完整权威状态返回。
- 其余 16 项均绿：畸形命令、accepted/revision/hash 篡改失败关闭；重复 sequence/request 被拒绝；重复恢复确定；继续写入使用更高 sequence 且保留旧损坏证据；坏对局不污染同库健康对局；整体文件截断失败关闭。
- 两次独立执行得到相同 5 项红，未出现非确定性、超时、共享数据库访问或夹具外写入。

## 6. 拟申请的 B1 最小产品租约

Main 批准的唯一产品文件是：`服务端WebSocket/TwelveLegions/MatchRecorder.Journal.cs`。现有三份 B0 文件继续用于测试与报告；未申请 schema、迁移、quarantine 表、第二存储、`RoomManager`、结算或 outbox。

B1 拟实现语义：

1. 恢复时按 sequence 降序枚举已有 checkpoint；每个候选必须通过编码、解压长度、随机状态、反序列化、revision 与 state hash 校验。
2. 对候选 checkpoint 之后的命令逐条要求 sequence 连续，并验证命令可解析及存档的 accepted/revision/state hash；只有完整重放到已提交边界的候选才可被采用。
3. 已提交边界至少要同时核对 `match_events.sequence` 与已提交 `match_action_requests.command_sequence`，避免请求表仍证明 sequence 65 已提交时静默返回 sequence 64。
4. 最新 checkpoint 损坏但较早 checkpoint 加完整尾链可验证时允许安全回退；尾链缺失、乱序或任一结果不一致时整场失败关闭。
5. 只读恢复不得删除、覆盖或“修复”损坏记录；继续追加仍使用恢复后 sequence + 1，并保留现有原始证据。

实现只修改上述产品文件即可闭环，未触发扩租停止条件，也未触碰 `MatchRecorder.RankedPersistence.cs` 或 `L12PersistenceContract.cs`。

## 7. B1 实现与逐项结果

B1 在 `LoadJournalEngineAsync` 的完整恢复路径内完成以下闭环，保留 `LoadLatestCheckpointAsync` 的原始“读取最新一项”语义：

- 同时读取 `match_events.sequence`、`match_action_requests.command_sequence`、`match_action_events.command_sequence` 和 checkpoint sequence 的最大值；辅助表或 checkpoint 证明存在更高已提交命令但 `match_events` 缺行时，抛出稳定的 `InvalidDataException`。
- checkpoint 按 sequence 降序验证编码、解压长度、随机状态、状态结构、revision 和 hash；只有候选自身数据损坏才继续尝试较早项。存储、随机状态或状态格式不兼容不被误当成可回退损坏。
- 从候选之后逐 sequence 重放到耐久上界，并逐条验证 JSON/type、accepted、revision 和 state hash；任何缺口或分叉整场失败关闭。
- 成功恢复的 sequence 必须等于耐久上界；去重请求只加载到该上界，顺序增加稳定的次级键。恢复过程不写库、不删改坏证据。

| ID | B1 结果 | 明确验收证据 |
| --- | --- | --- |
| CP-01 | 绿 | 跳过尾截断 checkpoint，经 sequence 32 完整重放至 65 |
| CP-02 | 绿 | 跳过半截断 checkpoint，经完整尾链恢复 |
| CP-03 | 绿 | 错误解压长度不产生未验证状态 |
| CP-04 | 绿 | 错误解压长度不产生未验证状态 |
| CP-05 | 绿 | 跳过 hash 不符 checkpoint，经完整尾链恢复 |
| CP-06 | 绿 | 跳过 revision 不符 checkpoint，经完整尾链恢复 |
| CP-07 | 绿 | 损坏随机状态不重新播种，仅可安全回退 |
| CP-08 | 绿 | 不支持的随机状态版本明确失败，不向前回退 |
| CMD-01 | 绿 | `InvalidDataException`：恢复命令 JSON 损坏 |
| CMD-02 | 绿 | `InvalidDataException`：恢复命令缺少类型 |
| CMD-03 | 绿 | `InvalidDataException`：恢复尾部校验失败 |
| CMD-04 | 绿 | `InvalidDataException`：恢复尾部校验失败 |
| CMD-05 | 绿 | `InvalidDataException`：恢复尾部校验失败 |
| CMD-06 | 绿 | `InvalidDataException`：恢复尾部校验失败 |
| CMD-07 | 绿 | `InvalidDataException`：缺少耐久已提交边界对应命令 |
| DUP-01 | 绿 | SQLite 唯一约束拒绝重复 sequence |
| DUP-02 | 绿 | 重复 request 事务回滚，恢复仍为 sequence 65 |
| REC-01 | 绿 | 两次恢复的状态、随机状态、sequence、请求集合一致 |
| REC-02 | 绿 | 追加 sequence 66，旧损坏证据原值保留 |
| ISO-01 | 绿 | 坏对局明确失败，同库健康对局仍可恢复 |
| DB-01 | 绿 | 整体截断以 SQLite/I/O 数据库错误失败关闭 |

专项矩阵为 `21/21`；包含既有两类恢复测试的门禁为 `74/74`。最终文件状态连续两次独立运行均为 `74/74`，脚本耗时分别为 10.4 秒和 10.9 秒，低于 120 秒预算；P0—P4 架构锁通过，`git diff --check` 通过。候选失败分类仅吞掉物理/逻辑 checkpoint 数据损坏以继续寻找更早候选；取消、SQLite、I/O 与版本边界错误均不被吞掉。

## 8. B0/B1 验收条件

1. 每项矩阵有确定性 ID、单一注入、期望错误类别、合成副本路径和实际红/绿结果。
2. 所有“绿”项必须证明失败后未返回可继续的错误权威状态；同库隔离项必须同时证明健康对局仍可恢复。
3. 所有“红”项必须保留严格的目标语义，不得把“当前抛错”误写成“已完成安全回退”。
4. 连续恢复比较完整状态 hash、随机状态版本/载荷/抽取计数、命令序号和去重请求集合。
5. 继续写入使用 `recovered.CommandSequence + 1`，并对损坏证据做写前/写后字节比较。
6. 专项脚本确定性、非管理员、默认 120 秒内结束；不接触共享运行库。
7. B0 完成后只回传矩阵结果和 B1 最小租约建议；未获 Main 新批准前不改产品代码、不提交、不推送、不部署。

## 9. 精确 B0/B1 写入租约

仅写：

1. `artifacts/reports/LC-03B-JOURNAL-FAILURE-TARGETS-20260926.md`
2. `TwelveLegions.Tests/JournalFailureClosureAdversarialTests.cs`
3. `scripts/test-l12-lc03b-journal-failure.ps1`
4. `服务端WebSocket/TwelveLegions/MatchRecorder.Journal.cs`

不修改既有测试、产品代码、共享台账、构建脚本或部署文件。

## 10. 停止条件与非范围

若 B0 证明需要 schema 迁移、quarantine 表、第二套存储、`RoomManager`/结算/outbox 变更，或无法识别已提交边界、会丢弃坏证据、健康对局被行级损坏牵连、出现非确定性/超时，立即暂停并回报 Main。

非范围：LC-03C Soak、真实裁判 viewer role、卡效、UI、部署、线上数据修复、真实数据库取样。
