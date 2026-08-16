const endpoint = process.argv[2] || 'ws://localhost:8080/ws/'

function client(name) {
  const ws = new WebSocket(endpoint)
  const messages = []
  const waiters = []
  ws.addEventListener('message', event => {
    const message = JSON.parse(String(event.data))
    messages.push(message)
    for (const waiter of [...waiters]) {
      if (waiter.predicate(message)) {
        waiters.splice(waiters.indexOf(waiter), 1)
        waiter.resolve(message)
      }
    }
  })
  const wait = (predicate, timeout = 5000) => new Promise((resolve, reject) => {
    const existing = [...messages].reverse().find(predicate)
    if (existing) return resolve(existing)
    const waiter = { predicate, resolve }
    waiters.push(waiter)
    setTimeout(() => {
      const index = waiters.indexOf(waiter)
      if (index >= 0) waiters.splice(index, 1)
      reject(new Error(`${name} wait timed out; received: ${messages.map(m => m.type).join(', ')}`))
    }, timeout)
  })
  const send = payload => ws.send(JSON.stringify(payload))
  return { name, ws, wait, send, messages }
}

const a = client('甲')
const b = client('乙')
await Promise.all([a.wait(m => m.type === 'session'), b.wait(m => m.type === 'session')])
a.send({ type: 'hello', name: a.name })
b.send({ type: 'hello', name: b.name })
await Promise.all([a.wait(m => m.type === 'session' && m.name === a.name), b.wait(m => m.type === 'session' && m.name === b.name)])

a.send({ type: 'createRoom' })
const created = await a.wait(m => m.type === 'roomState')
b.send({ type: 'joinRoom', roomCode: created.roomCode })
await Promise.all([
  a.wait(m => m.type === 'roomState' && m.players.length === 2),
  b.wait(m => m.type === 'roomState' && m.players.length === 2),
])
a.send({ type: 'ready', ready: true })
b.send({ type: 'ready', ready: true })
let [ga, gb] = await Promise.all([a.wait(m => m.type === 'gameState'), b.wait(m => m.type === 'gameState')])
if (ga.state.stateHash !== gb.state.stateHash) throw new Error('initial state hashes differ')

const clients = [a, b]
while (ga.state.phase !== 'Mulligan') {
  const visible = [ga.state.prompts?.[0], gb.state.prompts?.[0]]
  const viewer = visible.findIndex(Boolean)
  if (viewer < 0) throw new Error(`preparation stalled in ${ga.state.phase}`)
  const prompt = visible[viewer]
  const actor = clients[viewer]
  const revision = ga.state.revision
  actor.send({ type: 'gameAction', command: { type: 'resolvePrompt', promptId: prompt.promptId, choice: prompt.validChoices[0] } })
  ;[ga, gb] = await Promise.all([
    a.wait(m => m.type === 'gameState' && m.state.revision > revision),
    b.wait(m => m.type === 'gameState' && m.state.revision > revision),
  ])
}
if (ga.state.disasterDeck?.length !== 4 || ga.state.bannedDisasters?.length !== 3)
  throw new Error('disaster preparation result invalid')

a.send({ type: 'gameAction', command: { type: 'mulligan', cardInstanceIds: [] } })
b.send({ type: 'gameAction', command: { type: 'mulligan', cardInstanceIds: [] } })
ga = await a.wait(m => m.type === 'gameState' && m.state.phase === 'Main' && m.state.round === 1)
gb = await b.wait(m => m.type === 'gameState' && m.state.phase === 'Main' && m.state.round === 1)
if (ga.state.stateHash !== gb.state.stateHash) throw new Error('post-mulligan state hashes differ')
if (!ga.state.recentEvents?.some(event => event.text === '执行抽牌阶段')) throw new Error('automatic phase events missing')

const active = ga.state.activePlayer
clients[1 - active].send({ type: 'gameAction', command: { type: 'endTurn' } })
await clients[1 - active].wait(m => m.type === 'actionRejected')
clients[active].send({ type: 'gameAction', command: { type: 'endTurn' } })
ga = await a.wait(m => m.type === 'gameState' && m.state.phase === 'Main' && m.state.round === 2)
gb = await b.wait(m => m.type === 'gameState' && m.state.phase === 'Main' && m.state.round === 2)
if (ga.state.stateHash !== gb.state.stateHash) throw new Error('end-turn state hashes differ')

console.log(JSON.stringify({ ok: true, roomCode: created.roomCode, matchId: ga.state.matchId, revision: ga.state.revision, hash: ga.state.stateHash }))
a.ws.close()
b.ws.close()
