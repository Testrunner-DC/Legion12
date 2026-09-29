import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/game/authoritativeCardVisibility.ts', import.meta.url), 'utf8')
const output = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { acquireAuthoritativeCardVisibility, authoritativeCardVisibilityLeaseCount } = await import(`data:text/javascript;base64,${Buffer.from(output).toString('base64')}`)
const node = visibility => ({ style:{ visibility } })

const shared = node('')
const releaseZone = acquireAuthoritativeCardVisibility(shared)
const releaseState = acquireAuthoritativeCardVisibility(shared)
assert.equal(authoritativeCardVisibilityLeaseCount(shared), 2, 'the shared lease exposes its active owner count for regression verification')
assert.equal(shared.style.visibility, 'hidden', 'overlapping layers share the hidden authority node')
releaseZone()
assert.equal(shared.style.visibility, 'hidden', 'one cancelled layer cannot reveal a node still owned by another layer')
releaseZone()
assert.equal(shared.style.visibility, 'hidden', 'release is idempotent')
releaseState()
assert.equal(shared.style.visibility, '', 'the last inverse-order release restores the original inline value')
assert.equal(authoritativeCardVisibilityLeaseCount(shared), 0, 'the last release removes the lease entry')

const prehidden = node('collapse')
const releasePrehiddenA = acquireAuthoritativeCardVisibility(prehidden)
const releasePrehiddenB = acquireAuthoritativeCardVisibility(prehidden)
releasePrehiddenB()
releasePrehiddenA()
assert.equal(prehidden.style.visibility, 'collapse', 'a non-empty original inline visibility is restored exactly')

const replaced = node('')
const replacement = node('visible')
const releaseReplaced = acquireAuthoritativeCardVisibility(replaced)
const releaseReplacement = acquireAuthoritativeCardVisibility(replacement)
releaseReplaced()
assert.equal(replaced.style.visibility, '', 'a detached/replaced old node releases only its own lease')
assert.equal(replacement.style.visibility, 'hidden', 'releasing the old node never mutates its replacement')
releaseReplacement()
assert.equal(replacement.style.visibility, 'visible', 'the replacement restores its independent original value')

const unmounted = node('')
const cleanup = [acquireAuthoritativeCardVisibility(unmounted), acquireAuthoritativeCardVisibility(unmounted)]
cleanup.splice(0).forEach(release => release())
assert.equal(unmounted.style.visibility, '', 'unmount/reset cleanup releases every outstanding owner')

const exceptional = node('')
try {
  const release = acquireAuthoritativeCardVisibility(exceptional)
  try { throw new Error('animation failed') } finally { release() }
} catch {}
assert.equal(exceptional.style.visibility, '', 'animation exceptions release the visibility lease in finally paths')

console.log('authoritative card visibility lease tests passed')
