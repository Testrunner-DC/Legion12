# LC-04A｜真实 WebSocket 连接代际与请求至多一次矩阵

状态：开发候选已完成连续统一门禁，等待 L12-main 独立验收、集成和推送。子批未推送、未部署。

## 1. Main 对齐与精确范围

- 权威基线：`origin/main@374f3804ba6a40a88ccf4c9554322aa853145894`。
- 唯一目标：补齐既有真实 WebSocket 矩阵的三个缺口，不把 LC-03A 已覆盖路径重新宣称为新成果。
- 精确租约仅为本报告、`scripts/test-l12-lc04a-room-reconnect.ps1` 和 `TwelveLegions.Tests/WebSocketTransportRecoveryAdversarialTests.cs`。
- 提示语义 A 批租用服务端 Prompt／引擎、前端 Prompt 展示及 P1 合同夹具；本批与其文件零重叠。本批只读取稳定 PromptId／Revision，不断言文案、不改变提示含义。
- 不修改服务端产品代码、前端、卡效、持久化格式、权限规则、提示语义或总计划；不推送、不部署。若测试证明必须修改产品，停在最小红测并另行申请修复批。

## 2. 不重复建设的既有证据

`WebSocketTransportRecoveryAdversarialTests` 已覆盖真实双玩家与普通观战者、同 requestId 异载荷重放、替代连接 generation、旧 socket fence、恢复 revision／完整快照、严格 delta、三接收者 stateHash 与隐私收敛、命令计数。`WebSocketConnectionGenerationTests` 已覆盖连接代际。

本批直接复用同一个 `WireClient`、`L12WebSocketServer + ClientWebSocket + MatchRecorder` 和已有隐私／投影断言；没有建立第二套网络客户端、房间驱动器或产品观察口。

## 3. 唯一新增验收矩阵

| ID | 网络时序 | 验收条件 |
| --- | --- | --- |
| LC04A-A | 当前连接代不读取响应，连续双发同 requestId＋同载荷 | 两个请求帧均写入传输链后才消费响应；两个响应收敛到同一 Revision、stateHash 和玩家投影；命令只新增 1 条；Journal 去重基线中该 requestId 只有 1 条 |
| LC04A-B | 玩家连接被替代后，新连接重发 A 的同 requestId＋同载荷 | 返回原权威投影；Revision 与命令数不变；Journal 去重基线仍只有 1 条 |
| LC04A-C | 另一玩家替代连接完成恢复后，立即以新 requestId 连续双发第一条合法动作 | `session → full gameState → recoveryComplete` 顺序明确；恢复完成前测试客户端不可写；两帧发送完毕后才消费两个响应，命令只新增 1 条，双方玩家与观战者最终 stateHash 一致 |

恢复消息序列还要求 `recoveryComplete.recoveryRevision`、完整状态 Revision 和替代前权威 Revision 三者相同。连接只有处理到 `recoveryComplete` 后才进入测试客户端可写状态。

## 4. 原子性与持久化证据

- A 和 C 均在同一当前 socket 上合法地先后完整写入两个请求帧，发送期间不读取任何服务端响应；两帧都进入传输链后才消费两个响应，因此不是收到结果后才发起的普通重试，也不依赖 `ClientWebSocket` 未承诺的多 send 并发行为。
- 房间命令数通过 `MatchRecorder.GetMatchAsync` 核对；requestId 去重记录通过既有 `LoadJournalEngineAsync` 读取，没有为测试新增产品端点。
- B 在替代连接上重发完全相同的请求；投影以结构化 JSON 深比较，命令计数和 Journal request 计数保持不变。
- C 的首个新请求完成后，玩家甲、玩家乙和普通观战者继续用既有 stateHash 与隐私哨兵合同核对，后续增量和完整同步路径不得退化。

## 5. 预算、门禁与停止条件

- `WebSocketTransportRecoveryAdversarialTests|WebSocketConnectionGenerationTests` 专项连续运行两次，单次不超过 60 秒。
- 统一脚本还运行 P1、P2/P3 架构测试与 P0—P4 架构锁，总耗时不超过 120 秒。
- 候选提交前执行 `git diff --check`，并确认 diff 仅含三个租约文件。
- 完整 Release 由 L12-main 独立执行。
- 任一连接代、Revision、命令数、Journal request、权威投影、stateHash 或私密哨兵分叉，出现不稳定或超时，或需要修改租约外产品文件时立即停止；不得删除断言、放宽预算或绕过真实网络入口。

## 6. 当前实测

首次专项：3/3 通过，包括真实传输长链 1 项和连接代际 2 项；测试运行约 3.0 秒。当前代连续双发、替代连接同 requestId 重发、恢复完成后的首个新 requestId 连续双发均只形成一条持久化命令和一条 request 去重记录。未发现产品红项。

Main 差异审查指出同一 `ClientWebSocket` 不保证多个 send 并发安全；候选已按其回执移除不受支持的多 send 并发，改为合法的连续完整写帧，且发送期间不读取响应。修正后的统一脚本连续运行两次，结果均为真实 WebSocket／连接代际 3/3、P1/P2/P3 架构测试 7/7、P0—P4 架构锁通过；专项墙钟分别为 9.0 秒和 4.5 秒，统一脚本墙钟分别为 11.5 秒和 7.0 秒，低于 60／120 秒预算。两轮均无失败、无跳过、无不稳定重试。
