# 弹框叙事全量盘点与实施矩阵（B0）

- 盘点日期：2026-09-27
- 权威基线：`origin/main@9042dbdca5ecaa662aaa9030ee9b317c6c9267a4`
- 工作树：`tmp/prompt-presentation-b-20260927`
- 分支：`codex/prompt-presentation-b-20260927`
- 范围：只读盘点；本文件是唯一写入。未修改产品、测试、脚本、冻结清单、Git 远端或部署状态。
- 架构锁：延伸 P0（唯一规则写入口）、P2（按接收者投影）、P3（检查点/回放兼容）；不改变规则语义、投影结构或隐藏信息边界。

## 1. 结论

当前产品只有一个 `L12Prompt` 实例化点：`L12PromptsAndSetup.cs` 中的统一 `CreatePrompt` 工厂。全库 125 个产品调用入口全部流经该工厂，因此 A 批新增的 `Presentation` 已覆盖所有**新创建的 L12Prompt**。不存在绕过工厂直接构造产品 Prompt 的第二入口。

全量迁移的主要缺口在叙事质量，而非字段覆盖：

1. `Situation` 优先取堆叠项的 `effectText`，否则从原 `Text` 拆分。多步骤效果经常显示整段卡效，无法准确说明“当前走到哪一步、刚发生了什么”。
2. `Instruction` 主要按 `kind` 生成通用句，不能完整描述费用、数量、区域、取消规则、选择顺序与确认后行为。
3. `ChoiceConsequences` 目前与 `ChoiceLabels` 共用同一字典。它保证按钮不再被前端擅自改写，但多数入口仍只有“发动／不发动／选择某卡”等标签，没有“选择后将发生什么”的后果说明。
4. 调度、抵挡/支援、GM 放置、主宰/阵营/场上主动能力选择不全是 `L12Prompt`，仍由前端专用分支写死文案。
5. `PromptOverlay` 已优先读取权威标题、情况、指令和等待摘要；`GameBoard` 的内联目标/位置/资源面仍保留硬编码确认文案，并且选择文案优先级仍是 `ChoiceLabels` 高于 `ChoiceConsequences`。

建议按 Main 已批准的顺序实施：B1 先迁移服务端叙事生产，B2 再统一前端消费者和真实浏览器矩阵。C/D 单独处理对局记录，不能混入 B。

## 2. 扫描方法与精确数量

使用的只读查询：

```text
rg -n --glob '*.cs' 'CreatePrompt\(' 服务端WebSocket/TwelveLegions
rg --count-matches --glob '*.cs' 'CreatePrompt\(' 服务端WebSocket/TwelveLegions
rg -n --glob '*.cs' 'new\s+L12Prompt\b|PendingPrompts\.Add\s*\(' 服务端WebSocket/TwelveLegions
rg -n --glob '*.vue' --glob '*.ts' 'waitingPrompt|resolvePrompt|prompt\.text|presentation|pendingDefense' opcgpro-vue/src/l12
rg -n --glob '*.mjs' --glob '*.cs' 'PromptOverlay|waitingSummary|PromptPresentation|mulligan|response|resource-payment' opcgpro-vue/scripts TwelveLegions.Tests
```

结果：

- `CreatePrompt` 词法引用共 126 处：1 处工厂定义、125 处产品调用。
- 125 个调用分布于 22 个服务端 partial 文件。
- 120 个调用使用 34 种字面量 `kind`；5 个调用使用动态/条件 kind（3 个通用 `kind`、1 个 `promptKind`、1 个天灾条件 kind）。
- `new L12Prompt` 仅 1 处，即统一工厂内部。
- 检查点可以恢复 `Presentation = null` 的旧 Prompt；这不是第二生产入口。

### 2.1 125 个产品调用入口按文件分布

