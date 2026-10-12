# Legion12 正式服快速部署

## 当前可复用发布流程（2026-10-13）

本节优先于下方旧日期的现场记录。唯一已验收应用版本、Git同步、两服实际版本及访问状态，读取D盘A2 `artifacts/bug-closure-continuation-20261011/current-status.json` 和本轮不可变回执；文档更新不能触发部署。

1. 在D盘权威工作树完成精确候选的完整Release、最终同包专项、schema3包/DLL/卡图/双前端入口及累计玩家更新日志核验，fetch并安全推送后精确读回。新工具批与旧产品候选分别记录，不能借旧绿色宣称新提交通过。
2. 服务商管理入口为 `https://tisula.com/servicedetail?id=85751`。登录页面不是SSH连通或服务器身份的证据；先观察实例/IP，再通过可信控制台独立核验重装后的主机指纹。历史隔离只读指纹不可自动晋升为部署信任，禁止自动接受或替换全局known_hosts。
3. 如果重装丢失部署访问，仅幂等恢复原自有公钥并保留其他授权；不读取旧含密码控制台URL、不记录密码/私钥/token/cookies。严格指纹和单一自有Identity的只读探针通过后，再核当前服务、PID、版本、runtime、挂载/容量/权限、部署锁/围栏及已存在的回执。浏览器控制接口无法读取当前页面时不得猜坐标或抓取缓存凭据绕过。
4. 两环境发布时先走 `ops/windows/deploy-l12-testrun.ps1`，随后独立核验测试服，并确认正式版本/PID/数据没有受影响。测试具名能力标记、SHA/路径/链接等校验必须与标准生成器一致，不能剥离标记。`-ValidateArtifactOnly`仅本地验包；`-DryRun`可能上传，不可称纯本地。
5. 正式历史服务名 `legion12-test.service`、8083、`/opt/legion12-runtime`，测试为 `legion12-testrun.service`、8084、独立 `/opt/legion12-testrun-runtime` 与 `/testrun/`。这些是现场核对项；不能以名称test误认正式，也不能把重装前配置当现场事实。制品根以当前标准工具及实际挂载核验为准，不依据下方旧opt/www描述选择。
6. 正式仅运行当前 `ops/windows/deploy-l12.ps1`。保持本轮维护/封新局，核实际活动、待恢复、待结算、待命令与写入lease，使用真实v1 drain/seal/stop许可和远端部署锁。在线人数、WS连接、时区或维护时长不替代“无正在进行的对局”；不得清历史、伪造许可、使用旧日期/单次升级例外。
7. 停服后保全最新数据库、非空WAL、镜像和校验锚点；快照、字节/完整性校验的原始proof必须落盘。只切换已验收预构建程序，不在服务器拉源码或构建。新程序尝试启动后保护最新数据，禁止旧库覆盖或自动撤销F2；partial/不确定返回先只读核对，不盲目重发。
8. Main独立验证本机及公网HTTP、service/PID、匿名WS、精确提交/静态入口/玩家日志、drain控制与围栏；工具返回成功不替代独立核验。正式成功后才按当批授权，以版本/CAS校验结束当前即时或已生效维护计划，保留未来独立计划及其他配置，并重新验证新局入口；仅health维护false不够。
9. 专用管理员令牌只在内存及安全输入流转，finally分别撤销实际创建的会话。保存测试/正式/独立验收/维护解除回执并更新A2；关联Bug仍须原文、具名回归、实际部署版本、ETag/CAS及独立终态读回，不能因部署成功批量关闭。

当次授权仅涵盖已请求的发布与成功解除当前维护，不自动扩大到重装、VM重启/电源、网络/安全组、账号权限或采购。2026-10-13管理页面已由用户报告登录，但浏览器控制初始化失败、原部署公钥认证拒绝仍是实际访问阻碍；不得写成登录失败或部署已经成功。

> **2026-10-07 08:01开服实际核验**：预约维护08:00自然结束，08:00:54工具和08:01:26独立fresh读回正式`2da4344ee3d776cc8396fcf5a0466cc9da1270f4`、health ok、策略92 open、maintenance/entryBlocked/immediate均false；同PID、重启0、三发布围栏missing、匿名WS正常。没有追加发布、没有维护配置CAS，专用会话已撤销。实际D盘`maintenance-open-20261007/maintenance-open-receipt.json`及`independent-opening-readback.json`优先于下方待开服历史；一次回访已删除。未完本地方案继续，不能再使用今日截止前的追加部署授权。

