# LC-03C｜长局累计与恢复性能基线

状态：LC-03C-1 已由 L12-main 验收、集成并推送到 `d441845afcad5e9a8b751f6f7fe773753bf3db46`；LC-03C-2 已在该基线上获得四文件精确租约并完成七风险族首次全绿。未授权产品修复、推送或部署。

## 1. Main 对齐与边界

- LC-03C-1 建立短／中／长三档命令链、多检查点、重复恢复与继续写入的规模曲线；LC-03C-2 再扩代表性七类长链正确性；仅在出现可复现红项并获得新租约后进入 LC-03C-3 产品修复。
- 权威计划不再保留独立 LC-03D 标签，真实裁判 viewer role 归入 LC-05。LC-03C-2 只验证既有 referee 占位投影在恢复前后同授权域一致，不定义或扩大真实裁判权限。普通 spectator 仍不得看到手牌、牌库顺序、盖伏身份及私密 Prompt／选择。
- 不修改产品、规则、投影、权限、前端、共享台账、部署或运营配置；不读取真实数据库。

## 2. C-1 分目标

1. 使用 64、256、1024 条命令三档精确四倍规模；每档使用三个固定种子并取中位数，另有不计入结果的预热样本。
2. 每 32 条命令保留真实 Journal V2 checkpoint；只破坏 sequence 大于 0 的 checkpoint hash，迫使恢复器验证多个候选并从初始 checkpoint 完整重放到耐久上界。
3. 每个样本执行首次恢复、继续写入一条更高 sequence 命令、再次恢复；比较完整状态、hash、随机状态、revision、事件序号和 CardFact 序号，并确认继续写入 request 进入去重基线。
4. 记录 Journal 载荷字节、checkpoint 载荷字节、SQLite 文件字节、checkpoint 数、最慢一次恢复耗时、样本总耗时、进程分配量、工作集高水位、状态字符数和 processed request 数。

## 3. 预算

- 单样本不超过 60 秒；Focused 脚本总计不超过 180 秒；恢复宽松绝对上限 30 秒。
- 命令规模扩大四倍时，Journal、checkpoint、SQLite 文件和恢复耗时中位数均不得超过前一档 6 倍；恢复比率分母至少按 1ms 计算以抑制计时器噪声。
- 正确性要求零差异，不允许跳过；性能红项必须保留规模、固定种子与指标，不得只放宽阈值。

## 4. C-1 验收矩阵

| ID | 检查 | 期望 |
| --- | --- | --- |
| SCALE-01 | 64 条、3 个固定种子 | 多 checkpoint、全尾链恢复、继续写入与二次恢复正确 |
| SCALE-02 | 256 条、3 个固定种子 | 相对 SCALE-01 体积与恢复增长不超过 6 倍 |
| SCALE-03 | 1024 条、3 个固定种子 | 相对 SCALE-02 增长不超过 6 倍；每样本小于 60 秒 |
| STATE-01 | 两次恢复完整状态合同 | state/hash/random/revision/event/CardFact 全相等 |
| DEDUP-01 | 恢复后追加 request | 二次恢复包含新 request，sequence 精确加一 |
| FAIL-01 | 中间 checkpoint hash 损坏 | 只读回退且保留损坏证据；完整尾链才成功 |
| COMPAT-01 | 既有 LC-01/LC-03B 回归 | 长链正确性与 Journal 损坏 0 回退 |
| BUDGET-01 | Focused 总耗时 | 低于 180 秒且输出三档逐样本与中位数 |

## 5. 停止条件与非范围

发现超过 6 倍增长、单样本／总脚本超时、状态合同任一不一致、已处理 request 可重复执行、测试依赖真实数据，或证明必须修改产品时，立即停止并向 Main 回传复现与测量，不自行修复。

非范围：LC-03C-2 七风险族组合实施、LC-03C-3 产品修复、LC-03D 真实裁判权限、324 卡笛卡尔积、UI、部署、正式服配置、线上数据。

