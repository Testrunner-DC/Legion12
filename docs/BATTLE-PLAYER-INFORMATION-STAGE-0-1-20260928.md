# 对战玩家信息：阶段 0/1 盘点与展示合同

日期：2026-09-28。工作区基线：`d2206cc274e3a3618830ba637383071124807529`。状态：只读盘点和拟定合同；尚未修改产品代码，也未通过新文案的桌面、移动、真实对局或真人理解验收。

此文件承接《对战玩家信息优化：分阶段执行方案》的阶段 0/1。范围包括对战页面内的常驻信息、操作弹框、卡牌/效果详情、等待与错误提示、工具弹框、结算、对局记录，以及对应的观战与回放展示。弹框与收起栏是玩家处理信息的主要入口，不能仅作为布局附件检查。

## 证据等级和既有边界

- **S：源码可确认**——当前字段、条件分支、渲染和投影逻辑；不据此声称玩家已正确理解。
- **P：构造事件投影**——用指定事件调用现有日志投影得到的输出；不等同于完整引擎复现。
- **E：真实引擎复现**——需要具名确定性测试及事件样本，目前本文件没有完成。
- **U：体验假设**——须经真实浏览器或玩家走查验证；不能作为已证实 Bug。

保留现有 P0 单一效果权威、P1 引擎单写、P2 房间投影隐私、P3 Journal V2 不重算历史、P4 响应式布局边界。现有 `PromptPresentation`、事件元数据和 `logViewModel` 是扩展入口；不另建平行结算或日志体系。现有身份布局 55 场景/95 张截图是前置基线，不能证明本轮较长文案、弹框交互和记录详情已经合格。

## 信息入口清单

优先级 P0 表示可能误导当前决定、真实结算或历史回顾；P1 表示影响理解或可达性；P2 表示用词一致性。以下是按信息任务登记的入口，后续批次需在每个入口下展开实际文案和具名场景。

