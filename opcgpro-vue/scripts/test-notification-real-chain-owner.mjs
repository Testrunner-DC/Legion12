import assert from 'node:assert/strict'
import {closeNotificationProbeResources} from './verify-notification-real-chain.mjs'

for (const rejected of [[], ['browser'], ['vite'], ['browser', 'vite']]) {
  const called = []
  const resource = name => ({close: async () => {called.push(name); if (rejected.includes(name)) throw new Error('synthetic close failure')}})
  const failures = await closeNotificationProbeResources(resource('browser'), resource('vite'))
  assert.deepEqual(called.toSorted(), ['browser', 'vite'])
  assert.deepEqual(failures.toSorted(), rejected.toSorted())
}
assert.deepEqual(await closeNotificationProbeResources(undefined, undefined), [])
console.log(JSON.stringify({status: 'passed', cases: 5, bothOwnersAlwaysClosed: true, browsersLaunched: 0, networkCalls: 0}))
