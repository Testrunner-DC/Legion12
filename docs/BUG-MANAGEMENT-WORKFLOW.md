# Bug 管理连接与确认流程

服务器 Bug 管理是玩家反馈、管理员审计和 Codex 修复工作的共同入口。它不授权自动修改代码或自动关闭反馈。

## 固定流程

1. 只读拉取 `new`、`confirmed` 或指定条件的反馈，同时读取每条反馈的处理时间线。
2. 先检索 `docs/BUGFIX-REGISTRY.md`，再关联页面、房间、对局 JSON 和已有回归测试。
3. 向用户提交解决方案，列明根因假设、同类型扫描范围、拟修改公共层和防回滚测试。
4. 只有用户确认后才修改代码。
5. 验证通过后，在后台追加处理记录；状态变更、优先级、负责人和摘要均由服务器记录操作人、修改前后值和时间。
6. 按仓库约定同步 GitHub。部署仍须等待用户明确指令。

## 开发电脑只读拉取

密码只从 `L12_ADMIN_PASSWORD` 环境变量或交互式安全输入读取，不写入仓库：

```powershell
$env:L12_ADMIN_PASSWORD = '<服务器管理员密码>'
powershell -ExecutionPolicy Bypass -File .\ops\windows\Get-L12BugQueue.ps1 -Status new -OutputPath D:\GPT\L12-bug-queue.json
```

可用筛选参数：`-Status`、`-Priority`、`-Search`。脚本只调用登录和 Bug 查询接口，不包含更新、关闭或删除操作。

## 管理后台闭环

- 检索：编号、标题、正文、玩家、页面、房间、对局、负责人。
- 筛选：状态与优先级。
- 关联：存在 `matchId` 时可直接打开对应对局 JSON。
- 审计：建立反馈、状态、优先级、负责人、处理摘要和追加记录均持久化。
- 历史数据：旧 Bug 没有时间线时仍可读取；第一次处理后开始保留完整历史。

