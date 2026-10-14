import assert from 'node:assert/strict'
export function hasReliableQueuedPayloadDispatch(source) {
  // The third argument is observational, not a replacement for the existing queue flag.
  return /\boutbound\.TryEnqueue\(queued,\s*message\.ReplaceableGameState(?:,\s*messageDiagnostic)?\)/.test(source)
}
const legacy='if (outbound.TryEnqueue(queued, message.ReplaceableGameState))'
assert.equal(hasReliableQueuedPayloadDispatch(legacy),true)
assert.equal(hasReliableQueuedPayloadDispatch(legacy.replace('State)', 'State, messageDiagnostic)')),true)
for (const bad of [
  legacy.replace('queued','other'),
  legacy.replace('message.ReplaceableGameState','true'),
  legacy.replace('message.ReplaceableGameState','false'),
  legacy.replace('State)','State, unknown)'),
  legacy.replace('State)','State, messageDiagnostic, unknown)'),
  legacy.replace('TryEnqueue','TryDrop'),
]) assert.equal(hasReliableQueuedPayloadDispatch(bad),false)
