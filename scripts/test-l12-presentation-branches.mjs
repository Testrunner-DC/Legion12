import assert from 'node:assert/strict'
import { presentationBranches } from './lib/l12-presentation-branches.mjs'

const branch = { SceneId: 'volley-front', BranchLabel: '前排', RequiredChoices: { source: 'known', volleyMode: 'mode:front' } }
const before = JSON.stringify(branch)
assert.deepEqual(presentationBranches([branch]), [{ sceneId: 'volley-front', branchLabel: '前排', requiredChoices: { source: 'known', volleyMode: 'mode:front' } }])
assert.equal(JSON.stringify(branch), before)
assert.deepEqual(presentationBranches([{ SceneId: 'only-condition', RequiredChoices: { mode: 'use' } }]), [{ sceneId: 'only-condition', branchLabel: '', requiredChoices: { mode: 'use' } }])
assert.deepEqual(presentationBranches([{ SceneId: 'only-label', BranchLabel: '结果分支' }]), [{ sceneId: 'only-label', branchLabel: '结果分支', requiredChoices: {} }])
assert.deepEqual(presentationBranches([{ SceneId: 'plain', BranchLabel: null, RequiredChoices: null }]), [])
assert.deepEqual(presentationBranches([{ SceneId: 'old-unused-field', BranchId: 'not-a-runtime-field' }]), [])
const alternatives = ['front', 'back', 'single'].map(mode => ({ SceneId: 'volley-' + mode, BranchLabel: mode, RequiredChoices: { volleyMode: 'mode:' + mode } }))
assert.equal(presentationBranches(alternatives).length, 3)
assert.equal(new Set(presentationBranches(alternatives).map(item => JSON.stringify(item.requiredChoices))).size, 3)
assert.throws(() => presentationBranches([{ BranchLabel: 'missing identity' }]), /identity/)
assert.throws(() => presentationBranches([{ SceneId: 'bad', RequiredChoices: [] }]), /metadata/)
assert.throws(() => presentationBranches([{ SceneId: 'bad', RequiredChoices: { mode: 1 } }]), /strings/)
console.log(JSON.stringify({ status: 'passed', cases: 9, nonMutating: true, branchTuplePreserved: true }))
