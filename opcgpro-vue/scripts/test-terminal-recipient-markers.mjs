import assert from 'node:assert/strict'
import {TerminalRecipientMarkers, RECIPIENT_WINDOW_MS} from './terminal-recipient-markers.mjs'

const nonce = '0123456789abcdef0123456789abcdef'
let cases = 0
function scenario(expectedCommands = 1) {
  let now = 10
  const receipts = [], markers = []
  const observer = new TerminalRecipientMarkers({nonce, expectedCommands, now: () => now, onMarker: row => markers.push(row)})
  const rooms = Array.from({length: expectedCommands}, (_, slot) => {
    const clients = [0, 1].map(index => ({numericRoomSlot: slot, numericClientSlot: slot * 2 + index,
      playerIndex: index, send: payload => receipts.push({client: slot * 2 + index, payload}),
      envelope: {state: {matchId: `private-match-${slot}`, you: index, phase: 'Main', revision: 7}}}))
    const room = {host: clients[0], guest: clients[1], expectedMatchId: `private-match-${slot}`}
    assert.equal(observer.bindRoom(room), true)
    return room
  })
  return {observer, rooms, receipts, markers, time: value => { now = value },
    begin: (slot = 0, actor = rooms[slot].host) => {
      const id = observer.tryBegin({stage: 'cleanup', client: actor,
        commandType: 'surrender', roomSlot: slot, sentAt: now})
      if (id !== null) assert.equal(observer.observeNativeSend(actor, id, now), true)
      return id
    },
    observation: (room, role, requestId, callbackAt = 20) => ({message: {type: 'gameState',
      requestId: role === 0 ? requestId : null, state: {phase: 'GameOver', matchId: room.expectedMatchId,
        you: role, revision: 8}}, callbackAt, parseStartedAt: callbackAt + 1, parsedAt: callbackAt + 2, bytes: 200000})}
}

