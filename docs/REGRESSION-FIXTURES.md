# 确定性对局回归样例规范

## 用途

人工 Bug 必须尽量转为可重复场景，避免每次重新建房、抽牌和摆场。优先使用直接构造 `L12GameEngine` 状态的测试；只有涉及协议顺序、断线或回放兼容时才保存 JSON 样例。

## 最小样例内容

- 规则版本与卡池版本；
- 随机种子；
- 当前玩家、回合、阶段和天灾状态；
- 双方必要区域的最小卡牌实例；
- 待处理 Prompt/Stack/TriggerBatch；
- 操作序列；
- 双方各自可见的预期快照、日志与最终区域。

## 隐私与稳定性

- 真实对局必须先匿名化；不得保存账号、昵称、密码、Token、房间密钥、IP或无关私密手牌。
- 卡牌实例使用稳定的测试 ID，时间使用固定值，随机行为使用显式种子。
- 玩家视角、对手视角、观战/裁判视角分别断言，不能用 GM 全可见快照替代隐藏信息测试。
- 样例升级必须显式说明协议或规则版本变化，禁止为了让测试通过而无解释地覆盖期望结果。

## 推荐位置

- 纯规则：`TwelveLegions.Tests` 内的对应规则测试类；
- WebSocket、顺序与恢复：`服务端WebSocket.Tests/Fixtures/`；
- 前端只读回放：`opcgpro-vue/src/data/fixtures/`；
- 跨层发布回归：通过现有 UI 契约和 WebSocket 冒烟脚本读取经过匿名化的固定样例。

## Batch 6H 进攻时公开声明样例

- 固定种子 `7601` 至 `7610` 覆盖韩信费用预付、高杉目标失效不返费、信长来源离场、平阳牌库顶不提前泄露、高文独立段、理查候选与被无效税、罗宾候选、冲田隐藏控制组、斯巴达标签，以及美尼斯选择自身作为弃置费用后不触发阵亡、进攻中止且自身增益取消的边界。
- 24张卡另以 `7650 + 卡序号` 构造最小公开场面，逐张断言真实攻击入口先进入 `PendingActivation` 或同一时点 `TriggerBatch`，不得出现 `card-effect` 结算期声明。
- 高文样例的稳定协议值为 `rune-count:1..N`；`skip` 只表示不发动，发动时X不得为0。理查与罗宾样例通过 `trigger-order` 的候选说明定位独立段，不依赖不稳定候选ID。

## Batch 6B 试炼完成事件样例

- 固定种子 `7621` 至 `7626` 覆盖寻找圣杯/湖中仙女的牌库身份延迟、湖中仙女首段拒绝时直接从强制回牌库/重洗段入栈而不产生空响应栈、该强制段被无效后费用减免段仍继续、芬尼亚X符文预付与重复目标独立段、来源离区/目标失效不退款、安格斯同一时点排序/独立可选触发，以及十字军东征无印刷完成触发控制组。
- 芬尼亚数量使用稳定协议值 `rune-count:1..N`，随后以 `target1..targetN` 分键保存公开目标；不同目标步允许重复选择同一实例。湖中仙女使用显式 `mode:none` 跳过首句，整次声明不提供会吞掉后续强制段的 `skip`。
- S3/S4 的牌库实例不得出现在入栈前 Prompt、候选或事件；只有对应效果段合法开始后，操作者私密 Prompt 才能包含牌库实例，选中后再按卡面公开展示。

## Batch 6C 复合手牌战术独立段样例

- 固定种子 `7701` 至 `7707` 覆盖祭天天灾值预声明、议和受影响玩家结算期选择、观星/花魁隐藏牌库、花魁确切士气目标、密米尔次数预留，以及李牧免费打出无控制者声明战术不产生空 Prompt。
- 入栈前声明只能包含公开模式、天灾值或公开士气目标，不得包含牌库顶部实例。合法首段开始后才生成私密排序/选择 Prompt；首段被无效时不建立这些隐藏候选，但已声明的后段仍独立入栈。
- 智慧法典放弃议和谈判的当前抽牌段时，只无效该段；谈判段仍应独立入栈，战术来源直到最后一段完成才从 `Resolving` 进入墓地。

