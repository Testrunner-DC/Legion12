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
| `temp` | 临时文件 | 最后一次目录及文件写入满24小时，且未被进程使用 |
| `artifacts` | 测试、网络计量、部署制品 | 测试至少2份；部署保留线上、回滚、待发布和最近2份的并集 |
| `archives` | 旧脏工作树的必要恢复资料 | 保存补丁、清单、哈希，不保存完整依赖/构建物 |

## 容量门禁

运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\audit-l12-storage.ps1 -Strict
```

默认预算重点限制：活动工作区 2.2 GiB、卡图 650 MiB、`node_modules` 220 MiB、测试产物 500 MiB、部署产物 700 MiB、缓存 1.2 GiB。保留当前开发工作区的一份 `dist/bin/obj` 热构建，以支持增量编译和正在运行的预览；不批量删除它们。`dist` 上限100 MiB，包含约60 MiB从public复制的官方桌垫、卡背等素材，原5 MiB上限不适用于完整产物。

卡图只保留两类永久数据：`source-library`中的原始归档，以及`D:\L12-assets\published\current`当前完整内容寻址版本。Git当前树、前端`public/cards`、发布包和服务器运行目录均不得再保存旧PNG副本；Git历史不做破坏性改写。服务器只在新版本线上校验通过后清理非活动内容哈希版本和旧`/cards`目录。

清理先预览，确认后执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\clean-l12-generated.ps1
# 核对服务器 deployment-info.txt 后，填写完整40位哈希；不能沿用上次发布值。
.\scripts\clean-l12-generated.ps1 -ProductionCommit <线上提交> -RollbackCommit <上一回滚提交>
# 审查本次精确列表后，同样参数加 -Apply。待发布版本通过 -PendingCommits 显式保护。
```

脚本只处理 `D:\GPT\Legion12` 内明确的可再生目录：拒绝任意祖先/子孙目录联接；无法读取进程信息时停止；活跃文件跳过；应用前再次核验。默认没有提供线上/回滚哈希就跳过全部部署包清理。保留包及其卡图依赖先核对哈希，未引用的旧卡图压缩包才可删除。每次删除在 `artifacts/cleanup` 保存文件路径、大小、SHA256和执行状态；这些日志用于核对，不是文件备份，旧二进制需从对应提交与原素材重新生成。

旧独立验证目录只接受显式 `-ObsoleteVerificationDirectory verify-日期-名称`，必须已人工确认过期、超过24小时且只包含JSON和归档包。不自动删除源码、运行数据库、secrets、会话文件、原始卡图、其他工作区、依赖和热缓存。根目录严格使用实际物理目录，不能传入兼容联接。

2026-09-05维护已清理12个旧发布目录、2份不再引用的旧卡图压缩包和 `verify-20260903-prompt`，合计约2.17 GiB。保留 `8ba286b` 线上版本与 `9af7b28` 回滚版本及 `910b3449…` 卡图包。详情见 `docs/HANDOFF.md`；后续维护必须重新获取版本。

## Codex 与 VPN

本项目的长会话 JSONL 曾达到十余 GiB；完整历史派发给多个子任务，会重复上传大量上下文。项目规则因此禁止 `fork_turns="all"`，只允许无历史或最小窗口。

Codex 会话属于全局应用数据，其中同时包含 Legion12、HeroRush 和其他任务，不能归入任何单一项目目录。关闭 Codex 后，将全局 `sessions` 目录统一迁至中立位置 `D:\GPT\CodexData\sessions`，并在原位置建立目录联接。这样既释放 C 盘空间、保持 Codex 原路径可用，也不会把其他项目数据混入 `D:\GPT\Legion12`。

本次迁移已把完全闲置的日期目录搬到 D 盘；当前正在写入的主任务必须等 Codex 关闭后再收口。关闭应用后从普通 PowerShell 运行：

```powershell
powershell -ExecutionPolicy Bypass -File D:\GPT\Legion12\app\ops\windows\finalize-l12-codex-session-move.ps1
```

Codex 仍在运行时可加 `-PlanOnly` 只读预览。正式执行会先拒绝正在运行的 Codex，保留 HeroRush 等已有外部目录联接，验证冲突文件哈希后才删除重复副本，兼容迁移早期误放在 `D:\GPT\Legion12\codex-session` 的会话，最后把整个全局 `sessions` 路径替换为指向中立目录的联接。目录联接创建完成后不需要持续管理员权限。

网络计量：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\watch-l12-network.ps1
```

日志写入 `artifacts\network`，分别记录上传与下载增量，便于识别下一次突发流量方向。脚本不记录访问内容，也不会自动断开 VPN。

## GitHub Actions

推送与拉取请求只执行规则测试、平台持久化测试及前端构建。Linux 发布包仅在手动运行工作流或推送 `v*` 标签时生成；发布脚本对可选 `runtimes` 目录作存在性检查，并输出明确的制品大小错误。
