import assert from 'node:assert/strict'
import { resolveClientReleaseVersion } from './client-release-version.mjs'

assert.equal(resolveClientReleaseVersion({ command: 'serve', supplied: '' }), 'dev')
assert.equal(resolveClientReleaseVersion({ command: 'build', supplied: 'abcdef123456' }), 'abcdef123456')
assert.equal(resolveClientReleaseVersion({ command: 'build', supplied: 'v1.2.3' }), 'v1.2.3')
assert.throws(() => resolveClientReleaseVersion({ command: 'build', supplied: '' }), /VITE_APP_VERSION/)
assert.throws(() => resolveClientReleaseVersion({ command: 'build', supplied: 'dev' }), /VITE_APP_VERSION/)

console.log('Client release version gate passed.')
