# Legion12 公网隔离验收站

测试服固定入口为 `https://legion-12.com/testrun/`。它复用正式域名和 HTTPS 证书，但不复用正式服务、端口、活动版本或运行数据；因此不需要新增 DNS、子域名或证书。

| 边界 | 测试服固定值 |
| --- | --- |
| 公网路径 | `https://legion-12.com/testrun/` |
| 后端 | `127.0.0.1:8084` |
| systemd | `legion12-testrun.service` |
| 活动版本 | `/opt/legion12-testrun` |
| 版本目录 | `/opt/legion12-testrun-releases` |
| 运行数据 | `/opt/legion12-testrun-runtime` |
| 部署状态/归档 | `/opt/legion12-testrun-deployment` |
| 环境文件 | `/etc/legion12-testrun.env` |
| Nginx 路径片段 | `/etc/nginx/snippets/legion12-testrun-path.conf` |

正式首页、正式 `/api`、正式 `/ws` 和正式 `/health` 仍按原配置工作；只有 `/testrun/`、`/testrun/api/`、`/testrun/ws` 与 `/testrun/health` 进入测试服务。测试服前端使用独立的 `dist-testrun`，构建资源路径固定带 `/testrun/` 前缀，避免刷新子路由或加载资源时落回正式站。

卡图等不可变资源继续使用正式站的内容寻址公开资源，不因测试服发版重复上传。发布包仍携带卡图版本清单用于一致性校验；服务器已有相同内容哈希时直接复用缓存，只有缺失的新哈希才上传。

## 数据与资源隔离

测试服务的 systemd 单元显式屏蔽正式活动目录和正式 runtime，只给测试 runtime 写权限；`CPUWeight`/`IOWeight` 为 10、`Nice=10`、`OOMScoreAdjust=750`，正式服务保持调度优先。内存使用 `MemoryHigh=768M`、`MemoryMax=896M`。

测试服固定使用 `L12_TESTRUN_MATCH_STORAGE=ephemeral`：账号、构筑和必要站点配置可以保留，但服务每次启动都会在严格核验公网基址与独立 runtime 后清空测试对局数据库及 WAL/SHM。测试对局、录像与分析事实不作为长期数据保存，也不会进入正式服统计。

测试服务单元同时固定启用`L12_TESTRUN_ACCEPTANCE_DATA=acceptance-v1`。只有上述临时对局隔离校验已通过时，服务才会为`Aimin`（不存在时回退到测试服`Admin`）补齐三份带`[验收]`前缀的牌库，其中两份公开牌库包含指南、对局建议、长名称和横卡场景。补齐操作按名称和内容哈希幂等，不覆盖验收人员已编辑的同名数据；删除后的样例只会在下次测试服务启动时恢复。该场景包不创建比赛结果、不写入正式统计，也无法在正式入口启用。

## 已有测试服务迁移到路径入口

现有测试服务已经存在时，不重跑 bootstrap。日常部署入口会先执行受管路径激活器：

1. 只接受正式站受管配置 `/etc/nginx/sites-available/legion12`，并核验其 enabled 链接和 `server_name legion-12.com`。
2. 安装 `legion12-testrun-path.nginx` 与更新后的 `legion12-testrun.service`。
3. 把测试环境中的 `L12_PUBLIC_BASE_URL` 从旧测试子域精确迁移为 `https://legion-12.com/testrun`；其他环境值不改。
4. 在正式 TLS server 的唯一前端 `location /` 之前插入测试路径片段。
5. 通过 `nginx -t` 后只 reload Nginx，不重启正式服务；任一步失败会恢复站点、服务、环境和片段备份。

对应文件：

- `ops/server/activate-l12-testrun-path.sh`
- `ops/server/legion12-testrun-path.nginx`
- `ops/server/legion12-testrun.service`

旧 `testrun.legion-12.com` 配置不是新流程依赖。路径入口验收成功后可另行清理旧站点链接，但不得把清理与首次路径上线合并，避免扩大回滚范围。

## 首次 bootstrap

只有服务器上完全不存在测试 service、env、runtime、release 时才使用 `ops/server/bootstrap-l12-testrun.sh`。它会拒绝覆盖既有测试环境。

在干净提交上生成 schema 3 Release 产物，将运行包和必要脚本上传到固定 incoming/`/tmp` 路径，再以 root 调用：

```text
bootstrap-l12-testrun.sh <commit> <releaseSha256> <releaseArchive> <assetHash> <assetSha256|-> <assetArchive|->
```

Bootstrap 创建独立管理员密钥和 root-only `0600` 环境文件；邮件、SMTP 与离线第二审批人引导固定关闭，`L12_PUBLIC_BASE_URL` 固定为 `https://legion-12.com/testrun`。管理员密钥不得写入仓库、日志或部署回执。

## 日常测试服发布

先在干净提交上运行发布验证，获得 `l12-release-<commit>.json`。随后使用唯一 Windows 入口：

```powershell
pwsh -NoProfile -File .\ops\windows\deploy-l12-testrun.ps1 `
  -ArtifactManifest D:\path\to\l12-release-<commit>.json `
  -KnownHostsFile C:\Users\<user>\.ssh\known_hosts `
  -IdentityFile C:\Users\<user>\.ssh\id_ed25519
```

允许的目标仅为 `root@legion-12.com` 或已核验源站 IP；实际连接始终锁定核验过的 IP、主机指纹与显式私钥。增加 `-ValidateArtifactOnly` 只验证本地目标和发布包；增加 `-DryRun` 会上传并执行服务器端暂存验证，但不切换活动版本。

部署入口会：

1. 拒绝脏工作区、与 HEAD 不同的 manifest、错误哈希、越界路径、链接、runtime、内嵌卡图或额外顶层成员。
2. 安装/核验路径入口与测试 service，不停止或重启正式服务。
3. 复用服务器已有内容寻址卡图缓存；没有变化时不上传卡图包。
4. 停止测试服务、快照测试 runtime、原子切换测试版本并启动。
5. 核验本机及公网提交身份、`/testrun/` 首页、`/testrun/health`、卡牌页和 `/testrun/ws`。
6. 新版本失败时只恢复上一测试版本；旧版本也无法验证时保持测试服务停止并写入阻断标记，绝不切换正式版本。

成功后只保留当前与上一测试程序、最新一份测试 runtime 快照、这两个版本实际引用的测试缓存，以及两天内 incoming 文件。正式服目录不在清理范围。

## 本地与上线验收

部署链修改后至少运行：

```powershell
pwsh -NoProfile -File .\scripts\test-l12-testrun-deploy-behavior.ps1
node .\opcgpro-vue\scripts\check-testrun-base-path.mjs
git diff --check
```

正式切换测试版本前必须使用干净提交完成 Release 验证。上线验收标准：

- `https://legion-12.com/testrun/` 返回测试服页面且静态资源从 `/testrun/assets/` 加载；
- `https://legion-12.com/testrun/health` 返回目标测试提交；
- `wss://legion-12.com/testrun/ws` 可建立测试连接；
- 正式 `/health` 与正式 `/ws` 保持原版本和可用状态；
- `legion12-testrun.service` 为 active，正式服务没有被重启；
- 测试服维护状态与正式服维护状态互不连带。

只有以上全部通过，才把“测试服地址可访问”标记为完成。
