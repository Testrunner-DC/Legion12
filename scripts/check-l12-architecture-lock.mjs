import { existsSync, readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const read = path => readFileSync(join(root, path), 'utf8').replace(/\r\n/g, '\n')
const requireFile = path => {
  if (!existsSync(join(root, path))) throw new Error(`Architecture lock missing required file: ${path}`)
  return read(path)
}
const assert = (condition, message) => {
  if (!condition) throw new Error(`Architecture lock failed: ${message}`)
}

const target = requireFile('docs/ARCHITECTURE-TARGET.md')
const lock = requireFile('docs/ARCHITECTURE-LOCK.md')
const agentRules = requireFile('AGENTS.md')
for (const phase of ['P0', 'P1', 'P2', 'P3', 'P4']) {
  assert(new RegExp(`### ${phase}：[^\\n]*（已完成`).test(target), `${phase} must remain explicitly exited`)
}
assert(target.includes('P0—P4均已完成'), 'the target must retain the bounded P0-P4 exit conclusion')
assert(lock.includes('扩展而不绕过') && lock.includes('变更协议'), 'the lock must document extension and change protocol')
assert(agentRules.includes('docs/ARCHITECTURE-LOCK.md') && agentRules.includes('P0—P4 invariant'),
  'future change batches must read and preserve the architecture lock')

const ports = requireFile('服务端WebSocket/TwelveLegions/L12KernelPorts.cs')
const persistence = requireFile('服务端WebSocket/TwelveLegions/L12PersistenceContract.cs')
const rooms = requireFile('服务端WebSocket/TwelveLegions/L12RoomManager.cs')
assert(['IL12KernelCommandPort', 'IL12KernelProjectionPort', 'IL12KernelCheckpointPort', 'L12KernelProjection']
  .every(token => ports.includes(token)), 'P1/P2 kernel ports and recipient projection adapter must remain present')
assert(['CurrentStateFormatVersion', 'CurrentJournalStorageVersion', 'MinimumCheckpointRecoveryVersion']
  .every(token => persistence.includes(token)), 'P3 persistence compatibility constants must stay centralized')
assert(rooms.includes('L12KernelProjection.ForPlayer') && rooms.includes('L12KernelProjection.ForSpectator'),
  'room output must continue through the shared recipient projection adapter')

const ruleCenter = requireFile('opcgpro-vue/src/l12/site/RuleCenterPage.vue')
for (const tab of ['core', 'quick-start', 'terms', 'faq', 'construction', 'tournament', 'versions']) {
  assert(ruleCenter.includes(`id: '${tab}'`), `rule center tab ${tab} must remain available`)
}
assert(ruleCenter.includes("getPublicContent('rules.center')") && ruleCenter.includes('parsePublishedRuleCenter'),
  'the player rule center must continue consuming the published projection')

const router = requireFile('opcgpro-vue/src/router/index.ts')
const app = requireFile('opcgpro-vue/src/App.vue')
const viewport = requireFile('opcgpro-vue/src/l12/mobileViewport.ts')
const optedRoutes = [...router.matchAll(/path:\s*'([^']+)'[^\n]+landscapeCanvas:\s*true/g)].map(match => match[1]).sort()
assert(JSON.stringify(optedRoutes) === JSON.stringify(['/deck-editor', '/game', '/sandbox']),
  `logical landscape opt-in must stay route-scoped; found ${optedRoutes.join(', ')}`)
assert(app.includes('route.meta.landscapeCanvas === true') && app.includes('data-l12-landscape-canvas'),
  'the app shell must retain the explicit landscape-canvas boundary')
assert(viewport.includes('visualViewport') && viewport.includes('audioPreferences.mobileLayout'),
  'mobile layout must continue using actual visual geometry and the player setting')
assert(!/navigator\.(userAgent|platform)|screen\.orientation/.test(viewport),
  'mobile layout must not regress to device identity or physical orientation detection')

const changeGate = requireFile('scripts/verify-l12-change.ps1')
const workflow = requireFile('.github/workflows/verify-release.yml')
assert(changeGate.includes('check-l12-architecture-lock.mjs'), 'local Focused/Batch/Release verification must run this lock')
assert((workflow.match(/node scripts\/check-l12-architecture-lock\.mjs/g) ?? []).length === 1,
  'GitHub main verification must run this lock exactly once')

console.log('L12 architecture lock passed: P0-P4 exits, kernel/persistence boundaries, rule center, route-scoped adaptive layout, local and CI gates.')
