# Legion12 公网隔离验收站

`testrun.legion-12.com` 是公开访问、无 Basic Auth 的验收环境。它与正式站只共用同一台主机，不共用服务、端口、活动版本或运行数据。卡图使用独立的测试缓存命名空间；当测试包与正式服引用同一个内容哈希时，可由 root 预置为只读硬链接，从而复用不可变数据块而不复制约 237 MB 文件。哈希不同则必须上传到独立测试缓存。

| 边界 | 验收站固定值 |
| --- | --- |
| 后端 | `127.0.0.1:8084` |
| systemd | `legion12-testrun.service` |
| 活动版本 | `/opt/legion12-testrun` |
| 版本目录 | `/opt/legion12-testrun-releases` |
| 运行数据 | `/opt/legion12-testrun-runtime` |
| 内容寻址卡图 | `/opt/legion12-testrun-static/card-assets` |
| 部署状态/归档 | `/opt/legion12-testrun-deployment` |
| 环境文件 | `/etc/legion12-testrun.env` |

测试服务的 systemd 单元显式屏蔽正式活动目录、正式 runtime 和正式卡图目录，只给验收 runtime 写权限；`CPUWeight`/`IOWeight` 为 10、`Nice=10`、`OOMScoreAdjust=750`，以调度权重而不是半核硬限额让默认权重的正式服务优先。内存由原来的 512 MiB 调整为 `MemoryHigh=768M`、`MemoryMax=896M`，避免常规加载过早 OOM，同时仍低于正式服务现有 1 GiB 上限。

## 首次 bootstrap（只执行一次）

首次建站与日常发版是两条不同入口。`ops/server/bootstrap-l12-testrun.sh` 会拒绝任何已存在的验收站 service、env、runtime、release 或 Nginx enabled 链接，因此不能拿它重跑或“修复”已上线测试站。

1. 由 DNS 管理者把 `testrun.legion-12.com` 指向当前源站；本仓库脚本不修改 DNS。
2. 在干净提交上完成 Release，取得 schema 3 的 `l12-release-<commit>.json`、运行包和卡图包。不要用当前脏树或手工 tar 包。
3. 通过已经人工核验的 `38.76.208.25` 主机指纹，把运行包上传为 `/opt/legion12-testrun-deployment/incoming/l12-testrun-release-<commit>.tar.gz`；卡图缓存缺失时上传为 `l12-testrun-card-assets-<assetHash>.tar.gz`。把以下仓库文件上传到同名 `/tmp` 路径：
   - `bootstrap-l12-testrun.sh`
   - `legion12-testrun.service`
   - `legion12-testrun-http.nginx`
   - `legion12-testrun.nginx`
   - `activate-l12-testrun-tls.sh`
   - `deploy-l12-testrun-release.sh`
   - `verify-l12-health.mjs`（远端名为 `verify-l12-testrun-health.mjs`）
4. 以 root 调用 bootstrap：`bootstrap-l12-testrun.sh <commit> <releaseSha256> <releaseArchive> <assetHash> <assetSha256|-> <assetArchive|->`。参数路径必须与上一步的固定路径完全一致。

Bootstrap 不读取、复制或解析任何正式环境文件。它创建随机 64 位十六进制管理员密码，并把唯一允许的键写入 root-only `0600` 环境文件；邮件功能固定关闭、所有 SMTP 值固定为空、`L12_PUBLIC_BASE_URL` 固定为 `https://testrun.legion-12.com`。`publish/runtime` 只能链接独立验收 runtime，内容寻址卡图只会使用 `/opt/legion12-testrun-static/card-assets` 下已经完整校验的目标。若用正式服同哈希卡图预置硬链接，必须保持 root 所有、目录 `0755`、文件 `0644`，并由 bootstrap 重新核对 manifest、文件长度和全树内容哈希；测试服务没有写卡图权限。归档内携带 runtime、卡图、链接、特殊文件、越界路径或额外顶层目录都会在切换前被拒绝。

HTTP bootstrap vhost 只开放 ACME challenge，其他请求统一 503；它不会用明文 HTTP 暴露登录或后台。取得证书后，以 root 执行 `/usr/local/sbin/activate-legion12-testrun-tls`。激活器只把验收站 enabled 链接从受管 HTTP 配置切至受管 TLS 配置；Nginx 语法、HTTPS health、首页和公网 WebSocket 任一失败都会恢复 ACME-only HTTP 配置。不要在 TLS 激活前让测试人员登录。

管理员密码只留在 `/etc/legion12-testrun.env`，不上传、不写入仓库或部署收据。需要首次登录时由有权读取该 root-only 文件的维护者线下取得并立即妥善保管。

## 日常测试服发布

日常只使用 Windows 专用入口，不再运行 bootstrap：

```powershell
pwsh -NoProfile -File .\ops\windows\deploy-l12-testrun.ps1 `
  -ArtifactManifest D:\path\to\l12-release-<commit>.json `
  -KnownHostsFile D:\secure\testrun_known_hosts `
  -IdentityFile D:\secure\testrun_ed25519
```

先增加 `-ValidateArtifactOnly` 可只检查目标和归档而不建立远程连接；增加 `-DryRun` 会上传到验收站 incoming 并执行服务器端只读/暂存验证，但不停止服务或切换版本。

入口只接受 `root@testrun.legion-12.com` 或 `root@38.76.208.25`，实际连接固定为 `38.76.208.25`，并强制 `StrictHostKeyChecking=yes`、该 IP 的 `HostKeyAlias` 及显式 known_hosts。它拒绝脏工作区、与 HEAD 不同的 manifest、错误 schema/文件名/SHA256/提交标记、链接或特殊成员、越界路径、runtime、内嵌卡图和额外顶层内容。

服务器日常入口 `/usr/local/sbin/deploy-legion12-testrun-release` 只读取并校验现有 TLS vhost，不安装、替换或 reload Nginx，不修改 env/systemd，不使用正式服务、正式端口或正式 runtime。它停止验收服务后快照验收 runtime，以原子链接切换版本，并同时核验本机/公网提交身份、首页、卡牌页和 WebSocket。新版本失败时只恢复上一测试版本并重启验收服务，不自动用快照覆盖可能已产生的新测试数据；若旧版本也无法验证，则保持验收服务停止并写入 `deployment-blocked.txt` 等待人工对账。成功后只保留当前与上一测试程序、最新 1 份测试 runtime 快照、被这两个程序实际引用的卡图哈希，并删除超过 2 天的测试 incoming 文件；正式服任何目录都不在该清理范围。

## 聚焦验证与激活后检查

本地修改部署链后运行：

```powershell
pwsh -NoProfile -File .\scripts\test-l12-testrun-deploy-behavior.ps1
git diff --check
```

测试覆盖任意主机拒绝、严格指纹参数、manifest/归档篡改和额外成员拒绝、邮件关闭与独立 env、只允许 8084/验收服务/验收 runtime/验收卡图缓存、HTTP 不暴露应用、TLS 不加 Basic Auth、日常不改 Nginx，以及新版本健康失败后旧测试版本仍为 active。此链不修改 DNS、不申请证书，也不在本地修改后自动部署；首次 DNS、上传/bootstrap、证书签发与 TLS 激活仍必须由主任务按授权逐步执行并留收据。
