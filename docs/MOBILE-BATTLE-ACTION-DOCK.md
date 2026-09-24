# 移动对战操作停靠区专项（2026-09-24）

## 交接与边界

- 分支：`codex/mobile-battle-action-dock`。
- 独立工作树：`C:/Users/neptu/Documents/ChatGPT/Legion12/tmp/mobile-battle-action-dock`。
- 创建来源：指定的 `tmp/batch-b-data-analytics` 仓库，初始基线 `e7f13d31fba540bc8369b56985f6e9de4f618f95`。
- 根据 Main 的追加要求，先整合 `f179acdbb478113fd48c06d154dc63e22afb99db`，最终快进整合到 `a2c8c822ad7b45daa76d610a35b6c2a3889cf746`。package 相邻脚本通过三方合并保留双方内容；`platform.ts`、公开牌库详情、请求可靠性实现及原有治理门禁保持主线内容，仅在主线 package 的 UI 检查链增加本专项契约。
- 不推送、不部署、不修改 Main 或其他任务工作树。最终 Release、合入与部署由 Main 执行。
- 功能提交以本文件所在提交为准；最终提交 SHA 在任务回传中提供。

## 根因及唯一状态来源

1. 截图的三段式组件是 `BattleUtilityDock.vue`，唯一实例原在 `GameBoard.vue` 的 `selected-card-utility-slot`，与移动端被 `display:none` 的 `left-detail-layout` 绑定，因此整个设置／好友／对局工具入口消失。
2. 身份面板、路由按钮、记录、计时器、结束回合使用多个独立 fixed/absolute 坐标。身份面板后来占用顶部 96px，记录／计时器沿用不同补丁的 150/184px，动态动作仍以 bottom 64px 独立排布，缺少共同高度预算。
3. `HandArea`、`PlayerMat` 的动态按钮原 Teleport 到画布宿主，右栏还位于战场树。不同堆叠上下文、高 z-index、透明右栏以及重复覆盖使视觉位置与实际命中区域不同。
4. Prompt、墓地、主宰、阵营／卡牌能力等最小化条共用同一个固定右下位置，多条同时出现时相互重叠。
5. `inspectDialogCard` 仍会在移动端将 `mobileInspectorOpen` 设为 true，与“点选只更新详情内容、常驻按钮控制开合”不一致。
6. `mobileViewport.css` 的通用全屏规则未排除详情抽屉，使生产画布中的详情覆盖整幅画面。旧预览没有真正启动 `useLandscapeViewport`，这一错误未在旧的坐标注入式验收中暴露。
7. 移动士气摘要子控件使用与有边框父控件相同的固定高度，产生内部裁切；子控件改为占满父控件实际内容高度。

动作来源保持不变：普通／支付／目标／防御选择由 `GameBoard` 状态与既有 `GameActions` 判断；手牌打出由 `HandArea`；军团动作由 `PlayerMat`；对局管理由 `BattleUtilityDock`；路由由 `GamePage`；GM 由 `GmPanel`。没有另建动作列表、复制权限、绕过 busy 或改变规则命令。

同类扫描：`rg -n 'card-context-actions|mobile-action-dock|Teleport|board-target-controls|mobileInspectorOpen|\.minimized' opcgpro-vue/src/l12/game`，以及全 S1/S2 数据中的 `uiPattern`、`choiceMode`、支付、响应、区域选择字段。问题属于共享呈现层，适用于全部 324 张卡，无卡号特判或卡池迁移；44 类显式效果呈现发布者的现有契约仍通过。

## 布局规则

- `MobileBattleDock` 只在现有 `battleViewportLayout` 给出移动布局时挂载，使用生产 `useLandscapeViewport` 已计算的逻辑安全画布；不会再次叠加 safe-area 或 visualViewport 偏移。
- 右侧留宽由一个连续变量决定：`clamp(104px, 画布高度 × .26, 132px)`；战场网格预留同一宽度加 8px。停靠区距安全画布边缘 4px。
- 从上到下为辅助区、当前操作区、主操作区、三段快捷入口。辅助区最高占 42%，内部滚动；当前操作占据剩余空间并可独立滚动；结束回合／调度确认和快捷入口不随上方内容滚走。
- 返回／投降、双方信息（可展开）、记录、计时器、GM 入口按文档流排列。小屏可能需要在辅助区内滚动查看计时器等内容；当前动作及结束回合不会被这些内容挤出。
- `BattleDockPortal` 用组件实例级注入的 HTMLElement 目标移动原组件，不复制状态。目标不存在时禁用 Teleport，桌面节点回到原位置。宽窄切换保留选择和唯一快捷组件实例。
- 当前卡牌动作、战场／空位／资源确认、取消、支援／抵挡和恢复条共同使用当前操作区。最小化 Prompt、墓地、主宰、阵营、卡牌能力也排列在此，透明最小化层不再占据整个画布。
- 操作区 z-index 为 2200，普通 Prompt 与既有卡牌弹框仍保留其交互优先级；设置／GM／对局结果／工具／反馈进入同一宿主并高于操作区。展开的阻塞弹框仍阻止点击穿透，最小化后恢复战场命中。
- 卡牌详情只由常驻开合按钮控制；选择只刷新内容。主动打开的阅读抽屉可以收起，不自动抢开；动态操作区始终在其右侧可达。
- Bug 反馈仅通过移动对战的“对局工具 → Bug反馈”进入。独立浮动按钮在移动对战隐藏，包括 4:3 和较高平板；反馈仍使用原组件、事件与提交服务。
- 删除旧移动动态动作、记录、计时器的独立坐标覆盖及目标条预留。桌面样式块不添加移动覆盖。

