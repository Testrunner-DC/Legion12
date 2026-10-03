# Legion12 本地存储治理

## 2026-10-03发布后实际盘点与有界优化

本次正式发布和维护解除实际完成后，Main按物理根目录遍历，跳过每个reparse目录/文件，使用卷序号+文件索引去重硬链接，并读取文件实际分配量（压缩/稀疏文件使用GetCompressedFileSize）。两次扫描错误0、重复硬链接0；目录联接只记录目标、不重复遍历。此口径不包含NTFS元数据，也不能推断项目根之外的硬链接归属。

| 项目目录 | 优化前逻辑/分配 GiB | 优化后逻辑/分配 GiB |
| --- | --- | --- |
| C盘旧项目根 | 23.253 / 23.539 | 22.356 / 22.640 |
| D盘项目物理根 | 149.197 / 149.530 | 142.098 / 132.444 |

分步实际空闲变化：9处旧NuGet packages与保留的cache/primary/nuget/packages逐文件SHA256一致，清理后C盘+0.90GiB、D盘+7.13GiB；197个旧query-bounds合成夹具文件采用可逆NTFS压缩，197/197内容哈希保持、删除0，D盘另+9.97GiB。以上不重复计入此前10个bin释放的C2.61/D10.44GiB。最后实际空闲约C18.07、D55.51GiB；外部进程占用会造成少量波动，不把盘面全部变化归因于本次清理。恢复旧依赖可按清单从主缓存复制或重新restore，压缩夹具可逐文件compact /u还原；不可回写运行数据库。

证据均在D盘artifacts：storage-audit-20261003.json、storage-temp-audit-20261003.json、storage-audit-after-20261003.json、cleanup-duplicate-packages-20261003.json（逐文件恢复SHA256）及compress-closed-query-fixtures-20261003.json（逐文件前后SHA256）。具体操作脚本同目录；没有修改通用治理脚本、业务代码或热缓存默认策略。

实际膨胀来源及仍保护的范围：

- 优化前D盘cache约87.02GiB，primary/temp约46.13GiB，其中match analytics约20.36GiB、lc02约5.61、lc01约5.11。MatchAnalyticsTests.TestDirectory每次生成独立目录，query-bounds用5万行合成数据但没有目录清理；长链夹具有finally删除，却可能因SQLite池/句柄失败而残留。上述是源码和目录相互核对，不把所有目录视作可删成功证据。本次只压缩闲置旧查询库，最新和失败内容均保留。
- D盘app约24.85GiB、worktrees约20.38GiB；C盘tmp约13.33GiB，多套历史验证树和各自构建/依赖副本叠加。规范app脏树、公共Git、.runtime、未收口及未知工作树全部保留；未删除分支或重置规范工作树。应先按提交归属归档未提交差异，再另定逐树白名单，不执行整根清空。
- 历史NuGet副本是实际复制，不是硬链接虚计。只删了与主缓存同哈希的9个packages子目录，父目录、源码、HTTP日志、报告、备份仍保留。当前主缓存、最新编译、运行库和Codex/Adobe进程未终止或压缩。
- 正式45b65e47、回退d1659fa3、测试934a68c6发布包、卡图依赖、校验备份、F2真实隔离副本及损坏拒绝/失败证据全部保护。全局Codex会话和跨项目运行文件没有处理。
- 现有旧容量预算将热运行库/历史事故数据与可重建缓存混计，不能因OVER就直接删。后续建议分别预算源码、当前运行、依赖、成功证据、事故保留及隔离副本；逐测试补清理责任、SQLite池释放与有界重试，失败夹具保留最小复现和哈希。此段只是建议，不自动立项、改测试或削弱门禁。

## 2026-10-03第二步执行及防复发边界

两棵已关闭、无脏差异且提交已进入主线的C盘验证树已归档，归档哈希、Git恢复引用和非强制移除回执在 `D:\GPT\Legion12\archives\storage-20261003\closure.json`：C盘实际释放约0.213GiB，D盘归档成本约0.171GiB。第三棵后台验证树实际仍有22项暂存差异，未删除。

旧 `server-backups` 和 `.cache` 已逐文件核对后迁至D盘，在原精确路径保留兼容联接；两次操作合计C盘空闲增加约3.561GiB、D盘占用增加约3.633GiB。这是转移而非总容量释放。第二项曾因复制原目录所有者权限失败，在确认目标为空、来源完整之后，仅恢复有效访问权限并完成全部内容哈希核验；未盲目重跑删除。回执为 `artifacts/relocate-c-legacy-storage-20261003.json`。

