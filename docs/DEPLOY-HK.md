# Legion12 正式服快速部署

> **2026-10-14 07:48实际终态**：两服应用e2cda3e5074a87558a2c695d08185ab2804ed24c；正式PID88174/重启0，独立HTTP/WS/DLL/静态/玩家日志通过，即时维护103→104结束，entryBlocked=false、v1 Open/围栏无。唯一汇总C盘artifacts/formal-release-20261014/release-session-summary.json引用原始发布/备份proof/独立验收/维护回执，不重复发布/解维护；下方cda/c4当前描述是历史。

本次共因并入四阶段、不另增审批：邀请按产品10分钟有效期而非过期原始总数；原生输入显式UTF-8/LF，中文配置/时间保留服务端原始表示，以完整快照SHA承接CAS，不跨Windows重序列化比字节；有界等待就绪，重复来源合并为真实屏障内唯一快照，不重复全堆扫描。最新备份proof/控制/独立核验不减少，不伪造许可，本次三旧房退休不转永久停止豁免。


> **2026-10-14当前**：本批仅测试服cda118d5已标准发布、独立核验并通过2/4/8轻量压力；正式仍c4f7499/PID30347/maintenance=false，未改正式业务或维护。单一回执C盘`artifacts/read-isolation-testrun-pressure-20261013/final-acceptance.json`（SHA780A621B），当前A2及总表已更新；本轮不触发正式发布或线上Bug关闭。以下10-13发布及旧状态仅历史，纯记录同步不重建/部署。
>
> **2026-10-13历史收口**：当时正式与测试应用均为`c4f7499ae0a5a50e45b37f0e075e3b60147da166`；正式即时维护CAS101→102已结束，独立开服及未开局房间入口验收通过。该次现场回执为`D:\GPT\Legion12\artifacts\deploy-authorized-20261013\deploy-closure.json`；其“本次未找到旧A2”只描述当时工作目录，不代表当前状态索引缺失。
>
> 本轮用户明确批准完整保留13条历史记录和7条具名旧孤儿日志、关闭1个未开局等待房间。仅本次固定旧0e49→目标c4f7499、包SHA735f0019、操作50fedc33d5f549c28bebcf780c43208f采用精确历史保留停止例外；没有消费或伪造v1 StopPermit，没有永久放宽标准门禁。最新快照SHA9a366cc0及两数据库/WAL原始证明通过，历史指纹不变，没有旧库回写。标准入口在监听前立即调用重启观察，失败现场保留后只恢复同一目标程序；实际应用和原消费者确认新进程/同owner/递增epoch，才精确取消本次围栏。旧10月6日发布记录完整另存，不能用陈旧deployment-info推断活跃版本。后续发布仍按当前标准工具及当批授权，不复用本次单次执行器。

## 部署标准：四步完成

2026-10-13用户要求合并重复步骤、停止琐碎审批。本节是唯一执行标准；其他指南引用本节，下方日期记录只供查历史，不再形成第二套流程。应用版本、文档提交和实际发布回执分开记录；更新文档绝不触发部署。

### 整批授权，不逐项再问

用户已明确授权本批完整部署时，正常的准备、受管工具修正、校验、上传、切换、经现场证实安全的有限重试和同候选／最新数据恢复，均在原授权内推进；用户同时要求成功后解除维护的，解除当前维护也一并执行。不得因参数、工具超时、回执文件名、内部阶段或同类历史记录数量变化重新索要授权。工具每次执行仍有界并留恢复余量，但工具预算不是人工授权到期时间；用户真正指定的发布／开服截止必须遵守。

只在实际超出原范围时询问：例如服务器／身份信任边界改变，未授权的删除或覆盖玩家数据、重装、扩权、采购，以及取证后仍无法解释且影响数据安全的新风险。先汇总完整事实和选项，一次提出必要决定；不把正常技术选择转交用户。既有明确批准的实例／指纹／原密钥绑定不反复确认，不将例外伪称为已完成独立身份核验。

2026-10-13用户针对部署资源重复计量修正明确“永久批准部署过程中的这个权限要求，不要重复询问”：已存在且大小／SHA完全相同的资源不再次计作新增，缺失目标仍逐路径计逻辑字节；冲突、链接、路径逃逸、四项预算上限及包与提交绑定全部保持。此类同边界计算修正由Main实现、补冷热／坏内容回归并验包后推进，不重复请求同类授权；不能把它扩大为预算上限增加、删除保护或任意门禁绕过。

### 四个阶段

