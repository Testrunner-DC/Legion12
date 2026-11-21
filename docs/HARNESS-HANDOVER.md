# Harness 交接说明（2026-11-21）

## 现状

项目原先由 Codex 主导执行，路由规则与子 agent 配置落在 `.codex/`。自 2026-11-21 起，
主执行 harness 为 **DSH**。两者的规则消费方式不同：

| 机制 | Codex | DSH |
|---|---|---|
| 项目规则 | `AGENTS.md` | 直接读取并注入 `AGENTS.md`（同一份文件，无需额外配置） |
| 子 agent 路由 | `.codex/agents/*.toml`（模型 + 推理强度） | 运行期授权列表（`list_subagent_models` / `subagent` 的 provider/model） |
| 会话配置 | `.codex/config.toml` | 无对应物 |

结论：**`AGENTS.md` 是两种 harness 共同的契约**，`.codex/*.toml` 仍然为 Codex 协作者保留。

## `verify-l12-codex-routing.ps1` 的校验分级

| 断言 | 是否依赖 Codex CLI | 何时执行 |
|---|---|---|
| `.codex/config.toml` 的 `max_concurrent_threads_per_session = 2` | 否 | 只要触发即执行 |
| 四个 agent TOML 的 model/effort 组合 | 否 | 只要触发即执行 |
| `AGENTS.md` 的 11 条路由与测试隔离 marker | 否 | 只要触发即执行 |
| TOML 语法解析（有 python 时） | 否 | 只要触发即执行 |
| `codex --strict-config --help` 退出码 0 | **是** | 仅当 `.codex/` 变更，或设置 `L12_REQUIRE_CODEX_CLI=1` |

CLI 缺失时脚本打印警告并以 0 退出，**输出明确说明"跳过不等于通过"**；
一旦 `.codex/` 本身被改动，缺失 CLI 仍然是硬失败。

## 给 Codex 协作者

在装有 Codex CLI 的机器上不需要任何额外动作；若 CLI 不在 PATH，设置：

```powershell
$env:CODEX_CLI_PATH = "C:\path\to\codex.exe"
```

要强制在任何机器上执行严格配置校验：

```powershell
$env:L12_REQUIRE_CODEX_CLI = "1"
pwsh -File .\scripts\verify-l12-change.ps1 -Level Batch
```

## 给 DSH 主执行

`AGENTS.md` 与本文档共同构成当前执行契约；子 agent 路由由 DSH 的授权路由决定，
不得因为 `.codex/*.toml` 里写着某个模型就假定 DSH 也用它。参见 `docs/SOURCE-OF-TRUTH.md`。
