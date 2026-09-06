export interface BackgroundMusicTarget {
  src: string
  loop: boolean
  volume: number
  onEnded?: () => void
}

interface ActiveTrack {
  audio: HTMLAudioElement
  source: string
  generation: number
}

const FADE_DURATION_MS = 520
const FADE_STEP_MS = 40

/**
 * Owns all BGM transitions. A monotonically increasing generation prevents an
 * ended/fade callback from an obsolete track from changing the current route's
 * music after rapid navigation or repeated enable/disable changes.
 */
export class BackgroundMusicController {
  private active: ActiveTrack | null = null
  private generation = 0
  private fades = new Map<HTMLAudioElement, number>()

  setTarget(target: BackgroundMusicTarget | null) {
    const generation = ++this.generation
    const volume = Math.max(0, Math.min(1, target?.volume ?? 0))
    if (!target || volume <= 0) {
      const previous = this.active
      this.active = null
      if (previous) this.fade(previous.audio, 0, generation, true)
      return
    }

    if (this.active?.source === target.src) {
      this.active.generation = generation
      this.active.audio.loop = target.loop
      this.bindEnded(this.active, target.onEnded)
      this.fade(this.active.audio, volume, generation)
      void this.active.audio.play().catch(() => undefined)
      return
    }

    const previous = this.active
    const audio = new Audio(target.src)
    const next: ActiveTrack = { audio, source: target.src, generation }
    audio.loop = target.loop
    audio.volume = 0
    this.active = next
    this.bindEnded(next, target.onEnded)
    void audio.play().catch(() => undefined)
    this.fade(audio, volume, generation)
    if (previous) this.fade(previous.audio, 0, generation, true)
  }

  destroy() {
    this.generation++
    for (const [audio, timer] of this.fades) {
      window.clearInterval(timer)
      audio.pause()
    }
    this.fades.clear()
    this.active?.audio.pause()
    this.active = null
  }

  private bindEnded(track: ActiveTrack, callback?: () => void) {
    track.audio.onended = () => {
      if (this.active !== track || track.generation !== this.generation || track.audio.loop) return
      callback?.()
    }
  }

  private fade(audio: HTMLAudioElement, target: number, generation: number, retire = false) {
    const existing = this.fades.get(audio)
    if (existing !== undefined) window.clearInterval(existing)
    const from = Number.isFinite(audio.volume) ? audio.volume : 0
    const started = performance.now()
    const finish = () => {
      const timer = this.fades.get(audio)
      if (timer !== undefined) window.clearInterval(timer)
      this.fades.delete(audio)
      audio.volume = target
      if (retire) {
        audio.pause()
        audio.onended = null
      }
    }
    if (Math.abs(from - target) < .005) { finish(); return }
    const timer = window.setInterval(() => {
      const progress = Math.min(1, (performance.now() - started) / FADE_DURATION_MS)
      audio.volume = Math.max(0, Math.min(1, from + (target - from) * progress))
      if (progress >= 1 || (retire && generation < this.generation - 1)) finish()
    }, FADE_STEP_MS)
    this.fades.set(audio, timer)
  }
}