| 入口/来源 | 玩家需要理解或处理的事 | 当前证据与缺口 | 优先级/验收场景 |
| --- | --- | --- | --- |
| `GamePage.vue` 对局载入、连接与退出结果 | 对局是否仍有效，等待还是离开 | S：页面有连接、投降和结果文案；连接恢复与离开后果需按真实状态核对 | P1；断线、重连、结算、主动离开 |
| `GameBoard.vue` 回合、阶段、双方状态与当前选择 | 轮到谁，当前操作人是谁，局面数值如何影响可选操作 | S：战场 HUD 与若干内嵌选择栏分散展示；同一操作可能和通用 Prompt 分流 | P0；双方视角、响应窗口、移动窄屏 |
| `GameActions.vue` 调度、抵挡、支援与结束回合 | 选什么、付什么、提交后的战斗状态 | S：调度已有“换掉所选牌/零选择保留”；抵挡和支援按钮存在；实际支付、牺牲与结果需逐场景对齐 | P0；调度零选、抵挡弃牌、支援牺牲 |
| `PromptOverlay.vue` 选择/确认弹框 | 为什么出现，数量范围、费用状态、每项后果、确认/取消/放弃差异 | S：已有标题、情境、要求、支付、提交后果和部分选项后果字段；旧分支与回退文案仍需核对 | P0；费用、响应、可选发动、多选、无对象 |
| `PromptOverlay.vue` 最小化、等待面板 | 收起后待办是否保留、是否仍在等谁 | S：收起栏显示要求；等待视角另有摘要。须核对各 Prompt 型别、恢复与实时更新 | P0；收起/展开、轮到对方、重连 |
| `PromptCardCandidate.vue` 卡片式选项 | 同名卡如何区分，为何不可选，选中后果 | S：仅显示名称、元信息、已选/当前不可选；组件未接收具体禁用原因或 `ChoiceConsequences`；卡片文字有截断样式 | P0；同名目标、不可选目标、长名称、键盘/触控 |
| `GameBoard.vue` 内嵌位置/资源选择 | 位置与费用是否清楚，取消是否会支付或打出 | S：存在与通用 Prompt 并行的战场选择控件；需统一合同并核对即时提交 | P0；格位、混合支付、返还、取消 |
| `PlayerMat.vue` 阵营与场上能力弹框 | 能力条件、可用性与目标 | S：阵营效果弹框与能力入口；卡文、当前条件、实际结果尚需分层检查 | P1；可用/不可用能力 |
| `MasterOverlay.vue` 主宰能力弹框 | 哪个能力能发动，说明与禁用原因 | S：展示能力文本、按钮和收起/关闭；须核对禁用原因和焦点恢复 | P1；能力选择、窄屏 |
| `GraveyardOverlay.vue` 墓地弹框 | 公开区域里有什么，关闭/收起后能否回到原任务 | S：有独立窗口和移动安全视口样式；需核对与选择弹框叠层 | P1；目标选择期间查看墓地 |
| `CardDetailContent.vue` 与战场卡牌详情 | 规则原文、当前修正与本次效果是否可区分 | S：共用详情内容；不能用卡文反推已发生结果 | P1；持续效果和同名卡 |
| `BattleUtilityDock.vue` 设置、平局、举报、好友 | 提交对象与后果、申请状态、对方响应 | S：工具、设置和平局响应均有独立弹框与通知；部分英文装饰与关闭语义需核对 | P1；请求提交、等待、拒绝、关闭 |
| `PlayerTurnClock.vue` / `SetupDecisionClock.vue` | 谁的时间、何时超时、超时如何处理 | S：计时来源独立；文案要随具体场景，不做统一虚构承诺 | P0；调度与排位计时 |
| `GameBoard.vue` 移动端事件栏与 `BattleEventLog.vue` | 当前发生什么，怎样回顾前因后果 | S：事件栏使用 `projectLog`；除战斗行外普通行不能展开事实详情 | P0；长链、部分完成、移动 |
| `logViewModel.ts` 事件投影 | 摘要短而准确，详情保留费用、目标、顺序、失败原因 | S/P：部分权威结果类型被隐藏；分组摘要只挑选部分事实；详见下节复现 | P0；无效、跳过、部分完成 |
| `ReplayPage.vue` 回放及结果 | 历史时点和观看视角，不被当前棋盘覆盖 | S：复用 `GameBoard`、日志和重建状态；历史位置须取事件当时事实 | P0；双方视角、旧记录、恢复 |
| 服务端 `L12PromptsAndSetup.cs` | Prompt 的要求、等待摘要、格位名称 | S：`BuildPromptPresentation` 有结构化字段和通用回退；`PlayerBattlefieldSlotLabel` 已采用阵营/排/左中右 | P0；具体 Prompt 分支、隐私投影 |
| 服务端 `L12ResponsePresentation.cs` | 响应来源、公开对象、支付方式 | S：公开目标带位置；以发动者为准的“我方/对方”需避免与观看者视角混淆 | P0；双方响应和隐藏卡 |
| 服务端效果事件与卡池展示 | 每段效果实际完成/未发动/无效/跳过/失败 | S：`effect-result` 有终态；目标失效原因可在 `effect-failed/noop`；前端未完整利用 | P0；3A/3B/3C 全类扫描 |
| `PhasePlayback.vue`、`actionPresentation.ts` 等文字消费者 | 改文案后动画、阶段和日志仍匹配 | S：存在中文文本匹配和正则；不能全局替换权威审计文字 | P0；文本兼容与回放 |

## 已确认的记录缺口与待复现问题

以下 P 证据只针对构造事件。`projectLog` 输出和源事件应成对保留，实施前补具名测试，再用引擎场景核对。

| 输入事实 | 当前玩家输出 | 影响 |
| --- | --- | --- |
| `move` 文本含“从我方前排左格移动到我方后排左格” | `〈来源〉已移动` | 起止位置不能从记录回顾 |
| `continuous` 文本含数值由 6000→4000 | `〈来源〉：持续状态更新` 和 `1张` | 变化原因、目标和数值丢失 |
| 同组 `effect` + `effect-result(skipped)` | 只显示“发动主动效果” | 无对象跳过可被误看成已执行 |
| 同组 `effect` + 支付 2 士气 + `effect-result(negated)` | 显示“发动主动效果；主动效果被无效” | 已付费用不可回顾 |
| `attack` + `defense-invalid` 含具体原因 | 战斗摘要“未击破”，详情“抵挡/支援无效” | 防守方式和失败原因混称 |

