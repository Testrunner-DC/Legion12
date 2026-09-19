# 生产边界与 GrandUMI 遗留清理（2026-09-19）

## 目标

让十二军团生产服务器、规则测试和平台测试只依赖十二军团代码，移除已退出运行时的 GrandUMI 卡牌、效果、对局、旧网关及其专属测试和说明，降低构建、检索和维护噪声。

## 已完成边界

- 生产入口仍使用 `服务端WebSocket/GrandUMIServer.csproj` 和 `GrandUMIServer.dll`，以保持部署脚本、服务单元和回滚路径兼容；程序集名称的迁移不与本次清理混做。
- 生产工程显式排除已退役的 `Cards`、`Effects`、`Game`、`WebSocketBridge.cs`、`WsSession.cs` 和 `BugReportStore.cs` 路径，防止旧实现被重新放回后静默进入发布包。
- 平台、账号、后台、赛事、存储与控制面测试已迁入独立的 `TwelveLegions.Platform.Tests` 工程；规则与对局测试继续位于 `TwelveLegions.Tests`。
- Windows、GitHub 和变更分级门禁均改为运行完整平台测试工程，不再使用名称筛选从混合 GrandUMI 测试工程中挑选用例。

## 已清理内容

- GrandUMI `Cards`、`Effects`（含 1,155 个 Scripted 文件与旧 Definitions）、`Game`。
- 旧 `WebSocketBridge`、`WsSession`、`BugReportStore`。
- GrandUMI 专属测试、夹具与混合测试工程。
- `docs/grandumi`、GrandUMI 学习稿和旧网关集成说明。

本次从当前工作树移除 1,292 个已跟踪遗留文件，历史版本仍按 Git 正常保留；没有改写仓库历史。

## 验证口径

- 生产服务器在显式排除旧源码后以及物理删除后均可 Release 构建，0 警告、0 错误。
- 独立平台工程迁移前后及物理删除后均完成全量验证：含并行后台改动的当前工作树 146/146，通过仅含本批差异的隔离工作树 144/144。
- Codex 路由、GitHub 发布工作流、单次发布门禁和 41 项部署行为回归通过。
- 仅含本批差异的隔离工作树完成 `TwelveLegions.Tests` 4078/4078，0 失败、0 跳过。
- 隔离工作树生产发布成功，发布目录共 13 个文件、7,490,548 字节；保留兼容程序集名 `GrandUMIServer.dll`。
- 原工作树中并行统一卡效批次的中间态失败不纳入本批提交，也不得用于绕过该批次自身的回归要求。

## 后续约束

- 不恢复 GrandUMI 运行时或专属测试作为十二军团发布依赖。
- 若未来迁移 `GrandUMIServer.csproj` / `GrandUMIServer.dll` 名称，必须作为独立部署兼容批次处理，并同步 Windows/Linux 部署、服务单元、健康检查和回滚脚本。
- Git 历史体积不在本批重写；如确需缩小克隆体积，应单独评估历史重写及所有协作者重新克隆成本。
