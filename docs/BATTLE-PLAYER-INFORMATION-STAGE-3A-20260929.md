# 对战玩家信息阶段 3A：全池分类与验收边界

本批只处理普通主动／触发效果、可选发动、支付和对象选择在玩家操作前及本次实际结算后的解释。弹框与战场内嵌选择属于同一信息入口。结算规则、响应后的目标复验（3B）、多段／持续／天灾试炼等复杂效果（3C），以及对局记录、旧回放和新卡固定门禁均不在本批修改范围。

## 全池初筛

[逐卡清单](l12/BATTLE-PLAYER-INFORMATION-STAGE-3A-POOL-20260929.tsv)以本次 `origin/main@2be687e7` 的三份卡牌目录和[结构化能力映射](l12/BATTLE-PLAYER-INFORMATION-ABILITY-MAP-20260928.tsv)关联，逐一列出 324 张卡的 3A 入口数量、支付／对象／可选入口数量、能力 ID 和留给后续阶段的能力 ID。324 个卡牌 ID 均唯一；结构化能力共 686 条，其中 408 条属于主动、触发、战术或相应授予效果的初筛入口。

| 逐卡分类 | 张数 | 含义 |
| --- | ---: | --- |
| `3A-ordinary-present` | 132 | 至少一个 3A 入口不带响应、多段分支或天灾试炼标签；同卡其他能力仍可能留待后续。 |
| `3A-entry-with-deferred-semantics` | 124 | 存在主动／触发等入口，但相关能力同时包含响应、多段分支或天灾试炼标签；本批仅可修共用操作前信息，不以 3A 验收代替后续效果验收。 |
| `non-3A-only` | 61 | 结构化能力仅属于常驻、规则、响应等其他入口。 |
| `no-structured-ability` | 7 | 当前映射没有结构化能力条目；不据卡牌文字推断应有的运行时效果。 |

408 条初筛入口中，支付标签 147 条、对象标签 131 条、`control.optional` 188 条，三类可重叠；190 条未带响应、多段分支或天灾试炼标签。全池 280 条能力带至少一种后续阶段标签。以上是静态定位，不代表已经逐张验收，更不代表规则文本里出现相关字样就必有玩家 Prompt。实际改动清单须再以 `CreatePrompt` 调用、待决激活步骤、合法候选和具名引擎场景核对。

## 本批信息合同

操作前以权威 pending 步骤说明来源、当前选择、合法数量／范围和可选退出的真实后果。`pending` 与 `paid` 只从已明确的支付阶段或已完成费用事实产生；不能从卡文、“消耗”字样或当前棋盘猜测已支付。提交后只描述实际已发生的操作或权威结算终态；未提交的选择、私区卡牌与牌序不进入对手、普通观战或普通裁判视角。格位完整名称使用“我方／对方前排／后排左格／中格／右格”，数字只作 UI 辅助，不写成规则术语。玩家主动查看墓地是私人动作，不向对手广播，也不记作公开对局事件。

后续具名测试至少覆盖：可选发动与不发动、支付待选／完成／失败或取消、无合法对象与声明后失效（3A 只核对现有事实，3B 负责时序和重验）、私区选择、双方玩家与观战／裁判接收者边界。前端用真实操作核对桌面、320／360／390／430 竖屏及 568×320、667×375 横屏；长说明、收起再展开、按钮可触达和不会遮挡战场操作均为退出条件。

## 代表性前后样例与本批实现

| 场景 | 操作前 | 选择／结算后的可声称事实 |
| --- | --- | --- |
| 普通主动效果先选费用、后选对象 | 共用待决激活出口显示来源、当前步骤、选项数量；即使已选费用但声明尚未完成，仍标“待支付”。 | 取消时仅显示本次声明终止；费用和效果结果须看权威处理。不能把已选择资源写成“已支付”。 |
| 可选触发段与后续强制步骤 | “不执行本次可选段”与“取消整次发动声明”使用不同后果；不可取消的步骤不凭空出现取消按钮。 | 选择不执行可选段时，后续必须完成的步骤继续按权威流程处理，不能写成整张卡的效果都被放弃。 |
| 无合法对象或声明失效 | 没有合法候选时不展示无法完成的必选弹框；等待者仅看安全摘要。 | 由既有 `ability-rejected` 等权威事实报告未入栈／未支付，不根据当前棋盘补写历史对象。 |
| 混合士气／神力资源支付 | 共用支付 Prompt 明确“待支付”、份数和可用资源种类；确认后说明先核验所选资源。 | 仅在实际支付完成后的既有权威字段出现“已支付”；无效选择不得提前改写支付状态。 |

共享出口只补操作前信息：`L12RuleKernelIntegration.cs` 给普通主动／触发声明加阶段、待付费用、取消及提交后果；`L12MoralePayments.cs` 给通用资源支付提示加待付状态。已有结算状态和事件投影继续负责结果，未在本批制造新的结算推断。桌面 Prompt 的最小化按钮移到标题左侧，避开保留在右上角的“返回大厅／投降”控件；移动布局保持原位置。

## 费用选择元数据补审

[直接构造点清单](l12/BATTLE-PLAYER-INFORMATION-STAGE-3A-DIRECT-STEP-AUDIT-20260929.tsv)逐一定位 `L12ActivationSelectionStep` 的 77 个显式 `new` 构造点，列出类型、声明键、显式费用标记及审阅线索。线索列只用于人工定位，不把卡文、中文提示或 `DeclarationKey` 当作费用判据；费用归类以对应执行路径的支付事实为准。共用工厂返回的 `new()` 由下表单独核对，不混入 77 处直接构造点的数量。清单包含后续 3B／3C 的构造点，列出位置不表示本阶段验收了其结算语义。

