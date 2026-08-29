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

/** Programmatic, project-owned short cues. No BGM or network audio asset. */
export function playL12ActionSound(kind: ActionPresentationKind) {
  const { isMuted, sfxVolume } = useAudioStore.getState()
  if (isMuted || sfxVolume <= 0) return
  const volume = Math.min(0.12, 0.12 * sfxVolume)
  switch (kind) {
    case 'phase':
      tone(392, 0.12, volume * 0.42, 'sine', 392)
      tone(587, 0.18, volume * 0.46, 'sine', 587, 0.1)
      break
  }
}