| 服务端文件 | 调用数 | 主要范围 |
|---|---:|---|
| `L12Actions.cs` | 6 | 通用映射入口、登场方式、晋升、致命替代、资源支付 |
| `L12ActiveAbilities.cs` | 4 | 主动能力的手牌/牌库/模式选择 |
| `L12AuthorityEvents.cs` | 1 | 抵挡/支援附加弃牌 |
| `L12CardEffects.cs` | 5 | 基础卡效、议和谈判 |
| `L12Disasters.cs` | 9 | 天灾弃牌、目标、顺序、持续模式 |
| `L12EffectContinuations.cs` | 3 | 多段效果后续位置/顺序/顶部牌选择 |
| `L12GameEngine.cs` | 3 | 先后攻、手牌上限、致命替代 |
| `L12EnterPublicTriggerPlans.cs` | 6 | 登场时公开触发的检索/弃牌/可选登场 |
| `L12LethalReplacements.cs` | 1 | 通用致命替代 |
| `L12LibraryPlacementTransactions.cs` | 1 | 牌库顶部/底部排列事务 |
| `L12MoralePayments.cs` | 1 | 通用费用支付 |
| `L12MoraleReturns.cs` | 3 | 士气返还、展示牌去向、登场位置 |
| `L12PostResolutionGeneratedEffects.cs` | 1 | 结算后生成的免费主宰效果 |
| `L12PromptsAndSetup.cs` | 12 | 准备、试炼、匿名手牌、天灾、响应、响应费用 |
| `L12PublicTriggerEffectPlans.cs` | 1 | 公开触发通用分派入口 |
| `L12RuleKernelIntegration.cs` | 2 | 声明式激活步骤、同一时点触发顺序 |
| `L12S1ExtendedEffects.cs` | 11 | S1 扩展卡效 |
| `L12S1FactionEffects.cs` | 15 | S1 阵营卡效 |
| `L12S2CounterTactics.cs` | 2 | S2 反击战术费用/弃牌 |
| `L12S2UniversalEffects.cs` | 7 | S2 通用卡、同意/拒绝与天灾查看 |
| `L12S2FactionEffects.cs` | 29 | S2 阵营卡效 |
| `L12TrialCompletionTriggerPlans.cs` | 2 | 试炼完成后的牌库检索 |
| **合计** | **125** | 所有调用均进入统一工厂 |

34 种字面量 kind 为：`card`、`cards`、`covered-counter`、`disaster-ban`、`disaster-pick`、`disaster-reveal`、`discard`、`discard-cost`、`discard-or-decline`、`friendly-target`、`hand-card`、`hand-cards`、`information-confirm`、`initiative`、`library-search`、`opponent-confirm`、`option`、`optional`、`optional-card`、`optional-cards`、`optional-target`、`optional-targets`、`order`、`resource-payment`、`resource-return`、`response`、`response-target`、`search`、`slot`、`target`、`target-morale`、`targets`、`trial-order`、`trigger-order`。

`L12RuleKernelIntegration.cs` 的 `promptKind` 和两个通用 `kind` 网关会承接声明式步骤，因此运行时场景多于 34 种字面量。匿名对手手牌步骤会被统一转换成 `opponent-hand-card`，候选使用随机槽位，不投影真实实例。

## 3. 权威生产与投影链

```text
125 个 CreatePrompt 调用 / 声明式步骤
  → L12PlayerFacingText.Naturalize
  → ApplySharedPromptPresentation（来源、卡图、choiceMode、effect-decision）
  → BuildPlayerChoiceLabels
  → BuildPromptPresentation
  → L12Prompt.Presentation
  → SnapshotForInternal
      操作者：完整 Prompt + Presentation
      对手：自己的 Prompt 完整；别人的 Prompt 仅 waitingSummary
      普通观战：无完整 Prompt；仅 waitingSummary
      裁判：不接收任一玩家的完整 Prompt，仅接收安全 waitingSummary；可查看全部天灾，不查看双方手牌
      GM：全部 Prompt + Presentation，不生成 waitingPrompt；可查看双方手牌
  → PromptOverlay / GameBoard 内联选择面
```

### 3.1 当前字段来源

| Presentation 字段 | 当前权威来源 | 现状 | 迁移要求 |
|---|---|---|---|
| `Title` | `data.sourceName` → `Text` 冒号前缀 → kind 通用标题 | 来源卡存在时基本可读；无来源、多步骤或无冒号文本会退化成“操作确认” | 每个动态步骤明确来源名称；系统流程使用稳定自然语言标题 |
| `Situation` | `data.effectText` → `Text` 冒号后/全文 | 堆叠流程常显示整段卡效；非堆叠流程把“请选择”当情况 | 明确描述触发、已支付/已发生事项与当前步骤，不把操作指令混入情况 |
| `Instruction` | `PromptInstruction(kind, min, max)` | 多为通用模板，费用、目标限制、取消含义和选择顺序不足 | 每入口生成可执行自然语言；数量、区域、合法性、确认行为必须清楚 |
| `WaitingSummary` | 玩家名 + kind 级动作 | 安全且可读；少数动态 kind 退化成“正在完成当前操作” | 保持无来源名、无候选、无私密区域细节；补齐安全动作族即可 |
| `ChoiceConsequences` | 与 `ChoiceLabels` 同一字典实例 | 能保持后加取消选项同步，但通常只是按钮标签 | 保留短按钮标签；另给可选后果句。不得把未公开身份或牌序写入后果 |

