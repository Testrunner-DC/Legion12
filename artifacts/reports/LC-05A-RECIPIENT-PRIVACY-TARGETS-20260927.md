# LC-05A｜全接收者隐私、历史回放与共享过滤收口

状态：开发候选已完成聚焦与 Batch 验收；提交级 Release 结果由最终候选 SHA 的交接回执绑定。未推送、未部署。

## 1. Main 对齐与范围

- 精确基线：`origin/main@6aea53d995c6de97df53843152d4f0c0c61f9d79`。
- 独立工作树：`tmp/lc05a-recipient-privacy`；分支 `codex/lc05a-recipient-privacy`。
- 初始租约：本报告、`scripts/test-l12-lc05a-recipient-privacy.ps1`、`TwelveLegions.Tests/RecipientPrivacyMatrixAdversarialTests.cs`。
- 首轮矩阵发现产品红灯后，Main 独立批准修复租约：`MatchRecorder.cs`、`L12GameEngine.cs`，以及一个纯接收者可见性辅助文件 `L12RecipientVisibility.cs`。
- 没有修改 Prompt、前端、持久化格式、既有测试、总计划或规则数值；没有测试服/正式服部署权限。
- 本批扩展 P2 接收者投影与 P3 玩家回放边界，不改变 P0/P1/P4。

权限口径由 Main 明确：

1. 玩家回放必须与该历史时点的实时玩家视角一致；
2. 不得看到对方手牌、盖伏身份、未公开天灾、未完成试炼、owner-only Prompt，以及只属于另一方的 `private-return`、`private-disaster-reveal`、`disaster-selected` 卡牌与文本；
3. 已公开信息只影响公开之后的新帧，历史帧不得回写；
4. `SnapshotForReferee()` 保持内部裁判权威视图：可见双方未完成试炼与全部天灾，但不可见双方手牌、盖伏身份或 owner-only Prompt；
5. 不宣称不存在的 referee WebSocket role、referee replay 或 spectator replay。玩家回放只使用 `GetMatchForPlayerAsync`，`GetMatchAsync` 仅作内部权威历史字节证据。

## 2. 同型扫描与共享根因

只读扫描覆盖：

```text
rg -n 'SnapshotFor\(|SnapshotForSpectator|SnapshotForReferee|SnapshotForGm|SnapshotForInternal|SnapshotField|SpecialZonesSnapshot|FilterDisasterEvent|DisasterVisibilitySnapshot' 服务端WebSocket/TwelveLegions TwelveLegions.Tests -g '*.cs'
rg -n 'waitingPrompt|waitingSummary|ChoiceLabels|ChoiceConsequences|Presentation' 服务端WebSocket/TwelveLegions TwelveLegions.Tests -g '*.cs'
rg -n 'GetMatchForPlayerAsync|GetMatchAsync\(|SanitizeRecordedState|SanitizeRecordedCommand|RedactCardArray|RedactCoveredField|RedactEffectHandAddStack|L12TrialProgressVisibility' 服务端WebSocket/TwelveLegions TwelveLegions.Tests -g '*.cs'
rg -n 'private-return|private-disaster-reveal|disaster-selected|effect-hand-add|prompt-resolved' 服务端WebSocket/TwelveLegions TwelveLegions.Tests -g '*.cs'
rg -n 'SnapshotForReferee' TwelveLegions.Tests 服务端WebSocket/TwelveLegions -g '*.cs'
```

同型面包括双方手牌、双方盖伏、双方未完成试炼、双方未公开天灾、私密 Prompt、私密放回、私下查看天灾、天灾选择、Prompt 审计、检查点和双方玩家回放。

首轮红灯稳定复现两次：实时快照正确，但双方玩家历史首帧对称泄露对方未公开天灾，以及 `private-return`、`private-disaster-reveal`、`disaster-selected` 的卡牌与文本。`Players[].Hand`、盖伏主体和未完成试炼主体已被原 sanitizer 正确裁剪；手牌实例哨兵来自未裁剪的 `private-return` 历史事件。

根因是两条出口使用了不同规则：

- 实时 `SnapshotForInternal` 经 `DisasterVisibilitySnapshot` / `FilterDisasterEvent` 按接收者裁剪；
- 玩家回放 `SanitizeRecordedState` 虽已处理牌库、手牌、盖伏、试炼、效果加入手牌栈和 PendingPrompt，却没有对 `ChosenDisasters`、`Events`、`LastAction`、`Log` 应用同一时点的接收者规则。