## 6. C-1 实测结果

首轮完整 Focused 在 40.6 秒内通过：新增性能预算 `1/1`，既有 LC-01 与 LC-03B 回归 `38/38`，无失败、无跳过。测试使用合成 SQLite、固定卡表与固定种子，不接触真实数据。

| 命令数 | Journal B | Checkpoint B／行数 | SQLite+WAL+SHM B | 恢复中位数 ms | 样本总耗时中位数 ms | 分配量中位数 B | 工作集高水位中位数 B | 状态字符 | Requests 首次／续写后 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 64 | 31,432 | 29,011／3 | 3,575,976 | 133.729 | 513.760 | 151,194,952 | 197,828,608 | 246,221 | 64／65 |
| 256 | 124,520 | 90,816／9 | 4,750,936 | 182.962 | 798.579 | 570,233,560 | 225,341,440 | 254,885 | 256／256 |
| 1024 | 497,028 | 335,883／33 | 5,885,696 | 622.961 | 2,751.459 | 2,253,190,440 | 245,760,000 | 254,918 | 256／256 |

四倍规模增长比：

- 64→256：Journal `3.962×`、checkpoint `3.130×`、SQLite 总占用 `1.329×`、恢复 `1.368×`、样本总耗时 `1.554×`、分配量 `3.772×`、工作集高水位 `1.139×`。
- 256→1024：Journal `3.992×`、checkpoint `3.699×`、SQLite 总占用 `1.239×`、恢复 `3.405×`、样本总耗时 `3.445×`、分配量 `3.951×`、工作集高水位 `1.091×`。
- 所有受约束指标均低于 `6×`；1024 档三次恢复最慢样本低于 0.64 秒、单样本总耗时低于 2.8 秒，距 30 秒／60 秒预算均有充分余量。
- 状态大小在事件窗口达到上限后稳定于约 255k 字符；processed requests 按既有 256 上限保持有界。每次二次恢复均找到续写 request，且全部已损坏 checkpoint 行在恢复后仍原样存在。

结论：C-1 未发现性能或正确性红项，不触发 C-3 产品修复申请。下一步应在重新取得 Main 回执后进入 C-2 七风险族代表性长链正确性。

最终文件状态又连续运行两次完整 Focused，均为性能预算 `1/1` 加兼容回归 `38/38`，总耗时分别为 41.4 秒与 39.7 秒；结果稳定且低于 180 秒预算。运行时仅出现因隔离环境无法访问 NuGet 漏洞索引的 `NU1900` 警告，项目依赖已完整解析，编译和测试均成功。

基线更新后复验：三文件候选重放到 `aefc91d` 后再次连续运行两次，仍为性能预算 `1/1` 加兼容回归 `38/38`，总耗时 53.7 秒与 40.0 秒；无新增差异、无跳过，继续低于 180 秒预算。

## 7. C-1 精确写入租约（历史）

本子批仅新增：

1. `TwelveLegions.Tests/LongChainJournalPerformanceBudgetTests.cs`
2. `scripts/test-l12-lc03c-long-chain.ps1`
3. `artifacts/reports/LC-03C-LONG-CHAIN-TARGETS-20260927.md`

C-1 当时获准但无需修改的 `LongChainJournalRecoveryTests.cs` 与 `LongChainAdversarialHarness.cs` 保持不变；二者在 C-2 获得了新的独立租约，见下节。

## 8. C-2 七风险族代表性长链正确性

### 8.1 精确租约与基线

- 基线：`origin/main@d441845afcad5e9a8b751f6f7fe773753bf3db46`；独立分支 `codex/lc03c2-long-chain`。
- 修改范围仅为 `LongChainJournalRecoveryTests.cs`、`LongChainAdversarialHarness.cs`、本脚本和本报告。
- 并行弹框文案 A 批只租用 `Models.cs`、`L12PromptsAndSetup.cs`、`types.ts`、`PromptOverlay.vue`、`GameBoard.vue` 及其专项测试，与本批不重叠。