## 4. 五视角可见性矩阵

| 接收者 | 完整 Prompt | Presentation | 候选/ChoiceLabels | WaitingSummary | 手牌/牌库/未公开来源 |
|---|---|---|---|---|---|
| 玩家 0 | 仅玩家 0 的 PendingPrompt | 完整 | 可见自己的合法候选 | 玩家 1 操作时仅见安全摘要 | 只按玩家 0 的规则权限查看自己的私密区域 |
| 玩家 1 | 仅玩家 1 的 PendingPrompt | 完整 | 可见自己的合法候选 | 玩家 0 操作时仅见安全摘要 | 只按玩家 1 的规则权限查看自己的私密区域 |
| 普通观战 | 不投影任何完整 Prompt | 不投影完整 Presentation | 不可见 | 当前等待者与安全动作 | 不得泄露任何私密内容 |
| 裁判 | 不投影任何完整 Prompt | 不投影完整 Presentation | 不可见 | 当前等待者与安全动作 | 可查看全部天灾；不可查看双方手牌、归属玩家的完整提示与候选 |
| GM | `revealAllHands` 下全部 Prompt | 全部完整 | 全部可见 | `waitingPrompt = null` | 可查看双方手牌和完整提示，用于控制与验收 |

对应实现位于 `L12GameEngine.cs` 的 `SnapshotForInternal`：玩家只取 `prompt.PlayerIndex == viewer` 的完整 Prompt；普通观战和裁判都只接收安全等待投影。`SnapshotForReferee()` 只额外公开全部天灾，不开启 `revealAllHands`；`SnapshotForGm(viewer)` 才开启 `revealAllHands`，因此只有 GM 接收全部 Prompt。等待投影只包含 `playerIndex`、`playerName`、`kind`、`waitingSummary`。`IsPrivate` 当前不扩大投影范围。

现有强回归：

- `PromptPresentationContractTests`：完整合同、玩家 0、玩家 1、观战、裁判、GM 五视角隐私矩阵、检查点恢复、旧检查点降级、天灾/试炼归属。
- `PromptCardPresentationSnapshotTests`：手牌、墓地、试炼、触发顺序、预览卡等投影。
- `PromptSemanticsRegressionTests`、`PublicTriggerPromptPresentationRegressionTests`：共享提示语义。
- `verify-batch263-prompts.mjs`、`check-l12-prompt-presentation.mjs`、`check-ui-contracts.mjs`：真实组件和静态合同。

## 5. 全量提示面实施矩阵

下表中的“操作者/等待者”均受第 4 节投影矩阵约束。代表性入口只用于定位；同族必须按 125 个入口全量扫描，不能只改示例卡。

