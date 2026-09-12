<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { cancelFriendInvitation, inviteFriend, l12State, resolveFriendInvitation, spectateRoom } from '@/l12/net'
import { friendApi, login, platformState, register, type PlatformPresence } from '@/l12/platform'
import SiteIcon from './SiteIcon.vue'
import L12SettingsModal from './L12SettingsModal.vue'
import MaintenanceTicker from './MaintenanceTicker.vue'

const siteBrandIcon = '/favicon.png'
const releaseVersion = String(import.meta.env.VITE_APP_VERSION || 'dev')
const updateEntries = [
  {
    date: '2026-09-12', title: '效果结算、支付取消与场上响应更新', version: releaseVersion,
    sections: [
      { title: '页面与对战操作', items: [
        '打出卡牌时需要手动选择士气、陵墓守卫、符文或墓地费用的步骤，现在都可以直接“取消打出”；取消后手牌、场上格位和各类资源保持原样。',
        '取消入口统一显示在资源支付控制条或选择弹框底部，并会立即单独提交，不会被误算成已选择的资源或卡牌。',
        '费用条件变化、重连恢复或旧选择失效时，不再吞掉选择弹框或让对局卡住，而是回到仍可取消的正确选择步骤。',
        '主动效果按钮与实际提交现在使用同一份士气费用：主宰效果免耗、〈傲慢之罪〉附加费用等变化会同时反映在按钮可用性和最终支付上。',
        '需要单个对象或位置的主动效果，在没有合法候选时会直接置灰并显示与提交一致的原因；选择完成前不会先扣费，重连后仍可继续，重复提交也不会再次支付。',
        '已经分段的卡牌效果会在真正结算后再播放对应分支的卡牌动效；发动声明不会重复播放。被无效、没有合法处理对象或未能完成时，对局记录会显示实际结果，重连和回放后仍保持一致。',
      ] },
      { title: '主动效果与费用判定', items: [
        '《须佐之男》的前排强化与《草薙剑》置入前排、《山河社稷图》的弃牌检索、《草薙剑》的降费与强攻两个分支，都会按当前场面列出真实可选对象；没有对象或位置时不再出现可点但必然失败的按钮。',
        '《奥尔加》《众神之乡》《安卡神碑》的单目标主动效果同样按当前前排、兵力、衍生卡与休整状态筛选；选择在恢复对局后仍有效，目标过期时不会误扣士气。',
        '每个能力中的冒号只分隔该能力自己的费用与效果。唯一效果对象存在时必须由玩家明确点选；没有效果对象时只跳过无对象的效果段，不再被当作费用无法支付。',
        '《奥尔加》的远程进攻无损、手牌自伤减费、场上弃置减兵三个能力已完全分开：没有敌方前排时仍会支付“弃置此军团”的费用，但不会生成无目标的减兵步骤。',
        '《传奇的拉格纳》《无情者哈拉尔》《血斧艾瑞克》《齐格鲁德》《奥尔加》《卡纽特大帝》的自伤减费只属于各自的手牌打出能力；选择发动时会先对我方主宰造成1点伤害，再让登场费用-1，之后才让军团离开手牌并进入登场流程。',
        '上述军团的其他登场、阵亡、伤害后、位移与进攻能力不再被自伤费用影响；《无情者哈拉尔》和《血斧艾瑞克》的必选对象在存在合法对象时必须点选，没有对象时才跳过对应效果。',
        '《莫德雷德》等只有一个合法效果对象的能力不会自动略过选择；《伊西斯》选择三张陵墓守卫的步骤明确作为费用处理，费用对象和后续效果对象不会再混淆。',
      ] },
      { title: '打出费用与响应判定', items: [
        '《步行者罗洛》只提供当前确实付得起的墓地代表数量；费用不足或选择过期时不会扣除士气，也不会让卡牌离开手牌。',
        '《槲寄生符咒》可以消耗3符文把费用减至0并正常打出；符文支付也可以在确认前取消，取消后不会消耗符文或打出卡牌。',
        '《落穴陷阱》只响应真实位于战场的军团登场效果：《草薙剑》仍在圣物区、孙悟空仍在主宰区时不会被误判；它们实际成为场上军团后才按军团登场处理。',
        '《土方岁三》的登场效果改为一次选择最多2个对象：选择2个时至少一个费用不高于1，另一个费用不高于2；只有一个合法对象时仍由玩家明确选择。',
        '《阿麦金》的“仅具有【彼界】单一特征”会把阵营和附加特征分别判断：圆桌骑士、晋升者、杨戬专属及哪吒专属均属于额外特征，职介和试炼值不属于特征。',
        '装备《万物统御之戒》后，通用特征会随持有者改为当前阵营特征；因此只有这一项有效特征的通用卡可按对应单一阵营处理。',
        '费用筛选中的0费会包含《自然馈赠》等没有印刷费用的非主宰卡；卡牌费用、主宰血量和军团兵力最低显示为0，不会出现负数。',
      ] },
    ],
  },
  {
    date: '2026-09-10', title: '卡效结算、完整响应提示、移动端与牌库更新', version: '5c2c7d0a7771f7d87af22c456f2b73795953c05a',
    sections: [
      { title: '卡牌效果与对战规则', items: [
        '《草薙剑》等费用上限判断不再把无费用军团当作0费；真正0费和合法减费对象仍正常处理。',
        '《雷神之锤》发动入口与墓地代表份数一致，《渴求死亡的勇士》可以代表复数费用；同质士气自动支付，不同支付后果仍保留选择。',
        '《梅杰德》按对方回合触发；《西施》可使用支付自身后腾出的合法位置；《嫦娥》与天廷阵营效果独立排入同次时点，并保留发生时的零士气资格。',
        '《孟婆》两个主动分支共用回合次数；多张《亚里士多德》的减费累计；《诸葛亮》会先让操作者看到下一张天灾，再决定后续调整，不向对方泄露。',
        '《杨戬》私密返牌不再通过对局记录泄露身份；《佣兵部队》声明时支付弃置费用，被无效后不会返还。',
        '《陵墓构造体》战斗阵亡后正确处理原有守卫与3个休整守卫；《孙悟空》作为军团离场时始终回到主宰区。',
        '《黯陨晨星》选择的免费主动战术状态持续本回合，但不免除效果正文另列的费用；天灾伤害不受玩家卡牌的伤害增减或替换影响。',
        '同时触发的效果可按发动顺序入栈并逆序结算；响应可以声明任意合法堆叠对象，同方连续发动后再让过，不局限于栈顶。反击战术仍不能响应天灾。',
      ] },
      { title: '对战页面与恢复', items: [
        '响应说明会显示发动方、完整独立效果、公开目标及所在格位；最小化后以金色高亮实际目标实例，多张同名军团也能区分，费用选择期间仍保留上下文。',
        '《宫廷魔术师》等公共场上选择可以直接点选战场盖伏位置，同时不会公开盖牌身份。',
        '修复短画面底部裁切；窄屏对战、回放与牌库编辑器共用横屏适配，弹框、点击和动效坐标同步，输入和退出页面可恢复正常布局。',
        '读取对局存档时会正确还原双方战场；异常存档不再默认为空场继续运行。',
        '手牌与军团尺寸、装饰框、圣物横卡框和试炼数字空间完成调整；对局记录保留实际变化和可点卡名、区分我方与对方，并删除重复流程文本。',
        '胜负结算会保留到玩家主动返回大厅，双方可独立离开并继续开局；房主退出不会带走另一方的结算页面。',
      ] },
      { title: '牌库、排行与大厅', items: [
        '牌库保存和删除以服务器成功结果为准，登录或其他标签页的旧缓存不再覆盖新结果；额外区列表与主牌库风格统一，横卡详情不会超出容器。',
        '公共牌库与对局档案共用构筑查看，包含额外区、主动与反击标签，并支持复制码、导出牌库图和复制到我的牌库。',
        '排行榜会纳入已结算的新格式对局并自动刷新；没有段位或称号时不再显示占位文字。',
        '预约维护提示与提前广播会同时开始；撤回好友邀请后对应弹框自动失效，其他邀请不受影响。',
        '《安格斯·麦·奥格》的第二张试炼可以按具体卡牌实例打开操作，进度变化后已打开的操作面板会同步刷新。',
      ] },
    ],
  },
  {
    date: '2026-09-09', title: '卡效分段结算、反击响应与动效文案更新', version: '04fb70c6cc1f8f17b2e37394442b8b6f9bbfc891',
    sections: [
      { title: '卡牌效果与结算顺序', items: [
        '《花魁的馈赠》先完成检索，再选择是否活跃士气；支付这张牌后刚变为休整的士气也可选择。拒绝后段、目标失效或后段被无效，不会撤销已完成的检索或退回已支付费用。',
        '《图特摩斯三世》进攻时和阵亡时先结算全体兵力-1000，再根据当时的实际兵力选择击杀对象；原先超过1000、减兵后符合条件的军团不再遗漏。',
        '《本多忠胜》进攻时即使没有可击杀的军团，也会正常令对方所有军团本回合费用-1；之后只对当前费用为0的合法对象提供击杀选择，没有对象不再卡住流程。',
        '《神妙行军》我方没有前排军团时，仍可在满足后段条件时选择返还2士气，击杀对方兵力不高于6000的军团。前段未生效不再阻止独立后段；返还费用支付后不会因击杀目标失效而退回。',
        '《海伦》《霍列姆赫布》在同一次战斗中选择不发动替死后，不会反复弹出同一选择；新的独立战斗仍按规则提供选择，不会把一次拒绝记成永久拒绝。',
      ] },
      { title: '响应、防御与士气支付', items: [
        '《落穴陷阱》只响应当前正在发动的军团登场效果，普通登场和晋升登场的独立效果分别判断；不会沿着响应链错误地无效《绝对防御》等其他效果。',
        '《伏击》可以响应对方发动的《伏击》等合法效果；反击战术仍不能响应天灾，进攻专用反击的使用方向不因此放宽。',
        '《帕西瓦尔》等效果引发反击时，抵挡与支援按钮按这一次进攻的实际防御方显示，不再错误地按当前回合归属显示；主宰防御的手牌选择同步修正。',
        '《腐秽大地》生效时，即使没有可用士气也能免费盖伏反击战术；卡牌显示费用、按钮可用性与实际支付保持一致，天灾免费不会消耗其他效果给予的免费次数。',
        '军团位移等操作的资源判断纳入本回合可合法使用的活跃《陵墓守卫》和临时士气；不再出现实际能支付但按钮不可用的情况，休整或隐藏的陵墓守卫不计入。',
      ] },
      { title: '对战动效与后台编辑', items: [
        '多选项效果按实际选择的分支显示文案，多段效果按正在执行的段落显示，不再用整段全文代替所有分支与步骤。',
        '后台动效编辑器分别列出段序号与选项分支，可独立修改、预览和恢复默认文案；支持直接回车换行，换行不会误触保存。已保存的动效覆盖继续按对局版本冻结。',
      ] },
      { title: '录像与服务器维护', items: [
        '本次清理与Bug无关的旧录像载荷，保留账号、构筑、战绩、排位结算及关联Bug证据。被清理的录像不再播放，相关战绩与构筑仍保留。',
        '发布文件与部署前备份改用独立存储空间，并增加容量、完整性和失败保护检查，避免发布占满在线数据库所在磁盘。',
      ] },
    ],
  },
  {
    date: '2026-09-09', title: '主宰勘误、操作命中、排位分析与维护沙盒更新', version: 'eb199add',
    sections: [
      { title: '卡牌效果与卡图', items: [
        '《迦具土》血量更新为8；《荷鲁斯》《迦具土》《安格斯·麦·奥格》替换为最新卡图，图鉴、构筑和对战中的卡图同步更新。',
        '《安格斯·麦·奥格》改为我方回合1次，推进试炼进度时可获得1符文。触发后可选择不发动，不发动不消耗次数；发动后即使被无效也计入本回合次数。旧的完成试炼得符文效果停止使用。',
        '《安格斯·麦·奥格》保留原有收录，并新增ST06彼界阵营预组（勘误收录）；图鉴收录产品下方可查看旧效果全文。',
        '《斯卡哈》消耗符文的进攻效果成功结算后，本次进攻立即获得进攻无损，兵力增加与本回合持续效果正常生效。',
        '晋升登场纳入对应登场响应范围；《落穴陷阱》对普通登场时与晋升登场的独立效果分别响应，弹框会列出正在响应的来源、时点和效果内容。',
      ] },
      { title: '对战操作与构筑详情', items: [
        '修复战场装饰层遮挡军团按钮点击的问题，避免看得到按钮却按不到；已检查不同窗口尺寸和缩放倍率下的实际点击。',
        '牌库编辑器改用图鉴同款卡牌详情，保留效果和卡牌信息，隐藏收录产品与勘误记录；窄屏长文本可滚动完整阅读。',
      ] },
      { title: '排位单卡分析与录像', items: [
        '单卡分析仅统计符合新版分析条件的排位对局，可按主宰和卡效版本查看；对比会考虑双方主宰、先后手和版本，样本不足时不再把不确定结果显示为确定优势。',
        '分析仪表盘展示样本场次、玩家数量、携带表现、使用路径和对阵切片；非排位不再新增单卡分析明细，旧数据不补做新版分析。',
        '玩家可查看自己最近30场已结束对局的录像。休闲与好友房录像仅在双方都已无法从最近30场入口查看后按日清理，保留战绩、构筑和结果，并保护进行中对局及未处理Bug证据。',
        '不具新版分析资格的已结束休闲、好友及旧排位对局，单卡分析明细超过10天清理；合格排位明细先汇总再按30天保留策略清理，不因此删除战绩或构筑。',
      ] },
      { title: '维护与管理员测试', items: [
        '具有运营管理权限的管理员可在维护期间建立并操作沙盒，预约维护到点不会再中断沙盒；普通玩家的新对局限制保持不变。',
        '本次部署保留已有维护计划与维护状态，不自动开放普通对局；正式版本切换期间的临时发布拦截独立于维护计划。',
      ] },
    ],
  },
  {
    date: '2026-09-08', title: '卡牌结算、账号治理、对战界面与长局稳定性更新', version: 'eac1ec9',
    sections: [
      { title: '卡牌效果与结算', items: [
        '《符文之力》结算时先获得1符文，再由玩家决定是否消耗1士气查看牌库顶部3张；存在不同士气支付结果时使用统一资源选择，不会重复收费或生成空选择。',
        '《兰斯洛特》的击杀时窗口补齐实际效果选项；《高杉晋作》改为先抽牌，再在仍有合法目标时选择费用降低对象，没有目标也不会吞掉抽牌或卡死结算。',
        '《雷神之锤》允许墓地中的《渴求死亡的勇士》分别代表复数费用；《阿麦金》公开牌库顶卡，只有按当前规则仅具有【彼界】单一特征时才可加入手牌。',
        '所有可选公开触发在条件不成立或没有候选时由框架安静跳过，不再生成无法操作的空弹框；反击战术不会获得响应天灾的窗口，普通合法响应仍按原规则处理。',
      ] },
      { title: '排位、赛事与账号', items: [
        '后台可设置排位总操作时间、单步时间、断线宽限、调度与天灾选择时限；新建对局冻结当时配置，进行中的对局和重启恢复不会被后续改动篡改。',
        '排位广播可设置显示时长、进入大厅后的延迟、播放间隔、连胜与终结连胜门槛、最低段位及各类广播开关，前端按后台配置播放并避免刷新后重复领取。',
        '全局赛事管理员可查看全部玩家创建的赛事（包括仅分享链接赛事），并按既有权限执行开赛、轮次控制、判罚和归档；普通玩家及单场工作人员权限不扩大。',
        '用户名统一为2–11个可见字符；违规部分在公共区域以星号遮蔽，命中的存量账号下次登录必须先改名，改名完成前不能进入业务页面或连接对战。',
      ] },
      { title: '对战界面与阅读体验', items: [
        '当前16:9对战布局重新收容双方战场、方形场格、15张手牌、试炼额外区、常驻计时与玩家摘要；右侧玩家信息保持完整，并为对局记录释放更多高度。',
        '关键词每行最多3个并向上换行，状态图标与卡牌信息不会越出容器；选中卡牌、效果弹框、回放焦点、战术标签和对局记录统一使用同一套可读呈现。',
        '取消全项目固定最小字号，改为按信息层级和可用空间响应式排布；保留全站黑体、深色下拉与文字完整性检查，避免放大后侵占相邻组件。',
      ] },
      { title: '操作响应与连接稳定性', items: [
        '对战操作不再等待其他玩家或观战者的网络发送完成；网络较慢的单个连接会独立处理，提示、拒绝、恢复和终局消息仍保持正确顺序。',
        '连续产生的普通对局快照会自动合并为最新状态，避免慢连接积压拖累整间房；超过发送时限的连接会断开并按原有恢复流程重连。',
        '操作提交加入稳定请求标识。连接中断后会安全重试同一次操作，服务器重启恢复后也不会把同一点击重复结算。',
        '服务端发现孤立或失效的选择提示时会安全清理并写入可回放的权威事件；被拒绝的旧操作不会让对局卡死，重启恢复后也能还原清理结果。',
      ] },
      { title: '快照、观战与回放', items: [
        '支持增量对局快照并定期自动发送完整快照，减少长局中玩家和观战者每一步需要传输的数据；不支持新协议的旧页面仍可继续使用完整快照。',
        '对局记录改为完整行动日志配合周期检查点，普通操作不再重复保存整场状态；长局回放、断线恢复、排位结算和后台溯源保持可用。',
        '玩家界面的近期操作记录保留最近128条，完整历史继续保存在回放中，避免日志无限增长拖慢每次操作。',
      ] },
    ],
  },
  {
    date: '2026-09-07', title: '登录、观战与赛后重连紧急修复', version: 'b522f72052c1240aabf25e25b59e3d6dca9de89a',
    sections: [
      { title: '登录与连接稳定性', items: [
        '修复反向代理导致玩家共用登录限流的问题：其他网络来源的密码输入错误不再连带阻止你登录；账号和来源的防爆破保护仍保留。',
        '修复排位结束返回大厅后残留计时状态、导致房间误冻结和持续重连失败的问题。已完成的胜负及排位结算不会被重复处理。',
        '从对战页返回大厅、查看记录或切换已登录页面时，不再主动断开正常连接；登录失效和被其他页面接管仍按安全规则处理。',
        '连接假在线、长时间收不到服务器回复时自动重连；恢复快照异常时采用间隔重试，避免连续请求进一步拖慢服务器。',
      ] },
      { title: '观战与长对局恢复', items: [
        '修复普通观战和赛事观战在进入、刷新后反复提示“权威恢复快照尚未完整到达”的问题，补齐观战者的公共房间信息，不公开私密手牌或牌库选择。',
        '恢复单场排位时只读取该场记录，服务器启动时逐场恢复；不再同时加载所有对局的完整历史状态，降低长局恢复时的内存压力。',
        '降低每次快照校验的临时内存分配，保持原有校验结果、回放记录和结算数据不变。',
      ] },
      { title: '士气与大厅提示', items: [
        '被效果锁定、暂时不能活跃的士气上方显示半透明小锁，便于与普通休整士气区分；不会改变士气支付或返还规则。',
        '排位快讯只领取本次订阅后的实时新消息，已经领取的消息不会因刷新、切换页面或确认失败反复播放；跨页面继续剩余播放进度，播放确认在后台重试。',
      ] },
    ],
  },
  {
    date: '2026-09-07', title: '远程判定、荷鲁斯原格登场与迦具土交互修复', version: '637a489f0a4d1f807e5ed4d8621e76ef7399fcd2',
    sections: [
      { title: '远程军团与卡牌效果', items: [
        '远程身份按当前有效职介及明确的远程能力判断：弓手、术师及“视为弓手／术师”生效的军团属于远程；攻城投石车等明确拥有远程能力的特殊职介军团也属于远程，但不会把所有特殊职介都算作远程。',
        '荆轲、锡瓦的卡巴、夺命诗人埃吉尔、服部半藏、彭忒西勒亚、克劳迪娅、聂隐娘的前排增距效果不再使其被误认作远程军团；源义经的后排增距同样不改变远程身份。原有攻击距离与远程进攻无损效果保持生效。',
        '阿塔兰忒·晋升在后排视为弓手时才属于远程。特勒马科斯、埃涅阿斯·晋升、猎神的赐福及阿尔忒弥斯涉及远程军团的筛选和触发统一使用正确身份，离场触发按离场前的有效状态判断。',
      ] },
      { title: '费用选择与对局操作', items: [
        '修复荷鲁斯已允许原格登场、界面却无法点击的问题：作为费用弃置的军团所在格会正确高亮并可选择，两种费用方式都适用；未列为合法候选的占用格仍不可选择。',
        '迦具土在我方军团进攻或被进攻时显示完整效果及费用说明，保留“消耗1士气／弃置1张手牌”的选择，“不发动”与“确认选择”并列显示，不再用笼统时点文字代替效果。',
        '迦具土确认使用士气支付后，多枚完全等价的普通士气不再要求额外选择具体哪一枚；普通士气、临时士气、黑色莲花、陵墓守卫等存在支付结果差别时，仍保留玩家选择。',
        '士气保持每行3枚的固定列位置，不足3枚或最后一行从左向右排列，不再单独居中。我方向下换行、对方向上换行，圆标尺寸和完整数量显示不变。',
      ] },
    ],
  },
  {
    date: '2026-09-07', title: '荷鲁斯双费用、效果弹框与沙盒溯源更新', version: 'cce59c9f3e66c49315ce51f36de762f0ff69105b',
    sections: [
      { title: '卡牌效果与对局规则', items: [
        '荷鲁斯可选择两种费用：弃置我方战场2张陵墓守卫，或消耗1士气并弃置我方战场2张军团。两种方式共用我方回合1次，随后让墓地兵力不高于2000的太阳城军团休整登场。卡牌图鉴与实际效果同步更新。',
        '荷鲁斯可弃置休整的陵墓守卫；选择士气费用时，同一守卫可以先作为士气休整再作为军团弃置。支付费用后腾出的格子可以用于登场，信仰狂热者的免费复制仍不支付费用、不占正常次数。',
        '加拉哈德、芬恩等登场效果弹框展示对应的完整效果文本。只有发动与不发动的弹框可直接点击决定，不再多点一次确认；真正的模式、目标和多选流程仍保留必要选择。',
        '每场对局双方合计只能发起一次平局申请。被拒绝、取消或过期后不能重新申请，重连不会重置；当前待处理申请仍可正常回应。',
      ] },
      { title: '对局界面与连接', items: [
        '选中卡牌面板在普通对局、弹框打开和最小化时保持相同位置与外框，卡图增大约15%，阵营与职介标签按内容自然排列。',
        '士气圆标保持32px，每行3枚、最多展示12枚，超出部分保留总数；我方向下、对方向上展开。资源组随内容高度居中，五个资源罐保持单行，试炼区域避让圣物。',
        '回放中的主宰补充效果说明，推进、回退或跳转时自动展示当前行动的公开来源卡；卡牌目录暂时加载失败不再阻止整场回放。',
        '修复登录验证暂时失败、连接取消及恢复握手超时后一直显示未连接的问题。临时网络故障会自动重试，登录失效仍需重新登录。',
        '排行榜各处主宰头像统一为44px方形；沙盒入口按我方牌库、对方牌库、测试账号、天灾模式排列，并减少空白。',
      ] },
      { title: '后台管理与问题溯源', items: [
        '有权限的管理员可直接执行后台操作，不再需要第二人批准；权限、版本冲突检查和操作审计仍保留，历史待批准操作不会自动执行。',
        '已删除账号收纳到刷新左侧的“已删除”入口；单卡影响分析可按我方和对方主宰名称筛选，无需手填编号。',
        '新沙盒对局保存后台专用回放，包含GM操作后的状态，用于定位沙盒Bug。玩家不能查看这些回放，也不计入排位和单卡统计。',
        '沙盒回放每周清理一次超过7天的记录，实际保留约7～14天，正在使用的沙盒不清理。回放到期后保留Bug关联标识，并明确提示回放已过期。',
        '沙盒断线后保留5分钟重连时间，超过宽限且没有真人在线时结束沙盒，回放仍按最后活动时间保留；在线玩家或观战者不会被清理。服务器重启后的旧沙盒录像仍可在后台排查。',
      ] },
    ],
  },
  {
    date: '2026-09-07', title: '卡牌结算、对局操作与后台维护更新', version: '2a0e938a012f4c5931ba2afe4a6fd2112e88e73e',
    sections: [
      { title: '卡牌效果与构筑', items: [
        '猎杀时刻的效果文本已更新：将墓地4张卡牌自选顺序返回我方牌库底部，击杀对方1张兵力不高于6000的军团。回牌是效果而非发动费用；墓地不足4张时不回牌，仍可发动击杀。击杀目标失效不会撤回已经完成的回牌。',
        '雷神索尔赋予的冲锋正确覆盖效果登场的阿斯加德军团；黑胡子的完整登场效果被绝对防御无效后，不再继续抽牌；卡纽特大帝的声明与效果全部处理完毕后才继续翻天灾。',
        '乾坤·阳等原本兵力判断正确识别草薙剑的军团形态；步行者罗洛能识别魔戒赋予的阵营；哮天犬衍生物阵亡消失后仍会正确加入休整士气。',
        '荷鲁斯与阿尔伟达可选择支付费用后腾出的合法登场格，取消预选不会提前扣费；安德华拉诺特的构筑上限固定为1张。',
        '勇士等可代表多张的墓地卡改为逐张选择代表数量，不再堆叠大量组合；会排除无法完成所需数量的选项。没有符文时，消耗符文的阵营能力明确显示不可用。',
      ] },
      { title: '对局界面与交互', items: [
        '效果弹框与响应提示展示完整的对应效果，保留必要规则说明；太阳城相关提示使用正确来源。不发动与确认选择并列且同尺寸，长文字、窄窗口和卡牌候选均可滚动查看。',
        '双方手牌、战场、常驻回合计时及左侧详情重新分区，避免互相遮挡；双方手牌保持相同尺寸，无计时房间仅显示回合状态。士气每行3枚、最多显示12枚圆标，超出数量仍完整计数；我方向下、对方向上增加，资源组随行数相对牌库和墓地居中。',
        '选中卡牌下方保留设置、好友和对局工具三个等宽图标入口；进攻目标选择旁可取消尚未提交的选择，试炼操作与军团其他动作对齐。',
        '军团阵亡时先展示战斗伤害与退场动画；没有伤害数值的效果击杀显示“击杀”，不再用卡面兵力猜测伤害。返回手牌、位移及替代存活不会被误显示为普通阵亡。',
        '修复权威快照已经恢复却仍停留大厅的问题；主动返回后，不会被同一对局的重复快照再次拉回。排行榜主宰对阵行高与列宽统一，最强称号说明入口更清晰。',
        '全站文字统一使用黑体，通过字号和字重区分层级；下拉菜单保持深色，设置控件在窄窗口中不会互相挤压。',
      ] },
      { title: '好友与对局治理', items: [
        '对局工具提供Bug反馈、申请平局、屏蔽与举报入口，好友功能可在对局中打开；屏蔽用于阻止该玩家后续好友申请，可在好友管理中解除。',
        '真实双人休闲与排位对局可申请平局，由另一方决定是否接受；排位和局不增减双方分数，也不记为无效对局。重复响应、迟到确认和断线恢复不会重复结算。',
        '后台新增独立对局举报管理，保留申请、处理和操作记录；赛事对局继续使用裁判流程，机器人及GM沙盒不开放双人平局申请。',
      ] },
      { title: '维护与资讯后台', items: [
        '后台新增独立“维护服务器”按钮：立即停止开启新对局，已开始的对局可以继续完成和重连；管理员填写预计维护小时数，对战子页持续滚动广播。预计时长结束不会自行开放，需明确结束维护。',
        '即时维护与预约维护分开保存，普通配置保存或回退不会误清除即时维护；解除即时维护后，如预约仍在生效，会明确提示对局尚未开放。维护公告和倒计时会持续刷新，短暂请求失败保留最后有效内容。',
        '资讯后台可手动预览并导入官方摩点项目的历史及新增动态为草稿，正文图片保存到本站素材库；重复导入会识别已有内容，本地编辑冲突需要确认，不会自动发布或覆盖已发布文章。',
      ] },
    ],
  },
  {
    date: '2026-09-06', title: '最强称号规则、排位准备计时与对局体验更新', version: 'cb14e6b07d36fd8df1d1961b6a094d4f0b2577a9',
    sections: [
      { title: '称号与排行榜', items: [
        '“最强”称号固定按近30日有效排位评选：通常至少40场，冷门主宰降为20场，并要求5个参赛日与10名不同对方；少于6回合、掉线结局和无明确胜负的对局不参与评选。',
        '最强称号采用剔除本人对局后的主宰基准与贝叶斯修正胜率，降低小样本连胜影响；镜像局按整场去重。同分依次比较场次和胜场，最终只产生一名持有者。排行榜和“我的”均可打开完整规则。',
        '玩家榜按排名、昵称、阵营、段位、称号、最擅长主宰、七曜值、场次、战绩、胜率分列；搜索栏收窄，主宰头像统一为方形，主宰对阵数据行保持相同高度。',
        '后台同等级段位共用数值设置，三个派系仍可分别命名；历史荣耀仍以赛季结算结果为准，当前赛季不提前进入历史。',
      ] },
      { title: '卡牌与支付', items: [
        '信仰狂热者统一免除可复制主宰能力的全部费用，且不占用原能力正常次数；荷鲁斯的免费复活、杨戬的抽牌回牌、梅杰德的减兵力、瓦尔基里与洛基的能力、天照、须佐之男和雷神索尔均纳入回归检查，目标与登场空间限制仍然保留。',
        '神力中的太阳城、阿斯加德与高天原相关复制能力一并检查；太阳城检索和英灵回收按当前有效特征判断候选，不再遗漏因效果获得阵营特征的卡牌。',
        '普通士气、临时士气及陵墓守卫等可选资源统一参与支付选择；有多种合法组合时由玩家决定。彼界阵营效果也可以手动选择要消耗的士气。',
        '士气每行最多8枚，我方向下、对方向上增加行；黑色莲花产生的临时士气使用莲花专属标识，已选择或不足的资源不能被重复用于同一组费用。',
      ] },
      { title: '对局操作与记录', items: [
        '排位手牌调度显示先后手与1分钟倒计时，超时保留原手牌；天灾禁选每一步同样限时1分钟，超时由服务器从当前合法候选中选择。准备阶段不扣除正式对局总时限，刷新与重连不会重置选择时间。',
        '修正我方场面外框的样式传递；回合玩家与双方计时常驻，并为手牌预留独立位置。右侧玩家信息完整换行显示，无段位或最强称号时留空，不再展示阵营和缺失提示。',
        '对战左下角保留设置、在线人数和连接状态。对局记录按回合分组，显示卡牌编号、名称、公开选择及结算信息，可点击查看相关卡牌；非公开选择不泄露候选与手牌。',
        'GM沙盒正确显示横版卡牌，并提供独立卡牌详情入口。各处过小的操作文字与说明统一提高到卡牌图鉴效果文本的可读字号，并调整拥挤布局。',
      ] },
      { title: '好友、设置与运营', items: [
        '收到好友申请时弹窗并播放提示音，可直接通过、拒绝或屏蔽；取消屏蔽后允许重新申请，申请方不会获知自己被屏蔽。',
        '音乐切换使用平滑淡出、换曲与淡入；音乐和音效分别控制，音乐滑杆支持更细的低音量调整。卡牌尺寸、动画速度及声音设置继续按账号保存。',
        '后台单卡分析可按双方主宰筛选，对局档案通过“查看构筑”浏览赛后牌库；长回放按页读取，避免一次加载整场记录。',
        '维护与公告新增独立长期公告，显示在大厅牌库选择区上方；维护结束时间可不填写，并可显式启动服务器。部署或短暂连接失败期间，首页继续显示最后成功加载的已发布内容。',
      ] },
    ],
  },
  {
    date: '2026-09-06', title: '卡牌交互、排位恢复与运营功能更新', version: '37f1dcc9dd68ab958841d4415f1ec52195f30dae',
    sections: [
      {
        title: '卡牌效果',
        items: [
          '陵墓构造体同时触发离场与阵亡时，现在只会召唤3张休整的陵墓守卫，不再先全部登场后进入墓地。',
          '栖木猎鹰的登场与进攻动画会分别显示对应的抽牌时点，不再使用无法区分时点的说明。',
          '荷鲁斯的士气与军团费用改为分步支付；已横置的陵墓守卫仍可继续作为弃置费用，费用离场的军团及其空出的原位置也可参与后续复活选择。',
          '信仰狂热者复制荷鲁斯时会跳过原本的士气、费用与弃置要求，且不会占用荷鲁斯正常的“我方回合1次”。',
          '兰斯洛特登场时，即使原本没有符文，也可使用同一触发流程中“寻找圣杯之旅”刚获得的符文来取得冲锋。',
          '月读可选择另一张我方或对方的活跃/休整军团进行位移；托勒密十三世只能再次发动本回合打出的上一张主动战术。',
          '海伦的代替阵亡恢复为选发；黄金圣甲虫的弃牌减兵力效果严格限制为我方回合1次。',
          '梅林等“选择一项”效果会先公开所选项目，再询问绝对防御是否响应，响应方能够看见具体选择。',
        ],
      },
      {
        title: '对局与房间',
        items: [
          '好友房现在可以由房主选择并固定当前赛季天灾规则，未选择时仍沿用普通好友房规则。',
          '进攻选择阶段只高亮合法的被攻击对象；挑衅等关键词被无效时保留文字并显示红色叉号，避免误认为效果被移除。',
          '统一放大“不响应”按钮；临时士气改用白色十二军团标志显示，并在休整后正确消失。',
          '胜负结算页面不再自动离开，双方可查看结果并在点击返回后退出对局。',
        ],
      },
      {
        title: '排位与维护',
        items: [
          '排位对局在服务器重启后可恢复进行中状态、双方总时限、单次操作时限、掉线窗口及尚未处理的交互。',
          '对局记录与排位账本增加持久化结算对账；重复结算会保持幂等，异常旧记录会被隔离而不会污染正常战绩。',
          '后台新增计划维护与恢复服务器功能：按设定时间停止新对局、循环广播维护预告、提醒进行中的玩家，并在维护开始时废弃未结束对局。',
          '维护即将开始或维护期间，玩家点击开启对局会明确提示对局功能已关闭；已经进行的对局在正式维护前不受开局门禁影响。',
        ],
      },
      {
        title: '界面与设置',
        items: [
          '排行榜统一使用主宰头像并压缩主宰对阵行高；对局内的回合标识和双方计时常驻显示，玩家信息栏强制完整展示。',
          '加入官网与对局音乐，并提供音乐、音效、卡牌尺寸和动画速度设置；登录账号后会跨设备保存。',
          '本次起每次正式部署都会附带与线上版本对应的更新日志，明确列出已完成的卡效和功能结果。',
        ],
      },
    ],
  },
]

