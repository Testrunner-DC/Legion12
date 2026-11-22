# 平台存储「发布先于落盘」立项评估（2026-11-22）

## 1. 触发这件事的实测

| 场景 | 中位耗时（测服实测，各 5 次） |
|---|---|
| 新用户名注册 | **1277.2 ms**（1004–1363） |
| 重复用户名注册 | **190.6 ms**（179–219） |
| 差距 | **≈1.09 s** |

这条差距就是账号枚举的时序信道：攻击者不需要读文案，只比耗时就能判断用户名是否存在。
此前一次修补（重复分支加 20–40 ms 随机抖动）已证明无效：抖动量与被测差距差两个数量级。

## 2. 为什么差距不在密码学上

注册成功路径依次做：校验 → 口令派生（PBKDF2 120k）→ 建号 → 播种预组 → 发令牌 → **整库快照 Save()**。
被跳过的重复分支只做前两步。一次 PBKDF2 只有几十毫秒，而 Save() 要序列化整个平台快照并落盘。
**主导项是持久化，不是哈希。**

## 3. 为什么「延迟写盘」这条路走不通（已实验否决）

把 Save() 改成后台写盘后注册立即返回，但既有 31 个平台测试变红，根因写在读取路径的注释里：

> The owner of a synchronous transaction must still see its staged input.
> All other readers consume the one atomically published durable bundle.

读取端消费的是**已发布的持久快照**，不是内存工作集。延迟落盘会让刚注册的账号与令牌立刻不可用
（AuthenticateTokenSession 返回 null），客户端拿到一个不存在的 token。
**结论：先改读写模型，才谈得上延迟落盘。** 这就是本立项的边界。

## 4. 三个候选方案

### A · 发布先于落盘（内存发布 + 异步持久化）
- 做法：读者读内存工作集的不可变快照；落盘异步、合并、退出前 flush。
- 改动面：L12PlatformStore 读取路径（AuthenticateTokenSession、Accounts 及各查询）、Save/PersistTransactionalData、启动恢复。
- 风险：**高**。崩溃会丢最后一次未落盘变更；所有依赖「读即持久」的测试要重新审视。
- 成本：**3–5 人日**。
- 验收：注册耗时不随平台库体积增长；注册后立即用 token 认证成功；强杀后重启最多丢最后 N 秒且可从快照恢复；平台测试全绿。

### B · 增量持久化（拆表）
- 做法：整库单行快照改为分表行级写入（账号/会话/审计/内容各自成表），Save 变成若干小写入。
- 改动面：存储 schema（v3→v4）+ 迁移 + 全部写路径 + 恢复校验。
- 风险：**中高**，但**收益最大**——它同时解决「越运营越慢」（2026-11-20 方案记录的第一根因）。
- 成本：**5–10 人日**（含迁移与回滚方案）。
- 验收：1 万账号 + 5 万对局数据集下 Save p99 < 20 ms；迁移可回滚；崩溃恢复演练通过。

### C · 注册专用发布通道（外科手术）
- 做法：注册只把「新账号 + 新会话」作为增量写入并立即发布，其余快照异步。
- 风险：**中**。等于给存储引入两套发布语义，后续每个读写路径都要问「你读的是哪份真相」。
- 成本：**2–3 人日**。能最快消掉这条信道，但不建议长期保留。

## 5. 建议

1. 先做 **B 的前置测量（0.5 人日）**：生产窗口统计 Save 的字节构成与耗时分布，确认拆表能拿回多少。
2. 据此二选一：**B（治本，同时治「越运营越慢」）** 或 **A（改动更集中，直接关闭时序信道）**。
3. 不建议长期采用 C。

## 6. 明确不做

- 不再用随机抖动掩盖时序差：已实测无效，且会随负载漂移。
- 不在未改读写模型前再次尝试延迟落盘：实验已否决，会破坏 token 立即可用性。
- 不把这条信道当「低危可不修」：注册已受 30 次/窗口限流约束，但**已登录用户的改名路径同样存在**。

## 7. 前置测量结果（2026-11-22，测试服 df28aada，只读）

