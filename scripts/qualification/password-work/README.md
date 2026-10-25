# 密码计算的纯合成容量工具

只编译链接现有 `L12PasswordCredential.cs` 与 `L12PasswordWorkBudget.cs`。不加载平台账号库、不启动 HTTP、不读取密码或凭据文件，不修改活动成本，也不生成发布包。

量测五条计算路径：120k 创建／校验、600k 创建／校验、旧密码校验后再派生600k。串行每条默认8个样本；突发每组默认24个请求，分别限定1／2名计算者，保留原8个排队位及250ms等待。

## 输出口径

- `serial` 为预热后的原始计算分布；最近秩P95在8样本下就是最大值，不足以证明稳定的线上尾延迟。
- `bursts.totalMs` 只统计成功取得计算许可的请求；`busy` 和 `busyResponseMs` 另列，不能把拒绝的请求藏进成功率。
- 250ms仅是排队等待参数，不是整个请求的完成期限；线程调度还会影响实际返回时刻。不通过的配置不能靠加大队列／等待时间掩盖。
- `processCpuMs` 是此独立工具进程的CPU时间。1／2名计算者不等于模拟1／2核主机；`processorCount` 是当前进程可见逻辑处理器数量，`cpuQuotaEmulated` 固定为false。
- 不包含HTTP、平台持久化、失败登录桶、对局或共享服务器负载。本机结果不是正式／测试服容量或30＋10验收，不用于直接开启更高成本。
- 只输出数值与兼容性布尔值，不输出合成密码、盐、哈希或账号信息。

## 在既有D盘工作流中复现

先在权威工作树调用标准存储守卫与构建初始化；任何新输出目录都要在物理路径核验后创建。不要在C盘兼容入口裸跑默认构建。

```powershell
. './ops/windows/L12GeneratedStorage.ps1'
$probeBuild = 'D:/GPT/Legion12/cache/primary/build/g4-password-probe'
$probeEvidence = 'D:/GPT/Legion12/artifacts/stage-goals-20261025/g4-capacity-repeat'
Assert-L12GeneratedPath -Path $probeBuild | Out-Null
Assert-L12GeneratedPath -Path $probeEvidence | Out-Null
& './ops/windows/Initialize-L12BuildEnvironment.ps1'
New-Item -ItemType Directory -Path $probeEvidence -Force | Out-Null
$probeReport = Join-Path $probeEvidence 'local-capacity.json'
if (Test-Path -LiteralPath $probeReport) { throw '保留既有证据；本次请选择另一个已核验的证据目录。' }
dotnet build scripts/qualification/password-work/L12.PasswordWorkProbe.csproj -c Release --artifacts-path $probeBuild -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw '工具编译失败。' }
$probeAssembly = Join-Path $probeBuild 'bin/L12.PasswordWorkProbe/release/L12.PasswordWorkProbe.dll'
$probeJson = & dotnet $probeAssembly --samples 8 --burst 24
if ($LASTEXITCODE -ne 0) { throw '量测失败，不生成通过回执。' }
$probeJson | Set-Content -LiteralPath $probeReport -Encoding utf8
node scripts/test-l12-password-work-probe.mjs (Get-Command dotnet).Source $probeAssembly $probeReport
if ($LASTEXITCODE -ne 0) { throw '参数／输出／反例验收失败。' }
```

## 当前回滚障碍与后续顺序

历史v1量测（c43bfb90）确认：当时的120k读者拒绝600k记录。当前v2读者白名单上限为600k，可以兼容新旧记录，但默认写入仍120k、没有自动升级；`--self-test`只核兼容边界，不冒新的容量量测。校验器保留v1历史口径，并单独核v2，原始v1数值报告不改写。

读取兼容不是高成本启用许可。须先验收兼容读者，使可回滚版本都能读新旧记录；然后在有代表性的服务器上验证CPU、排队、失败认证和共享负载，再限定启用新写入／成功登录渐进升级。升级与登录提交共享完整凭据CAS、原限流、保存回滚和会话边界，不保留旧弱哈希作为第二凭据、不在失败登录时重哈希、不批量改现库。停升级时保兼容读取，不回退到不能读新成本的二进制；关闭新写入不会令已有凭据降级，更不能靠旧数据库回写解决。

[OWASP密码存储参考](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#using-work-factors)要求结合目标服务器实测成本与负载；PBKDF2-HMAC-SHA256的600k是本轮评估目标，不是本工具自授的上线许可。