> **2026-10-07实际读回优先于下方历史资料**：正式已部署并独立核验`2da4344ee3d776cc8396fcf5a0466cc9da1270f4`，测试独立运行`428fff25`；真实收口为D盘`release-20261007-approved/retry-2/release-closure.json`。当前标准Windows工具默认制品根`/www/legion12`，正式不可变release、已消费incoming和备份在该根下，稳定入口/活跃数据库仍`/opt/legion12-test`及`/opt/legion12-runtime`，不把目录名test误认测试服。下方09月“测试服尚未启用/当前根opt”仅历史，不作为当前配置。外部原子排空consumer正在本地实现，开关仍未启用，未知/历史未结束记录不豁免；未通过新批完整验收不可发布。今日新部署仅在完整牌库存储A/B/C和正式对局保障同时完成且07:30前有足够安全余量时授权；否则不追加发布，08:00仍开服。不得执行旧日期发布/维护入口，不进行测试服部署，不恢复旧数据库。

> **迁移后正式服提醒（2026-09）**：本文件中的发布路径当前服务于正式服 `legion-12.com`；`legion12-test.service` 只是历史名称，不能据此认定为测试服。伙伴提交与维护者正式发布的角色边界、Git 门禁、上线验收及停止条件，请先阅读[《伙伴提交与正式服发布指南》](CONTRIBUTION-AND-PRODUCTION-RELEASE.md)。独立测试服尚未启用，旧服务器不得重新启用或形成双写。

发布流程采用“开发电脑或 CI 完整验证一次、服务器只校验并切换预构建产物”的模型。服务器不再保存源码、`node_modules` 或执行第二轮完整测试；账号、牌库、Bug、官网内容和对局记录独立保存在共享运行目录。

## 目录模型

```text
/opt/legion12-test -> /opt/legion12-releases/<commit>-<time> # 稳定入口
/opt/legion12-releases/                                      # 当前不可变版本
/opt/legion12-runtime/                                      # 持久化运行数据
/opt/legion12-static/card-assets/<tree-hash>/                # 内容寻址卡图缓存
/opt/legion12-deployment/runtime-backups/                    # 切换前运行数据快照
/opt/legion12-deployment/incoming/                           # 待校验发布包
```

Nginx 和 systemd 继续访问 `/opt/legion12-test`，版本切换只原子替换该符号链接。首次使用新流程时，旧版 `publish/runtime` 会在服务停止后通过同文件系统移动到共享目录，不复制约 1GB 的数据库和平台数据。

## 当前正式服固定资料（2026-09-21 核验）

- 服务商实例：Tisula 服务编号 `85751`，实例 `server-85751`，主机名 `ser550468033481`。
- SSH：`root@154.201.80.91:22`；部署时仍以 `root@legion-12.com` 连接并固定 `HostKeyAlias=154.201.80.91`。
- 本机部署私钥：`C:\Users\neptu\.ssh\id_ed25519`；公钥指纹 `SHA256:ZWYKCWbtujvI4yVUGl610IZOTcXKfo8D+aT2b4bO9v0`。
- 已授权部署公钥：`ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFPzOIcDTQh/UWWdwQQGqsksGMHHDiz153ecQOtRxHhE neptu@hero-rush`。
- 服务器 ED25519 主机公钥：`ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIK7zDzJDfZJI1gyqTg8/QL7ALF8wYzXhvnenVp+Czbd2`；人工核验指纹 `SHA256:8GSTnRZ46ArgsRj+G5c1aiZI/0GuyEx0DJyipV5i1Lg`。
- 本机可信主机记录：`C:\Users\neptu\.ssh\known_hosts`，以 IP `154.201.80.91` 为别名保存。指纹变化时必须先从服务商控制台独立核验，禁止自动接受。
- systemd 服务：`legion12-test.service`（仅为历史名称，实际承载正式服）。稳定入口 `/opt/legion12-test`，运行数据 `/opt/legion12-runtime`，当前发布根目录 `/opt`。`/www/legion12` 仅保留历史制品和受保护备份，不在日常部署中自动迁移或删除。
- 健康接口 `https://legion-12.com/health`，WebSocket `wss://legion-12.com/ws`。部署完成必须同时核验本机和公网 HTTP、WebSocket、提交版本、服务重启次数与发布围栏。

