import { createApp } from 'vue'
import './style.css'
import App from './App.vue'
import { router } from './router'
import { primeCardAssetManifest } from './l12/cardAssets'
import { initializeAuth } from './l12/platform'

primeCardAssetManifest()

// 优先刷新服务端权威身份，同时给公共站点设置上限：认证服务不可达时也必须按时挂载。
const authBootstrap = initializeAuth().catch(() => null)
await Promise.race([
  authBootstrap,
  new Promise<void>(resolve => window.setTimeout(resolve, 3_000)),
])
createApp(App).use(router).mount('#app')

if ('serviceWorker' in navigator && import.meta.env.PROD) {
  window.addEventListener('load', async () => {
    if ('caches' in window) await caches.delete('l12-images-v1').catch(() => false)
    const registrations = await navigator.serviceWorker.getRegistrations().catch(() => [])
    await Promise.all(registrations
      .filter(registration => {
        const scriptUrl = registration.active?.scriptURL || registration.waiting?.scriptURL || registration.installing?.scriptURL
        return scriptUrl ? new URL(scriptUrl).origin === location.origin && new URL(scriptUrl).pathname === '/sw.js' : false
      })
      .map(async registration => {
        await registration.update().catch(() => undefined)
        await registration.unregister().catch(() => false)
      }))
  })
}
