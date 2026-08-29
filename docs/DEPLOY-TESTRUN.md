# Legion12 隔离验收站

`testrun.legion-12.com` 是与正式站完全隔离的验收环境：

- 后端端口：`127.0.0.1:8084`
- 服务：`legion12-testrun.service`
- 当前版本入口：`/opt/legion12-testrun`
- 版本目录：`/opt/legion12-testrun-releases`
- 持久化数据：`/opt/legion12-testrun-runtime`
- 环境文件：`/etc/legion12-testrun.env`

测试站可以只读复用 `/opt/legion12-static` 中的内容寻址卡图，但禁止复用
`/opt/legion12-runtime`、8083 端口或 `legion12-test.service`。因此测试发布和
测试数据都不会切换正式站。

首次启用前必须在 Cloudflare 为 `testrun.legion-12.com` 建立指向源站的 DNS
记录。先启用 `ops/server/legion12-testrun-http.nginx` 并签发 ACME 证书，随后
改用 `ops/server/legion12-testrun.nginx`。任何一步验证失败都只停止测试服务，
不得修改 `/opt/legion12-test` 或重启正式服务。

首次部署由 `ops/server/bootstrap-l12-testrun.sh` 完成。脚本只接受固定的验收站
发布包路径，验证包哈希、成员路径、systemd 与 Nginx 配置后才会切换
`/opt/legion12-testrun`。DNS 和证书就绪前仅启用 HTTP 引导配置；正式域名不会
因此切换到验收版本。
