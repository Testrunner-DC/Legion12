# Grand UMI（OPCG 在线对战）全量学习笔记

> 来源：`corazon1999/GrandUMI`（公开的设计/架构/卡效工作流文档 + Vue 客户端 ARCHITECTURE + 设计系统规范 + 规则文档）
> 目的：为《十二军团》网页版对战引擎（UI 打磨 + 观战/回放模式）找真实参考。
> 时间：2026-08-12
> ⚠️ 限制：本机无浏览器自动化技能、连接全断，无法登录实机进入应用内观战，也无法查看渲染后的截图；卡图不入 git。以下全部基于对**官方文档与真实客户端架构/设计系统**的研究（已覆盖引擎全貌）。另：仓库 `git clone` 在本会话网络下极慢（22MB/7min），未能拉全源码，故走文档研究。

---

## 0. 关键纠正

- README 写的是「Next.js + Unity」，但**真实 Web 客户端是 `opcgpro-vue/`（Vue 3 + Vite）**，在 `develop` 分支（main 分支树不含它）。所有前端结论以 develop 分支的 ARCHITECTURE.md 为准。
- 真实客户端结构是**纯前端 + C# 服务端权威**：前端零结算代码，只「读状态 + 发指令」。

---

## 1. 技术栈与形态

| 层 | 技术 |
|----|------|
| 服务端 | C# .NET 10 · WebSocket（8080）· AES 动态密钥 · 30s 心跳 |
| Web 客户端 | **Vue 3 + Vite** · 状态用 zustand/vanilla（框架无关）桥接 Vue Ref |
| 协议 | WebSocket + JSON；`MsgGameState` 推送快照，`MsgGameAction` 上报操作 |
| 卡图 | 不入 git，由 `download_cards.mjs` 下载（版权原因） |

---

## 2. 回合结构（Turn Loop）

一回合 = 5 阶段（OPCG 规则，与《十二军团》的 6 阶段概念接近）：

1. **重置 Reset**：休息卡→活跃；「直到回合开始」效果失效；发动「回合开始时」效果。
2. **抽卡 Draw**：回合玩家抽 1（先攻首回合不抽）。
3. **费用 Don!!**：费用区 +2（先攻首回合 +1）。
4. **主要 Main**（核心）：反复「出牌 / 发动效果 / 赋予费用 / 战斗」；新登场角色当回合不能攻击，除非【速攻】。
5. **结束 End**：发动「我方/对方回合结束时」自动效果（各 1 次）；对方成为回合玩家。

先攻/后攻差异：先攻首回合不抽、费用+1、首回合不能攻击；后攻反之。

---

## 3. 战斗结算模型（Combat = 5 步）

仅回合玩家主要阶段可攻击；第 1 回合不能战斗。

1. **攻击步骤**：攻击方转休息 → 宣言 → 选目标（领袖 **或** 休息状态的角色）。
2. **阻挡步骤**：防守方仅 1 次发动【阻挡者】代防。
3. **反击步骤**：防守方反复发动（图标）反击 / 【反击】事件。
4. **伤害步骤**：攻击力 ≥ 被攻击力 → 胜（打领袖给 1 点伤害、打角色 KO）；否则无事。
5. **战斗结束**：发动「战斗结束时」效果 → 回主要阶段。

关键机制：【速攻】【双重攻击】【流放】【阻挡者】【不可阻挡】、赋予费用(+1000)、角色区满员需先废弃 1 张、文本优先于综合规则。

> 对照《十二军团》：我们当前是「触发天灾/重置/抽牌/士气/主要/结束」6 阶段 + 进攻宣言/距离/抵挡/支援/伤害。
> **差异**：Grand UMI 的「阻挡步骤」是**独立一步 + 专属 UI 弹窗**；我们攻击军团时是自动互扣、仅在攻主宰时弹窗。可把「抵挡/支援」做成显式可选步骤。

---

## 4. 效果系统（实际实现 —— 对标我们的 effects.ts）

### 4.1 分发模型（`服务端WebSocket\Effects\EffectRuntime.cs`）
一次结算 = `EffectRuntime.Resolve(state, ownerIdx, source, trigger, prompts, payload)`：
1. 检查是否被无效化（`IsContinuouslyNullified` / `IsTriggerNullified`）；
2. 若 `ScriptedEffectRegistry` 命中且 `HandlesTrigger(trigger)` → 跑 C# 脚本（复杂效果，一卡一类 `OPxx_yyy.cs`）；
3. 否则 → `DslInterpreter` 解释 **DSL JSON**（简单效果）；
4. finally：最外层排空 watcher 队列 + 被效果登场卡的【登场时】。

