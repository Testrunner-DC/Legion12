[CmdletBinding()]
param(
    [string]$Root = "D:\GPT\Legion12",
    [switch]$Apply,
    [int]$TestRunRetentionDays = 3,
    [int]$TempRetentionHours = 24,
    [int]$DeployArtifactsToKeep = 2,
    [int]$SnapshotSetsToKeep = 2,
    [int]$LogRetentionDays = 14,
    [int]$LogsToKeep = 20
)

# Keep parameter binding for old callers, but never scan or delete through the
# retired age/count-only implementation. In particular, -Apply must fail before
# filesystem enumeration or output creation, regardless of the supplied root.
throw 'L12_STORAGE_LEGACY_ENTRY_RETIRED: 此旧清理入口已停用（预览和应用均不执行）。请使用 scripts/clean-l12-generated.ps1 的受保护预览，并按 docs/STORAGE-GOVERNANCE.md 核对具名清单；不得仅按年龄、份数删除日志、备份或未知测试资料。'
