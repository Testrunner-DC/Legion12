# 新会话交接：存储发布模型改造（B-1 → D5 前向 schema → B-2/B-3）

> 复制以下全文作为新对话的首条消息。它自包含：环境、现状、三项任务、规则、坑。

## 0. 你是谁、边界在哪

你是本项目的主执行 agent。用户已批准的事项可直接推进；**未批准的不动**。当前已批准的范围恰好是下面三项，按序做。生产服部署**未获授权**，只有测试服（testrun）已获授权。

## 1. 环境与真相源（先确认，别信记忆）

| 项 | 值 |
|---|---|
| 权威源码树 | `D:\GPT\Legion12\worktrees\lubu-pingyang-20261001`（`D:\GPT\Legion12\app` 不存在） |
| `origin/main` | `c80fb5cc` |
| 测试服版本 | `df28aada`（`https://legion-12.com/testrun/`，健康，`/api/metrics` 200） |
| 正式服 | 未触碰 |
| 仓外台账（唯一进度真相） | `D:\GPT\Legion12\docs\十二军团优化任务台账-20261121.md` |
| 深度评估报告 | `D:\GPT\Legion12\docs\十二军团深度评估报告-20261121.md` |

**开工第一条命令**（确认树没走错，这是本项目踩过的第一个坑）：
```powershell
cd D:\GPT\Legion12\worktrees\lubu-pingyang-20261001
git log -1 --format='%h %ad %s' --date=short
git rev-list --left-right --count HEAD...origin/main
```
然后读三份文档：`docs/SOURCE-OF-TRUTH.md`（哪棵树权威）、`docs/HARNESS-HANDOVER.md`（Codex/DSH 规则消费差异）、`docs/l12/STORAGE-PUBLISH-MODEL-EVALUATION.md`（本次改造的完整证据链）。

## 2. 为什么做这件事（一句话证据）

测试服实测：**新的注册 1277ms，重复注册 190ms**，差距 1.09s —— 账号枚举的时序信道。根因不是密码学，是**每次保存都重写整库**：

| 实测 | 值 |
|---|---|
| `platform.db` | 44.4 MB |
| `platform_state.snapshot_json`（单行） | **12.96 MB** |
| 每次保存重写量 | ≈ 2 × 12.96 MB（快照 + 镜像各序列化一次） |
| `storage_revision` | 6,903（累计保存次数） |
| `admin_audit_events` | 4,427 行 / 3.43 MB payload |

## 3. 任务一：B-1 审计增量插入（0.5–1 人日）

### 目标
一次保存不再对全部审计行做插入尝试，只处理新增审计。

### 定位（必须自己再读一遍，别照抄行号）
`服务端WebSocket/TwelveLegions/L12PlatformStore.TransactionalStorage.cs`
- `:275` `PersistTransactionalData` 入口；`:278` 主实现
- `:313` `MergeIndependentAudit(..., prepareAppend: true)` → `validatedAudit`
- `:342` 主保存路径调用 `AppendIndependentAudit`
- `:546`、`:622` 另外两处调用（恢复/迁移路径，不要漏）
- `:670-700` `AppendIndependentAudit` 定义
- `:749+` `MergeIndependentAudit` 定义；`ReadValidatedAudit` 在 `:702`/`:743`

### ⚠ 第一件事：先证伪现状，不要假设
代码里**已经有一层跳过**：`AppendIndependentAudit` 内 `if (validatedAudit?.ContainsUnchanged(audit) ?? present!.Contains(audit.Id)) continue;`。

所以在动任何代码前，先回答：**真正的成本在哪**？三选一（用一次实测回答，不要猜）：
1. 插入确实已被跳过 → 成本在 `ValidateExistingAuditRows` / `MergeIndependentAudit` 的**读取与比对**（可能每条保存都 SELECT 全部 4,427 行）
2. 未被跳过（例如 `validatedAudit` 为 null 的路径）→ 才需要水位
3. 两者都有

**测量手段**：给三处路径各加一个 `internal` 计数器（插入条数 / 比对条数 / SELECT 行数），跑一次连续保存的用例打印出来。计数器在实施后可保留为守卫断言点。

### 若确认需要水位（情况 2）
- 加 `_auditWatermarkCreatedUtc` + `_auditWatermarkId`，在 `ReadSnapshot` 之后用
  `SELECT created_utc, id FROM admin_audit_events ORDER BY created_utc DESC, id DESC LIMIT 1` 初始化
- `AppendIndependentAudit` 只遍历**严格大于水位**的条目
- **水位推进必须放在 `transaction.Commit()` 成功之后**，否则崩溃丢审计
- 冲突重试（`L12PlatformStorageConflictException`）与恢复分支（`recovered`）必须重建水位

### 验收
1. 连续两次保存，第二次的审计插入数 = 新增条数（不是总数）
2. `StorageFailureInjector` 注入失败后水位不前进，重试仍能写入该条审计
3. 平台项目全量通过；`admin_audit_events` 行数与内存集合语义一致（不丢、不重）
4. 重启后水位从数据库重建，首次保存不重放历史

### 边界
只改 `L12PlatformStore.TransactionalStorage.cs`（必要时加一个测试文件）。**不碰 schema、不碰快照写入、不碰聊天/广播同步。**

## 4. 任务二：D5 前向 schema（3–4 人时）

### 已完成的评估结论（别重做）
`scripts/audit-l12-registry-structure.ps1`（只读）实测 693 条登记条目：词表归一后字段覆盖率只有 **6%–17%**，未识别标签 **981 种**，无法归类的正文行 **2742 行**。→ **历史条目 JSONL 源化不可行**，已否决。