| 提示族 | 服务端生产入口与代表实例 | 是否走统一 CreatePrompt / 已有 Presentation | 前端消费者与当前自行改写 | 当前叙事来源 | 隐藏信息风险 | 缺口与建议批次 |
|---|---|---|---|---|---|---|
| 准备/先后攻 | `L12GameEngine.cs:136`；`L12PromptsAndSetup.cs:83,87` | 是 / 是 | `PromptOverlay` 为先后攻绘制专用骰子；标题/指令取 Presentation | 系统 Text；按钮由 common labels | 低；等待摘要只能说明正在决定先后攻 | Situation 应说明掷骰结果和谁取得决定权；两个开局可选效果应说明来源、执行后果。B1-core，B2-overlay |
| 登场时 | `L12EnterPublicTriggerPlans.cs` 6 处；`L12S2FactionEffects.cs:267,294,322...`；声明式 public-trigger 网关 | 是 / 是 | `PromptOverlay` 将 option/optional 识别为 effect-decision；卡牌候选使用卡图条 | stack `sourceName/effectText`，否则 Text 冒号拆分 | 中高；牌库/手牌候选只可给操作者 | Situation 必须写“〈卡名〉登场时效果已触发”；Instruction 只描述当前步骤；发动/不发动后果需要独立句。B1-card |
| 主动军团/主宰/阵营 | `L12ActiveAbilities.cs`；`L12PostResolutionGeneratedEffects.cs`；`L12RuleKernelIntegration.cs` | 后续选择是 / 是；首次能力选择可能不走 Prompt | `MasterOverlay`、`PlayerMat` 阵营/卡牌能力弹框直接显示能力 label；点击后才进入 Prompt | 玩家视图的 ability labels + 后续 Prompt | 中；禁用理由可见但不能泄露尚未公开候选 | 首次弹框缺 Situation/Instruction/后果结构；后续步骤要与首次选择保持同一叙事。B1-core/card + B2-special |
| 主动战术 | `L12CardEffects.cs`、`L12S1ExtendedEffects.cs`、`L12S2UniversalEffects.cs`、`L12S2FactionEffects.cs`，以及 pending-activation 网关 | 是 / 是 | `PromptOverlay`；目标/支付可能被 `GameBoard` 抑制后改用内联面 | stack effectText 或入口 Text | 中高；手牌来源、牌库结果与未公开战术不可进等待摘要 | 弹框需说明已打出的战术、费用是否已付、当前步骤和取消是否撤销整次发动。B1-card，B2-inline |
| 反击/响应/无效 | `L12PromptsAndSetup.cs:1605,1895,1931,1944`；`L12S2CounterTactics.cs`；`L12AuthorityEvents.cs` | 是 / 是 | `PromptOverlay` 额外拼接 `responseContext`；`response-target` 有专用标签；棋盘持续高亮目标 | responseText、bound response context、choice labels | **高**；响应可用性、盖伏来源和可选响应牌不可泄露给等待方 | 权威 Situation 应包含“正在响应什么”；Instruction 说明响应/不响应、登场位置和费用；无效对象与已付费用需可读。前端不应再次自行拼接或改写。B1-core/card，B2-overlay |
| 费用支付/返还 | `L12MoralePayments.cs`、`L12MoraleReturns.cs:27`、`L12Actions.cs:211`、`discard-cost`、声明式 cost steps | 是 / 是 | 桌面 `GameBoard` 资源条、移动士气弹框；确认按钮按 kind 写死“确认支付/返还/选择” | promptText + generic Instruction；选择标签由资源映射 | 中；只能暴露操作者可支付资源，等待方不得看到数量构成 | Situation 区分“费用尚待支付/已经支付”；Instruction 写清数量、可用资源、取消后果；按钮从服务端动作语义生成。B1-core，B2-inline |
| 目标与区域选择 | `target(s)`、`optional-target(s)`、`friendly-target`、`slot`，以及 `ApplyDirectBoardChoiceMode` | 是 / 是 | `GameBoard` 抑制主弹框，直接显示 instruction、计数、绿色高亮和硬编码确认按钮 | stack effectText + generic target/slot instruction | 中；盖伏卡可暴露位置但不可暴露身份；对手合法候选本身只给操作者 | Situation 必须保留效果来源；Instruction 说明我方/对方、前/后排、数量和确认动作；选择后果不能含隐藏身份。B1-core/card，B2-inline |
| 私密手牌/牌库/检索 | `hand-card(s)`、`discard`、`search/library-search`、`optional-card(s)`；匿名手牌网关 | 是 / 是 | `PromptOverlay` 卡图条、排列工作区；匿名槽位用卡背 | Text + 卡元数据；匿名手牌随机槽位 | **最高**；真实实例、牌序、未公开卡名、候选集合绝不能进入 waitingSummary/观战投影 | owner Situation/Instruction 可详细；WaitingSummary 只能说“正在完成卡牌选择”。ChoiceConsequences 只能描述公开动作。B1-card + 隐私合同 |
| 墓地/公开区 | `grave-card` 扩展、S1/S2 墓地 `optional-card(s)`、`covered-counter` | 是 / 是 | `PromptOverlay` 显示完整公开墓地并灰置非法项；棋盘公开区可内联高亮 | Text、`sourceZone=graveyard`、完整公开卡元数据 | 低中；墓地公开，但“哪些是合法候选”仍只给操作者；盖伏反击身份需遵守公开状态 | 文案需区分“查看完整墓地”和“当前可选对象”，说明选中后回手/登场/叠放/弃置。B1-card，B2-overlay |
| 天灾 | `L12PromptsAndSetup.cs:1325,1327,1351,1375`；`L12Disasters.cs` 9 处；祷告仪式信息确认 | 是 / 是 | `PromptOverlay` 有准备历史和信息确认专用布局 | Text、天灾 stack effectText、卡图历史 | 高；未揭示天灾只显示卡背；私看下一张天灾只给操作者 | **天灾值是公共数值，无我方/对方归属。** Situation 应区分禁用、选用、随机公开、私看、触发和数值变化；等待摘要不得暴露未公开天灾。B1-core/disaster，B2-overlay |
| 试炼 | `trial-order`；试炼完成后的 2 个检索入口；动态 mode:trial | 是 / 是 | `PromptOverlay` 单行排序/卡牌候选 | Text、卡元数据、common `trial`/`mode:trial` 标签 | 中；试炼区公开规则保持现状，私密检索仍只给操作者 | **试炼有玩家归属，文本必须保留“我方/对方试炼进度”。** 排序 Instruction 需说明顺序含义；推进选项需说明归属和变化。B1-core/card，B2-overlay |
| 调度 | `L12Phase.Mulligan` + `HandleMulligan`，不创建 L12Prompt | 否 / 无 | `PromptOverlay` 专用分支直接写“选择需要调度的起始手牌”“等待对手完成调度” | phase + hand + `mulliganDone` | **高**；等待者只能知道对方尚未完成，不能看到换牌数或卡牌 | 应建立与 Presentation 同结构的客户端视图模型或服务端安全准备摘要；操作者说明“选中的牌将洗回并补至原手牌数”。B2-special；如需服务端字段另申请 B1-core 租约 |
| 抵挡/支援 | `L12PendingDefense`，不是 L12Prompt；额外费用才进入 Prompt | 否 / 无 | `GameBoard` + `GameActions` 专用面；攻击者等待文案由 `PromptOverlay` 本地生成 | pendingDefense stage/target + 前端固定按钮 | 中高；支援/抵挡合法牌只给操作者，攻击者只看安全等待 | 统一自然语言：当前进攻者/目标、可抵挡或支援、选择数量、确认/不行动后果；最小化条保持同义。B2-special，必要服务端公共摘要另申请 B1-core |
| GM/沙盒 | `SnapshotForGm` 提供全部 Prompt；沙盒用 `sandboxAction` 代表当前 Prompt 玩家；GM 放置由本地 `gmPlacement` | 游戏 Prompt 是 / 是；GM 放置不是 | `GmPanel`、`GameBoard` GM 放置条、`PromptOverlay` sandbox actor | GM 控件固定文案 + 完整 GM 快照 | GM 在授权控制场景拥有完整权限；必须防止 GM 文案或状态流入普通玩家快照。本行不得作为 referee 权限证据 | GM/沙盒应复用权威叙事，但保留“当前代操作玩家”；GM 放置写清目标玩家与卡牌。B2-special |
| 旧检查点降级 | 恢复的 PendingPrompt 可没有 Presentation | 不重新生产 / `Presentation=null` | `PromptOverlay` 的 `legacyPromptTitle/Situation/Instruction/WaitingSummary` | 旧 Text/Data/kind | 高；等待降级只能用 playerName + kind 映射，不能显示旧 Text | 保留兼容；新增 kind 必须有安全降级；恢复后不能补写可能泄露的 owner 文本给等待方。B1/B2 合同 |
| 等待/最小化提示 | `waitingPrompt` 安全投影；Prompt/战斗/资源/GM 各自最小化条 | 等待摘要是；多个最小化条不是 | `PromptOverlay`、`GameBoard` 的恢复按钮与战斗/资源/GM 条 | waitingSummary；其余为本地固定字符串 | **高**；最小化状态仍不能出现私密来源、候选或数量 | 等待只说谁在进行何类公开动作；操作者最小化条可显示权威标题；各专用面恢复文案要与展开态一致。B2-inline/special |

