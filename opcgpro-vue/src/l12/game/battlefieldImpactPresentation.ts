export type BattlefieldImpactScope = 'slot' | 'half' | 'board'

export type BattlefieldImpactFollower = {
  element: HTMLElement
  scale?: number
}

export type BattlefieldImpactRun = {
  scopes: BattlefieldImpactScope[]
  animations: Animation[]
  cancel: () => void
}

export type BattlefieldImpactInput = {
  root: HTMLElement
  anchor?: Element | null
  playerIndex?: number
  strength: number
  direction: -1 | 1
  duration: number
  contactOffset: number
  easing?: string
  transactionKey: string
  followers?: BattlefieldImpactFollower[]
  disabled?: boolean
}

const EMPTY_RUN: BattlefieldImpactRun = { scopes: [], animations: [], cancel: () => {} }

export function battlefieldImpactStrengthFromTroops(value: unknown) {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) return 0
  if (value < 3000) return .35
  if (value < 6000) return .65
  return 1
}

export function battlefieldImpactScopes(strength: number): BattlefieldImpactScope[] {
  if (!Number.isFinite(strength) || strength <= 0) return []
  if (strength < .5) return ['slot']
  if (strength < .85) return ['slot', 'half']
  return ['slot', 'half', 'board']
}

function clamp(value: number, minimum: number, maximum: number) {
  return Math.min(maximum, Math.max(minimum, value))
}

function surfaceFrames(amplitude: number, direction: -1 | 1, contactOffset: number): Keyframe[] {
  const contact = clamp(contactOffset, .08, .88)
  const settle = Math.min(.96, contact + .08)
  return [
    { translate: '0px 0px', offset: 0 },
    { translate: '0px 0px', offset: Math.max(.01, contact - .02) },
    { translate: `${-amplitude * .34}px ${direction * amplitude}px`, offset: contact },
    { translate: `${amplitude * .18}px ${direction * -amplitude * .34}px`, offset: settle },
    { translate: '0px 0px', offset: 1 },
  ]
}

function waveFrames(strength: number, contactOffset: number): Keyframe[] {
  const contact = clamp(contactOffset, .08, .88)
  return [
    { opacity: 0, transform: 'scale(.58)', offset: 0 },
    { opacity: 0, transform: 'scale(.58)', offset: Math.max(.01, contact - .03) },
    { opacity: .14 + .24 * strength, transform: 'scale(.76)', offset: contact },
    { opacity: 0, transform: `scale(${1.06 + .28 * strength})`, offset: 1 },
  ]
}

function impactHalf(input: BattlefieldImpactInput) {
  const fromAnchor = input.anchor?.closest<HTMLElement>('.battlefield-half')
  if (fromAnchor) return fromAnchor
  if (input.playerIndex === undefined || !Number.isInteger(input.playerIndex)) return null
  const escaped = typeof CSS !== 'undefined' && typeof CSS.escape === 'function'
    ? CSS.escape(String(input.playerIndex)) : String(input.playerIndex)
  const candidate = input.root.querySelector(`[data-player-index="${escaped}"]`)
  return candidate instanceof HTMLElement ? candidate : null
}

function impactSurfaces(input: BattlefieldImpactInput) {
  const planned = battlefieldImpactScopes(input.strength)
  const slot = input.anchor?.closest<HTMLElement>('.formation-slot') ?? null
  const half = impactHalf(input)
  if (!half || !input.root.contains(half) || (input.anchor && !half.contains(input.anchor))) return []
  const entries: Array<[BattlefieldImpactScope, HTMLElement]> = []
  for (const scope of planned) {
    const element = scope === 'slot' ? (slot && half.contains(slot) ? slot : null) : scope === 'half' ? half : input.root
    if (element && !entries.some(([, existing]) => existing === element)) entries.push([scope, element])
  }
  return entries
}

function appendWave(surface: HTMLElement, scope: BattlefieldImpactScope, transactionKey: string) {
  const wave = surface.ownerDocument.createElement('span')
  wave.dataset.l12BattlefieldImpact = scope
  wave.dataset.l12BattlefieldImpactTransaction = transactionKey
  wave.setAttribute('aria-hidden', 'true')
  const inset = scope === 'slot' ? '-9%' : scope === 'half' ? '5%' : '2%'
  Object.assign(wave.style, {
    position: 'absolute', inset, zIndex: '14', pointerEvents: 'none', opacity: '0',
    border: '1px solid rgba(224,187,102,.56)', borderRadius: scope === 'slot' ? '20%' : '8px',
    background: 'radial-gradient(ellipse at center,rgba(238,207,128,.18),rgba(107,72,35,.08) 46%,transparent 72%)',
    boxShadow: 'inset 0 0 22px rgba(222,172,84,.16),0 0 13px rgba(0,0,0,.4)',
    transform: 'scale(.58)', transformOrigin: 'center', willChange: 'transform,opacity',
  })
  surface.appendChild(wave)
  return wave
}

export function startBattlefieldImpact(input: BattlefieldImpactInput): BattlefieldImpactRun {
  if (input.disabled || input.duration <= 0 || input.root.isConnected === false
      || !('l12GameBoard' in input.root.dataset)) return EMPTY_RUN
  const surfaces = impactSurfaces(input)
  if (!surfaces.length) return EMPTY_RUN
  const strength = clamp(input.strength, 0, 1)
  const animations: Animation[] = []
  const waves: HTMLElement[] = []
  let cancelled = false
  const cancel = () => {
    if (cancelled) return
    cancelled = true
    // Optional scene paint must never prevent the original card owner's cleanup.
    for (const animation of animations) { try { animation.cancel() } catch { /* continue cleanup */ } }
    for (const wave of waves) { try { wave.remove() } catch { /* continue cleanup */ } }
  }
  const easing = input.easing ?? 'linear'
  let combinedAmplitude = 0
  const amplitudes: Record<BattlefieldImpactScope, number> = {
    slot: 1.8 + 3.2 * strength,
    half: 1.1 + 2.1 * strength,
    board: .6 + 1.3 * strength,
  }
  try {
    for (const [scope, surface] of surfaces) {
      const amplitude = amplitudes[scope]
      combinedAmplitude += amplitude
      const surfaceAnimation = surface.animate(surfaceFrames(amplitude, input.direction, input.contactOffset), {
        duration: input.duration, easing, composite: 'add',
      })
      animations.push(surfaceAnimation)
      const wave = appendWave(surface, scope, input.transactionKey)
      waves.push(wave)
      const waveAnimation = wave.animate(waveFrames(strength, input.contactOffset), {
        duration: input.duration, easing,
      })
      animations.push(waveAnimation)
      const remove = () => wave.remove()
      waveAnimation.addEventListener('finish', remove, { once: true })
      waveAnimation.addEventListener('cancel', remove, { once: true })
    }
    for (const follower of input.followers ?? []) {
      const scale = typeof follower.scale === 'number' && Number.isFinite(follower.scale) ? follower.scale : 1
      animations.push(follower.element.animate(
        surfaceFrames(combinedAmplitude * scale, input.direction, input.contactOffset),
        { duration: input.duration, easing, composite: 'add' },
      ))
    }
  } catch {
    cancel()
    return EMPTY_RUN
  }
  return {
    scopes: surfaces.map(([scope]) => scope),
    animations,
    cancel,
  }
}