const route = useRoute()
const router = useRouter()
const mobileOpen = ref(false)
const modal = ref<'settings' | 'updates' | 'online' | null>(null)
const invitationMinimized = ref(false)
const outgoingInvitationMinimized = ref(false)
watch(() => l12State.friendInvitation?.invitationId, () => { invitationMinimized.value = false })
watch(() => l12State.outgoingFriendInvitation?.invitationId, () => { outgoingInvitationMinimized.value = false })

const mainNav = [
  { to: '/', icon: 'home', label: '主页' },
  { to: '/news', icon: 'news', label: '资讯' },
  { to: '/battle', icon: 'battle', label: '对战' },
  { to: '/decks', icon: 'decks', label: '牌库' },
  { to: '/cards', icon: 'archive', label: '图鉴' },
  { to: '/rules', icon: 'rules', label: '规则' },
  { to: '/me', icon: 'profile', label: '我的' },
]
const battleNav = [
  { to: '/', icon: 'home', label: '主页' },
  { to: '/battle', icon: 'battle', label: '大厅' },
  { to: '/battle/tournaments', icon: 'tournament', label: '赛事' },
  { to: '/decks?from=%2Fbattle', icon: 'decks', label: '牌库' },
  { to: '/battle/rankings', icon: 'ranking', label: '排行' },
  { to: '/battle/friends', icon: 'friends', label: '好友' },
  { to: '/battle/records', icon: 'records', label: '对局' },
]
const nav = computed(() => route.meta.section === 'battle' ? battleNav : mainNav)
const accountGate = computed(() => route.meta.requiresAccount === true && !platformState.account)
const authMode = ref<'login' | 'register'>('login')
const auth = reactive({ username: '', password: '' })
const authBusy = ref(false)
const authNotice = ref('')
async function submitAuth() {
  authBusy.value = true
  authNotice.value = ''
  try {
    if (authMode.value === 'login') await login(auth.username, auth.password)
    else await register(auth.username, auth.password)
    auth.password = ''
    if (platformState.account?.mustChangeUsername) await router.push({ name: 'me', query: { reason: 'username-change-required' } })
  } catch (error) {
    authNotice.value = error instanceof Error ? error.message : '登录失败'
  } finally { authBusy.value = false }
}

