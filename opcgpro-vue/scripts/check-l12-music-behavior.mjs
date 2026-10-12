import assert from 'node:assert/strict'
import { BackgroundMusicController } from '../src/l12/backgroundMusic.ts'

let now = 0
let nextTimer = 0
const timers = new Map()
const audios = []
globalThis.window = {
  setInterval(callback) { const id = ++nextTimer; timers.set(id, callback); return id },
  clearInterval(id) { timers.delete(id) },
}
Object.defineProperty(globalThis, 'performance', { value: { now: () => now }, configurable: true })
globalThis.Audio = class {
  constructor(src) { this.src = src; this.srcAttribute = src; this.currentSrc = src; this.volume = 1; this.loop = false; this.paused = true; this.loadCalls = 0; audios.push(this) }
  play() { this.paused = false; return Promise.resolve() }
  pause() { this.paused = true }
  getAttribute(name) { return name === 'src' ? this.srcAttribute : null }
  removeAttribute(name) {
    if (name !== 'src') return
    this.srcAttribute = null
    this.src = ''
  }
  load() { assert.equal(this.onended, null); this.currentSrc = ''; this.loadCalls++ }
}
function advance(ms) {
  now += ms
  for (const [id, callback] of [...timers]) if (timers.has(id)) callback()
}
function close(actual, expected) { assert.ok(Math.abs(actual - expected) < 1e-8, actual + ' != ' + expected) }
const music = new BackgroundMusicController()
let staleEnded = 0
let currentEnded = 0
music.setTarget({src:'home.mp3',loop:false,volume:.12,onEnded:()=>staleEnded++})
close(audios[0].volume, 0)
advance(260)
close(audios[0].volume, .06)
advance(260)
close(audios[0].volume, .12)
const oldEnded = audios[0].onended
music.setTarget({src:'battle.mp3',loop:true,volume:.08})
oldEnded()
assert.equal(staleEnded, 0)
advance(260)
close(audios[0].volume, .06)
close(audios[1].volume, .04)
advance(260)
assert.equal(audios[0].paused, true)
assert.equal(audios[0].getAttribute('src'), null)
assert.equal(audios[0].loadCalls, 1)
close(audios[1].volume, .08)
music.setTarget({src:'battle.mp3',loop:false,volume:.0001,onEnded:()=>currentEnded++})
advance(520)
assert.equal(audios.length, 2)
assert.equal(audios[1].loop, false)
audios[1].onended()
assert.equal(currentEnded, 1)
assert.equal(audios[1].loadCalls, 0)
close(audios[1].volume, .0001)
music.setTarget(null)
advance(520)
assert.ok(audios.every(audio => audio.paused))
assert.equal(audios[1].getAttribute('src'), null)
assert.equal(audios[1].loadCalls, 1)
music.setTarget({src:'home.mp3',loop:true,volume:.2})
advance(80)
music.setTarget({src:'battle2.mp3',loop:true,volume:.2})
advance(80)
music.setTarget({src:'home.mp3',loop:true,volume:.2})
const newest = audios.at(-1)
advance(80)
music.destroy()
assert.equal(timers.size, 0)
assert.ok(audios.every(audio => audio.paused))
assert.ok(audios.every(audio => audio.getAttribute('src') === null))
assert.ok(audios.every(audio => audio.loadCalls === 1))
const loadsAfterDestroy = audios.map(audio => audio.loadCalls)
music.destroy()
assert.deepEqual(audios.map(audio => audio.loadCalls), loadsAfterDestroy)
assert.equal(newest.currentSrc, '')
console.log('L12 music behavior passed: fade-in, crossfade retirement, stale ended, same-src loop/callback/low-volume reuse, mute, A-B-A cleanup, idempotent destroy')