## Batch 6D 最后已知来源与权威区域事务样例

- 固定种子 `7801` 至 `7805` 覆盖桂小五郎返回牌库后独立士气段、该段无效不回滚返回、声明后单个士气目标失效不吞其他目标、陵墓构造体跨控制双候选与草薙剑真实实例缺失失败关闭。
- 草薙剑参数化覆盖战场、圣物区、额外圣物区、试炼区、卡诺匹斯区、结算区、手牌、牌库、墓地与移出区；每次只允许一个权威实例，完成后相同实例位于所有者牌库顶部且全区域总数仍为1。
- 陵墓构造体样例让卡牌控制者与所有者不同，并让守卫 `OwnerIndex` 为空，强制以真实墓地区域确定所有者；阵亡与离场候选分别响应，先段无效不得吞后段。

## Batch 6E 公开位置结算重验样例

- 固定种子 `7900` 至 `7914` 覆盖9张焦点卡的私区军团登场、同一位置双声明碰撞、勇士比约恩冒号前完整费用、黄金圣甲虫主动次数及陵墓构造体成功控制组。
- 在公开位置声明完成、效果进入响应窗口后，由测试把另一张真实军团置入该格。结算必须保留占位军团，待登场军团仍在原手牌或墓地，全区域实例数保持1，并记录本对象取消；不得自动改选其他空位。
- 比约恩声明提交后、效果入栈前，主宰伤害和4张墓地卡的牌库底顺序都已完成。效果被无效或所选位置被占后，费用不返还，比约恩仍在墓地。黄金圣甲虫主动位置失效后，圣物继续休整且本回合次数已消费。

## Batch 6F 试炼推进公共事件样例

- 固定种子 `8000` 至 `8042` 覆盖8张 TrialValue 军团通常推进、四张登场公开模式、兰斯洛特击杀、芬恩独立转活跃、安格斯强制once、阿瓦隆回合开始及推进到8的未完成控制组。
- 通常推进和加拉哈德/芬恩/康斯坦丝的试炼模式必须在响应窗口出现前休整，兰斯洛特/芬恩符文费用必须在对应段入栈前支付；堆叠项被无效、来源离场或目标状态失效均不得返还已付费用。
- `trialAdvanceEvent=true` 与不可变 `trialAdvanceCount` 是推进堆叠项的稳定协议值。芬恩后续使用独立 `trial-advance-followup` 候选；达到8点后 `TrialCompleted` 仍为false且不得出现 `trial-complete` 候选，直至玩家另行完成试炼。

## Batch 6G-A 可选触发声明样例

- 固定种子 `9000` 至 `9202` 覆盖玛格丽特两项触发、安德华拉诺特、阿尔忒弥斯、莫瑞甘、李牧与完成圣杯后的圆桌骑士触发；每个候选在任何堆叠项出现前先提供 `mode:none/mode:use`。
- 安德华拉诺特、阿尔忒弥斯、莫瑞甘、李牧与圣杯触发以 `onceKey:pending` 保留；拒绝必须清除pending，确认必须在入栈前写入final，之后无效、来源离场或结算目标失效均不得恢复。阿尔忒弥斯另声明确切休整普通士气实例，翻面后保持休整。
- 玛格丽特转休整是冒号前费用；`margaret-heal` 与 `margaret-heal-lock` 是两个独立堆叠段。测试只让过第一段响应后检查第二段仍在堆叠，避免测试助手跨段全部让过掩盖独立响应边界。拒绝最后候选必须恢复已排定天灾检查，天廷士气归零提示同一玩家只能存在一个。

## Batch 6G-B 李牧隐藏展示与独立抽牌样例

