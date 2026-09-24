export type ChunkRecoveryDecision = 'ignore' | 'reload' | 'show-error'

export function isChunkLoadFailure(error: unknown): boolean
export function chunkRecoveryStorageKey(release: string, pathname: string): string
export function decideChunkRecovery(input: {
  error: unknown
  alreadyRecovered: boolean
}): ChunkRecoveryDecision