源码进一步确认：`PLAYER_LOG_HIDDEN_TYPES` 包括 `effect-result`、失败、跳过及 `defense-invalid` 等；`projectGroupedAction` 处理无效、失败和放弃但未处理 skipped，也未保留全组费用、目标与每段结果。服务端 `L12GameEngine.EffectPresentations.cs` 已对无效/跳过/失败/放弃记录终态，对目标结算失败另记原因。需要基于权威事实扩展投影并维持私密信息过滤，不能简单把隐藏类型全部公开。

## 卡池初扫（候选分类，尚非受影响卡片结论）

全库文件 `cards.s1.json` 133 张、`cards.s2.json` 115 张、`cards.st.json` 76 张，共 324 张。对 `effect` 文本做正则扫描得到重叠分类：支付/费用 164、选择/目标 124、响应/抵挡/支援/无效 32、顺序/多段 54、持续/期限 141、区域变化 250、试炼/天灾 28。15 张未命中这些词组。文本命中不能代表能力结构、实际触发分支或最终修改清单；阶段 3 各批须结合原子能力、展示场景与引擎测试列出卡片 ID 和覆盖分支。

服务端 `TwelveLegions` 内按 `CreatePrompt(` 搜索共有 126 处调用，分布于 22 个 C# 文件；其中 `L12S2FactionEffects.cs` 29 处、`L12S1FactionEffects.cs` 15 处、`L12PromptsAndSetup.cs` 13 处，说明只修改通用弹框不能代表全卡提示已覆盖。该数字是调用点计数，不是 Prompt 型别数或独立玩家场景数。

## 阶段 1 展示合同（拟定）

### 一次操作的三个时点

1. **操作前**：展示触发原因、当前执行者、本步动作、数量与合法范围、已知关键数值、待付/已付费用、确认/取消/放弃的真实后果。若可收起，收起栏保留未完成任务；“关闭详情”不得等同放弃选择。
2. **结算中**：显示权威来源的当前段与顺序。未确定的将来结果写为条件，不预测成功；目标、费用与响应变化按引擎事件更新。
3. **结算后及记录**：区分卡牌规则原文、此次效果段的实际状态、实际费用、目标、公开数值、结果和失败原因。摘要可压缩，明细不得丢失；未知事实明确为“记录未提供”，不由当前棋盘或卡文补写。

### 字段与状态

优先扩展现有 `L12PromptPresentation`（`Title`、`Situation`、`Instruction`、`WaitingSummary`、`ChoiceConsequences`、`PaymentStatus`、`PaymentSummary`、`SubmissionConsequence`）和现有事件/投影元数据。候选项应有稳定 ID、可见名称、事件时位置、可选状态、具体不可选原因、选项后果；卡片式与文字式呈现同一字段。服务器只提供已确定事实，客户端负责格式和交互。

| 权威状态 | 面向玩家的含义 | 记录要求 |
| --- | --- | --- |
| pending | 等待选择/结算 | 不写成已发动或已生效 |
| resolved | 本段已完成 | 写实际对象与变化；不能仅重复卡文 |
| declined | 玩家选择不发动 | 明确未发动及已发生的前置费用（若有） |
| negated | 已发动但效果被无效 | 保留响应链和实际已付费用 |
| skipped | 无合法处理对象等原因跳过 | 明确本段跳过，后续独立段仍单独呈现 |
| failed | 已声明对象失效或结算条件不满足 | 记录权威原因及已完成的其他对象/段 |

状态必须按段、分支和实际执行顺序绑定，不能以一个总状态覆盖部分完成。优先沿用 `effectSceneId`、`effectAbilityId`、`effectSegmentId/index/count`、`effectBranchId/Label`、`effectResultStatus`、`playerLogGroupId` 等现有字段。必要的新字段在权威事件侧冻结，评估旧记录兼容后再加；不以事件相邻或同名卡猜关系。

### 术语与位置

玩家可见格位统一为“我方/对方＋前排/后排＋左格/中格/右格”，例如“我方前排左格”。这是 UI 定位辅助，不是规则术语；不把内部行列编号写进卡牌正式规则。按当前观看者确定我方/对方及左右映射；历史记录保存事件当时的位置、归属与公开身份。区分“回合玩家”“当前操作玩家”“效果发动者”，不能互代。区分“抵挡”“支援”“响应无效”“进攻中止”“未击破”；不能只写笼统防御成功/失败。

