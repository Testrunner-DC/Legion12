# P0 测试记录

日期：2026-08-15  
环境：Windows 10、Node.js 22.18.0、npm 10.9.3、.NET SDK 10.0.302

## 2026-08-21 TriggerBatch 与 S2 卡效缺口闭环

- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj`：187/187 通过，0 失败、0 跳过。
- `npm run build`：UI 契约 37/37 通过，TypeScript/Vue 类型检查通过，Vite 生产构建完成（116 modules transformed）。
- `powershell -File .\scripts\audit-l12-card-assets.ps1`：通过，当前权威卡牌数据引用的卡图资源未发现缺失。
- TriggerBatch：同一时点触发按回合玩家/非回合玩家分批，由各自拥有者决定批内顺序；Prompt 携带稳定来源实例和触发实例标识，测试断言实际结算顺序。
- 明确缺失卡闭环：S02-01M1、S02-0305、S02-03M1、S02-05D1、S02-05M1、S02-0510、S02-0523、S02-06M2。
- 部分实现卡闭环：S02-0106、S02-04M1、S02-0516、S02-0519、S02-0601、S02-0615。
- 关键交互回归：悟空返还具体士气、万物统御之戒/雷神之锤真实准备选择、神域两段区域选择、月读同一时点批次排序、希波吕忒休整状态免费移动、阿尔忒弥斯转神力、特洛伊木马进入对方战场与延迟离场。

## 2026-08-19 README 当前基线复核

- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj`：176/176 通过，0 失败、0 跳过。
- `npm run build`：UI 契约 32/32 通过，TypeScript/Vue 类型检查通过，Vite 生产构建完成（116 modules transformed）。
- README 本地链接与图片检查：12/12 路径存在。
- 本次仅调整项目说明与验证记录，没有改动对战规则、卡牌数据或运行时代码。

## 2026-08-16 规则与 GrandUMI 查漏回归

- `dotnet test TwelveLegions.Tests`：45/45 通过。新增 FAQ 12 回归，验证同一操控者打出军团时可以挤掉自己覆盖在后排的反击战术；旧反击翻开并进入墓地。
- `npm run build`：通过。前端同步允许该后排反击战术格成为军团的合法落位目标。
- 浏览器实测：SQLite 对局记录可播放/暂停并从头自动逐步播放；显示双方公开战场快照、前后排、主宰、圣物、牌库/墓地、天灾和回合玩家；常规重置/抽牌/士气阶段维护不进入玩家可见日志。
- 390×844 断点检查：对战画布和记录保持可操作，但大厅外壳仍使用 1000px 最小宽度，属于横向画布适配，不是最终原生移动端布局；已列入规则/GrandUMI 审计的 P1 缺口。
- 规则内核第二批：`dotnet test TwelveLegions.Tests` 58/58 通过；新增 PendingActivation 单步/多步声明、目标失效不付费、LibraryOps 各类边界、TriggerBatch AP/NAP 顺序、DerivedStats 免死与持续减益、S2 神力/符文原子费用及晋升状态继承测试。
- 回放不再维护独立缩略棋盘，改为把 SQLite 权威快照转换成实战 `GameState` 并复用只读 `GameBoard`；`npm run build` 通过。

## 自动验证

| 项目 | 命令 | 结果 |
|---|---|---|
| Vue 类型检查与生产构建 | `npm run build` | 通过；60 modules transformed |
| npm 依赖审计 | `npm audit` | 0 vulnerabilities |
| C# 服务端构建 | `dotnet build` | 通过；0 warning / 0 error |
| 规则与记录单测 | `dotnet test TwelveLegions.Tests` | 32/32 通过 |
| WebSocket 无状态发布探针 | `node scripts/ws-smoke.mjs` | 通过；认证房间与双端状态同步由规则/平台测试覆盖 |
| 卡牌档案数据校验 | S1 本地 + 查询站 S2 去重计数 | 133 + 115 = 248 张；S2 卡图抽查 HTTP 200 |