const onlinePlayers = ref<PlatformPresence[]>([])
const onlineCount = computed(() => onlinePlayers.value.length)
const incomingRequestCount = computed(() => onlinePlayers.value.filter(player => player.friendStatus === 'pending' && player.friendDirection === 'incoming').length)
const onlineActionBusy = ref('')
const onlineNotice = ref('')
const connectionLabel = computed(() => {
  if (l12State.connectionIssue === 'authentication') return '登录状态失效'
  if (l12State.connectionIssue === 'superseded') return '已由其他页面接管'
  if (l12State.connectionIssue === 'maintenance') return '维护中 · 连接正常'
  if (l12State.status === 'connecting') return l12State.recoveryPhase === 'snapshot-received' ? '快照确认中' : '连接恢复中'
  if (l12State.status === 'online') return '连接正常'
  return l12State.connectionIssue === 'websocket' ? '对战连接中断' : '未连接'
})

watch(() => route.fullPath, () => { mobileOpen.value = false })
function enterFriendRoom() { void router.push('/battle') }
let presenceTimer = 0
async function refreshPresence() {
  if (!platformState.account || !platformState.token) {
    onlinePlayers.value = []
    return
  }
  try { onlinePlayers.value = await friendApi.presence() } catch { onlinePlayers.value = [] }
}
const activityLabel = (player: PlatformPresence) => ({ idle: '在线 · 空闲', inRoom: '在线 · 房间中', playing: '在线 · 对局中', spectating: '在线 · 观战中' }[player.activity])
async function addOnlineFriend(player: PlatformPresence) {
  onlineActionBusy.value = player.accountId
  onlineNotice.value = ''
  try {
    const result = await friendApi.request(player.accountId)
    onlineNotice.value = result.message
    await refreshPresence()
  } catch (error) { onlineNotice.value = error instanceof Error ? error.message : '好友申请发送失败' }
  finally { onlineActionBusy.value = '' }
}
async function resolveOnlineFriend(player: PlatformPresence, accept: boolean) {
  onlineActionBusy.value = player.accountId
  onlineNotice.value = ''
  try {
    const result = await friendApi.resolve(player.accountId, accept)
    onlineNotice.value = result.message
    await refreshPresence()
  } catch (error) { onlineNotice.value = error instanceof Error ? error.message : '好友申请处理失败' }
  finally { onlineActionBusy.value = '' }
}
function inviteOnlinePlayer(player: PlatformPresence) {
  onlineNotice.value = ''
  inviteFriend(player.accountId)
  onlineNotice.value = `已向 ${player.username} 发送对战邀请`
}
function watchOnlinePlayer(player: PlatformPresence) {
  if (!player.roomCode || !player.canSpectate) return
  spectateRoom(player.roomCode)
  modal.value = null
  void router.push('/battle')
}
function answerInvitation(accept: boolean) {
  if (!l12State.friendInvitation) return
  const invitationId = l12State.friendInvitation.invitationId
  l12State.friendInvitation = null
  resolveFriendInvitation(invitationId, accept)
}
function outgoingInvitationTarget(targetAccountId: string) {
  return onlinePlayers.value.find(player => player.accountId === targetAccountId)?.username || targetAccountId
}
function cancelOutgoingInvitation() {
  const invitationId = l12State.outgoingFriendInvitation?.invitationId
  if (invitationId) cancelFriendInvitation(invitationId)
}
watch(() => platformState.account?.id, () => void refreshPresence())
onMounted(() => {
  window.addEventListener('l12-friend-room-created', enterFriendRoom)
  void refreshPresence()
  presenceTimer = window.setInterval(() => void refreshPresence(), 15_000)
})
onBeforeUnmount(() => {
  window.removeEventListener('l12-friend-room-created', enterFriendRoom)
  window.clearInterval(presenceTimer)
})
</script>

