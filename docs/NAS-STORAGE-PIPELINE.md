# NAS 后台转存管线

## 目的

NAS 转存不再依赖资源管理器窗口。管线以 UNC 目标运行在 Windows 任务计划中，使用 `/Z` 断点续传，复制结束后对目标文件逐项核对大小和 SHA-256，再决定是否允许源端退出。

## 组成

- `ops/windows/Invoke-L12NasStoragePipeline.ps1`：Plan／Apply／Verify 三阶段入口。
- `ops/windows/Register-L12NasStorageTask.ps1`：以当前 Windows 用户的交互式凭据注册后台任务，不保存密码、不创建新的凭据。
- `ops/windows/Register-L12NasRetentionTask.ps1`：注册独立的留存轮转任务，按策略每次最多清理一组已核验旧批次。
- `ops/windows/nas-storage-policy.example.json`：源目录、NAS 目标和保护边界模板。
- `ops/windows/Invoke-L12NasRetention.ps1`：NAS 侧保留两组、proof/失败/PINNED保护和单组轮转。
- `ops/windows/Retire-L12LegacyCloudStorage.ps1`：NAS完成回执之后停用旧百度云任务、清理旧状态，并在显式开关下退役旧代码入口。

任务使用 UNC 路径，不依赖盘符映射；但 NAS 必须在执行任务的 Windows 用户会话中可访问。任务不会打开或控制资源管理器，也不会把“进程退出”当成成功。

## 阶段

1. `Plan`：验证策略、源目录和目标路径，生成计划回执，不复制、不删除。
2. `Apply`：建立本地互斥锁，先记录轻量文件数/字节数，再使用 Robocopy `/Z` 复制；复制完成后才逐文件生成不可变 SHA 清单并双端核验，保存日志和部分结果。`layout=grouped` 写入 `groups/<批次>`；`layout=root` 仅在策略明确允许且已有根目录盘点无冲突时补齐缺失文件，不覆盖同名资料。策略可固定 `groupName`，使断网后的下一轮继续写同一批次而不是从头新建目录。
3. `Verify`：读取最近一份或指定批次的源清单，逐项检查目标文件存在、大小及 SHA-256；任意缺失、大小不符或哈希不符都失败保留。
4. 验证完成后在 NAS 批次目录写入 `nas-group.json`。留存管线只接受这个具名清单，不按目录时间或复制进程退出猜测成功。
5. 只有策略明确允许且命令显式传入 `-AllowSourceRetirement` 时，才会在验证通过后退出源目录；默认永远保留源文件。

## 运行与异常

所有回执写入 D 盘受治理 artifacts 根。NAS 不可达或 UNC 探测超过有界超时时写入 `paused-nas-unavailable` 回执，不启动下一批；网络恢复后可重新使用同一策略和 `/Z` 续传。任务锁防止两批同时写同一目标，避免重复复制和带宽争抢。

最新发布、失败证据、私有资料、当前运行数据、数据库和 WAL 不得自动纳入策略。D 盘旧 `.tmp`、旧验收 artifacts 和历史部署证据必须先逐组分类，再加入实际策略文件。示例策略保持 `template`/禁用状态，只有在源目录和目标共享已由人工核对后才复制成 D 盘受管的 `active` 策略。

## 旧管线退役

旧百度云管线不会在 NAS 迁移完成前删除。迁移完成后必须先拿到 NAS `verified-source-retained` 或 `verified-and-retired` 回执，再使用退役清单：先停用旧计划任务，再移除旧状态/传输残留；旧代码入口只有在所有引用已切到 NAS 并完成候选整合后，才允许显式退役。退役结果写入独立回执，避免后续误把百度云入口当作可用管线。这样不会出现“源还没迁完就删旧管线”或“旧入口被误复用”。
