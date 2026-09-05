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
  try { return JSON.parse(localStorage.getItem('l12-audio-preferences-v1') || '{}') } catch { return {} }
})()

export const audioPreferences = reactive<L12AudioPreferences>({
  musicEnabled: stored.musicEnabled ?? true,
  musicVolume: stored.musicVolume ?? .35,
  sfxEnabled: stored.sfxEnabled ?? true,
  sfxVolume: stored.sfxVolume ?? .7,
  cardSize: ['small', 'medium', 'large'].includes(stored.cardSize) ? stored.cardSize : 'auto',
  animation: ['off', 'fast'].includes(stored.animation) ? stored.animation : 'standard',
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
  store.setBgmVolume(audioPreferences.musicEnabled ? audioPreferences.musicVolume : 0)
  store.setSfxVolume(audioPreferences.sfxEnabled ? audioPreferences.sfxVolume : 0)
  localStorage.setItem('l12-audio-preferences-v1', JSON.stringify(audioPreferences))
}

syncAudioStore()