2026-09-29 格内短标补充：上句完整方位用于目标弹框、确认摘要、对局记录及格位的 `aria-label`，不能因视觉简化而缩短。对战垫双方前后排的空格位内部仅显示“左格／中格／右格”，自身所在半场和排位由战场布局表达；有卡格位仍由卡面占据，不叠加格位文字。正式对局、观战和回放共用该边界，不改变格位几何或权威位置语义。

### 响应目标的权威时序硬标准（新增）

可响应效果若需要选择目标，发动方必须先完成目标声明，将目标与当时可公开的身份、位置和数值冻结为权威上下文，然后才向对方开放响应窗口。响应者在决定响应前应看到已经选定的目标；不能只看到来源卡，不能在目标尚未知时要求响应。无目标效果沿用声明后响应；不发动、取消或未形成效果不得制造响应窗口。

响应窗口按接收者权限展示目标卡名、归属、“我方/对方＋前后排＋左中右格”和响应判断所需的公开数值。隐藏卡、私人手牌和牌序仍按现有投影遮蔽。响应结束后按冻结目标重新验证合法性；失效须记录实际原因，不自动改选目标，也不跳过独立后续段。目标选择事件必须先于响应事件写入日志与回放；重连后恢复的响应窗口须显示同一冻结选择。

阶段 2 的响应子批先改服务端响应/Prompt 的权威顺序与展示字段，再接入前端；不通过延迟弹框或客户端猜测掩饰时序。阶段 3B 核对逐能力目标声明、响应、结算重验和独立后续段；阶段 4C 核对日志、回放、重连、旧记录及四种接收者投影。具名场景必须覆盖单目标、多目标、目标中途失效、无目标、取消/不发动、重连、回放、玩家双方/观战/裁判隐私，以及桌面和七档移动视口。

### 可见性矩阵

| 信息 | 当前操作玩家 | 对方玩家 | 普通观战/回放 | 裁判授权视角 |
| --- | --- | --- | --- | --- |
| 公开来源、已公开目标、位置、公开支付及结果 | 可见 | 可见 | 随公开事件可见 | 可见 |
| 本人的私密候选、牌序、未公开卡身份、未提交选择 | 仅合法本人可见 | 不可见 | 不可见，除非历史上后来公开 | 按既有授权投影 |
| 等待中的私人 Prompt 内容 | 完整可见 | 仅等待摘要 | 仅公开等待摘要 | 按既有授权投影 |
| 历史记录详情 | 取事件时已授权冻结事实 | 同左，按接收者投影 | 不从当前状态补私密信息 | 按既有授权投影 |

`Models.cs` 已注明等待视角只能取得 `WaitingSummary`；本合同不能绕过房间投影给非操作方发送其余 Prompt 字段。裁判视角沿用现有授权，不因文案优化扩大权限。

### 代表性前/中/后样例（合同示意，非已复现卡牌）

若权威场景要求“支付 2 士气，选择一个我方前排军团”，操作前弹框应说明来源、可选范围、待支付 2 士气、确认是否立即提交及取消后果。卡片式选项显示“我方〈卡名〉（我方前排左格）”，同名卡按位置区分；不能选择时给具体原因。若实际支付后效果被无效，结算提示写“已支付 2 士气；本段效果被无效，目标没有发生预定变化”。记录摘要写来源与“效果被无效”；展开详情依事件顺序显示选择、支付、响应、终态。若某事实没有权威记录，详情标记“记录未提供”，不能臆造。

## 后续文件租约与最小实现边界（待 Main 回执）

首个完整竖切建议先做“操作弹框 → 权威效果结果 → 可展开记录”：前端 `PromptOverlay.vue`、`PromptCardCandidate.vue`、`BattleEventLog.vue`、`logViewModel.ts` 与对应测试；服务端先核对并仅在事实确实缺失时触碰 `L12PromptsAndSetup.cs`、`L12GameEngine.EffectPresentations.cs` 和模型/投影端口。其他弹框（`GameBoard.vue` 内嵌选择、`PlayerMat.vue`、`MasterOverlay.vue`、`GraveyardOverlay.vue`、`BattleUtilityDock.vue`、`GamePage.vue`）在对应 2/5 批进入，不一次改全。所有批次在同一隔离工作区顺序写入，不改主检出、不提交/推送/部署或维护 Main 台账。