→ **trigger 粒度分发**，脚本只接管部分时机、其余退回 DSL。上下文沿 `AsyncLocal` 传播，支持嵌套与并发房间隔离。
> 我们目前是「每卡一个 TS 函数对象（9 钩子）」，**方向一致**。可借鉴：① 触发需显式注册（他们用 `effectTags` 含时机名，否则写了不触发）；② 复杂卡可「DSL + 脚本」混用。

### 4.2 效果 DSL（数据驱动 JSON，键=卡号）
一张卡一个条目，按**触发节**组织：
- `triggers`: `[ { on, if?, cost?, oncePerTurn?, then:[op] } ]` —— 登场/攻击/KO/阻挡 等事件触发。**不支持 cost**（除非显式）。
- `activated`: `{ cost?, if?, oncePerTurn?, then:[op] }` —— 【启动主要】。
- `main`: `{ cost?, if?, then:[op] }` —— 事件【主要】。
- `counter`: `[op]` —— 事件【反击】。
- `trigger`: `[op]` —— 生命牌【触发】。

**触发枚举（on）**：`OnEnterField`(登场) · `OnAttackDeclare`(攻击) · `OnOppAttackDeclare`(对方攻击) · `OnBlockDeclare`(阻挡) · `OnKO` · `PreKO`(KO前置换) · `OnDamageToLeader` · `OnLifeRevealTrigger` · `ActivatedMain` · `EventMain` · `EventCounter` · `OnGameStart` · `OnTurnStart` · `OnMyTurnEnd` · `OnOppTurnEnd` · `OnDonAttached` · `OnDrawCard` · `OnPlayCard` · `OnEnterTrash` + Wave2 反应式 watcher（`OnCharRested`/`OnCharLeaveField`/`OnAllyCharEnter`/`OnOppEventPlayed`/`OnOppBlocker`/`OnAllyWillBeKOd`/`OnAllyWillLeaveField`/`OnAnyCharKOd`/`OnBattleEnd`/`OnTriggerActivated`/`OnHandDiscarded`/`OnDonReturnedToDeck`）。

**op 原子操作（节选）**：`Draw` · `MillTop` · `AddPowerThisTurn/Battle/All` · `SetPower` · `KO` · `Rest`/`Activate` · `GiveKeyword` · `AttachDon` · `Choose`(选目标→`$var`) · `BounceToHand` · `ReturnToDeckBottom` · `ReturnDonToDeck` · `RefreshDon` · `PlayCharFromHand/Trash` · `TrashToHand` · `LookTopReveal` · `SearchDeck` · `AddCostMod` · `Nullify` · `AddRestriction` · `OpponentDiscard` · `DiscardHand` · `DiscardOwnChosen` · `AddLifeFromDeck` · `MoveCharToLife` · `LifeToHand` · `MarkPreventKO` · `RestActiveDon`。
> 这些 ≈ 我们 `engine.ts` 暴露的 `draw/kill/giveKeyword/addTroops/returnToHand/...` 辅助方法。我们已覆盖大部分原语。

**cost 成本节**（仅 activated/main/counter）：`restSelf`(横置自身) · `selfToTrash` · `donReturn:N` · `restActiveDon:N` · `handDiscard:N` · `millTop:N` · `lifeToHand`。

**if 条件**（多键=AND）：`leaderHasKeyword` · `leaderColorIncludes` · `selfHandCountGte` · `ownCharCountGte` · `turnCountGte` · `isMyTurn` · `selfPowerGte` · `leaderPowerGte` 等。

**执行顺序**（解释器）：`CheckCondition(if)` → `CheckOncePerTurn` → `PayActivationCost` → `RunSteps(then)` → `MarkOncePerTurnUsed`（成本天然先于收益）。

### 4.3 持续效果（`ContinuousEffect`）
常驻效果（光环/静态减费/禁攻/防KO）注册到 `state.ContinuousEffects`，字段含 `PowerDelta/CostDelta/GrantRestriction/GrantKeyword/KoGuard/PreventReset`，**作用对象必须写进 `Predicate(s, side, card)`**（不能只靠 Scope 显示）。费用判定统一走 `CurrentCostOf`。
> 我们目前用 ad-hoc 字段（`lockReset`/`costCut`/`attackNoLoss` 等）实现持续效果——能用，但可参考其 `ContinuousEffect + Predicate` 做更干净的状态级模型（优先级低）。

