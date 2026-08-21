# Legion12 香港测试服部署

香港测试服通过开发电脑生成 Git 归档并经 SSH 上传，服务器端完成构建、数据迁移、版本切换、外网检查和失败回滚。服务器上的源目录不是 Git 工作区，不应直接执行 `git pull`。

## 首次准备

开发电脑需要安装 Git、PowerShell、Node.js、npm 和仓库 `global.json` 指定的 .NET SDK，并拥有 GitHub 仓库权限。

为开发电脑生成独立 SSH 密钥：

```powershell
ssh-keygen -t ed25519 -C "L12开发部署电脑"
Get-Content "$env:USERPROFILE\.ssh\id_ed25519.pub"
```

将公钥加入服务器 `/root/.ssh/authorized_keys` 后验证：

```powershell
ssh root@legion12.grand-umi.com
```

不要在电脑之间复制 SSH 私钥。

## 正式部署

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1
```

脚本只允许从干净且与 `origin/main` 完全一致的 `main` 分支部署，并依次执行：

1. 拉取 GitHub 远端状态并核对提交；
2. 运行 L12 规则回归和平台数据持久化测试；
3. 安装锁定版本的前端依赖并完成生产构建；
4. 使用 `git archive` 生成完整且可追溯的源代码包；
5. 上传并安装仓库内的服务器端发布工具；
6. 在服务器应用香港环境补丁，再次构建和测试；
7. 停止后端后复制 `publish/runtime`，保留账号、Bug、官网内容和对局记录；
8. 切换目录、启动服务并检查主页、卡牌页、健康接口和公网 WebSocket；
9. 任一切换后检查失败时，自动恢复上一版本。

部署不会替换 Nginx 配置、TLS 证书、systemd 服务文件或 `/etc/legion12-test.env`。

## 干运行

首次使用或修改部署工具后，应先执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1 -DryRun
```

干运行会完成上传、香港补丁、服务器构建和测试，但不会停止服务、复制运行数据或切换线上版本。

## 回滚和排查

每次成功切换前，旧版本会保存在：

```text
/opt/legion12-rollback-<新提交>-<UTC时间>
```

部署元数据记录在：

```text
/opt/legion12-deployment/deployment-info.txt
```

查看服务状态和日志：

```powershell
ssh root@legion12.grand-umi.com "systemctl status legion12-test.service --no-pager"
ssh root@legion12.grand-umi.com "journalctl -u legion12-test.service -n 200 --no-pager"
```

不要直接覆盖 `/opt/legion12-test`，也不要删除 `publish/runtime`。
