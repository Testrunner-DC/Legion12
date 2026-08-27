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

`ops\windows\maintain-l12-storage.ps1` 默认只预览，不删除文件：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\maintain-l12-storage.ps1
```

确认列表后显式应用：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\maintain-l12-storage.ps1 -Apply
```

默认策略：

- 测试产物保留 3 天；
- 临时文件保留 24 小时；
- 每个提交的部署产物只保留最新 2 份；
- `active-*` 与 `main-*` 紧凑迁移快照各保留最新 2 份；
- 日志保留 14 天且最多 20 份；
- 检测到未提交 Git 改动的目录一律跳过。

可以用参数覆盖保留期限，但不要把 `Root` 指向磁盘根目录、用户目录或其他项目。

## 恢复与兼容

旧的 `D:\GPT\L12-cache`、`D:\GPT\L12-build-cache`、`D:\GPT\L12-deploy-artifacts` 和 `D:\GPT\L12-deploy-temp` 目前仅保留目录联接，实际数据均在统一目录。新脚本直接使用统一路径，旧链接只用于兼容尚未更新的个人命令。

`archives\legacy` 中的旧克隆保留原分支和未提交内容。确认不再需要前，不应手工删除。迁移前的主仓库和权威工作树恢复包位于 `migration\compact-snapshots`；它们只保存当时未提交的文件，已提交内容从清单记录的 Git HEAD 恢复，避免重复保存数 GiB 的构建输出和 Git 对象。