### 4.4 交互链路（Choose → PromptOverlay）
脚本/DSL 调 `ctx.Prompts.ChooseCards(...)` → 下发 prompt 快照（kind/text/validChoices/extra）→ 客户端 `PromptOverlay` 渲染 → 玩家选 → `GameRequest.respondPrompt` 续接 await。
> **这正对应我们 Task #8 缺的「目标选择弹窗」**。当前我们的卡效（白起拉哪些士气、刘备选关羽、李靖展示等）是引擎自动选最优，缺玩家交互。这是最直接的借鉴点。

---

## 5. Vue 客户端架构（实际结构）

```
src/
├── pages/        HomePage / DeckEditorPage / GamePage(对战) / SpectatePage(观战) / ReplayPage(回放) / LoadingPage
├── components/   home/ deck-editor/ game/(PlayerMat,HandArea,FieldArea,AnimationLayer) ui/(CardItem,Modal,MessageBox,NetStatePanel)
├── composables/ useNet / useStore / useGameInit / useGameAnimation(lastAction驱动) / useGameAudio / usePlayback(回放) / useResponsive / useVirtualList
├── store/        gameStore(syncFromServer唯一写入口) / netStore / battleStore(攻击/防御/计算阶段暂态) / deckStore / audioStore / settingsStore
├── net/          NetManager(WS单例,重连指数退避2/4/8/16/32/64s,6次) / eventBus / HomeProtocol / GameProtocol / GameRequest
├── types/        net(Msg*，字段名严格对齐C#含拼写怪癖 vesion/IsWin/MainDeck) / game(BattlePhase,GameMode) / card / playback
├── data/         CardLoader / DeckMapper / MockPlayback / cardSets / gameLabels(PHASE_LABELS)
└── lib/          colorMap
```

**最值得抄的 5 条（均已验证于我们 Task #8 计划）：**
1. **`gameStore` 只读镜像**：`syncFromServer(msg)` 是唯一写入入口；组件经 `useStore` 订阅切片自动重渲染。我们 `engine.ts` 快照 + `Battle.tsx` 渲染已是同构。
2. **`battleStore` 交互态隔离**：把「选攻击者/选目标/计算阶段」从权威状态里拆出来。我们当前把 `attacker/pendingMaster/blockUids` 混在组件 state，建议抽到独立 `useBattleUI()`。
3. **`SpectatePage` / `ReplayPage` 是独立路由**：观战=复用只读渲染、**不发 GameRequest**；回放=`usePlayback` 控制 action 流播放。这直接给出我们「观战模式」的落地方向（方案 A：引擎录制 action 流 → 只读页逐帧播放）。
4. **`useGameAnimation` 监听 `lastAction` 驱动 `AnimationLayer`**：给 `GameState` 加 `lastAction` 字段，UI 监听播放横置/进场/受击动画。
5. **重连指数退避 + `NetStatePanel`**：仅联网时需要，本地可后置。

---

## 6. 观战 / 回放模式（你提到的重点）

- **观战 SpectatePage**：以只读身份订阅房间状态广播，复用同一套渲染组件，**不允许发送任何 GameAction**。
- **回放 ReplayPage**：`types/playback.ts` + `usePlayback.ts` + `MockPlayback.ts`（开发用模拟数据），按 action 序列逐帧播放。
- 二者都**不需要单独的结算模块**——只是「订阅 + 只看不点」。

> 对《十二军团》的落地（纯本地、无后端）：
> **A. 回放观战（最轻量）**：`engine.ts` 记录每步 action/快照序列，新增只读 `/replay` 路由逐帧播放 = 本地版 Observer。
> **B. 双开同屏**：同一引擎实例，P1/P2 由同一 UI 驱动（练习用）。
> 真正的「联网观战」要等服务端，可后置。

---

## 7. 设计系统（可直接抄的视觉规范）

**双主题**：海贼(pirate)=深暖黑 `#0e0a06` + 琥珀金 `#e8b04b`；海军(navy)=深冷蓝 `#070d18` + 钢蓝 `#5b9bd5`。通过 `<div data-theme>` 切换。
> 我们当前硬编码 jinteki 暗色 hex（`#0f1923` 等）。建议迁移到 **CSS 变量 token 体系**（不硬编码、可双主题）。

**色彩 Token（全部走 CSS 变量）**：`--bg0/--bg1/--surface/--surface2`(背景层级) · `--primary/--primary-bright/--primary-glow/--on-primary/--accent`(主题色) · `--ink/--ink-dim/--ink-faint`(文字) · `--line/--line-strong`(边框) · `--good/--bad`(状态)。

