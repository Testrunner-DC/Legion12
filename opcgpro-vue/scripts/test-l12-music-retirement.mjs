import assert from 'node:assert/strict'
import { BackgroundMusicController } from '../src/l12/backgroundMusic.ts'

let now = 0
let nextTimer = 0
const timers = new Map()
const audios = []

globalThis.window = {
  setInterval(callback) {
    const id = ++nextTimer
    timers.set(id, callback)
    return id
  },
  clearInterval(id) { timers.delete(id) },
}
Object.defineProperty(globalThis, 'performance', {
  value: { now: () => now },
  configurable: true,
})

globalThis.Audio = class FakeAudio {
  constructor(src) {
    this.src = src
    this.currentSrc = src
    this.srcAttribute = src
    this.volume = 1
    this.loop = false
    this.paused = true
    this.onended = null
    this.playCalls = 0
    this.pauseCalls = 0
    this.removeSrcCalls = 0
    this.loadCalls = 0
    audios.push(this)
  }

  play() {
    this.playCalls++
    this.paused = false
    return Promise.resolve()
  }

  pause() {
    this.pauseCalls++
    this.paused = true
  }

  getAttribute(name) {
    return name === 'src' ? this.srcAttribute : null
  }

  removeAttribute(name) {
    if (name !== 'src') return
    this.removeSrcCalls++
    this.srcAttribute = null
    this.src = ''
  }

  load() {
    assert.equal(this.onended, null, 'onended must be cleared before load()')
    assert.equal(this.srcAttribute, null, 'src must be removed before load()')
    this.loadCalls++
    this.currentSrc = ''
  }
}

function reset() {
  assert.equal(timers.size, 0, 'previous scenario leaked a fade timer')
  now = 0
  nextTimer = 0
  audios.length = 0
}

function advance(milliseconds) {
  now += milliseconds
  for (const [id, callback] of [...timers]) {
    if (timers.has(id)) callback()
  }
}

function close(actual, expected) {
  assert.ok(Math.abs(actual - expected) < 1e-8, `${actual} != ${expected}`)
}

function assertReleasedOnce(audio, label) {
  assert.equal(audio.paused, true, `${label}: released audio must be paused`)
  assert.equal(audio.onended, null, `${label}: released audio must clear onended`)
  assert.equal(audio.getAttribute('src'), null, `${label}: released audio must remove src`)
  assert.equal(audio.currentSrc, '', `${label}: test-double load bookkeeping`)
  assert.equal(audio.removeSrcCalls, 1, `${label}: src must be removed once`)
  assert.equal(audio.loadCalls, 1, `${label}: load() must run once`)
}

function sameSourceReuseAndIdempotentDestroy() {
  reset()
  const music = new BackgroundMusicController()
  let firstEnded = 0
  let currentEnded = 0
  music.setTarget({ src: 'same.mp3', loop: true, volume: .2, onEnded: () => firstEnded++ })
  const audio = audios[0]
  advance(520)
  close(audio.volume, .2)

  music.setTarget({ src: 'same.mp3', loop: false, volume: .35, onEnded: () => currentEnded++ })
  assert.equal(audios.length, 1, 'same active src must reuse its Audio element')
  assert.equal(audio.loop, false, 'same-src update must refresh loop')
  assert.equal(audio.loadCalls, 0, 'same-src update must not release the active element')
  advance(520)
  close(audio.volume, .35)
  audio.onended?.()
  assert.equal(firstEnded, 0, 'replaced same-src ended callback must not fire')
  assert.equal(currentEnded, 1, 'current same-src ended callback must fire')

  music.destroy()
  assertReleasedOnce(audio, 'same-src destroy')
  assert.equal(timers.size, 0)
  music.destroy()
  assertReleasedOnce(audio, 'second destroy')
}

