type StoragePort = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

export class BrowserLoginStorageError extends Error {
  readonly operation: 'save' | 'clear'
  constructor(operation: 'save' | 'clear') {
    super(operation === 'save' ? '当前浏览器无法保存登录状态，请允许网站存储后重试。'
      : '当前页面已退出，但无法清除浏览器登录记录；重新打开可能恢复登录，请清除本站数据。')
    this.name = 'BrowserLoginStorageError'
    this.operation = operation
  }
}

/** Only disposable preferences/cache hints may use this port, never a durable save. */
export function createOptionalBrowserStorage(resolve: () => StoragePort): StoragePort {
  return {
    getItem(key) { try { return resolve().getItem(key) } catch { return null } },
    setItem(key, value) { try { resolve().setItem(key, value) } catch { /* Live state remains usable. */ } },
    removeItem(key) { try { resolve().removeItem(key) } catch { /* Retire live identity independently. */ } },
  }
}

// Durable consumers retain throwing reads/writes, resolving the getter inside
// their existing error boundary (e.g. readDeckCache), not at the call argument.
export const persistentLocalStorage: StoragePort = {
  getItem(key) { return globalThis.localStorage.getItem(key) },
  setItem(key, value) { globalThis.localStorage.setItem(key, value) },
  removeItem(key) { globalThis.localStorage.removeItem(key) },
}
// Resolve lazily: even evaluating window.localStorage/sessionStorage can throw.
export const optionalLocalStorage = createOptionalBrowserStorage(() => persistentLocalStorage)
export const optionalSessionStorage = createOptionalBrowserStorage(() => globalThis.sessionStorage)

/** Do not publish a new verified identity until all of its persisted fields succeed. */
export function persistLoginStorage(entries: ReadonlyArray<readonly [string, string]>,
  resolve: () => StoragePort = () => globalThis.localStorage) {
  let storage: StoragePort | undefined
  let previous: Array<readonly [string, string | null]> = []
  try {
    storage = resolve()
    previous = entries.map(([key]) => [key, storage!.getItem(key)] as const)
    for (const [key, value] of entries) storage.setItem(key, value)
  } catch {
    // Best effort only: an origin whose storage policy changed can also reject rollback.
    // No in-memory identity is committed on this path; /me still verifies any later reload.
    for (const [key, value] of previous) {
      try {
        if (value === null) storage?.removeItem(key)
        else storage?.setItem(key, value)
      } catch { /* Do not mask the original persistence failure. */ }
    }
    throw new BrowserLoginStorageError('save')
  }
}

/** The token is removed first; a write-only fallback still makes reload anonymous. */
export function clearLoginStorage(keys: readonly string[], resolve: () => StoragePort = () => globalThis.localStorage) {
  let storage: StoragePort
  try { storage = resolve() } catch { throw new BrowserLoginStorageError('clear') }
  let failed = false
  for (const key of keys) {
    try { storage.removeItem(key) }
    catch {
      try { storage.setItem(key, '') } catch { failed = true }
    }
  }
  if (failed) throw new BrowserLoginStorageError('clear')
}
