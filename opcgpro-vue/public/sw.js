// 一次性退役脚本：只删除本项目旧图片缓存，不接管任何网络请求。
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', event => {
  event.waitUntil((async () => {
    await caches.delete('l12-images-v1')
    await self.registration.unregister()
  })())
})
