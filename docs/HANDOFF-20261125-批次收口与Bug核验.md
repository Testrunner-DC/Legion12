# 交接 · 2026-11-25（批次收口 / Bug 核验 / 图坦卡蒙修复）

> 权威工作树：`D:\GPT\Legion12\worktrees\lubu-pingyang-20261001`
> 台账（唯一进度真相）：`D:\GPT\Legion12\docs\十二军团优化任务台账-20261121.md`
> 本批验证流程：`docs/l12/CARD-ANALYTICS-P1-VERIFICATION.md`（五道关，建议沿用）

---

## 一、线上现状

| 环境 | 版本 | 说明 |
|---|---|---|
| 正式服 | `798f6777` | 未包含本批任何改动；Bug 队列已按此版本核验 |
| 测试服 | `fc5dc1e7` | 含到"重连修复"为止的全部改动 |
| `main` | 见 `git log` | 领先正式服约 26 个提交；**工作树有一处未提交改动**（见第四节） |

---

## 二、本批完成（按批次）

**批次 T（赛事可演练化）**：A2 实跑跑通（`a9418586`，测试服两次 71/71）；A2b 权威结算实现（`027bc676`）并**端到端跑通**（18/18，事件序列 `authority-conclusion-requested` → `game-result|actor=game-engine`，`rulings=0`）；B1–B4（相位不再降级 Status、开赛双守卫、回归、cancel/完赛一致性）`a6aef1ea`+`8eda7101`。

**批次 S**：S1/S2/S3 早前已实现，本批只核验 + 补 S3 三条验收（缓存窗口复用、10 并发单飞、超时不抛）→ `bb19503d`；`/var/log` 结论：服务账号可读（418 MB）但本服务写 journald，故默认分类不含它。

**批次 U**：P0 六项前端（`cad8772d`）；P1 的 ⑧a `FactsLoaded`（`43aa0abc`）、⑧b 详情 200+reason 与前端消费（`210729ce`）、⑨① total 合并（`c9c2a04f`）、⑨② 六切片合并 + 逐桶字面等价（`de4bae4a`）、⑨③ 索引对齐**量测后判定不做**（`2f0446c8`，2 万场实测加索引反而列表 +18.7%）。⑩（缓存单飞+确定性键）经核验**早已实现**。

**重连修复**（`fc5dc1e7`）：赛事房间结束后重连不再让 `hello` 失败，改回大厅；线上用同一对彩排账号连跑两次 A2b 验证（两次 18/18）。

**前端契约接线**（`300e26c2`）：9 个"僵尸"赛事契约脚本并入 `check:ui-contracts`（赛事段 1→10），4 个浏览器/截图类保持手动。

**Bug 队列收口**（生产只读拉取 989 条，未结 14 条）：
- `BUG-20261118-a0a70c1a`（公共牌库复制）→ resolved/fixed_verified（`5694edec`+`c93766ad` 已在 798f6777）
- `BUG-20261112-1749ac85`（手机版须佐剑按钮）→ resolved/fixed_verified（`9db0d79c` 已在 798f6777）
- `BUG-20261117-20fd1129`（回合时长）、`BUG-20261117-0fa59e88`（澳门登录）→ closed/rejected

**A3 人工勾选表**（Lead 代跑，Playwright + 系统 Chrome）：判罚通过、横屏详情与对局页 844/844 无溢出、选手间手牌隔离成立；用户裁定两条产品约定——**裁判观战可见双方手牌**、**裁判可见选手聊天**；**房内聊天是缺口**（房内无聊天面板，只有站点「打开好友功能」，用户裁定应做在站点聊天内）。

---

## 三、必须知道的运维/契约事实（踩过的坑）

1. **部署数据预算**：测试服 data 分区上限 512 MiB；运行时目录里三份 `platform.db.pre-*` 快照各 43 MB，部署会因预算失败。处理办法：下到本地留档（sha256 一致）后移到 `/var/backups/legion12-testrun/`。
2. **门禁假绿**：手工给 `verify-l12-change.ps1 -ChangedPaths` 传**非 ASCII 路径**时分类只匹配 ASCII 项，日志出现 `Changed files: 1` 且不跑平台测试，exit 0 是假绿。判断：日志里必须有 `Platform persistence focused tests`。正确做法：**不传 `-ChangedPaths`**。
3. **裸跑前端门禁会假红**：`npm run check:ui-contracts` 需要 `Initialize-L12BuildEnvironment` 提供存储根，否则 `test-tutorial-contract` 报错；走 `verify-l12-change.ps1` 即可。
4. **Bug 关单契约**：`PATCH /api/admin/bugs/{id}` 的 `expectedVersion` 用**平台业务版本**（`GET /api/admin/bugs` 的 `platformVersion`），详情里的 `version` 是报告时提交号；`fixed_verified` 必须同时给 `fixCommit` + **命名 `regressionTest`** + `deployedVersion`；`rejected` 的理由读 **`comment`**；客户端禁传 `verifiedBy/verifiedAt`（服务端会 400）。
5. **A3 走法**：桌次入口在**赛程页签**；玩家按钮「进入对局」，已在房内时消失（走自动恢复）；手牌要等**先后手选定、进入调度阶段**才渲染；裁判观战入口在**赛程行**的「观战」，不在「裁判台」页内。
6. **rehearsal 账号池 彩排01–08**：同一账号进过某房间后赛程行控件会变化，复测请换号；`L12_A3_PLAYERS='彩排0X,彩排0Y' node artifacts/a3-prepare.mjs` 可开新现场。