移动端为每批硬门禁：桌面及 320–430px 竖屏、568×320 和 667×375 横屏；长说明、同名多选、禁用原因、展开记录可滚动可读；关键操作可触控；弹框打开/关闭/最小化与焦点返回正确；安全区不压住按钮；不侵入战场既有几何、不穿透点击。仅有静态 CSS 或既有身份截图不算通过。`PromptOverlay.vue` 的固定高度卡片及截断、`MobileBattleDock.css` 的嵌套 `overflow:hidden`、横屏可用高度是高风险检查点，不预先认定为失败。

## 阶段 0/1 收口补充：Prompt 全入口映射

本轮从同一工作区扫描 CreatePrompt：125 个实际调用点，另有 1 处方法声明；覆盖 34 个字面 kind 值和 5 个动态 kind 入口。逐调用点的文件/行、kind、文本表达式、候选、数量、continuation、私密标记、数据、叙事来源、等待摘要和消费方见 [Prompt 入口映射](l12/BATTLE-PLAYER-INFORMATION-PROMPT-MAP-20260928.tsv)。[盘点脚本](../scripts/audit-battle-player-information-stage01.mjs) 从源码生成该表，完整保留动态表达式，不伪造运行时值。

| 动态入口 | 上游来源与语义边界 |
| --- | --- |
| L12Actions.cs 的 CreateMappedChoicePrompt(kind) | 本文件调用为 option，以及 CreateOptionalGraveEntryCostPrompt 传入的 card-select、order；后者显式加入 cancel 和 allowCancel=true。最终取消后果仍由 continuation 决定。 |
| L12Disasters.cs 的三元 kind | choice 为 field 时是 targets，否则是 discard。 |
| L12PromptsAndSetup.cs 的 CreateAnonymousHandChoicePrompt(kind) | 当前调用者传 opponent-hand-card；匿名槽映射真实手牌，包装器创建前写入叙事，不向非操作者发送真实身份。 |
| L12PublicTriggerEffectPlans.cs 的 CreateResolutionChoicePrompt(kind) | 调用者传 enemy-legion、enemy-target、option、active-target、adjacent-slot；延迟包装器另传 hand-card、field-legion、card 等。action 和 continuation 决定结算。 |
| L12RuleKernelIntegration.cs 的 CreateActivationStepPrompt(promptKind) | 默认来自 step.Kind；声明步骤可改写为 slot、option、card、active-target、resource-payment、grave-card、hand-card 等。step.CancellationPolicy 决定是否附加 skip；SeparateChoice 为“取消整次发动”，其他适用场景为“不发动”。 |

服务端字段都经同一 CreatePrompt 路径冻结。标题优先取叙事 Title，其次 data.sourceName、文本冒号前缀、PromptKindTitle(kind)；情况取叙事 Situation、data.effectText、原文本；要求取叙事 Instruction 或 PromptInstruction(kind, min, max, uiPattern)；等待摘要取叙事 WaitingAction 或 PromptWaitingSummary(kind)。选项标签由 BuildPlayerChoiceLabels 生成；逐项后果、费用状态/摘要、提交后果仅在叙事提供权威值时非空。逐调用点 TSV 同时列出这些字段来源；47 个调用点直接在 CreatePrompt 实参中写 WithPromptNarrative，其余可能由包装器或调用前数据写入，也可能走回退，因此不能据此断言有 78 个缺文案。

PromptOverlay 默认先选择后按 min/max 确认；choiceMode=instant、纯“发动/不发动”以及 allowCancel=true 的 cancel 立即提交。客户端发送携带 promptId 与绑定字段的 resolvePrompt，实际结果由服务端 continuation 决定。skip、cancel、no、decline 等只有列在 ValidChoices 中才可选，名称不能代替真实后果。公开场面、同质资源、空位的合法候选可经 ApplyDirectBoardChoiceMode 转为 GameBoard 的 board-target、resource-selection、board-selection 或 board-slot 控件，PromptOverlay 对同一 promptId 被抑制；私有手牌、牌库、墓地仍使用卡图选择。等待视角只取得 WaitingSummary。

