# 赛季切换阻断数据恢复

本流程只适用于 2026-10-01 已确认的正式服阻断数据。用户已裁定对 `cfc10d2b3fd0` 采用 `waive`：接受平台中已有的 `restore-incompatible` 无效局结论，停止错误对账，不补玩家排位。

正式服精确版本 `07fcbf857169605a1b35c06215f4a1310e4b208e` 的只读证据表明，该场已有且仅有一条 S01 完整性审计，`RankedSettlements`、`RankedSettlementProfileFacts`、奖励、裁定及修正记录均为 0。其 `$unknown` 最强称号事实为 `winner=null`、主宰为空、`Games=0/Wins=0`，不满足正式服务的称号统计条件。因此没有七曜、胜负、段位或最强称号结果需要撤销。

`waived` 是本次恢复工具使用的永久审计保留状态，不是正式服务原生的 `applied` 状态。正式服务只 drain `pending`，对账只额外读取 `applied`，门禁只统计 `pending`、带错误的 `applied`、`quarantined` 和恢复隔离表，因此 `waived` 不会重放也不再阻断切季。回放清理和审计保留把 `status<>'applied'` 视为保护项，所以原对局、事件和 payload 会继续保留。

## 安全边界

`ops/server/repair-l12-season-cutover-blockers.py` 默认只读，并使用 SQLite `mode=ro` 与 `query_only=ON`。写入要求：

- `--apply --decision waive`，不能带 `--season-id`；
- 精确 123 条 legacy 候选与四场隔离证据全部一致；
- 证据文件绑定完整 match identity SHA-256、payload hash、隔离原因与时间、runtime/outbox 状态、attempts、last error、applied time、事件数、payload version 和 season identity；
- 完整 v1 payload 通过与正式 `ValidateSettlement` 相同的结构检查；
- `legion12-test.service` 实际为 inactive/failed，且 `/proc` 中没有其他进程打开数据库、WAL 或 SHM；
- 回执路径不存在并能独占创建；操作者、人工理由和工具来源提交均已填写；
- 工具取得 `BEGIN IMMEDIATE` 写锁并再次复验后，在锁持有期间通过第二只读连接调用 SQLite Backup API。写锁阻止其他写入，快照与事务修改前的逻辑状态一致；
- 快照通过 `quick_check` 并生成 SHA-256 后才执行修改，事务内失败自动回滚。

`databaseMainFileSha256Before/After` 只是 SQLite 主文件的物理哈希；WAL 模式下它不代表完整逻辑数据库。锁内 Backup 快照和 `logicalPreRepairSnapshotSha256` 才是回滚依据。

## 固定证据文件

把以下内容保存为 `/opt/legion12-deployment/season-cutover-evidence-20261001.json`：