- 固定种子`9301`至`9308`覆盖公开展示/抽牌模式预声明、跳过展示直接从抽牌段入栈、双拒绝无空栈、展示段被无效仍续接抽牌、合法展示后才暴露顶部身份、不合格牌放底后独立抽牌、合格战术复用公共免费打出声明，以及来源离场不吞独立段。
- 入栈前的TriggerCandidate、PendingActivation与声明数据只允许包含`revealMode`和`drawMode`，不得包含牌库顶实例ID、卡号、名称或费用。只有`limu-reveal`合法开始后才能读取顶部；若符合条件，`s2-limu-tactic`私密Prompt才可提供“免费打出/放底”。
- `limu-reveal`与`limu-draw`是两个独立堆叠段。`revealMode=mode:none`且`drawMode=mode:use`时第一个EffectStack必须直接是`compositeSegment=1`的抽牌段；双`mode:none`不得生成任何响应窗口。免费打出战术走公共复合声明，父展示段完成后仍按原声明续接抽牌。

## Batch 6I-A 原子可选触发声明样例

- 固定种子`9400`至`9652`覆盖13张卡14项Verified Atomic Optional：逐项入栈前声明、逐项拒绝无空栈、不可变`mode:use`结算、六项前置条件失败无候选、条件时点快照、阿塔兰忒双登场候选及宫本武藏真实进攻不重复。
- 前置Condition只在TriggerCandidate建立时读取当时公开状态，成立后写入`verifiedAtomicConditionLocked=true`；响应期间状态变化不重新取消已经合法建立的效果。声明阶段不读取隐藏信息，本组也不增加卡面没有的once限制。
- 生产运行时不得创建`verified-atomic-optional` Prompt或continuation。每个合法可选时点只出现一个`pending-activation`，提交后堆叠项仅消费`declared:mode`；来源离场、被无效或目标失效均不回滚声明。

## Batch 6I-B 直接阵亡/进攻后触发声明样例

- 固定种子`9700`至`9873`覆盖18项直接旧入口的入栈前模式、公开墓地/战场目标、公开位置、私密手牌与冒号前士气费用声明；每个合法候选的第一个交互均为`pending-activation`，没有合法选择时不建立候选或空响应栈。
- 荆轲按`mode:use`→确切士气→最多1个目标的顺序声明，提交时返还士气；效果被无效或目标失效不返还费用。疯狂爱丽丝候选建立时写入`once:pending`，拒绝释放，提交转为final且之后不恢复。
- 黑胡子蒂奇、传奇拉格纳、奥拉夫二世与雷神之锤只预声明公开发动模式，抽牌合法完成后才产生私密弃牌Prompt。上杉谦信、坂本龙马、亚瑟王的手牌实例只出现在控制者快照，位置公开并在结算期重验；忒修斯必须发布展示与效果加入手牌权威事件，格温莉安仅接受`cause=effect`且必须在回血/抽牌中选择一项。

## Batch 6J-A 登场与晋升登场公开声明样例

- 固定种子`9900`至`9977`覆盖44张卡45个登场/晋升登场时点、隐藏牌库身份、冒号前费用、目标失效不退款、独立段、拒绝无空栈与强制目标不存在。每个有公开选择的合法候选在任何EffectStack出现前先提供`pending-activation`。
- 李靖、万物统御之戒、武田信玄、八尺琼勾玉和罗宾汉的声明不得包含牌库顶、检索命中或侍从实例；这些身份仅在对应StackItem合法开始后出现在控制者私密Prompt或公开展示事件。私密手牌费用只对选择者可见，公开格位仍须提前声明。
- 克劳迪娅等冒号前费用在声明提交时支付，被无效或目标失效不返还。诸葛亮与卡诺匹斯罐的后续段仍独立入栈；武田信玄由2026-09-05玩家最新裁定覆盖旧“分别响应”结论：开头不发动整段停止，发动后检索、洗牌、可选真田登场和士气转活跃共用一个StackItem，检索跳过/未命中继续，整段被无效则全部停止。强制目标不存在时不留下空栈、空Prompt或悬挂candidate。

## Batch 6J-B 主动/响应/结算期Prompt分类样例