---

## 四、本批末尾的未提交改动（接手第一件事）

`服务端WebSocket/TwelveLegions/L12PublicTriggerEffectPlans.cs`（未提交）：图坦卡蒙 `case ("S01-0207","enter",_)` 现在按"登场后=我方+1"预估军团数，条件不成立就**不构建** entryCards/entrySlot 步骤（用户裁定"图坦卡蒙自己算在内"，因此判定时序不变，只补计划阶段拦截）。**已编译通过，未跑测试、未过门禁、未提交。**

两个子代理正在并行（都只写一个测试文件、不改生产代码）：
- `tut-guard-tests`：`TwelveLegions.Tests/TutankhamunTombGuardPlanTests.cs`（不满足→无选择步骤；满足→可选且召唤成功）+ 8 条同类计划的前置校验核对表。
- `death-trigger-repro`：`TwelveLegions.Tests/DeathTriggerRevalidationTests.cs`（格温莉安被神妙行军击杀后是否触发；立花支援位阵亡是否触发亡语）。

**下一步顺序**：收两个子代理 → 跑门禁（`-Level Focused`，不传 `-ChangedPaths`）→ 提交 → 按结果决定格温莉安/立花关单或立项。

---

## 五、未闭环风险（如实）

1. **正式服仍是 798f6777**：本批所有收益（A2b、U-P0/P1、S 验证、重连修复、9 个契约脚本接线）都只在测试服或只在源码里。
2. **A3 聊天往返未验**：房内聊天是缺口（已裁定应做在站点聊天内），尚未立项。
3. **格温莉安 / 立花**：注册表 `BUG-20261120-GWEN-TACHIBANA` 写明"未定位、保持非终态"；用户以为已修，**实际未修**，复现测试在跑。
4. **异画丢失两条**（`2e7ce237` 异画傀儡 / `b99c66cc` 孙悟空）：用户裁定**都是缺陷**，注册表分析指向 `ResolveOwnedAlternateArtUrls` 的**静默丢弃**（要求 `IsGrantableAlternateArtDefinition` + `owned.Contains` + `BaseCardId == selection.Key`），需运行时异画授权数据；尚未立项。
5. **阿伊前排进攻不能给自己加攻**（`f8bd01fb`，用户裁定为缺陷）与**互相绝防记录缺失**（`8548fff3`，用户裁定为缺陷且要覆盖同类记录）：尚未核验根因、未立项。
6. **平面公主+杨戬**（`c7902235`）：用户裁定**当前符合规则**（平昭阳改后是 2 点非致命伤害，打不了 2 血主宰），**尚未执行关单**（`rejected`）。

---

## 六、待办（按可执行顺序）

1. 收子代理证据 → 门禁 → 提交图坦卡蒙修复；按结果关/立项格温莉安与立花。
2. 关 `BUG-20261122-c7902235`（rejected，理由：平昭阳改后为 2 点非致命伤害，无法伤害 2 血主宰）。
3. 立项并核验：阿伊加攻、互相绝防记录（含同类记录覆盖）、异画丢失两条（同一根因 + 全 324 卡扫描）。
4. 房内聊天缺口立项（用户裁定应做在站点聊天内）。
5. 正式服部署（需你显式授权）：本批 + 之前媒体海报批次一起上，成功后按 `DEPLOY-HK.md` 四步收尾。
6. 台账 lint（D5）与 P2 读审计量测仍挂账。

---

## 七、环境与凭据位置

- 正式服：`artifacts/deploy-credentials.json`（**从不入库**）
- 测试服管理员：`artifacts/testrun-admin-secret.txt`；彩排口令：`artifacts/rehearsal-secret.txt`（**从不入库**）
- SSH：`root@154.201.80.91`（host key 只按 IP 记在 known_hosts）
- 证据：`artifacts/bug-queue-all.json`、`bug-open-details.json`、`a2b-e2e-*.json`、`a3-*-result.json`、`a3-ruling-after.png`、`u9-measure-*.json`
- 部署产物：`artifacts/deploy/<40-hex>/`；日志 `artifacts/release-*.log`、`testrun-deploy-*.log`
