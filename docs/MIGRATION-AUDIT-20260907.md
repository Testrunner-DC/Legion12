# OPS-20260907-259 新机内部只读核验

核验日期：2026-09-07（UTC+8）。用户要求暂停其他工作、优先核验迁服；本次未部署、重启、切换版本、迁移或清理数据库，也未更新后台 Bug 状态。

## 连接交接

- 正式节点：`38.76.208.25`，主机名 `ser550468033481`，入口 `legion-12.com`。
- 协作者通过既有可信连接提供的 ED25519 主机指纹，与本机实际 SSH 握手逐字匹配：`SHA256:d8Z8H6iOZF5/CZLsCq9XE7TYHFhIwQssZJfeXJl2PGM`。
- 经用户明确授权生成专用公钥，协作者追加授权成功；本机实际公钥登录已验证。密钥目录为仓库外 `D:\GPT\Legion12\secrets\ssh-l12-codex`，主机信任文件为 `D:\GPT\Legion12\secrets\legion12_known_hosts`。不得将私钥、凭据或上述目录内容纳入 Git；私钥 ACL 仅保留当前 Windows 用户和 SYSTEM。
- 后续连接显式指定专用 identity、专用 known_hosts、`IdentitiesOnly=yes`、`BatchMode=yes`、`StrictHostKeyChecking=yes`。不得再假定协作者另一台电脑的 `C:\Users\admin\.ssh` 在本机存在。
- 服务商 VNC 仍连接失败，未定位其内部原因；SSH 通道已可用，未为此重启或降低安全校验。

## 当前服务与资源

- 活动版本：`cb14e6b07d36fd8df1d1961b6a094d4f0b2577a9`，活动目录 `/opt/legion12-releases/cb14e6b07d36fd8df1d1961b6a094d4f0b2577a9-20260906T162358Z`。
- `legion12-test.service` 为正式服：active/running/enabled，`ExecMainStatus=0`、`NRestarts=0`，启动时间 `2026-09-06 16:28:10 UTC`；检查结束时仍为 active/running、重启0。
- 磁盘58 GiB、已用26 GiB、可用33 GiB（45%）；内存约3819 MiB、available约2992 MiB，swap使用0。均为检查时快照。
- Nginx 配置检查通过、服务 active；`certbot.timer` enabled/active。本次未实际触发证书续期。
- 本机 health 与公网 health 均为 ok，service、serverVersion、engineVersion 一致，324张可玩卡，maintenance=false。公网 `ws-smoke.mjs` 返回 ok=true、protocolVersion=1；探针未创建对局。
- journalctl 检查迁服后 warning及以上为0；当前进程启动后读取的6条应用日志未匹配异常类型或应用 warning/error 标记。仅表示本次日志覆盖范围，不证明所有历史均无异常。

## 数据库及账本

两库均通过 SQLite `mode=ro` / query_only、低 CPU/IO 优先级检查，未下载数据库。

- `platform.db`：完整性检查 ok，外键违规0；快照 SHA256、storage revision、business version 均与索引列一致。
- `matches.db`：约18.74 GB，完整性检查 ok（约41秒），外键违规0，WAL模式。
- `ranked_match_runtime` 实际分组为 completed 8；没有活动恢复记录。不要由 Outbox 数量推断还存在未查询到的 runtime 行。
- `ranked_settlement_outbox` 为 applied 9，pending 0，payload SHA256全匹配；runtime与Outbox均无父对局孤儿。
- 5条有明确胜负的Outbox分别对应两席结算、1条完整性审计及master记录；4条无胜负分别对应0条分数结算、1条审计且不记master。对席位、Won、主宰、结论、有效操作数与网络指纹等已执行字段比对，差异0。
- 全部48条平台结算无重复 `(MatchId,AccountId)`，每个结算对局恰两席；完整性审计无重复对局。
- 恢复隔离2条：缺少初始权威状态1条、排位命令序号不连续1条。均有对应无胜负结算Outbox，不重复计分，不擅自恢复、删除或修改。
- 123条legacy和1条friendly记录的结束时间为空，但开始/最后事件均早于迁服和当前服务启动；三类排位恢复/Outbox/隔离关联均为0。保留历史数据，不能据此声称当前有124场活动对局。

## 迁服证据与未决项

- 新机保存的最终冻结源/目标清单均为1155文件、18,640,122,491字节，聚合SHA256均为 `f52f4c8fea042a073f745a76783f206186cb065d14ef23346897ccdc98eca744`；当时两库完整性证据均为ok。本次核对既有证据，未重新读取旧机或声称重新计算两端全树哈希。
- BUG-20260906-b130b777、82e6141a、17333d1f 指向同一对局；已从正式平台库核实完整MatchId，并确认当前matches、events、quarantine均无该记录。模式信息不足，尚不能判定沙盒未录制、历史记录问题或其他原因；不能把它直接定性为迁移丢失，也不能标记已恢复。相关卡效工作继续暂停，报告保持原状态。
- 本次未发现必须立即对生产执行修复/重启/回滚的数据一致性或服务健康问题。此前本地部署脚本加固及卡效/UI改动均未因本核验而发布；其他批次继续暂停。
