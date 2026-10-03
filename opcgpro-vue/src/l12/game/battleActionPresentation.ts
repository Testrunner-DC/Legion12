export type BattleActionActorState = 'self' | 'opponent' | 'neutral'

export type BattleActionActorPresentation = {
  label: string
  state: BattleActionActorState
}

export function battleActionActorPresentation(
  actorPlayerIndex: number | undefined,
  viewerPlayerIndex: number,
  neutral = false,
): BattleActionActorPresentation {
  if (neutral || actorPlayerIndex === undefined)
    return { label: '当前玩家正在选择', state: 'neutral' }
  return actorPlayerIndex === viewerPlayerIndex
    ? { label: '你的操作', state: 'self' }
    : { label: '对手操作', state: 'opponent' }
}

export function battleActionSelectionRange(minChoose: number, maxChoose: number) {
  if (maxChoose === 0) return '无需选择；请确认信息'
  if (minChoose === maxChoose) return `需选择 ${maxChoose} 项`
  return `需选择 ${minChoose} 至 ${maxChoose} 项`
}

export function battleActionSelectionStatus(selectedCount: number, maxChoose: number) {
  if (maxChoose === 0) return '无需选择，等待确认'
  return `已选择 ${selectedCount}/${maxChoose}`
}

export function battleActionDirectSubmitStatus() {
  return '点击绿色高亮格位即提交'
}
