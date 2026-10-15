import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { gunzipSync } from 'node:zlib'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repositoryRoot = path.resolve(root, '..')
const fixturePath = path.resolve(process.env.L12_SPECTATOR_SINGLE_OWNER_FIXTURE
  || path.join(root, 'scripts/fixtures/battle-authority-engine-v5.json.gz.b64'))
const stateFixturePath = process.env.L12_SPECTATOR_SINGLE_OWNER_STATE_FIXTURE
  ? path.resolve(process.env.L12_SPECTATOR_SINGLE_OWNER_STATE_FIXTURE) : null
const output = path.resolve(process.env.L12_SPECTATOR_SINGLE_OWNER_OUT
  || 'D:/GPT/Legion12/artifacts/spectator-single-visual-owner-20261015')
const expectation = String(process.env.L12_SPECTATOR_SINGLE_OWNER_EXPECT || 'red').trim().toLowerCase()
assert.ok(['red','green'].includes(expectation),
  `unsupported L12_SPECTATOR_SINGLE_OWNER_EXPECT: ${expectation}`)
const requestedStateCase = String(process.env.L12_SPECTATOR_SINGLE_OWNER_STATE_CASE || '').trim()
const requestedStateInstance = String(process.env.L12_SPECTATOR_SINGLE_OWNER_STATE_INSTANCE || '').trim()
const requestedProbe = String(process.env.L12_SPECTATOR_SINGLE_OWNER_PROBE || '').trim()
assert.ok(!requestedProbe || ['player-native-click', 'player-inline-minimize', 'zone-grave-decoded-source', 'zone-grave-pile-fallback', 'combat-hidden-clone', 'state-decode-cancel'].includes(requestedProbe),
  `unsupported L12_SPECTATOR_SINGLE_OWNER_PROBE: ${requestedProbe}`)
const zoneFixturePath = path.resolve(process.env.L12_SPECTATOR_SINGLE_OWNER_ZONE_FIXTURE
  || 'D:/GPT/Legion12/artifacts/battle-animation-all-transfers-20261004/authority-grave-entry-fixture-v6.json')
const formal19Commit = '19ea6cba42d3be6f42d8ef2d62dc5fa190ffc3e4'
const expectedFormalBlobs = {
  'opcgpro-vue/src/l12/game/GameBoard.vue': '87409f759982260066b89460edc1e431a7f681c2',
  'opcgpro-vue/src/l12/game/CardStateTransitionLayer.vue': '9e2cfcf21ceea7608dac708b2cd4cfbe8dbf5c8a',
  'opcgpro-vue/src/l12/game/ZoneMovementPresentationLayer.vue': '215752d4ec58e91b3c78b1894375aa22c18dbbe9',
  'opcgpro-vue/src/l12/game/presentationSequenceCoordinator.ts': '5cc069a57b4624e63c7fb308ae93facbb8d32828',
  'opcgpro-vue/src/l12/game/visualTransitionProjection.ts': '3c6a07bf36ba0fc18964a07e9fa038b64388ea0b',
  'opcgpro-vue/src/l12/game/authoritativeCardVisibility.ts': 'f977f3d5103120c441d8154a5730559a20cf2bca',
  'opcgpro-vue/src/style.css': 'e7402e8ad5d98f50b18c1f230283e1f081d7c307',
}
const candidatePaths = new Set([
  'opcgpro-vue/src/l12/game/GameBoard.vue',
  'opcgpro-vue/src/l12/game/CardStateTransitionLayer.vue',
  'opcgpro-vue/src/l12/game/ZoneMovementPresentationLayer.vue',
  'opcgpro-vue/src/l12/game/presentationSequenceCoordinator.ts',
  'opcgpro-vue/src/l12/game/visualTransitionProjection.ts',
])
const candidateOnlyPaths = [
  'opcgpro-vue/src/l12/game/authoritativeCardVisualSnapshot.ts',
]

if (!fs.existsSync(fixturePath)) throw new Error(`Fixture not found: ${fixturePath}`)
if (stateFixturePath && !fs.existsSync(stateFixturePath))
  throw new Error(`State fixture not found: ${stateFixturePath}`)
if (fs.existsSync(output)) throw new Error(`Refusing to overwrite evidence directory: ${output}`)
fs.mkdirSync(output, { recursive: true })

const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex')
const storedFixture = fs.readFileSync(fixturePath)
const fixtureBytes = fixturePath.endsWith('.gz.b64')
  ? gunzipSync(Buffer.from(storedFixture.toString('utf8').trim(), 'base64'))
  : storedFixture
const fixture = JSON.parse(fixtureBytes.toString('utf8'))
const fixtureSha256 = sha256(fixtureBytes)
const fixtureContainerSha256 = sha256(storedFixture)
const storedStateFixture = stateFixturePath ? fs.readFileSync(stateFixturePath) : null
const stateFixtureBytes = storedStateFixture && stateFixturePath?.endsWith('.gz.b64')
  ? gunzipSync(Buffer.from(storedStateFixture.toString('utf8').trim(), 'base64'))
  : storedStateFixture
const stateFixture = stateFixtureBytes ? JSON.parse(stateFixtureBytes.toString('utf8')) : null
const stateFixtureSha256 = stateFixtureBytes ? sha256(stateFixtureBytes) : null
const storedZoneFixture = ['zone-grave-decoded-source', 'zone-grave-pile-fallback'].includes(requestedProbe)
  ? fs.readFileSync(zoneFixturePath) : null
const zoneFixture = storedZoneFixture ? JSON.parse(storedZoneFixture.toString('utf8')) : null
const zoneFixtureSha256 = storedZoneFixture ? sha256(storedZoneFixture) : null
const git = (...args) => execFileSync('git', args, { cwd: repositoryRoot, encoding: 'utf8' }).trim()
const headCommit = git('rev-parse', 'HEAD')
const sourceBindings = Object.entries(expectedFormalBlobs).map(([relativePath, expectedBlob]) => {
  const headBlob = git('rev-parse', `HEAD:${relativePath}`)
  const formalBlob = git('rev-parse', `${formal19Commit}:${relativePath}`)
  const bytes = fs.readFileSync(path.join(repositoryRoot, relativePath))
  const diskBlob = git('hash-object', '--', relativePath)
  assert.equal(formalBlob, expectedBlob, `${relativePath}: formal-19 blob binding changed`)
  assert.equal(headBlob, formalBlob, `${relativePath}: HEAD baseline no longer matches formal 19`)
  if (expectation === 'red') assert.equal(diskBlob, formalBlob,
    `${relativePath}: RED must execute formal-19 disk bytes`)
  else if (candidatePaths.has(relativePath)) assert.notEqual(diskBlob, formalBlob,
    `${relativePath}: GREEN must execute the current local candidate, not formal 19`)
  else assert.equal(diskBlob, formalBlob,
    `${relativePath}: unchanged support source drifted from formal 19`)
  return { relativePath, headBlob, formalBlob, diskBlob, sha256: sha256(bytes), bytes: bytes.length }
})
for (const relativePath of candidateOnlyPaths) {
  const bytes = fs.readFileSync(path.join(repositoryRoot, relativePath))
  const diskBlob = git('hash-object', '--', relativePath)
  sourceBindings.push({ relativePath, headBlob: null, formalBlob: null, diskBlob,
    sha256: sha256(bytes), bytes: bytes.length, candidateOnly: true })
}

function assertSourceBindingsStable() {
  for (const binding of sourceBindings) {
    const bytes = fs.readFileSync(path.join(repositoryRoot, binding.relativePath))
    assert.equal(git('hash-object', '--', binding.relativePath), binding.diskBlob,
      `${binding.relativePath}: disk blob changed during browser evidence capture`)
    assert.equal(sha256(bytes), binding.sha256,
      `${binding.relativePath}: disk SHA changed during browser evidence capture`)
  }
}

function deepEqual(left, right) {
  return JSON.stringify(left) === JSON.stringify(right)
}

function clone(value) {
  return JSON.parse(JSON.stringify(value))
}

function eventSequences(events) {
  if (!Array.isArray(events) || events.length > 128) return null
  const result = []
  for (const event of events) {
    const sequence = event?.sequence
    if (!event || typeof event !== 'object' || Array.isArray(event)
      || !Number.isSafeInteger(sequence) || sequence <= 0
      || (result.length && sequence <= result.at(-1))) return null
    result.push(sequence)
  }
  return result
}

// JavaScript transcription of L12RecentEventWindowDelta.TryCreate. It is used
// only to exercise the production browser decoder; authority still comes from
// the stored engine projections selected below.
function createEventWindow(previous, current) {
  const before = previous?.state?.recentEvents
  const after = current?.state?.recentEvents
  const baseSequences = eventSequences(before)
  const resultSequences = eventSequences(after)
  if (!baseSequences || !resultSequences || !before.length || !after.length || deepEqual(before, after)) return null
  const removeFirst = baseSequences.indexOf(resultSequences[0])
  if (removeFirst < 0) return null
  const retained = baseSequences.length - removeFirst
  if (retained > resultSequences.length) return null
  for (let index = 0; index < retained; index += 1)
    if (!deepEqual(before[index + removeFirst], after[index])) return null
  return { baseSequences, removeFirst, append: clone(after.slice(retained)), resultSequences }
}

// JavaScript transcription of L12SnapshotWireCodec.BuildChanges, including its
// object/array traversal order and recentEvents exclusion when window-v1 applies.
function buildChanges(previous, current, pathParts, changes, skipRecentEvents) {
  if (deepEqual(previous, current)) return
  if (previous && current && typeof previous === 'object' && typeof current === 'object'
    && !Array.isArray(previous) && !Array.isArray(current)) {
    for (const key of Object.keys(previous)) {
      if (Object.hasOwn(current, key) && deepEqual(previous[key], current[key])) continue
      const next = [...pathParts, key]
      if (skipRecentEvents && next.length === 2 && next[0] === 'state' && key === 'recentEvents') continue
      if (!Object.hasOwn(current, key)) changes.push({ path: next, value: null, remove: true })
      else buildChanges(previous[key], current[key], next, changes, skipRecentEvents)
    }
    for (const key of Object.keys(current)) {
      if (!Object.hasOwn(previous, key)) changes.push({ path: [...pathParts, key], value: clone(current[key]), remove: false })
    }
    return
  }
  if (Array.isArray(previous) && Array.isArray(current) && previous.length === current.length) {
    for (let index = 0; index < current.length; index += 1)
      if (!deepEqual(previous[index], current[index]))
        buildChanges(previous[index], current[index], [...pathParts, index], changes, skipRecentEvents)
    return
  }
  changes.push({ path: pathParts, value: clone(current), remove: false })
}

function createDelta(previous, current) {
  const eventWindow = createEventWindow(previous, current)
  const changes = []
  buildChanges(previous, current, [], changes, Boolean(eventWindow))
  const delta = {
    type: 'gameStateDelta',
    matchId: current.state.matchId,
    baseRevision: previous.state.revision,
    revision: current.state.revision,
    changes,
  }
  if (eventWindow) delta.eventWindow = eventWindow
  return delta
}

function applyDeltaOracle(previous, delta) {
  const result = clone(previous)
  for (const change of delta.changes) {
    let target = result
    for (const segment of change.path.slice(0, -1)) target = target[segment]
    const key = change.path.at(-1)
    if (change.remove) delete target[key]
    else target[key] = clone(change.value)
  }
  if (delta.eventWindow) {
    const events = result.state.recentEvents
    assert.deepEqual(eventSequences(events), delta.eventWindow.baseSequences)
    result.state.recentEvents = [
      ...events.slice(delta.eventWindow.removeFirst),
      ...clone(delta.eventWindow.append),
    ]
    assert.deepEqual(eventSequences(result.state.recentEvents), delta.eventWindow.resultSequences)
  }
  return result
}

function frameByLabel(chainName, label) {
  const chain = fixture[chainName]
  assert.ok(Array.isArray(chain), `missing engine chain ${chainName}`)
  const frame = chain.find(item => item.label === label)
  assert.ok(frame?.spectator, `missing spectator frame ${chainName}/${label}`)
  return frame
}

function assertPublicSpectatorSnapshot(snapshot, label) {
  assert.ok(snapshot && Array.isArray(snapshot.players), `${label}: invalid spectator snapshot`)
  for (const player of snapshot.players) {
    assert.equal(Object.hasOwn(player, 'hand'), false, `${label}: spectator projection exposed private hand`)
    assert.ok(Number.isSafeInteger(player.handCount), `${label}: spectator handCount missing`)
  }
  assert.ok(Array.isArray(snapshot.prompts) && snapshot.prompts.length === 0,
    `${label}: spectator projection exposed private prompts`)
}

function publicEnvelope(state, tournament = false) {
  const envelope = {
    type: 'gameState', spectating: true, gmEnabled: false, observerView: 'public',
    playerBadges: [], state: clone(state),
  }
  if (tournament) Object.assign(envelope, {
    tournamentId: 'spectator-red-tournament', tournamentCode: 'SINGLE-OWNER',
    tournamentMatchId: `table-${state.matchId}`,
  })
  return envelope
}

function cardLocations(snapshot) {
  const result = new Map()
  for (const player of snapshot.players ?? []) {
    const add = (cards, zone) => {
      for (const card of cards ?? []) if (card?.instanceId)
        result.set(card.instanceId, { card, zone, playerIndex: player.playerIndex })
    }
    for (const row of player.field ?? []) add(row, 'field')
    add(player.graveyard, 'graveyard')
    add(player.resolving, 'resolving')
    add([player.relic, ...(player.extraRelics ?? [])], 'relic')
  }
  return result
}