仓库只记录公钥和已人工核验的指纹。服务商密码、SSH 私钥、登录令牌、临时控制台直连地址不得进入仓库或部署日志。若服务器因重装丢失部署公钥，只能从已登录的服务商控制台将上述公钥幂等加入 `/root/.ssh/authorized_keys`，并恢复 `.ssh` 为 `700`、文件为 `600`，随后先执行严格指纹 SSH 探针。

## 公网域名

- 主站为 `https://legion-12.com`，生产 WebSocket 为 `wss://legion-12.com/ws`；`https://www.legion-12.com/*` 以 308 保留路径和查询参数跳转主站。
- 旧迁移域名已停止提供静态站、API 和 WebSocket；生产配置、发布探针及运维脚本不得再依赖旧域名。
- 生产前端不显式设置 `VITE_WS_URL`，由浏览器按当前 HTTPS 主机选择同源 `wss://legion-12.com/ws`。发布探针和运维脚本以新主域为唯一权威目标。
- 登录令牌、离线牌库和界面偏好保存在浏览器 `localStorage`，不会跨域复制。旧域用户转到新主域后可能需要重新登录；服务端账号、牌库、对局和回放数据仍在共享运行目录，不受域名下线影响。禁止通过 URL、重定向参数或日志传递 bearer token。

## 首次准备

开发电脑需要 Git、PowerShell、Node.js、npm、tar、仓库 `global.json` 指定的 .NET SDK，以及服务器 SSH 权限。建议把构建缓存放在 D 盘：

```powershell
$env:TEMP = "D:\GPT\Legion12\artifacts\temp"
$env:TMP = $env:TEMP
$env:npm_config_cache = "D:\GPT\Legion12\artifacts\temp\npm-cache"
$env:L12_DEPLOY_CACHE = "D:\GPT\Legion12\artifacts\deploy"
```

SSH 公钥加入新服务器且已人工核验 `154.201.80.91` 的主机指纹后验证：

```powershell
ssh -o HostKeyAlias=154.201.80.91 root@legion-12.com "echo SSH连接成功"
```

不要复制其他电脑的 SSH 私钥，也不要用 `ssh-keyscan` 的未核验结果直接建立信任。部署脚本会自动读取仓库所有者的用户 SSH 配置；只要新服务器 IP 已存在于所选 `known_hosts`，主域就固定使用该可信 IP 作为 `HostKeyAlias`，不会读取可能残留的旧域名主机项。若可信 IP 条目不存在，脚本保持 `StrictHostKeyChecking=yes` 并失败关闭，不自动接受新指纹。

## 完整验证与构建

单独执行完整验证：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\verify-l12.ps1
```

它会执行 L12 规则测试、平台持久化测试、前端 UI 契约与生产构建，并生成 Linux 后端运行包。产物默认保存在 `D:\GPT\Legion12\artifacts\deploy\<commit>`；同一提交重试会校验并复用该产物。需要强制重测时使用 `-Force`。

卡图独立按 Git tree hash 打包。服务器已经存在相同卡图版本时，不会再次上传约 388MB 的卡图。

## 正式部署

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1
```

脚本默认制品根为当前正式服使用的 `/opt`。`/www/legion12` 仍是受支持的外置制品根，但只有部署前只读审计确认独立挂载、持久化、权限及至少 14 GiB 可用空间时才可显式选择；空间不足时停止，不通过删除受保护快照强行达标。2026-09-29 的 `e1e561ad` 发布已使用 `/opt`，`/www` 旧制品保留未删除。

部署脚本会：

1. 最多重试三次 GitHub 连接，并要求工作区干净、`HEAD` 与 `origin/main` 完全一致；
2. 复用当前提交已验证的发布包，不存在时自动调用完整验证脚本；
3. 只上传预构建运行包；卡图版本变化时才上传卡图包；
4. 服务器验证 SHA256、归档路径、提交标记、目录结构和真实运行账号权限；
5. 首次迁移共享运行数据；后续每次切换前为共享运行目录生成与待回滚程序配对的数据快照；
6. 将版本放入不可变 release 目录并原子切换稳定入口；
7. 检查主页、卡牌页、健康接口和公网 WebSocket；
8. 启动新服务前失败时恢复上一版本并核验；新服务一旦尝试启动，保留当前运行数据和失败现场，不自动覆盖数据库。只有证实数据兼容后才可按授权回滚程序；
9. 默认只统计运行数据快照，不自动删除；只有另行明确启用受管清理并满足成组校验、保留数量和恢复边界时才会清理。