```json
{
  "schema": 1,
  "records": [
    {"role":"decision","matchIdentitySha256":"cfc10d2b3fd07b8998066472f96177a9bce7a48d76064ef6a41c7b9974ed9869","quarantineReason":"排位恢复不兼容：缺少初始权威状态","quarantineCreatedUtc":"2026-09-05T21:11:42.7807493+00:00","modeId":"ranked","startedUtc":"2026-09-03T10:17:41.9903221+00:00","endedUtc":"2026-09-05T21:11:42.7807493+00:00","recordedSeasonId":null,"runtimeStatus":null,"runtimeRoomMatches":true,"outboxStatus":"quarantined","attempts":1,"lastError":"outbox-payload-invalid:recorded season identity missing","outboxCreatedUtc":"2026-09-05T21:11:42.7807493+00:00","appliedUtc":"2026-09-05T21:11:43.5927603+00:00","payloadHash":"acdbffff6e9c64d99bb2805d34a279d6a4e16992a94210bff6f88970c096393b","payloadVersion":1,"payloadSeasonId":null,"seasonIdentityState":"v1-missing-recorded","eventCount":158},
    {"role":"applied","matchIdentitySha256":"8f4c558ddd74fe004c313b548154da428c2efbc8797407a95395fcc6100dc61c","quarantineReason":"排位恢复不兼容：排位命令序号不连续","quarantineCreatedUtc":"2026-09-06T15:18:01.4526221+00:00","modeId":"ranked","startedUtc":"2026-09-06T14:18:05.3198271+00:00","endedUtc":"2026-09-06T15:18:01.4526221+00:00","recordedSeasonId":"S01","runtimeStatus":"completed","runtimeRoomMatches":true,"outboxStatus":"applied","attempts":0,"lastError":null,"outboxCreatedUtc":"2026-09-06T15:18:01.4526221+00:00","appliedUtc":"2026-09-06T15:18:01.8764606+00:00","payloadHash":"7fc28475bdbd665f20093a618fd5176222b3e9f5df426ed066da6b2d42b72813","payloadVersion":1,"payloadSeasonId":null,"seasonIdentityState":"v1-recorded-backfill","eventCount":265},
    {"role":"applied","matchIdentitySha256":"178a692be41bd2f5124f90909073dbdd8c5f022004fb937bfdb3dd8362d7ca98","quarantineReason":"排位恢复不兼容：排位命令重放校验失败：70","quarantineCreatedUtc":"2026-09-10T06:04:18.0269312+00:00","modeId":"ranked","startedUtc":"2026-09-10T05:52:58.4943223+00:00","endedUtc":"2026-09-10T06:04:18.0269312+00:00","recordedSeasonId":"S01","runtimeStatus":"completed","runtimeRoomMatches":true,"outboxStatus":"applied","attempts":0,"lastError":null,"outboxCreatedUtc":"2026-09-10T06:04:18.0269312+00:00","appliedUtc":"2026-09-10T06:04:20.2622890+00:00","payloadHash":"061bad00d5b57082d3598e8be6e3ff27d62bbb35b0d506bfa4f35ea732b8d67f","payloadVersion":1,"payloadSeasonId":null,"seasonIdentityState":"v1-recorded-backfill","eventCount":86},
    {"role":"applied","matchIdentitySha256":"551ef0b11a74ce014447c58d8850dfdb747bc31632e8553c96fd696ea60cb978","quarantineReason":"排位恢复不兼容：排位命令重放校验失败：260","quarantineCreatedUtc":"2026-09-11T06:15:37.3047821+00:00","modeId":"ranked","startedUtc":"2026-09-11T05:54:04.8098656+00:00","endedUtc":"2026-09-11T06:15:37.3047821+00:00","recordedSeasonId":"S01","runtimeStatus":"completed","runtimeRoomMatches":true,"outboxStatus":"applied","attempts":0,"lastError":null,"outboxCreatedUtc":"2026-09-11T06:15:37.3047821+00:00","appliedUtc":"2026-09-11T06:15:41.3302421+00:00","payloadHash":"56e1f425fc7482434f9a80a7aec4b0b3ef33585941f1b00a5abcabfbbca164e0","payloadVersion":1,"payloadSeasonId":null,"seasonIdentityState":"v1-recorded-backfill","eventCount":267}
  ]
}
```

## 正式服执行清单

`source_commit` 必须是包含本工具的已审查提交，不能使用当前正式服版本号代替。

```bash
set -Eeuo pipefail
repair_tool=/opt/legion12-deployment/repair-l12-season-cutover-blockers.py
database=/opt/legion12-runtime/matches.db
evidence=/opt/legion12-deployment/season-cutover-evidence-20261001.json
snapshot_directory=/www/legion12/runtime-backups
receipt=/opt/legion12-deployment/season-cutover-repair-waive-20261001.json
verify_receipt=/opt/legion12-deployment/season-cutover-repair-waive-verify-20261001.json
operator='填写实际操作者'
reason='接受既有 restore-incompatible 无效局结论，停止旧 v1 缺赛季身份导致的错误对账；不补玩家排位'
source_commit='填写包含本工具的40位Git提交'
test -f "$repair_tool" && test ! -L "$repair_tool"
test -f "$database" && test ! -L "$database"
test -f "$evidence" && test ! -L "$evidence"
test -d "$snapshot_directory" && test ! -L "$snapshot_directory"
test ! -e "$receipt" && test ! -L "$receipt"
test ! -e "$verify_receipt" && test ! -L "$verify_receipt"
```

服务运行时先做一次只读演练：

