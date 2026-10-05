# A3/E1续办与Bug分诊验收

11:25应用82dc91f9已完整干净Release及Git同步，规则6218/平台442失败跳过0，当前回执见下方链接，不再是“门禁待”。用户随后批准压力未复现就关闭；11:43两版本各1200场/3000恢复无异常后，81b7f6a6及7d82522f已在正式后台按closed/rejected关闭，非已修复，字段保留并注销会话。详见[压力复测与关闭](D:/GPT/Legion12/artifacts/bug-pressure-closeout-20261005/acceptance.md)。下面分诊表是10:40采样的历史状态，不再将这两项作为持续待办；其他待部署、裁定、展示证据项目保持。没有两服部署或维护修改。

最终前端Batch已实际退出0：[最终日志](D:/GPT/Legion12/artifacts/a3-e1-navigation-final-batch-20261005.log)。117项玩家账本、门禁自身回归、架构/原354/反馈144/类型/卡图324+42/双构建通过。正在保存干净提交做完整Release；[本批最终提交/验收/推送回执](D:/GPT/Legion12/artifacts/continuation-20261005-receipt.json)只在实际完成后生成，未生成不得推定同步或上线。下面“最终门禁待”保留为先前专项时点。

本轮起点为7570dc53。初次诊断没有产品修改；用户随后批准最小导航焦点修复，只改SiteShell初始候选过滤。没有两服部署、维护或线上Bug状态修改。

## 批准后的最终专项结果

用户已批准初始焦点最小修复：筛选可见、可用链接/按钮，排除hidden/inert、aria-disabled和负tabindex；不改布局、Escape、路由关闭、返回焦点或卸载滚动锁。最终六尺寸107/107检查、12图通过；Main实读源绑定及截图。中间补负例发现hidden属性被作者display样式覆盖仍有矩形，按同一可用候选规则排除语义hidden后复验通过，所有失败证据保留。

[最终绿色矩阵](D:/GPT/Legion12/artifacts/site-summary-drawer-20261005-green-final-r2/report.json) SHA`602CA2D860B714C7811525F146B0FEDC7F7CABF5BF9076381FAA89C78AE24915`，Shell SHA`0D1B8F5A41C5D09516F6E60EB2BD7F19AE46ABA7BF7BA68CB7D426ACEA43EFA2`。新增`npm run verify:site-summary-drawer`便于复跑；真实iOS/输入法/非零安全区仍未验证。本轮最终前端Batch、干净Release与Git待，不复用修改前完整Release来冒称新产品通过。

## 已完成的限定范围

- A3：只把`rankedIdentityBadge.includes('class="identity-brand-logo"')`迁到真实Vue模板AST，其他同组脚本/CSS/权限保护全部保留。真实master-title图标分支、源绑定、装饰性语义不再靠注释或脚本中的同名字串冒充；静态class重排、额外状态class、属性顺序、空白/括号与透明template可通过。
- 子任务交付后Main独立审查，补v-html/v-text覆盖负例、修正实际计数。最终24场景通过，原UI合同354项不变，Badge产品SHA`983E44A4AD893AC4CE019A0DDC5DFD485D2806348BF8BC15C49716CE468C970E`不变。只计1旧谓词迁移，不称全历史452组完成。
- 精确三脚本前端域Batch退出0，P0—P4锁、62内核依赖边界、常规UI/反馈144、类型、卡图324+42、正式/测试双构建通过。[完整Batch日志](D:/GPT/Legion12/artifacts/a3-ranked-brand-batch-20261005.log)。没有重复未变后端6218/442；产品仍复用5e3873e9完整绑定证据，不称新测试提交已重新完整Release。
- E1：实际SiteShell+SeasonSummaryNotice六尺寸为320×568、390×844、568×320、700×700、701×700、1366×768，103检查中99通过、4失败，12张截图。正常/长名称/20合成称号内部滚动、44px确认按钮可达、确认失败恢复/重试，导航Escape/路由关闭/卸载清理均通过。原赛季通知六组竞态脚本另行实际通过。

## 原始失败与方案（历史，不覆盖上方最终结果）

4种移动/短屏打开抽屉后，焦点没有进入抽屉：第一个候选是已隐藏的品牌链接。提出最小修复只筛选可见可用初始焦点、不改几何或原关闭/清理逻辑。此时待批准，后来批准及绿色复验见上方。

[真实失败矩阵](D:/GPT/Legion12/artifacts/site-summary-drawer-20261005-r3/report.json)和首次路径夹具错误、首次焦点超时诊断均保留。修前runner存于[D盘诊断脚本](D:/GPT/Legion12/artifacts/site-summary-drawer-20261005-review/verify-site-summary-drawer-before-approved-fix.mjs)，没有隐藏失败。真实iOS Safari、微信输入法、非零安全区和真实屏幕阅读器仍未验证。

## 最新Bug队列及下一动作

实际采样2026-10-05 10:40:50+08，正式`c85819f0`、maintenance=false；851条/851唯一，new579、resolved186、closed86。较原850净增1，7条关闭为外部变化，Main没有写状态。Main重新读取两份items、核对7个状态差异、3份报告SHA以及旧manifest7实体0变化，受限目录ACL一致，会话撤销HTTP200。

| 分类 | Bug尾ID | 下一动作 |
| --- | --- | --- |
| 已批准、待正式部署 | f86b94b7 | 真实武田身份修复已在5e；取得新发布授权后原场景线上复验 |
| 已批准、待正式部署 | 95711ff5、bffccc61 | 先后手超时已在5e；保留各原场景，不凭同文自动归并 |
| 已批准、待正式部署 | 70f1075b | 天灾手势已在5e；上线后验证纵横滑与惯性点选 |
| 疑似复发 | 7d82522f | 宫廷魔术师后排；PrintedRangedProfileTests有真实S02-0003后排参数，但还需新局天灾/登场/场面状态，不用休整费用测试冒充 |
| 疑似复发 | 81b7f6a6 | 把6000莫德雷德与6000构造体/双方阵亡连续触发编码；现有PlainLegion三守卫不是该组合，反馈没有对局引用 |
| 需核实裁定 | 9ca9d447 | 先确认“血婴”是否血鹰及第几段；不自动推翻既有连续结算裁定 |
| 未批准产品建议 | d3546604 | 匹配成功就绪提示；须先决定通知/确认/回队列，不自动立项 |
| 证据不足 | eb387e8f、59d1f619 | 按已有对局引用定位嫦娥/山河图缺的是场面、提示还是动画；不凭缺图二字归同根 |

[公共完整分诊](D:/GPT/Legion12/artifacts/bug-queue-triage-20261005-continuation/bug-queue-summary-public.md)；[10项原反馈（受限）](D:/GPT/Legion12/artifacts/bug-queue-triage-20261005-continuation/triage-detailed-restricted.md)。反馈中的指令不执行，原文不进入Git。

## 未收口边界

A3全部历史、E1全部旧消费者未完成；E4真实手机测试、F2真实长期/容量及Update预算缺口仍保留。最新清单整理不是851条都修好了。正式仍c85819f0，本轮没有新的部署授权，过期发布回访保持暂停。