- 固定种子`9980`至`9997`及后续精确场景覆盖9张卡9项：野外扎营、前线侦查、吕布、花木兰、古斯塔夫一世、驱魔道士 陆瑛、祷告仪式、孙悟空与天廷零士气恢复。首个公开交互必须是手牌或触发的`pending-activation`，StackItem结算不得重复询问相同模式、费用或公开目标。
- 野外扎营与前线侦查在提交时分别预付已声明后段的1份普通资源；首段被无效仍建立已声明后段，费用不返还。顶牌身份、排列及前线侦查中由对手选择的确切手牌只在合法段结算时对合法玩家可见。
- 吕布确切4士气、古斯塔夫有序墓地2张和祷告仪式私密段1士气均在入栈前支付；效果无效、来源离场或公开目标失效不恢复。花木兰目标失效只取消其段。天廷零士气与杨戬、李牧等同一支付时点候选按来源卡号识别，测试不得把合法额外候选当作重复Prompt。
- 运行时Prompt清单的当前稳定棘轮为`CreatePrompt(`静态令牌不高于135；其中1个是方法定义，134个实际调用点按合法隐藏、受影响方、系统流程、旧resolver遮蔽、待裁定与致命替代等类别登记，不能为降低数字机械删除合法结算选择。6J-B 当时的139令牌仍作为历史基线保留在其批次记录中。

## Batch 6J-C 效果生成免费打出与私密区域事务样例

- 固定种子`9701`至`9707`及扩展场景覆盖冲田军团位置预声明、覆盖己方反击、位置失效不回手/不覆盖、私区登场AuthorityEvent、圣物替换与登场触发、李牧普通/复合战术统一事务、免费战术被无效不回牌库、来源离场及私密入手对手快照。
- 冲田合法展示顶部军团后选择“打出”，下一交互必须是`pending-activation`而非`s2-okita-slot`；可选格包括后排己方反击占位。提交时若声明格已失效，只记录对应打出取消，牌留原区域，不改选、不覆盖也不降级为加入手牌。
- 李牧与冲田的主动战术都经`BeginEffectGeneratedFreePlay`；普通战术StackItem携带`effectGeneratedPlay=free/originZone=library`，复合战术继续进入HandPlay Composite声明。效果被无效只让已提交战术正常结算到墓地，不返还到牌库。
- 静态棘轮：生产直接play Push不高于3且只允许公共复合、公共效果生成与托勒密裁定隔离；直接`Hand.Add`不高于7且只允许统一helper、GM、开局装配和非效果普通移区；直接`Library.Remove`不高于41，新路径必须先分类或进入权威区域事务。

## Batch 6K-A S01通用与天廷逐卡逐能力审查样例

- 固定清单冻结56张94项能力；`AtomicReviewBatch6KARegressionTests`的种子`8101`至`8109`覆盖黑胡子、战斗至黎明、空城计、摄政皇权、西施、刘备、杨戬及凌霄宝殿的独立段与声明边界。逐卡审计表必须恰有56张唯一状态：46通过、8明确错误已修复、2有疑点。
- 黑胡子登场弃牌、战斗至黎明强化、西施召唤、刘备检索、杨戬抽牌、凌霄宝殿首段被无效时，各自后续已声明段仍建立新的响应窗口；零目标或拒绝可选首段时，首个真实StackItem直接是后段，不生成空栈。合法支付的费用不因首段无效、来源离场或目标失效回滚。
- 摄政皇权有合法手牌军团和位置时，控制者的第一步必须是私密`hand-card`声明且不得含`mode:none/skip`；随后公开声明战场与格。声明完成前盖伏来源仍留场且隐藏，首个真实StackItem入栈前翻开并移入`Resolving`，对手只在此后获得响应权。
- `test-l12-s01-universal-heaven-audit.ps1`锁定56/94清单、8/46/2状态、复合计划、置伏来源公开时点、摄政强制选择及战斗至黎明/空城计旧continuation不得回流。李靖依赖“随后”和雷霆天怒骰点并列只由`OPEN-QUESTIONS.md`记录，不在夹具中猜测。

## Batch 6K-C S01高天原逐卡逐能力审查样例

