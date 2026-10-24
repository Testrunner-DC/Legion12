import { selectLegalCommand } from '../large-terminal-public/large-terminal-contract.mjs'

function activationBinding(prompt) {
  if (!prompt.activationId) return {}
  return {
    activationId: prompt.activationId,
    sourceInstanceId: prompt.sourceInstanceId,
    sourceCardId: prompt.sourceCardId,
    step: prompt.step,
    createdRevision: prompt.createdRevision,
    controller: prompt.controller,
  }
}

export function selectLegalCommandWithAllBottom(
  client, snapshot, room, blockedByVisibleDisaster = () => false,
) {
  const selected = selectLegalCommand(client, snapshot, room, blockedByVisibleDisaster)
  if (selected?.skip !== 'unsupported_authoritative_prompt') return selected

  const prompts = Array.isArray(snapshot?.prompts) ? snapshot.prompts : []
  const ownPrompts = prompts.filter(prompt => prompt?.playerIndex === client?.playerIndex)
  if (ownPrompts.length !== 1) return selected
  const prompt = ownPrompts[0]
  const choices = prompt?.validChoices
  const minimum = prompt?.minChoose
  const maximum = prompt?.maxChoose
  if (prompt?.kind !== 'order' || prompt?.data?.placementMode !== 'all-bottom'
      || typeof prompt?.promptId !== 'string' || prompt.promptId.length === 0
      || !Array.isArray(choices) || choices.length === 0
      || choices.some(choice => typeof choice !== 'string' || choice.length === 0)
      || new Set(choices).size !== choices.length
      || !Number.isInteger(minimum) || !Number.isInteger(maximum)
      || minimum !== choices.length || maximum !== choices.length) return selected

  return {
    command: {
      type: 'resolvePrompt',
      promptId: prompt.promptId,
      topCardInstanceIds: [],
      bottomCardInstanceIds: choices.slice(),
      ...activationBinding(prompt),
    },
    revisionKey: `${client.playerIndex}:${snapshot.revision}:resolvePrompt:${prompt.promptId}`,
  }
}

