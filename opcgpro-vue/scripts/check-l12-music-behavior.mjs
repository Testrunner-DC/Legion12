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
  constructor(src) { this.src = src; this.volume = 1; this.loop = false; this.paused = true; audios.push(this) }
  play() { this.paused = false; return Promise.resolve() }
  pause() { this.paused = true }
}
function advance(ms) {
  now += ms
  for (const [id, callback] of [...timers]) if (timers.has(id)) callback()
}
function close(actual, expected) { assert.ok(Math.abs(actual - expected) < 1e-8, actual + ' != ' + expected) }
const music = new BackgroundMusicController()
let staleEnded = 0
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
close(audios[1].volume, .08)
music.setTarget({src:'battle.mp3',loop:true,volume:.0001})
advance(520)
assert.equal(audios.length, 2)
close(audios[1].volume, .0001)
music.setTarget(null)
advance(520)
assert.ok(audios.every(audio => audio.paused))
music.setTarget({src:'home.mp3',loop:true,volume:.2})
advance(80)
music.setTarget({src:'battle2.mp3',loop:true,volume:.2})
advance(80)
music.destroy()
assert.equal(timers.size, 0)
assert.ok(audios.every(audio => audio.paused))
console.log('L12 music behavior passed: fade-in, crossfade, stale ended, low volume, mute, rapid-switch cleanup')
