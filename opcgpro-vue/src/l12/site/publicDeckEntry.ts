export function preservePublicDeckDetails<T extends { details?: unknown }>(current: T | null, updated: T): T {
  return { ...updated, details: updated.details ?? current?.details }
}
