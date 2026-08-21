# WebSocket 协议（P0）

地址：`ws://<host>:8080/ws/`。消息均为 UTF-8 JSON，字段使用 camelCase。

## 客户端消息

```json
{ "type": "deploymentProbe" }
{ "type": "hello", "authToken": "登录令牌" }
{ "type": "createRoom" }
{ "type": "joinRoom", "roomCode": "ABC123" }
{ "type": "ready", "ready": true }
{ "type": "gameAction", "command": { "type": "mulligan", "cardInstanceIds": [] } }
```

`command.type` 支持：

- `resolvePrompt`：`promptId`，单选带 `choice`，多选/排序带 `choices`；用于先后手、天灾禁选与选择、调度后的全部卡效选择
- `mulligan`：`cardInstanceIds`
- `advancePhase`：保留兼容但会拒绝；触发天灾至主要阶段由服务器自动执行并写入事件记录
- `playCard`：`cardInstanceId`；军团与覆盖的反击战术另带 `row`、`slot`
- `attack`：`cardInstanceId`、`target: { type: "master" | "legion", instanceId? }`
- `resolveDefense`：主宰抵挡带 `cardInstanceIds`，军团支援带 `supportInstanceId`
- `move`：`cardInstanceId`、`row`、`slot`
- `activateAbility`：`cardInstanceId`、`ability`，少数直接目标能力另带 `cardInstanceIds`
- `flipHidden`：`cardInstanceId`
- `endTurn`
- `surrender`

## 服务端消息

- `session`：会话编号与昵称。
- `roomState`：房间码、座位、连接/就绪状态。
- `gameState`：面向单个玩家裁剪的权威状态。对手手牌和牌库只发送数量。
- `actionRejected`：操作被规则层拒绝，包含可显示的 `message`。
- `error`：会话或消息格式错误。
- `pong`：心跳响应。
- `deploymentProbe`：无需认证、无状态且不写入运行数据的发布探针；返回服务标识、协议版本与认证方式。仅用于验证部署后的 WebSocket 建连和协议版本，不建立玩家会话。

每次合法操作增加 `revision`。`stateHash` 是服务器完整状态的 SHA-256，用于两端一致性核对和后续回放校验。

`gameState.state.pendingPrompts` 只包含当前查看者有权回答的 Prompt；私有候选不会发送给另一方。`effectStack` 显示公开堆叠，`responseWindow` 表示当前响应权。覆盖的反击战术对所有者显示正面数据，对对手只显示统一卡背占位。
