function normalizeBase(value: string) {
  const trimmed = value.trim()
  if (!trimmed || trimmed === './' || trimmed === '/') return '/'
  return `/${trimmed.replace(/^\/+|\/+$/g, '')}/`
}

export const deploymentBasePath = normalizeBase(import.meta.env.BASE_URL || '/')

// Root deployment keeps its existing login; mounted environments never inherit
// or delete those ambiguous legacy keys. The build base, not the current route
// or an editable endpoint, determines ownership for the page's entire lifetime.
export function deploymentStorageKey(legacyKey: 'l12-auth-token' | 'l12-account' | 'l12-nickname' | 'l12-endpoint') {
  return deploymentBasePath === '/' ? legacyKey : `${legacyKey}@${deploymentBasePath}`
}

export function deploymentPath(path: string) {
  if (!path || /^(?:[a-z]+:)?\/\//i.test(path) || path.startsWith('data:') || path.startsWith('blob:')) return path
  const suffix = path.replace(/^\/+/, '')
  return deploymentBasePath === '/' ? `/${suffix}` : `${deploymentBasePath}${suffix}`
}

export function deploymentWebSocketPath() {
  return deploymentPath('/ws')
}

export function endpointHttpBase(endpoint: string) {
  const url = new URL(endpoint)
  url.protocol = url.protocol === 'wss:' ? 'https:' : 'http:'
  url.pathname = url.pathname.replace(/\/ws\/?$/, '').replace(/\/$/, '')
  url.search = ''
  url.hash = ''
  return url.toString().replace(/\/$/, '')
}
