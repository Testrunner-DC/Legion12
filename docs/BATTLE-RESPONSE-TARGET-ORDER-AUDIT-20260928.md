# 响应窗口与目标选择时序：阶段 2 后续只读审计

证据级别 S（源码路径）；尚未把以下分类当作全卡规则结论或浏览器验收结果。审计不改响应、堆叠、目标或事件顺序。阶段 0/1 展示合同中的“先选目标，再响应”是后续实施硬标准。

| 共享入口 | 当前顺序与可见性 | 判断 |
| --- | --- | --- |
| `L12RuleKernelIntegration.cs` 的 `BeginPendingActivationSequence` / `ResolvePendingActivation` / `CompleteResolvedPendingActivation` | 选择步骤把 ID 写入 `DeclaredTargets` / `DeclaredValues`，公开战场对象另存 `ResponsePresentationTargetIds`，之后才由手牌打出、主动能力或响应效果提交入栈；提交时 `PushEffect` 将这些 ID 带入 StackItem | 对已接入这条声明链的目标，是“先声明后响应”；取消/声明失败不入栈，仍须逐能力检查步骤标记与事件记录 |
| `L12CompositeEffectPlans.cs` 的首段 `PublicTargetKeys`、分段 `DeclareAtSegmentStart` | 已声明的首段目标随 `CompositeFirstSegmentTargets` 入栈；独立后续段可在上一段结算后另行声明并取得自己的响应时点 | 已接入计划的首段/独立段具备目标先行结构；需核对每个能力是否漏标公开目标或误把后段并入前段响应 |
| `L12PublicTriggerEffectPlans.cs` 的触发候选及 `L12EnterPublicTriggerPlans.cs` 的多数 enter 计划 | `QueueOrPushTriggeredEffect` 对已登记公共计划先排触发候选、建立声明，完成后才入栈；例如吕布、神剑格拉墨等 enter 计划在 `Batch6JAEnterSteps` 预选目标 | 多数登记计划已有前置声明；未登记的传统直接 `PushEffect` 路径仍需逐类审计 |
| `L12AuthorityEvents.cs` 的已知公共对象 | `effect-ready` 将已存在的公开战场目标 ID 写进响应展示数据，再 `BeginResponseWindow`；`effect-hand-add` 刻意不公开手牌身份 | 已知对象能展示；无公开对象时只展示事件，不可推断隐藏身份 |
| `L12EnterPublicTriggerPlans.cs` 的 `morale-flip`、`theseus-flip`、`morale-flip-two`、`takasugi`（S02-0513、S02-0518、S02-0520、S01-0408） | 计划明确标记 `declaration-complete`，不预选目标；前 3 类在响应后调用 `PromptS2FlipMorale`，高杉晋作在抽牌后通过 `CreateResolutionChoicePrompt` 选择对方军团 | 已确认“先响应后选对象”的入口。高杉的抽牌是同段前置结果，改时须保留独立后续处理与失效事实，不能简单搬动弹框 |
| `L12PublicTriggerEffectPlans.cs` 的 `CreateResolutionChoicePrompt` 和 `CreateDelayedPublicResolutionPrompt` | 在已有 StackItem 的结算 continuation 中创建选择 Prompt；后者显式标记 `declarationTiming=post-hidden-reveal`，涉及抽后弃牌、私有顶牌选择、对手匿名手牌等 | 这一类当前响应时点早于处理对象选择。部分候选只有前段结算/隐藏公开后才存在，需按段及隐私逐案决定如何前置或拆成独立可响应段，不能提前暴露私牌 |
| `L12ResponsePresentation.cs` 的 `PublicResponseTargets` / `DescribeResponse` | 响应 Prompt 根据 StackItem.Targets 与 `responsePresentationTargetIds` 读取当下场面，显示已选目标卡名（隐藏时“盖伏卡牌”）及我方/对方格位；`responseTargetIds` 可供高亮 | 对有 ID 的入口已有目标文字；但“只显示来源”的入口可能是尚未声明、未写展示 ID，或目标非战场公开对象。没有冻结姓名/位置/关键公开数值的专门快照，重连时文字按当前状态重算；数值尚未呈现 |
| `L12RuleKernelIntegration.cs` 的声明事件与 `PushEffect` | 声明开始可有 `activation-declare`，`PushEffect` 后有 `effect-activation`/`effect-trigger` 与 `stack-push`；当前所查声明完成处只保存选中 ID，未见逐目标 `target-selected` 事件 | 不能据现有代码证明日志/Journal 已按“目标选择→响应”保存可回顾的独立事实；阶段 4C 必须补权威事件顺序和旧记录兼容 |

下一批从服务端权威顺序着手：列出每个响应 StackItem 在开放窗口前是否已冻结合法目标、公开展示快照和事件；对未声明的目标按其能力段与隐私条件决定时序。验证矩阵为单目标、多目标、目标中途失效、无目标、取消/不发动、重连、回放、玩家双方/普通观战/裁判投影，且每个 UI 场景覆盖桌面与 320/360/390/430 竖屏、568×320 和 667×375 横屏。目标失效必须保留原 ID 和真实原因，不自动改选，也不跳过独立后续段。

## 2026-09-29 Stage2B-1 实施结果

本节取代上表对 S01-0408、S02-0513、S02-0518、S02-0520 的“当前顺序”描述，但保留原表作为实施前基线。

- 已迁移：高杉晋作的对方公开战场军团，以及三张奥林匹斯登场效果的我方公开士气，均在第一个对手响应窗口之前选择并冻结公开展示事实。玩家响应使用“我方／对方”，普通观战与裁判的堆叠投影使用“玩家1／玩家2”；盖伏目标不保存或展示身份。目标离场、被覆盖或失去资格时只令对应处理失败，不自动改选。
- 必做／可选边界：高杉的抽1是必做，目标提示没有 skip/cancel；没有合法军团时仍抽1，有目标但结算前失效时仍抽1。三张翻士气效果仍可选择不发动；选择后只处理原实例，原实例失效不改选。
- 旧 V2：缺少新预声明字段的旧检查点继续走原结算期选择入口；缺少冻结展示事实时保持中性降级，不用当前场面补造历史选择。
- 暂不前移的同类：对方手牌弃牌、雷神之锤抽后弃手、梅林／特勒马科斯／埃涅阿斯的牌库查看检索、佐佐木小次郎的对方手牌涉及私区或新抽候选；神妙行军返还后的击杀、高天原抽牌后的位移与通用 optional-paid-effect 属于真正后续独立段；邪眼末日由受影响玩家在灾害结算时各自选择。S02-05C1 主动翻面、S02-05D1 神圣翻面和旧名称兼容路由不是本批四张登场首响应入口。
- 展示边界：本批新增的是服务端观战／裁判堆叠投影字段，不等于新增观战堆叠 UI。普通玩家响应弹框和场上精确士气实例高亮已接入；玩家主动打开墓地等区域仍是本地查看，不新增通知或对局日志。
- 验收：服务端相关 399/399、独立定向 74/74、UI 契约 350、Vue 类型检查通过；真实 Edge 覆盖 12 档尺寸，精确实例高亮、最小化／恢复／结束清理、页面无横向溢出及双方战场／资源区几何不位移均通过。代表截图位于 `artifacts/stage2b-response-highlights/`。
