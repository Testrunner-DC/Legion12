import { createApp } from 'vue'
import './style.css'
import App from './App.vue'
import { router } from './router'
import { primeCardAssetManifest } from './l12/cardAssets'

primeCardAssetManifest()
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
