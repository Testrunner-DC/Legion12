# 公开牌库核心浏览闭环

## 范围

- 公开牌库列表支持名称/作者搜索，并可组合主宰、阵营、本赛季合法性、包含卡牌、更新时间和排序条件。
- 当前页签和全部筛选写入 URL；刷新、分享链接和浏览器前进后退均可恢复同一结果。
- 公开牌库详情使用独立地址 `/decks/:deckId`，可直接打开；从详情返回时恢复原筛选地址与列表滚动位置。
- 详情展示主牌、士气、试炼/额外区、自动额外区、费用曲线与基础热度，并保留点赞、复制牌库码、生成牌库图、复制到我的牌库及作者管理操作。
- 卡牌详情统一复用 `CardDetailContent`，构筑场景不显示“收录产品”和刊物记录。窄屏点击卡牌直接打开安全区内详情，不在每张卡下增加额外按钮。

## 数据合同

- 列表继续使用 `GET /api/public-decks`。
- 独立详情使用 `GET /api/public-decks/{id}`，服务端按当前查看者返回点赞状态，并重新核验当前赛季合法性。
- 官方预组继续由既有权威清单提供，使用稳定的 `official-{index}` 地址，不写入社区公开牌库。

## URL 合同

列表查询参数为 `tab`、`q`、`master`、`faction`、`legal`、`card`、`updated`、`sort`。默认值省略，避免生成无意义长链接。

## 排除项

本批不新增 Guide、Matchups、Matches、Versions、Hands，不重做牌库编辑器，也不改动对战桌或回放布局。

## 验证与回滚

- `node opcgpro-vue/scripts/check-public-deck-browser.mjs`
- `TwelveLegions.Platform.Tests/PublicDeckAndLeaderboardVisibilityTests`
- Vue 类型检查、UI 契约和正式/测试路径构建。
- 回滚时移除详情路由与详情 GET 端点，并把列表入口恢复为原列表内详情；不会改变已经保存或公开的牌库数据。