## 6. 前端消费者清单

### 6.1 已消费 Presentation 的权威路径

1. `PromptOverlay.vue`
   - `Title`：弹框标题与操作者最小化条。
   - `Situation`：正文；效果决定时还会本地追加 `responseContext`。
   - `Instruction`：正文指令与普通确认区。
   - `ChoiceConsequences`：只有 `ChoiceLabels` 不可用时才用于主标签；与主标签不同才显示附加小字。
   - `WaitingSummary`：对手/观战等待面与等待最小化条。
2. `GameBoard.vue`
   - 桌面棋盘目标、棋盘位置、资源支付/返还与移动士气面读取 `Instruction`。
   - skip/cancel 优先取 `ChoiceLabels`，其次才取 `ChoiceConsequences`。

### 6.2 合同外或仍自行改写的专用路径

- `PromptOverlay`：先后攻骰子、天灾历史、卡牌排列、调度、抵挡等待、kind 小标题、legacy 降级。
- `GameBoard`：棋盘目标/位置/资源确认按钮、选择计数、移动士气标题、抵挡/支援、GM 放置与各类恢复按钮。
- `GameActions.vue`：确认抵挡/不抵挡、确认支援/不支援。
- `MasterOverlay.vue`：主宰主动能力列表和禁用原因。
- `PlayerMat.vue`：阵营能力与场上卡牌主动能力列表。
- `GmPanel.vue`：GM 区域、放置与测试进攻入口；沙盒沿用同一对局面并用 `sandboxAction` 代操作。

