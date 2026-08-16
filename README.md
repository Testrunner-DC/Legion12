# 十二军团网页对战版

基于 GrandUMI `develop` 分支架构建立的 Vue 3 + C# WebSocket 对战项目。当前里程碑是可运行、可记录、可复盘的“两套预组卡效版”：两名玩家通过房间码联机，服务端负责全部规则判定，客户端只发送意图并显示服务器状态。

大厅与战场采用 GrandUMI 的页面结构：顶栏/侧栏/主内容/信息栏大厅，以及固定画布等比缩放的左详情栏/中央牌桌/右操作栏战场。组件映射见 `docs/UI-ARCHITECTURE.md`。

## 当前可玩范围

- 游客昵称、六位房间码、两人就绪开局。
- 固定预组：P1 天廷 S1，P2 高天原 S1。
- 掷骰胜者选择先后手、有序天灾禁选/选择、起手 6 张与双方同时调度；之后自动进入回合流程。
- 士气费用、2×3 阵地、部署、位移、近战/远程距离、进攻、主宰防御、同列后排支援、伤害、阵亡、牌库耗尽与主宰生命胜负。
- 卡牌档案：本地权威 S1 133 张 + 查询站 S2 115 张，共 248 张；支持卡池/类型/阵营/费用/天灾等级筛选、排序、卡面/效果详情与预组收录统计。
- Prompt 选择、响应堆叠与覆盖反击战术已接入；天廷、高天原两套 S1 预组及 S01-DS01 至 S01-DS10 天灾效果已进入服务端规则层。
- 每次客户端操作（包括被拒绝操作）均写入 SQLite，保存命令、版本号、完整状态、SHA-256 状态哈希与结果。
- 大厅“对局记录”可读取 SQLite 历史，按操作拖动查看任一步完整快照与截至当时的事件记录。

## 环境

- Node.js 22+
- .NET SDK 10.0.302+（项目通过 `global.json` 锁定 10.0 功能带）
- Chrome / Edge，建议 1366×768 以上

## 启动

终端 1：

```powershell
cd opcgpro-vue
npm ci
npm run dev
```

终端 2：

```powershell
cd 服务端WebSocket
dotnet run
```

浏览器打开 `http://localhost:5173`。同机双开浏览器即可测试；局域网玩家打开主机的 `http://<主机IP>:5173`，前端服务器地址填写 `ws://<主机IP>:8080/ws/`。服务端默认监听 `0.0.0.0:8080`。

SQLite 对局记录生成于服务端输出目录的 `runtime/matches.db`；只读接口为 `/api/matches` 与 `/api/matches/{matchId}`。协议见 `docs/PROTOCOL.md`，FAQ 裁定索引见 `docs/l12/FAQ-RULINGS.md`，测试证据见 `docs/TEST-REPORT.md`。

## 验证

```powershell
cd opcgpro-vue
npm run build

cd ..\TwelveLegions.Tests
dotnet test

cd ..
node scripts/ws-smoke.mjs
```

运行 WebSocket 冒烟测试前需先启动服务端。

## 来源与边界

- 架构模板：[GrandUMI](https://github.com/corazon1999/GrandUMI) `develop`。
- 十二军团 S1 数据与规则结论来自旧项目 `D:\WorkBuddyData\十二军团-web` 的选择性迁移；旧项目未被修改。
- 后续卡效资料入口：[十二军团卡牌查询](https://twelve-legions-card-lookup.pages.dev)。
- GrandUMI 的卡图与历史对局没有迁入；其 C# 卡效组织方式保留在源码中，仅供后续实现参考。

GrandUMI 仓库当前未发现明确 LICENSE 文件，因此此代码库按用户指定仅用于本地项目开发；对外发布前需确认模板授权和卡图/卡牌文字的使用权。
