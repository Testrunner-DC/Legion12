# 十二军团卡图对象存储与 CDN 方案

更新时间：2026-08-23

## 结论

第一阶段采用 **Cloudflare R2 Standard + `cards.legion12.grand-umi.com` 自定义域名 + Cloudflare CDN**。现有 Steam 图床只作为迁移来源，不再作为客户端长期直连源。

选择理由：

- R2 Standard 适合高频网站图片，没有取回费和最低保存期；公网出站流量免费。
- 每月包含 10 GB 存储、100 万次 Class A 写操作、1000 万次 Class B 读操作；当前约 248 张卡及多尺寸衍生图在内测期预计处于免费额度内。
- 项目域名已经由 Cloudflare 提供 HTTPS/WebSocket，增加 `cards` 子域名不需要再引入一套证书和 DNS 运维。
- R2 兼容 S3 API，后续可无损迁移至 Bunny Storage、AWS S3 或其他兼容对象存储。

官方价格基线（执行采购前仍应重新核对）：

- Cloudflare R2 Standard：`$0.015/GB/月`；Class A `$4.50/百万次`；Class B `$0.36/百万次`；公网出站免费。免费额度为 10 GB、100 万次 Class A、1000 万次 Class B。
- Bunny Storage Standard 单区域：`$0.01/GB/月`，但有 `$1/月`最低消费；通过 Bunny CDN 向亚洲和大洋洲分发为 `$0.03/GB`。

资料：

- https://developers.cloudflare.com/r2/pricing/
- https://developers.cloudflare.com/r2/buckets/storage-classes/
- https://docs.bunny.net/storage/pricing
- https://docs.bunny.net/cdn/pricing

## 图片规范

每张卡使用不可变内容地址，禁止覆盖同一个 URL：

```text
cards/{catalogVersion}/{cardId}/{sha256}/original.webp
cards/{catalogVersion}/{cardId}/{sha256}/thumb-240.webp
cards/{catalogVersion}/{cardId}/{sha256}/board-480.webp
cards/{catalogVersion}/{cardId}/{sha256}/detail-960.webp
cards/{catalogVersion}/{cardId}/{sha256}/detail-960.avif
```

- 原始图片保留一份；浏览器默认使用 240/480/960 三档 WebP，通过 `srcset` 选择。
- 支持 AVIF 的浏览器优先 AVIF，WebP 作为兼容回退。
- 横版天灾、试炼等保留原始方向；档案/构筑中的顺时针 90° 仅由 UI 展示规则处理，不另存旋转副本。
- 卡图清单记录 `cardId`、内容哈希、宽高、方向、各尺寸 URL 与文件字节数。

## 缓存与加载

- 哈希路径返回 `Cache-Control: public, max-age=31536000, immutable`。
- 当前卡图清单使用短缓存（建议 5 分钟）和 ETag；更新卡图时只发布新哈希并切换清单。
- 对战首屏只预加载双方主宰、手牌小图、本局天灾和当前战场卡；其余卡图使用懒加载。
- 卡牌详情先显示 240/480 版本，再无闪烁替换为 960 版本。
- Service Worker 按 `catalogVersion` 缓存缩略图，保留当前和上一版本，避免每次部署重新下载全部图片。
- 牌库图生成优先读取同源 CDN 缓存，失败时使用本地缓存图，不直接回源第三方图床。

## 迁移步骤

1. 统计所有卡图 URL、尺寸、哈希、缺图和重复图；生成只读迁移报告。
2. 下载至 D 盘卡图工作区，校验可解码与卡号映射；失败条目不覆盖现有数据。
3. 生成三档 WebP 与一档 AVIF，输出内容寻址清单。
4. 创建 R2 Standard bucket，绑定 `cards.legion12.grand-umi.com`，配置只读公开访问与 CORS。
5. 上传对象并抽样验证 HTTP 状态、Content-Type、ETag、缓存头、方向与清晰度。
6. 将卡牌数据的 `imageUrl` 切换为清单地址；保留旧 URL 作为仅服务端可见的 `sourceUrl`。
7. 先在沙盒与卡牌档案灰度验证，再切换对战和牌库图生成。
8. 连续一周记录首屏时间、缓存命中率、404、对象读取次数与流量；确认稳定后停止客户端直连 Steam 图床。

## 预算档位

| 阶段 | 假设 | R2 月成本预估 | 说明 |
|---|---:|---:|---|
| 当前内测 | 10 GB 内、每月读取不超过 1000 万次 | `$0` | 预计落在 R2 Standard 免费额度内 |
| 小规模公开测试 | 20 GB、每月 3000 万次读取 | 约 `$7.35` | 存储约 `$0.15`，2000 万次计费读取约 `$7.20` |
| 较高读取量 | 50 GB、每月 1 亿次读取 | 约 `$33` | 主要成本来自读取请求而非带宽 |

以上只估算 R2，不含域名续费和可选图片变换服务。第一阶段不购买动态图片变换，所有尺寸在构建流水线预生成。

## 备选与切换条件

若中国大陆/东亚实测的 R2 延迟或可达性不足，将 **Bunny Storage + Bunny CDN** 作为第二方案；先用相同 240/480/960 图片集做 7 天 A/B 测试，再根据 P75 首图时间、失败率和每 GB 实际成本决定。对象键和清单保持 S3 风格，切换只需更改清单域名，不改卡效、牌库或回放数据。