function findRestAuthorityCase() {
  const sources = [
    ...(stateFixture ? [{ label: 'focused-handle-export', value: stateFixture }] : []),
    { label: 'battle-authority-engine-v5', value: fixture },
  ]
  for (const source of sources) {
    const preferred = requestedStateCase ? [requestedStateCase]
      : ['attack', 'activeRest', 'ready', 'readyResolved', 'readyResolvedExtraRelic', ...Object.keys(source.value)]
    for (const chainName of [...new Set(preferred)]) {
      const chain = source.value[chainName]
    if (!Array.isArray(chain)) continue
    for (let index = 1; index < chain.length; index += 1) {
      const before = chain[index - 1]?.spectator
      const after = chain[index]?.spectator
      if (!before || !after || after.revision <= before.revision) continue
      const baselineSequences = new Set((before.recentEvents ?? []).map(event => event.sequence))
      for (const event of after.recentEvents ?? []) {
        const fact = event.playerCardStateTransition
        if (baselineSequences.has(event.sequence) || !fact?.instanceId || fact.fromTapped === fact.toTapped) continue
        if (requestedStateInstance && fact.instanceId !== requestedStateInstance) continue
        const beforeCard = cardLocations(before).get(fact.instanceId)?.card
        const afterCard = cardLocations(after).get(fact.instanceId)?.card
        if (!beforeCard || !afterCard || Boolean(beforeCard.tapped) !== Boolean(fact.fromTapped)
          || Boolean(afterCard.tapped) !== Boolean(fact.toTapped)) continue
        return { source: source.label, chainName, beforeLabel: chain[index - 1].label,
          afterLabel: chain[index].label, before, after, event, fact,
          playerIndex: cardLocations(after).get(fact.instanceId)?.playerIndex,
          authority: 'explicit-playerCardStateTransition' }
      }
      const beforeLocations = cardLocations(before)
      const afterLocations = cardLocations(after)
      const freshEvents = (after.recentEvents ?? []).filter(event => !baselineSequences.has(event.sequence))
      const orderingEvent = freshEvents.at(-1)
      if (!orderingEvent) continue
      for (const [instanceId, beforeLocation] of beforeLocations) {
        if (requestedStateInstance && instanceId !== requestedStateInstance) continue
        const afterLocation = afterLocations.get(instanceId)
        if (!afterLocation || Boolean(beforeLocation.card.tapped) === Boolean(afterLocation.card.tapped)) continue
        return { source: source.label, chainName, beforeLabel: chain[index - 1].label,
          afterLabel: chain[index].label, before, after, event: orderingEvent,
          fact: { instanceId, fromTapped: Boolean(beforeLocation.card.tapped),
            toTapped: Boolean(afterLocation.card.tapped) },
          playerIndex: afterLocation.playerIndex, authority: 'legacy-revision-state-diff' }
      }
    }
  }
  }
  return null
}

function nextSequence(snapshot) {
  return Math.max(0, ...(snapshot.recentEvents ?? []).map(event => event.sequence)) + 1
}

function createRestSameTextControl(restCase) {
  const state = clone(restCase.after)
  state.revision += 1
  state.stateHash = `${state.stateHash ?? 'state'}-same-text-distinct`
  const location = cardLocations(state).get(restCase.fact.instanceId)
  assert.ok(location, 'rest control card missing')
  const fromTapped = Boolean(restCase.fact.toTapped)
  const toTapped = !fromTapped
  location.card.tapped = toTapped
  const event = clone(restCase.event)
  event.sequence = nextSequence(state)
  event.playerCardStateTransition = { instanceId: restCase.fact.instanceId, fromTapped, toTapped }
  event.cards = (event.cards ?? []).map(card => card.instanceId === restCase.fact.instanceId
    ? { ...card, tapped: toTapped } : card)
  // Deliberately retain event.type and event.text. A different authoritative
  // sequence must remain visible even when its human wording is identical.
  state.recentEvents = [...state.recentEvents, event]
  state.lastAction = event
  return { state, sequence: event.sequence, text: event.text, instanceId: restCase.fact.instanceId }
}

const entryBefore = frameByLabel('galahadEntry', 'entry-baseline-hand').spectator
const entryAfter = frameByLabel('galahadEntry', 'entry-committed-declaration').spectator
const entryRelease = frameByLabel('galahadEntry', 'entry-followup-5-response-stack-response').spectator
assertPublicSpectatorSnapshot(entryBefore, 'galahad entry baseline')
assertPublicSpectatorSnapshot(entryAfter, 'galahad entry commit')
assertPublicSpectatorSnapshot(entryRelease, 'galahad entry release')
assert.ok(entryAfter.waitingPrompt, 'Galahad commit must retain the real spectator waiting prompt')
assert.equal(entryRelease.waitingPrompt, null, 'Galahad release must clear the real spectator waiting prompt')
const entryEvent = (entryAfter.recentEvents ?? []).find(event => event.type === 'play'
  && event.cards?.some(card => card.instanceId === 'real-galahad'))
assert.ok(entryEvent, 'real Galahad spectator play event missing')
assert.equal(cardLocations(entryBefore).has('real-galahad'), false, 'Galahad already public before entry')
assert.equal(cardLocations(entryAfter).get('real-galahad')?.zone, 'field', 'Galahad did not settle on field')

const restCase = findRestAuthorityCase()
const fixturePreconditions = {
  publicSpectatorHandsAbsent: true,
  entry: { chain: 'galahadEntry', before: 'entry-baseline-hand', after: 'entry-committed-declaration',
    release: 'entry-followup-5-response-stack-response', instanceId: 'real-galahad', sequence: entryEvent.sequence },
  rest: restCase ? {
    source: restCase.source, chain: restCase.chainName,
    before: restCase.beforeLabel, after: restCase.afterLabel,
    instanceId: restCase.fact.instanceId, sequence: restCase.event.sequence,
    fromTapped: restCase.fact.fromTapped, toTapped: restCase.fact.toTapped,
    authority: restCase.authority,
  } : null,
  missing: restCase ? [] : ['stored engine fixture has no spectator playerCardStateTransition fact'],
}

function buildScenario(name, before, after, instanceId, control = null, release = null,
  expected = {}) {
  assertPublicSpectatorSnapshot(before, `${name} baseline`)
  assertPublicSpectatorSnapshot(after, `${name} commit`)
  const ordinaryBaseline = publicEnvelope(before, false)
  const ordinaryCommit = publicEnvelope(after, false)
  const ordinaryDelta = createDelta(ordinaryBaseline, ordinaryCommit)
  assert.deepEqual(applyDeltaOracle(ordinaryBaseline, ordinaryDelta), ordinaryCommit,
    `${name}: local codec transcription failed to reconstruct engine frame`)
  const ordinaryFullBytes = Buffer.byteLength(JSON.stringify(ordinaryCommit))
  const ordinaryDeltaBytes = Buffer.byteLength(JSON.stringify(ordinaryDelta))
  assert.ok(ordinaryDeltaBytes < ordinaryFullBytes * 0.9,
    `${name}: production codec would choose full instead of this delta (${ordinaryDeltaBytes}/${ordinaryFullBytes})`)
  const scenario = {
    name, instanceId, expected,
    ordinary: {
      baseline: ordinaryBaseline,
      commit: ordinaryDelta,
      retransmit: ordinaryCommit,
      commitWireType: 'gameStateDelta', ordinaryFullBytes, ordinaryDeltaBytes,
    },
    tournament: {
      baseline: publicEnvelope(before, true),
      commit: publicEnvelope(after, true),
      retransmit: publicEnvelope(after, true),
      commitWireType: 'gameState', ordinaryFullBytes, ordinaryDeltaBytes,
    },
  }
  if (release) {
    assertPublicSpectatorSnapshot(release, `${name} release`)
    const ordinaryRelease = publicEnvelope(release, false)
    const ordinaryReleaseDelta = createDelta(ordinaryCommit, ordinaryRelease)
    assert.deepEqual(applyDeltaOracle(ordinaryCommit, ordinaryReleaseDelta), ordinaryRelease,
      `${name}: release delta failed to reconstruct engine frame`)
    const releaseFullBytes = Buffer.byteLength(JSON.stringify(ordinaryRelease))
    const releaseDeltaBytes = Buffer.byteLength(JSON.stringify(ordinaryReleaseDelta))
    assert.ok(releaseDeltaBytes < releaseFullBytes * 0.9,
      `${name}: production codec would choose full instead of release delta (${releaseDeltaBytes}/${releaseFullBytes})`)
    scenario.ordinary.release = ordinaryReleaseDelta
    scenario.ordinary.retransmit = ordinaryRelease
    scenario.tournament.release = publicEnvelope(release, true)
    scenario.tournament.retransmit = scenario.tournament.release
    scenario.release = { revision: release.revision, releaseFullBytes, releaseDeltaBytes }
  }
  if (control) {
    const ordinaryControl = publicEnvelope(control.state, false)
    const tournamentControl = publicEnvelope(control.state, true)
    scenario.ordinary.control = createDelta(ordinaryCommit, ordinaryControl)
    scenario.tournament.control = tournamentControl
    scenario.control = { sequence: control.sequence, text: control.text, instanceId: control.instanceId,
      authority: 'synthetic-consumer-only-not-engine-proof' }
  }
  return scenario
}

const scenarios = {
  entry: buildScenario('entry', entryBefore, entryAfter, 'real-galahad', null, entryRelease,
    { playerIndex: entryEvent.playerIndex, zone: 'field', tapped: false }),
}
scenarios.entry.expectedCommitMotions = { state: 1, movement: 1 }
scenarios.entry.expectedCommitOrder = ['movement', 'state']
scenarios.entry.authoritySteps = [
  { label: 'baseline', revision: entryBefore.revision, zone: 'hand-private-count-only',
    tapped: null, handCount: entryBefore.players[entryEvent.playerIndex]?.handCount ?? null },
  { label: 'entry-commit', revision: entryAfter.revision, zone: 'field', tapped: false,
    eventSequence: entryEvent.sequence },
  { label: 'response-release', revision: entryRelease.revision, zone: 'field', tapped: true,
    orderingWatermark: Math.max(0, ...(entryRelease.recentEvents ?? []).map(event => event.sequence)) },
]
assert.equal(cardLocations(entryRelease).get('real-galahad')?.card?.tapped, true,
  'real Galahad release must retain its independent active-to-rested authority change')
if (restCase) {
  const control = createRestSameTextControl(restCase)
  scenarios.rest = buildScenario('rest', restCase.before, restCase.after,
    restCase.fact.instanceId, control, null,
    { playerIndex: restCase.playerIndex ?? restCase.event.playerIndex,
      zone: cardLocations(restCase.after).get(restCase.fact.instanceId)?.zone,
      tapped: Boolean(restCase.fact.toTapped) })
  scenarios.rest.expectedCommitMotions = { state: 1, movement: 0 }
  scenarios.rest.expectedCommitOrder = ['state']
  scenarios.rest.expectedControlMotions = { state: 1, movement: 0 }
}

const combatBaselineFrame = fixture.attack?.find(frame => frame.label === 'attack-response-pass-1')
const combatCommitFrame = fixture.attack?.find(frame => frame.label === 'attack-response-pass-2')
assert.ok(combatBaselineFrame?.spectator && combatCommitFrame?.spectator,
  'real attack defeat spectator frames missing')
assertPublicSpectatorSnapshot(combatBaselineFrame.spectator, 'combat defeat baseline')
assertPublicSpectatorSnapshot(combatCommitFrame.spectator, 'combat defeat commit')
const combatBaselineEnvelope = publicEnvelope(combatBaselineFrame.spectator, false)
const combatCommitEnvelope = publicEnvelope(combatCommitFrame.spectator, false)
const combatCommitDelta = createDelta(combatBaselineEnvelope, combatCommitEnvelope)
assert.deepEqual(applyDeltaOracle(combatBaselineEnvelope, combatCommitDelta), combatCommitEnvelope,
  'combat defeat delta failed to reconstruct the real engine frame')
const combatProbe = {
  baseline: combatBaselineEnvelope,
  commit: combatCommitDelta,
  retransmit: combatCommitEnvelope,
  commitWireType: 'gameStateDelta',
  instanceIds: ['real-attacker', 'rested-defender'],
  expected: { zone: 'graveyard', tapped: false },
  source: { chain: 'attack', before: combatBaselineFrame.label, after: combatCommitFrame.label },
}

function playerEnvelope(state) {
  return {
    type: 'gameState', spectating: false, gmEnabled: false, observerView: 'public',
    playerBadges: [], state: clone(state),
  }
}

const playerClickChain = fixture.readyResolved
assert.ok(Array.isArray(playerClickChain), 'readyResolved player chain missing')
const playerClickBaselineFrame = playerClickChain.find(frame => frame.label === 'baseline')
const playerClickCommitFrame = playerClickChain.find(frame => frame.label === 'declared-target-pending')
assert.ok(playerClickBaselineFrame?.player && playerClickCommitFrame?.player,
  'readyResolved real player baseline/target-pending frames missing')
