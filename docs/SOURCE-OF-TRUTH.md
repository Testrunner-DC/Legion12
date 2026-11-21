# 单一真相源（2026-11-21）

## 为什么需要这一页

2026-11-21 的一次评估中，分析在 `D:\GPT\Legion12\repo`（main @ `fd0d602e`，2026-09-22）上进行，
而当时的权威树已在其后约两个月。结果是：报告基线错误、一处分项被判「缺失」而实际早已修复、
并在非权威树上做了一次需要回滚的改动。

## 当前的真相

| 项 | 值 |
|---|---|
| 权威源码树 | 由批次指定的 `worktrees/<name>`（2026-11-21 时为 `worktrees\lubu-pingyang-20261001`） |
| 已失效约定 | `D:\GPT\Legion12\app` —— 该路径**不存在**，AGENTS.md 已改写 |
| 决策文档（仓外） | `D:\GPT\Legion12\docs\`：性能诊断、存储与卡顿最终优化方案、评估候选方案等 |
| 版本锚点 | `git tag` 当前为 **0 个**；发布身份以产物 manifest 的 `commit` 字段为准 |

## 一条命令确认

```powershell
git -C <tree> log -1 --format='%h %ad %s' --date=short
git -C <tree> rev-list --left-right --count HEAD...origin/main   # 左领先 / 右落后
git -C <tree> merge-base --is-ancestor <旧树HEAD> HEAD; $LASTEXITCODE   # 0 = 旧树是它的祖先
```

## 决策文档为何没有复制进仓库

仓外那批文档含生产性能读数、部署与存储运维细节；本仓库是公开仓库，整篇复制等于公开内网运维信息。
落地方式改为：**路径与用途登记在本页**，需要归档时由批次单独评估可公开范围。
