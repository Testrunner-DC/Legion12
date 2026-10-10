# BUG 010fc531：乾坤·阳零目标仍可发动

## 裁定与边界

- 产品裁定：〈乾坤·阳〉第一段“击杀对方1张原本兵力不高于3000的军团”是效果，不是发动费用。没有合法目标时整张牌仍可发动，第一段仅记为无目标跳过。
- 第二段“可返还1士气：抽取1张牌”是独立可选效果段。第一段无目标、结算失败或正常击杀，均不得吞掉第二段自己的声明窗口。
- 本批扩展 `ARCHITECTURE-LOCK` 的 P0 卡效出口：继续使用结构化复合计划、公共声明和公共段推进，不增加逐卡结算旁路，不修改 P1—P4。

## 根因与共享修复

- 根因：公共复合计划已经能在后续段声明时把“无合法对象”排成跳过段并继续，但首段初始声明仍固定要求至少1个目标，整卡合法性校验也固定要求该目标存在。因此零目标时在入栈前被拒绝，无法进入已有的独立后段推进出口。
- 修复：`L12CompositeEffectSegmentSpec` 增加 `SkipWhenNoLegalTargets` 计划元数据；公共目标声明构造器据此生成自动完成的零选择步骤，公共整卡校验只在当前确实没有合法目标时接受空声明，公共首段数据把该段投影为 `skipped/effect-noop` 并沿用 `QueueNextCompositeSegment` 推进后段。
- 〈乾坤·阳〉只在计划数据上标记该语义；没有在 `ResolveCardEffect`、乾坤·阳结算器或卡号分派中添加零目标特判。已有“已声明目标在响应逆结算后失效”的 `failed -> QueueNextCompositeSegment` 出口保持不变。

## 同族扫描

- 结构查询：遍历 `L12CompositeEffectPlans.AllPresentationPlans()`，筛选“第一段含公开目标，且存在 `DeclareAtSegmentStart=true`、`RequiresPreviousSuccess=false` 的后续独立段”。结果固定为3项：
  - `S01-0118`〈神妙行军〉：无我方前排目标时已用 `mode:none` 跳过第一段，后续返还士气击杀已有正常、无目标、目标失效、被无效、恢复回归。
  - `S02-0105`〈乾坤·阳〉：第一段合法目标集合与第二段抽牌无关；本次唯一需要启用公共零目标首段出口的计划。
  - `S02-0522`〈倪克斯的陨星〉：两段均要求对方军团；第一段无任何对象时第二段也没有可声明目标，不存在“后段仍可执行”的受影响场景。
- 防回滚：具名扫描测试同时锁定上述3项全集、后段不依赖前段成功，以及当前仅〈乾坤·阳〉声明 `SkipWhenNoLegalTargets`。新增同类计划必须显式选择是否启用该元数据，不能再用卡号条件补洞。

## 回归与验证

- 修复前红测：`QianYangWithoutAKillTargetStillOffersItsIndependentDrawSegment` 两个数据行均以“没有足够的合法目标”失败；其余有目标两路及目标失效后段推进为绿。
- 修复后具名回归6/6：
  - 零目标＋不返还；
  - 零目标＋返还1士气并抽1；
  - 有目标击杀＋不返还；
  - 有目标击杀＋返还1士气并抽1；
  - 已声明击杀目标离场，第一段失败后仍提供独立抽牌段；
  - 三项同族计划结构扫描。
- 相关聚焦集33/33：覆盖〈乾坤·阳〉原本兵力过滤、既有两条正常路径、声明目标失效、〈神妙行军〉零首段目标／后段支付／目标失效／无效／恢复／重复效果，以及〈倪克斯的陨星〉延后声明。
- 静态门禁：手牌复合计划声明／响应前费用／独立段守卫、卡效运行时语义证据、P0—P4 架构锁均通过。
- 已知环境警告：NuGet 漏洞源不可用产生 `NU1900`；另有既存 `DisasterTriggerSuppressionRulingTests.cs` 的 `xUnit2029` 警告，均无编译或测试失败。
- 按 Main 协调边界，本工作区未运行完整 Batch/Release、全规则套件、平台或前端重型门禁；由 Main 合并后串行验收。

## 文件与发布边界

- `服务端WebSocket/TwelveLegions/L12CompositeEffectPlans.cs`
- `TwelveLegions.Tests/Bug010fc531QianYangEmptyTargetTests.cs`
- `docs/BUG-010FC531-QIANYANG-EMPTY-TARGET-REPORT.md`
- 本批不修改 `docs/TASK-LEDGER.md`、`docs/BUGFIX-REGISTRY.md`、`docs/HANDOFF.md` 等共享台账，不推送、不部署、不关闭生产 Bug。