```bash
python3 "$repair_tool" --database "$database" --evidence-file "$evidence" \
  --legacy-before 2026-09-03T00:00:00Z --expected-legacy-count 123 \
  --expected-applied-quarantine-fingerprint 8f4c558ddd74 \
  --expected-applied-quarantine-fingerprint 178a692be41b \
  --expected-applied-quarantine-fingerprint 551ef0b11a74 \
  --decision-fingerprint cfc10d2b3fd0
```

只在回执显示 123 条候选、四场证据完全匹配、`activeMatches=123`、`quarantinedSettlements=5` 时继续。停止服务；不得改变后台维护配置：

```bash
systemctl stop legion12-test.service
test "$(systemctl is-active legion12-test.service)" = inactive
```

停服后重复只读演练，两次证据必须一致。随后只执行一次 waive：

```bash
python3 "$repair_tool" --database "$database" --evidence-file "$evidence" \
  --legacy-before 2026-09-03T00:00:00Z --expected-legacy-count 123 \
  --expected-applied-quarantine-fingerprint 8f4c558ddd74 \
  --expected-applied-quarantine-fingerprint 178a692be41b \
  --expected-applied-quarantine-fingerprint 551ef0b11a74 \
  --decision-fingerprint cfc10d2b3fd0 --apply --decision waive \
  --service-name legion12-test.service --service-stopped-ack I_HAVE_STOPPED_LEGION12 \
  --snapshot-directory "$snapshot_directory" \
  --operator "$operator" --reason "$reason" --source-commit "$source_commit" \
  --receipt "$receipt"
```

提取 run id 并以独立只读模式核验门禁、127 条审计、原 payload hash 和三场 applied 记录：

```bash
run_id="$(python3 -c 'import json,sys; d=json.load(open(sys.argv[1],encoding="utf-8")); assert d["databaseCommitted"] is True; print(d["runId"])' "$receipt")"
python3 "$repair_tool" --database "$database" --evidence-file "$evidence" \
  --legacy-before 2026-09-03T00:00:00Z --expected-legacy-count 123 \
  --expected-applied-quarantine-fingerprint 8f4c558ddd74 \
  --expected-applied-quarantine-fingerprint 178a692be41b \
  --expected-applied-quarantine-fingerprint 551ef0b11a74 \
  --decision-fingerprint cfc10d2b3fd0 \
  --verify-waive-run "$run_id" --receipt "$verify_receipt"
```

只有复核显示 `ready=true`、`unfinishedLegacy=0`、`activeQuarantines=0`、`auditRows=127`、`payloadHashPreserved=true` 才能启动服务：

```bash
systemctl start legion12-test.service
systemctl is-active --quiet legion12-test.service
curl --fail --silent --show-error http://127.0.0.1:8083/health
```

启动后用新的、不存在的复核回执路径再执行一次 post-check。然后验证自动切季计划状态，再按用户授权通过受管 `ops/windows/deploy-l12.ps1` 发布一次。修复和发布都不得改变后台维护状态。

## 回滚

事务内失败自动回滚。若进程返回退出码 3，数据库已经提交但最终回执写失败；不得重跑 apply，应使用 prepared 回执中的 run id 和只读 post-check 判断状态。

提交后的回滚必须保持服务停止：

1. 从 apply 标准输出或回执定位 snapshot，核对 `.sha256` sidecar。
2. 对当前数据库、WAL 和 SHM 另做故障现场副本。
3. 对快照运行 `quick_check=ok`。
4. 清除旧 WAL/SHM 后，以快照原子替换主数据库，并恢复原所有者和权限。
5. 启动服务；原始只读演练应恢复到 123 条 legacy、4 条恢复隔离和 1 条 outbox 隔离。

不得在服务持续写入时覆盖 `matches.db`，也不得把物理主文件哈希当作逻辑快照校验。

## 本地验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-l12-season-cutover-repair.ps1
dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --configuration Release --filter "FullyQualifiedName~SeasonCutoverRepairIntegrationTests" --no-restore -- xUnit.ParallelizeTestCollections=false
```

第一项覆盖完整 v1 载荷、证据篡改、并发写锁、锁内快照、settle/waive、回滚和 post-check。第二项走真实 `MatchRecorder`、`L12PlatformStore` 与 `L12RoomManager` 恢复路径，证明 waived 不被 drain、不会写平台排位、四项切季门禁为 0，并继续受到审计保留。