## 阶段 0/1 收口补充：结构化全卡映射

从实际 L12Catalog.AtomicEffects 导出：324 张卡、686 个稳定能力 ID、7 张无能力卡、757 个展示场景。导出时运行 EffectLifecycleInventoryTests.CommittedInventoryMatchesRuntimeDefinitions，测试通过并与仓库 [逐能力台账](l12/EFFECT-ABILITY-INVENTORY.md) 一致。每个具名卡牌、能力 ID、原子 Kind、执行模型、展示场景与段/分支、路由候选见 [结构化能力映射](l12/BATTLE-PLAYER-INFORMATION-ABILITY-MAP-20260928.tsv)。

| 结构家族 | 能力段 | 卡牌 | 分类依据 |
| --- | ---: | ---: | --- |
| 支付 | 210 | 174 | cost 原子或 cost 阶段 |
| 目标 | 172 | 149 | selection.target 原子 |
| 响应 | 20 | 20 | reaction 模型或反应触发 |
| 多段/分支 | 234 | 194 | 复合流程原子或展示场景段/分支 ID |
| 区域与位移 | 245 | 198 | 移区、位移、抽牌或洗牌原子 |
| 持续与期限 | 325 | 206 | 持续模型或 duration.apply 原子 |
| 天灾与试炼 | 57 | 38 | 卡牌类型、触发或推进试炼原子 |
| 其他 | 74 | 66 | 上述结构条件未命中，仍保留在完整清单 |

类别可重叠。这是为实施批次选样的结构化分类，不是改动覆盖率、真实触发率或生命周期通过证明。同卡同触发的不同能力不能借用另一能力的测试结论；先前卡文关键词命中数仅保留为探索线索。

## 阶段 0/1 收口补充：五类确定性记录证据

[日志投影基线脚本](../opcgpro-vue/scripts/audit-battle-player-information-log-gaps.mjs) 可重复运行，五个夹具逐一断言来源事件具有关键事实，而当前 projectLog 输出省略或混称。五条断言全部通过，证据级别 P；没有把构造事件冒充真实引擎对局。

| 夹具 | 输入事实 → 当前玩家输出 | 后续真实引擎依据 |
| --- | --- | --- |
| move-origin-destination | 我方前排左格→我方后排左格 → “已移动” | SuccessfulMoveUsesOneActionEventForLogMotionReplayAndPresentation 证实位移动作事件为同一来源；仍需核对事件时位置冻结 |
| continuous-target-delta-duration | 目标 6000→4000、本回合 → “持续状态更新1张” | 需核对各持续状态的权威事件字段 |
| skipped-segment | 第 1/2 段无对象跳过 → 仅“发动主动效果” | RealStructuredRowEffectCanResolveWithNoTargetsAsSkipped 证实服务端可产生真实 skipped 终态 |
| negated-paid-cost | 支付 2 士气后被无效 → 仅“发动主动效果；主动效果被无效” | 服务端已有无效结果测试；仍须按同组实际费用事件核对 |
| defense-invalid-reason | 支援军团离场不能支援 → “未击破”及“抵挡/支援无效” | DefenseUsesFrozenAttackValueAndKeepsSameColumnSupportSemantics 证实防守行为区别；仍需具体无效原因场景 |

已运行的引擎专项过滤包含 PromptNarrativeMatrixTests、PromptPresentationContractTests、RecipientPrivacyMatrixAdversarialTests、TrialProgressPrivacyTests，以及上述位移、跳过、支援具名用例，共 92/92 通过。另运行 RealWebSocketReconnectReplayAndRecipientProjectionsConvergeWithoutPrivateLeaks，1/1 通过。它们证明现有服务端状态、隐私和恢复基线；五类前端投影缺口仍待后续修复。

## 阶段 0/1 收口补充：旧记录、重连与四接收者

