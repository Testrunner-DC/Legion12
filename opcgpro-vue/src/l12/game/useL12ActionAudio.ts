import { useAudioStore } from '@/store/audioStore'
import type { ActionPresentationKind } from './actionPresentation'

type AudioContextConstructor = typeof AudioContext

let context: AudioContext | null = null

function audioContextConstructor(): AudioContextConstructor | null {
  if (typeof window === 'undefined') return null
  return window.AudioContext ?? (window as typeof window & { webkitAudioContext?: AudioContextConstructor }).webkitAudioContext ?? null
}

function ensureContext() {
  if (context) return context
  const Constructor = audioContextConstructor()
  if (!Constructor) return null
  context = new Constructor()
  return context
}

export function primeL12ActionAudio() {
  const current = ensureContext()
  if (current?.state === 'suspended') void current.resume().catch(() => undefined)
}

function tone(frequency: number, duration: number, volume: number, type: OscillatorType, slideTo?: number, delay = 0) {
  const current = ensureContext()
  if (!current || current.state !== 'running') return
  const start = current.currentTime + delay
  const oscillator = current.createOscillator()
  const gain = current.createGain()
  oscillator.type = type
  oscillator.frequency.setValueAtTime(frequency, start)
  if (slideTo) oscillator.frequency.exponentialRampToValueAtTime(Math.max(25, slideTo), start + duration)
  gain.gain.setValueAtTime(0.0001, start)
  gain.gain.exponentialRampToValueAtTime(Math.max(0.0001, volume), start + 0.015)
  gain.gain.exponentialRampToValueAtTime(0.0001, start + duration)
  oscillator.connect(gain).connect(current.destination)
  oscillator.start(start)
  oscillator.stop(start + duration + 0.02)
}

function noise(duration: number, volume: number) {
  const current = ensureContext()
  if (!current || current.state !== 'running') return
  const length = Math.max(1, Math.floor(current.sampleRate * duration))
  const buffer = current.createBuffer(1, length, current.sampleRate)
  const channel = buffer.getChannelData(0)
  for (let index = 0; index < length; index++) channel[index] = Math.random() * 2 - 1
  const source = current.createBufferSource()
  const filter = current.createBiquadFilter()
  const gain = current.createGain()
  source.buffer = buffer
  filter.type = 'bandpass'
  filter.frequency.value = 880
  filter.Q.value = 0.8
  gain.gain.setValueAtTime(volume, current.currentTime)
  gain.gain.exponentialRampToValueAtTime(0.0001, current.currentTime + duration)
  source.connect(filter).connect(gain).connect(current.destination)
  source.start()
}

/** Programmatic, project-owned short cues. No BGM or network audio asset. */
export function playL12ActionSound(kind: ActionPresentationKind) {
  const { isMuted, sfxVolume } = useAudioStore.getState()
  if (isMuted || sfxVolume <= 0) return
  const volume = Math.min(0.12, 0.12 * sfxVolume)
  switch (kind) {
    case 'draw':
      noise(0.08, volume * 0.45)
      tone(520, 0.12, volume * 0.45, 'triangle', 700)
      break
    case 'play':
      tone(150, 0.15, volume, 'triangle', 90)
      tone(440, 0.12, volume * 0.45, 'sine', 620, 0.07)
      break
    case 'attack':
      noise(0.11, volume * 0.7)
      tone(620, 0.18, volume * 0.7, 'sawtooth', 170)
      break
    case 'defense':
      tone(190, 0.18, volume * 0.8, 'square', 130)
      break
    case 'support':
      tone(360, 0.12, volume * 0.55, 'sine', 520)
      tone(540, 0.14, volume * 0.45, 'sine', 720, 0.06)
      break
    case 'damage':
      noise(0.13, volume * 0.85)
      tone(120, 0.2, volume * 0.75, 'sawtooth', 55)
      break
    case 'grave':
      tone(330, 0.28, volume * 0.55, 'sine', 85)
      break
    case 'turn':
      tone(392, 0.12, volume * 0.42, 'sine', 392)
      tone(587, 0.18, volume * 0.46, 'sine', 587, 0.1)
      break
  }
}