| 共用构造路径 | 本阶段费用元数据审计 | 同类型效果对象的反证 |
| --- | --- | --- |
| `L12AttackPublicTriggerPlans`、`L12EnterPublicTriggerPlans` | 士气返还、神力、弃牌、墓地返牌、军团弃置等预付步骤均显式传 `isCostSelection`。 | `L12EnterPublicTriggerPlans` 的士气翻转目标保留非费用。 |
| `L12PublicTriggerEffectPlans` | 吕布、荆轲、刘备返还士气与祷告仪式、月读资源支付显式标为费用；共用 `GraveCostSelectionStep` 标为费用，`GraveEffectSelectionStep` 保留非费用。 | 花木兰等士气效果目标未标为费用。 |
| `L12PublicActiveEffectPlans`、`L12StarterTargetedEffectPlans`、`L12StarterRemainingEffects` | 伊西斯、孟婆、天照、萧何、荷鲁斯等弃牌／返还／资源成本显式标记；雅典娜、光之剑及色欲之罪的弃牌步骤也标记。 | 天照后续转活跃士气、雅典娜翻转士气、银臂努阿达转活跃士气保持效果对象。 |
| `L12S1FactionEffects`、`L12S2FactionEffects`、`L12S2RemainingEffects`、`L12RuleKernelIntegration` | 安卡神碑、黄金圣甲虫、阿尔忒弥斯、希波吕忒等弃牌及孙悟空返还、傲慢之罪附加费用依权威路径标记。 | 梅杰德、阿尔忒弥斯的后续效果目标不因同一声明中含费用而被标记。 |

完整的 3A `target-morale` 共用构造调用分组：13 处费用选择（进攻 2、登场 5、其他公开触发 3、余下初始卡 2、萧何 1）与 10 处效果目标（登场 1、其他公开触发 4、公开主动 1、余下初始卡 2、初始定向 1、通用触发 1）。后续多段复合构造里的 `target-morale` 留给 3C；独立响应构造留给 3B。分类据执行路径和显式元数据核验，同名 `Kind` 不能决定费用。真实卡流程回归覆盖萧何选择 `mode:none` 后声明结束、韩信士气费用待付、天照弃牌费用待付且后续士气效果目标仍是对象选择；合成边界测试覆盖同类型无费用标记时不得出现待付。

选 `mode:none` 的通用提交提示保持中性，再根据后续声明条件说明“不发动本次可选效果，本次声明到此结束”或“后续声明按步骤继续”。它不再把萧何的整个可选触发错误写成进入下一步；复杂条件无法确定时说明按已声明条件处理，不推断结算结果。

具名回归见 `Stage3AActivationPlayerInformationTests` 与 `PromptNarrativeMatrixTests.NonHomogeneousPaymentAndReturnExplainAmountsResourcesAndCancelConsequences`。浏览器脚本 `verify-battle-player-information-stage3a.mjs` 以服务端同型字段验证“待支付”、提交/取消后果、同名卡格位、最小化及七档视口；截图在 `artifacts/battle-player-information-stage3a/`，重点是 `1280x720-pending.png`、`568x320-payment.png`、`320x568-pending.png` 和各档 `*-minimized.png`。这套夹具验证客户端消费权威字段与操作可达性，不冒充 324 张卡的逐卡实战回放。

已有真实结算回归由完整规则集一并复跑，例如 `AtomicReviewBatch3RegressionTests.ActiveReturnAndRestCostsUseTheCommittedStateReceipt`、`HelaRejectsBeforeBasePaymentWhenItsColonCostCannotBePaid`、`ArtemisActiveLifecycleTests.PaymentSelectionCanBeCancelledBeforeDiscardingOrUsingTheAbility` 与 `AnkhSteleActiveLifecycleTests.ReadyModeRevalidatesItsTargetAfterResponseAndKeepsPaidCostsOnFailure`。这些测试验证费用成功／失败和对象失效的现有权威边界；本批没有改其结算或事件内容。

验证回执：Focused 规则 5802/5802、UI 合同 352 项通过；Batch 规则 5802/5802、UI 合同 352 项、卡图 42 项／324 张、Vue 类型检查、正式与测试前端各 464 模块通过。七档 3A 浏览器脚本及七档 Stage2 Prompt 复测均通过。第一次 Batch 因隔离缓存缺少 NuGet 包且联网受限，在测试还原前报 `NU1301`；复制本机已有包到本批缓存、禁用联网漏洞元数据查询后，完整 Batch 退出码 0。没有推送或部署。

补审回执：真实卡与同类型效果反证的专项回归 123/123；补审 Batch 的架构、卡池与完整规则 5804/5804 全部通过，原样日志见 `artifacts/battle-player-information-stage3a/supplemental-batch.log`。后端补修后重跑七档 3A 浏览器视口检查 7/7，通过；本轮未修改前端组件，补审 Batch 因此只执行其后端及通用门禁。补审只形成待 Main 验收的本地候选，未推送或部署。

## 当前未覆盖边界

本清单按能力结构分类，不能替代运行时分支覆盖。3B 将验收声明目标后给响应者的信息、失效目标和独立后续段；3C 将验收多段、持续、区域变化及特殊流程；记录阶段将核对事件时冻结事实、旧回放缺字段和因果回顾。固定新卡门禁只在整个对战玩家信息任务完成并验收现有卡池后建立。