### 8.2 七风险族与代表链

| 风险族 | 代表场景 | 真实证据与恢复切点 |
| --- | --- | --- |
| 叠放、转移与最后已知信息 | `lc01-arthur`／亚瑟王 | 王者之剑真实叠放；`attachment-committed` 前后恢复 |
| 限时与持续效果源失效 | `lc01-oiran`／吉原的花魁 | 支付后、来源离场后仍保留限时修正；`paid-cost-before-resolution`、`source-left-field` |
| 响应、无效化与同时触发 | `lc01-hanxin`／韩信 | 真实响应栈、盖伏反击与 negated 终态；`response-stack` |
| 手牌、牌库、检索与顺序 | `lc01-gustav`／古斯塔夫一世 | 两张墓地牌按声明顺序回牌库；`private-library-order-committed` |
| 天灾与试炼 | 上述四条代表链 | 试炼实际完成、天灾实际翻开；`after-disaster-and-trial` |
| 跨回合清理、次数与阵亡替代 | `lc01-oiran`／吉原的花魁 | 来源离场后保留、回合结束后准确清理；`cross-turn-cleanup` |
| 重复、延迟、乱序与过期请求 | `lc03c-request-ordering` | 真实 `L12RoomManager`、Journal V2、连接替换和同 requestId 重试至多一次 |

四条卡效代表链各自至少写入 65 条 Journal 命令，并形成至少两个非初始真实 Journal 检查点。深度段每 8 条命令重建 B 线，最终再从 Journal V2 重建 C 线；不是 15 张历史卡与七风险族的笛卡尔积。

### 8.3 比较与隐私合同

- A/B/C 每步显式比较 Pending Prompt、Pending Activation、EffectStack、ResponseWindow、使用次数、权威事件、CardFact、完整状态、StateHash、随机状态／抽取计数、Revision 及玩家甲／乙、普通 spectator、既有 referee 占位投影。
- `UnpersistedEvents` 是发送队列，检查点恢复后按设计清空，不属于跨恢复历史等价字段；权威 `State.Events`、`EventSequence` 和 CardFact 必须一致。拒绝原子性仍要求同一执行线的 `UnpersistedEvents` 不变。
- 双方手牌和牌库顺序使用专用合成哨兵。盖伏牌允许既有 P2 合同公开稳定 `instanceId` 定位，但对手、普通观战和 referee 占位投影必须把 `cardId/name/cardType/faction/effectText` 裁剪为 `hidden-card/覆盖的卡牌/covered/hidden/null`。
- 恢复后的 Journal 命令序号、最新检查点序号和 processed request 集合必须与不中断线一致。

### 8.4 固定预算与停止条件

- C-2 正确性合计不超过 90 秒、单代表场景不超过 20 秒；统一脚本不超过 180 秒。
- 完整状态小于 2MB、每个接收者投影小于 1MB、snapshot P95 小于 1 秒、restore P95 小于 2 秒。
- C-1 的单样本 60 秒、恢复 30 秒和四倍规模增长 6 倍预算保持不变。
- 任一权威字段、随机流、事件/CardFact、Journal、请求幂等或投影分叉，任一私密哨兵越权，固定种子不稳定，或必须修改租约外产品文件，立即停止并回传最小复现。

### 8.5 当前验证

- 红测先因七族矩阵、代表映射与请求时序合同尚未实现而预期失败；没有产品行为红灯。
- 七族首次全绿：`LongChainJournalRecoveryTests` 17/17，约 27 秒。
- 统一门禁连续两轮稳定：每轮均为性能 1/1、Recovery 17/17、FailureClosure 22/22、P1/P2-P3 架构测试 7/7；墙钟分别为 50.5 秒和 51.0 秒。P0—P4 架构锁和 `git diff --check` 通过。
- 构建输出固定到 `D:\GPT\Legion12\cache\primary\lc03c2`；C 盘空间不足只影响最初的依赖复制，未影响测试结论，也未删除用户数据。