单测覆盖：133 张 S1 数据与四套预组、相同 seed 的状态哈希一致、双方调度门槛、自动阶段、战斗与支援、SQLite 永久记录，以及有序天灾准备、随机公开天灾确认、前后排远程距离、远程原文识别、远程进攻无损、覆盖反击战术与响应堆叠、绝对防御费用保留、草薙剑置入前排后视为军团、四阵营效果、杨戬同弹框抽牌调整、观星同弹框顶/底分组排序、太阳城开局陵墓守卫、贝奥武夫堆墓、卡诺匹斯额外圣物和服务端主动效果元数据。

追加 FAQ 语义回归：伊西斯的 3 张陵墓守卫在效果进入堆叠前作为费用弃置；埃吉尔只在登场时发动其减兵力效果，进攻时不再错误重复；前线侦查、阿伊、织田信长、土方岁三、高杉晋作的可选士气效果不再自动支付；奥拉夫二世、古斯塔夫一世、勇士比约恩与猎杀时刻的墓地卡牌改由玩家选择并决定返回牌库底部的顺序。

## 联机冒烟证据

- 房间码：`CY2DMM`
- matchId：`9090e8f8c6574c18ab662d05783d849a`
- 最终 revision：`3`
- 两端一致状态哈希：`cc1760fec96c8b25841ee7bdaaa14688d63bdab23567efd9cfba2a7c588ad7f0`
- 验证流程：两客户端连接 → 建房/加入 → 双方就绪 → 双方空调度 → 先手推进至主要阶段 → 非回合方结束回合被拒绝 → 合法结束回合。
- SQLite：`matches.db` 实际生成，测试后大小 258,048 bytes；包含合法与被拒绝操作的逐步完整快照。
- 历史接口：`/api/matches?limit=3` 返回 3 场记录，最新一场包含 4 次命令；`/health` 返回 133 张卡牌。

## 第二阶段联机冒烟证据

- 房间码：`8BT47Q`
- matchId：`a405703ab1e24297830d7b161e94c1eb`
- 最终 revision：`9`
- 两端一致状态哈希：`5015bd83e312ffb4541ed231ae1567a701cf3c7ad90fdec81e2ef2601dc0983a`
- 验证流程：两客户端连接 → 建房/加入 → 双方就绪 → 掷骰胜者选择先手 → 三次有序禁选即时公开 → 随机公开 → 先后三选一/二选一 → 双方同时调度 → 两端哈希一致。

## 浏览器渲染检查

使用本地浏览器实际检查 GrandUMI 式大厅、卡牌档案、SQLite 对局回看，以及固定 1600×760 等比缩放的三栏战场。完成双人建房/加入/就绪、有序天灾禁用与选择、双方调度、自动阶段、点击军团后直接点击阵地登场、巴御前登场获得冲锋并在同回合进攻、主宰选择不抵挡、记录内卡名点击、天灾牌背/图标、回合半区亮度、士气、同尺寸活跃/休整卡牌和可滚动完整记录。追加实测了居中的先后攻与天灾弹框、对手等待动作、天灾完整详情、弹框最小化、调度弹框、杨戬主宰点击入口和 7 张手牌选择、双方中间高两侧低的自适应扇形手牌，以及仅在阶段条逐项点亮的自动阶段过程。页面在 1280×720 完整可用；目标最低分辨率为 1366×768。随后扩展档案为 S1 133 + S2 115 共 248 张，生产构建再次通过，并抽查 S2 卡面资源可访问。

双会话房间实测：双方分别选择“太阳城预组 S1 · 梅杰德”和“阿斯加德预组 S1 · 洛基”；选择状态双端同步，准备后按实际预组建局，太阳城墓地显示 3 张陵墓守卫，掷骰胜者获得居中先后攻弹框，对手显示等待动作。WebSocket 保存地址会从 `/ws/` 归一化为 `/ws`，创建房间后状态由 offline 正常切换为 online。

2026-08-16 同类语义回归：陵墓守卫不计入 40–50 张主牌库且最多 3 张；所有离场去向经统一替代层处理；阿伊、图坦卡蒙、陵墓构造体、不朽之礼及同类从区域登场流程均要求选择合法阵地；所有效果文字含“可对我方主宰造成1点伤害：此军团登场费用-1”的军团自动进入统一的是/否额外费用流程；所有手牌选牌提示统一为单行横向滚动。