const playerClickBaseline = clone(playerClickBaselineFrame.player)
const playerClickCommit = clone(playerClickCommitFrame.player)
const playerClickInstanceId = 'ankh-resolved-guard'
const playerClickPrompt = playerClickCommit.prompts?.find(prompt => prompt.promptId === 'prompt-1')
const playerClickBaselineCard = cardLocations(playerClickBaseline).get(playerClickInstanceId)?.card
const playerClickCommitCard = cardLocations(playerClickCommit).get(playerClickInstanceId)?.card
assert.ok(playerClickPrompt?.kind === 'active-target'
  && playerClickPrompt.validChoices?.includes(playerClickInstanceId)
  && playerClickPrompt.data?.choiceMode === 'board-target',
  'real player target prompt no longer names the field candidate')
assert.ok(playerClickBaselineCard && playerClickCommitCard?.tapped,
  'real player target candidate missing or no longer rested')
// This one consumer-safety probe deliberately varies only the prior public
// tapped bit. The committed prompt, candidate, bindings and target position are
// the stored engine player projection; this is not an engine-chain claim.
playerClickBaselineCard.tapped = false
playerClickBaseline.stateHash = `${playerClickBaseline.stateHash ?? 'state'}-consumer-active-baseline`
const playerClickFixture = {
  baseline: playerEnvelope(playerClickBaseline),
  commit: playerEnvelope(playerClickCommit),
  instanceId: playerClickInstanceId,
  promptId: playerClickPrompt.promptId,
  authority: 'synthetic-prior-public-state-plus-real-engine-committed-player-prompt',
  realChain: { chain: 'readyResolved', baseline: 'baseline', commit: 'declared-target-pending' },
}

let zoneProbeFixture = null
if (zoneFixture) {
  const fullChain = zoneFixture.chains?.graveEntryResolved
  const targetId = zoneFixture.target?.instanceId
  assert.ok(Array.isArray(fullChain) && targetId, 'real grave-entry resolved chain/target missing')
  const commitIndex = fullChain.findIndex(step => (step.owner?.recentEvents ?? []).some(event =>
    event.type === 'put' && event.cards?.some(card => card.instanceId === targetId)))
  assert.ok(commitIndex > 0, 'real grave-entry put fact missing')
  const movementEvent = fullChain[commitIndex].owner.recentEvents.find(event =>
    event.type === 'put' && event.cards?.some(card => card.instanceId === targetId))
  const beforeLocation = cardLocations(fullChain[0].owner).get(targetId)
  const afterLocation = cardLocations(fullChain[commitIndex].owner).get(targetId)
  const baselineGraveTop = fullChain[0].owner.players
    .find(player => player.graveyard?.some(card => card.instanceId === targetId))?.graveyard?.at(-1)
  assert.equal(beforeLocation?.zone, 'graveyard', 'real grave-entry target did not begin in graveyard')
  assert.equal(afterLocation?.zone, 'field', 'real grave-entry target did not settle on field')
  assert.ok(baselineGraveTop?.instanceId && baselineGraveTop.instanceId !== targetId,
    'grave-entry source guard requires a different public grave pile top')
  zoneProbeFixture = {
    target: clone(zoneFixture.target),
    baselineGraveTop: clone(baselineGraveTop),
    movementSequence: movementEvent.sequence,
    chain: fullChain.slice(0, commitIndex + 1).map(step => ({ label: step.label, state: clone(step.owner) })),
    sourcePath: zoneFixturePath,
    sourceSha256: zoneFixtureSha256,
    generatedFrom: zoneFixture.generatedFrom ?? null,
  }
}

const entrySource = `
import { createApp, h, ref } from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import * as net from '/src/l12/net.ts'
import { useLandscapeViewport } from '/src/l12/mobileViewport'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
import '/src/l12/motion.css'

const scenarios = ${JSON.stringify(scenarios)}
const combatProbe = ${JSON.stringify(combatProbe)}
const params = new URLSearchParams(location.search)
const scenarioName = params.get('scenario')
const mode = params.get('mode')
const selected = scenarioName === 'combat-exit' ? combatProbe : scenarios[scenarioName]?.[mode]
if (!selected) throw new Error('unknown scenario/mode '+scenarioName+'/'+mode)
window.__singleVisualBoot = {stage:'imports-complete'}

class FakeWebSocket {
  static CONNECTING = 0
  static OPEN = 1
  static CLOSING = 2
  static CLOSED = 3
  static instances = []
  readyState = FakeWebSocket.CONNECTING
  sent = []
  onopen = null
  onmessage = null
  onerror = null
  onclose = null
  constructor(url) {
    this.url = url
    // Vue does not proxy non-extensible host objects. Mirror native WebSocket
    // identity so production's stale-socket strict equality remains meaningful.
    Object.preventExtensions(this)
    FakeWebSocket.instances.push(this)
  }
  open() { this.readyState = FakeWebSocket.OPEN; this.onopen?.({}) }
  receive(payload) { this.onmessage?.({data:JSON.stringify(payload)}) }
  send(payload) { this.sent.push(String(payload)) }
  close(code=1000,reason='') { this.readyState=FakeWebSocket.CLOSED; this.onclose?.({code,reason}) }
}
globalThis.WebSocket = FakeWebSocket
useLandscapeViewport(ref(false))

const originalDrawImage=CanvasRenderingContext2D.prototype.drawImage
const originalImageDecode=HTMLImageElement.prototype.decode
let holdStateDecode=false
const armStateDecodeHold=()=>{
  holdStateDecode=true
  CanvasRenderingContext2D.prototype.drawImage=function(...args){
    if(holdStateDecode)throw new DOMException('diagnostic pixel capture hold','InvalidStateError')
    return originalDrawImage.apply(this,args)
  }
  HTMLImageElement.prototype.decode=function(){
    if(holdStateDecode)return new Promise(()=>{})
    return originalImageDecode?originalImageDecode.call(this):Promise.resolve()
  }
}
const releaseStateDecodeHold=()=>{
  holdStateDecode=false
  CanvasRenderingContext2D.prototype.drawImage=originalDrawImage
  if(originalImageDecode)HTMLImageElement.prototype.decode=originalImageDecode
}

const visible = element => {
  if (!(element instanceof HTMLElement)) return false
  const visual = element.matches('.zone-card-movement') ? element.querySelector('.moving-card') : element
  if (!(visual instanceof HTMLElement)) return false
  const style = getComputedStyle(visual)
  const rect = visual.getBoundingClientRect()
  return style.display !== 'none' && style.visibility !== 'hidden'
    && Number(style.opacity || 1) > 0 && rect.width > 0 && rect.height > 0
}
const angle = element => {
  if (!(element instanceof HTMLElement)) return null
  const transform = getComputedStyle(element).transform
  if (!transform || transform === 'none') return 0
  const match = transform.match(/^matrix\\(([^)]+)\\)$/)
  if (!match) return null
  const [a,b] = match[1].split(',').map(Number)
  return (Math.atan2(b,a)*180/Math.PI+360)%360
}
const frames = []
const events = []
const flags = {
  authorityVisibleBeforeGhost:false, authorityTransitionBeforeGhost:false,
  ghostVisible:false, blankFrame:false,
}
let tracing = false
let deliveredAt = null
let raf = 0
let visibleMotionStarted = false
let visualOwnerTakenOver = false
const seenMotionAnimations = new WeakSet()
const instanceId = scenarioName === 'combat-exit' ? combatProbe.instanceIds[0] : scenarios[scenarioName].instanceId
const expected = selected.expected ?? scenarios[scenarioName].expected
const describeAnimation = animation => ({
  type:animation.constructor?.name ?? 'Animation', playState:animation.playState,
  currentTime:Number(animation.currentTime ?? 0), transitionProperty:animation.transitionProperty ?? null,
})
const describeGhost = element => {
  const rect = element.getBoundingClientRect()
  const style = getComputedStyle(element)
  return {
    visible:visible(element), angle:angle(element), transform:style.transform, opacity:style.opacity,
    rect:{x:Number(rect.x.toFixed(3)),y:Number(rect.y.toFixed(3)),width:Number(rect.width.toFixed(3)),height:Number(rect.height.toFixed(3))},
    animations:element.getAnimations().map(describeAnimation),
  }
}
const describeCombatGhost = element => {
  const card = element.firstElementChild
  return {instanceId:element.dataset.cardInstanceId??null,...describeGhost(element),
    cardVisible:visible(card),cardVisibility:card instanceof HTMLElement?getComputedStyle(card).visibility:null,
    cardInlineVisibility:card instanceof HTMLElement?card.style.visibility:null}
}
const sample = now => {
  const authority = document.querySelector('[data-l12-game-stage] [data-card-instance-id="'+CSS.escape(instanceId)+'"]')
  const stateGhosts = [...document.querySelectorAll('.l12-card-state-transition-ghost')]
  const movementGhosts = [...document.querySelectorAll('.l12-zone-flight-ghost,.zone-card-movement')]
  const visibleStateGhosts = stateGhosts.filter(visible)
  const visibleMovementGhosts = movementGhosts.filter(visible)
  const combatGhosts = [...document.querySelectorAll('.l12-combat-defeat-ghost')]
  const sourceBacks = scenarioName === 'entry'
    ? [...document.querySelectorAll('[data-player-index="'+expected.playerIndex+'"][data-l12-zone="hand"] > .card-back,'
      +'[data-player-index="'+expected.playerIndex+'"] [data-l12-zone="hand"] > .card-back')]
    : []
  for (const [kind, ghosts] of [['state', stateGhosts], ['movement', movementGhosts]]) {
    for (const ghost of ghosts) for (const motion of ghost.getAnimations()) {
      if (motion.playState === 'idle' || motion.currentTime == null || seenMotionAnimations.has(motion)) continue
      seenMotionAnimations.add(motion)
      events.push({at:performance.now(),type:kind+'-motion-started',key:ghost.dataset.visualTransitionKey ?? ghost.dataset.movementKey ?? null})
    }
  }
  const authorityAnimations = authority instanceof HTMLElement
    ? authority.getAnimations().map(describeAnimation) : []
  const movementLedger = document.querySelector('[data-ui-contract="zone-movement-reservation-ledger"]')
  const frame = {
    index:frames.length, at:Number(now.toFixed(3)), sinceDelivery:deliveredAt == null ? null : Number((now-deliveredAt).toFixed(3)),
    revision:net.l12State.game?.revision ?? null,
    authority:{exists:authority instanceof HTMLElement,visible:visible(authority),angle:angle(authority),
      tapped:authority instanceof HTMLElement && authority.classList.contains('tapped'),
      visibility:authority instanceof HTMLElement ? getComputedStyle(authority).visibility : null,
      opacity:authority instanceof HTMLElement ? getComputedStyle(authority).opacity : null,
      pointerEvents:authority instanceof HTMLElement ? getComputedStyle(authority).pointerEvents : null,
      zone:authority instanceof HTMLElement ? authority.closest('[data-l12-zone]')?.getAttribute('data-l12-zone') ?? null : null,
      animations:authorityAnimations},
    sourceBacks:{count:sourceBacks.length,visibleCount:sourceBacks.filter(visible).length,
      rects:sourceBacks.filter(visible).map(node=>{const rect=node.getBoundingClientRect();return {
        x:Number(rect.x.toFixed(3)),y:Number(rect.y.toFixed(3)),width:Number(rect.width.toFixed(3)),height:Number(rect.height.toFixed(3))}})},
    stateGhosts:stateGhosts.map(element=>({key:element.dataset.visualTransitionKey,
      from:element.dataset.stateFrom,to:element.dataset.stateTo,...describeGhost(element)})),
    movementGhosts:movementGhosts.map(element=>({key:element.dataset.movementKey,
      from:element.dataset.movementFrom,to:element.dataset.movementTo,instanceId:element.dataset.movementInstanceId,
      ...describeGhost(element)})),
    combatGhosts:combatGhosts.map(describeCombatGhost),
    movementReservationCount: movementLedger?.getAttribute('data-movement-reservation-count') ?? null,
  }
  frames.push(frame)
  const visibleGhostCount = visibleStateGhosts.length + visibleMovementGhosts.length
  const authorityIsFinal = frame.authority.zone === expected.zone && frame.authority.tapped === expected.tapped
  if (tracing && !visibleMotionStarted && visibleGhostCount === 0 && frame.authority.visible && authorityIsFinal) {
    if (scenarioName === 'entry') flags.authorityVisibleBeforeGhost = true
    if (scenarioName === 'rest')
      flags.authorityTransitionBeforeGhost = true
  }
  if (visibleGhostCount) {
    flags.ghostVisible = true
    visibleMotionStarted = true
    visualOwnerTakenOver = true
  }
  const oldEntryVisual = scenarioName === 'entry' && !visualOwnerTakenOver && frame.sourceBacks.visibleCount > 0
  if (tracing && !frame.authority.visible && visibleGhostCount === 0 && !oldEntryVisual)
    flags.blankFrame = true
  raf = requestAnimationFrame(sample)
}

new MutationObserver(records => {
  for (const record of records) for (const node of record.addedNodes) {
    if (!(node instanceof HTMLElement)) continue
    for (const element of [node,...node.querySelectorAll('*')]) {
      if (!(element instanceof HTMLElement)) continue
      if (element.matches('.l12-card-state-transition-ghost')) events.push({at:performance.now(),type:'state-ghost-created',key:element.dataset.visualTransitionKey})
      if (element.matches('.l12-zone-flight-ghost,.zone-card-movement')) events.push({at:performance.now(),type:'movement-ghost-created',key:element.dataset.movementKey,instanceId:element.dataset.movementInstanceId})
      if (element.matches('.l12-combat-defeat-ghost')) events.push({at:performance.now(),type:'combat-defeat-ghost-created',instanceId:element.dataset.cardInstanceId})
    }
  }
}).observe(document.documentElement,{subtree:true,childList:true})
document.addEventListener('transitionrun', event => {
  const target = event.target
  if (target instanceof HTMLElement && target.dataset.cardInstanceId === instanceId)
    events.push({at:performance.now(),type:'authority-transitionrun',property:event.propertyName})
}, true)
document.addEventListener('transitionstart', event => {
  const target = event.target
  if (target instanceof HTMLElement && target.dataset.cardInstanceId === instanceId)
    events.push({at:performance.now(),type:'authority-transitionstart',property:event.propertyName})
}, true)

createApp({setup(){return()=>net.l12State.game
  ? h(GameBoard,{game:net.l12State.game,readOnly:true,spectatorLiveView:true})
  : h('div',{'data-probe-waiting':'true'},'waiting')}}).mount('#app')

localStorage.setItem('l12-auth-token','spectator-single-owner-local-only')
window.__singleVisualBoot.stage = 'before-connect'
const connected = net.connect()
await Promise.resolve()
const socket = FakeWebSocket.instances[0]
if (!socket) throw new Error('production net consumer did not create WebSocket')
window.__singleVisualBoot.stage = 'socket-created'
socket.open()
socket.receive({type:'session',sessionId:'spectator-red-session',accountId:'spectator-red',name:'观战诊断',connectionGeneration:19,
  capabilities:{requestIds:true,deltaGameState:true,eventWindowDeltaV1:true,presenceDeltaV1:true}})
socket.receive({type:'roomState',roomCode:selected.baseline.state.roomCode,yourPlayerIndex:null,started:true,players:[]})
socket.receive(selected.baseline)
socket.receive({type:'recoveryComplete',connectionGeneration:19,roomCode:selected.baseline.state.roomCode,
  matchId:selected.baseline.state.matchId,recoveryRevision:selected.baseline.state.revision})
window.__singleVisualBoot.stage = 'recovery-sent'
window.__singleVisualBoot.netState = ()=>({status:net.l12State.status,recoveryPhase:net.l12State.recoveryPhase,
  notice:net.l12State.notice,roomCode:net.l12State.room?.roomCode,matchId:net.l12State.game?.matchId,
  revision:net.l12State.game?.revision})
await connected
window.__singleVisualBoot.stage = 'connected'
requestAnimationFrame(sample)

const counts = () => ({
  state:events.filter(item=>item.type==='state-ghost-created').length,
  movement:events.filter(item=>item.type==='movement-ghost-created').length,
})
const starts = () => ({
  state:events.filter(item=>item.type==='state-motion-started').length,
  movement:events.filter(item=>item.type==='movement-motion-started').length,
})
const cloneDecodeProbe = () => {
  const source = document.querySelector('[data-l12-game-stage] [data-card-instance-id="'+CSS.escape(instanceId)+'"]')
  if (!(source instanceof HTMLElement)) return {sourceExists:false}
  const images = root => [...root.querySelectorAll('img')].map(image => ({
    complete:image.complete,naturalWidth:image.naturalWidth,currentSrc:image.currentSrc || image.src,
  }))
  const clone = source.cloneNode(true)
  const detached = images(clone)
  const shell = document.createElement('div')
  Object.assign(shell.style,{position:'fixed',left:'-10000px',top:'-10000px',visibility:'hidden',pointerEvents:'none'})
  shell.appendChild(clone)
  document.body.appendChild(shell)
  const result = {sourceExists:true,source:images(source),detached,attached:images(clone)}
  shell.remove()
  return result
}
const deliver = kind => {
  if (kind === 'commit') { tracing=true; deliveredAt=performance.now() }
  if (kind === 'release') events.push({at:performance.now(),type:'real-chain-release',decodeProbe:cloneDecodeProbe()})
  socket.receive(selected[kind])
}
const switchedMatchId=selected.baseline.state.matchId+'-decode-cancel'
const switchedEnvelope=message=>{
  const next=structuredClone(message)
  next.state.matchId=switchedMatchId
  next.state.roomCode=(next.state.roomCode||'ROOM')+'-DC'
  return next
}
window.__singleVisualOwner = {
  ready:true,scenarioName,mode,instanceId,wireType:selected.commitWireType,
  deliver,counts,starts,flags,frames,events,
  armStateDecodeHold,
  releaseStateDecodeHold,
  switchMatch:()=>socket.receive(switchedEnvelope(selected.baseline)),
  deliverSwitchCommit:()=>socket.receive(selected.commit),
  deliverSwitchRetransmit:()=>socket.receive(switchedEnvelope(selected.retransmit)),
  combat:()=>({ghosts:[...document.querySelectorAll('.l12-combat-defeat-ghost')].map(describeCombatGhost),
    created:events.filter(item=>item.type==='combat-defeat-ghost-created').map(item=>item.instanceId)}),
  state:()=>({revision:net.l12State.game?.revision,matchId:net.l12State.game?.matchId,status:net.l12State.status,spectating:net.l12State.spectating,
    observerView:net.l12State.observerView,roomCode:net.l12State.room?.roomCode}),
  stop:()=>{releaseStateDecodeHold();tracing=false;cancelAnimationFrame(raf);net.disconnect();return {frames:structuredClone(frames),events:structuredClone(events),flags:{...flags},counts:counts(),starts:starts()}},
}
`

