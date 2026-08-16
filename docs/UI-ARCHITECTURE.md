# 页面结构对照 GrandUMI

十二军团保留自身色彩、规则术语、卡牌数据与网络协议，页面布局和组件职责以 GrandUMI `develop` 的 Vue 客户端为基线。

## 大厅

对应 GrandUMI `MainPanel`：

- `grand-topbar`：全局标题、模式说明、玩家与连接状态。
- `grand-sidebar`：主页、房间、卡牌、记录、设置入口。
- `grand-main`：当前功能主视图。
- `grand-info-rail`：服务器、预组与开发状态。
- `friendly-room`：加入房间后切换到双玩家整备面板，对应 `FriendlyRoomPanel`。

## 对战

`GamePage.vue` 只承载战场和全局浮层，对应 GrandUMI 的薄路由页。`game/GameBoard.vue` 使用 1600×760 固定设计画布并按视口等比缩放，对应 GrandUMI `GameBoard`。

三栏结构：

1. 左栏：选中卡牌详情、对局记录。
2. 中栏：对手隐藏手牌、对手半场、阶段接缝、我方半场、我方手牌。
3. 右栏：玩家信息、操作日志、阶段与行动按钮。

组件映射：

| 十二军团 | GrandUMI 职责 |
|---|---|
| `l12/game/GameBoard.vue` | `components/game/GameBoard.vue` |
| `l12/game/PlayerMat.vue` | `components/game/PlayerMat.vue` |
| `l12/game/HandArea.vue` | `components/game/HandArea.vue` |
| `l12/game/PhaseTrack.vue` | `components/game/PhaseTrack.vue` |
| `l12/game/GameActions.vue` | `components/game/GameActions.vue` |
| `l12/CardTile.vue` | `components/ui/CardItem.vue` |

十二军团拥有 2×3 阵地，因此 `PlayerMat` 中央区域由 GrandUMI 的单排角色区调整为前后两排三列；其余层级和信息分区保持同类关系。