这导致 P2 实时正确、P3 回放旁路，不是某张卡或单个事件的特例。

## 3. 共享修复

新增纯函数端口 `L12RecipientVisibility`：

- `CanSeeDisaster` 唯一判断天灾是否已公开、属于当前玩家或裁判全可见；
- `ProjectActionEvent` 唯一处理试炼进度、私密放回、私下查看天灾和天灾选择；
- 无引擎推进、无状态写入、无持久化副作用。

实时引擎的 `DisasterVisibilitySnapshot` 与 `FilterDisasterEvent` 改为调用该端口，保持原序列化语义。玩家回放将历史权威状态只读反序列化后调用同一端口，并：

- 对 `ChosenDisasters` 保留实时视图已有的匿名槽位实例标识、`Hidden=true` 与 ownerIndex，但不保留未授权卡号、卡名、图像或规则字段；
- 对 `Events` 与 `LastAction` 使用同一事件投影，并保持实时视图相同的 128 项有界窗口；
- 清空实时视图本就不公开的自由文本 `Log`；
- 对内部 `DisasterPool`、`SelectedDisasters` 和 `DisasterDeck` 保留牌背而不暴露身份；
- 历史状态无法安全解释时，玩家出口对天灾选择、事件和 LastAction 失败关闭；内部 `GetMatchAsync` 权威记录不改写。

没有按卡号、事件文本或 LC-05A 哨兵写特判。

## 4. 验收矩阵

| ID | 范围 | 结果 |
| --- | --- | --- |
| M1 | 同一局双手牌、双方盖伏、双方未完成试炼、双方未公开天灾、私密 Prompt 与私密事件 | 通过 |
| M2 | owner `ChoiceLabels` / `ChoiceConsequences` 分离；等待视图精确四字段 | 通过 |
| M3 | 玩家0、玩家1、观战、裁判检查点前后逐 JSON 节点等价 | 通过 |
| M4 | 后续公开只改变新快照；先前四视角快照与 Journal 首帧字节不变 | 通过 |
| M5 | 私密 Prompt 审计只留下通用完成记录 | 通过 |
| M6 | 双方玩家回放按历史时点裁剪；`ChosenDisasters` 与 `Events` 逐事实等同当时实时玩家视图 | 通过 |
| M7 | 固定种子连续两轮一致 | 通过 |
| M8 | 相邻 Prompt、试炼、既有回放、WebSocket、P1/P2/P3、P0—P4 与 Batch | 通过 |

`waitingPrompt` 白名单固定为 `playerIndex`、`playerName`、`kind`、`waitingSummary`。owner 文本、候选、Data、Continuation、Labels 和 Consequences 在对手、观战、裁判三个等待视图中均不存在。

## 5. 验证证据

聚焦统一脚本：

- 新矩阵第1轮：2/2，7.6 秒；
- 新矩阵第2轮：2/2，2.7 秒；
- Prompt、试炼、审计、既有玩家回放、真实 WebSocket 恢复：33/33，8.8 秒；
- P1/P2/P3：7/7；
- P0—P4 架构锁：通过；
- 统一墙钟：21.5 秒，低于 180 秒预算。

Batch：

- 性能架构、资源同步、请求可靠性、动作门、HTTP 流量与可观测性全部通过；
- P0—P4、P1 内核依赖边界、Git 空白、卡效运行时证据、公开声明、私密区域事务和试炼完成声明全部通过；
- S1/S2/ST 全量审计和原子零遗留通过；324 张卡、0 个无运行入口；
- Release 配置规则测试 `5659/5659` 通过。

本批首次 Batch 尝试仅因新工作树没有 `opcgpro-vue/node_modules`，在最前置性能脚本加载 TypeScript 前退出；随后只把忽略目录连接到规范依赖缓存，未安装依赖、未改源码，并从头重跑上述完整 Batch。

## 6. 同步边界

- 候选只提交当前分支，不推送、不部署；由 Main 独立审查、运行提交级 Release、集成并决定同步。
- `artifacts/reports` 默认被忽略，本报告需要在候选提交中显式加入。
- 测试服当前已部署的 `6aea53d9` 与本批无关；不得把部署状态混入 LC-05A 候选。
- 若 Main 验收发现新的私密哨兵、旧回放兼容或实时序列化分叉，停止同步并回到共享过滤端口修正，不得放宽矩阵。
