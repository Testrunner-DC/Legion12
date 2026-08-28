# Legion12 香港测试服快速部署

发布流程采用“开发电脑或 CI 完整验证一次、服务器只校验并切换预构建产物”的模型。服务器不再保存源码、`node_modules` 或执行第二轮完整测试；账号、牌库、Bug、官网内容和对局记录独立保存在共享运行目录。

## 目录模型

```text
/opt/legion12-test -> /opt/legion12-releases/<commit>-<time>  # 稳定入口
/opt/legion12-releases/                                     # 不可变版本
/opt/legion12-runtime/                                      # 持久化运行数据
/opt/legion12-static/cards/<tree-hash>/                      # 内容寻址卡图缓存
```

Nginx 和 systemd 继续访问 `/opt/legion12-test`，版本切换只原子替换该符号链接。首次使用新流程时，旧版 `publish/runtime` 会在服务停止后通过同文件系统移动到共享目录，不复制约 1GB 的数据库和平台数据。

## 公网域名与双域过渡

- 主站为 `https://legion-12.com`，生产 WebSocket 为 `wss://legion-12.com/ws`；`https://www.legion-12.com/*` 以 308 保留路径和查询参数跳转主站。
- `https://legion12.grand-umi.com` 在迁移宽限期继续提供相同静态站、API 和 WebSocket，不做重定向，避免旧页面重连或进行中的对局因域名切换中断。
- 生产前端不显式设置 `VITE_WS_URL`，由浏览器按当前 HTTPS 主机选择同源 `wss://<当前主机>/ws`。发布探针和运维脚本以新主域为权威目标。
- 登录令牌、离线牌库和界面偏好保存在浏览器 `localStorage`，不会跨域复制。用户首次打开新主域需要重新登录；服务端账号、牌库、对局和回放数据仍在共享运行目录，不受域名切换影响。禁止通过 URL、重定向参数或日志传递 bearer token；旧域应保留足够宽限期供用户检查尚未同步的本机数据。

## 首次准备

开发电脑需要 Git、PowerShell、Node.js、npm、tar、仓库 `global.json` 指定的 .NET SDK，以及服务器 SSH 权限。建议把构建缓存放在 D 盘：

```powershell
$env:TEMP = "D:\GPT\Legion12\artifacts\temp"
$env:TMP = $env:TEMP
$env:npm_config_cache = "D:\GPT\Legion12\artifacts\temp\npm-cache"
$env:L12_DEPLOY_CACHE = "D:\GPT\Legion12\artifacts\deploy"
```

SSH 公钥加入服务器后验证：

```powershell
ssh root@legion-12.com "echo SSH连接成功"
```

不要复制其他电脑的 SSH 私钥。迁移宽限期内若新域 SSH 名称尚未写入受信主机记录，可显式传入部署脚本参数 `-Server root@legion12.grand-umi.com` 使用旧域恢复入口；不得关闭主机密钥校验。

## 完整验证与构建

单独执行完整验证：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\verify-l12.ps1
```

它会执行 L12 规则测试、平台持久化测试、前端 UI 契约与生产构建，并生成 Linux 后端运行包。产物默认保存在 `D:\GPT\Legion12\artifacts\deploy\<commit>`；同一提交重试会校验并复用该产物。需要强制重测时使用 `-Force`。

卡图独立按 Git tree hash 打包。服务器已经存在相同卡图版本时，不会再次上传约 388MB 的卡图。

## 正式部署

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1
```

部署脚本会：

1. 最多重试三次 GitHub 连接，并要求工作区干净、`HEAD` 与 `origin/main` 完全一致；
2. 复用当前提交已验证的发布包，不存在时自动调用完整验证脚本；
3. 只上传预构建运行包；卡图版本变化时才上传卡图包；
4. 服务器验证 SHA256、归档路径、提交标记、目录结构和真实运行账号权限；
5. 首次迁移共享运行数据；后续每次切换前为共享运行目录生成与待回滚程序配对的数据快照；
6. 将版本放入不可变 release 目录并原子切换稳定入口；
7. 检查主页、卡牌页、健康接口和公网 WebSocket；
8. 任一切换后检查失败时，同时原子恢复上一版本程序和切换前运行数据快照，避免旧二进制读取新 schema；
9. 仅保留最近 5 份运行数据快照，避免备份无界增长。