const playerClickSource = `
import { createApp, h, ref } from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import * as net from '/src/l12/net.ts'
import { useLandscapeViewport } from '/src/l12/mobileViewport'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
import '/src/l12/motion.css'

const fixture = ${JSON.stringify(playerClickFixture)}
window.__singleVisualBoot = {stage:'imports-complete'}
class FakeWebSocket {
  static CONNECTING=0; static OPEN=1; static CLOSING=2; static CLOSED=3; static instances=[]
  readyState=FakeWebSocket.CONNECTING; sent=[]; onopen=null; onmessage=null; onerror=null; onclose=null
  constructor(url){this.url=url;Object.preventExtensions(this);FakeWebSocket.instances.push(this)}
  open(){this.readyState=FakeWebSocket.OPEN;this.onopen?.({})}
  receive(payload){this.onmessage?.({data:JSON.stringify(payload)})}
  send(payload){this.sent.push(String(payload))}
  close(code=1000,reason=''){this.readyState=FakeWebSocket.CLOSED;this.onclose?.({code,reason})}
}
globalThis.WebSocket=FakeWebSocket
const mobileLayout=new URLSearchParams(location.search).get('mobile')==='1'
const visible=element=>{
  if(!(element instanceof HTMLElement))return false
  const style=getComputedStyle(element),rect=element.getBoundingClientRect()
  return style.display!=='none'&&style.visibility!=='hidden'&&Number(style.opacity||1)>0&&rect.width>0&&rect.height>0
}
createApp({setup(){
  useLandscapeViewport(ref(mobileLayout))
  return()=>net.l12State.game
    ? h(GameBoard,{game:net.l12State.game,readOnly:false,spectatorLiveView:false})
    : h('div',{'data-probe-waiting':'true'},'waiting')
}}).mount('#player-root')
localStorage.setItem('l12-auth-token','player-native-click-local-only')
const connected=net.connect();await Promise.resolve();const socket=FakeWebSocket.instances[0]
if(!socket)throw new Error('production net consumer did not create WebSocket')
socket.open()
socket.receive({type:'session',sessionId:'player-native-click-session',accountId:'player-native-click',name:'原生点击诊断',connectionGeneration:19,
  capabilities:{requestIds:true,deltaGameState:true,eventWindowDeltaV1:true,presenceDeltaV1:true}})
socket.receive({type:'roomState',roomCode:fixture.baseline.state.roomCode,yourPlayerIndex:0,started:true,players:[]})
socket.receive(fixture.baseline)
socket.receive({type:'recoveryComplete',connectionGeneration:19,roomCode:fixture.baseline.state.roomCode,
  matchId:fixture.baseline.state.matchId,recoveryRevision:fixture.baseline.state.revision})
await connected
const snapshot=()=>{
  const authority=document.querySelector('[data-l12-game-stage] [data-card-instance-id="'+CSS.escape(fixture.instanceId)+'"]')
  const slot=authority?.closest('.formation-slot')
  const holder=[...document.querySelectorAll('.l12-card-state-transition-ghost')]
    .find(node=>node.dataset.visualTransitionKey?.includes(fixture.instanceId))
  const rect=slot instanceof HTMLElement?slot.getBoundingClientRect():null
  const x=rect?rect.left+rect.width/2:null,y=rect?rect.top+rect.height/2:null
  const hit=x!==null&&y!==null?document.elementFromPoint(x,y):null
  const confirm=document.querySelector('.inline-prompt-controls button.primary')
  return {
    revision:net.l12State.game?.revision??null,spectating:net.l12State.spectating,
    promptVisible:visible(document.querySelector('.inline-prompt-controls')),
    promptOverlayVisible:visible(document.querySelector('.l12-prompt-overlay')),
    minimizeVisible:visible(document.querySelector('.inline-prompt-controls .board-control-minimize')),
    restoreVisible:visible(document.querySelector('.board-control-restore')),
    authority:{exists:authority instanceof HTMLElement,visible:visible(authority),
      visibility:authority instanceof HTMLElement?getComputedStyle(authority).visibility:null,
      tapped:authority instanceof HTMLElement&&authority.classList.contains('tapped'),
      pointerEvents:authority instanceof HTMLElement?getComputedStyle(authority).pointerEvents:null},
    holder:{exists:holder instanceof HTMLElement,visible:visible(holder),pointerEvents:holder instanceof HTMLElement?getComputedStyle(holder).pointerEvents:null},
    slot:{exists:slot instanceof HTMLElement,targetable:slot instanceof HTMLElement&&slot.classList.contains('targetable'),
      selected:slot instanceof HTMLElement&&slot.classList.contains('prompt-selected'),rect:rect?.toJSON()??null},
    hit:{tag:hit?.tagName??null,className:hit instanceof HTMLElement?hit.className:null,
      withinSlot:slot instanceof HTMLElement&&hit instanceof Element&&slot.contains(hit),
      withinGhost:hit instanceof Element&&Boolean(hit.closest('.l12-card-state-transition-ghost'))},
    confirm:{exists:confirm instanceof HTMLButtonElement,disabled:confirm instanceof HTMLButtonElement?confirm.disabled:null},
    sent:socket.sent.map(raw=>{try{return JSON.parse(raw)}catch{return {raw}}}),
  }
}
window.__playerNativeClick={ready:true,fixture,deliver:()=>socket.receive(fixture.commit),snapshot,
  stop:()=>{net.disconnect();return snapshot()}}
`

const zoneProbeSource = `
import { createApp, h, reactive, ref } from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import { useLandscapeViewport } from '/src/l12/mobileViewport'
import { snapshotDecodedAuthoritativeCardVisual } from '/src/l12/game/authoritativeCardVisualSnapshot.ts'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
import '/src/l12/motion.css'

const fixture=${JSON.stringify(zoneProbeFixture)}
const game=reactive(structuredClone(fixture.chain[0].state))
useLandscapeViewport(ref(false))
const events=[]
const seenAnimations=new WeakSet()
const originalAnimate=Element.prototype.animate
Element.prototype.animate=function(keyframes,options){
  const animation=originalAnimate.call(this,keyframes,options)
  if(this instanceof HTMLElement&&(this.matches('.l12-zone-flight-ghost')||this.closest('.l12-zone-flight-ghost'))){
    seenAnimations.add(animation)
    events.push({at:performance.now(),type:'movement-motion-started',key:this.dataset.movementKey??this.closest('.l12-zone-flight-ghost')?.dataset.movementKey??null})
  }
  return animation
}
new MutationObserver(records=>{
  for(const record of records)for(const node of record.addedNodes){
    if(!(node instanceof HTMLElement))continue
    for(const element of [node,...node.querySelectorAll('.l12-zone-flight-ghost')])
      if(element instanceof HTMLElement&&element.matches('.l12-zone-flight-ghost'))
        events.push({at:performance.now(),type:'movement-holder-created',key:element.dataset.movementKey,
          instanceId:element.dataset.movementInstanceId,initialVisual:{className:element.firstElementChild?.className??null,
            canvasCount:element.querySelectorAll('canvas[data-l12-pixel-snapshot="true"]').length,
            imageAlts:[...element.querySelectorAll('img')].map(image=>image.alt),text:element.textContent??''}})
  }
}).observe(document.documentElement,{subtree:true,childList:true})
const visible=element=>{
  if(!(element instanceof HTMLElement))return false
  const style=getComputedStyle(element),rect=element.getBoundingClientRect()
  return style.display!=='none'&&style.visibility!=='hidden'&&Number(style.opacity||1)>0&&rect.width>0&&rect.height>0
}
const sourceProbe=()=>{
  const source=document.querySelector('.graveyard-overlay [data-card-instance-id="'+CSS.escape(fixture.target.instanceId)+'"]')
  if(!(source instanceof HTMLElement))return {sourceExists:false}
  const images=root=>[...root.querySelectorAll('img')].map(image=>({complete:image.complete,naturalWidth:image.naturalWidth,naturalHeight:image.naturalHeight,
    offsetWidth:image.offsetWidth,offsetHeight:image.offsetHeight}))
  const clone=source.cloneNode(true)
  const detached=images(clone)
  const pixelSnapshot=snapshotDecodedAuthoritativeCardVisual(source,clone)
  const pixelCanvases=[...clone.querySelectorAll('canvas[data-l12-pixel-snapshot="true"]')]
    .map(canvas=>({width:canvas.width,height:canvas.height}))
  const shell=document.createElement('div')
  Object.assign(shell.style,{position:'fixed',left:'-10000px',top:'-10000px',visibility:'hidden',pointerEvents:'none'})
  shell.appendChild(clone);document.body.appendChild(shell)
  const attached=images(clone);shell.remove()
  return {sourceExists:true,stageContains:document.querySelector('[data-l12-game-stage]')?.contains(source)??false,
    closestZone:source.closest('[data-l12-zone]')?.getAttribute('data-l12-zone')??null,
    source:images(source),detached,pixelSnapshot,pixelCanvases,attached}
}
const snapshot=()=>{
  const authority=document.querySelector('[data-l12-game-stage] [data-card-instance-id="'+CSS.escape(fixture.target.instanceId)+'"]')
  const holders=[...document.querySelectorAll('.l12-zone-flight-ghost')].filter(node=>node.dataset.movementInstanceId===fixture.target.instanceId)
  return {revision:game.revision,sourceInGrave:Boolean(document.querySelector('.graveyard-overlay [data-card-instance-id="'+CSS.escape(fixture.target.instanceId)+'"]')),
    authority:{exists:authority instanceof HTMLElement,visible:visible(authority),zone:authority?.closest('[data-l12-zone]')?.getAttribute('data-l12-zone')??null,
      visibility:authority instanceof HTMLElement?getComputedStyle(authority).visibility:null},
    holders:holders.map(holder=>({key:holder.dataset.movementKey,visible:visible(holder),className:holder.firstElementChild?.className??null,
      canvasCount:holder.querySelectorAll('canvas[data-l12-pixel-snapshot="true"]').length,
      images:[...holder.querySelectorAll('img')].map(image=>({complete:image.complete,naturalWidth:image.naturalWidth,alt:image.alt,src:image.currentSrc||image.src}))})),
    created:events.filter(event=>event.type==='movement-holder-created'&&event.instanceId===fixture.target.instanceId).length,
    starts:events.filter(event=>event.type==='movement-motion-started'&&String(event.key).startsWith(fixture.movementSequence+':')).length,
    events:structuredClone(events)}
}
createApp({render:()=>h(GameBoard,{game,readOnly:true,spectatorLiveView:false})}).mount('#app')
let consumerEpoch=0
const applyZoneFrame=index=>{
  const state=structuredClone(fixture.chain[index].state)
  if(consumerEpoch)state.matchId+='-consumer-reset-'+consumerEpoch
  Object.assign(game,state)
}
window.__zoneDecodedSource={ready:true,fixture,sourceProbe,snapshot,
  apply:applyZoneFrame,
  restart:()=>{consumerEpoch++;applyZoneFrame(0)},
  stop:()=>snapshot()}
`