### 要做的
- 定义 schema（`id/type/date/title/status/symptom/rootCause/sameTypeScan/change/regression/verification/rollbackGuard/bodyExtra`）
- **只约束新条目**：升级 `scripts/verify-l12-doc-ledger.ps1`，对"近 N 天内新增条目"强制 schema；历史条目原样保留为 `bodyExtra`
- 不做解析器、不改写历史、不引入 JSONL

### 验收
- 新条目缺必填字段 → 门禁红；故意造一条缺字段的条目验证它真的红
- 历史条目零改动（`git diff` 里不含历史条目内容）

## 5. 任务三：B-2 / B-3 拆表（4–7 人日）

- **B-2**（1–2 人日）：会话/令牌从快照拆成独立表（最热、最独立、不涉玩家可见数据）
- **B-3**（3–5 人日）：账号与内容拆表 + schema v3→v4 迁移
- 参考：库内 `account_decks`(12,720 行)、`deck_payloads`、`chat_*`、`published_deck_*` **已经拆表成功**，照它们的模式做
- 验收：1 万账号 + 5 万对局数据集下 `Save` p99 < 20 ms；迁移可回滚；崩溃恢复演练通过

## 6. 工程规则（违反会被门禁或用户拦下）

### 门禁
```powershell
pwsh -File scripts/verify-l12-change.ps1 -Level Focused   # 开发中
pwsh -File scripts/verify-l12-change.ps1 -Level Batch     # 一个功能完成
pwsh -File scripts/verify-l12-change.ps1 -Level Release   # 部署前（要求干净树）
```
最近一次全量：**规则 8,811/8,811、平台 3,168/3,168、原子审计通过、台账 lint 通过**。

### 自定义测试必须走治理入口
```powershell
. .\scripts\lib\l12-test-storage.ps1
& .\ops\windows\Initialize-L12BuildEnvironment.ps1 | Out-Null
$sb = $env:L12_WORK_CACHE
Invoke-L12TestRun -Executable 'dotnet' -Arguments @('test','.\TwelveLegions.Platform.Tests\TwelveLegions.Platform.Tests.csproj','--configuration','Debug','--filter','FullyQualifiedName~XXX','--','xUnit.ParallelizeTestCollections=false') -TemporaryBase (Join-Path $sb 'test-temp') -EvidenceBase (Join-Path $sb 'test-evidence') -Label 'xxx'
```

### 测试服部署（已获授权）
```powershell
pwsh -File .\ops\windows\deploy-l12-testrun.ps1 ^
  -ArtifactManifest <产物.json> ^
  -KnownHostsFile D:\GPT\Legion12\artifacts\security-reinstall-prep-20261009\ssh-reinstall-pinned-20261011.known_hosts ^
  -IdentityFile C:\Users\neptu\.ssh\id_ed25519
# 回滚到旧产物加 -Rollback（已在 2f7dacf7 实现）
```
**部署前必须**：干净树 + `HEAD == 产物 commit`（或 `-Rollback`）+ `-ValidateArtifactOnly` 先离线校验。

### 审批规则（用户明确要求）
- 每项任务开工前交"改动预告"（文件清单 + 验证链 + 回滚方式），用户点头后才动手
- 一项一闭环：实施 → 验证 → 证据回填台账 → 再开下一项
- **生产部署、生产数据操作、删除服务器文件都要单独授权**
- 台账（仓外那份）是唯一进度真相，每次都要回填

## 7. 已踩过的坑（省你时间）

| 坑 | 事实 |
|---|---|
| 分析错树 | `repo\` 是旧树（落后两个月）；权威树是 `worktrees\lubu-pingyang-20261001`。用 `git merge-base --is-ancestor` 确认 |
| nginx 只代理 `^~ /api/` 与 `= /health` | 新端点必须挂 `/api/` 下，否则被 SPA 兜底吞掉（`/metrics` 就踩过） |
| 部署强制 `HEAD == 产物 commit` | 回滚必须用 `-Rollback`；否则旧产物永远部署不了 |
| 测试服磁盘双预算 | program 1.5 GiB / data 512 MiB；当前 1452M/1536M、456M/512M，**都很紧**。保留策略已在 `c0a61606` 改为自动轮转（release 保最新 3、runtime 备份保最新 2 组） |
| Codex CLI 守卫 | 本机没有 `codex` CLI；`verify-l12-codex-routing.ps1` 已改为条件化（只有改 `.codex/` 才强制），CLI 缺失会打"跳过不等于通过"的警告 |
| SQLite 测试临时目录 | 清理前要 `SqliteConnection.ClearAllPools()`，并把临时根目录保持短路径 |
| 门禁会真的拦人 | 本轮被拦两次：A4 改了注册文案撞既有断言、C4b 迁移日志破坏 Console 断言。**先改实现去适配契约，别改测试就范** |

## 8. 已完成、不必重做

- A1–A6（门禁修补、未知卡效显式失败、审计棘轮、连接上限、`synchronous=NORMAL`、浏览计数）全部闭环，其中 3 项是"上游已实现"
- B2 的 20–40ms 抖动**已撤**（`0964f7cf`）——实测无效，别再把它加回来
- C4 `/api/metrics` 已上线并实测（200 + 具名序列 + 六类敏感片段全不出现）
- D1 单一真相源、D2 tag（`v2026.11.21-1`）、D2b 回滚往返演练（df28aada → 4c7ea53b → df28aada）已完成
- D5 一期 lint（重复 id / 日期倒序，基线 2 / 38 / 15，只降不升）已上线
- B5 GameBoard 拆解：**用户已决定不做**
