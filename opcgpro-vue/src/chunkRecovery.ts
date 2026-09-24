import {
  chunkRecoveryStorageKey,
  decideChunkRecovery,
  type ChunkRecoveryDecision,
} from './chunkRecoveryCore.mjs'

type ChunkFailureSource = 'vite-preload' | 'router'

type PreloadErrorEvent = Event & { payload?: unknown }

const releaseVersion = import.meta.env.VITE_APP_VERSION || 'unknown'
const overlayId = 'l12-chunk-recovery'
const windowMarkerPrefix = 'l12:chunk-recovery-window:'
let recoveryScheduled = false
let recoveryDestination = ''
let installed = false

function recoveryContext(targetPath?: string) {
  let target = new URL(window.location.href)
  if (targetPath) {
    try {
      const candidate = new URL(targetPath, window.location.href)
      if (candidate.origin === window.location.origin) target = candidate
    } catch { /* 非法目标保持当前页面。 */ }
  }
  return {
    destination: `${target.pathname}${target.search}${target.hash}`,
    key: chunkRecoveryStorageKey(releaseVersion, target.pathname),
  }
}

function wasRecovered(key: string) {
  try { if (window.sessionStorage.getItem(key) === '1') return true }
  catch { /* 继续检查标签页级备用标记。 */ }
  return window.name === `${windowMarkerPrefix}${key}`
}

function markRecovered(key: string) {
  try { window.sessionStorage.setItem(key, '1') }
  catch { /* 无会话存储时使用同标签页 window.name 备用标记。 */ }
  if (!window.name || window.name.startsWith(windowMarkerPrefix)) window.name = `${windowMarkerPrefix}${key}`
}

function baseHomePath() {
  const base = import.meta.env.BASE_URL || '/'
  return base.endsWith('/') ? base : `${base}/`
}

function createOverlay(title: string, message: string, stable: boolean) {
  const existing = document.getElementById(overlayId)
  if (existing) existing.remove()

  const overlay = document.createElement('section')
  overlay.id = overlayId
  overlay.setAttribute('role', stable ? 'alertdialog' : 'status')
  overlay.setAttribute('aria-live', 'assertive')
  Object.assign(overlay.style, {
    position: 'fixed', inset: '0', zIndex: '2147483647', display: 'grid', placeItems: 'center',
    boxSizing: 'border-box', padding: '24px', background: 'rgba(4, 8, 10, .94)', color: '#f2f0e9',
    fontFamily: "'Microsoft YaHei','微软雅黑',system-ui,sans-serif",
  })

  const panel = document.createElement('div')
  Object.assign(panel.style, {
    width: 'min(460px, 100%)', boxSizing: 'border-box', padding: '28px', border: '1px solid #58666d',
    background: '#101820', boxShadow: '0 24px 80px #000', textAlign: 'center',
  })
  const heading = document.createElement('h1')
  heading.textContent = title
  Object.assign(heading.style, { margin: '0 0 12px', fontSize: '22px', lineHeight: '1.4' })
  const copy = document.createElement('p')
  copy.textContent = message
  Object.assign(copy.style, { margin: '0', color: '#aeb8bc', fontSize: '15px', lineHeight: '1.8' })
  panel.append(heading, copy)

  if (stable) {
    const actions = document.createElement('div')
    Object.assign(actions.style, { display: 'flex', flexWrap: 'wrap', justifyContent: 'center', gap: '10px', marginTop: '22px' })
    const reload = document.createElement('button')
    reload.type = 'button'
    reload.textContent = '重新加载'
    Object.assign(reload.style, {
      minHeight: '44px', padding: '9px 18px', border: '1px solid #d1ad54',
      background: '#29220f', color: '#f0d478', font: 'inherit', fontWeight: '900', cursor: 'pointer',
    })
    reload.addEventListener('click', () => window.location.reload())
    const home = document.createElement('a')
    home.href = baseHomePath()
    home.textContent = '返回主页'
    Object.assign(home.style, {
      display: 'grid', minHeight: '44px', padding: '0 18px', placeItems: 'center', boxSizing: 'border-box',
      border: '1px solid #58666d', color: '#d5dde0', textDecoration: 'none', fontWeight: '900',
    })
    actions.append(reload, home)
    panel.append(actions)
    window.setTimeout(() => reload.focus(), 0)
  }

  overlay.append(panel)
  document.body.append(overlay)
}

function applyDecision(decision: ChunkRecoveryDecision, key: string, destination: string) {
  if (decision === 'ignore') return false
  if (decision === 'show-error') {
    createOverlay('页面更新未能完成', '请检查网络后重新加载；系统已停止自动重试，避免反复刷新。', true)
    return true
  }
  if (recoveryScheduled) {
    markRecovered(key)
    recoveryDestination = destination
    return true
  }
  recoveryScheduled = true
  recoveryDestination = destination
  markRecovered(key)
  createOverlay('版本已更新，正在重新加载', '正在切换到最新页面，请稍候。', false)
  window.setTimeout(() => window.location.replace(recoveryDestination), 120)
  return true
}

export function handleChunkLoadError(error: unknown, _source: ChunkFailureSource = 'router', targetPath?: string) {
  const context = recoveryContext(targetPath)
  return applyDecision(
    decideChunkRecovery({ error, alreadyRecovered: wasRecovered(context.key) }),
    context.key,
    context.destination,
  )
}

export function installChunkRecovery() {
  if (installed) return
  installed = true
  window.addEventListener('vite:preloadError', event => {
    const preloadEvent = event as PreloadErrorEvent
    if (!handleChunkLoadError(preloadEvent.payload, 'vite-preload')) return
    preloadEvent.preventDefault()
  })
}