<template>
  <div class="site-shell">
    <header class="site-mobile-head">
      <router-link class="mobile-brand" to="/" title="返回主页"><img :src="siteBrandIcon" alt="十二军团"/></router-link>
      <button aria-label="打开导航" @click="mobileOpen = !mobileOpen">{{ mobileOpen ? '×' : '☰' }}</button>
    </header>

    <aside class="site-sidebar" :class="{ open: mobileOpen }">
      <router-link class="site-brand" to="/" title="十二军团官方网站">
        <img :src="siteBrandIcon" alt="十二军团"/>
      </router-link>

      <nav class="site-nav" aria-label="主要导航">
        <router-link v-for="item in nav" :key="item.to" :to="item.to" :title="item.label">
          <SiteIcon :name="item.icon"/><span>{{ item.label }}</span>
        </router-link>
      </nav>

      <div class="site-utilities">
        <button title="设置" @click="modal = 'settings'"><SiteIcon name="settings"/><span>设置</span></button>
        <button title="更新日志" @click="modal = 'updates'"><SiteIcon name="updates"/><span>更新日志</span></button>
        <button :class="{ 'has-unread': incomingRequestCount > 0 }" :title="incomingRequestCount ? `在线玩家 · ${incomingRequestCount} 个未读好友申请` : '在线人数'" @click="modal = 'online'"><span class="utility-icon"><SiteIcon name="online"/><i>{{ onlineCount }}</i></span><span>在线人数</span><b v-if="incomingRequestCount" class="utility-unread" :aria-label="`${incomingRequestCount} 个未读好友申请`">{{ incomingRequestCount }}</b></button>
        <button class="connection" :class="l12State.status" :title="connectionLabel"><span class="utility-icon"><SiteIcon name="connection"/><i/></span><span>{{ connectionLabel }}</span></button>
      </div>
    </aside>

    <main class="site-content"><MaintenanceTicker v-if="route.meta.section === 'battle'"/><slot /></main>

    <div v-if="modal" class="site-modal-mask" @click.self="modal = null">
      <L12SettingsModal v-if="modal === 'settings'" @close="modal = null"/>

      <section v-else-if="modal === 'updates'" class="site-modal update-modal">
        <header><div><small>CHANGELOG</small><h2>更新日志</h2></div><button @click="modal = null">×</button></header>
        <article v-for="entry in updateEntries.slice(0, 10)" :key="`${entry.date}-${entry.version}`">
          <time>{{ entry.date }}</time><h3>{{ entry.title }}</h3><code>{{ entry.version }}</code>
          <section v-for="section in entry.sections" :key="section.title" class="update-section">
            <h4>{{ section.title }}</h4>
            <ul><li v-for="item in section.items" :key="item">{{ item }}</li></ul>
          </section>
        </article>
      </section>

      <section v-else class="site-modal online-modal">
        <header><div><small>ONLINE</small><h2>在线玩家</h2></div><button @click="modal = null">×</button></header>
        <p v-if="onlineNotice" class="online-notice">{{ onlineNotice }}</p>
        <div v-for="player in onlinePlayers" :key="player.accountId" class="online-entry">
          <i/><div class="online-identity"><b>{{ player.username }}</b><span>{{ player.accountId === platformState.account?.id ? '在线 · 当前账号' : activityLabel(player) }}</span><em v-if="player.friendStatus === 'pending' && player.friendDirection === 'incoming'" class="online-unread">新好友申请</em></div>
          <div v-if="player.friendStatus !== 'self'" class="online-actions">
            <template v-if="player.friendStatus === 'pending' && player.friendDirection === 'incoming'">
              <button class="quiet" :disabled="onlineActionBusy === player.accountId" @click="resolveOnlineFriend(player, false)">拒绝</button>
              <button :disabled="onlineActionBusy === player.accountId" @click="resolveOnlineFriend(player, true)">{{ onlineActionBusy === player.accountId ? '处理中' : '接受' }}</button>
            </template>
            <template v-else>
              <button v-if="player.friendStatus === 'accepted'" class="quiet" :disabled="!player.canInvite" :title="player.actionReason || '邀请好友直接建立房间'" @click="inviteOnlinePlayer(player)">好友 · 邀战</button>
              <button v-else-if="player.friendStatus === 'pending'" disabled>好友 · 已申请</button>
              <button v-else :disabled="onlineActionBusy === player.accountId" @click="addOnlineFriend(player)">{{ onlineActionBusy === player.accountId ? '发送中' : '添加好友' }}</button>
            </template>
            <button v-if="player.activity === 'playing'" :disabled="!player.canSpectate" :title="player.actionReason || '进入该玩家的对局观战'" @click="watchOnlinePlayer(player)">观战</button>
          </div>
        </div>
        <div v-if="onlinePlayers.length === 0" class="modal-empty">登录并连接服务器后可查看在线玩家。</div>
      </section>
    </div>

    <div v-if="accountGate" class="site-modal-mask account-gate">
      <section class="site-modal auth-modal">
        <header><div><small>BATTLE ACCOUNT</small><h2>登录后进入对战</h2></div><button title="返回主页" @click="router.push('/')">×</button></header>
        <p>对战、赛事、好友、排行榜和个人对局记录使用同一账号身份。</p>
        <div class="auth-tabs"><button :class="{ active: authMode === 'login' }" @click="authMode = 'login'">登录</button><button :class="{ active: authMode === 'register' }" @click="authMode = 'register'">注册</button></div>
        <label>用户名<input v-model="auth.username" autocomplete="username"/></label>
        <small v-if="authMode === 'register'" class="username-rule-hint">2–11 个可见字符，不得包含冒充官方、辱骂、色情、违法交易或广告导流内容。</small>
        <label>密码<input v-model="auth.password" type="password" maxlength="128" :autocomplete="authMode === 'login' ? 'current-password' : 'new-password'" @keyup.enter="submitAuth"/></label>
        <p v-if="authNotice" class="auth-notice">{{ authNotice }}</p>
        <button class="auth-submit" :disabled="authBusy || !auth.username.trim() || !auth.password" @click="submitAuth">{{ authMode === 'login' ? '登录并进入' : '注册并进入' }}</button>
        <button class="auth-home" @click="router.push('/')">返回主页</button>
      </section>
    </div>

    <div v-if="l12State.friendInvitation || l12State.outgoingFriendInvitation" class="invitation-stack">
      <div v-if="l12State.outgoingFriendInvitation" class="outgoing-invitation-gate" :class="{ minimized: outgoingInvitationMinimized }">
        <button v-if="outgoingInvitationMinimized" class="invitation-minimized" @click="outgoingInvitationMinimized = false">已发送对战邀请 · 展开</button>
        <section v-else class="site-modal invitation-modal outgoing-invitation-modal" role="status" aria-label="已发送好友对战邀请">
          <header><div><small>FRIEND BATTLE</small><h2>对战邀请已发送</h2></div><button aria-label="最小化已发送对战邀请" @click="outgoingInvitationMinimized = true">—</button></header>
          <p>正在等待 <b>{{ outgoingInvitationTarget(l12State.outgoingFriendInvitation.targetAccountId) }}</b> 回应。</p>
          <div class="invite-code"><span>预留房间码</span><strong>{{ l12State.outgoingFriendInvitation.roomCode }}</strong></div>
          <div class="invite-actions outgoing-invite-actions"><button class="quiet" @click="cancelOutgoingInvitation">撤回邀请</button></div>
        </section>
      </div>

      <div v-if="l12State.friendInvitation" class="invitation-gate" :class="{ minimized: invitationMinimized }">
        <button v-if="invitationMinimized" class="invitation-minimized" @click="invitationMinimized = false">好友对战邀请 · 展开</button>
        <section v-else class="site-modal invitation-modal" role="dialog" aria-modal="false" aria-label="好友对战邀请">
          <header><div><small>FRIEND BATTLE</small><h2>好友对战邀请</h2></div><button aria-label="最小化好友对战邀请" @click="invitationMinimized = true">—</button></header>
          <p><b>{{ l12State.friendInvitation.fromName }}</b> 邀请你进行友谊战。</p>
          <div class="invite-code"><span>预留房间码</span><strong>{{ l12State.friendInvitation.roomCode }}</strong></div>
          <p class="invite-note">接受后将直接创建房间：发起方成为房主，你将自动进入整备室，无需再输入房间码。</p>
          <div class="invite-actions"><button class="quiet" @click="answerInvitation(false)">拒绝</button><button @click="answerInvitation(true)">接受并进入房间</button></div>
        </section>
      </div>
    </div>
  </div>
