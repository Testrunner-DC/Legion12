// Select the existing canonical packager; never assemble an alternative package.
import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { execFileSync } from 'node:child_process'

const sha = /^[0-9a-f]{40}$/
const zero = '0'.repeat(40)
const verifyOnly = /^(?:docs\/|[^/]+\.md$|TwelveLegions\.(?:Platform\.)?Tests\/|opcgpro-vue\/scripts\/|scripts\/(?:test-[^/]+|fixtures\/).*)/
const docsOnly = /^(?:docs\/|[^/]+\.md$)/

export function selectPackage(eventName, event, head, changedPaths) {
  assert(sha.test(head) && head !== zero, 'CI_HEAD_INVALID')
  if (eventName === 'pull_request') return { package: false, verify: true, reason: 'pull-request-verification' }
  if (eventName === 'workflow_dispatch') return { package: true, verify: false, reason: 'explicit-canonical-build' }
  assert.equal(eventName, 'push', 'CI_EVENT_UNSUPPORTED')
  assert.equal(event.after, head, 'CI_EVENT_HEAD_MISMATCH')
  assert.equal(event.deleted, false, 'CI_DELETED_REF_REJECTED')
  if (/^refs\/tags\/v[^\s\x00-\x1f]*$/.test(event.ref)) return { package: true, verify: false, reason: 'version-tag' }
  assert.equal(event.ref, 'refs/heads/main', 'CI_REF_UNSUPPORTED')
  assert(Array.isArray(changedPaths), 'CI_CHANGED_PATHS_REQUIRED')
  for (const value of changedPaths) {
    assert(typeof value === 'string' && value && !/^[\/\\]|[\\\x00-\x1f]/.test(value)
      && !value.split('/').some(part => !part || part === '.' || part === '..'), 'CI_CHANGED_PATH_INVALID')
  }
  // Unknown source/config changes conservatively use the complete canonical gate.
  // Only an explicitly known docs/QA-only batch avoids an application release.
  const relevant = changedPaths.some(value => !verifyOnly.test(value))
  const verify = !relevant && changedPaths.some(value => !docsOnly.test(value))
  return { package: relevant, verify, reason: relevant ? 'source-or-release-tool-change' : verify ? 'qa-only-verification' : 'docs-only-no-application-build' }
}

export function readPolicy(eventName, event, head, git) {
  if (eventName === 'push' && /^refs\/tags\/v/.test(event.ref) && event.after !== head) {
    assert(sha.test(event.after), 'CI_TAG_OBJECT_INVALID')
    const resolved = git(['rev-parse', '--verify', `${event.after}^{commit}`]).trim()
    assert.equal(resolved, head, 'CI_TAG_COMMIT_MISMATCH')
    return selectPackage(eventName, { ...event, after: resolved }, head)
  }
  if (eventName !== 'push' || event.ref !== 'refs/heads/main') return selectPackage(eventName, event, head)
  assert(sha.test(head) && head !== zero && sha.test(event.before), 'CI_BASE_INVALID')
  assert.equal(event.after, head, 'CI_EVENT_HEAD_MISMATCH')
  let output
  if (event.before === zero) output = git(['ls-tree', '-r', '--name-only', '-z', head])
  else {
    try { git(['cat-file', '-e', `${event.before}^{commit}`]) }
    catch { git(['fetch', '--no-tags', '--depth=1', 'origin', event.before]) }
    // Shallow multi-commit pushes may not contain the connecting history. An
    // unproven ancestor (also a force update) conservatively builds the package;
    // it must never be misclassified as docs-only or fetch unlimited history.
    try { git(['merge-base', '--is-ancestor', event.before, head]) }
    catch { return { package: true, verify: false, reason: 'unproven-ancestor-full-canonical' } }
    output = git(['diff', '--no-renames', '--name-only', '-z', event.before, head])
  }
  assert(output.length === 0 || output.endsWith('\0'), 'CI_GIT_PATH_OUTPUT_INVALID')
  return selectPackage(eventName, event, head, output ? output.slice(0, -1).split('\0') : [])
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [eventName, eventPath, head] = process.argv.slice(2)
  assert(eventPath && fs.statSync(eventPath).isFile() && fs.statSync(eventPath).size <= 1024 * 1024, 'CI_EVENT_FILE_INVALID')
  const event = JSON.parse(fs.readFileSync(eventPath, 'utf8'))
  const git = args => execFileSync('git', args, { encoding: 'utf8', timeout: 60000,
    maxBuffer: 8 * 1024 * 1024, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
  assert(sha.test(head) && head !== zero, 'CI_HEAD_INVALID')
  const commit = git(['rev-parse', '--verify', `${head}^{commit}`]).trim()
  assert.equal(git(['rev-parse', 'HEAD']).trim(), commit, 'CI_CHECKOUT_HEAD_MISMATCH')
  console.log(JSON.stringify({ ...readPolicy(eventName, event, commit, git), commit }))
}
