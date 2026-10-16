import { platformState, tournamentApi, type TournamentCreateInput } from './platform'

function requireVersion(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0)
    throw new Error('赛事创建版本不可用，请稍后重试；草稿已保留')
  return value
}

// No write retry: an intervening change must still produce the original CAS conflict.
export async function createTournamentWithCurrentContext(
  buildInput: (isCurrent: () => boolean) => Promise<TournamentCreateInput> | TournamentCreateInput,
  options: { preview?: boolean; isCurrent?: () => boolean } = {},
) {
  const token = platformState.token, accountId = platformState.account?.id
  const isCurrent = () => !!token && !!accountId && platformState.token === token
    && platformState.account?.id === accountId && (options.isCurrent?.() ?? true)
  const ensureCurrent = () => { if (!isCurrent()) throw new Error('页面或账号已变化，本次创建已停止') }
  ensureCurrent()
  // A fresh authenticated one-item summary, not a full tournament directory or a public version 0.
  const context = await tournamentApi.createContext()
  ensureCurrent()
  const expectedVersion = requireVersion(context.platformVersion)
  const input = await buildInput(isCurrent)
  ensureCurrent()
  if (options.preview) {
    const preview = await tournamentApi.create(input, expectedVersion, true)
    ensureCurrent()
    if (!preview || typeof preview.id !== 'string' || !preview.id || !Number.isSafeInteger(preview.version) || preview.version <= 0)
      throw new Error('赛事预检结果不可用，请稍后重试；草稿已保留')
    // Audit-only preview does not advance the business version. Keep this exact
    // context through commit, so intervening policy/business changes still fail CAS.
  }
  const created = await tournamentApi.create(input, expectedVersion)
  ensureCurrent()
  return created
}