最终回归：`dotnet test TwelveLegions.Tests` 66/66 通过；正式目录与工作目录的 16 个本轮文件哈希一致。浏览器实测选择太阳城主宰后可加入且最多加入 3 张陵墓守卫，顶部显示“主牌库 0 / 40–50 ＋ 陵墓守卫 3”，陵墓守卫不计入主牌库数量；创建测试房间后连接状态从 `offline` 正常切换为 `online`。

2026-08-16 卡效根因审计回归：`dotnet test TwelveLegions.Tests` 71/71 通过，前端生产构建通过。新增安卡神杯两项主动效果、神剑格拉墨主动伤害、法老王庆典“加入手牌→置入墓地→剩余排序回底”、打牌与主动效果的陵墓守卫支付选择，以及虚拟阵营卡在 PendingActivation/支付续接中的来源恢复测试。卡效结算中的可选士气支付统一进入陵墓守卫支付选择入口；野外扎营不再自动扣费。

2026-08-16 S2 首批语义迁移：`dotnet test TwelveLegions.Tests` 80/80 通过。补齐驱魔道士陆瑛“对方战术结算后产生新堆叠并可回手”、路易芒德兰/重装士兵整段对方回合前排持续 +1000、万物统御之戒“弃手→检索→洗牌”、防御部署一次覆盖最多2张反击战术、祷告仪式双方公开确认/拒绝后私下查看、神农鼎抽牌与重置主宰效果次数、乾坤阳击杀与可选返还士气抽牌。S2-0015 至 S2-0018 已先纠正为反击战术分类，但其各自响应时点与效果仍未完成，不能计为卡效完成。

2026-08-16 权威事件与 S2 反击战术：`dotnet test TwelveLegions.Tests` 91/91 通过。新增服务端 `AuthorityEvent`，统一抵挡/支援、非手牌登场、效果加入手牌、效果转为活跃四类响应时点；完成 S02-0015～S02-0018 的实际结算及4项语义测试。`TriggerBatch` 同时扩展至阵亡、离场、进攻后、主宰受伤、对方战术结算后触发，并增加双方同时阵亡时 AP/NAP 建栈顺序的集成测试。仅有的构建警告为离线环境无法读取 NuGet 漏洞索引，不影响编译与测试。

前端同轮回归：交战时取消整个回合半场提亮，只高亮进攻卡和被攻击军团所在单格/主宰；随机公开天灾的放大规则不再依赖服务端是否附带 `cardType`；调度、墓地及其他蒙版上方统一显示选中卡牌详情；兵力高于/低于印刷值使用高对比暗绿/暗红底白字；交互按钮增加统一的高对比度约束。

双客户端实机补验：走完先后攻、三次禁用、随机公开、双方天灾选择、双方调度、墓地查看并推进至真实军团交战。随机公开与调度/墓地中的卡牌详情均在蒙版上层可见；交战状态 DOM 中进攻格与目标格各 1 个，整片玩家半区高亮为 0，确认军团和主宰均只高亮实际被攻击对象。

## 本轮重复缺陷根因与防复发约束

- 根因一：同一规则语义散落在逐卡 `switch` 分支中，修正一张卡不会自动覆盖同文本卡。现在按效果文本/动作语义进入公共流程，并以全部匹配卡号做参数化回归。
- 根因二：手牌、墓地、战场和牌库之间存在绕过统一移动入口的直接写入，导致陵墓守卫的离场替代规则只在部分效果生效。现在战场离场统一经过区域移动/替代层，禁止逐卡自行决定最终区域。
- 根因三：旧测试只核对最终数值，未核对玩家是否得到选择、候选位置是否合法、拒绝后是否不支付费用。现在把 Prompt 的出现、候选项、选择后的阵地及费用一并作为断言。
- 根因四：前端按“某个卡名/某个 Prompt 名称”决定布局，导致奈芙蒂斯等同类手牌选择再次换行。现在布局取决于来源区域和交互类别；所有手牌候选统一为单行横向滚动。
- 根因五：早期占位数据 `8` 同时承担测试夹具与规则数量，且构筑总数没有独立定义“计入主牌库的卡”。现在陵墓守卫默认 3 张，主牌库计数在前后端均显式排除该卡并独立限制其最多 3 张。

## 2026-08-21 单人测试沙盒与 GM 权限回归

- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --no-restore`：191/191 通过，0 失败、0 跳过。
- `npm run build`：UI 契约 41/41 通过，Vue/TypeScript 类型检查通过，Vite 生产构建完成。
- `dotnet build .\服务端WebSocket\GrandUMIServer.csproj --no-restore`：0 warning / 0 error。
- 权限验证：正式房间直接调用 `gmAction` 被拒绝；把 GM 类型伪装成普通 `gameAction` 同样被拒绝；沙盒创建者可执行，虚拟对手和观战者不会获得 `gmEnabled`。
- 状态验证：未知卡号、非法/占用阵地不会产生部分写入；无效果战术会正常进入墓地；强制切阶段会清理处理区与待响应状态；启用天灾的沙盒会建立可触发牌库且〈堙灭〉始终位于最底部。
- 记录验证：双方沙盒调度、合法及被拒绝 GM 命令、对应权威快照均写入 SQLite；面板可下载 `/api/matches/{matchId}` 的完整 JSON 用于复盘。

## 2026-08-21 卡效原子化基线与首批实战迁移

- 全卡清单：S1/S2 共 248 张均能在管理后台查询原文、逐能力时点、原子流程、参数、内核契约和原始 JSON。
- 实战迁移：首批 16 个能力由后台与服务端共用的 `verified-runtime-program` 接管；15 个旧卡号分支被移除，服部半藏主动翻面作为原先缺失能力直接进入原子运行时，旧分支由 224 降至 209。
- 防重复结算：原子解释器遇到选择会暂停，遇到 `legacy.resolve` 会转交旧权威实现；两条路径不会在同一次能力中同时继续。
- 防回滚审计：`powershell -ExecutionPolicy Bypass -File scripts/audit-l12-atomic-effects.ps1` 通过，卡池 248/248，旧分支 209 未增长，原子 Schema 与注册表存在。
- 前端：UI 契约 45/45、Vue/TypeScript 类型检查和 Vite 生产构建通过。
- 后端：服部半藏、山河社稷图、观星与原子化专项 11/11 通过；完整回归 200/200 通过。
- 旧行为防回滚：服部半藏覆盖后仅己方可见卡面并可在自己主要阶段主动翻正；山河社稷图检索后的剩余卡统一复用观星的单行排序和“全部回顶/全部回底”协议。

## 2026-08-23 条件费用与挑衅公共规则回归

- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --no-restore`：264/264 通过，0失败、0跳过。
- `powershell -ExecutionPolicy Bypass -File .\scripts\audit-l12-atomic-effects.ps1`：卡池248/248，旧卡号分支208处，未高于209基线。
- 条件费用：S02-0509、S02-0510、S02-0512、S02-0518 的显示费用与实际支付统一读取结构化能力原子；补回奥德修斯“神力为0张时登场费用-1”。
- 挑衅：全卡池8张前排印刷挑衅统一使用带排位的合法目标查询，军团处于后排时不再错误限制进攻目标；动作结算、主宰目标和合法目标快照使用同一入口。

## 2026-08-23 沙盒固定视角、资源点击、GM 选位与有效响应回归

- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --no-restore`：266/266 通过，0失败、0跳过。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --configuration Release --filter "FullyQualifiedName~PlatformStoreTests"`：4/4 通过。
- `npm run build`：UI 契约 71/71 通过；Vue/TypeScript 类型检查及 Vite 生产构建通过。
- 沙盒：观察视角与动作控制方分离，我方固定在下方、对方固定在上方；双方手牌及 Prompt 均由沙盒创建者操作，切换动作方不再翻转棋盘。
- GM：从目录或手牌选择军团后进入棋盘选位，仅合法空位可提交；圣物与战术仍按原规则立即执行。
- 符文：高文与槲寄生统一使用场面符文圆形支付，后端按玩家实际点击的 `rune:N` 结算。
- 响应：默认仅在权威合法动作集合非空时打开窗口；无覆盖反击且无合法手牌响应时自动让过，戏法师的傀儡等合法响应仍会正常出现。
- 误跑 `GrandUMIServer.Tests` 全集会因仓库不包含 GrandUMI 历史 `卡牌数据` 而报告45项环境失败；正式发布门禁只运行上述 `PlatformStoreTests`，本轮按发布脚本口径验证4/4通过。

## 2026-08-23 奥林匹斯人工辅助原子化扩展

