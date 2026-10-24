# Legion12 公网隔离验收站

部署授权、四阶段、证据复用及失败处理统一遵循[部署标准](DEPLOY-HK.md#部署标准四步完成)。本文只定义测试环境参数和隔离验收，不再增加独立审批／重复流程；同包已部署验收时，正式发布不重复切换测试服。纯文档同步不构建或部署。

测试服固定入口为 `https://legion-12.com/testrun/`。它复用正式域名和 HTTPS 证书，但不复用正式服务、端口、活动版本或运行数据；因此不需要新增 DNS、子域名或证书。

2026-10-25当前测试版本为`8e094524f2a93bc3f3a6df8562b61679cc017eb6`（产品源ad6cdebe，另补两条已验等待样式的账本路径），10月24日17:54:11Z发布、17:54:27Z独立读回通过，PID720731。人类本次明确要求完整包，开场演出／对局动效／教学／聊天均已包含；8415规则、2462平台及完整前端双构建新Release全过，359磁盘／12公网静态与四HTTP／四WS及最新快照独验。正式仍`3a376f17e5a6f9431303b35775977fabd2083b6a`、PID698071、维护false，不重启或重发。唯一回执`D:/GPT/Legion12/artifacts/test-release-20261025-ad6cdebe/release-session-summary.json`；旧24a为历史，上线不冒新30＋10、真机或人类最终开场视觉通过，后续纯记录提交不重建此包。

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

## 共享卡图与图鉴资料（2026-10-23用户要求）

卡图、图鉴及资料后台使用一份正式资料源，不合并两服平台数据库。固定测试build的两个精确公开读取（`/api/alternate-arts`、`/api/gallery/presentation`）直达同站正式根路径，不带测试Authorization/Cookie、不跟重定向；其他测试API仍走8084，不扩大为整个API代理。路由行为与前端同包回退，不在Nginx遗留新规则影响回退后的旧客户端。后台资料编辑打开正式资料管理入口，使用正式登录及原权限；入口明确说明编辑同时影响两服。测试账号、权益／派发、对局、回放、统计和审计继续独立。

测试服务标记`L12_TESTRUN_SHARED_GALLERY=production-public-v1`只在严格隔离测试profile下启用只读公开定义桥：固定loopback正式公开API，不读正式runtime文件／DB，不向正式API发送认证或写入。定义仅存在有界原子内存快照，不在平台锁或对局读取中联网；测试grant、我的异画和构筑／开局校验消费相同公开定义及正式媒体路径，来源不可用时不得回落本地陈旧定义。旧测试资料原样保留但不再作为共享目录权威；测试权益记录仍本地。

读取有2秒截止、15秒刷新、45秒单调时钟鲜度上限及4MiB/4096条边界；失败撤下当前定义，不靠测试旧副本补齐。非空已存异画选择在来源不可用时先拒绝保存，不清除原偏好；本地同身份旧奖励规则仍可停用。一次排位结算的双方、一次整季归档的全部玩家和主宰冠军使用同一不可变定义代，不能因刷新中途换代形成部分奖励；来源失败沿原事务/outbox恢复，不新增第二套奖励队列。

此段是本批配置合同，未部署前不能称测试服已切换共享资料。发布验收应验证两条公共读、正式新增定义在测试权益／选用中的闭环、停用与来源失败，以及本地／正式账号和写入隔离；不能只看图鉴页面或health宣称完成。

## 数据与资源隔离

2026-10-18用户明确批准的当前测试服磁盘预算为：程序区1.5GiB、运行数据及备份／失败证据区512MiB、临时峰值1GiB、合计峰值3GiB。按文件路径逻辑字节核算，共享硬链接水合路径仍重复计数，双前缀兼容web缓存及发布前runtime备份仍计入；不能改成物理块数掩盖增长。任一维度超过上限即在停服前拒绝，等于上限允许；不通过清理未知文件或业务数据凑预算，保留最新数据库、必要备份及回滚，正式服8GiB空间余量不变。四项是磁盘上限，不增加CPU、systemd内存额度或购买资源。旧512／128MiB以及10月13日1GiB／256MiB／512MiB／1.5GiB拒绝记录保留，不改成成功。当前同包b4cbeae8实际测试部署与独立核验见D盘test-recorder-v3-20261018/deploy-r2/result.json；正式4325/PID296786及维护保持不动，诊断仍关闭，不把上线成功当性能达标。

测试服务的 systemd 单元显式屏蔽正式活动目录和正式 runtime，只给测试 runtime 写权限；`CPUWeight`/`IOWeight` 为 10、`Nice=10`、`OOMScoreAdjust=750`，正式服务保持调度优先。内存使用 `MemoryHigh=768M`、`MemoryMax=896M`。

测试服固定使用 `L12_TESTRUN_MATCH_STORAGE=ephemeral`：账号、构筑和必要站点配置可以保留，但服务每次启动都会在严格核验公网基址与独立 runtime 后清空测试对局数据库及 WAL/SHM。测试对局、录像与分析事实不作为长期数据保存，也不会进入正式服统计。

测试服务单元同时固定启用`L12_TESTRUN_ACCEPTANCE_DATA=acceptance-v2`。只有上述临时对局隔离校验已通过时，服务才会为`Aimin`（不存在时回退到测试服`Admin`）幂等补齐三份带`[验收]`前缀的牌库，其中两份公开牌库包含指南、对局建议、长名称和横卡场景；同时补齐三派系、多段位的验收玩家、主宰排行与对阵、历史荣誉及 Aimin 的整体/主宰战绩。排位验收事实只存在于测试进程的服务端投影，不写入`matches.db`，因此不会出现在玩家回放列表，也不会被当作真实录像或长期分析资料。账号、牌库和历史荣誉按稳定标识幂等补齐；正式入口无法通过该开关，生产服务单元也不配置它。

## F2 测试对象写

教学建设期间仅测试服务模板明确设置`L12_TUTORIAL_ENABLED=true`，环境文件中显式false仍可关闭；应用默认关闭，正式服务不配置。公共单人入口改为教学，管理员沙盒从后台进入，必须实际完成课程操作与权限／恢复验收。三关阶段成果不等于整套教学已验收，整套完成前不得发布正式服；本段只记录配置合同，不证明已经部署。

聊天默认关闭；本次用户要求收口后部署测试服，测试 service 模板显式 `L12_CHAT_ENABLED=true`，环境文件中的显式 false 可关闭。仍复用原 `/testrun/ws`，不另开连接；正式 service 与正式环境不新增此项。部署后须实际读回 session 的 `chatV1` 能力并验证双方消息、历史和拒收，不能用 health 代替功能验收。此段为本批配置合同，不代表已经部署。

2026-10-02 起，已获授权的测试service显式配置 `L12_PRIVATE_DECK_OBJECT_PERSISTENCE=true`；测试环境文件显式false可覆盖并在同版本重启后回到全量路径。正常正式service未配置，默认关闭。开关只在启动时捕获，变更需重启，不是运营配置热更新。受保护只读状态 `GET /api/admin/storage/private-deck-persistence` 必须读回 `configuredEnabled=true`、`effectiveMode=object`、`writable=true`；健康接口不披露内部模式。

启用与回退不得用旧库覆盖最新数据。Main通过受控测试牌库CRUD／陈旧标签页冲突和重新登录读回验证；生产副本预演与生产启用另行批准。详情见 `F2-CONTROLLED-ENABLE-20261002.md`。

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

以下为10月13日历史计算修正，不是当前容量值；当前四上限见上节。真实e619包当时在停服前因newWeb增长重复计量被拒绝，两PID/版本未变；已证明128779470字节为已有同内容资源，实际缺3400562字节，旧投影还误含25410418字节非assets水合。用户已长期批准仅修正该计算：整release和全部hydrate继续按逻辑路径计量，新cache仅计实际两assets前缀的缺路径，已有逐大小/SHA等价才0增长，坏内容/链接/逃逸继续拒绝。该授权不自行增加预算或改变HEAD/包绑定；本次四上限调整另有10月18日明确批准。

先在干净提交上运行发布验证，获得 `l12-release-<commit>.json`。随后使用唯一 Windows 入口：

发布验证须显式将活动前端/打包输出放在已过物理路径检查的D cache，例如向`ops/windows/verify-l12.ps1`传`-OutputDirectory D:/GPT/Legion12/cache/primary/release/current-test-20261024`；紧凑会话回执仍放D artifacts。不要沿用把可执行QA暂存到`artifacts`的旧调用默认值：原代码读取保护会正确拒绝；改输出位置，不放宽保护或重做同源已过后端。本轮资格为完整后端前缀＋原canonical前端/包后缀，须如实分段记录，不称新单轮完整运行。

```powershell
pwsh -NoProfile -File .\ops\windows\deploy-l12-testrun.ps1 `
  -ArtifactManifest D:\path\to\l12-release-<commit>.json `
  -KnownHostsFile D:\GPT\Legion12\artifacts\security-reinstall-prep-20261009\ssh-reinstall-pinned-20261011.known_hosts `
  -IdentityFile C:\Users\neptu\.ssh\id_ed25519
```

允许的目标仅为 `root@legion-12.com` 或已核验源站 IP；实际连接始终锁定核验过的 IP、主机指纹与显式私钥。增加 `-ValidateArtifactOnly` 只验证本地目标和发布包；增加 `-DryRun` 会上传并执行服务器端暂存验证，但不切换活动版本。

上述独立主机记录绑定的是用户已批准的重装后RUD指纹；其授权与独立核验范围见DEPLOY-HK。当前全局known_hosts仍可能含重装前旧指纹，必须显式复用独立记录，不回退全局、不关闭严格校验、不因此重复询问同一访问授权。2026-10-18教学首片实际测试部署已沿此入口成功，正式service为legion12-test而非legion12；不要因名称误判环境。

部署入口会：

1. 拒绝脏工作区、与 HEAD 不同的 manifest、错误哈希、越界路径、链接、runtime、内嵌卡图或额外顶层成员。
2. 安装/核验路径入口与测试 service，不停止或重启正式服务。
3. 复用服务器已有内容寻址卡图缓存；没有变化时不上传卡图包。
4. 停止测试服务、快照测试 runtime、原子切换测试版本并启动。
5. 核验本机及公网提交身份、`/testrun/` 首页、`/testrun/health`、卡牌页和 `/testrun/ws`。
6. 新版本失败时只恢复上一测试版本；旧版本也无法验证时保持测试服务停止并写入阻断标记，绝不切换正式版本。

程序和缓存按既有固定保留规则收敛，PINNED、失败/诊断绑定及未确认资料不删；测试runtime备份按2026-10-24已批准的最近两组、每轮最多一旧组规则处理，详见`SERVER-STORAGE-MAINTENANCE.md`，不是只留一份。正式服目录不在清理范围。确认停服前容量失败后，核对精确可再生旧测试程序，在原部署flock保护下清理并重进同一已上传且SHA确认的标准完整入口，不重新上传或增预算。若外层持续持锁，子入口`L12_TESTRUN_DEPLOY_LOCKED=1`仅在原真实锁整个子进程期间仍持有时使用，不可先解锁再借变量跳锁。

本轮程序保留消费者因历史完整正常诊断目录仍保守拒绝自动清理；六份证据全部保留，未全局豁免。一次精确旧TEST57程序清理及随后标准部署已完成；分类工具修正另列，不把收尾清理跳过写成部署失败，也不反复切包。

## 本地与上线验收

2026-10-14实际cda118d5已用该唯一入口成功发布，部署日志C盘read-isolation-testrun-pressure-20261013/test-deployment-cda-r1.log；完整新Release/独立上线及2/4/8有界压力统一引用同目录final-acceptance.json，正式c4原PID未变。纯收口记录不重建/重部署这个包。该次既有资源新增计量修正可继续复用长期授权；没有新范围/数据风险，不逐项重问。压测启动参数必须服从工具自身下限（HTTP至少2000ms），机器可读ISO时间窗口取原字符串，不经PowerShell自动DateTime转本地显示再解析；本轮失败及修正已留痕，不把空指标当通过。

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
