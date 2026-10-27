<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import { adminApi, hasPermission, platformState, type AdminAccountPage, type PlatformAccount } from '@/l12/platform'

const props = withDefaults(defineProps<{
  modelValue: string
  label?: string
  placeholder?: string
}>(), {
  label: '玩家账号',
  placeholder: '输入昵称、用户名或玩家 ID',
})
const emit = defineEmits<{
  'update:modelValue': [accountId: string]
  selected: [account: PlatformAccount | null]
  change: [account: PlatformAccount]
}>()
const pickerId = useId()
const listboxId = `admin-account-options-${pickerId}`

const accounts = ref<PlatformAccount[]>([])
const query = ref('')
const open = ref(false)
const loading = ref(false)
const error = ref('')
const activeIndex = ref(-1)
const candidatesCurrent = ref(false)
let requestGeneration = 0
let searchTimer: number | null = null
let blurTimer: number | null = null
const PAGE_SIZE = 20
const SEARCH_DEBOUNCE_MS = 220

const selectedAccount = computed(() => accounts.value.find(account => account.id === props.modelValue) ?? null)
const accessScope = computed(() => `${platformState.account?.id || ''}\u0000${platformState.token || ''}\u0000${hasPermission('admin.accounts.read') ? '1' : '0'}`)
const candidates = computed(() => accounts.value.slice(0, PAGE_SIZE))
const selectableCandidates = computed(() => candidatesCurrent.value
  ? candidates.value.filter(account => !account.disabled && !account.deleted)
  : [])
const activeAccount = computed(() => activeIndex.value >= 0 ? selectableCandidates.value[activeIndex.value] : undefined)

function accountStatus(account: PlatformAccount) {
  if (account.deleted) return '已删除，不可选择'
  if (account.disabled) return '已禁用，不可选择'
  return '可选择'
}
function optionId(account: PlatformAccount) { return `admin-account-option-${pickerId}-${account.id}` }
function syncSelected() {
  const selected = selectedAccount.value
  if (selected) query.value = selected.username
  emit('selected', selected)
}
function validatedItems(result: AdminAccountPage | null | undefined, deleted: boolean) {
  if (!result || !Array.isArray(result.items)) throw new Error('账号候选分页响应无效')
  const integers = [result.page, result.pageSize, result.total, result.activeCount, result.deletedCount]
  if (integers.some(value => !Number.isSafeInteger(value) || value < 0)
    || result.page !== 1 || result.pageSize !== PAGE_SIZE || result.items.length > PAGE_SIZE
    || result.total < result.items.length || result.items.some(account => Boolean(account.deleted) !== deleted))
    throw new Error('账号候选分页响应无效')
  return result.items
}
function clearSearchTimer() {
  if (searchTimer === null) return
  window.clearTimeout(searchTimer)
  searchTimer = null
}
function retireRequests() {
  clearSearchTimer()
  if (blurTimer !== null) { window.clearTimeout(blurTimer); blurTimer = null }
  requestGeneration += 1
  candidatesCurrent.value = false
  activeIndex.value = -1
  loading.value = false
}
async function loadAccounts(search = query.value.trim()) {
  const generation = ++requestGeneration
  error.value = ''
  candidatesCurrent.value = false
  activeIndex.value = -1
  if (!hasPermission('admin.accounts.read')) {
    error.value = '当前管理员没有读取玩家账号候选的权限'
    loading.value = false
    emit('selected', null)
    return
  }
  loading.value = true
  const scope = accessScope.value
  try {
    const activeRequest = adminApi.accountsPage({ page: 1, pageSize: PAGE_SIZE, search, deleted: false })
    const deletedRequest = search
      ? adminApi.accountsPage({ page: 1, pageSize: PAGE_SIZE, search, deleted: true })
      : Promise.resolve<AdminAccountPage | null>(null)
    const [activePage, deletedPage] = await Promise.all([activeRequest, deletedRequest])
    if (generation !== requestGeneration || scope !== accessScope.value) return
    const rows = [...validatedItems(activePage, false), ...(deletedPage ? validatedItems(deletedPage, true) : [])]
    accounts.value = [...new Map(rows.map(account => [account.id, account])).values()]
    if (props.modelValue && selectedAccount.value) query.value = selectedAccount.value.username
    candidatesCurrent.value = true
    activeIndex.value = selectableCandidates.value.length ? 0 : -1
    syncSelected()
  } catch (cause) {
    if (generation !== requestGeneration || scope !== accessScope.value) return
    error.value = cause instanceof Error ? cause.message : '玩家账号候选读取失败'
  } finally {
    if (generation === requestGeneration) loading.value = false
  }
}
function scheduleSearch() {
  clearSearchTimer()
  searchTimer = window.setTimeout(() => {
    searchTimer = null
    void loadAccounts(query.value.trim())
  }, SEARCH_DEBOUNCE_MS)
}
function onInput(event: Event) {
  requestGeneration += 1
  query.value = (event.target as HTMLInputElement).value
  open.value = true
  error.value = ''
  candidatesCurrent.value = false
  activeIndex.value = -1
  loading.value = hasPermission('admin.accounts.read')
  if (props.modelValue) {
    emit('update:modelValue', '')
    emit('selected', null)
  }
  scheduleSearch()
}
function choose(account: PlatformAccount) {
  if (loading.value || error.value || !selectableCandidates.value.includes(account)) return
  query.value = account.username
  open.value = false
  activeIndex.value = -1
  emit('update:modelValue', account.id)
  emit('selected', account)
  emit('change', account)
}
function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') { open.value = false; activeIndex.value = -1; return }
  const choices = selectableCandidates.value
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    open.value = true
    if (!choices.length) { activeIndex.value = -1; return }
    const delta = event.key === 'ArrowDown' ? 1 : -1
    activeIndex.value = activeIndex.value < 0
      ? (delta > 0 ? 0 : choices.length - 1)
      : (activeIndex.value + delta + choices.length) % choices.length
    return
  }
  if (event.key === 'Enter' && open.value && activeIndex.value >= 0) {
    event.preventDefault()
    const account = choices[activeIndex.value]
    if (account) choose(account)
  }
}
function onFocus() {
  if (blurTimer !== null) { window.clearTimeout(blurTimer); blurTimer = null }
  open.value = true
  activeIndex.value = selectableCandidates.value.length ? 0 : -1
}
function onBlur() {
  if (blurTimer !== null) window.clearTimeout(blurTimer)
  blurTimer = window.setTimeout(() => { blurTimer = null; open.value = false }, 100)
}

