import { inject, provide, shallowReactive, type InjectionKey } from 'vue'

export type BattleDockLane = 'tools' | 'context' | 'primary' | 'utility'
type BattleDock = Partial<Record<BattleDockLane, HTMLElement | null>>
const key: InjectionKey<BattleDock> = Symbol('mobile-battle-dock')

/** Targets belong to one battle instance. Desktop has no targets and keeps its DOM. */
export function provideMobileBattleDock() {
  const dock = inject(key, null) ?? shallowReactive<BattleDock>({})
  provide(key, dock)
  return dock
}
export function useMobileBattleDock() { return inject(key, null) }
