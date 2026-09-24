# 牌库编辑器参考流程批次

日期：2026-09-24  
状态：独立 worktree 实现与完整验证完成，待 Main 对抗复核；未推送、未部署。

## 范围

- 严格落实已确认方案的第三阶段：Gallery / Stats / Hand、统一卡池筛选、构筑分区折叠、就地禁限提示、The Bench 备选区及移动端单任务流程。
- 宽屏继续让卡池或统计/起手与当前牌表并行；844×390 短横屏使用两栏紧凑结构。
- 360×780、390×844 竖屏通过“卡池 / 牌表 / 统计·起手”单任务入口完成编辑，不再要求旋转设备。
- 不改视觉风格；不加入标签、文件夹、归档、自动保存、撤销/重做、冲突合并或离线队列。

## 编辑流程

- Gallery 复用同一份编辑状态，覆盖名称/编号/效果、阵营、类型、产品、费用、兵力、天灾、当前禁限与排序；移动端放入底部筛选面板并显示启用条件数。
- Stats 使用当前尚未保存的构筑计算费用曲线、类型、阵营、主牌/士气/额外区/备选区数量和合法性。
- Hand 从当前尚未保存的主牌随机抽取 6 张；切换工作区不销毁 Gallery 节点，不重置筛选或构筑，也不产生网络或对局记录。
- 主宰、主牌库、士气、额外区、备选区独立显示数量并可折叠；展开状态写入本地偏好。
- 当前赛季禁用、限量、超量和阵营错误显示到对应主牌条目；服务端仍是保存时的最终规则裁定。

## 备选区存储

- `benchIds` 只属于账号私人牌库，不参与主牌数量与合法性，不进入公开牌库构筑正文、公开版本、牌库码、赛事牌表或对局牌表。
- `account_decks.bench_cards_json` 按卡号＋数量保存，重复卡号不重复写字符串；读取时恢复为兼容前端的编号列表。
- 服务端限制最多 200 张，并拒绝未知卡、非主牌类型、衍生卡、异阵营卡及超过卡牌固有携带上限的同编号备选卡。
- 备选区随私人牌库长期保存；没有按时间、数量或引用状态清理牌库信息的任务。

## 验证入口

- `dotnet test TwelveLegions.Platform.Tests/TwelveLegions.Platform.Tests.csproj --filter FullyQualifiedName~DeckDomainStorageTests`
- `dotnet test TwelveLegions.Tests/TwelveLegions.Tests.csproj --filter FullyQualifiedName~DeckValidatorTests`
- `npm run check:deck-sync`
- `npm run check:ui-contracts`
- `npm run check:performance-architecture`
- `node scripts/verify-deck-editor-reference-flow.mjs`
- Vue 类型检查、完整平台/规则测试与生产/testrun 构建。

当前视觉证据覆盖 `1920×1080`、`1440×900`、`1280×720`、`390×844`、`360×780`、`844×390`，共 12 张截图；生成物位于 `artifacts/deck-editor-reference-flow`，不提交 Git。

完整回执：账号牌库存储定向 5/5、完整平台 168/168、完整规则 4743/4743、编辑器合同 18 项、UI 契约 341、请求可靠性 30/30、卡图 41 项/324 张、Vue 类型检查及生产/testrun 各 323 模块构建通过。仅有 NuGet 漏洞源不可达的既有 `NU1900` 环境警告。
