# 十二军团简短交接

核验日期：2026-09-06。这是短任务的起点，版本必须重新核实，不能当作永远有效的配置。

## 当前发布批次

- `BATCH-20260906-249` 已完成、同步并部署。应用提交为 `a936b72924b467a8f9a916be3812bb35d3b804ae`；Batch/Release 均通过规则 2304/2304、指定 PlatformStore/ControlPlane 72/72、UI 契约 254、卡图 40 项/324 张、Vue/TypeScript/Vite 211 模块生产构建。
- 本批收口此前排队但未发布的全部问题：卡牌触发/费用/目标/公开信息与致死替代、好友房赛季天灾、进行中排位恢复与结算 Outbox、停机维护、跨设备设置与游戏音乐、胜负页手动返回、排行榜和对局 UI 完整度。
- 用户已明确授权：最终提交推送 `Testrunner-DC/Legion12` 的 `main`；通过 SSH/SCP 部署到 `legion-12.com`；允许排位数据库 Schema 迁移、持久化数据备份、远端版本切换、`legion12-test.service` 重启、HTTP/WebSocket/版本验证和成功后的更新日志；验证失败时允许回滚并重启。
- GitHub 推送、部署前备份、Schema 迁移、版本切换、服务重启、公网 HTTP/WebSocket/版本验证及玩家更新日志均已完成；本次没有触发回滚。

## 工作区与线上

- 唯一开发目录：`D:\GPT\Legion12\app`，分支 `codex/deploy-verify-20260821`；Git公共目录位于 `D:\GPT\Legion12\repo\.git`，不是可删除的副本。
- 当前功能开发提交（development commit）：`a936b72924b467a8f9a916be3812bb35d3b804ae`。
- GitHub `origin/main` 已包含应用提交 `a936b72924b467a8f9a916be3812bb35d3b804ae` 及本交接记录；下一任务仍须重新 `git fetch origin main` 核实最新值。
- 线上已部署提交（deployed commit）：`a936b72924b467a8f9a916be3812bb35d3b804ae`。
- 线上活动：`/opt/legion12-releases/a936b72924b467a8f9a916be3812bb35d3b804ae-20260905T210744Z`；上一版本：`/opt/legion12-releases/8ba286b85958b192380ffbe82f1404559a6e7c99-20260905T105254Z`。
- 运行数据恢复快照：`/opt/legion12-deployment/runtime-backups/runtime-before-a936b72924b4-20260905T210744Z.tar.gz`；由服务器管理，不纳入本地清理。
- 卡图资源版本：`910b3449455cd1505cc787eaf3412a4412c09d2e373c5450d85cd6c9994a5cc3`，原图及当前内容寻址图库保留。
- 本地预览 `http://127.0.0.1:5174/` 已按用户2026-09-05最新要求关闭；端口无监听，不要在未获新指令时自动启动。

## 已完成与约束

- FEATURE-20260905-241 已部署：连续文章编辑器、对齐/插图、正文图任意比例、资讯独立详情与返回一览。
- UI-20260905-240 已部署：轮播移除全屏渐变，仅文字轻阴影；视频作者无前缀，右侧显示日期。
- UI-20260905-243、BUG-20260905-244至BUG-20260906-248均已随 `a936b72` 发布；旧条目中“未部署”状态已失效。
- BUG-20260906-248 的排位 runtime 单调 checkpoint、Recorder 原子事务、pending/applied Outbox 对账、逐事件重放、停机重连窗及坏数据隔离已上线。迁移后 `ranked_match_runtime`、`ranked_settlement_outbox`、`ranked_recovery_quarantine` 均存在；部署核验时 Outbox 为 applied 1 / pending 0，旧对局因缺少初始权威状态隔离 1 条。
- 已部署应用证据：规则2304/2304、指定平台72/72、UI254、卡图40项/324张、Vite 211模块生产构建；公网 health 返回 `serverVersion=a936b72...`、`engineVersion=l12-engine/a936b72...`，WebSocket探针通过，service active/running、ExecMainStatus=0、NRestarts=0，更新日志已进入生产包。
- 长期授权：完成批次验证且无冲突后可提交推送main；生产部署须本批明确授权。`BATCH-20260906-249` 已取得本批授权。
- 不自行新增产品规则，不把完整卡效文本改成概括，不让插入任务覆盖此前需求。详情按ID查 `TASK-LEDGER.md`、`BUGFIX-REGISTRY.md`。

## 旧工作区的真实状态

`D:\GPT\Legion12\repo` 分支main为 `ebcf670c58c352695769ecc5a9c6f5c94f6ebc7b`。
相对8ba286b有1个独有提交/当前分支220个独有提交；`git cherry 8ba286b ebcf670` 输出减号，证明该独有提交的补丁存在等价版本，不能描述为“协作者最新未同步功能”。
但该工作区仍有未提交 `README.md`、`opcgpro-vue/src/l12/net.ts`，以及未跟踪 `docs/DEPLOYMENT.md`、`ops/`。其所有者/用途尚未确认。保留原样，不自动合并、重置、移除工作树或删除依赖。

## 本次维护与后续

- 修正清理策略：显式保护线上/回滚/待发布包；不删热构建；拒绝目录联接；24小时过期检查；活跃进程检测；保留删除清单和哈希。
- 已回收约2.17 GiB：12个旧部署目录、2个旧卡图归档和9月3日旧验证包。
- 删除证据：`D:\GPT\Legion12\artifacts\cleanup\cleanup-75fe27758b8d455587c620c761f947ca.json`。删除内容可依来源重新生成，日志不是二进制备份。
- 本次仅完善工具与交接；新产品需求从新批次开始。旧台账存在历史滞后记录，未逐条重新验收，不能仅因旧行写“未部署”就重发已上线功能。
- 会话历史保留；下一任务携带本文件和具体目标即可，无需全文粘贴历史消息。当前无用户要求的新任务创建或定时清理授权，不自动创建。