- 固定清单冻结24张55项原子目录能力；`AtomicReviewBatch6KCRegressionTests`的固定种子`8301`至`8311`覆盖本多前置击杀目标、天照两个主动复合段、天诛手牌声明、服部隐匿、戒指阵营及稻姬/草薙剑/须佐目标。逐卡审计表必须恰有24张唯一状态：15通过、9明确错误已修复、0疑点。
- 本多减费与随后击杀、天照减费/击杀与士气翻正/前排强化均必须由独立StackItem开放响应；已付费用、已合法完成的前段不因后段无效或目标失效回滚。`DeferredEffectStack`每次只压入一项，下一段在前段收尾后再获得自己的响应窗口。
- 服部半藏覆盖时不得出现在普通公开军团目标列表，但天灾专用测试必须继续命中它；万物统御之戒使通用卡在战场、手牌、牌库、墓地与额外区都使用控制者阵营。`test-l12-s01-takamagahara-audit.ps1`禁止裸`Faction ==/!= "gaotianyuan"`、旧本多/天诛continuation与批量延迟压栈回流。

## Batch 6K-B S01太阳城与阿斯加德逐卡逐能力审查样例

- 固定清单冻结53张124项能力；`AtomicReviewBatch6KBRegressionTests`覆盖图特摩斯进攻/阵亡、伊瓦尔登场、纳芙蒂蒂受影响方私密弃牌、卡诺匹斯箱/罐二/罐三与众神之乡/英灵殿的公开声明、隐藏信息和独立段。逐卡审计表必须恰有53张唯一状态：44通过、9明确错误已修复、0有疑点；2026-09-05新增艾瑞克`after-damage`路由完整性与恰一次弃牌证据。
- 图特摩斯无合法击杀目标时首个真实StackItem直接是强制减兵段；伊瓦尔拒绝不生成空响应，牌库顶身份只在合法开始后读取；卡诺匹斯及两张阵营牌的前段无效不吞随后段，公开墓地目标入栈前锁定并在结算重验。
- 智慧法典对确切StackItem支付弃牌费用后只获得一次成功奖励；复合效果新建的语义独立后段不得继承`wisdomRewards`。历史精确夹具同时断言抽牌/回收完成后不存在第二个奖励PendingActivation或候选。
- `test-l12-s01-sun-city-asgard-audit.ps1`锁定53/124清单、8/43/2状态、7组公开复合计划、智慧法典一次性标记清理和旧串行分支不得回流。霍列姆赫布致命替代与托勒密重复主动仅由既有`OPEN-QUESTIONS.md`记录。

## Batch 6L-A S02通用与天廷逐卡逐能力审查样例

- 固定清单冻结26张51项能力；`AtomicReviewBatch6LARegressionTests`覆盖孙悟空士气与前排格联合声明、响应期占位、始皇帝两个独立段、费用不返及本回合士气来源限制。逐卡审计表必须恰有26张唯一状态：23通过、2明确错误已修复、1有疑点。
- 孙悟空声明提交前不得出现EffectStack；声明格结算时被占只取消登场，已付士气和once不恢复，既有卡不被覆盖。始皇帝首段/后段可分别无效，任一段失效不回滚已付手牌费用或另一已完成段。
- 始皇帝限制生效时，普通卡效/主宰/试炼等`AddMorale`调用必须被拦截，只有明确携带阵营效果来源标记的入口可增加士气。`test-l12-s02-universal-heaven-audit.ps1`锁定26/51清单、2/23/1状态、复合计划、位置重验与士气来源标记；信仰狂热者仅核对OPEN仍存在，不猜声明层级。

## Batch 6L-B S02太阳城与阿斯加德逐卡逐能力审查样例

- 固定清单冻结16张44项能力；`AtomicReviewBatch6LBRegressionTests`覆盖陵墓圣武士离场计数/回合清零/跨控制所有者墓地、沙漠君临入栈前弃置与位置失效、雷神之锤墓地主动声明及雷神索尔跨控制陵墓守卫资源。逐卡审计表必须恰有16张唯一状态：12通过、4明确错误已修复、0疑点。
- 陵墓计数只在`RemoveFromField`和`MoveFieldCardToZone`真实离场成功后登记，位移与叠放不能误计；所有者墓地与当时控制者减费计数必须同时保持。沙漠君临全部费用对象先验证再原子支付，后续对象/位置失效不返费、不改选、不覆盖。
- 雷神之锤墓地主动按钮已经表达发动意图，下一交互必须直接是PendingActivation费用/位置声明，不得恢复`graveyard-active-confirm`；抽牌后弃牌仍是合法隐藏结算Prompt。`test-l12-s02-sun-city-asgard-audit.ps1`锁定16/44清单、4/12/0状态、两条离场登记、复合预付、跨控制资源与旧确认不得回流。
- `CreatePrompt(`当前静态棘轮为135个令牌（1定义、134调用）；后续可以继续下降，但不得为压数字删除合法隐藏信息、受影响方选择或系统流程Prompt。

