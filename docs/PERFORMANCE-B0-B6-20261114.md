# 全站／对战卡顿专项 B0–B6（2026-11-14）

## 本批结论

本批在本地候选完成 B0–B6 的架构门禁、客户端增量重建修复、前端类型检查、后端构建和 production/testrun 双构建。未部署、未写线上配置、未生成真实对局或容量统计。

| 批次 | 覆盖面 | 状态 | 证据 |
| --- | --- | --- | --- |
| B0 | 全站 HTTP/WS 预算、固定性能阶段、运行时慢请求／错误窗口 | 通过 | `ops/performance-budgets.json`、`L12PerformanceMetrics.cs`、`check-performance-architecture.mjs` |
| B1 | 公开／私有牌库摘要投影、分页与有界读取 | 通过 | `L12PlatformStore.PublicDeckSummaryQueries.cs`、`L12PlatformStore.DeckQueries.cs` |
| B2 | 对局房间闸门、动作／持久化观测、出站可替换队列 | 通过 | `L12RoomManager.cs`、`L12PerformanceMetrics.cs`、`L12OutboundConnection.cs` |
| B3 | HTTP 流量闸门、慢请求／5xx／429 采样 | 通过 | `L12HttpReliability.cs`、`check-http-traffic-guard.mjs` |
| B4 | 管理事务、切季／恢复的阶段级计时和失败计数 | 通过 | `L12PlatformOperationMetrics.cs`、`L12PerformanceMetrics.cs` |
| B5 | 聊天全局／账号级消息项和字节上限、关键消息不被合并 | 通过 | `L12ChatRuntime.cs`、`L12OutboundConnection.cs` |
| B6 | 客户端 `gameStateDelta` 从全量 JSON 深拷贝改为根／路径写时复制；手牌分支单独隔离 | 通过 | `src/l12/net.ts`、`test-b6-delta-structural-sharing.mjs`、`test-hand-array-delta.mjs` |

## B6 实际修复

`rebuildDeltaGameState` 现在只复制根对象、`state` 以及变更路径上的数组／对象；未变更分支保持共享。手牌差量在恢复前仅复制相关玩家的手牌数组和卡对象，避免恢复函数的原地写入污染基线。事件窗口追加只复制追加数组，不再对整个 baseline 或 append 做 JSON 深拷贝。任何坏路径、版本不匹配或手牌校验失败仍返回 `null`，沿既有权威全量恢复。

## 验收结果

- B0–B6 专项契约：`19/19`。
- B6 结构化写时复制：`10/10`。
- 事件窗口差量：`32/32`。
- 手牌差量与恶意路径：`29` 个案例通过。
- 性能架构及观测、队列、HTTP、清理和测试归属门禁：全部通过；工作负载来源测试在 D 盘受控缓存重跑 `88` 案例通过。
- Vue TypeScript 项目引用检查：通过。
- `GrandUMIServer.csproj --no-restore`：通过，0 warning / 0 error。
- 前端 production 与 testrun 构建：通过；产物位于 `D:/GPT/Legion12/cache/primary/perf-b0-b6-20261114/`。

## 边界

本批证明的是代码路径、预算和本地受控构建，不等同于线上 FPS、真实公网容量或正式服发布。正式／测试服部署、线上版本切换和生产数据读写均未执行，需另行获得部署指令后再走 Release 四步标准。
