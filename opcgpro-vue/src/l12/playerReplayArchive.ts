import { platformRequest } from './platform'
import { ReplayArchiveLoader, type ReplayArchivePage } from './replayArchiveLoader'
import { exportReplayPayload, type MatchSummary } from './replayModel'

export interface PlayerReplayPage extends ReplayArchivePage {
  match: MatchSummary
  nextCursor: string | null
  limit: number
  pageBytes: number
  totalCommands: number
  viewerPlayerIndex: 0 | 1
}

const maximumPageBytes = 4 * 1024 * 1024
const pageFields = ['items', 'limit', 'match', 'nextCursor', 'pageBytes', 'totalCommands', 'viewerPlayerIndex']
const cancelled = () => new DOMException('Replay read cancelled', 'AbortError')
function current(signal: AbortSignal) { if (signal.aborted) throw cancelled() }
function invalid(): never {
  throw Object.assign(new Error('回放数据不完整，请返回对局记录重新选择'), { code: 'invalid_replay_page' })
}

// This adapter is only for the account-owned route. Read-only rendering grants no archive privilege.
export function createPlayerReplayReader(matchId: string) {
  let identity: string | undefined
  return async (cursor: string | undefined, signal: AbortSignal): Promise<PlayerReplayPage> => {
    current(signal)
    const query = new URLSearchParams({ limit: '50' })
    if (cursor !== undefined) query.set('cursor', cursor)
    const page = await platformRequest<PlayerReplayPage>(
      `/api/matches/${encodeURIComponent(matchId)}/replay?${query}`, {
        signal, cache: 'no-store', reliability: { maxAttempts: 1 },
      })
    current(signal)
    if (!page || typeof page !== 'object' || Object.keys(page).sort().join('|') !== pageFields.join('|')
      || !page.match || page.match.matchId !== matchId || page.match.deck0 !== '' || page.match.deck1 !== ''
      || !page.match.endedUtc || ![0, 1].includes(page.viewerPlayerIndex)
      || !Number.isSafeInteger(page.totalCommands) || page.totalCommands < 1
      || page.match.commandCount !== page.totalCommands
      || !Number.isSafeInteger(page.limit) || page.limit < 1 || page.limit > 100
      || !Array.isArray(page.items) || page.items.length < 1 || page.items.length > page.limit
      || !Number.isSafeInteger(page.pageBytes) || page.pageBytes < 2 || page.pageBytes > maximumPageBytes
      || (page.nextCursor !== null && (typeof page.nextCursor !== 'string' || !page.nextCursor || page.nextCursor === cursor))
      || new TextEncoder().encode(JSON.stringify(page)).length > maximumPageBytes) invalid()
    for (const item of page.items) {
      if (!item || !item.state || typeof item.state !== 'object' || Array.isArray(item.state)
        || (item.state.MatchId ?? item.state.matchId) !== matchId) invalid()
    }
    const incomingIdentity = JSON.stringify([page.match, page.totalCommands, page.viewerPlayerIndex])
    if (identity !== undefined && incomingIdentity !== identity) invalid()
    identity = incomingIdentity
    current(signal)
    return page
  }
}

// Only serialized page fragments survive eviction. The final Blob still costs the total JSON size.
export async function exportPlayerReplayBlob(matchId: string, signal: AbortSignal) {
  const read = createPlayerReplayReader(matchId)
  const first = await read(undefined, signal)
  current(signal)
  const envelope = exportReplayPayload({ match: first.match, commands: [], viewerPlayerIndex: first.viewerPlayerIndex })
  const { detail, ...header } = envelope
  const parts: BlobPart[] = [JSON.stringify(header).slice(0, -1), ',"detail":{"match":',
    JSON.stringify(detail.match), ',"commands":[']
  let count = 0
  const loader = new ReplayArchiveLoader(read, {
    matchId, maxAttempts: 1,
    onPage: commands => {
      current(signal)
      const projected = exportReplayPayload({ match: first.match, commands,
        viewerPlayerIndex: first.viewerPlayerIndex }).detail.commands
      if (count > 0) parts.push(',')
      parts.push(JSON.stringify(projected).slice(1, -1))
      count += projected.length
    },
  })
  const abort = () => { loader.dispose(); parts.length = 0 }
  signal.addEventListener('abort', abort, { once: true })
  try {
    current(signal)
    loader.seed(first)
    while (loader.hasMore) {
      current(signal)
      if (!await loader.loadMore()) {
        current(signal)
        throw new Error(loader.issue?.message || '保存回放失败，请重新保存')
      }
      loader.focus(loader.indexedSteps - 1)
    }
    current(signal)
    if (count !== first.totalCommands) invalid()
    parts.push('],"viewerPlayerIndex":', JSON.stringify(first.viewerPlayerIndex), '}}')
    return { blob: new Blob(parts, { type: 'application/json' }), match: first.match }
  } finally {
    signal.removeEventListener('abort', abort)
    loader.dispose()
    parts.length = 0
  }
}
