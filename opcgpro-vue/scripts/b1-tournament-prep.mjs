import fs from 'node:fs'
const BASE = 'https://legion-12.com/testrun'
const adminPw = fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt', 'utf8').trim()
const playerPw = fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt', 'utf8').trim()
const out = { steps: [] }
const api = async (method, path, { token, body, version, key } = {}) => {
  const headers = { Accept: 'application/json' }
  if (token) headers.Authorization = 'Bearer ' + token
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (key) headers['Idempotency-Key'] = key
  if (version !== undefined) headers['If-Match'] = '"' + version + '"'
  const payload = body === undefined || version === undefined ? body : { ...body, expectedVersion: version }
  let r
  try { r = await fetch(BASE + path, { method, headers, body: payload === undefined ? undefined : JSON.stringify(payload) }) }
  catch (error) { out.steps.push({ networkFail: method + ' ' + path, message: String(error?.message), cause: String(error?.cause?.code || error?.cause || '') }); throw error }
  const text = await r.text(); let json = null
  try { json = text ? JSON.parse(text) : null } catch { }
  if (!r.ok) out.steps.push({ fail: method + ' ' + path, status: r.status, text: text.slice(0, 200) })
  return { status: r.status, json, text }
}
const login = async (u, p) => { const r = await api('POST', '/api/auth/login', { body: { username: u, password: p } }); return r.json?.token }
const me = async (t) => (await api('GET', '/api/auth/me', { token: t })).json
const admin = await login('Admin', adminPw)
const t07 = await login('彩排07', playerPw)
const t08 = await login('彩排08', playerPw)
const t01 = await login('彩排01', playerPw)
const t02 = await login('彩排02', playerPw)
out.tokens = { admin: !!admin, t07: !!t07, t08: !!t08, t01: !!t01, t02: !!t02 }
const id07 = (await me(t07))?.id, id08 = (await me(t08))?.id
const id01 = (await me(t01))?.id, id02 = (await me(t02))?.id
const idAdmin = (await me(admin))?.id
out.ids = { id07, id08, id01, id02, idAdmin }

// 好友：Admin↔08、Admin↔01（裁判必须是主办者好友）；07↔08 供转移后使用
const befriend = async (a, bId, aId, tA, tB) => {
  const r = await api('POST', '/api/friends/requests', { token: tA, body: { accountId: bId } })
  if (r.status >= 300) return { a, b: bId, request: r.status }
  const acc = await api('POST', '/api/friends/requests/' + aId + '/resolve', { token: tB, body: { accept: true } })
  return { a, b: bId, request: r.status, accept: acc.status }
}
out.friends = []
out.friends.push(await befriend('Admin', id08, idAdmin, admin, t08))
out.friends.push(await befriend('Admin', id01, idAdmin, admin, t01))
out.friends.push(await befriend('彩排07', id08, id07, t07, t08))

const decks01 = (await api('GET', '/api/decks', { token: t01 })).json
const decks02 = (await api('GET', '/api/decks', { token: t02 })).json
const list = (v) => Array.isArray(v) ? v : (v?.decks ?? [])
out.deckNames = { a: list(decks01).map(d => d.name).slice(0, 3), b: list(decks02).map(d => d.name).slice(0, 3) }
const name01 = list(decks01)[0]?.name, name02 = list(decks02)[0]?.name

const runId = Date.now().toString(16)
const ctx = await api('GET', '/api/tournaments/summaries?section=host&page=1&pageSize=1', { token: admin })
const created = await api('POST', '/api/tournaments', { token: admin, key: 'b1-' + runId + '-c', version: ctx.json.platformVersion,
  body: { tournament: { name: '[B1] ' + runId + ' 裁判观战本桌频道', format: 'single', visibility: 'public', maxPlayers: 4,
    startAt: new Date(Date.now() + 3600000).toISOString(), ruleset: 'official', description: 'B1 裁判/主办者/普通观战者本桌频道受众',
    deckVisibility: 'private', disasterMode: 'none', banList: '', roundMinutes: 30, checkInMinutes: 30, swissRounds: 1,
    registrationVisibility: 'public', lateGraceMinutes: 5, operationMode: 'managed', refereeAccountIds: [id08, id01] } } })
out.created = { status: created.status, fails: out.steps.filter(s => s.fail).slice(-1) }
const id = (created.json?.tournament || created.json)?.id
out.tournamentId = id
out.code = (await api('GET', '/api/tournaments/' + id, { token: admin })).json?.code ?? ''
const versionOf = async () => (await api('GET', '/api/tournaments/' + id, { token: admin })).json?.version
let seq = 0
for (const [name, token, deckName] of [['彩排01', t01, name01], ['彩排02', t02, name02]]) {
  seq += 1
  let v = await versionOf()
  await api('POST', '/api/tournaments/' + id + '/registrations', { token, key: 'b1-' + runId + '-reg-' + seq, version: v, body: { deckName } })
  v = await versionOf()
  await api('POST', '/api/tournaments/' + id + '/pre-check-in', { token, key: 'b1-' + runId + '-pre-' + seq, version: v, body: { deckName } })
}
let v = await versionOf()
await api('POST', '/api/tournaments/' + id + '/start', { token: admin, key: 'b1-' + runId + '-s', version: v, body: {} })
let ci = 0
for (const [name, token] of [['彩排01', t01], ['彩排02', t02]]) {
  ci += 1
  v = await versionOf()
  await api('POST', '/api/tournaments/' + id + '/rounds/1/check-in', { token, key: 'b1-' + runId + '-ci-' + ci, version: v, body: { ready: true } })
}
v = await versionOf()
await api('POST', '/api/tournaments/' + id + '/rounds/1/start', { token: admin, key: 'b1-' + runId + '-r1s', version: v, body: {} })
const view = (await api('GET', '/api/tournaments/' + id, { token: admin })).json
const match = view?.rounds?.[0]?.matches?.[0]
out.match = match ? { id: match.id, status: match.status, roomCode: match.roomCode, a: match.playerAName, b: match.playerBName } : null
out.referees = view?.referees?.length ?? null
fs.writeFileSync('D:/GPT/Legion12/artifacts/a3-clear-20261126/b1-prep.json', JSON.stringify(out, null, 2))
console.log(JSON.stringify(out, null, 1).slice(0, 2600))