隔离工作区会被自动识别；只要工作区干净且 `HEAD` 与最新 `origin/main` 完全一致，就使用同一条正式部署命令。旧参数 `-AllowVerifiedWorktree` 仅为兼容历史调用保留，已无需手动追加。

## 快速干运行

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1 `
  -ServerArtifactRoot "/opt" `
  -DryRun
```

干运行上传并验证预构建产物和权限，但不停止服务、不迁移数据、不切换版本。因为完整测试只在产物生成时执行，重复干运行通常只需要上传小型运行包并完成服务器校验。

## GitHub Actions

`.github/workflows/verify-release.yml` 会在 `main` 推送、Pull Request 和手动触发时执行同等测试，并生成 Linux 运行包 Artifact。下载后的 JSON 清单可传给部署脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1 `
  -ServerArtifactRoot "/opt" `
  -ArtifactManifest D:\path\l12-release-<commit>.json
```

CI Artifact 不重复包含卡图；若服务器没有对应缓存，部署电脑会从当前同提交仓库生成一次卡图包。

## 回滚与排查

部署元数据：

```text
/opt/legion12-deployment/deployment-info.txt
```

使用当前 `/opt` 制品根时，切换前运行数据快照保存在 `/opt/legion12-deployment/runtime-backups`；显式使用外置根时才保存在 `/www/legion12/runtime-backups`。快照用于取证和经核验的恢复，不表示可以覆盖新写入。新服务一旦尝试启动，就可能修改 schema 或产生新数据；此后脚本停止并保留现场，不自动恢复旧快照。必须先证明当前运行数据与旧程序兼容，才可以按本批授权只回滚程序并核验；不兼容时保持维护、保留新数据并报告阻断原因。详见《伙伴提交与正式服发布指南》。部署前应关闭新局入口，等待已有对局自然结束且无待恢复运行局，再执行切换。

发布脚本只切换程序并执行健康核验，不代表获得解除维护的授权。只有用户在当批任务中明确要求，才可另行结束维护；否则保持健康接口中的 `maintenance: true`。

查看状态：

```powershell
ssh -o HostKeyAlias=154.201.80.91 root@legion-12.com "systemctl status legion12-test.service --no-pager"
ssh -o HostKeyAlias=154.201.80.91 root@legion-12.com "journalctl -u legion12-test.service -n 200 --no-pager"
```

禁止直接覆盖 `/opt/legion12-test`、删除 `/opt/legion12-runtime`，或在服务器源码目录执行 `git pull`。旧 release 暂不自动删除，以便人工审计和回滚。

域名下线不切换应用 release，也不移动运行数据。2026-08-28 的新域上线前备份位于 `/root/legion12-nginx-backups/20260828T113923Z`；新主域与 `www` 分别由 `/etc/nginx/sites-enabled/legion12-new-domain`、`legion12-www-domain` 启用。旧域下线前必须另建带时间戳的 Nginx 备份，先将旧入口收敛为到新域的精确 308，再删除旧 DNS 并停用旧虚拟主机及证书续期。任何配置变更都必须先执行 `nginx -t`，成功后才 `systemctl reload nginx`；不得停止后端或修改 `/opt/legion12-runtime`。DNS 回滚由 Cloudflare 控制台单独执行，仓库脚本不持有其凭据。

## Windows 构建缓存位置

Windows 完整验证和部署入口会自动初始化本次进程使用的构建缓存。开发电脑存在统一项目目录时，默认使用：

~~~text
D:\GPT\Legion12\cache\primary\temp
D:\GPT\Legion12\cache\primary\dotnet-home
D:\GPT\Legion12\cache\primary\nuget
D:\GPT\Legion12\cache\primary\npm
D:\GPT\Legion12\cache\primary\corepack
D:\GPT\Legion12\artifacts\deploy
~~~

这只影响当前验证/部署进程及其子进程，不会修改 Windows 全局 TEMP，也不会删除 C 盘旧缓存。需要临时改用其他磁盘时可传入：

~~~powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\verify-l12.ps1 -CacheRoot E:\L12-cache
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1 -CacheRoot E:\L12-cache
~~~

也可以预先设置进程环境变量 L12_WORK_CACHE；显式 -CacheRoot 的优先级最高。发布产物目录仍由 L12_DEPLOY_CACHE 或 -OutputDirectory 独立控制。
