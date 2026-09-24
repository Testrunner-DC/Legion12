import { computed, getCurrentScope, onScopeDispose, reactive } from 'vue'

export function useActionGate() {
  const active = reactive(new Set<string>())
  const cooldowns = new Map<ReturnType<typeof setTimeout>, () => void>()
  let disposed = false

  function waitForCooldown(milliseconds: number) {
    return new Promise<void>(resolve => {
      const finish = () => {
        cooldowns.delete(timer)
        resolve()
      }
      const timer = setTimeout(finish, milliseconds)
      cooldowns.set(timer, finish)
    })
  }

  if (getCurrentScope()) {
    onScopeDispose(() => {
      disposed = true
      for (const [timer, finish] of cooldowns) {
        clearTimeout(timer)
        finish()
      }
      active.clear()
    })
  }

  async function run<T>(key: string, action: () => Promise<T>, cooldownMs = 250): Promise<T | undefined> {
    if (disposed || active.has(key)) return undefined
    active.add(key)
    try {
      return await action()
    } finally {
      if (!disposed && cooldownMs > 0) await waitForCooldown(cooldownMs)
      active.delete(key)
    }
  }

  return {
    pending: computed(() => active.size > 0),
    isPending: (key: string) => active.has(key),
    run,
  }
}
