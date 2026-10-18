import type { ActiveTutorialState, TutorialLessonId } from '../types'

export const TUTORIAL_PROGRESS_VERSION = 1 as const
export const tutorialLessonIds = ['deploy', 'move', 'combat'] as const satisfies readonly TutorialLessonId[]

type StoredCompletion = { matchId: string; completedAt: string }
type StoredTutorialProgress = {
  version: typeof TUTORIAL_PROGRESS_VERSION
  completed: Partial<Record<TutorialLessonId, StoredCompletion>>
}
type TutorialProgressStorage = Pick<Storage, 'getItem' | 'setItem'>

export interface TutorialCompletionBinding {
  accountId: string
  sessionId: string
  connectionGeneration: number
  matchId: string
}

function isLessonId(value: unknown): value is TutorialLessonId {
  return tutorialLessonIds.includes(value as TutorialLessonId)
}

export function tutorialProgressStorageKey(accountId: string) {
  return `l12-tutorial-progress:v1:account:${encodeURIComponent(accountId)}`
}

function emptyProgress(): StoredTutorialProgress {
  return { version: TUTORIAL_PROGRESS_VERSION, completed: {} }
}

function browserStorage(): TutorialProgressStorage | null {
  try { return window.localStorage }
  catch { return null }
}

function readProgress(accountId: string, storage: TutorialProgressStorage): StoredTutorialProgress | null {
  if (!accountId) return emptyProgress()
  try {
    const parsed = JSON.parse(storage.getItem(tutorialProgressStorageKey(accountId)) || 'null') as Partial<StoredTutorialProgress> | null
    if (!parsed || parsed.version !== TUTORIAL_PROGRESS_VERSION || !parsed.completed
      || typeof parsed.completed !== 'object' || Array.isArray(parsed.completed)) return emptyProgress()
    const completed: StoredTutorialProgress['completed'] = {}
    for (const [lessonId, value] of Object.entries(parsed.completed)) {
      if (!isLessonId(lessonId) || !value || typeof value !== 'object' || Array.isArray(value)) continue
      const record = value as Partial<StoredCompletion>
      if (typeof record.matchId !== 'string' || !record.matchId
        || typeof record.completedAt !== 'string' || !record.completedAt) continue
      completed[lessonId] = { matchId: record.matchId, completedAt: record.completedAt }
    }
    return { version: TUTORIAL_PROGRESS_VERSION, completed }
  } catch { return null }
}

export function loadTutorialProgress(accountId: string, storage?: TutorialProgressStorage) {
  try {
    const selected = storage ?? browserStorage()
    const progress = selected ? readProgress(accountId, selected) : null
    return progress ? Object.keys(progress.completed).filter(isLessonId) : []
  }
  catch { return [] as TutorialLessonId[] }
}

export function recordTutorialCompletion(projection: ActiveTutorialState,
  binding: TutorialCompletionBinding, storage?: TutorialProgressStorage,
  completedAt = new Date().toISOString()) {
  if (!binding.accountId || projection.version !== TUTORIAL_PROGRESS_VERSION
    || projection.status !== 'completed' || !isLessonId(projection.lessonId)
    || projection.accountId !== binding.accountId || projection.sessionId !== binding.sessionId
    || projection.connectionGeneration !== binding.connectionGeneration
    || !binding.matchId || projection.matchId !== binding.matchId) return false
  try {
    const selected = storage ?? browserStorage()
    if (!selected) return false
    const progress = readProgress(binding.accountId, selected)
    if (!progress) return false
    const current = progress.completed[projection.lessonId]
    if (current?.matchId === projection.matchId) return true
    progress.completed[projection.lessonId] = { matchId: projection.matchId, completedAt }
    selected.setItem(tutorialProgressStorageKey(binding.accountId), JSON.stringify(progress))
    return true
  } catch { return false }
}