- LegacyCheckpointWithoutPresentationStillRestoresForClientSideNaturalLanguageFallback：旧检查点没有 Presentation 时仍能恢复，前端必须保留自然语言回退。
- LiveAndCheckpointViewsKeepEveryPrivateDomainInsideItsRecipientBoundary 与 LaterDisclosureDoesNotRewriteHistoricalFramesOrLeakAcrossPlayerReplay：核对玩家 0、玩家 1、普通观战、裁判四种快照；后来公开不会重写旧帧，玩家回放仍按接收者投影。
- 普通裁判快照不接收私人 Prompt 全文，只见白名单等待摘要；当前内部裁判视角可见双方未完成试炼等既有授权字段。SnapshotForGm 是另一个受控操作者视角，不能与普通裁判混同。
- JournalReplayPreservesOldCheckpointHashesAndRedactsBothOldAndNewProgress 与真实 WebSocket 重连专项：旧 Journal 哈希、恢复及接收者投影符合现状边界。

这些测试不能自动证明以后新增的日志详情字段安全。阶段 4C 必须针对新字段重跑四接收者、旧记录、重连和回放。旧记录从未冻结的历史位置、失败原因或费用须显示“记录未提供”，不得由当前棋盘或卡文补写。

## 阶段 0/1 收口补充：55/95 基线与新增移动场景

现有身份基线 report.json 含 55 个场景：3 档桌面 1280×720、1440×810、1920×1080 和 8 档移动 320×568、360×800、390×844、430×932、568×320、667×375、844×390、932×430，每档 5 种身份变化；接受记录为 95 张截图。它不能证明本次较长文案和记录详情合格。新增验收矩阵每行至少覆盖桌面、320/360/390/430 竖屏、568×320 与 667×375 横屏；844×390 和 932×430 是延伸回归。

| 新场景 ID | 样本 | 必查交互及边界 |
| --- | --- | --- |
| UI-01 | 长标题、情况、要求、费用、后果同屏 | 内容完整、可滚动、确认/取消可触控 |
| UI-02 | 同名目标在双方前后左右不同格 | 标签与实际位置一致，双方视角、观战及回放均不显示编号格位 |
| UI-03 | 卡片式选项有长名称、后果和不可选原因 | 全部可达，不依赖 hover |
| UI-04 | 多选、唯一候选、即时提交及选择后确认 | 文案与真实提交时点一致 |
| UI-05 | 弹框最小化、查看场面、再展开 | 待办保留，不挡计时/互动区，不穿透点击 |
| UI-06 | 卡牌详情、墓地或主宰弹框返回原 Prompt | 叠层、关闭、焦点恢复、底层几何不变 |
| UI-07 | 无效、跳过、部分完成的长解释 | 各段状态可读且不截断 |
| UI-08 | 费用、响应、目标、失败原因的记录明细 | 摘要和详情可触控、可滚动、顺序可回顾 |
| UI-09 | 移动起止位置与持续状态期限 | 不侵入战区、计时、记录和安全区 |
| UI-10 | 抵挡、支援、战斗中止的原因 | 区分方式，长原因完整可达 |
| UI-11 | 断线、等待、平局/投降与结算弹框 | 状态与真实结果一致，关闭和焦点正确 |
| UI-12 | 连续 resize、125%/150% 字体及刘海安全区 | 页面无横向溢出、无几何漂移，按钮整个命中区可点 |
| UI-13（阶段 2 响应子批） | 单目标、多目标、无目标、取消/不发动 | 发动方先选并冻结目标，再给对方响应；响应前看到获授权的目标身份、归属、格位和关键公开数值；无目标与未形成效果不伪造目标/窗口 |
| UI-14（阶段 3B/4C） | 目标响应后失效、独立后续段、重连与回放 | 目标不自动改选；失效原因与各段结果入记录；目标选择事件早于响应事件；重连后冻结目标一致；玩家双方、观战、裁判各守私密边界 |

后续各产品批次须保存截图、触控/滚动/关闭/焦点检查结果，以及 documentElement.scrollWidth 对照；上述矩阵只是验收设计，未提前标“通过”。

## 阶段 0/1 当前退出判断

入口、现有能力段、确定性日志缺口和现有隐私/兼容边界已有可重复清单与证据；产品行为未修改。仍无法从只读基线确认的规则依据，是动态 Prompt continuation 的逐分支实际后果、旧记录缺失事实能否从权威历史补充，以及全部能力的真实玩家理解效果。这些留给对应代码批次的具名引擎/浏览器场景和真人走查，不自行改规则或伪造历史。进入阶段 2 仍需 Main 回执。