- 原子专项：10/10 通过。
- 十二军团规则/实战：266/266 通过。
- 原子棘轮：卡池248/248，旧卡号分支208处，未高于209基线。
- 人工辅助：S02-0513～S02-0520、S02-0522、S02-0523、S02-05M1 录入设计者给出的能力边界与原子组合。
- 人工确认：S02-0521、S02-05M2、S02-05C1、S02-05C1A 标记 confirmed；S02-0502、S02-0506 的原有确认保持不变。
- 信息可见性：柏拉图、忒修斯、荣耀之路与普罗米修斯的展示均包含双方可见、对手确认和日志卡名链接策略。
- 迁移声明：本批只完成后台/实战共用的结构化定义与审查状态，不把旧权威分支误报为已迁移；实战仍只走唯一 legacy 兜底。

## 2026-08-26 规则书关键词基线与条件触发迁移

- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --configuration Release --no-restore`：405/405通过，0失败、0跳过。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~PlatformStoreTests"`：8/8通过。
- `npm run check:ui-contracts`：88/88通过；`npm run build` 的Vue/TypeScript类型检查与Vite生产构建通过。
- 新增规则书五类效果、八个正式关键词、进攻距离、弓手职介能力与核心概念基线文档。
- 新增并实战迁移试炼推进、双方主宰恢复、条件强攻、条件冲锋和条件可选伤害；后续扫描又迁移宫本武藏条件冲锋与赫拉克勒斯·晋升进攻时强攻。
- 验证原子由37项增至44项；旧卡号 `case` 分支由187处/154卡降至181处/151卡，原子棘轮收紧为181。

## 2026-08-26 历史卡号分支清零

- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --no-restore`：406/406通过，0失败、0跳过；其中原子专项46/46通过。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~PlatformStoreTests"`：8/8通过。
- `npm run check:ui-contracts`：88/88通过；`npm run build` 的Vue/TypeScript类型检查与Vite生产构建通过。
- `powershell -ExecutionPolicy Bypass -File .\scripts\audit-l12-atomic-effects.ps1 -RequireZero`：248/248张卡进入清单，旧卡号 `case` 为0处/0卡，零分支门禁通过。
- 178条结构化复合路由逐条通过唯一触发、唯一可执行 `CompositeFlow`、无 legacy 回退和运行时同对象验证；3条已完成试炼映射按试炼名称处理。
- 曾误跑不属于发布门禁的 GrandUMI 历史测试全集；其45项失败均为仓库不包含外部 `卡牌数据` 目录，正式 `PlatformStoreTests` 8/8通过，未将环境失败计为本批回归结果。

## 2026-08-28 公共进攻子阶段与战斗阵亡顺序

- `dotnet build .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --no-restore`：0 warning / 0 error。
- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --no-build --filter FullyQualifiedName~CombatTimelineRegressionTests`：11/11 通过。
- 新时序与既有〈绝对防御〉聚焦复验：12/12 通过；完整后端门禁：445/445 通过。完整门禁首次发现〈绝对防御〉未接入新的防守方响应时点，修复公共响应分流并增加“不回退已结算进攻时效果”回归后通过。
- `npm run check:ui-contracts`：121/121 通过；`npm run build`：Vue/TypeScript 类型检查与 Vite 生产构建通过。
- `powershell -ExecutionPolicy Bypass -File .\scripts\audit-l12-atomic-effects.ps1 -RequireZero`：248/248 通过，旧卡号 `case` 0处/0卡。
- 精确覆盖：进攻方/防守方时点隔离、攻击者离场自动中止、重连快照子阶段、冻结进攻值、同列后排支援、权威防御重校验、当时实现的单方击杀与“双方同归无击杀”、触发结束后入墓、贯穿父子进攻恢复。其中“双方同归无击杀”已在 2026-08-31 依 FAQ 19 最终裁定纠正，不再是现行回归口径。

## 2026-08-29 傲慢资源、天灾高清、精确回合开始期限与击杀门禁

- 阿喀琉斯聚焦回归8/8：真实击杀获得挑衅、佣兵抵挡且没有击杀时不获得、前排动态生效、额外回合开始精确失效、晋升登场及远程入伤规则。
- 其余聚焦覆盖：伊西斯三守卫不可双付、第四守卫支付傲慢、库丘林前排免死、宫廷魔术师期限、晋升状态转移及天照双能力独立费用。
- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --no-restore --configuration Release --settings <串行配置>`：517/517通过。普通并行运行首次因 .NET 10 正则引擎内部 CLR 错误中止，已通过禁用测试集合并行完成全量复验；没有断言失败。
- 平台持久化聚焦：22/22通过。
- `npm run build`：UI契约147/147、卡图架构19/19（248张）、Vue/TypeScript及Vite生产构建通过。
- 原子审计：248/248、legacy case 0、`cardConditional=212`、`cardSwitchArm=67`、`effectTextInference=3`；新增身份判断进入结构化语义层，没有提高旧分支基线。