仓库另有 `opcgpro-vue/src/components/game/PromptOverlay.vue` 的旧 GrandUMI 页面，只渲染 `prompt.text`，不属于当前 `src/l12` 对战链。B2 应用路由/构建合同再次确认它未被 L12 页面引用，避免误改退休路径。

## 7. 风险分级

### R0 阻断级：隐藏信息越界

- 私密手牌、牌库顺序、检索候选、匿名对手手牌真实实例、未揭示天灾、盖伏响应来源进入 `WaitingSummary`、普通对手或观战快照。
- 把完整 Presentation 投影给非操作者，或让旧检查点降级使用 `prompt.text` 生成等待文案。
- 防线：玩家 0、玩家 1、普通观战、裁判、GM 五视角结构断言 + 序列化字段白名单 + 真实 WebSocket/浏览器等待态；任何失败阻断同步。

### R1 高风险：服务端与前端叙事冲突

- `ChoiceConsequences` 与 `ChoiceLabels` 语义未分开；前端继续优先旧标签或写死确认按钮。
- stack `effectText` 覆盖当前步骤，导致标题正确但正文说的是整张卡，不知道现在要做什么。
- 响应上下文由前端二次拼接，可能重复、错位或在不同消费者中缺失。
- 防线：代表性登场时、主动战术、响应/无效、支付、目标多步骤快照 + 浏览器断言。

### R2 中风险：同族文案不一致

- 相同语义散落在 22 个 partial 文件；只修示例卡会继续出现“侍从骑士/加拉哈德式”的显示差异。
- 天灾值误加“我方/对方”；试炼进度反而丢失归属。
- 专用弹框展开态、最小化条、移动端恢复按钮使用不同措辞。
- 防线：共享叙事构造器、全池扫描合同、天灾/试炼专用断言和多视口浏览器矩阵。

### R3 低风险：兼容与可读性退化

- 旧检查点缺 Presentation、未知 kind、空来源名、0 选项信息确认退化成内部词或生硬计数句。
- 防线：旧存档 fixture、内部协议词黑名单、空/单/多选自然语言表格测试。

## 8. B1 建议租约：服务端叙事生产迁移

B1 不改变 `SnapshotForInternal` 的投影结构，不改规则、费用、合法目标、响应窗口、结算顺序、随机性、卡效或日志。建议拆成两个串行、互不重叠的文件组；B1-core 先稳定共享构造器，B1-card 再迁移卡效入口。

### B1-core｜共享构造器、系统流程与动态网关

建议精确租约：

- `服务端WebSocket/TwelveLegions/L12PromptsAndSetup.cs`
- `服务端WebSocket/TwelveLegions/L12GameEngine.cs`
- `服务端WebSocket/TwelveLegions/L12Actions.cs`
- `服务端WebSocket/TwelveLegions/L12MoralePayments.cs`
- `服务端WebSocket/TwelveLegions/L12MoraleReturns.cs`
- `服务端WebSocket/TwelveLegions/L12RuleKernelIntegration.cs`
- `服务端WebSocket/TwelveLegions/L12LibraryPlacementTransactions.cs`
- `服务端WebSocket/TwelveLegions/L12LethalReplacements.cs`
- `TwelveLegions.Tests/PromptPresentationContractTests.cs`
- 建议新增 `TwelveLegions.Tests/PromptNarrativeMatrixTests.cs`
- 如冻结合同需要更新：`TwelveLegions.Tests/Fixtures/architecture-p1-contracts.json`，仅允许 Main 指定的 prompt surface 哈希变化

实施内容：为 Presentation 引入显式、公开安全的叙事输入或构造器；分离短按钮标签与选择后果；动态 activation/public-trigger 步骤传入当前步骤 Situation/Instruction；补准备、响应、费用、目标、匿名手牌、旧检查点与五视角合同。

### B1-card｜卡效、天灾与试炼生产入口全量迁移

建议精确租约：