隔离工作区可以使用：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1 -AllowVerifiedWorktree
```

该开关只放宽分支名称，不能绕过干净状态和远端提交一致性。

## 快速干运行

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1 -DryRun -AllowVerifiedWorktree
```

干运行上传并验证预构建产物和权限，但不停止服务、不迁移数据、不切换版本。因为完整测试只在产物生成时执行，重复干运行通常只需要上传小型运行包并完成服务器校验。

## GitHub Actions

`.github/workflows/verify-release.yml` 会在 `main` 推送、Pull Request 和手动触发时执行同等测试，并生成 Linux 运行包 Artifact。下载后的 JSON 清单可传给部署脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1 -ArtifactManifest D:\path\l12-release-<commit>.json
```

CI Artifact 不重复包含卡图；若服务器没有对应缓存，部署电脑会从当前同提交仓库生成一次卡图包。

## 回滚与排查

部署元数据：

```text
/opt/legion12-deployment/deployment-info.txt
```

切换前运行数据快照保存在 `/opt/legion12-deployment/runtime-backups`。自动回滚必须把上一版本程序与同次部署前的数据快照作为一组恢复；禁止只切换旧 release 而继续使用升级后的 `platform.db`。快照归档会先校验，再解压到临时目录并通过同文件系统重命名替换，失败时服务保持停止并保留现场供人工排查。

查看状态：

```powershell
ssh root@legion-12.com "systemctl status legion12-test.service --no-pager"
ssh root@legion-12.com "journalctl -u legion12-test.service -n 200 --no-pager"
```

禁止直接覆盖 `/opt/legion12-test`、删除 `/opt/legion12-runtime`，或在服务器源码目录执行 `git pull`。旧 release 暂不自动删除，以便人工审计和回滚。

域名迁移不切换应用 release，也不移动运行数据。2026-08-28 的 Nginx 切换前备份位于 `/root/legion12-nginx-backups/20260828T113923Z`；新主域与 `www` 分别由 `/etc/nginx/sites-enabled/legion12-new-domain`、`legion12-www-domain` 启用，旧域虚拟主机保持独立。若新域验证失败，应只禁用这两个新链接并在迁移窗口内从备份恢复 `/etc/nginx/sites-available/legion12`，随后先执行 `nginx -t`，成功后才 `systemctl reload nginx`。继续以旧域验证主页、健康接口和 WebSocket；不要停止后端、删除新证书或修改 `/opt/legion12-runtime`。DNS 回滚由 Cloudflare 控制台单独执行，仓库脚本不持有其凭据。

## Windows 构建缓存位置

Windows 完整验证和部署入口会自动初始化本次进程使用的构建缓存。开发电脑存在统一项目目录时，默认使用：

~~~text
D:\GPT\Legion12\cache\primary\temp
D:\GPT\Legion12\cache\primary\dotnet-home
D:\GPT\Legion12\cache\primary\nuget
D:\GPT\Legion12\cache\primary\npm
D:\GPT\Legion12\cache\primary\corepack
D:\GPT\Legion12\artifacts\deploy
~~~

这只影响当前验证/部署进程及其子进程，不会修改 Windows 全局 TEMP，也不会删除 C 盘旧缓存。需要临时改用其他磁盘时可传入：

~~~powershell
powershell -ExecutionPolicy Bypass -File .\ops\windows\verify-l12.ps1 -CacheRoot E:\L12-cache
powershell -ExecutionPolicy Bypass -File .\ops\windows\deploy-l12.ps1 -CacheRoot E:\L12-cache
~~~

也可以预先设置进程环境变量 L12_WORK_CACHE；显式 -CacheRoot 的优先级最高。发布产物目录仍由 L12_DEPLOY_CACHE 或 -OutputDirectory 独立控制。
