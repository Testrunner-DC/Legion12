import { createApp } from 'vue'
import './style.css'
import App from './App.vue'
import { router } from './router'

createApp(App).use(router).mount('#app')

if ('serviceWorker' in navigator && import.meta.env.PROD) {
  window.addEventListener('load', () => navigator.serviceWorker.register('/sw.js').catch(() => undefined))
}