| 指标 | 实测 |
|---|---|
| platform.db 体积 | **44.4 MB**（10,836 页 × 4 KB） |
| platform_state.snapshot_json 单行 | **12,958,662 字节 ≈ 12.96 MB**（账号/会话/内容全在这一行） |
| storage_revision | **6,903**（建库以来累计保存次数） |
| admin_audit_events | **4,427 行 / 3.43 MB** payload |
| account_decks | **12,720 行**（已拆表） |
| matches.db | 4 KB（测试服无对局数据，无参考价值） |
| /api/metrics 平台序列 | 当前窗口 count=0（测服无流量，耗时分布需生产窗口） |

### 由此得出的确定结论

1. 每次保存重写的字节量 ≈ **2 × 12.96 MB ≈ 26 MB**（快照与镜像各序列化一次），与改动量无关——
   改一个浏览计数也写 26 MB。
2. 审计不是主项但不小：每次保存还要对最多 4,427 条审计逐条 INSERT OR IGNORE（3.43 MB payload）。
3. 快照占库体积 29%（12.96 / 44.4）；其余是审计与已独立成表的牌库数据——说明「拆表」在本库已被验证可行
   （account_decks、deck_payloads、chat_*、published_deck_* 都已拆出）。
4. 拆表后一次账号类变更预计只写受影响行（KB 级）并增量追加审计，而不是 26 MB 全量重写——
   这正是 2026-11-20 方案记录的第一根因。

### 仍缺的一块（需要生产窗口）

- 生产 platform.db 的真实体积与 snapshot_json 大小
- 生产每分钟保存次数与 transactional-save 耗时分布

补充方式：生产窗口抓一次 /api/metrics 即可（该端点已上线，暴露 bytes.platform.phase.* 与
duration.platform.operation.transactional-save），无需改代码。

## 8. B-1 施工预告（审计增量插入，尚未实施）

### 为什么先做它

审计重放的字节与行数（4,427 行 / 3.43 MB）不随改动量变化，是「一次保存 = 全量重写」的第二大成本；
且它不需要改 schema、不需要迁移，是三片里风险最低、见效最快的一片。

### 现状（2026-11-22 读码定位）

- 调用点：L12PlatformStore.TransactionalStorage.cs:342 ——
  AppendIndependentAudit(connection, transaction, _data.AdminAudit, validatedAudit)
- 实现：同文件 AppendIndependentAudit 逐条 INSERT OR IGNORE
- 表：admin_audit_events(id, created_utc, category, outcome, actor_id, command_id, correlation_id, payload_json, payload_sha256)

### 要改什么

1. 增加水位 _auditWatermarkCreatedUtc 与 _auditWatermarkId；在 ReadSnapshot 之后用
   SELECT created_utc, id FROM admin_audit_events ORDER BY created_utc DESC, id DESC LIMIT 1 初始化。
2. AppendIndependentAudit 只遍历严格大于水位的条目；插入成功后把水位推进到本批最大值。
3. 事务失败不得推进内存水位（水位更新必须在 transaction.Commit() 成功之后）。
4. 保留 INSERT OR IGNORE 作为幂等兜底，但不再重放历史行。

### 必须一起处理的边界

- FilterMigratedAuditSnapshot 与 validatedAudit 参数的语义不能被绕过
- 审计保留策略会删除旧行：删除不影响水位（水位只用于判断哪些是新的）
- 冲突重试路径（L12PlatformStorageConflictException）后水位必须与已提交状态一致
- 恢复路径（recovered 分支）启动时必须从数据库重建水位，不能沿用内存值

### 验收标准

1. 新增守卫：连续两次保存，第二次的审计插入次数应等于新增条数而非总数；用内部计数器断言
2. 崩溃注入（现有 StorageFailureInjector）：失败后水位不前进，重试仍能写入该条审计
3. 平台项目全量通过；admin_audit_events 行数与内存集合语义一致（不丢行、不重复）
4. 重启后水位从数据库重建，首次保存不重放历史

### 为什么本轮没有实施

该路径同时承担 CAS 快照写入、对象写、聊天同步、广播同步与冲突重试；改动必须完整读懂约 200 行事务体。
在上下文不足时硬改耐久性关键路径，风险高于收益。定位、边界与验收已写在这里，下一轮可直接开工。

