import { computed, reactive } from 'vue'

export function useActionGate() {
  const active = reactive(new Set<string>())

  async function run<T>(key: string, action: () => Promise<T>, cooldownMs = 250): Promise<T | undefined> {
    if (active.has(key)) return undefined
    active.add(key)
    try {
      return await action()
    } finally {
      if (cooldownMs > 0) await new Promise(resolve => setTimeout(resolve, cooldownMs))
      active.delete(key)
    }
  }

  return {
    pending: computed(() => active.size > 0),
    isPending: (key: string) => active.has(key),
    run,
  }
}