## Batch 6L-C S02高天原与奥林匹斯逐卡逐能力审查样例

- 固定清单冻结35张104项能力；`AtomicReviewBatch6LCRegressionTests`覆盖月读后→前独立触发、武运在天隐藏检索/确定性后段、晋升状态继承、万物统御之戒跨区域奥林匹斯筛选、诸神巅独立回收/登场及神力四状态。逐卡审计表必须恰有35张唯一状态：18通过、17明确错误已修复、0有疑点；2026-09-05新增武田单StackItem与普罗米修斯公开所选牌/隐藏其余候选证据。
- 月读每次后→前移动只建立候选，效果被无效不得增加进攻增益且不消费次数；成功结算后可按移动次数累计。武运在天首段合法开始前不得读取顶5身份，首段无效仍建立确定性上杉后段。诸神巅已付2神力不因任一段无效、墓地目标或声明格失效而恢复。
- 晋升必须共享底座休整、限时修正及其已吸收伤害、赋予关键词、限时禁止/许可和移动/进攻计数；不得启用底座印刷效果或把底座基础兵力叠加。所有效果语义的奥林匹斯筛选必须识别戒指通用卡，晋升身份配对仍只看印刷阵营。
- `test-l12-s02-takamagahara-olympus-audit.ps1`锁定35/101清单、14/20/1状态、月读候选、两组Composite、晋升状态事务、戒指有效阵营和裸Olympus效果筛选不得回流；海伦致命替代仅核对既有OPEN，不猜裁定。

## Batch 6L-D S02彼界与天灾逐卡逐能力审查样例

- 固定种子`8701`至`8711`覆盖38张/83项清单冻结、梅林隐藏检索、圣杯隐藏存在性与戒指命中、鲍斯/莫瑞甘/芬尼亚/符文之力有效阵营、十字军共享once及加拉哈德费用预付。逐卡审计表必须恰有38张唯一状态：30通过、8明确错误已修复、0疑点。
- 梅林和圣杯在StackItem合法开始前只能读取公开牌库数量，不得把匹配卡实例、卡号或“是否命中”写入声明；戒指通用卡应被彼界效果识别，但阿麦金和十字军“只拥有/只有彼界特征”仍只接受印刷彼界卡。
- 加拉哈德`mode:heal`提交时先经权威离场事务弃置自身，再建立EffectStack；被无效时费用不返、不抽牌、不回血也不触发阵亡。十字军三种主动共用`crusade-choice`，取消不消耗、任一提交后其余模式本回合均不可再用。
- `test-l12-s02-otherworld-disaster-audit.ps1`锁定38/83清单、8/30/0状态、隐藏读取、有效/印刷阵营边界、共享once、费用先后与S02天灾不可响应；并汇总七份逐卡审计表，要求全池248张每张恰有一个唯一结论。

## Batch 6M 全卡池最终交叉审查样例

- `AtomicReviewBatch6JARegressionTests.RingEntrySearchDeclarationDoesNotPeekForAHiddenUniversalMatch`固定种子10005：牌库非空但没有【通用】命中时仍必须提供私密发动声明，声明不得预读或泄露命中存在性。
- `AtomicReviewBatch6KBRegressionTests.DivinityRecoveryDeclarationUsesRingModifiedEffectiveFaction`覆盖众神之乡与英灵殿：戒指生效时，墓地通用卡必须出现在控制者的前置声明中；公共声明/复合计划静态门禁禁止恢复裸印刷阵营过滤。
- 七表汇总门禁要求248张、552项、188通过/54修复/6疑点卡，并核对矩阵状态及5项OPEN；Release固定证据为规则1006、平台60、原子248、UI171、卡图19/248、优化卡图248/248及生产构建/publish。

