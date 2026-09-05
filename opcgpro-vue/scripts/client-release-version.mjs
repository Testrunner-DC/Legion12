const commitPattern = /^[0-9a-f]{7,64}$/i
const releasePattern = /^v?\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/

export function resolveClientReleaseVersion({ command, supplied }) {
  const value = String(supplied || '').trim()
  if (command !== 'build') return value && value !== 'dev' ? value : 'dev'
  if (!value || value === 'dev' || (!commitPattern.test(value) && !releasePattern.test(value))) {
    throw new Error('Production build requires VITE_APP_VERSION to be a release version or commit hash; dev is forbidden.')
  }
  return value
}