function switchRetiresOnlyAfterFade() {
  reset()
  const music = new BackgroundMusicController()
  let staleEnded = 0
  music.setTarget({ src: 'a.mp3', loop: false, volume: .8, onEnded: () => staleEnded++ })
  advance(520)
  const oldAudio = audios[0]
  const oldEnded = oldAudio.onended

  music.setTarget({ src: 'b.mp3', loop: true, volume: .6 })
  const currentAudio = audios[1]
  oldEnded?.()
  assert.equal(staleEnded, 0, 'obsolete track ended callback must be generation-isolated')
  advance(519)
  assert.equal(oldAudio.loadCalls, 0, 'retire must preserve the 520 ms fade')
  assert.notEqual(oldAudio.getAttribute('src'), null)
  advance(1)
  assertReleasedOnce(oldAudio, 'crossfade old track')
  assert.equal(currentAudio.loadCalls, 0, 'old-track retirement must not release the current track')
  assert.equal(currentAudio.paused, false)
  assert.equal(currentAudio.getAttribute('src'), 'b.mp3')
  advance(1_000)
  assertReleasedOnce(oldAudio, 'crossfade old track after stale ticks')
  music.destroy()
  assertReleasedOnce(currentAudio, 'crossfade current destroy')
}

function rapidAbaDoesNotRetireNewestA() {
  reset()
  const music = new BackgroundMusicController()
  let oldEndedCount = 0
  music.setTarget({ src: 'a.mp3', loop: false, volume: .4, onEnded: () => oldEndedCount++ })
  const firstA = audios[0]
  const firstAEnded = firstA.onended
  advance(80)
  music.setTarget({ src: 'b.mp3', loop: true, volume: .4 })
  const middleB = audios[1]
  advance(80)
  music.setTarget({ src: 'a.mp3', loop: true, volume: .4 })
  const newestA = audios[2]
  assert.equal(audios.length, 3, 'A-B-A must allocate a new A while old A retires')
  firstAEnded?.()
  assert.equal(oldEndedCount, 0)
  advance(520)
  assertReleasedOnce(firstA, 'rapid A-B-A first A')
  assertReleasedOnce(middleB, 'rapid A-B-A middle B')
  assert.equal(newestA.loadCalls, 0, 'stale retire callbacks must not unload newest A')
  assert.equal(newestA.paused, false)
  assert.equal(newestA.getAttribute('src'), 'a.mp3')
  music.destroy()
  assertReleasedOnce(newestA, 'rapid A-B-A newest A destroy')
}

function disableAgainThenRestartKeepsNewTrack() {
  reset()
  const music = new BackgroundMusicController()
  let obsoleteEnded = 0
  music.setTarget({ src: 'old.mp3', loop: false, volume: .5, onEnded: () => obsoleteEnded++ })
  advance(520)
  const oldAudio = audios[0]
  const oldEnded = oldAudio.onended
  music.setTarget(null)
  music.setTarget(null)
  advance(80)
  music.setTarget({ src: 'new.mp3', loop: true, volume: .5 })
  const newAudio = audios[1]
  oldEnded?.()
  assert.equal(obsoleteEnded, 0)
  advance(40)
  assertReleasedOnce(oldAudio, 'disable/null/restart old track')
  assert.equal(newAudio.loadCalls, 0, 'old disable closure must not unload restarted track')
  assert.equal(newAudio.paused, false)
  assert.equal(newAudio.getAttribute('src'), 'new.mp3')
  advance(520)
  assert.equal(newAudio.loadCalls, 0)
  music.destroy()
  assertReleasedOnce(newAudio, 'disable/null/restart current destroy')
}

function destroyReleasesOverlappingFadesOnce() {
  reset()
  const music = new BackgroundMusicController()
  music.setTarget({ src: 'a.mp3', loop: true, volume: .5 })
  advance(80)
  music.setTarget({ src: 'b.mp3', loop: true, volume: .5 })
  advance(80)
  const [oldAudio, activeAudio] = audios
  music.destroy()
  assertReleasedOnce(oldAudio, 'destroy overlapping retired track')
  assertReleasedOnce(activeAudio, 'destroy overlapping active track')
  assert.equal(timers.size, 0, 'destroy must clear every fade timer')
  music.destroy()
  advance(2_000)
  assertReleasedOnce(oldAudio, 'second destroy old track')
  assertReleasedOnce(activeAudio, 'second destroy active track')
  assert.equal(timers.size, 0)
}

sameSourceReuseAndIdempotentDestroy()
switchRetiresOnlyAfterFade()
rapidAbaDoesNotRetireNewestA()
disableAgainThenRestartKeepsNewTrack()
destroyReleasesOverlappingFadesOnce()

console.log('Background music retirement guard passed: release, fade boundary, same-src reuse, A-B-A, disable/restart, generation isolation, idempotent destroy')