const html = '<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module" src="/__spectator_single_owner.js"></script></body></html>'
const playerClickHtml = '<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"><div id="l12-landscape-teleports"></div><div id="player-root" class="l12-landscape-surface" data-l12-landscape-canvas></div></div><script type="module" src="/__player_native_click.js"></script></body></html>'
const zoneProbeHtml = '<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module" src="/__zone_decoded_source.js"></script></body></html>'
const localFace = '<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#172533"/><path d="M20 20h210v310H20z" fill="none" stroke="#d7bb71" stroke-width="8"/><text x="125" y="180" text-anchor="middle" fill="#f5ebcf" font-size="24">L12 authority</text></svg>'
const plugin = {
  name: 'spectator-single-visual-owner-red',
  enforce: 'pre',
  resolveId(id) {
    if (id === '/__spectator_single_owner.js' || id === '/__player_native_click.js'
      || id === '/__zone_decoded_source.js') return id
  },
  load(id) {
    if (id === '/__spectator_single_owner.js') return entrySource
    if (id === '/__player_native_click.js') return playerClickSource
    if (id === '/__zone_decoded_source.js') return zoneProbeSource
  },
  configureServer(server) {
    server.middlewares.use((request, response, next) => {
      const requestUrl = request.url ?? ''
      const requestPath = decodeURI(requestUrl.split('?')[0])
      if (requestPath === '/__spectator-single-owner') {
        response.setHeader('Content-Type', 'text/html; charset=utf-8')
        response.end(html)
        return
      }
      if (requestPath === '/__player-native-click') {
        response.setHeader('Content-Type', 'text/html; charset=utf-8')
        response.end(playerClickHtml)
        return
      }
      if (requestPath === '/__zone-decoded-source') {
        response.setHeader('Content-Type', 'text/html; charset=utf-8')
        response.end(zoneProbeHtml)
        return
      }
      if (!/[?&](?:import|url)(?:[=&]|$)/.test(requestUrl)
        && (/\.(?:png|jpe?g|webp)$/i.test(requestPath) || requestPath.includes('/cards/faces/'))) {
        response.setHeader('Content-Type', 'image/svg+xml; charset=utf-8')
        response.setHeader('Cache-Control', 'no-store')
        response.end(localFace)
        return
      }
      next()
    })
  },
}

const report = {
  schema: 1,
  status: 'running',
  expectation, requestedProbe: requestedProbe || null,
  claim: requestedProbe === 'player-native-click'
    ? 'Current local candidate: native hit-tested player target selection over a prepared static owner, using a stored real engine player prompt plus an explicitly synthetic prior public tapped bit; not an engine-chain or deployed-build claim.'
    : requestedProbe === 'player-inline-minimize'
      ? 'Current local candidate: a real stored player board-target prompt remains routed to its inline control through native mobile minimize/restore and never reappears as a generic PromptOverlay; not a deployed-build claim.'
    : requestedProbe === 'zone-grave-decoded-source'
      ? 'Current local candidate against a stored real graveyard-to-field engine chain, with its already-decoded public grave card opened in the real GraveyardOverlay before authority delivery; not a deployed-build claim.'
    : requestedProbe === 'zone-grave-pile-fallback'
      ? 'Current local candidate against the same stored real graveyard-to-field engine chain with the overlay closed: the unrelated public pile top contributes geometry only and never pixels to the target owner; not a deployed-build claim.'
    : requestedProbe === 'combat-hidden-clone'
      ? 'Current local candidate: stored real spectator attack resolution through the production ordinary delta consumer keeps both confirmed-defeat clones visibly painted; not a deployed-build claim.'
    : requestedProbe === 'state-decode-cancel'
      ? 'Current local candidate: a deliberately held clone decode is cancelled by a real match boundary, leaving no holder or visibility lease and allowing the next real authority transition to run; not a deployed-build claim.'
    : expectation === 'red'
    ? 'Formal-19 disk bytes against stored real spectator authority frames.'
    : 'Current local candidate against the same formal-19 baseline and stored real spectator authority frames; not a deployed-build claim.',
  headCommit, formal19Commit, sourceRoot: repositoryRoot,
  fixture: { path: fixturePath, sha256: fixtureSha256, containerSha256: fixtureContainerSha256,
    generatedFrom: fixture.generatedFrom ?? null, schema: fixture.schema ?? null },
  stateFixture: stateFixturePath ? { path: stateFixturePath, sha256: stateFixtureSha256,
    containerSha256: storedStateFixture ? sha256(storedStateFixture) : null,
    generatedFrom: stateFixture?.generatedFrom ?? null, schema: stateFixture?.schema ?? null } : null,
  fixturePreconditions,
  requestedStateCase: requestedStateCase || null,
  requestedStateInstance: requestedStateInstance || null,
  sourceBindings,
  consumerPaths: {
    ordinary: 'Fake WebSocket -> production net.ts gameStateDelta/eventWindow-v1 decoder -> reactive GameBoard',
    tournament: 'Fake WebSocket -> production net.ts full gameState consumer -> reactive GameBoard',
    playerNativeClick: 'Fake WebSocket -> production net.ts full player gameState -> reactive GameBoard -> native pointer hit-test -> resolvePrompt gameAction',
  },
  playerClickFixture: ['player-native-click', 'player-inline-minimize'].includes(requestedProbe) ? {
    authority: playerClickFixture.authority,
    realChain: playerClickFixture.realChain,
    instanceId: playerClickFixture.instanceId,
    promptId: playerClickFixture.promptId,
    baselineRevision: playerClickFixture.baseline.state.revision,
    commitRevision: playerClickFixture.commit.state.revision,
  } : null,
  zoneProbeFixture: ['zone-grave-decoded-source', 'zone-grave-pile-fallback'].includes(requestedProbe) ? {
    path: zoneProbeFixture.sourcePath, sha256: zoneProbeFixture.sourceSha256,
    generatedFrom: zoneProbeFixture.generatedFrom,
    target: zoneProbeFixture.target, movementSequence: zoneProbeFixture.movementSequence,
    baselineGraveTop: zoneProbeFixture.baselineGraveTop,
    steps: zoneProbeFixture.chain.map(step => ({ label: step.label, revision: step.state.revision })),
  } : null,
  combatProbeFixture: requestedProbe === 'combat-hidden-clone' ? {
    ...combatProbe.source, instanceIds: combatProbe.instanceIds,
    baselineRevision: combatProbe.baseline.state.revision,
    commitRevision: combatProbe.retransmit.state.revision,
    commitWireType: combatProbe.commitWireType,
  } : null,
  results: [],
  redFailures: [],
}

