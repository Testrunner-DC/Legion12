// Presentation scenes carry a label and exact choice requirements, not BranchId.
// Retain the full tuple: a branch label alone is not its execution condition.
export function presentationBranches(scenes) {
  if (!Array.isArray(scenes)) throw new Error('Presentation scenes must be an array')
  return scenes.flatMap(scene => {
    const label = scene.BranchLabel ?? ''
    const choices = scene.RequiredChoices ?? {}
    if (typeof label !== 'string' || !choices || typeof choices !== 'object' || Array.isArray(choices))
      throw new Error('Invalid presentation branch metadata')
    const entries = Object.entries(choices).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)
    if (entries.some(([, value]) => typeof value !== 'string')) throw new Error('Branch choice values must be strings')
    if (!label && entries.length === 0) return []
    if (typeof scene.SceneId !== 'string' || !scene.SceneId) throw new Error('Branch scene identity is required')
    return [{ sceneId: scene.SceneId, branchLabel: label, requiredChoices: Object.fromEntries(entries) }]
  })
}