## 2026-08-29 观战退出、重连与房间关闭生命周期

- 聚焦回归4/4：观战者可退出运行中房间并立即创建新房间；显式退出后重连不恢复旧观战；房主关闭已结束房间会释放并通知全部观战者；既有玩家投降后离房保持正常。
- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --no-restore --configuration Release --settings <串行配置>`：520/520通过，0失败、0跳过。
- `npm run check:ui-contracts`：148/148通过；`npm run build`：UI契约148/148、卡图架构19/19（248张）、Vue/TypeScript及Vite生产构建通过。
- 原子审计：248/248、legacy case 0、`cardConditional=212`、`cardSwitchArm=67`、`effectTextInference=3`。

## 2026-08-30 BQ-20260830-01 拉美西斯、狂热者与王者之剑

- 新增5条回归首轮3失败/2通过；修复后与相关既有守卫合计13/13通过。
- `dotnet test .\TwelveLegions.Tests\TwelveLegions.Tests.csproj --no-restore --configuration Release --settings <串行配置>`：525/525通过，0失败、0跳过。
- 平台持久化聚焦：22/22通过。
- `powershell -ExecutionPolicy Bypass -File .\scripts\audit-l12-atomic-effects.ps1 -RequireZero`：248/248通过，legacy case 0、`cardConditional=212`、`cardSwitchArm=67`、`effectTextInference=3`。
- `npm run check:ui-contracts`：148/148通过；`npm run build`：UI契约148/148、卡图架构19/19（248张）、Vue/TypeScript与Vite生产构建通过。
- GrandUMI历史全套额外审计为213/223，10项既有`NewAtomicOps`、`EB_M6`、`OP16`和`OP16_045`失败；与本批L12规则文件无交集，不属于既定L12发布门禁。NU1900仅为NuGet漏洞元数据源离线警告。

## 2026-08-30 费用资源排序、神力辨识与牌库 Profile

- `npm run check:ui-contracts`：152/152通过；新增契约覆盖普通/奥林匹斯资源顺序、物理实例ID映射、淡黄色神力标识及牌库 Profile 公共组件消费者。
- `npm run check:card-assets`：19/19通过，248张卡牌资产架构无回退。
- `npm run build`：Vue/TypeScript类型检查与Vite生产构建通过。
- 全入口扫描确认牌库编辑器、我的/公开牌库、详情、新旧友谊战房间与沙盒均使用 `DeckProfile`；排序不写回 `player.morale`，支付/返还仍提交真实 `instanceId`。

## 2026-08-31 FAQ 19 双方同归各自战斗击杀

- 原始 FAQ 19 复核：双方军团以相同兵力互相战斗致死时，双方均享受各自【击杀时】效果。公共时间线固定为进攻者击杀、防守者击杀、进攻者阵亡、防守者阵亡，最后统一入墓。
- 红测：修复前 `CombatTimelineRegressionTests` 15项中3项失败、12项通过；失败精确命中同归双方印刷击杀、防守者下一次击杀转活跃、防守者击杀贯穿子进攻。修复后专项16/16，包含同归双方赋予击杀效果、延迟入墓和父子进攻恢复。
- 全卡池扫描：6张印刷战斗击杀来源为 `S01-0409`、`S02-0002`、`S02-0503`、`S02-0602`、`S02-0606`、`S02-0611`；动态消费者覆盖 `S02-0608` 经 `S02-06S6` 获得的临时贯穿及 `S02-0520`、`S02-06M1` 赋予的下一次击杀转活跃。
- Focused：十二军团规则550/550，UI契约166/166。
- Batch：Release规则550/550；原子审计248/248、legacy case 0、`cardConditional=211`、`cardSwitchArm=67`、`effectTextInference=0`；UI166/166、卡图19/19（248张）、Vue/TypeScript/Vite生产构建通过。
- Release：规则550/550；平台持久化/控制平面60/60；原子、UI、卡图和生产构建再次通过；`verify-l12.ps1` commit-level验证成功。未部署。

## 2026-08-31 第一批卡效审查：复合预声明与FAQ53类型化击杀

- 红测：新增 `AtomicReviewBatch1RegressionTests` 首轮4/4按预期失败，分别命中〈倪克斯的陨星〉支付后才选择公开对象、〈沙漠君临〉支付后才选择弃牌/召唤/空位、〈乾坤阳〉取消声明仍可能付费，以及后续独立段目标失效后费用/父流程边界；实现类型化来源击杀后再加入震击连带双杀专项。最终新增专项5/5通过。
- 相关回归：`AtomicReviewBatch1RegressionTests|S2FactionRegressionTests|S2UniversalEffectsTests|LatestBugRegressionTests|CombatTimelineRegressionTests` 合计278/278，覆盖7张迁移卡、李牧/冲田总司免费发动链与FAQ19双方同归直接战斗击杀。
- Focused：Debug规则555/555，0失败、0跳过。
- Batch：首次Release规则555/555通过，随后Windows PowerShell 5因新增审计帮助脚本为UTF-8无BOM而解析失败；补齐BOM后同一旧PowerShell审计通过，完整Batch重跑规则555/555及原子审计均通过。该次失败仅为脚本宿主编码兼容，不是业务断言失败。
- Release：首轮规则555/555、平台持久化/控制面60/60、原子审计通过；commit-level再次通过规则555/555、平台60/60、UI契约166项、卡图架构19项/248张、Vue TypeScript、Vite生产构建与发布包构建。未执行部署。
- 原子审计：卡池248；legacy卡号case 0；`cardConditional=208`、`cardSwitchArm=67`、`effectTextInference=0`；44项细原子覆盖41张、179条复合路由覆盖148张；74张未进入两个原子路由文件，但74张均有权威实战入口，0张无实战入口，63张另有测试证据。

## 2026-08-31 第二批卡效审查：语义证据与主动公开预声明

- 运行时证据：矩阵测试证据新增 `ability:<id>`、`type:<cardType>`、`entry:<shared>` 稳定语义映射，不再要求测试源码出现卡号字面量；自测试覆盖养由基能力、符文类型与试炼共享入口。重新导出矩阵后仍为248张、权威实战入口缺失0张，未进两类原子路由但有语义测试证据由63张增至73张。
- 主动事务：复用公共 `PendingActivation`，将克利奥帕特拉七世、洛基回血、凌霄宝殿、黄泉之门两项、太阳城〈不朽之礼〉、阿尔维达、天照大神降费击杀、雷神之锤、奥林匹斯翻士气及诸神巅公开模式/目标/战场位置收敛为“声明→复核→支付→入栈→结算”。取消或声明失效不支付；黄泉回收统一发布 `effect-hand-add` 权威事件；凌霄在声明、提交和结算均复核复活军团费用不高于实际返还士气。
- 独立段与回归：黄泉四段分别入栈并开放响应；费用不高于3的目标失效会记录取消并继续费用不高于1的段。新增第二批专项10/10，覆盖黄泉独立段、十字军取消/非法/符文不足/贯穿期限/权威回手、诸神巅六次伤害预声明与翻士气失效。受影响类109项首轮仅第一批旧事件文本断言失败，按“失效段继续”新语义修正后精确1/1通过。
- 门禁：主代理将项目内 PowerShell 审计改为验证器同进程执行，消除 Windows 子进程受限语言模式造成的 dot-source 假失败，并让公共声明源码本身触发防回滚检查。Release 完整规则566/566、平台持久化/控制面60/60、语义证据门禁、主动预声明门禁、原子审计248/248、UI契约166项、卡图架构19项/248张、Vue/TypeScript、Vite生产构建、发布包构建及 `git diff --check` 全部通过。原子审计为legacy case 0、`cardConditional=210`、`cardSwitchArm=67`、`effectTextInference=0`。未部署。
