export interface MaintenanceCountdownSource {
  enabled: boolean
  message: string
  broadcastMessage: string
  startsAt?: string
  endsAt?: string
  immediateActive?: boolean
  immediateExpectedDurationHours?: number
}

export interface MaintenanceCountdownView {
  phase: 'scheduled' | 'active'
  title: string
  countdown: string
  message: string
}

function timestamp(value?: string) {
  if (!value) return null
  const parsed = Date.parse(value)
  return Number.isFinite(parsed) ? parsed : null
}

export function formatMaintenanceRemaining(milliseconds: number) {
  const totalSeconds = Math.max(0, Math.ceil(milliseconds / 1_000))
  const days = Math.floor(totalSeconds / 86_400)
  const hours = Math.floor(totalSeconds % 86_400 / 3_600)
  const minutes = Math.floor(totalSeconds % 3_600 / 60)
  const seconds = totalSeconds % 60
  const clock = [hours, minutes, seconds].map(value => String(value).padStart(2, '0')).join(':')
  return days > 0 ? `${days}天 ${clock}` : clock
}

export function maintenanceCountdown(source: MaintenanceCountdownSource, now = Date.now()): MaintenanceCountdownView | null {
  if (source.immediateActive) return {
    phase: 'active',
    title: '服务器维护中',
    countdown: '新对局已关闭 · 进行中对局可继续',
    message: source.broadcastMessage || `当前服务器维护中，预计维护时间为${source.immediateExpectedDurationHours ?? 2}小时。`,
  }
  if (!source.enabled) return null

  const startsAt = timestamp(source.startsAt)
  const endsAt = timestamp(source.endsAt)
  if (endsAt !== null && now >= endsAt) return null

  if (startsAt !== null && now < startsAt) {
    return {
      phase: 'scheduled',
      title: '维护倒计时',
      countdown: `距离维护开始 ${formatMaintenanceRemaining(startsAt - now)}`,
      message: source.broadcastMessage || source.message,
    }
  }

  return {
    phase: 'active',
    title: '服务器维护中',
    countdown: endsAt === null
      ? '维护进行中 · 结束时间待定'
      : `距离维护结束 ${formatMaintenanceRemaining(endsAt - now)}`,
    message: source.message,
  }
}