## 验收与证据

全部材料在工作树下 `artifacts/mobile-action-dock/`，不提交图片与临时产物。

| 材料 | 验证内容 | 结果 |
| --- | --- | --- |
| `batch-a2c8c82.log` | 最新主线上的 `verify-l12-change.ps1 -Level Batch`，独立缓存目录 | 退出码 0 |
| `acceptance/manifest.json` | 9 档动态动作、真实命令、5 点命中、安全区、48 次连续随机尺寸、visualViewport 缩放 | 127 组断言，88 张截图 |
| `dialogs/manifest.json` | 16 类弹框 × 4 档，最小化、查看详情、关闭详情、恢复、选择保持 | 64 场景 + 12 安全区场景 + 36 焦点场景；412 张截图 |
| `overlays/manifest.json` | 设置／好友／工具／反馈、表单焦点、三个同时恢复条、GM、观战、天灾、空位、费用、牌库侦察、等待／断线、宽窄切换 | 50 场景 |
| `desktop/manifest.json` | 对原基线逐节点比较 17 组选择器的几何、缩放、display、pointer-events、z-index | 5 档完全一致，10 张截图 |

真实浏览器为 Chromium `147.0.7727.15`（Playwright 驱动本机浏览器），桌面对比使用本机 Edge。动态验收包含实际点击／滚动／表单输入，断言 `attack`、`playCard`、`activateAbility`、`resolvePrompt`、`resolveDefense`；没有向真实服务器发游戏命令或提交反馈。

固定视口：844×390、780×360、740×360、915×412、932×430、1024×600、1024×768、800×600、667×320。安全区包括左 59px、右 59px、双侧 44px，以及上 8px／下 21px。visualViewport 比例为 1.05、1.15、1.25、1；连续尺寸包含 735–746px 边界邻域及确定种子的随机比例。按钮最少／最多、长动作标签换行、详情开合、Prompt 恢复和多恢复条均留图。

桌面基线为 `e7f13d3` 的隔离归档，1280×720、1366×768、1440×900、1920×1080、1920×600。`e7f13d3..a2c8c82` 的请求治理、公开牌库详情及相关门禁／文档变动没有修改桌面对战几何。最终基线再次通过五档桌面对比、50 组附加弹层、公开牌库浏览合同 17 项及随机起手／详情保留测试；对比脚本可针对 Main 任意基线预览重跑。

Batch 明细：UI 341 项、主题 11/11、移动停靠契约、视口与弹框数值契约、连接恢复 25/25、重入 6/6、牌库放置／恢复 10/10、防御归属 23/23、资源投影 9/9、请求可靠性 30/30、性能 13/13、资源同步 18/18、卡图 41 项／324 卡均通过；Vue 类型检查与正式、testrun 双构建各 331 模块通过。没有后端改动，Batch 按变更范围未运行完整规则／平台测试，Main 的最终 Release 仍应执行其集成门禁。

## 长期回归命令

在 `opcgpro-vue` 下运行。预览只监听本机；`canvas=1` 使用真实生产视口管理器和合成状态。

```powershell
node scripts/preview-l12-battle-layout.mjs --port 5197
# 另一个终端，按本机安装情况指定支持 safe-area CDP 的 Chromium：
$env:L12_CHROMIUM_EXECUTABLE = 'C:\Users\neptu\AppData\Local\ms-playwright\chromium-1217\chrome-win64\chrome.exe'
node scripts/verify-mobile-action-dock.mjs http://127.0.0.1:5197/__l12_battle_preview__
node scripts/verify-mobile-dock-overlays.mjs http://127.0.0.1:5197/__l12_battle_preview__
$env:L12_R6_DIALOG_OUT = '../artifacts/mobile-action-dock/dialogs'
node scripts/verify-mobile-dialog-minimize-r6.mjs http://127.0.0.1:5197/__l12_battle_preview__
# 5198 为单独的基线预览，不得直接在其他任务工作树起服务写缓存：
node scripts/verify-dock-desktop-isolation.mjs http://127.0.0.1:5198/__l12_battle_preview__ http://127.0.0.1:5197/__l12_battle_preview__
npm run check:ui-contracts
```

`check-mobile-action-dock.mjs` 已纳入 `check:ui-contracts`，锁定单一实例、桌面原位、四条布局轨、安全宿主、最小化层 pointer-events、按钮换行、反馈入口、禁止恢复旧 fixed 动作定位以及浏览器测试存在性。其余三份新浏览器脚本和更新后的弹框脚本均为正式验收工具；临时 `tmp-*` 文件不入库。

## 风险、迁移与回滚

- 未在 iPhone/Safari 实机运行；刘海、手势区和浏览器视口变化由 Chromium 的 safe-area／visualViewport 模拟。Main 应保留原三项真机复验安排。
- 截图使用合成公开状态，外部请求被阻断，缺少 CDN 卡图时显示项目自身占位图；本批验收针对控制布局与命中，不替代卡图内容验收。
- 辅助区与当前操作区在内容过多时内部滚动。这是设计行为；不能将其改回整个页面滚动或重新增加固定 top/bottom 按钮。
- 真实断线传输由已有恢复测试覆盖，本批浏览器额外验证等待与断线倒计时的呈现、恢复条及工具可达性。
- 移动回放仍由既有规则禁用；观战保留只读条件，不引入可执行动作。没有修改规则、服务器协议、账号、持久化或部署配置。
- 无数据迁移。Main 合入整批提交即可；回滚应 revert 整批（Portal、移动 CSS、原节点接线与契约一起回滚），不能只删停靠样式或只回滚 Portal。回滚后需再次运行桌面隔离及移动回放契约。