for (const expectedCommands of [0, 33]) {
  assert.throws(() => new TerminalRecipientMarkers({nonce, expectedCommands})); cases++
}
{
  const s = scenario(30)
  for (let slot = 0; slot < 30; slot++) {
    const room = s.rooms[slot], requestId = s.begin(slot)
    assert.match(requestId, new RegExp(`^l12diag:${nonce}:${slot}:[a-f0-9]{32}$`))
    for (const [role, client] of [room.host, room.guest].entries()) {
      const marker = s.observer.observeParsed(client, s.observation(room, role, requestId))
      assert.ok(marker); assert.equal(s.observer.afterDispatch(marker, 23), true)
    }
  }
  const report = s.observer.report()
  assert.equal(report.trace_complete, 1); assert.equal(report.original_3s_satisfied, 1)
  assert.equal(report.online_capacity_accepted, 0); assert.equal(report.rows.length, 60)
  assert.equal(s.receipts.length, 60); assert.equal(s.markers.length, 30)
  for (const receipt of s.receipts) {
    assert.deepEqual(Object.keys(receipt.payload), ['type', 'diagnosticNonce', 'diagnosticSlot', 'diagnosticRecipient'])
    assert.equal(receipt.payload.type, 'ping'); assert.equal(receipt.payload.diagnosticRecipient, receipt.client % 2)
  }
  const text = JSON.stringify(report)
  assert.ok(!text.includes(nonce) && !text.includes('private-match'))
  assert.ok(report.rows.every(row => Object.values(row).every(Number.isFinite)))
  report.rows[0].revision = 999
  assert.equal(s.observer.report().rows[0].revision, 8); cases++
}
for (const mutation of [
  row => { row.message.state.matchId = 'different' }, row => { row.message.state.you = 1 },
  row => { row.message.requestId = 'different' }, row => { row.message.state.revision = 7 },
  row => { row.message.state.revision = 8.1 }, row => { row.bytes = 0 },
  row => { row.bytes = 2 * 1024 ** 2 + 1 }, row => { row.parseStartedAt = 0 },
]) {
  const s = scenario(), requestId = s.begin(), room = s.rooms[0], observation = s.observation(room, 0, requestId)
  mutation(observation)
  assert.equal(s.observer.observeParsed(room.host, observation), null)
  assert.equal(s.receipts.length, 0); assert.equal(s.observer.report().trace_complete, 0); cases++
}
{
  const s = scenario(), requestId = s.begin(), room = s.rooms[0]
  for (const [role, client] of [room.host, room.guest].entries()) {
    const marker = s.observer.observeParsed(client, s.observation(room, role, requestId, role === 1 ? 3220 : 20))
    assert.equal(s.observer.afterDispatch(marker, role === 1 ? 3223 : 23), true)
  }
  assert.equal(s.observer.report().trace_complete, 1)
  assert.equal(s.observer.report().original_3s_satisfied, 0)
  assert.equal(s.observer.report().rows[1].exceeded_original_3s, 1); cases++
}
{
  const s = scenario(), requestId = s.begin(), room = s.rooms[0]
  const observation = s.observation(room, 0, requestId, 10 + RECIPIENT_WINDOW_MS)
  assert.equal(s.observer.observeParsed(room.host, observation), null)
  assert.equal(s.observer.report().expired_records, 1); assert.equal(s.receipts.length, 0); cases++
}
{
  const s = scenario(), requestId = s.begin(), room = s.rooms[0]
  const observation = s.observation(room, 0, requestId)
  const marker = s.observer.observeParsed(room.host, observation)
  assert.equal(s.observer.observeParsed(room.host, observation), null)
  assert.equal(s.observer.afterDispatch(marker, 23), true)
  assert.equal(s.receipts.length, 1)
  assert.equal(s.observer.afterDispatch(marker, 23), false)
  assert.equal(s.receipts.length, 1); assert.equal(s.observer.afterDispatch({}, 23), false); cases++
}
{
  const s = scenario(), requestId = s.begin(), room = s.rooms[0]
  room.host.send = () => { throw new Error('private transport failure') }
  const marker = s.observer.observeParsed(room.host, s.observation(room, 0, requestId))
  assert.equal(s.observer.afterDispatch(marker, 23), false)
  assert.equal(s.observer.report().invalid_records, 1)
  assert.ok(!JSON.stringify(s.observer.report()).includes('private transport failure')); cases++
}
{
  const s = scenario(), room = s.rooms[0]
  assert.equal(s.observer.tryBegin({stage: 'rooms-30', commandType: 'surrender', client: room.host, roomSlot: 0}), null)
  assert.equal(s.observer.tryBegin({stage: 'cleanup', commandType: 'endTurn', client: room.host, roomSlot: 0}), null)
  room.guest.envelope.state.revision = 6
  assert.equal(s.begin(), null); assert.equal(s.observer.report().invalid_records, 1); cases++
}
{
  const s = scenario(), room = s.rooms[0], requestId = s.begin()
  const marker = s.observer.observeParsed(room.host, s.observation(room, 0, requestId))
  assert.equal(s.observer.afterDispatch(marker, 1), false)
  assert.equal(s.receipts.length, 0); assert.equal(s.observer.report().trace_complete, 0); cases++
}
{
  const s = scenario(), room = s.rooms[0], requestId = s.begin(0, room.guest)
  for (const [role, client] of [room.guest, room.host].entries()) {
    const observation = s.observation(room, role, requestId)
    observation.message.state.you = client.playerIndex
    const marker = s.observer.observeParsed(client, observation)
    assert.ok(marker); assert.equal(s.observer.afterDispatch(marker, 23), true)
  }
  assert.equal(s.observer.report().trace_complete, 1)
  assert.deepEqual(s.observer.report().rows.map(row => [row.recipient, row.client_slot]), [[0, 1], [1, 0]])
  assert.deepEqual(s.receipts.map(receipt => [receipt.client, receipt.payload.diagnosticRecipient]), [[1, 0], [0, 1]]); cases++
}
{
  const s = scenario(), room = s.rooms[0], requestId = s.begin()
  for (const [role, client] of [room.host, room.guest].entries()) {
    const observation = s.observation(room, role, requestId)
    observation.message.state.revision += role
    const marker = s.observer.observeParsed(client, observation)
    assert.equal(s.observer.afterDispatch(marker, 23), true)
  }
  assert.equal(s.observer.report().trace_complete, 0); cases++
}
{
  const s = scenario(), room = s.rooms[0], requestId = s.begin()
  const peerObservation = s.observation(room, 1, requestId)
  peerObservation.message.requestId = requestId
  assert.equal(s.observer.observeParsed(room.guest, peerObservation), null)
  assert.equal(s.observer.observeParsed({playerIndex: 1}, peerObservation), null)
  assert.equal(s.receipts.length, 0); assert.equal(s.observer.report().trace_complete, 0); cases++
}
{
  const s = scenario(), room = s.rooms[0]
  const requestId = s.observer.tryBegin({stage: 'cleanup', client: room.host,
    commandType: 'surrender', roomSlot: 0, sentAt: 10})
  for (const [role, client] of [room.host, room.guest].entries()) {
    const marker = s.observer.observeParsed(client, s.observation(room, role, requestId))
    assert.equal(s.observer.afterDispatch(marker, 23), true)
  }
  assert.equal(s.observer.report().trace_complete, 0)
  assert.ok(s.observer.report().rows.every(row => row.native_send_valid === 0))
  assert.equal(s.observer.observeNativeSend(room.host, requestId, 11), false)
  assert.equal(s.observer.report().trace_complete, 0)
  assert.equal(s.observer.report().rows[0].marker_to_callback_us, 10000); cases++
}
for (const arm of ['peer', 'wrong-request', 'before-marker', 'duplicate']) {
  const s = scenario(), room = s.rooms[0]
  const requestId = s.observer.tryBegin({stage: 'cleanup', client: room.host,
    commandType: 'surrender', roomSlot: 0, sentAt: 10})
  if (arm === 'duplicate') assert.equal(s.observer.observeNativeSend(room.host, requestId, 10), true)
  const client = arm === 'peer' ? room.guest : room.host
  const id = arm === 'wrong-request' ? 'different' : requestId
  assert.equal(s.observer.observeNativeSend(client, id, arm === 'before-marker' ? 9 : 11), false)
  assert.equal(s.observer.report().trace_complete, 0); cases++
}
{
  const s = scenario(), room = s.rooms[0]
  const requestId = s.observer.tryBegin({stage: 'cleanup', client: room.host,
    commandType: 'surrender', roomSlot: 0, sentAt: 10})
  assert.equal(s.observer.observeNativeSend(room.host, requestId, 11), true)
  for (const [role, client] of [room.host, room.guest].entries()) {
    const marker = s.observer.observeParsed(client, s.observation(room, role, requestId))
    assert.equal(s.observer.afterDispatch(marker, 23), true)
  }
  assert.equal(s.observer.report().trace_complete, 1)
  assert.equal(s.observer.report().rows[0].callback_us, 9000)
  assert.equal(s.observer.report().rows[0].marker_to_callback_us, 10000); cases++
}
console.log(`Terminal recipient markers passed: ${cases} bounded identity, privacy, timing and recovery cases; no online workload`)
