const CHUNK_FAILURE_PATTERNS = [
  /failed to fetch dynamically imported module/i,
  /error loading dynamically imported module/i,
  /importing a module script failed/i,
  /failed to load module script/i,
  /unable to preload css/i,
  /loading css chunk\b.*\bfailed/i,
  /loading chunk\b.*\bfailed/i,
  /chunkloaderror/i,
]

function collectErrorText(error, seen = new Set()) {
  if (error == null || seen.has(error)) return ''
  if (typeof error === 'string') return error
  if (typeof error !== 'object') return String(error)
  seen.add(error)
  const value = error
  const parts = [value.name, value.message, value.stack]
  if ('cause' in value) parts.push(collectErrorText(value.cause, seen))
  return parts.filter(part => typeof part === 'string' && part.length > 0).join('\n')
}

export function isChunkLoadFailure(error) {
  const text = collectErrorText(error)
  return text.length > 0 && CHUNK_FAILURE_PATTERNS.some(pattern => pattern.test(text))
}

export function chunkRecoveryStorageKey(release, pathname) {
  const safeRelease = String(release || 'unknown')
  const safePath = String(pathname || '/')
  return `l12:chunk-recovery:${encodeURIComponent(safeRelease)}:${encodeURIComponent(safePath)}`
}

export function decideChunkRecovery({ error, alreadyRecovered }) {
  if (!isChunkLoadFailure(error)) return 'ignore'
  return alreadyRecovered ? 'show-error' : 'reload'
}