| 阶段 | 合并完成的工作 | 完成依据 |
| --- | --- | --- |
| 1. 准备（维护切换前） | 冻结已授权应用候选，核Git及完整Release／同包证据；一次汇总SSH身份、两服版本、容量／权限、全模式活动与待处理元数据、历史分类和已有回执。提前验证真实启动命令、可执行位、POSIX路径、配置输入是否被消费、重入／失败恢复及解除维护流程；问题先修正并做相称专项。 | 一份准备结果：可执行，或一次列清剩余阻碍；不得以模拟测试或源码审查代替整条工具链预演。 |
| 2. 切换 | 只有用户选了两服时才先测试、再正式；已成功的环境不重复部署。保持封新局，在部署锁内对易变化的版本／锁／当前对局及待恢复、待结算、待命令、lease做最后核对；使用真实排空许可，正常停服，验证最新DB／非空WAL／镜像与备份原始proof，再原子切换已验收包。 | 当前候选和最新数据保护均通过，服务启动；失败保留现场，不盲目再发切换。 |
| 3. 独立核验 | 一次验收会话合并本机／公网HTTP、服务／PID、WS、提交／静态资源／玩家更新日志、控制面／围栏及当批关键功能。启动就绪使用有界等待，提交错误、鉴权或数据错误立即失败，不用固定零等待制造失败。 | 实际线上证据与已验收包一致；发布工具返回成功不是独立核验。 |
| 4. 收口 | 已授权成功开服时立即结束当前维护，不等无必要的约定最晚时刻；持锁CAS保留未来计划及其他配置，一并核入口／围栏／版本，撤销本次专用会话。保存一个发布汇总，引用原始日志／备份proof／独立验收，更新唯一状态索引；只关闭满足原场景、具名回归、线上版本和终态读回条件的Bug。 | 已上线／已开放／保留维护或失败状态明确，不把记录提交当成新应用版本。 |

### 哪些复用，哪些必须现场重核

- **复用**：提交、源码、包和验证器没有变化且哈希绑定仍有效的完整门禁、同包专项、包预检及工具审查。只重验修改或实际失败影响的部分；纯文档同步不重跑应用Release、不重新构建或部署。测试服已验收同包时，不再为正式部署重复测试服切换。
- **必要的边界核对保留**：上传前的本地包与上传后的远端字节校验、切换前的易变现场状态、停服后的最新数据库／备份证明、启动后的独立线上核验，目的与时点不同，不能相互替代。它们由上述四阶段内部完成，不另拆手工清单或审批。
- **先诊断，再重试**：确定尚未停服／切换时，在原授权内修正常规准备问题并继续；SSH返回不确定、已有partial或新服务曾启动时，先查现场和最新数据，不能自动重复事务。新服务启动后的恢复保护最新数据，不回写旧DB、不自动撤销F2。重大新风险才集中询问。
- **历史不是当前活动**：在线人数、WS连接、未开局大厅房间、时区或维护时长不作为停服阻碍；真正运行中对局及未完成恢复／结算／命令仍阻止切换。先完整检查schema、引用和历史来源；已确认无当前业务关联且原样保留的同类历史事实，在现有保全授权内更新精确指纹，不按每新增几条重新审批。未知或损坏事实不可悄悄忽略，也不得删除历史、伪造v1许可或永久放宽标准门禁；工具不兼容时先做受控兼容验收。
- **一个汇总，不层层造文件**：直接引用标准工具已有日志和原始proof；失败记录不可覆盖，重试留原记录并新增尝试，但不为每次GET、哈希读回或审查创建一套新的批准任务。HANDOFF／台账／候选总表简短引用唯一状态与发布回执，不各写互相冲突的“当前候选”。协作者已发布时，接入其回执和记录，不为完成Main旧计划再次发布或改变维护。

### 固定入口与历史授权绑定

正式使用 `ops/windows/deploy-l12.ps1`，服务 `legion12-test.service`／8083／`/opt/legion12-runtime`；测试使用 `ops/windows/deploy-l12-testrun.ps1`，服务 `legion12-testrun.service`／8084／独立runtime／`/testrun/`。`-ValidateArtifactOnly`为本地验包，`-DryRun`可能上传；不能以名称test误认环境。目录参数、哈希／归档／链接拒绝和容量保护保持，具体命令见下文及[测试服参数](DEPLOY-TESTRUN.md)。不在服务器拉源码或构建，不使用旧日期单次执行器；既有工具不符合本标准的，在原任务授权范围内先修正验收，本文不冒称脚本已经全部改好。

2026-10-13已批准的访问绑定为服务商85751／154.201.80.91／ser550468033481、隔离指纹 `SHA256:RUD26iAbAoApLNZw2Nl9b4hvx0yolbslbfZZOAGrj5o` 和原公钥 `SHA256:ZWYKCWbtujvI4yVUGl610IZOTcXKfo8D+aT2b4bO9v0`。原authorized_keys完整备份后只恢复原公钥，公钥登录实际通过；用户接受当次控制台未独立核验的残余风险。不改全局known_hosts、不新建密钥或改密码，不输出凭据，不自动扩展到变化后的身份。见D盘 `deploy-traffic-20261013/access-restoration-closeout-r8.json`。其他具名历史例外只保留为该次记录，不复制成永久豁免。

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

该入口在四阶段内完成Git／已验包复用、传输和归档校验、目录／账号权限、最新快照、原子切换、健康及失败保护；这些内部检查不是第二份人工执行清单。服务器不重复完整测试，同哈希卡图不重复上传。默认仅统计快照，不自动删除；首次数据迁移、受管清理或回滚仍需在实际授权和兼容性边界内执行。

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