## ST 第三批B剩余卡效样例

- `StarterBatch3BRegressionTests`使用固定种子`20401`至`20423`的13项场景覆盖18张卡。目录样例同时断言结构化能力与verified runtime；原子审计必须保持324张、`noRuntimeEntranceCards=0`、卡号case为0、效果文本推断为0。
- ST-DS02、ST02-M1、ST03-M1、ST04-M1、ST05-M1等费用场景必须在第一张效果StackItem出现前完成公开声明与支付；取消、目标或费用失效均不得支付或入栈。ST05-01只在合法结算后读取牌库身份，候选只对控制者可见。
- ST03-10两个增益、ST05-01检索登场/随后洗牌、ST05-M1翻转士气/军团强化以及ST06-S1三句可选效果分别形成独立响应窗口；前段被无效不得吞掉确定存在的后段。
- ST04-05替代其他高天原军团时继承原致命动作、所有者墓地及离场语义；ST02-10的延迟弃置和ST04-10的叠放强攻必须经过公共区域入口。ST03-08墓地三倍计数除本批战术外，还以既有格拉墨费用作为全卡池控制组。
- ST06-M1主动的“最多2张”必须接受0目标；甲斐姬免除迦具土印刷士气费用时仍要生成自然语言发动选项。最终专项13/13，Focused/Batch规则均1089/1089，公开声明、私区事务、试炼完成和原子零旧入口门禁全绿。

## Prompt/PendingActivation 事务绑定与孤儿自愈样例

- `PromptActivationBindingRegressionTests`固定种子`60447342`：〈安卡神碑〉A3的Prompt必须携带与唯一PendingActivation一致的`ActivationId/SourceInstanceId/SourceCardId/Step/CreatedRevision/Controller`；重连快照必须原样恢复该绑定。`CreatedRevision`必须低于正常增长后的当前revision仍合法，不得被当作当前修订乐观锁。
- 〈阿伊〉实例不得替代安卡来源绑定，Prompt打开后的普通`playCard`也必须拒绝且不离手/不占格。错来源、陈旧step或CreatedRevision、局部回显和重复提交都不得移除当前合法Prompt，成功提交只弃牌恰一次。全部六字段都省略的旧客户仅在服务端内部完整对账后兼容。
- `skip`取消必须同时清理Prompt和Activation，不休整安卡、不弃牌、不恢复或消费事务两次；来源离开其合法主动区域时，下一命令前取消并允许结束回合。合法Prompt+Activation必须继续阻止回合；Prompt-only、Activation-only、重复ActivationId或绑定冲突必须在一次对账中自愈，不得留下第二次操作才能解锁的孤儿。
- UI静态契约必须同时锁定`GameBoard.hasBlockingPrompt`、同步清理普通交互状态、中央命令门禁、手牌/战场入口门禁以及`PromptOverlay`/棋盘直选的六字段回显。Prompt是否最小化不得改变阻塞信号。
- 全池回归除精确夹具外，必须覆盖公开主动、触发声明、响应声明、匿名对手手牌、复合手牌打出、效果生成打出和重复效果；对账不能因当前revision、轮次或来源合法离开后仍由candidate/stack/committed-parent承载而误清。

## Bug版本诊断与WebSocket generation恢复样例

