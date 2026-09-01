# 效果生成打出与私密区域事务分类

更新日期：2026-09-02

## 扫描口径与结论

- 范围：`TwelveLegions/*.cs` 中全部直接`PushEffect(...,"play")`、`.Hand.Add(`、`Library.Remove(`、`Resolving.Add(`、`SummonFromAnyPrivateZone`与效果生成“打出/免费打出/再次发动”文案。
- 硬问题：李牧与冲田两处直接免费打出旁路；冲田另有旧结算期位置Prompt、不能覆盖己方反击及位置失效错误回手。两处已统一迁移。
- 托勒密十三世只复制上一张主动战术的效果：父效果完整离栈后重新声明该效果所需模式/目标，生成独立可响应栈项；不重复支付原战术打出费用或冒号前费用，也不制造虚拟手牌/结算区移动。
- 未发现新增确定性错误：效果加入手牌的生产入口已统一使用`AddCardToHandByEffect/effect-hand-add`；剩余直接手牌与牌库原语均有明确非效果或合法结算上下文。

## 直接 play 路径（3）

| 路径 | 分类 | 保留理由 |
| --- | --- | --- |
| `L12CompositeEffectPlans.CompleteCommittedCompositeEffectDeclaration` | 已迁移权威入口 | 已提交免费战术复用HandPlay Composite的公开声明、费用与独立段 |
| `L12EffectGeneratedPlay.CompleteEffectGeneratedFreePlay` | 已迁移权威入口 | 李牧/冲田普通战术唯一公共入口；来源移区、日志、Resolving与不可变标记集中处理 |
| `L12PostResolutionGeneratedEffects`托勒密十三世 | 已迁移权威入口 | 父效果离栈后只复制效果；保留效果声明与合法性重验，不重付原费用、不移动虚拟卡 |

## 直接 Hand.Add 路径（7）

| 数量 | 分类 | 文件/理由 |
| ---: | --- | --- |
| 1 | 合法底层原语 | `L12AuthorityEvents.AddCardToHandByEffect`唯一写手牌并发布私密AuthorityEvent |
| 2 | GM受控原语 | `L12GmCommands`沙盒/管理移区，不是卡效结算入口 |
| 2 | 开局装配 | `L12PromptsAndSetup`雷神之锤初始设置/恢复，不是效果加入手牌 |
| 2 | 非效果普通移区 | `L12GameEngine`晋升组合/战场离场在无堆叠上下文回手；效果上下文已经分流至helper |

## 直接 Library.Remove 路径（41）

- 4处开局/恢复装配，1处规则内核抽牌，1处权威区域事务底层删除。
- 其余35处都位于牌库顶合法揭示、控制者私密检索、已选择排序/放底/入墓/入手之后；选择发生在对应效果段合法开始后，不把身份放入预声明或对手快照。
- 新卡效不得仅凭“当前可见”直接增加裸删除；需要先归入排序/检索底层原语、`AddCardToHandByEffect`、权威区域事务或效果生成打出事务。

## 不变量与棘轮

- 免费打出只修改支付为0，不绕过位置、反击覆盖、天灾等级、持续效果、登场触发、复合声明或结算区生命周期。
- 已展示卡的公开位置在提交前声明，提交时重验；失效不覆盖、不改选、不错误回手。
- 未展示卡加入手牌时，实例ID、卡号与名称不得出现在对手快照、公开日志或公共AuthorityEvent来源中；卡面明确展示时由独立`reveal`事件公开。
- `scripts/test-l12-effect-generated-play-transactions.ps1`锁定：3类权威路径中的直接play Push调用点不高于4、Hand.Add不高于7、Library.Remove不高于41，并限定合法文件与旧令牌清零。
