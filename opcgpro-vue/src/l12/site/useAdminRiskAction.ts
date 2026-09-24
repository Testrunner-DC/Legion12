import { ref, shallowRef } from 'vue'

export interface AdminRiskAction {
  title: string
  target?: string
  targetLabel?: string
  impact: string
  confirmLabel?: string
  severity?: 'danger' | 'warning'
  run: () => Promise<void>
}

export function useAdminRiskAction() {
  const riskAction = shallowRef<AdminRiskAction | null>(null)
  const riskBusy = ref(false)
  const riskError = ref('')

  function requestRiskAction(action: AdminRiskAction) {
    if (riskBusy.value) return
    riskError.value = ''
    riskAction.value = action
  }

  function cancelRiskAction() {
    if (!riskBusy.value) riskAction.value = null
  }

  async function confirmRiskAction() {
    const action = riskAction.value
    if (!action || riskBusy.value) return
    riskBusy.value = true
    riskError.value = ''
    try {
      await action.run()
      if (riskAction.value === action) riskAction.value = null
    } catch (error) {
      riskError.value = error instanceof Error ? error.message : '操作失败，请重试'
    } finally {
      riskBusy.value = false
    }
  }

  return { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction }
}
