# 伙伴提交与正式服发布指南

本文面向两类角色：伙伴负责把经过验证的改动提交到远端 `main`；维护者负责在获得本批明确授权后，将远端已验证版本发布到正式服。两种职责必须分开：伙伴不得自行部署，维护者不得把未同步或未验证的本地改动带上服务器。

## 当前环境与边界

- 唯一可修改的 Windows 工作区是 `D:\GPT\Legion12\app`。`D:\GPT\Legion12\workspace` 和旧路径只是兼容联接或历史工作区，不能在其中开发、提交或发布。
- 正式入口是 `https://legion-12.com`，WebSocket 为同源 `wss://legion-12.com/ws`，`www` 只做跳转。
- 正式服务器的应用仅监听 `127.0.0.1:8083`；稳定应用入口为 `/opt/legion12-test`，持久化运行数据在 `/opt/legion12-runtime`。
- `legion12-test.service` 是**当前正式服**的历史服务名，不是测试服。独立内部测试服务为 `legion12-testrun.service`、8084；其公网DNS/TLS仍单独管理，不要因为服务名含 `test` 而连接、重启或发布到错误环境。
- 旧服务器只保留迁移证据、release 和 runtime 的回滚材料，Legion12 应用已停用。禁止重启旧机服务、向旧机发布或让新旧两端同时接受写入。

不要在聊天、Issue、提交、脚本输出或文档中写入密码、Token、SSH 私钥、环境文件内容、数据库备份、真实玩家数据、房间密钥、会话或未脱敏日志。SSH 信任只能使用已由维护者人工核验的新服务器主机指纹；不要以 `ssh-keyscan` 的输出自动信任主机，也不要复制其他人的私钥。

## 伙伴：从本地改动到远端 main

开始前进入唯一工作区，并先保护其他人的改动：

```powershell
Set-Location D:\GPT\Legion12\app
git status --short --branch
git diff --check
git fetch --prune origin main
git merge --ff-only origin/main
```

`git merge --ff-only` 只允许快进：若失败、发现本地未提交内容、远端出现非快进历史或任何冲突，立即停止。不要用 `git reset --hard`、`git checkout --`、`git clean -fd` 或强推来“清理”工作区；这些操作可能删除伙伴的未提交或未跟踪资料。先向维护者说明分歧和涉及文件，待人工确认后再继续。

开发过程按改动粒度运行门禁：

```powershell
# 实现中
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-l12-change.ps1 -Level Focused

# 一个独立批次完成后
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-l12-change.ps1 -Level Batch
```

完成后再次检查差异，确认只包含本批内容，再提交和推送：

```powershell
git status --short
git diff --check
git add <本批文件>
git commit -m "中文说明本批变更"
git fetch --prune origin main
git push origin main
```

推送前若 `origin/main` 已前进，停止并安全整合远端改动；产生冲突时由相关作者人工处理，解决后重新运行与改动相称的门禁。不要把“能推送”视为“可以上线”：生产发布还需要维护者对该批次的明确授权。

## 维护者：发布远端已验证版本

部署电脑须具备 Git、PowerShell、Node.js、npm、tar、SSH，以及仓库 `global.json` 指定的 .NET SDK。`L12_CARD_ASSET_ROOT`（如设置）必须指向当前完整的 schema v3 卡图目录，清单应包含 324 张可玩卡和 38 张展示版本；不得为了通过门禁改用历史 schema v2 图库。先核对工具和 SDK：

```powershell
git --version
node --version
npm --version
dotnet --version
Get-Content .\global.json
```

如果 `dotnet --version` 与 `global.json` 不一致、卡图清单不完整或 SSH 主机指纹尚未由维护者核验，停止并先修复部署电脑环境，不要跳过验证。

发布前必须确认本批已有生产部署授权，并在 canonical 工作区执行以下检查：

```powershell
Set-Location D:\GPT\Legion12\app
git status --short --branch
git fetch --prune origin main
git rev-parse HEAD
git rev-parse origin/main
```

工作区必须干净，且两个提交号必须相同；否则停止。发布前运行 Release 门禁：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-l12-change.ps1 -Level Release
```

通过后，用标准入口发布：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1
```

