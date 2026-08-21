const endpoint = process.argv[2] || 'ws://localhost:8080/ws'
const expectedProtocolVersion = 1
const timeoutMs = Number(process.env.L12_WS_PROBE_TIMEOUT_MS || 10_000)
const authToken = String(process.env.L12_SMOKE_AUTH_TOKEN || '').trim()

function waitForOpen(ws) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`WebSocket open timed out: ${endpoint}`)), timeoutMs)
    ws.addEventListener('open', () => {
      clearTimeout(timer)
      resolve()
    }, { once: true })
    ws.addEventListener('error', () => {
      clearTimeout(timer)
      reject(new Error(`WebSocket transport failed: ${endpoint}`))
    }, { once: true })
  })
}

function waitForMessage(ws, predicate, label) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      cleanup()
      reject(new Error(`WebSocket ${label} timed out: ${endpoint}`))
    }, timeoutMs)
    const onMessage = event => {
      let message
      try { message = JSON.parse(String(event.data)) }
      catch { return }
      if (!predicate(message)) return
      cleanup()
      resolve(message)
    }
    const onClose = event => {
      cleanup()
      reject(new Error(`WebSocket closed before ${label}: ${event.code} ${event.reason || ''}`.trim()))
    }
    const onError = () => {
      cleanup()
      reject(new Error(`WebSocket failed before ${label}: ${endpoint}`))
    }
    const cleanup = () => {
      clearTimeout(timer)
      ws.removeEventListener('message', onMessage)
      ws.removeEventListener('close', onClose)
      ws.removeEventListener('error', onError)
    }
    ws.addEventListener('message', onMessage)
    ws.addEventListener('close', onClose, { once: true })
    ws.addEventListener('error', onError, { once: true })
  })
}

const ws = new WebSocket(endpoint)
try {
  await waitForOpen(ws)
  const probeWait = waitForMessage(ws, message => message.type === 'deploymentProbe', 'deployment probe')
  ws.send(JSON.stringify({ type: 'deploymentProbe' }))
  const probe = await probeWait
  if (probe.service !== 'twelve-legions' || probe.protocolVersion !== expectedProtocolVersion)
    throw new Error(`Unexpected deployment probe: ${JSON.stringify(probe)}`)

  let authenticated = false
  if (authToken) {
    const sessionWait = waitForMessage(ws, message => message.type === 'session', 'authenticated session')
    ws.send(JSON.stringify({ type: 'hello', authToken }))
    await sessionWait
    authenticated = true
  }

  console.log(JSON.stringify({ ok: true, endpoint, protocolVersion: probe.protocolVersion, authenticated }))
} finally {
  ws.close()
}
