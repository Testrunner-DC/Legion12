# 第 0 赛季派系最终数值内部证据

> 内部审计材料。不得把备份路径、内部赛季键、存储修订、哈希、置信度或“历史重建”等系统术语渲染到玩家界面；玩家端只展示自然赛季名称、派系名称和结算数值。本记录不包含玩家账号、用户名、比赛 ID 或其他个人明细。

## 结论

第 0 赛季按结算时既有权威口径（已完成定级，且账号存在、未禁用、未注销）的派系最终七曜值为：

| 派系 | 最终七曜值 | 纳入结算的已定级人数 |
| --- | ---: | ---: |
| 混沌 | 2,279,096 | 30 |
| 命运 | 2,946,802 | 42 |
| 秩序 | 2,407,333 | 38 |

这些数值可作为历史事实冻结。旧赛季历史展示不得再从当前账号状态或当前赛季 profile 动态重算。

## 权威证据

### 停服快照

- 备份：`/opt/legion12-deployment/runtime-backups/runtime-before-c36a0f637703-20260930T222919Z.tar.gz`
- 大小：`874,871,656` bytes
- 备份 SHA256（实际文件与 `.sha256` sidecar 一致）：`5c616c1c732cb41772d3f91d97ef3a0b72c381d8b07d01d76b9a442f3503edfb`
- 完整性：`gzip -t` 成功，结果为 `ARCHIVE_STREAM_OK`
- 备份中的 `./platform.db`：`53,362,688` bytes
- 备份中的 `./platform.db-wal`：`0` bytes
- 流式提取后的 `platform.db` SHA256：`09b4932c15cedd1436ab2a484c730d1f93972eb0293930c2fc5d0b7dfa7b7cec`
- SQLite：`PRAGMA quick_check` 返回 `ok`
- `platform_state.storage_revision`：`23077`
- `platform_state.business_version`：`21748`
- `platform_state.updated_utc`：`2026-09-30T21:42:50.9817995Z`（北京时间 2026-10-01 05:42:50）
- `platform_state.snapshot_sha256`：`4bb945d62fc7086a6af90a3b40648428c818cdbf3f7ed7ee9eed6a0e5fa43a5a`

正式发布脚本在停止服务并确认不再 active 后才创建 runtime 快照；快照覆盖整个 runtime，随后校验压缩流和 SHA256。因此该 `platform.db` 与空 WAL 构成一致的停服事实，不是运行中只复制主数据库文件所得的不完整视图。

### 最终归档对照

正式库中的第 0 赛季 `FinalizedSeasonAwards` history 均冻结于：

- UTC：`2026-09-30T22:32:16.7716066Z`
- Asia/Shanghai：`2026-10-01 06:32:16.7716066 +08:00`

备份中全部已定级 profile 与最终 history 的已定级人数和未扣账号状态七曜合计逐派一致：

| 派系 | 备份已定级人数 | 最终 history 已定级人数 | 备份全部已定级七曜合计 | 最终 history 已定级七曜合计 |
| --- | ---: | ---: | ---: | ---: |
| 混沌 | 31 | 31 | 2,527,436 | 2,527,436 |
| 命运 | 42 | 42 | 2,946,802 | 2,946,802 |
| 秩序 | 39 | 39 | 2,657,409 | 2,657,409 |

三个派系的已定级 profile 均能关联到备份中的账号行，缺失账号行计数均为 `0`。这组对照证明最终归档没有遗漏已定级 profile，且派系、定级状态和七曜值未在两份事实之间漂移。

### 时间窗内无变更

以备份平台事实更新时间为起点，并把终点放宽到结算后约八分钟：

- 起点：`2026-09-30T21:42:50.9817995Z`
- 终点：`2026-09-30T22:40:00Z`

正式库只读聚合结果：

| 检查项 | 数量 |
| --- | ---: |
| 账号 `disable` / `enable` / `logical-delete` / `username-changed` 审计 | 0 |
| `ranked-integrity` 后台变更审计 | 0 |
| `RankedIntegrityAudits` 事实 | 0 |
| `RankedSettlements` 事实 | 0 |

因此，备份中的账号启用资格在最终归档时没有变化；profile 也没有通过新结算或完整性治理发生变化。

## 计算口径

计算严格复现结算时 `FactionTotalsLocked()` 的口径：

1. 仅选择第 0 赛季的 profile。
2. `PlacementPlayed >= RankedConfig.PlacementMatches`。
3. profile 必须能关联到账号行。
4. 账号必须同时满足 `Disabled = false`、`Deleted = false`。
5. 按冻结派系分组求和 `SevenValue`。

备份中按该口径得到：混沌 `30 / 2,279,096`，命运 `42 / 2,946,802`，秩序 `38 / 2,407,333`。账号状态时间窗、profile 覆盖与最终 history 对照均闭合，因此这不是近似值或当前状态重算值。

## 操作边界

- 正式服只执行文件枚举、SHA256、压缩完整性检查和 `sqlite3 -readonly` 聚合。
- 未停止、重启或修改服务，未写入运行目录或备份目录。
- 仅把 `platform.db` 通过 `tar -xOzf` 流式提取到本地工作区临时目录；未下载整个备份。
- 查询和回执只输出派系级聚合，不输出玩家身份或比赛明细。
- 本地临时 DB、SQL、WAL 和 SHM 在核对后精确删除。