watch(() => props.modelValue, (value, previous) => {
  if (value === previous) return
  if (!value) { syncSelected(); return }
  if (selectedAccount.value) { syncSelected(); return }
  query.value = value
  void loadAccounts(value)
}, { flush: 'sync' })
watch(accessScope, (next, previous) => {
  if (next === previous) return
  retireRequests()
  accounts.value = []
  error.value = ''
  activeIndex.value = -1
  const nextActor = platformState.account?.id || ''
  const previousActor = previous.split('\u0000', 1)[0]
  if (nextActor !== previousActor) {
    query.value = ''
    if (props.modelValue) emit('update:modelValue', '')
    emit('selected', null)
  }
  if (!hasPermission('admin.accounts.read')) {
    error.value = '当前管理员没有读取玩家账号候选的权限'
    return
  }
  void loadAccounts(nextActor === previousActor ? props.modelValue || query.value.trim() : '')
}, { flush: 'sync' })
onMounted(() => { void loadAccounts(props.modelValue || '') })
onBeforeUnmount(retireRequests)
</script>

<template>
  <label class="admin-account-picker">
    <span>{{ label }}</span>
    <input
      :value="query"
      :placeholder="placeholder"
      maxlength="200"
      role="combobox"
      autocomplete="off"
      aria-autocomplete="list"
      :aria-controls="listboxId"
      :aria-expanded="open"
      :aria-activedescendant="open && activeAccount ? optionId(activeAccount) : undefined"
      @input="onInput"
      @focus="onFocus"
      @blur="onBlur"
      @keydown="onKeydown"
    >
    <div v-if="open" :id="listboxId" class="admin-account-options" role="listbox">
      <p v-if="loading">正在读取玩家账号…</p>
      <p v-else-if="error" class="account-picker-error" role="alert">{{ error }}</p>
      <template v-else-if="candidates.length">
        <button
          v-for="account in candidates"
          :id="optionId(account)"
          :key="account.id"
          type="button"
          role="option"
          :aria-selected="account.id === modelValue"
          :disabled="account.disabled || account.deleted"
          :class="{ active: activeAccount?.id === account.id }"
          @mousedown.prevent
          @click="choose(account)"
        >
          <span><b>{{ account.username }}</b><em>{{ accountStatus(account) }}</em></span>
          <code>{{ account.id }}</code>
        </button>
      </template>
      <p v-else>没有匹配的玩家账号</p>
    </div>
    <small v-if="selectedAccount">已选择唯一玩家 ID：<code>{{ selectedAccount.id }}</code></small>
  </label>
</template>

<style scoped>
.admin-account-picker{position:relative;display:grid;min-width:0;gap:5px;color:#c4ccce;font-size:13px;font-weight:800}.admin-account-picker>input{box-sizing:border-box;width:100%;min-width:0;min-height:38px;padding:8px 10px;border:1px solid #4a5860;background:#070d12;color:#fff;font:700 14px 'Microsoft YaHei','Microsoft JhengHei',sans-serif}.admin-account-picker>small{overflow-wrap:anywhere;color:#87949a;font-size:12px;line-height:1.5}.admin-account-picker>small code{font:inherit;color:#63cbd1}.admin-account-options{position:absolute;z-index:60;top:calc(100% - 20px);left:0;display:grid;width:100%;max-height:min(320px,42dvh);overflow:auto;border:1px solid #5a6870;background:#080e13;box-shadow:0 14px 34px #000b}.admin-account-options>button{display:grid;min-height:58px;padding:9px 10px;border:0;border-bottom:1px solid #28343a;background:#0e171e;color:#fff;text-align:left}.admin-account-options>button:hover,.admin-account-options>button.active{background:#19252d;box-shadow:inset 3px 0 #d4b65d}.admin-account-options>button:disabled{cursor:not-allowed;opacity:.58}.admin-account-options span{display:flex;align-items:center;justify-content:space-between;gap:8px}.admin-account-options b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.admin-account-options em{flex:none;color:#7fc7a0;font-size:12px;font-style:normal}.admin-account-options button:disabled em{color:#d09292}.admin-account-options code{margin-top:4px;overflow-wrap:anywhere;color:#8f9da3;font:12px/1.4 'Microsoft YaHei','Microsoft JhengHei',sans-serif}.admin-account-options>p{margin:0;padding:12px;color:#93a0a6;font-size:13px}.admin-account-options>.account-picker-error{color:#ef9ca5}@media(max-width:760px){.admin-account-options{position:fixed;z-index:3700;right:8px;bottom:max(8px,env(safe-area-inset-bottom));left:8px;top:auto;width:auto;max-height:min(46dvh,340px);border-color:#75642f}.admin-account-options>button{min-height:64px}}
</style>