**字体**：`--font-head`(Noto Serif SC/Cinzel) · `--font-ui`(Space Grotesk/Noto Sans SC) · `--font-mono`(JetBrains Mono)。Mood 分 A 终端 / B 电影(默认) / C 游戏 三档。

**圆角/模糊**：`--radius`10 · `--radius-lg`16 · `--radius-pill`999 · `--panel-blur`14px。

**组件模式（可直接套）**：
- **Panel 玻璃面板**：`bg:surface 82%透明 + border-line + backdrop-blur(14px)`，`.panel--solid` 不透明。
- **Kicker 区块标题**：`// 搜索卡牌` 风格（mono 字体 + `::before content:'//'`）。
- **Btn 主按钮**：金色渐变 + shimmer 扫光（`::after` 斜向高光 hover 滑过）；`--on-primary` 深色文字保证对比度。
- **Tag 筛选药丸** / **Seg 分段切换** / **Rule 带字分隔线** / **Nav-item 侧栏** / **Dot 状态脉冲点** / **Card Slot 5:7 卡牌占位**（hover 抬起 + 发光）。

**动效**：`--ease-out`(0.2,0.7,0.2,1) / `--ease-snap`(强弹性) / `fadeUp` / `scaleIn` / `shimmer` / `pulse`；时长表（hover180-250 / 弹窗500-600 / 脉冲1800 / 主题600）；支持 `prefers-reduced-motion`。

---

## 8. 对《十二军团》的改造建议（按优先级）

### P0（直接复用其模式，成本小、收益大）
1. **交互态隔离**：`attacker/pendingMaster/blockUids` 抽到独立 `useBattleUI()`，引擎只持权威状态（对齐 battleStore）。
2. **请求锁 `resolving`**：出牌/进攻/结束回合时禁用按钮，防连点。
3. **`lastAction` 驱动动画**：`GameState` 加 `lastAction`，UI 监听播横置/进场/受击（先 CSS transition，后上 Framer Motion/Vue 等效）。
4. **设计 token 化**：把 jinteki 硬编码 hex 迁到 CSS 变量（含双主题预留）。

### P1（战斗模型补全，对齐「阻挡步骤」）
5. **显式抵挡/支援弹窗**：攻击军团也弹「是否抵挡/支援」选择（保留自动互扣为默认，给手动入口）——即其 `PromptOverlay`/`Choose` 思路。
6. **补全钩子**：`onBeAttack`/`onBlock`/`onDamage`，把「自动选目标」改为显式可交互（参考其 `Choose`→prompt 链路）。

### P2（观战 / 回放 / 体验）
7. **回放观战（方案 A）**：引擎录制 action 流 + 只读 `/replay` 路由逐帧播放 = 本地 Observer。
8. **资源区可视化**：士气/费用/牌库数/天灾值做成独立常驻条（参考其 CostArea/LifeArea/PlayerMat）。
9. **卡牌详情悬浮**（CardInfoPanel）+ **断线/重连遮罩**（联网时）。

### 暂不抄
- 联网对战 / AES / protobuf / 重连指数退避：纯本地阶段不需要。

---

## 9. 我已掌握的真实产物清单（用于后续落地）
- ✅ 回合 5 阶段 + 战斗 5 步模型
- ✅ 效果 DSL 完整规范（触发节 / op 原语 / cost / if 条件 / 分发模型 / 持续效果 / Prompt 链路）
- ✅ Vue 客户端目录结构与 store/net/composable 划分
- ✅ SpectatePage / ReplayPage 独立路由方案
- ✅ 设计系统全套 CSS token + 组件模式 + 动效规范
- ✅ 观战/回放实现思路（只读订阅 + 逐帧播放）
- ❌ 未看到：渲染后截图（无浏览器）、`.vue` 战斗组件源码（树截断，但 ARCHITECTURE.md 已描述充分）

---

## 10. 新网络框架二次学习（待处理，2026-08-16 追加）

上文记录的是第一次研究时的旧框架结论，其中“纯本地阶段不需要联网”的判断已经过时；《十二军团》现已拥有正式 WebSocket 对战、观战、快照隔离和持久化基础。GrandUMI 最近更新网络框架后，需要重新获取最新代码并以提交差异为依据二次审计，不能沿用旧结论或凭界面猜测。

本任务排在当前规则内核与卡效测试稳定之后、赛事正式服务和深度断线重连之前。交付物与检查矩阵统一见 `TOURNAMENT-AND-LEARN-TO-PLAY-PLAN.md` 的“GrandUMI 新网络框架学习（待处理）”。