- `BugDiagnosticPersistenceTests`固定临时`platform.json`：新报告必须分别持久化client/server/engine版本，engine不得回退静态`1.0.0(.0)`，旧客户端省略版本得到`unknown-client`，旧数据缺新增字段仍可读取。`WebSocketConnectionGenerationTests.HealthAndLegacyBugSubmissionExposeAuthoritativeBuildsAndWhitelistDiagnostics`通过临时HTTP服务把恶意哨兵同时放入扩展`token/privateHand/ip`字段和合法HTTP/API/WS/恢复/认证/维护/close reason字段；服务端须把非法枚举与reason归一为`unknown`且哨兵不得进入持久化文件，`/health`须提供服务端与可追溯L12引擎版本；测试不得访问生产API或真实报告文件。
- `WebSocketConnectionGenerationTests.NewSocketGenerationFencesTheOlderLiveSocketWithoutRestartingTheServer`使用两个真实`ClientWebSocket`和同一账号：第二连接generation必须严格递增，认领决定为`fenced-active-session`，旧连接先收到`sessionSuperseded/newer-connection-generation`再关闭；接管后旧socket尝试`createRoom`不得改变权威状态，新socket同步仍无房并可正常建房，服务端诊断同时保留成功decision与`older-connection-fenced`原因。`GmSandboxTests.ConnectedSecondTabAtomicallyFencesTheOldSessionAndKeepsTheAuthoritativeRoom`另锁定活连接房间原子换座和旧session迟到命令失败。
- `GmSandboxTests.DisconnectDuringPromptRestoresTheOwnersPrivatePromptBeforeRecoveryAck`按实际掷骰方断线：新session的`gameState.state.prompts`必须先恢复私密Prompt，随后ack的generation/revision一致且`pendingPrompt=true`。既有断线恢复夹具改走`RecoveryStateWithAckAsync`，排位夹具同时断言`gameState.rankedClock`和`rankedClockRestored=true`。
- 排位断线期限在本批当时仍是内存权威4分钟：3分59秒重连继续、恰好4分钟与4分钟+1毫秒均先完成权威判负再返回恢复状态；服务进程重启持久化由后续`BUG-20260906-248`夹具覆盖。客户端UI契约必须要求只有`snapshot-acknowledged`才能从任意站点路由进入`/game`，并锁定close code/reason、heartbeat/pong、retry、generation/恢复阶段、三端版本展示和禁止`navigator.userAgent`。
- `check-client-release-version.mjs`固定开发空版本=`dev`、正式commit/semver合法、正式空值或`dev`抛错；`vite.config.ts`必须实际调用同一helper。所有正式构建入口都必须注入权威commit，不能在表单层继续用fallback掩盖构建配置错误。

## 排位重启恢复与结算Outbox样例

- `RankedPersistenceRecoveryTests`以临时`matches.db/platform.db/platform.json`运行，禁止读写生产数据。短重启夹具保留原match/room、双账号精确构筑、私密Prompt及当前决策方；服务停机3分59秒后总操作/单次操作剩余值不得减少，但双方重连窗从最后checkpoint继续，恰好4分钟必须先按双方掉线作废。好友局断言不创建排位runtime或outbox。
- 命令和权威GameOver分别在`before-ranked-command-commit`、`before-ranked-final-commit`故障注入，断言事件、最终match、completed runtime及outbox要么全有要么全无；`before-ranked-runtime-batch-commit`同时捕获两个房间，失败时两条generation都不前进。受控先写N+2再写N+1必须保留新快照，completed N+1也不能被旧active N拖垮同批其他房间。
- 平台`before-commit`失败后内存与SQLite都回到最后提交快照，outbox保持pending；清除故障并新建Store后两席只结算一次。`before-ranked-outbox-ack`模拟平台成功/Recorder确认失败，重启按matchId与完整载荷复核后只ack不重复加分。恢复旧平台快照时，applied outbox只有在同match完全缺失且双方没有更晚依赖结算时才补写；冲突失败须累计attempts并保留last_error，成功复核清错。
- 将一条outbox改为坏JSON时该项必须转quarantined，后续合法项继续结算；将一条runtime数组改为null、另一条初始状态删字段时，两项分别隔离且第三条健康对局仍恢复。两个active match共用room code时只隔离后者，清理必须按房间对象引用匹配，先恢复房间及其占位session保持可认领。
- 冻结恢复夹具先让disconnect checkpoint失败，再让第一次认领checkpoint继续失败：两次都不得开放命令；清除故障后必须先从Recorder重放权威状态、成功写入新连接generation，随后同一Prompt命令才可执行。恢复重放对每个命令严格比对`accepted/revision/state_hash`，任何不兼容只作无效/隔离且不得改变七曜。