const server = await createServer({
  root, cacheDir: path.join(output, 'vite-cache'), plugins: [plugin],
  server: { host: '127.0.0.1', port: 0 }, logLevel: 'error',
})
let browser
let activeProbe = null
let activePage = null
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  if (requestedProbe === 'player-native-click') {
    assert.equal(expectation, 'green', 'player-native-click is a GREEN consumer regression only')
    const context = await browser.newContext({ viewport: { width: 1366, height: 768 } })
    const page = await context.newPage()
    activePage = page
    const errors = []
    activeProbe = { probe: requestedProbe, errors }
    page.on('pageerror', error => errors.push(error.message))
    page.on('requestfailed', request => errors.push(`request failed: ${request.url()} (${request.failure()?.errorText ?? 'unknown'})`))
    page.on('response', response => {
      if (response.status() >= 400) errors.push(`http ${response.status()}: ${response.url()}`)
    })
    await page.route('**/*', async route => {
      const url = new URL(route.request().url())
      if (url.hostname === '127.0.0.1') await route.continue()
      else if (route.request().resourceType() === 'image')
        await route.fulfill({ status: 200, contentType: 'image/svg+xml', body: localFace })
      else await route.abort('blockedbyclient')
    })
    await page.goto(`http://127.0.0.1:${port}/__player-native-click`, { waitUntil: 'networkidle' })
    await page.waitForFunction(() => window.__playerNativeClick?.ready, null, { timeout: 8000 })
    await page.waitForSelector('[data-l12-game-stage]')
    await page.waitForTimeout(80)
    const baseline = await page.evaluate(() => window.__playerNativeClick.snapshot())
    assert.equal(baseline.spectating, false, 'player probe unexpectedly entered spectator mode')
    assert.equal(baseline.authority.tapped, false, 'synthetic prior public state did not render active')
    assert.equal(baseline.promptVisible, false, 'baseline unexpectedly rendered a target prompt')
    await page.evaluate(() => window.__playerNativeClick.deliver())
    await page.waitForFunction(() => {
      const snapshot = window.__playerNativeClick.snapshot()
      return snapshot.promptVisible && snapshot.authority.exists && !snapshot.authority.visible
        && snapshot.holder.visible && snapshot.slot.targetable
    }, null, { timeout: 3000 })
    const beforeClick = await page.evaluate(() => window.__playerNativeClick.snapshot())
    assert.equal(beforeClick.authority.visibility, 'hidden', 'prepared owner did not cover committed authority state')
    assert.equal(beforeClick.holder.pointerEvents, 'none', 'prepared holder captured player pointer events')
    assert.equal(beforeClick.hit.withinSlot, true, 'native hit test did not reach the authoritative formation slot')
    assert.equal(beforeClick.hit.withinGhost, false, 'native hit test landed on the presentation ghost')
    assert.equal(beforeClick.confirm.disabled, true, 'target confirmation was enabled before native selection')
    const beforeScreenshot = path.join(output, 'player-native-click-before.png')
    await page.screenshot({ path: beforeScreenshot })
    const clickPoint = await page.evaluate(id => {
      const authority = document.querySelector('[data-l12-game-stage] [data-card-instance-id="'+CSS.escape(id)+'"]')
      const rect = authority?.closest('.formation-slot')?.getBoundingClientRect()
      if (!rect) return null
      return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 }
    }, playerClickInstanceId)
    assert.ok(clickPoint, 'target slot click point missing')
    await page.mouse.click(clickPoint.x, clickPoint.y)
    await page.waitForFunction(() => window.__playerNativeClick.snapshot().slot.selected, null, { timeout: 1000 })
    const afterCandidateClick = await page.evaluate(() => window.__playerNativeClick.snapshot())
    assert.equal(afterCandidateClick.confirm.disabled, false, 'native candidate click did not enable confirmation')
    await page.locator('.inline-prompt-controls button.primary').click()
    await page.waitForFunction(() => window.__playerNativeClick.snapshot().sent
      .some(item => item.type === 'gameAction'), null, { timeout: 1000 })
    const afterConfirm = await page.evaluate(() => window.__playerNativeClick.snapshot())
    const actions = afterConfirm.sent.filter(item => item.type === 'gameAction')
    assert.equal(actions.length, 1, 'native target selection emitted an unexpected number of game actions')
    const action = actions[0]
    assert.equal(action.command?.type, 'resolvePrompt', 'native target selection emitted the wrong command')
    assert.equal(action.command?.promptId, playerClickPrompt.promptId, 'resolved prompt identity changed')
    assert.deepEqual(action.command?.cardInstanceIds, [playerClickInstanceId], 'selected target identity changed')
    for (const key of ['activationId', 'sourceInstanceId', 'sourceCardId', 'step', 'createdRevision', 'controller'])
      assert.deepEqual(action.command?.[key], playerClickPrompt[key], `prompt binding ${key} changed`)
    const afterScreenshot = path.join(output, 'player-native-click-selected.png')
    await page.screenshot({ path: afterScreenshot })
    report.results.push({
      probe: requestedProbe, errors, baseline, beforeClick, afterCandidateClick, afterConfirm,
      action, nativePointer: { clickPoint, forced: false, hitTestWithinSlot: beforeClick.hit.withinSlot },
      screenshots: [beforeScreenshot, afterScreenshot].map(file => ({
        path: file, sha256: sha256(fs.readFileSync(file)), bytes: fs.statSync(file).size,
      })),
    })
    assert.deepEqual(errors, [], 'player native-click browser page error')
    await page.evaluate(() => window.__playerNativeClick.stop())
    await context.close()
  }
  if (requestedProbe === 'player-inline-minimize') {
    assert.equal(expectation, 'green', 'player-inline-minimize is a GREEN consumer regression only')
    const context = await browser.newContext({ viewport: { width: 390, height: 844 } })
    const page = await context.newPage()
    activePage = page
    const errors = []
    activeProbe = { probe: requestedProbe, errors }
    page.on('pageerror', error => errors.push(error.message))
    page.on('requestfailed', request => errors.push(`request failed: ${request.url()} (${request.failure()?.errorText ?? 'unknown'})`))
    page.on('response', response => {
      if (response.status() >= 400) errors.push(`http ${response.status()}: ${response.url()}`)
    })
    await page.route('**/*', async route => {
      const url = new URL(route.request().url())
      if (url.hostname === '127.0.0.1') await route.continue()
      else if (route.request().resourceType() === 'image')
        await route.fulfill({ status: 200, contentType: 'image/svg+xml', body: localFace })
      else await route.abort('blockedbyclient')
    })
    await page.goto(`http://127.0.0.1:${port}/__player-native-click?mobile=1`, { waitUntil: 'networkidle' })
    await page.waitForFunction(() => window.__playerNativeClick?.ready, null, { timeout: 8000 })
    await page.waitForSelector('[data-l12-game-stage]')
    const baseline = await page.evaluate(() => window.__playerNativeClick.snapshot())
    assert.equal(baseline.promptOverlayVisible, false, 'baseline unexpectedly rendered a generic prompt modal')
    await page.evaluate(() => window.__playerNativeClick.deliver())
    await page.waitForFunction(() => {
      const snapshot = window.__playerNativeClick.snapshot()
      return snapshot.promptVisible && snapshot.minimizeVisible && !snapshot.promptOverlayVisible
    }, null, { timeout: 3000 })
    const expanded = await page.evaluate(() => window.__playerNativeClick.snapshot())
    const expandedScreenshot = path.join(output, 'player-inline-expanded.png')
    await page.screenshot({ path: expandedScreenshot })
    await page.locator('.inline-prompt-controls .board-control-minimize').click()
    await page.waitForFunction(() => {
      const snapshot = window.__playerNativeClick.snapshot()
      return !snapshot.promptVisible && snapshot.restoreVisible && !snapshot.promptOverlayVisible
    }, null, { timeout: 2000 })
    await page.waitForFunction(() => {
      const snapshot = window.__playerNativeClick.snapshot()
      return !snapshot.holder.exists && snapshot.authority.visible && snapshot.authority.tapped
    }, null, { timeout: 5000 })
    const minimized = await page.evaluate(() => window.__playerNativeClick.snapshot())
    const minimizedScreenshot = path.join(output, 'player-inline-minimized.png')
    await page.screenshot({ path: minimizedScreenshot })
    await page.locator('.board-control-restore').click()
    await page.waitForFunction(() => {
      const snapshot = window.__playerNativeClick.snapshot()
      return snapshot.promptVisible && snapshot.minimizeVisible && !snapshot.promptOverlayVisible
    }, null, { timeout: 2000 })
    const restored = await page.evaluate(() => window.__playerNativeClick.snapshot())
    assert.equal(restored.promptOverlayVisible, false,
      'inline board prompt reappeared as a generic PromptOverlay after restore')
    assert.deepEqual(restored.sent.filter(item => item.type === 'gameAction'), [],
      'minimize/restore emitted a game action')
    assert.deepEqual(errors, [], 'player inline-minimize browser page error')
    const restoredScreenshot = path.join(output, 'player-inline-restored.png')
    await page.screenshot({ path: restoredScreenshot })
    report.results.push({ probe: requestedProbe, errors, baseline, expanded, minimized, restored,
      nativeClicks: { minimizeForced: false, restoreForced: false },
      screenshots: [expandedScreenshot, minimizedScreenshot, restoredScreenshot].map(file => ({
        path: file, sha256: sha256(fs.readFileSync(file)), bytes: fs.statSync(file).size,
      })),
    })
    await page.evaluate(() => window.__playerNativeClick.stop())
    await context.close()
  }
  if (['zone-grave-decoded-source', 'zone-grave-pile-fallback'].includes(requestedProbe)) {
    assert.equal(expectation, 'green', `${requestedProbe} binds the current local candidate`)
    const pileFallback = requestedProbe === 'zone-grave-pile-fallback'
    const context = await browser.newContext({ viewport: { width: 1366, height: 768 } })
    const page = await context.newPage()
    activePage = page
    const errors = []
    activeProbe = { probe: requestedProbe, errors }
    page.on('pageerror', error => errors.push(error.message))
    page.on('requestfailed', request => errors.push(`request failed: ${request.url()} (${request.failure()?.errorText ?? 'unknown'})`))
    page.on('response', response => {
      if (response.status() >= 400) errors.push(`http ${response.status()}: ${response.url()}`)
    })
    await page.route('**/*', async route => {
      const url = new URL(route.request().url())
      if (url.hostname === '127.0.0.1') await route.continue()
      else if (route.request().resourceType() === 'image')
        await route.fulfill({ status: 200, contentType: 'image/svg+xml', body: localFace })
      else await route.abort('blockedbyclient')
    })
    await page.goto(`http://127.0.0.1:${port}/__zone-decoded-source`, { waitUntil: 'networkidle' })
    await page.waitForFunction(() => window.__zoneDecodedSource?.ready, null, { timeout: 8000 })
    await page.waitForSelector('[data-l12-game-stage]')
    if (!pileFallback) {
      await page.locator('[data-player-index="0"] button.pile.graveyard').click()
      await page.waitForSelector(`.graveyard-overlay [data-card-instance-id="${zoneProbeFixture.target.instanceId}"]`)
      await page.waitForFunction(id => {
        const source = document.querySelector('.graveyard-overlay [data-card-instance-id="'+CSS.escape(id)+'"]')
        const images = source ? [...source.querySelectorAll('img')] : []
        return images.length > 0 && images.every(image => image.complete && image.naturalWidth > 0)
      }, zoneProbeFixture.target.instanceId, { timeout: 3000 })
    }
    const sourceProbe = await page.evaluate(() => window.__zoneDecodedSource.sourceProbe())
    if (pileFallback) assert.equal(sourceProbe.sourceExists, false,
      'closed-grave fallback unexpectedly found the exact target instance')
    else {
      assert.equal(sourceProbe.sourceExists, true, 'real public grave source was not rendered')
      assert.ok(sourceProbe.source.length > 0
        && sourceProbe.source.every(image => image.complete && image.naturalWidth > 0),
      'real public grave source was not decoded before authority delivery')
      assert.ok(sourceProbe.detached.some(image => !image.complete || image.naturalWidth === 0),
        'probe no longer reproduces detached-clone image readiness loss')
    }
    const baseline = await page.evaluate(() => window.__zoneDecodedSource.snapshot())
    for (let index = 1; index < zoneProbeFixture.chain.length; index += 1) {
      await page.evaluate(index => window.__zoneDecodedSource.apply(index), index)
      await page.waitForTimeout(40)
    }
    await page.waitForFunction(() => window.__zoneDecodedSource.snapshot().created === 1, null, { timeout: 3000 })
    const afterDelivery = await page.evaluate(() => window.__zoneDecodedSource.snapshot())
    const preparedScreenshot = path.join(output, pileFallback ? 'zone-grave-pile-fallback-prepared.png' : 'zone-grave-source-prepared.png')
    await page.screenshot({ path: preparedScreenshot })
    report.results.push({ probe: requestedProbe, errors, sourceProbe, baseline, afterDelivery })
    assert.equal(afterDelivery.created, 1,
      'real decoded grave source was cloned but no target movement holder was prepared')
    const targetCreate = afterDelivery.events.find(event => event.type === 'movement-holder-created'
      && event.instanceId === zoneProbeFixture.target.instanceId)
    if (pileFallback) {
      assert.match(String(targetCreate?.initialVisual?.className ?? ''), /(?:^|\s)card-back(?:\s|$)/,
        'closed graveyard cloned the unrelated pile face instead of a generic public anchor')
      assert.equal(targetCreate?.initialVisual?.canvasCount, 0,
        'closed graveyard copied the unrelated pile-top pixels into the prepared owner')
      assert.ok(!String(targetCreate?.initialVisual?.text ?? '').includes(zoneProbeFixture.baselineGraveTop.name),
        'closed graveyard copied the unrelated pile-top identity into the prepared owner')
    } else {
      assert.equal(afterDelivery.starts, 0, 'paused grave overlay allowed target motion to start')
      const graveClose = page.getByRole('button', { name: '关闭墓地' })
      if (await graveClose.count()) await graveClose.click()
    }
    await page.waitForFunction(() => window.__zoneDecodedSource.snapshot().starts === 1, null, { timeout: 7000 })
    const motion = await page.evaluate(() => window.__zoneDecodedSource.snapshot())
    assert.ok(motion.holders.length === 0
      || motion.holders.some(holder => holder.images.some(image => image.alt === zoneProbeFixture.target.name)),
    'grave pile anchor flew with the unrelated public top face instead of the authority event card')
    assert.ok(motion.holders.every(holder => holder.images.every(image => image.alt !== zoneProbeFixture.baselineGraveTop.name)),
      'grave pile fallback leaked the unrelated public top face into the target flight')
    const motionScreenshot = path.join(output, pileFallback ? 'zone-grave-pile-fallback-motion.png' : 'zone-grave-source-motion.png')
    await page.screenshot({ path: motionScreenshot })
    await page.waitForFunction(() => window.__zoneDecodedSource.snapshot().holders.length === 0, null, { timeout: 5000 })
    const settled = await page.evaluate(() => window.__zoneDecodedSource.snapshot())
    await page.evaluate(index => window.__zoneDecodedSource.apply(index), zoneProbeFixture.chain.length - 1)
    await page.waitForTimeout(400)
    const retransmit = await page.evaluate(() => window.__zoneDecodedSource.stop())
    assert.equal(retransmit.created, 1, 'same-revision grave-entry retransmission created another holder')
    assert.equal(retransmit.starts, 1, 'same-revision grave-entry retransmission restarted motion')
    assert.equal(settled.authority.visible, true, 'final field authority was not visible after movement')
    assert.equal(settled.authority.zone, 'field', 'grave-entry target did not settle in field DOM')
    assert.equal(settled.authority.visibility, 'visible', 'final field authority retained a visibility lease')
    assert.deepEqual(errors, [], 'zone decoded-source browser page error')
    let resetCycle=null
    if(pileFallback){
      // Same public engine frames are reused only as a relabelled-match
      // consumer contrast. This is not a second real engine trajectory.
      await page.evaluate(()=>window.__zoneDecodedSource.restart())
      await page.waitForFunction(()=>{
        const snapshot=window.__zoneDecodedSource.snapshot()
        return snapshot.revision===0&&!snapshot.authority.exists
      },null,{timeout:2000})
      for(let index=1;index<zoneProbeFixture.chain.length;index++){
        await page.evaluate(index=>window.__zoneDecodedSource.apply(index),index)
        await page.waitForTimeout(40)
      }
      await page.waitForFunction(()=>window.__zoneDecodedSource.snapshot().starts===2,null,{timeout:7000})
      await page.waitForFunction(()=>window.__zoneDecodedSource.snapshot().holders.length===0,null,{timeout:5000})
      const afterReset=await page.evaluate(()=>window.__zoneDecodedSource.snapshot())
      await page.evaluate(index=>window.__zoneDecodedSource.apply(index),zoneProbeFixture.chain.length-1)
      await page.waitForTimeout(180)
      const resetRetransmit=await page.evaluate(()=>window.__zoneDecodedSource.snapshot())
      assert.equal(afterReset.created,2,'new match dropped or duplicated its first same-id movement')
      assert.equal(resetRetransmit.starts,2,'new match movement retransmission restarted motion')
      assert.equal(resetRetransmit.authority.visibility,'visible','new match final authority retained a lease')
      resetCycle={claim:'Relabelled-match public consumer contrast, not a second engine run',afterReset,resetRetransmit}
    }
    report.results[0] = {
      ...report.results[0], motion, settled, retransmit,resetCycle,
      screenshots: [preparedScreenshot, motionScreenshot].map(file => ({
        path: file, sha256: sha256(fs.readFileSync(file)), bytes: fs.statSync(file).size,
      })),
    }
    await context.close()
  }
  if (requestedProbe === 'combat-hidden-clone') {
    assert.equal(expectation, 'green', 'combat-hidden-clone is a GREEN consumer regression only')
    const context = await browser.newContext({ viewport: { width: 1366, height: 768 } })
    const page = await context.newPage()
    activePage = page
    const errors = []
    activeProbe = { probe: requestedProbe, errors }
    page.on('pageerror', error => errors.push(error.message))
    page.on('requestfailed', request => errors.push(`request failed: ${request.url()} (${request.failure()?.errorText ?? 'unknown'})`))
    page.on('response', response => {
      if (response.status() >= 400) errors.push(`http ${response.status()}: ${response.url()}`)
    })
    await page.route('**/*', async route => {
      const url = new URL(route.request().url())
      if (url.hostname === '127.0.0.1') await route.continue()
      else if (route.request().resourceType() === 'image')
        await route.fulfill({ status: 200, contentType: 'image/svg+xml', body: localFace })
      else await route.abort('blockedbyclient')
    })
    await page.goto(`http://127.0.0.1:${port}/__spectator-single-owner?scenario=combat-exit&mode=ordinary`, { waitUntil: 'networkidle' })
    await page.waitForFunction(() => window.__singleVisualOwner?.ready, null, { timeout: 8000 })
    await page.waitForSelector('[data-l12-game-stage]')
    const baseline = await page.evaluate(() => window.__singleVisualOwner.combat())
    assert.deepEqual(baseline, { ghosts: [], created: [] }, 'combat baseline replayed a historical defeat')
    await page.evaluate(() => window.__singleVisualOwner.deliver('commit'))
    await page.waitForFunction(() => window.__singleVisualOwner.combat().ghosts.length === 2, null, { timeout: 3000 })
    const visibleDefeats = await page.evaluate(() => window.__singleVisualOwner.combat())
    assert.deepEqual([...visibleDefeats.created].sort(), [...combatProbe.instanceIds].sort(),
      'real combat resolution did not create each confirmed defeat exactly once')
    for (const ghost of visibleDefeats.ghosts) {
      assert.equal(ghost.cardVisibility, 'visible', `${ghost.instanceId}: defeat clone inherited hidden visibility`)
      assert.equal(ghost.cardInlineVisibility, '', `${ghost.instanceId}: defeat clone retained an inline visibility lease`)
      assert.equal(ghost.cardVisible, true, `${ghost.instanceId}: defeat clone had no visible pixels`)
    }
    const screenshot = path.join(output, 'combat-confirmed-defeats-visible.png')
    await page.screenshot({ path: screenshot })
    await page.waitForFunction(() => window.__singleVisualOwner.combat().ghosts.length === 0, null, { timeout: 5000 })
    await page.evaluate(() => window.__singleVisualOwner.deliver('retransmit'))
    await page.waitForTimeout(350)
    const retransmit = await page.evaluate(() => window.__singleVisualOwner.stop())
    const combatCreates = retransmit.events.filter(item => item.type === 'combat-defeat-ghost-created')
    assert.equal(combatCreates.length, 2, 'same-revision combat retransmission replayed a defeat clone')
    assert.deepEqual(errors, [], 'combat hidden-clone browser page error')
    report.results.push({ probe: requestedProbe, errors, baseline, visibleDefeats,
      afterRetransmit: { activeGhosts: retransmit.frames.at(-1)?.combatGhosts ?? [], combatCreates },
      screenshot: { path: screenshot, sha256: sha256(fs.readFileSync(screenshot)), bytes: fs.statSync(screenshot).size },
    })
    await context.close()
  }
  if (requestedProbe === 'state-decode-cancel') {
    assert.equal(expectation, 'green', 'state-decode-cancel is a GREEN lifecycle regression only')
    assert.ok(scenarios.rest, 'state-decode-cancel requires a real rest authority case')
    const context = await browser.newContext({ viewport: { width: 1366, height: 768 } })
    const page = await context.newPage()
    activePage = page
    const errors = []
    activeProbe = { probe: requestedProbe, errors }
    page.on('pageerror', error => errors.push(error.message))
    page.on('requestfailed', request => errors.push(`request failed: ${request.url()} (${request.failure()?.errorText ?? 'unknown'})`))
    page.on('response', response => {
      if (response.status() >= 400) errors.push(`http ${response.status()}: ${response.url()}`)
    })
    await page.route('**/*', async route => {
      const url = new URL(route.request().url())
      if (url.hostname === '127.0.0.1') await route.continue()
      else if (route.request().resourceType() === 'image')
        await route.fulfill({ status: 200, contentType: 'image/svg+xml', body: localFace })
      else await route.abort('blockedbyclient')
    })
    await page.goto(`http://127.0.0.1:${port}/__spectator-single-owner?scenario=rest&mode=ordinary`, { waitUntil: 'networkidle' })
    await page.waitForFunction(() => window.__singleVisualOwner?.ready, null, { timeout: 8000 })
    await page.waitForSelector('[data-l12-game-stage]')
    const diagnostic = instanceId => {
      const authority = document.querySelector('[data-l12-game-stage] [data-card-instance-id="'+CSS.escape(instanceId)+'"]')
      const holders = [...document.querySelectorAll('.l12-card-state-transition-ghost')]
        .filter(holder => holder.dataset.visualTransitionKey?.includes(instanceId))
      return { matchId:window.__singleVisualOwner.state().matchId,
        counts:window.__singleVisualOwner.counts(),starts:window.__singleVisualOwner.starts(),
        holders:holders.map(holder=>({connected:holder.isConnected,visibility:getComputedStyle(holder).visibility,
          images:[...holder.querySelectorAll('img')].map(image=>({complete:image.complete,naturalWidth:image.naturalWidth}))})),
        authority:authority instanceof HTMLElement?{exists:true,visibility:getComputedStyle(authority).visibility,
          inlineVisibility:authority.style.visibility,tapped:authority.classList.contains('tapped')}: {exists:false} }
    }
    const instanceId = scenarios.rest.instanceId
    const baseline = await page.evaluate(diagnostic, instanceId)
    await page.evaluate(() => window.__singleVisualOwner.armStateDecodeHold())
    await page.evaluate(() => window.__singleVisualOwner.deliver('commit'))
    await page.waitForFunction(() => window.__singleVisualOwner.counts().state === 1, null, { timeout: 2000 })
    const pending = await page.evaluate(diagnostic, instanceId)
    assert.equal(pending.starts.state, 0, 'held clone decode still started state motion')
    assert.equal(pending.holders.length, 1, 'held clone decode did not retain exactly one hidden prepared holder')
    assert.equal(pending.holders[0].visibility, 'hidden', 'undecoded prepared holder became visible')
    assert.equal(pending.authority.visibility, 'visible', 'decode hold covered final authority without safe pixels')
    const pendingScreenshot = path.join(output, 'state-decode-pending.png')
    await page.screenshot({ path: pendingScreenshot })
    const switchAt = Date.now()
    await page.evaluate(() => window.__singleVisualOwner.switchMatch())
    await page.waitForFunction(id => {
      const state = window.__singleVisualOwner.state()
      const holder = [...document.querySelectorAll('.l12-card-state-transition-ghost')]
        .find(node => node.dataset.visualTransitionKey?.includes(id))
      return state.matchId.endsWith('-decode-cancel') && !holder
    }, instanceId, { timeout: 1000 })
    const cleaned = await page.evaluate(diagnostic, instanceId)
    const switchCleanupMs = Date.now() - switchAt
    assert.equal(cleaned.holders.length, 0, 'match switch retained the pending decode holder')
    assert.equal(cleaned.authority.visibility, 'visible', 'match switch retained an authority visibility lease')
    assert.equal(cleaned.authority.inlineVisibility, '', 'match switch left inline visibility ownership behind')
    assert.equal(cleaned.starts.state, 0, 'cancelled pending decode began motion during match reset')
    await page.evaluate(() => window.__singleVisualOwner.releaseStateDecodeHold())
    // The next action needs a decoded source in the new match. A cancelled
    // old clone is not that source, and painting the new baseline precedes
    // any claim about its subsequent animation.
    await page.waitForFunction(id => {
      const source = document.querySelector('[data-l12-game-stage] [data-card-instance-id="'+CSS.escape(id)+'"]')
      const images = source ? [...source.querySelectorAll('img')] : []
      return window.__singleVisualOwner.state().revision === 0
        && images.length > 0 && images.every(image => image.complete && image.naturalWidth > 0)
    }, instanceId, { timeout: 3000 })
    await page.evaluate(() => window.__singleVisualOwner.deliverSwitchRetransmit())
    await page.waitForFunction(() => window.__singleVisualOwner.starts().state === 1, null, { timeout: 3000 })
    await page.waitForFunction(() => !document.querySelector('.l12-card-state-transition-ghost'), null, { timeout: 5000 })
    const recovered = await page.evaluate(diagnostic, instanceId)
    assert.equal(recovered.authority.visibility, 'visible', 'post-reset authority remained hidden')
    assert.equal(recovered.authority.inlineVisibility, '', 'post-reset authority retained a visibility lease')
    assert.equal(recovered.authority.tapped, scenarios.rest.expected.tapped,
      'post-reset real transition did not settle its authority state')
    await page.evaluate(() => window.__singleVisualOwner.deliverSwitchCommit())
    await page.waitForTimeout(300)
    const retransmit = await page.evaluate(diagnostic, instanceId)
    assert.deepEqual(retransmit.counts, recovered.counts, 'post-reset same-revision retransmit recreated a holder')
    assert.deepEqual(retransmit.starts, recovered.starts, 'post-reset same-revision retransmit restarted motion')
    assert.deepEqual(errors, [], 'state decode-cancel browser page error')
    const observed = await page.evaluate(() => window.__singleVisualOwner.stop())
    report.results.push({ probe: requestedProbe, errors, baseline, pending, cleaned, recovered, retransmit,
      switchCleanupMs, queueOperationalAfterReset: recovered.starts.state === 1,
      observedCounts: observed.counts, observedStarts: observed.starts,
      screenshot: { path: pendingScreenshot, sha256: sha256(fs.readFileSync(pendingScreenshot)), bytes: fs.statSync(pendingScreenshot).size },
    })
    await context.close()
  }
  const requestedMode = String(process.env.L12_SPECTATOR_SINGLE_OWNER_MODE || '').trim()
  assert.ok(!requestedMode || ['ordinary', 'tournament'].includes(requestedMode),
    `unsupported L12_SPECTATOR_SINGLE_OWNER_MODE: ${requestedMode}`)
  const requestedScenario = String(process.env.L12_SPECTATOR_SINGLE_OWNER_SCENARIO || '').trim()
  assert.ok(!requestedScenario || Object.hasOwn(scenarios, requestedScenario),
    `unsupported L12_SPECTATOR_SINGLE_OWNER_SCENARIO: ${requestedScenario}`)
  const modes = requestedProbe ? [] : requestedMode ? [requestedMode] : ['ordinary', 'tournament']
  const selectedScenarios = requestedScenario
    ? [[requestedScenario, scenarios[requestedScenario]]]
    : Object.entries(scenarios)
  for (const [scenarioName, scenario] of selectedScenarios) for (const mode of modes) {
    const context = await browser.newContext({ viewport: { width: 1366, height: 768 } })
    const page = await context.newPage()
    activePage = page
    const errors = []
    activeProbe = { mode, scenarioName, errors }
    page.on('pageerror', error => errors.push(error.message))
    page.on('requestfailed', request => errors.push(`request failed: ${request.url()} (${request.failure()?.errorText ?? 'unknown'})`))
    page.on('response', response => {
      if (response.status() >= 400) errors.push(`http ${response.status()}: ${response.url()}`)
    })
    await page.route('**/*', async route => {
      const url = new URL(route.request().url())
      if (url.hostname === '127.0.0.1') await route.continue()
      else if (route.request().resourceType() === 'image') await route.fulfill({ status: 200, contentType: 'image/svg+xml', body: localFace })
      else await route.abort('blockedbyclient')
    })
    await page.goto(`http://127.0.0.1:${port}/__spectator-single-owner?scenario=${scenarioName}&mode=${mode}`, { waitUntil: 'networkidle' })
    await page.waitForFunction(() => window.__singleVisualOwner?.ready, null, { timeout: 8000 })
    await page.waitForSelector('[data-l12-game-stage]')
    await page.waitForTimeout(80)
    const initial = await page.evaluate(() => ({ counts: window.__singleVisualOwner.counts(),
      starts: window.__singleVisualOwner.starts(), state: window.__singleVisualOwner.state() }))
    assert.deepEqual(initial.counts, { state: 0, movement: 0 }, `${mode}/${scenarioName}: first snapshot replayed history`)
    assert.deepEqual(initial.starts, { state: 0, movement: 0 }, `${mode}/${scenarioName}: first snapshot started historical motion`)
    assert.equal(initial.state.spectating, true, `${mode}/${scenarioName}: production consumer did not retain spectator mode`)
    const beforeCommit = initial.counts
    const beforeCommitStarts = initial.starts
    await page.evaluate(() => window.__singleVisualOwner.deliver('commit'))
    const violationFlag = scenarioName === 'entry' ? 'authorityVisibleBeforeGhost' : 'authorityTransitionBeforeGhost'
    if (expectation === 'red')
      await page.waitForFunction(flag => window.__singleVisualOwner.flags[flag], violationFlag, { timeout: 3000 })
    else
      await page.waitForFunction(() => window.__singleVisualOwner.flags.ghostVisible, null, { timeout: 3000 })
    const preGhostScreenshot = path.join(output, `${mode}-${scenarioName}-${expectation === 'red' ? 'authority-before-ghost' : 'prepared-owner'}.png`)
    await page.screenshot({ path: preGhostScreenshot })
    if (scenario[mode].release) await page.evaluate(() => window.__singleVisualOwner.deliver('release'))
    await page.waitForFunction(() => window.__singleVisualOwner.flags.ghostVisible, null, { timeout: 7000 })
    const ghostScreenshot = path.join(output, `${mode}-${scenarioName}-ghost-visible.png`)
    await page.screenshot({ path: ghostScreenshot })
    await page.waitForFunction(({ scenarioName, beforeCommit }) => {
      const counts = window.__singleVisualOwner.counts()
      return scenarioName === 'entry' ? counts.movement >= beforeCommit.movement + 1 : counts.state >= beforeCommit.state + 1
    }, { scenarioName, beforeCommit }, { timeout: 3000 })
    await page.waitForFunction(() => !document.querySelector('.l12-card-state-transition-ghost,.l12-zone-flight-ghost,.zone-card-movement'), null, { timeout: 5000 })
    const settledBefore = await page.evaluate(id => {
      const node = document.querySelector('[data-l12-game-stage] [data-card-instance-id="'+CSS.escape(id)+'"]')
      return node instanceof HTMLElement
        ? { visibility:getComputedStyle(node).visibility, transform:getComputedStyle(node).transform,
          tapped:node.classList.contains('tapped'), rect:node.getBoundingClientRect().toJSON() }
        : null
    }, scenario.instanceId)
    await page.waitForTimeout(220)
    const settledAfter = await page.evaluate(id => {
      const node = document.querySelector('[data-l12-game-stage] [data-card-instance-id="'+CSS.escape(id)+'"]')
      return node instanceof HTMLElement
        ? { visibility:getComputedStyle(node).visibility, transform:getComputedStyle(node).transform,
          tapped:node.classList.contains('tapped'), rect:node.getBoundingClientRect().toJSON() }
        : null
    }, scenario.instanceId)
    assert.deepEqual(settledAfter, settledBefore,
      `${mode}/${scenarioName}: final authority CSS changed again after the visual owner released`)
    const afterCommit = await page.evaluate(() => window.__singleVisualOwner.counts())
    const afterCommitStarts = await page.evaluate(() => window.__singleVisualOwner.starts())
    const commitMotionOrder = await page.evaluate(() => window.__singleVisualOwner.events
      .filter(item => item.type === 'state-motion-started' || item.type === 'movement-motion-started')
      .map(item => item.type.slice(0, -'-motion-started'.length)))
    await page.evaluate(() => window.__singleVisualOwner.deliver('retransmit'))
    await page.waitForTimeout(300)
    const afterRetransmit = await page.evaluate(() => window.__singleVisualOwner.counts())
    const afterRetransmitStarts = await page.evaluate(() => window.__singleVisualOwner.starts())
    assert.deepEqual(afterRetransmit, afterCommit, `${mode}/${scenarioName}: same-revision retransmit replayed motion`)
    assert.deepEqual(afterRetransmitStarts, afterCommitStarts,
      `${mode}/${scenarioName}: same-revision retransmit restarted motion`)

    let afterControl = null
    let afterControlStarts = null
    if (selectedHasControl(scenario, mode)) {
      await page.evaluate(() => window.__singleVisualOwner.deliver('control'))
      await page.waitForFunction(({ scenarioName, afterCommit }) => {
        const counts = window.__singleVisualOwner.counts()
        return scenarioName === 'entry' ? counts.movement === afterCommit.movement + 1 : counts.state === afterCommit.state + 1
      }, { scenarioName, afterCommit }, { timeout: 3000 })
      await page.waitForFunction(() => !document.querySelector('.l12-card-state-transition-ghost,.l12-zone-flight-ghost,.zone-card-movement'), null, { timeout: 5000 })
      afterControl = await page.evaluate(() => window.__singleVisualOwner.counts())
      afterControlStarts = await page.evaluate(() => window.__singleVisualOwner.starts())
    }
    const observed = await page.evaluate(() => window.__singleVisualOwner.stop())
    const traceFrames = observed.frames.filter(frame => frame.sinceDelivery !== null)
    const visibleGhostCount = frame => frame.stateGhosts.filter(ghost => ghost.visible).length
      + frame.movementGhosts.filter(ghost => ghost.visible).length
    const ghostIsMoving = ghost => ghost.visible && ghost.animations.some(animation =>
      animation.playState === 'running' && animation.currentTime >= 0)
    const motionKindsAt = frame => [
      ...(frame.movementGhosts.some(ghostIsMoving) ? ['movement'] : []),
      ...(frame.stateGhosts.some(ghostIsMoving) ? ['state'] : []),
    ]
    const firstOwnerFrame = traceFrames.find(frame => visibleGhostCount(frame) > 0) ?? null
    const firstMotionFrame = traceFrames.find(frame => motionKindsAt(frame).length > 0) ?? null
    const authorityIsFinal = frame => frame.authority.zone === scenario.expected.zone
      && frame.authority.tapped === scenario.expected.tapped
    const preMotionFinalAuthorityFrames = traceFrames
      .filter(frame => (!firstMotionFrame || frame.index < firstMotionFrame.index)
        && frame.authority.visible && authorityIsFinal(frame))
      .map(frame => frame.index)
    const blankFrames = traceFrames.filter(frame => !frame.authority.visible
      && visibleGhostCount(frame) === 0
      && !(scenarioName === 'entry' && (!firstOwnerFrame || frame.index < firstOwnerFrame.index)
        && frame.sourceBacks.visibleCount > 0)).map(frame => frame.index)
    const overlappingOwnerFrames = traceFrames.filter(frame =>
      Number(frame.authority.visible) + visibleGhostCount(frame) > 1).map(frame => ({
        index: frame.index,
        authority: frame.authority.visible,
        stateGhosts: frame.stateGhosts.filter(ghost => ghost.visible).length,
        movementGhosts: frame.movementGhosts.filter(ghost => ghost.visible).length,
      }))
    const motionOrder = []
    for (const frame of traceFrames) for (const kind of motionKindsAt(frame))
      if (motionOrder.at(-1) !== kind) motionOrder.push(kind)
    const frameContract = {
      firstOwnerFrame: firstOwnerFrame?.index ?? null,
      firstMotionFrame: firstMotionFrame?.index ?? null,
      preMotionFinalAuthorityFrames,
      blankFrames,
      overlappingOwnerFrames,
      motionOrder,
      maxVisibleSpecificOwners: Math.max(0, ...traceFrames.map(frame =>
        Number(frame.authority.visible) + visibleGhostCount(frame))),
    }
    const screenshotFiles = [preGhostScreenshot, ghostScreenshot].map(file => ({
      path: file, sha256: sha256(fs.readFileSync(file)), bytes: fs.statSync(file).size,
    }))
    const result = {
      mode, scenario: scenarioName, wireType: scenario[mode].commitWireType, errors,
      initial, afterCommit, afterCommitStarts, commitMotionOrder,
      afterRetransmit, afterRetransmitStarts, afterControl, afterControlStarts,
      settledBefore, settledAfter,
      flags: observed.flags, counts: observed.counts, frames: observed.frames, events: observed.events,
      frameContract,
      screenshots: screenshotFiles,
      wireBudget: { fullBytes: scenario[mode].ordinaryFullBytes, deltaBytes: scenario[mode].ordinaryDeltaBytes,
        deltaRatio: Number((scenario[mode].ordinaryDeltaBytes / scenario[mode].ordinaryFullBytes).toFixed(6)) },
    }
    report.results.push(result)
    assert.deepEqual(errors, [], `${mode}/${scenarioName}: browser page error`)
    assert.deepEqual(frameContract.blankFrames, [], `${mode}/${scenarioName}: current handoff contains a blank frame`)
    if (frameContract.preMotionFinalAuthorityFrames.length) report.redFailures.push({ mode, scenario: scenarioName,
      contract: scenarioName === 'entry'
        ? 'authority destination must not become visible before its one entry movement begins'
        : 'authority node must not present the committed state before its one state ghost begins',
      frameIndexes: frameContract.preMotionFinalAuthorityFrames })
    if (expectation === 'green') {
      const delta = (after, before) => ({ state: after.state - before.state, movement: after.movement - before.movement })
      assert.deepEqual(delta(afterCommit, beforeCommit), scenario.expectedCommitMotions,
        `${mode}/${scenarioName}: committed authority actions did not each create exactly one visual`)
      assert.deepEqual(delta(afterCommitStarts, beforeCommitStarts), scenario.expectedCommitMotions,
        `${mode}/${scenarioName}: committed authority actions did not each start exactly one motion`)
      assert.deepEqual(commitMotionOrder, scenario.expectedCommitOrder,
        `${mode}/${scenarioName}: committed authority motions were missing, repeated, or reordered`)
      if (afterControl) {
        assert.deepEqual(delta(afterControl, afterRetransmit), scenario.expectedControlMotions,
          `${mode}/${scenarioName}: distinct same-text authority action did not create exactly one visual`)
        assert.deepEqual(delta(afterControlStarts, afterRetransmitStarts), scenario.expectedControlMotions,
          `${mode}/${scenarioName}: distinct same-text authority action did not start exactly one motion`)
      }
      assert.notEqual(frameContract.firstOwnerFrame, null, `${mode}/${scenarioName}: no prepared visual owner appeared`)
      assert.notEqual(frameContract.firstMotionFrame, null, `${mode}/${scenarioName}: no authority motion started`)
      assert.deepEqual(frameContract.preMotionFinalAuthorityFrames, [],
        `${mode}/${scenarioName}: final authority appeared before motion`)
      assert.deepEqual(frameContract.overlappingOwnerFrames, [],
        `${mode}/${scenarioName}: more than one visual owner was visible in a frame`)
      assert.equal(frameContract.maxVisibleSpecificOwners, 1,
        `${mode}/${scenarioName}: exact visual ownership was not one throughout presentation`)
      assert.equal(frameContract.motionOrder[0], scenarioName === 'entry' ? 'movement' : 'state',
        `${mode}/${scenarioName}: wrong first authority motion owner`)
    }
    await context.close()
  }

  assertSourceBindingsStable()
  report.status = requestedProbe ? 'green-confirmed'
    : expectation === 'green' ? 'green-confirmed'
    : restCase || requestedScenario === 'entry' ? 'red-confirmed' : 'partial-red-fixture-blocked'
  report.summary = {
    runs: report.results.length,
    redFailures: report.redFailures.length,
    blankFrames: report.results.filter(result => result.flags?.blankFrame).length,
    missingFixturePreconditions: fixturePreconditions.missing,
  }
  const reportPath = path.join(output, 'report.json')
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2))
  if (requestedProbe) {
    console.log(requestedProbe === 'player-native-click'
      ? 'GREEN confirmed: native player target selection remains hit-testable under a prepared visual owner'
      : requestedProbe === 'player-inline-minimize'
        ? 'GREEN confirmed: native inline prompt minimize/restore never reroutes to the generic modal'
      : requestedProbe === 'combat-hidden-clone'
        ? 'GREEN confirmed: real combat defeat clones remain visible and retransmit once'
      : requestedProbe === 'state-decode-cancel'
        ? 'GREEN confirmed: pending state decode cancels cleanly and the next match transition runs once'
      : 'GREEN confirmed: decoded public grave source produces exactly one grave-to-field movement')
  } else if (expectation === 'green') {
    assert.equal(report.redFailures.length, 0,
      'GREEN candidate still exposes final authority before its visual motion')
    console.log(`GREEN confirmed: ${report.results.length}/${report.results.length} selected spectator paths have one visual owner`)
  } else {
    if (!restCase && requestedScenario !== 'entry')
      throw new Error(`Partial RED only: ${fixturePreconditions.missing.join('; ')}`)
    assert.equal(report.redFailures.length, report.results.length,
      'Expected every selected rest/entry path to violate single visual ownership before production fix')
    throw new Error(`RED confirmed: ${report.redFailures.length}/${report.results.length} spectator paths expose authority before their ghost`)
  }
} catch (error) {
  report.status = report.status === 'running' ? 'harness-failed' : report.status
  report.activeProbe = activeProbe
  report.bootState = activePage && !activePage.isClosed() ? await activePage.evaluate(() => ({
    location: location.href,
    bodyText: document.body?.innerText ?? '',
    boot: window.__singleVisualBoot ? {
      stage: window.__singleVisualBoot.stage,
      netState: window.__singleVisualBoot.netState?.() ?? null,
    } : null,
    owner: window.__singleVisualOwner ? {
      counts: window.__singleVisualOwner.counts(),
      flags: {...window.__singleVisualOwner.flags},
      state: window.__singleVisualOwner.state(),
      events: structuredClone(window.__singleVisualOwner.events),
      frames: structuredClone(window.__singleVisualOwner.frames),
    } : null,
    resources: performance.getEntriesByType('resource').map(item => item.name),
  })).catch(probeError => ({ probeError: String(probeError) })) : null
  report.error = error instanceof Error ? { name: error.name, message: error.message, stack: error.stack } : String(error)
  fs.writeFileSync(path.join(output, 'failure.json'), JSON.stringify(report, null, 2))
  throw error
} finally {
  await browser?.close()
  await server.close()
}

function selectedHasControl(scenario, mode) {
  return Object.hasOwn(scenario[mode], 'control')
}
