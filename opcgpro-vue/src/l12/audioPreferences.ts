import { reactive } from 'vue'
import { useAudioStore } from '@/store/audioStore'

export interface L12AudioPreferences {
  musicEnabled: boolean
  musicVolume: number
  sfxEnabled: boolean
  sfxVolume: number
  cardSize: 'auto' | 'small' | 'medium' | 'large'
  animation: 'off' | 'fast' | 'standard'
}

const stored = (() => {
  try {
    const value = JSON.parse(localStorage.getItem('l12-audio-preferences-v1') || '{}') as unknown
    return value && typeof value === 'object' ? value as Partial<L12AudioPreferences> : {}
  } catch { return {} }
})()
const storedVolume = (value: unknown, fallback: number) => typeof value === 'number' && Number.isFinite(value)
  ? Math.max(0, Math.min(1, value)) : fallback
const isCardSize = (value: unknown): value is L12AudioPreferences['cardSize'] => typeof value === 'string'
  && ['auto', 'small', 'medium', 'large'].includes(value)
const isAnimation = (value: unknown): value is L12AudioPreferences['animation'] => typeof value === 'string'
  && ['off', 'fast', 'standard'].includes(value)

export const audioPreferences = reactive<L12AudioPreferences>({
  musicEnabled: typeof stored.musicEnabled === 'boolean' ? stored.musicEnabled : true,
  musicVolume: storedVolume(stored.musicVolume, .35),
  sfxEnabled: typeof stored.sfxEnabled === 'boolean' ? stored.sfxEnabled : true,
  sfxVolume: storedVolume(stored.sfxVolume, .7),
  cardSize: isCardSize(stored.cardSize) ? stored.cardSize : 'auto',
  animation: isAnimation(stored.animation) ? stored.animation : 'standard',
})

export function applyAudioPreferences(value?: Partial<L12AudioPreferences> | null) {
  if (!value) return
  audioPreferences.musicEnabled = value.musicEnabled ?? audioPreferences.musicEnabled
  audioPreferences.musicVolume = Math.max(0, Math.min(1, value.musicVolume ?? audioPreferences.musicVolume))
  audioPreferences.sfxEnabled = value.sfxEnabled ?? audioPreferences.sfxEnabled
  audioPreferences.sfxVolume = Math.max(0, Math.min(1, value.sfxVolume ?? audioPreferences.sfxVolume))
  audioPreferences.cardSize = value.cardSize && ['auto', 'small', 'medium', 'large'].includes(value.cardSize)
    ? value.cardSize : audioPreferences.cardSize
  audioPreferences.animation = value.animation && ['off', 'fast', 'standard'].includes(value.animation)
    ? value.animation : audioPreferences.animation
  syncAudioStore()
}

export function syncAudioStore() {
  const store = useAudioStore.getState()
  store.setBgmVolume(audioPreferences.musicEnabled ? l12MusicOutputVolume() : 0)
  store.setSfxVolume(audioPreferences.sfxEnabled ? audioPreferences.sfxVolume : 0)
  try { localStorage.setItem('l12-audio-preferences-v1', JSON.stringify(audioPreferences)) } catch { /* Keep live settings even when storage is unavailable. */ }
  if (typeof document !== 'undefined') {
    document.documentElement.dataset.l12CardSize = audioPreferences.cardSize
    document.documentElement.dataset.l12Animation = audioPreferences.animation
    const animationScale = audioPreferences.animation === 'off' ? '0' : audioPreferences.animation === 'fast' ? '.55' : '1'
    document.documentElement.style.setProperty('--l12-animation-scale', animationScale)
    document.documentElement.style.setProperty('--l12-card-scale', ({ small: '.86', medium: '1', large: '1.16', auto: '1' } as const)[audioPreferences.cardSize])
  }
}

export function l12MusicOutputVolume(value = audioPreferences.musicVolume) {
  const normalized = Math.max(0, Math.min(1, Number.isFinite(value) ? value : 0))
  return normalized * normalized
}

export function l12AnimationDuration(standardMs: number, minimumMs = 0) {
  const scale = audioPreferences.animation === 'off' ? 0 : audioPreferences.animation === 'fast' ? .55 : 1
  return Math.max(minimumMs, Math.round(standardMs * scale))
}

syncAudioStore()