当前候选三个项目的可重建 `bin/Debug` 已逐文件记录后删除，同项目 `bin/Release` 和源码保留，D盘实际释放约1.57GiB；证据 `artifacts/clean-candidate-debug-builds-20261003.json`。恢复方式是从保留源码重新构建Debug，不是旧文件备份。

测试入口开始使用单次自有临时根：只有实际测试进程退出，且本次TRX证明总数大于0、全部执行通过、失败和跳过均为0，才可删除该根。失败、空运行、丢失证据、目录联接或锁定情况均保留；TEMP/TMP/TMPDIR一致，规则与平台真实测试宿主核对隔离。CI保持完整两个测试项目，不削减门禁。成功记录小型计数回执；Release原有完整TRX按原预算保留。本地合成生命周期8场景、容量审计11项、清理及发布门禁专项已通过；完整Batch与干净Release以最终回执为准，不能据此宣称已发布。

## 唯一物理根目录

所有项目文件统一位于 `D:\GPT\Legion12`。迁移完成后的结构为：

| 目录 | 用途 | 保留策略 |
| --- | --- | --- |
| `app` | 唯一可修改 Git 工作区 | 永久；不得复制为新的开发仓库 |
| `workspace` | 兼容旧工具的目录联接，目标为 `app` | 仅联接 |
| `source-library` | 原始卡图、表格、规则资料、TTS 脚本 | 永久；按来源归档 |
| `references` | GrandUMI、HeroRush 等只读参考 | 仅保留当前参考版本 |
| `tools` | .NET、NuGet、迁移辅助工具 | 同版本只保留一份 |
| `cache` | 当前依赖、任务临时目录及历史夹具 | 超预算只触发审查；未知、失败、仍使用内容不能删除 |
| `temp` | 有明确归属的临时文件 | 必须有成功关闭证据、足够保留期且未被进程使用；仅年龄不构成清理授权 |
| `artifacts` | 测试、网络计量、部署制品 | 成功测试至少2份；失败/未知/PINNED保留；部署保护正式、回退、测试、待发布和最近2份并集 |
| `archives` | 旧脏工作树的必要恢复资料 | 保存补丁、清单、哈希，不保存完整依赖/构建物 |

## 容量门禁

运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\audit-l12-storage.ps1 -Strict
```

默认预算重点限制：活动工作区 2.2 GiB、卡图 650 MiB、`node_modules` 220 MiB、测试产物 500 MiB、部署产物 700 MiB、缓存 1.2 GiB。保留当前开发工作区的一份 `dist/bin/obj` 热构建，以支持增量编译和正在运行的预览；不批量删除它们。`dist` 上限100 MiB，包含约60 MiB从public复制的官方桌垫、卡背等素材，原5 MiB上限不适用于完整产物。

以上为默认Inventory总量盘点；历史总量仍可能超标，不能删除保护数据来刷绿。开发门禁另用 `-Scope Active -CandidateRoot <精确候选>`：源码、候选前端依赖（共享联接按唯一真实目标计量）、共享包及HTTP/npm/corepack缓存、任务temp/dotnet-home、三个项目bin/obj、两个前端dist、自有测试临时与回执分别预算。每个项目bin600MiB基于单一热配置约530–541MiB实测；同时保留Debug+Release超限。普通源码和清理输入拒绝联接，依赖联接的只读计量不构成目标清理授权。

卡图只保留两类永久数据：`source-library`中的原始归档，以及`D:\L12-assets\published\current`当前完整内容寻址版本。Git当前树、前端`public/cards`、发布包和服务器运行目录均不得再保存旧PNG副本；Git历史不做破坏性改写。服务器只在新版本线上校验通过后清理非活动内容哈希版本和旧`/cards`目录。

清理先预览，确认后执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\clean-l12-generated.ps1
# 核对服务器 deployment-info.txt 后，填写完整40位哈希；不能沿用上次发布值。
.\scripts\clean-l12-generated.ps1 -ProductionCommit <线上提交> -RollbackCommit <上一回滚提交> -TestCommit <测试服提交>
# 审查本次精确列表后，同样参数加 -Apply。待发布版本通过 -PendingCommits 显式保护。
```

脚本只处理 `D:\GPT\Legion12` 内明确的可再生目录：拒绝任意祖先/子孙目录联接；无法读取进程信息时停止；活跃文件跳过；应用前再次核验。没有完整正式/回退/测试哈希即跳过部署包清理，保护包及卡图依赖必须先验证清单和哈希。只删除已被完整合法旧清单引用、且不再受保护的卡图包；孤立未知包、残缺暂存目录不删。成功证据要求非空阶段全部显式通过，未知/失败/PINNED仍保护。每次删除在 `artifacts/cleanup` 保存文件路径、大小、SHA256和执行状态；这些日志用于核对，不是文件备份，旧二进制需从对应提交与原素材重新生成。

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