- `服务端WebSocket/TwelveLegions/L12ActiveAbilities.cs`
- `服务端WebSocket/TwelveLegions/L12AuthorityEvents.cs`
- `服务端WebSocket/TwelveLegions/L12CardEffects.cs`
- `服务端WebSocket/TwelveLegions/L12Disasters.cs`
- `服务端WebSocket/TwelveLegions/L12EffectContinuations.cs`
- `服务端WebSocket/TwelveLegions/L12EnterPublicTriggerPlans.cs`
- `服务端WebSocket/TwelveLegions/L12PostResolutionGeneratedEffects.cs`
- `服务端WebSocket/TwelveLegions/L12PublicTriggerEffectPlans.cs`
- `服务端WebSocket/TwelveLegions/L12S1ExtendedEffects.cs`
- `服务端WebSocket/TwelveLegions/L12S1FactionEffects.cs`
- `服务端WebSocket/TwelveLegions/L12S2CounterTactics.cs`
- `服务端WebSocket/TwelveLegions/L12S2UniversalEffects.cs`
- `服务端WebSocket/TwelveLegions/L12S2FactionEffects.cs`
- `服务端WebSocket/TwelveLegions/L12TrialCompletionTriggerPlans.cs`
- B1-core 新增的叙事矩阵测试文件（仅追加卡效用例）

实施内容：按 125 入口清单逐族迁移，优先覆盖登场时、主动战术、响应/无效、私密区、天灾和试炼；同语义使用共享模板，入口只提供公开上下文。天灾文本禁止我方/对方归属；试炼文本必须保留我方/对方归属。

若 Main 要求更小租约，B1-card 可再拆成：

1. `disaster/trial`：`L12Disasters.cs`、`L12TrialCompletionTriggerPlans.cs`；
2. `S1`：`L12ActiveAbilities.cs`、`L12S1ExtendedEffects.cs`、`L12S1FactionEffects.cs`；
3. `S2`：`L12S2CounterTactics.cs`、`L12S2UniversalEffects.cs`、`L12S2FactionEffects.cs`；
4. `shared-trigger`：其余 CardEffects/AuthorityEvents/EffectContinuations/Enter/Public/Post 文件。

## 9. B2 建议租约：前端消费者与真实浏览器矩阵

建议精确租约：

- `opcgpro-vue/src/l12/types.ts`（仅当 B1 合同字段变化）
- `opcgpro-vue/src/l12/game/PromptOverlay.vue`
- `opcgpro-vue/src/l12/game/GameBoard.vue`
- `opcgpro-vue/src/l12/game/GameActions.vue`
- `opcgpro-vue/src/l12/game/MasterOverlay.vue`
- `opcgpro-vue/src/l12/game/PlayerMat.vue`
- `opcgpro-vue/src/l12/game/GmPanel.vue`
- `opcgpro-vue/scripts/check-l12-prompt-presentation.mjs`
- `opcgpro-vue/scripts/check-ui-contracts.mjs`
- `opcgpro-vue/scripts/verify-batch263-prompts.mjs`
- 建议新增 `opcgpro-vue/scripts/verify-prompt-narrative-matrix.mjs`

实施内容：

1. 所有展开、内联、移动、最小化和恢复面统一消费权威叙事；前端只负责布局，不重写语义。
2. `ChoiceLabels` 作为短按钮文本，`ChoiceConsequences` 作为可选后果说明；确认/取消/不发动文案由服务端动作语义决定。
3. 调度、抵挡/支援、主宰/阵营/卡牌主动能力、GM 放置建立同样的“发生了什么/要做什么/选择后果”阅读顺序。
4. 真实浏览器至少覆盖桌面 1280、移动横屏 760、窄屏 390，并覆盖展开→最小化→恢复。
5. 五接收者场景：玩家 0、玩家 1、普通观战、裁判、GM；私密手牌、牌库检索、未揭示天灾必须做负断言。
6. 代表流：登场时发动/不发动、主动战术被响应并无效、议和同意/拒绝、资源支付/取消、棋盘目标/位置、调度、抵挡/支援、天灾、试炼。

## 10. C/D 对局记录依赖登记（本批不修改）

B 的弹框可以比日志更详细，但必须与后续日志使用同一套公开语义。C/D 另行申请基线与租约：

- C：自动动作记录。回合开始的抽牌与士气处理合并成一条自然语言信息；所有引起天灾值、手牌数、士气、血量、试炼等变化的自动动作必须有记录。
- D：玩家动作与卡效完整记录流。覆盖打出、同意/拒绝、发动/不发动、响应、无效、最终结果；统一侍从骑士、加拉哈德等同类“推进试炼”措辞。
- 日志硬门：天灾值无归属；试炼有我方/对方归属；日志只写各接收者可公开知道的结果，不能复用 owner-only 弹框细节。
- 预计依赖文件由 C/D 盘点另定，不能在 B0/B1/B2 顺带修改 `L12PromptAuditLog.cs`、事件生产或 `logViewModel.ts`。

