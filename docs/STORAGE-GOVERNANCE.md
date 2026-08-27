# Legion12 本地存储治理

## 唯一物理根目录

所有项目文件统一位于 `D:\GPT\Legion12`。迁移完成后的结构为：

| 目录 | 用途 | 保留策略 |
| --- | --- | --- |
| `app` | 唯一可修改 Git 工作区 | 永久；不得复制为新的开发仓库 |
| `workspace` | 兼容旧工具的目录联接，目标为 `app` | 仅联接 |
| `source-library` | 原始卡图、表格、规则资料、TTS 脚本 | 永久；按来源归档 |
| `references` | GrandUMI、HeroRush 等只读参考 | 仅保留当前参考版本 |
| `tools` | .NET、NuGet、迁移辅助工具 | 同版本只保留一份 |
| `cache` | 可重建依赖缓存 | 超预算即可删除 |
| `temp` | 临时文件 | 24小时 |
| `artifacts` | 测试、网络计量、部署制品 | 测试2份、部署2份 |
| `archives` | 旧脏工作树的必要恢复资料 | 保存补丁、清单、哈希，不保存完整依赖/构建物 |
| `codex-session` | Codex 会话数据的 D 盘落点 | 关闭 Codex 后迁移并建立联接 |

## 容量门禁

运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\audit-l12-storage.ps1 -Strict
```

默认预算重点限制：活动工作区 2.2 GiB、卡图 650 MiB、`node_modules` 220 MiB、测试产物 500 MiB、部署产物 700 MiB、缓存 1.2 GiB。生产构建完成后不得长期保留 `dist/bin/obj`。

清理先预览，确认后执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\clean-l12-generated.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\clean-l12-generated.ps1 -Apply
```

脚本只处理 `D:\GPT\Legion12` 内明确的可再生目录，并拒绝越界路径。

## Codex 与 VPN

本项目的长会话 JSONL 曾达到十余 GiB；完整历史派发给多个子任务，会重复上传大量上下文。项目规则因此禁止 `fork_turns="all"`，只允许无历史或最小窗口。关闭 Codex 后，将全局 `sessions` 目录迁至 `D:\GPT\Legion12\codex-session\sessions` 并在原位置建立目录联接，可释放 C 盘空间且不改变 Codex 路径。

本次迁移已把完全闲置的日期目录搬到 D 盘；当前正在写入的主任务必须等 Codex 关闭后再收口。关闭应用后从普通 PowerShell 运行：

```powershell
powershell -ExecutionPolicy Bypass -File D:\GPT\Legion12\app\ops\windows\finalize-l12-codex-session-move.ps1
```

脚本验证冲突文件哈希后才删除 C 盘副本，并最终把整个 `sessions` 路径替换为目录联接。

网络计量：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\watch-l12-network.ps1
```

日志写入 `artifacts\network`，分别记录上传与下载增量，便于识别下一次突发流量方向。脚本不记录访问内容，也不会自动断开 VPN。

## GitHub Actions

推送与拉取请求只执行规则测试、平台持久化测试及前端构建。Linux 发布包仅在手动运行工作流或推送 `v*` 标签时生成；发布脚本对可选 `runtimes` 目录作存在性检查，并输出明确的制品大小错误。
