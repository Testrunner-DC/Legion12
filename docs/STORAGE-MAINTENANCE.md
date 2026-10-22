# Legion12 本地存储布局与维护

> 当前权威目录、容量预算和清理门禁见 [STORAGE-GOVERNANCE.md](STORAGE-GOVERNANCE.md)。本文件保留迁移历史与兼容路径说明。

## 统一目录

项目的物理文件统一放在 `D:\GPT\Legion12`：

- `app`：当前唯一权威开发工作树；
- `repo`：保留旧未提交源码并承载公共 `.git` 数据，禁止作为日常开发入口；
- `workspace`：指向 `app` 的兼容目录联接；
- `cache`：NuGet、npm、.NET CLI 等可再生成缓存；
- `artifacts`：部署包、测试输出、临时文件和队列快照；
- `tools`：本项目使用的独立 SDK 与辅助资料；
- `archives`：仍有未提交内容或需要人工复核的旧副本；`archive` 是兼容联接；
- `migration`：迁移清单和仅包含未提交文件的紧凑安全快照。

Codex 原工作区中的 `L12work` 是指向 `D:\GPT\Legion12\workspace` 的目录联接。关闭完全文件访问后，仍从该入口继续开发，不需要把项目复制回 C 盘。

## 清理策略

旧入口 `ops\windows\maintain-l12-storage.ps1` 已停用：无论预览还是 `-Apply`，都会在扫描和删除前拒绝执行。它原先仅按年龄／份数分类，无法可靠保护失败证据、当前版本和独有备份，不再作为清理依据。

统一使用受保护入口，默认只预览：

```powershell
pwsh -NoProfile -File .\scripts\clean-l12-generated.ps1
```

核对具名清单及真实物理路径、占用、成功／失败回执和必要版本保护后，才可应用。部署包清理还必须传入实际正式／回退／测试提交；参数及范围见 [STORAGE-GOVERNANCE.md](STORAGE-GOVERNANCE.md)。此入口不会自动清理源码、运行数据库、任意旧 `bin/obj` 或未知临时目录。

删除前必须证明可再生或已有完整核验保留件。成功／失败记录、唯一业务数据和事故材料不能仅因“旧”或超预算而删除；可再生副本应清除，不把冗余搬到 E 盘无限保留。无法证明的单独保留，不能为刷出容量数字放宽范围。

## 恢复与兼容

旧的 `D:\GPT\L12-cache`、`D:\GPT\L12-build-cache`、`D:\GPT\L12-deploy-artifacts` 和 `D:\GPT\L12-deploy-temp` 目前仅保留目录联接，实际数据均在统一目录。新脚本直接使用统一路径，旧链接只用于兼容尚未更新的个人命令。

`archives\legacy` 中的旧克隆保留原分支和未提交内容。确认不再需要前，不应手工删除。迁移前的主仓库和权威工作树恢复包位于 `migration\compact-snapshots`；它们只保存当时未提交的文件，已提交内容从清单记录的 Git HEAD 恢复，避免重复保存数 GiB 的构建输出和 Git 对象。