当根分区容量已触发警戒时，不能继续把运行包和完整备份堆在 `/opt`。新机已核验独立、持久、可写的 `/www` 分区；本批使用固定受管制品根：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1 -Server root@154.201.80.91 -ServerArtifactRoot /www/legion12
```

SSH仍由现有可信配置校验；`ServerArtifactRoot`只接受`/opt`或`/www/legion12`，不可填任意目录。外置模式在上传大包前和服务器发布事务内检查独立挂载、持久挂载配置、权限、路径和容量：预备至少14GiB空闲，正式预算包含最大4GiB压缩备份、实际解包体积及8GiB余量。超限停止，不自动删除旧备份补空间。

外置模式只新增`incoming/staging/releases/runtime-backups/card-assets`受管目录，不移动`/opt/legion12-runtime`和已有程序、备份、测试资源。稳定入口仍是`/opt/legion12-test`，相同哈希卡图可继续引用原`/opt`缓存。备份先写`.partial`，完成压缩流校验和SHA256后原子发布；已有文件或软链目标一律拒绝覆盖。跨根的程序保留总量及资源引用按`SERVER-STORAGE-MAINTENANCE.md`人工核对，不能分别保留两套无限累积。

该脚本会再次同步远端、拒绝脏工作区或 `HEAD` 与 `origin/main` 不一致的版本，生成或复用经过校验的运行包，校验归档哈希后上传，并由服务器原子切换 release。它不会授权发布本身，也不应用于绕过本节门禁。只想验证传输与服务器前置条件而不切换版本时，可使用：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1 -DryRun
```

## 上线验收与记录

部署脚本完成不等于验收完成。维护者应确认：

- 公网主页和 `/health` 可访问，且 `/health` 返回的版本身份与本次部署提交一致；不要把文档中的历史提交号当作现场依据。
- `wss://legion-12.com/ws` 的 WebSocket 探针通过。
- 新服务器上的 `legion12-test.service` 为 `active/running`，且没有异常重启；服务名虽含 `test`，验收对象仍是正式服。
- 线上版本、卡图/静态资源和关键页面符合本批范围；失败时保留现场，不以手工覆盖目录修复。
- 发布成功后写入面向玩家的更新日志，并在任务交接中分别记录开发提交、远端提交和已部署提交。关联 Bug 只有在具名回归测试和线上版本均已验证后才能关闭。

可以在部署电脑执行以下只读核验：

```powershell
$expectedCommit = (git rev-parse HEAD).Trim()
$health = Invoke-RestMethod https://legion-12.com/health
if ($health.status -ne 'ok' -or $health.serverVersion -ne $expectedCommit) {
    throw "线上健康状态或版本与本次提交不一致"
}

node .\scripts\ws-smoke.mjs wss://legion-12.com/ws

ssh -o HostKeyAlias=154.201.80.91 root@legion-12.com `
    "systemctl show legion12-test.service -p ActiveState -p SubState -p UnitFileState -p NRestarts -p ExecMainStatus --no-pager; readlink -f /opt/legion12-test"
```

成功标准是 health 为 `ok` 且版本等于 `$expectedCommit`、WebSocket 输出 `"ok":true` 和协议版本 1、服务为 `active/running/enabled` 且主进程退出码 0。`NRestarts` 必须结合发布前基线判断，不能只因数值非零就手工清零或删除日志。SSH 若提示主机指纹不匹配，立即停止；不要自动接受新指纹。

## 回滚与必须停止的情况

管理员维护期沙盒与发布切换分开：发布脚本建立 `runtime/.maintenance-sandbox-drain`，验证成功后仅删除该临时围栏，失败保留。围栏不改运营版本、维护日期或广播；未经本批明确授权，不调用 `server/start` 或 `maintenance/end`。若用户要求保留维护，发布后必须核验原维护配置及入口阻断仍在。

回滚必须先证明程序与运行数据兼容。切换前产生的运行数据快照只与对应的旧 release 配对；新程序尚未启动时可恢复原程序并核验，原数据没有必要被重复覆盖。新程序一旦尝试启动，就可能迁移或产生新写入：常规脚本停止并保留现场，不自动恢复旧快照。只有证明当前数据仍兼容旧程序时，才可以在授权下只回滚程序；否则先对账或前滚修复，不得覆盖升级后的数据库、账本或媒体状态来换取表面成功。

卡效、Prompt或重放状态发生变化时，旧运行局不一定能跨版本重放。用户要求允许现有玩家打完时，必须先关闭新局入口，等待活动对局和未结待恢复记录均为零再重启；不能把“重启后尝试恢复”当作排空替代。成功开放服务器时只解除当前生效维护，保留未来预约与历史记录，并以有效策略和实际门禁核验为准。

出现以下任一情况时停止发布或停止继续操作，并交由维护者/更高权限运维处理：工作区不干净、提交不等于 `origin/main`、Git 非快进或冲突、主机指纹不匹配、验证或包哈希失败、健康检查/WebSocket/服务状态失败、发现新站已经产生写入却需要回滚、或怀疑旧机仍可写入。新站已有用户写入时，先冻结并导出新增事实，再决定前滚修复或受控回迁；不得直接恢复旧快照丢弃数据，更不得开启双写。

## 相关资料

- [迁服事实、正式/测试隔离与回滚边界](MIGRATION-READINESS-20260906.md)
- [正式发布脚本与服务器目录细节](DEPLOY-HK.md)
- [Windows 工作区、制品与清理规则](STORAGE-GOVERNANCE.md)
- [当前动态版本与任务交接](HANDOFF.md)

其中 `HANDOFF.md` 记录的是动态状态，发布时必须现场重新核验；不要复制其中容易过期的提交号、时间或历史服务器信息。
