# 发布状态：唯一实时读数

在仓库根目录执行 `node scripts/release-status.mjs` 查看人可读状态；执行 `node scripts/release-status.mjs --json` 获取同一份状态模型的机读结果。命令只读，不生成发布包、不推送、不部署、不修改维护。网络不可用时可使用 `--offline`，此时远端和服务器状态必须显示“未知”，不能沿用旧回执充数。

状态来源固定且相互独立：

| 维度 | 权威读数 |
| --- | --- |
| 开发候选 | 当前工作树 Git HEAD、分支与 porcelain 脏净状态 |
| GitHub 主线 | 实时 `git ls-remote origin refs/heads/main` |
| 测试服 | 公网 `/testrun/health` 的完整 `serverVersion` 与一致的服务状态 |
| 正式服 | 公网 `/health` 的完整 `serverVersion` 与一致的服务状态 |
| 正式服维护 | 同一次正式服 `/health` 的 `maintenance` 布尔值 |

每次报告必须附本次 `observedAt`；健康接口不可读、服务身份或版本不合法、状态与维护标志矛盾时，该环境标为“未知”。提交、通过 Release、推送 GitHub、测试服部署、正式服部署和维护开关是不同事件，绝不互相推定。历史 `HANDOFF.md` 和任务台账保留过程证据，但其中的“待部署”等文字不是实时状态源；发版前后均以本命令重新读回，部署还须结合部署命令成功回执与适当线上验收。纯推送无需部署权限；任何部署或解除维护仍需要用户当批明确授权。

回归由 `scripts/test-release-status.mjs` 覆盖独立版本、维护、不可读和畸形健康数据；本地 Release 和 GitHub CI 都运行此测试。正式服的预约维护计划不在公开健康接口内，本工具只报告当前是否处于有效维护，不推测未来计划。