## 11. B0 完成条件

- [x] 盘点全部 `CreatePrompt`、直接构造、等待投影和旧检查点降级。
- [x] 盘点 L12 PromptOverlay、棋盘内联选择、调度、抵挡/支援、主宰/阵营/卡牌能力、GM/沙盒。
- [x] 给出 125 个入口的精确总数、22 文件分布、34 个字面量 kind 和动态网关说明。
- [x] 给出玩家 0、玩家 1、普通观战、裁判、GM 五视角字段边界。
- [x] 给出风险分级以及 B1/B2 精确建议租约。
- [x] 登记 C/D 依赖但未修改日志。
- [x] 除本报告外无仓库写入；未提交产品候选、未推送、未部署。

## 12. B1-core-1 实施回执

### 12.1 共享合同

- `CreatePrompt` 的 10 参数反射合同保持不变；调用方通过受约束的 `L12PromptNarrativeInput` 明确传入 `Title`、`Situation`、`Instruction`、公开安全的等待动作枚举和可选 `ChoiceConsequences`。
- 叙事输入在创建 Prompt 前完成非空校验和选项键校验；内部传递键在写入 `L12Prompt.Data` 前全部移除，不进入检查点或任何接收者快照。
- `ChoiceLabels` 继续只承载短按钮文本；`ChoiceConsequences` 始终复制为独立字典，没有明确后果时为空，不再与标签共用实例。
- `WaitingSummary` 只从公开安全动作枚举生成，不复用操作者的 Situation、Instruction、来源卡名、候选、候选数量或私密区域内容。
- `Presentation` 的公开形状未改变；旧检查点的 `Presentation = null` 降级路径保持不变；P1 fixture 未修改。

### 12.2 已迁移入口（23 个语义场景）

| 文件 | 场景数 | 已迁移场景 |
|---|---:|---|
| `L12PromptsAndSetup.cs` | 12 | 先后攻；安德华拉诺特开局效果；雷神之锤开局效果；我方试炼排序；匿名对手手牌选择网关；公开天灾确认；禁用天灾；选择本局天灾；响应卡选择；响应对象选择；傀儡休整登场位置；绝对防御弃牌费用 |
| `L12Actions.cs` | 5 | 槲寄生符咒支付；自伤减费决定；晋升/普通登场方式；晋升基础军团；战斗致命代替 |
| `L12MoralePayments.cs` | 1 | 普通/异质资源支付与取消 |
| `L12MoraleReturns.cs` | 3 | 士气返还；诸葛连弩展示牌去向；李靖登场位置 |
| `L12LibraryPlacementTransactions.cs` | 1 | 牌库顶部/底部排列事务 |
| `L12LethalReplacements.cs` | 1 | 通用致命替代候选与不发动 |

本批没有修改规则、候选集合、费用、响应窗口、延续标识、投影结构、事件或对局记录。天灾准备文案没有给天灾值添加玩家归属；试炼排序明确使用“我方试炼”。

### 12.3 接收者合同

- 玩家 0、玩家 1：各自只接收自己的完整 Prompt；等待对方时只接收安全摘要。
- 普通观战：不接收完整 Prompt，只接收安全摘要。
- 裁判：可查看全部天灾，但不查看双方手牌，也不接收任一玩家的完整 Prompt；等待行为与普通观战相同。
- GM：可查看双方手牌和全部完整 Prompt，不生成 `waitingPrompt`。

### 12.4 验证证据

- Focused：通过。
- 固定种子 Release 矩阵双跑：11/11 + 11/11，通过；总耗时分别 1.95 秒、1.88 秒。
- Batch：5583/5583，通过；全规则测试耗时 8 分 23 秒。
- 前端完整 `npm run build`：性能、更新日志、UI 合同、响应/抵挡、排位广播、牌库、卡图、`vue-tsc`、production Vite 和 testrun Vite 全部通过。构建使用基线提交 `9042dbdca5ecaa662aaa9030ee9b317c6c9267a4` 作为必需的 `VITE_APP_VERSION`。
- Release：按租约由 L12-main 在集成后的干净提交上执行。
- 推送/部署：本工作树未推送、未部署。
