const IMAGE_CACHE = 'l12-images-v1'
const MAX_IMAGES = 180

self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()))

async function trim(cache) {
  const keys = await cache.keys()
  if (keys.length <= MAX_IMAGES) return
  await Promise.all(keys.slice(0, keys.length - MAX_IMAGES).map(key => cache.delete(key)))
}

self.addEventListener('fetch', event => {
  const request = event.request
  if (request.method !== 'GET' || request.destination !== 'image') return
  event.respondWith((async () => {
    const cache = await caches.open(IMAGE_CACHE)
    const cached = await cache.match(request)
    const update = fetch(request).then(async response => {
      if (response.ok || response.type === 'opaque') {
        await cache.put(request, response.clone())
        await trim(cache)
      }
      return response
    }).catch(() => cached)
    return cached || update
  })())
})