</template>

<style scoped>
.site-shell{--nav-w:92px;width:100vw;height:100vh;background:radial-gradient(circle at 80% 10%,rgba(18,101,108,.12),transparent 34%),radial-gradient(circle at 12% 84%,rgba(121,22,32,.13),transparent 35%),#060a0d;color:#f2f0e9;font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.site-sidebar{position:fixed;z-index:40;inset:0 auto 0 0;width:var(--nav-w);display:flex;flex-direction:column;border-right:1px solid rgba(232,227,213,.16);background:#0d1318}.site-brand{display:flex;height:96px;flex-direction:column;align-items:center;justify-content:center;gap:5px;color:#f3eee1;text-decoration:none}.site-brand img{width:44px;height:44px;border:0;border-radius:0;object-fit:contain;filter:brightness(0) invert(1)}.site-nav{display:flex;flex:1;min-height:0;flex-direction:column;overflow-y:auto}.site-nav a,.site-utilities button{position:relative;display:flex;min-height:58px;flex-direction:column;align-items:center;justify-content:center;gap:5px;border:0;background:transparent;color:#7d8991;text-decoration:none}.site-nav a:hover,.site-nav a.router-link-active{background:linear-gradient(90deg,rgba(48,181,190,.2),transparent);color:#f4f1e9}.site-nav a.router-link-active::before{content:'';position:absolute;left:0;top:12px;bottom:12px;width:3px;background:#51c5cc;box-shadow:0 0 12px #51c5cc}.site-nav b,.site-utilities b{font-size:15px}.site-nav span,.site-utilities span{font-size:14px;font-weight:900}.site-utilities{display:flex;flex-direction:column;gap:8px;padding:8px 0;border-top:1px solid rgba(232,227,213,.12)}.site-utilities button{width:100%;min-height:48px}.site-utilities .connection i{width:8px;height:8px;border-radius:50%;background:#6b7272}.site-utilities .connection.online i{background:#55c99a;box-shadow:0 0 8px #55c99a}.site-utilities .connection.connecting i{background:#d7b15f}.site-content{position:absolute;inset:0 0 0 var(--nav-w);overflow:auto}.site-mobile-head{display:none}.site-modal-mask{position:fixed;z-index:100;inset:0;display:grid;place-items:center;padding:20px;background:rgba(1,4,7,.75);backdrop-filter:blur(10px)}.site-modal{width:min(560px,94vw);max-height:min(720px,90vh);overflow:auto;border:1px solid rgba(235,230,216,.28);background:#111923;box-shadow:0 28px 90px #000;padding:24px}.site-modal>header{display:flex;align-items:center;justify-content:space-between;padding-bottom:15px;border-bottom:1px solid rgba(235,230,216,.14)}.site-modal header small{color:#51c5cc;font:900 14px monospace;letter-spacing:.18em}.site-modal h2{margin:4px 0 0;font-size:24px}.site-modal header button{width:34px;height:34px;border:1px solid #48545c;background:#0a1016;color:#fff}.setting-row{display:flex;align-items:center;justify-content:space-between;gap:18px;padding:18px 0;border-bottom:1px solid rgba(235,230,216,.1)}.setting-row b,.setting-row span{display:block}.setting-row span{margin-top:5px;color:#7f8b93;font-size:14px}.setting-row select,.toggle{min-width:118px;padding:10px;border:1px solid #52606a;background:#081018;color:#fff;font-weight:900}.toggle.on{border-color:#54b48f;color:#7ee2b9}.setting-note{color:#7e898f;font-size:14px;line-height:1.7}.update-modal article{padding:18px 0;border-bottom:1px solid rgba(235,230,216,.1)}.update-modal time{color:#d6ad59;font-size:14px;font-weight:900}.update-modal h3{margin:6px 0;font-size:15px}.update-modal code{display:inline-block;padding:3px 6px;border:1px solid #6f602e;color:#e5c866;font-size:14px}.update-modal li{margin:7px 0;color:#a8b0b3;font-size:14px;line-height:1.6}.online-entry{display:flex;align-items:center;gap:12px;margin-top:12px;padding:14px;background:#0a1118}.online-entry>i{flex:0 0 auto;width:9px;height:9px;border-radius:50%;background:#55c99a;box-shadow:0 0 8px #55c99a}.online-identity{min-width:0;flex:1}.online-entry b,.online-entry span{display:block}.online-entry span{margin-top:3px;color:#718088;font-size:14px}.online-actions{display:flex;flex:0 0 auto;gap:7px}.online-actions button{min-width:82px;padding:8px 10px;border:1px solid #d2b35f;background:#29220f;color:#f0d478;font-size:14px;font-weight:900}.online-actions button:disabled{border-color:#3f484e;background:#121920;color:#68747a;cursor:not-allowed}.online-notice{margin:12px 0 0;padding:9px 11px;border-left:3px solid #51c5cc;background:#0a151b;color:#9fd5d8;font-size:14px}.modal-empty{margin-top:18px;padding:38px 20px;border:1px dashed #39444b;color:#738089;text-align:center;font-size:14px;line-height:1.7}
@media(max-width:760px){.site-shell{--nav-w:0px}.site-mobile-head{position:fixed;z-index:60;top:0;left:0;right:0;height:58px;display:flex;align-items:center;justify-content:space-between;padding:0 14px;border-bottom:1px solid rgba(232,227,213,.16);background:#0d1318}.mobile-brand{display:flex;align-items:center;gap:9px;color:#fff;text-decoration:none}.mobile-brand b{display:grid;width:30px;height:30px;place-items:center;border:1px solid #d8b362;font:900 14px Georgia}.mobile-brand span{font-weight:900}.site-mobile-head button{width:38px;height:38px;border:1px solid #46525a;background:#111a22;color:#fff;font-size:20px}.site-sidebar{top:58px;width:min(310px,84vw);transform:translateX(-105%);transition:transform .2s}.site-sidebar.open{transform:none;box-shadow:18px 0 50px #000}.site-brand{display:none}.site-nav a,.site-utilities button{min-height:52px;flex-direction:row;justify-content:flex-start;padding:0 24px;gap:15px}.site-nav span,.site-utilities span{font-size:14px}.site-utilities{display:grid;grid-template-columns:1fr 1fr;column-gap:0;row-gap:8px}.site-content{top:58px}.site-modal{padding:18px}.setting-row{align-items:flex-start;flex-direction:column}.setting-row select,.toggle{width:100%}}
.utility-icon{position:relative;display:grid;place-items:center}.utility-icon>i{position:absolute;top:-7px;right:-9px;display:grid!important;min-width:15px!important;width:auto!important;height:15px!important;place-items:center;padding:0 3px;border-radius:8px!important;background:#71303a;color:#fff;font:900 14px monospace!important;font-style:normal}.site-utilities .connection .utility-icon>i{top:auto;right:-5px;bottom:-3px;width:7px!important;min-width:7px!important;height:7px!important;padding:0;border-radius:50%!important;background:#6b7272}.site-utilities .connection.online .utility-icon>i{background:#55c99a!important;box-shadow:0 0 8px #55c99a}.site-utilities .connection.connecting .utility-icon>i{background:#d7b15f!important}
.site-utilities .utility-unread{position:absolute;right:6px;top:4px;display:grid;min-width:18px;height:18px;place-items:center;padding:0 3px;border-radius:10px;background:#be3340;color:#fff;font:900 12px/1 monospace}.site-utilities button.has-unread{color:#f2d478}.online-unread{display:inline-block!important;margin-top:5px!important;padding:2px 6px;border:1px solid #a93642;background:#2d1117;color:#ef9ca5!important;font-size:12px!important;font-style:normal;font-weight:900}.online-actions{flex-wrap:wrap;justify-content:flex-end}
.audio-setting{display:flex;align-items:center;gap:12px}.audio-setting input{width:150px}
@media(max-width:760px){.mobile-brand img{width:30px;height:30px;border:0;border-radius:0;object-fit:contain;filter:brightness(0) invert(1)}.mobile-brand b{display:none}}
.auth-modal>p{color:#87939a;font-size:14px;line-height:1.7}.auth-tabs{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin:18px 0}.auth-tabs button,.auth-home{padding:11px;border:1px solid #46535b;background:#080e13;color:#9aa3a7;font-weight:900}.auth-tabs button.active{border-color:#e1c16c;background:#2a2414;color:#f2d985}.auth-modal label{display:block;margin:13px 0;color:#abb3b6;font-size:14px;font-weight:900}.auth-modal input{display:block;width:100%;margin-top:7px;padding:12px;border:1px solid #4b5860;background:#080e13;color:#fff}.auth-submit{width:100%;margin-top:16px;padding:12px;border:1px solid #e1c16c;background:#e1c16c;color:#080b0d;font-weight:900}.auth-submit:disabled{opacity:.45}.auth-home{width:100%;margin-top:8px}.auth-notice{padding:9px!important;border-left:3px solid #a72e39;background:#291016;color:#e5aab0!important}.account-gate{z-index:140}
.invitation-stack{position:fixed;z-index:160;right:18px;bottom:18px;display:flex;width:min(430px,calc(100vw - 36px));max-height:calc(100vh - 36px);flex-direction:column;gap:10px;overflow:auto;pointer-events:none}.invitation-gate,.outgoing-invitation-gate{width:100%;flex:0 0 auto;pointer-events:none}.invitation-modal{box-sizing:border-box;width:100%;max-height:min(620px,calc(100vh - 36px));padding:20px;pointer-events:auto}.invitation-modal>p{color:#aeb6ba;line-height:1.7}.invitation-minimized{padding:10px 14px;border:1px solid #e1c16c;background:#231c0d;color:#f0d478;box-shadow:0 12px 34px #000;font-weight:900;pointer-events:auto}.invite-code{display:flex;align-items:center;justify-content:space-between;margin:18px 0;padding:14px;border:1px solid #4e5b63;background:#080e13}.invite-code span{color:#79868d;font-size:14px}.invite-code strong{color:#f0d478;font:900 22px monospace;letter-spacing:.18em}.invite-note{font-size:14px}.invite-actions{display:grid;grid-template-columns:1fr 1.7fr;gap:10px;margin-top:20px}.invite-actions button{padding:12px;border:1px solid #e1c16c;background:#e1c16c;color:#080b0d;font-weight:900}.invite-actions button.quiet{border-color:#4a565e;background:#0a1117;color:#929da2}.outgoing-invitation-modal{border-color:rgba(81,197,204,.42)}.outgoing-invite-actions{grid-template-columns:1fr}
.online-actions button.quiet{border-color:#4b565c;background:#0b1217;color:#9ba5aa}
.update-modal{width:min(680px,94vw)}
.update-modal h3{font-size:17px}
.update-section{margin-top:18px}
.update-section h4{margin:0 0 8px;padding-left:9px;border-left:3px solid #d6ad59;color:#f0ede5;font-size:14px}
.update-modal ul{margin:0;padding-left:20px}
.update-modal li{margin:8px 0;color:#b2b9bc;line-height:1.75}
</style>
