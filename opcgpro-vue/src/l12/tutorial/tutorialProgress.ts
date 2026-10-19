import type { ActiveTutorialState, TutorialLessonId } from '../types'

export const TUTORIAL_PROGRESS_VERSION = 1 as const
export const tutorialLessonIds = ['deploy', 'move', 'combat', 'response', 'preparation', 'finale'] as const satisfies readonly TutorialLessonId[]
const firstChapterIds: readonly TutorialLessonId[] = ['deploy', 'move', 'combat']

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

export interface GuidedTutorialProgress {
  version: 2
  introductionRead: boolean
  reviewedScenes: TutorialLessonId[]
  responseReviewed?: true
  responseRecipeId?: 'public-response-v1'
  preparationReviewed?: true
  finaleReviewed?: true
}

export function guidedTutorialStorageKey(accountId: string) {
  return `l12-tutorial-course:v2:account:${encodeURIComponent(accountId)}`
}
const emptyGuidedProgress = (): GuidedTutorialProgress => ({ version: 2, introductionRead: false, reviewedScenes: [] })
const guidedMemory = new Map<string, { progress: GuidedTutorialProgress; unsaved: boolean; pendingReset: boolean }>()
const copyGuidedProgress = (progress: GuidedTutorialProgress): GuidedTutorialProgress => ({
  version: 2, introductionRead: progress.introductionRead, reviewedScenes: [...progress.reviewedScenes],
  ...(progress.responseReviewed && progress.responseRecipeId === 'public-response-v1'
    ? { responseReviewed: true as const, responseRecipeId: 'public-response-v1' as const } : {}),
  ...(progress.responseReviewed && progress.responseRecipeId === 'public-response-v1' && progress.preparationReviewed
    ? { preparationReviewed: true as const, ...(progress.finaleReviewed ? { finaleReviewed: true as const } : {}) } : {}),
})
function rememberGuidedProgress(accountId: string, progress: GuidedTutorialProgress, unsaved: boolean, pendingReset = false) {
  guidedMemory.delete(accountId)
  guidedMemory.set(accountId, { progress: copyGuidedProgress(progress), unsaved, pendingReset })
  if (guidedMemory.size > 16) guidedMemory.delete(guidedMemory.keys().next().value!)
}
function mergeGuidedProgress(left: GuidedTutorialProgress, right: GuidedTutorialProgress) {
  const introductionRead = left.introductionRead || right.introductionRead
  const longest = left.reviewedScenes.length >= right.reviewedScenes.length ? left : right
  const responseReviewed = introductionRead && longest.reviewedScenes.length === firstChapterIds.length
    && ((left.responseReviewed && left.responseRecipeId === 'public-response-v1')
      || (right.responseReviewed && right.responseRecipeId === 'public-response-v1'))
  const preparationReviewed = responseReviewed && (left.preparationReviewed || right.preparationReviewed)
  return { version: 2, introductionRead,
    reviewedScenes: introductionRead ? [...longest.reviewedScenes] : [],
    ...(responseReviewed ? { responseReviewed: true as const, responseRecipeId: 'public-response-v1' as const } : {}),
    ...(preparationReviewed ? { preparationReviewed: true as const,
      ...(left.finaleReviewed || right.finaleReviewed ? { finaleReviewed: true as const } : {}) } : {}),
  } satisfies GuidedTutorialProgress
}
function guidedProgressCovers(outer: GuidedTutorialProgress, inner: GuidedTutorialProgress) {
  return (!inner.introductionRead || outer.introductionRead)
    && outer.reviewedScenes.length >= inner.reviewedScenes.length
    && (!inner.responseReviewed || outer.responseReviewed === true)
    && (!inner.preparationReviewed || outer.preparationReviewed === true)
    && (!inner.finaleReviewed || outer.finaleReviewed === true)
}
function readGuidedProgress(accountId: string, storage: TutorialProgressStorage): GuidedTutorialProgress | null {
  if (!accountId) return emptyGuidedProgress()
  try {
    const value = JSON.parse(storage.getItem(guidedTutorialStorageKey(accountId)) || 'null')
    if (!value || value.version !== 2 || typeof value.introductionRead !== 'boolean'
      || !Array.isArray(value.reviewedScenes)) return emptyGuidedProgress()
    const reviewedScenes: TutorialLessonId[] = []
    if (value.introductionRead) for (const id of value.reviewedScenes) {
      if (id !== firstChapterIds[reviewedScenes.length]) break
      reviewedScenes.push(id)
    }
    const responseReviewed = value.responseReviewed === true && value.responseRecipeId === 'public-response-v1'
      && reviewedScenes.length === firstChapterIds.length
    const preparationReviewed = responseReviewed && value.preparationReviewed === true
    return { version: 2, introductionRead: value.introductionRead, reviewedScenes,
      ...(responseReviewed ? { responseReviewed: true as const, responseRecipeId: 'public-response-v1' as const } : {}),
      ...(preparationReviewed ? { preparationReviewed: true as const,
        ...(value.finaleReviewed === true ? { finaleReviewed: true as const } : {}) } : {}) }
  } catch { return null }
}
export function loadGuidedTutorialProgress(accountId: string, storage?: TutorialProgressStorage): GuidedTutorialProgress {
  if (!accountId) return emptyGuidedProgress()
  const memory = guidedMemory.get(accountId)
  try {
    const selected = storage ?? browserStorage()
    const progress = selected && readGuidedProgress(accountId, selected)
    if (memory?.pendingReset) return copyGuidedProgress(memory.progress)
    if (progress) {
      const merged = memory?.unsaved ? mergeGuidedProgress(progress, memory.progress) : progress
      const unsaved = Boolean(memory?.unsaved && !guidedProgressCovers(progress, memory.progress))
      rememberGuidedProgress(accountId, merged, unsaved)
      return copyGuidedProgress(merged)
    }
    return copyGuidedProgress(memory?.progress || emptyGuidedProgress())
  } catch { return copyGuidedProgress(memory?.progress || emptyGuidedProgress()) }
}
export function guidedTutorialPersistenceState(accountId: string, storage?: TutorialProgressStorage) {
  if (!accountId) return { unsaved: false }
  loadGuidedTutorialProgress(accountId, storage)
  return { unsaved: guidedMemory.get(accountId)?.unsaved === true }
}
export function retryGuidedTutorialProgress(accountId: string, storage?: TutorialProgressStorage) {
  if (!accountId) return false
  const memory = guidedMemory.get(accountId)
  if (!memory?.unsaved) return true
  try {
    const selected = storage ?? browserStorage()
    const readable = selected ? readGuidedProgress(accountId, selected) : null
    if (!selected || !readable) return false
    const progress = memory.pendingReset ? copyGuidedProgress(memory.progress)
      : mergeGuidedProgress(readable, memory.progress)
    selected.setItem(guidedTutorialStorageKey(accountId), JSON.stringify(progress))
    rememberGuidedProgress(accountId, progress, false)
    return true
  } catch { return false }
}
function writeGuidedProgress(accountId: string, change: (progress: GuidedTutorialProgress) => boolean,
  storage?: TutorialProgressStorage) {
  try {
    const selected = storage ?? browserStorage()
    if (!accountId) return false
    const readable = selected ? readGuidedProgress(accountId, selected) : null
    const memory = guidedMemory.get(accountId)
    const pendingReset = memory?.pendingReset === true
    const progress = pendingReset && memory
      ? copyGuidedProgress(memory.progress)
      : readable ? mergeGuidedProgress(readable, memory?.progress || emptyGuidedProgress())
        : copyGuidedProgress(memory?.progress || emptyGuidedProgress())
    if (!change(progress)) return false
    rememberGuidedProgress(accountId, progress, true, pendingReset)
    // An inaccessible existing key is not overwritten. The current course may
    // continue in bounded account-local memory, with false reporting unsaved state.
    if (!selected || !readable) return false
    selected.setItem(guidedTutorialStorageKey(accountId), JSON.stringify(progress))
    rememberGuidedProgress(accountId, progress, false)
    return true
  } catch { return false }
}
export function markGuidedIntroductionRead(accountId: string, storage?: TutorialProgressStorage) {
  return writeGuidedProgress(accountId, progress => { progress.introductionRead = true; return true }, storage)
}
export function restartGuidedTutorial(accountId: string, storage?: TutorialProgressStorage) {
  if (!accountId) return false
  const reset = emptyGuidedProgress()
  // Remember the explicit reset before touching storage. If storage is denied,
  // a later read in this session must not resurrect its older course value.
  rememberGuidedProgress(accountId, reset, true, true)
  try {
    const selected = storage ?? browserStorage()
    const readable = selected ? readGuidedProgress(accountId, selected) : null
    if (!selected || !readable) return false
    selected.setItem(guidedTutorialStorageKey(accountId), JSON.stringify(reset))
    rememberGuidedProgress(accountId, reset, false)
    return true
  } catch { return false }
}
export function recordGuidedSceneReview(projection: ActiveTutorialState,
  binding: TutorialCompletionBinding, storage?: TutorialProgressStorage) {
  const supported = projection.lessonId === 'response'
    ? projection.version === 2 && projection.recipeId === 'public-response-v1'
    : projection.version === 1
  if (!binding.accountId || !supported || projection.status !== 'completed'
    || projection.stepId !== 'completed' || !isLessonId(projection.lessonId)
    || projection.accountId !== binding.accountId || projection.sessionId !== binding.sessionId
    || projection.connectionGeneration !== binding.connectionGeneration
    || !binding.matchId || projection.matchId !== binding.matchId) return false
  return writeGuidedProgress(binding.accountId, progress => {
    if (!progress.introductionRead) return false
    if (projection.lessonId === 'response') {
      if (progress.reviewedScenes.length !== firstChapterIds.length) return false
      progress.responseReviewed = true
      progress.responseRecipeId = 'public-response-v1'
      return true
    }
    if (projection.lessonId === 'preparation' || projection.lessonId === 'finale') {
      if (!progress.responseReviewed || progress.responseRecipeId !== 'public-response-v1') return false
      if (projection.lessonId === 'preparation') progress.preparationReviewed = true
      else {
        if (!progress.preparationReviewed) return false
        progress.finaleReviewed = true
      }
      return true
    }
    if (progress.reviewedScenes.includes(projection.lessonId)) return true
    if (firstChapterIds[progress.reviewedScenes.length] !== projection.lessonId) return false
    progress.reviewedScenes.push(projection.lessonId)
    return true
  }, storage)
}
