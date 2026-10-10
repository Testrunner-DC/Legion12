# 玩家更新日志账本

## 为什么存在

玩家更新日志不再在正式部署前临时回忆。每个开发批次在修改玩家相关源码时，必须同时在 `release-ledger/entries/` 登记其玩家结果，或明确声明该改动仅属内部实现。正式部署入口从线上 `/health` 读取当前提交，并自动聚合“当前正式提交 -> 待部署提交”区间内的账本；没有登记完整时，部署在上传或切换前失败。

测试服部署不需要正式服基线，只校验账本 JSON、分类和文案，因此不会被尚未形成正式发布区间阻断。

## 登记玩家可见变化

每个批次新增一个稳定命名的 JSON。相同批次可在提交前持续补充，不要为每个小提交建立重复条目：

```json
{
  "schemaVersion": 1,
  "id": "2026-09-25-example-batch",
  "date": "2026-09-25",
  "audience": "players",
  "title": "牌库与对局体验更新",
  "changes": [
    {
      "category": "decks",
      "text": "公开牌库会使用原画展示，拥有异画的玩家仍可在自己的构筑和对战中使用异画。",
      "paths": [
        "opcgpro-vue/src/l12/site/L12DeckEditor.vue",
        "服务端WebSocket/TwelveLegions/L12PlatformStore.cs"
      ]
    }
  ]
}
```

`text` 只写玩家能感知的最终结果，不写数据库、内部路由、审计、部署、服务器路径或管理员接口。分类只能使用 `release-ledger/config.json` 中的键；目前覆盖卡效、对局、UI、规则、赛事、牌库、回放、排位、账号权益、内容等产品区域。

`paths` 应列出本条结果覆盖的精确源码；同一目录内确属同一结果时可使用 `目录/**`。禁止使用仓库级 `*` 或 `**` 来掩盖遗漏。

## 登记纯内部变化

如果修改位于玩家相关源码目录，但行为和外显内容均不变化，也必须留下可审计声明：

```json
{
  "schemaVersion": 1,
  "id": "2026-09-25-example-refactor",
  "date": "2026-09-25",
  "audience": "internal",
  "reason": "只拆分模块，不改变玩家行为或文案。",
  "paths": ["opcgpro-vue/src/l12/example/**"]
}
```

内部条目禁止填写 `title` 或 `changes`，因此不会进入玩家更新日志。不能把实际玩家变化标成内部变化来绕过验收；Main 在合并和发布验收时必须对照需求与差异检查分类。

## 本地检查

只检查所有账本的结构、分类和文案：

```powershell
node .\scripts\release-ledger.mjs validate --repo .
```

模拟某次正式发布，检查路径覆盖并生成预览：

```powershell
$from = '<当前正式服完整提交>'
$to = (git rev-parse HEAD).Trim()
node .\scripts\release-ledger.mjs release --repo . --from $from --to $to `
  --output .\artifacts\generatedPlayerRelease.ts `
  --summary .\artifacts\player-release-summary.json
```

如有未登记文件，命令会逐项列出，并提示添加 `players` 或 `internal` 条目。自动测试位于 `scripts/test-release-ledger.mjs`。

## 正式发布如何工作

1. `ops/windows/deploy-l12.ps1` 读取正式服 `/health.serverVersion`，并确认它是待部署提交的祖先。
2. `ops/windows/verify-l12.ps1 -ProductionBaseCommit <sha>` 对该区间执行账本覆盖检查。
3. 验证器只在隔离构建目录中生成 `generatedPlayerRelease.ts`，不会改脏源码工作树。
4. 前端把这份聚合结果放在既有历史更新日志之前；内部条目不会被编入玩家包。
5. 发布清单记录 `releaseBaseCommit` 和 `playerReleaseNotesSha256`。即使调用者提供已有制品，正式部署也会拒绝基线与当前线上版本不一致的制品。

`release-ledger/config.json` 的 `baselineCommit` 是机制启用边界。更早的历史继续由 `SiteShell.vue` 中的固定历史条目保留；门禁不会要求为整个旧历史补造逐提交账本。该基线不可为规避缺项而随意前移。

## 发布与回滚边界

- 账本和自动聚合不授权部署；正式部署仍须用户明确命令。
- 测试服不会更新正式发布基线，也不会把未发布条目标记为已上线。
- 正式服回滚后，下一次部署会重新读取现场提交并重新聚合；不得复用另一基线生成的制品。
- 发现遗漏时补充同一批次账本并重新验证、构建；不要直接编辑构建产物或在服务器上手写更新日志。
