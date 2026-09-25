<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { adminApi, type AtomicCardEffect, type AtomicCoverage, type EffectAtomDescriptor } from '@/l12/platform'
import CardImage from '@/l12/CardImage.vue'
import { cardTypeLabel } from '@/l12/cardPresentation'
import AdminEffectWorkbenchPanel from './AdminEffectWorkbenchPanel.vue'

const route = useRoute(); const router = useRouter()
const cards = ref<AtomicCardEffect[]>([]); const atoms = ref<EffectAtomDescriptor[]>([]); const coverage = ref<AtomicCoverage | null>(null)
const selected = ref<AtomicCardEffect | null>(null); const search = ref(''); const status = ref(''); const product = ref(''); const atomKind = ref('')
const page = ref(1); const total = ref(0); const loading = ref(false); const notice = ref('')
const requestedCardId = () => typeof route.params.cardId === 'string' ? route.params.cardId : ''
const reviewLabel = (value: string) => ({ 'human-assisted': '人工辅助', confirmed: '人工确认', rejected: '退回修正', unreviewed: '待审查' } as Record<string,string>)[value] || value

async function load(reset = false) {
  if (reset) page.value = 1
  loading.value = true
  try {
    const [result, descriptors] = await Promise.all([
      adminApi.effects({ search: search.value || requestedCardId(), status: status.value, product: product.value, atomKind: atomKind.value, page: page.value, pageSize: 50 }),
      atoms.value.length ? Promise.resolve(atoms.value) : adminApi.effectAtoms(),
    ])
    cards.value = result.items; total.value = result.total; coverage.value = result.coverage; atoms.value = descriptors
    const id = requestedCardId()
    if (id && selected.value?.cardId !== id) {
      const summary = result.items.find(item => item.cardId === id)
      if (summary) await select(summary, false)
      else notice.value = `当前筛选结果中未找到 ${id}`
    }
  } catch (cause) { notice.value = cause instanceof Error ? cause.message : '卡效清单加载失败' }
  finally { loading.value = false }
}
async function select(card: AtomicCardEffect, navigate = true) {
  try {
    selected.value = await adminApi.effect(card.cardId)
    if (navigate && requestedCardId() !== card.cardId) await router.push(`/admin/content/effects/${encodeURIComponent(card.cardId)}`)
  } catch (cause) { notice.value = cause instanceof Error ? cause.message : '卡效详情加载失败' }
}
function previous() { if (page.value > 1) { page.value--; void load() } }
function next() { if (page.value * 50 < total.value) { page.value++; void load() } }
watch(() => route.params.cardId, () => { void load(true) })
onMounted(() => { void load() })
</script>

<template>
  <section class="effects-page">
    <header><div><h2>卡效原子化与发布工作台</h2><p>列表、详情和发布状态使用同一权威卡效资源，不建立第二套工作台。</p></div><button :disabled="loading" @click="load()">刷新</button></header>
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <section v-if="coverage" class="coverage"><article><small>卡牌</small><b>{{ coverage.totalCards }}</b></article><article><small>能力</small><b>{{ coverage.totalAbilities }}</b></article><article><small>原子</small><b>{{ coverage.totalAtoms }}</b></article><article><small>已验证</small><b>{{ coverage.verifiedAbilities }}</b></article></section>
    <div class="layout"><section class="list panel"><div class="filters"><input v-model="search" placeholder="卡号 / 卡名 / 原文" @keyup.enter="load(true)"><select v-model="product" @change="load(true)"><option value="">全部卡池</option><option value="S01">S01</option><option value="S02">S02</option></select><select v-model="status" @change="load(true)"><option value="">全部状态</option><option value="verified">实战已验证</option><option value="legacy-backed">旧实现兜底</option><option value="partially-atomized">部分原子化</option><option value="declarative-ready">声明就绪</option></select><select v-model="atomKind" @change="load(true)"><option value="">全部原子</option><option v-for="atom in atoms" :key="atom.kind" :value="atom.kind">{{ atom.label }}</option></select></div><div class="effect-scroll" tabindex="0"><button v-for="card in cards" :key="card.cardId" class="card-row" :aria-current="selected?.cardId === card.cardId ? 'true' : undefined" @click="select(card)"><CardImage :card-id="card.cardId" :legacy-url="card.imageUrl" :alt="card.name" intent="thumb" fit="cover"/><span><code>{{ card.cardId }}</code><b>{{ card.name }}</b><small>{{ card.faction }} · {{ cardTypeLabel(card.cardType, card.isCounterTactic) }}</small><small>{{ reviewLabel(card.reviewStatus) }} · {{ card.migrationStatus === 'verified' ? '实战已验证' : card.migrationStatus === 'legacy-backed' ? '旧实现兜底' : card.migrationStatus }}</small></span><em>{{ card.abilities.length }} 能力</em></button></div><footer><button :disabled="page <= 1" @click="previous">上一页</button><span>第 {{ page }} 页 · 共 {{ total }} 张</span><button :disabled="page * 50 >= total" @click="next">下一页</button></footer></section><section class="detail panel"><template v-if="selected"><section class="atomic-source"><header><h3>{{ selected.name }}</h3><span>{{ cardTypeLabel(selected.cardType, selected.isCounterTactic) }}</span></header><p class="l12-effect-body">{{ selected.effectText }}</p><article v-for="ability in selected.abilities" :key="ability.abilityId"><header><b>ABILITY {{ ability.sequence }}</b><span>{{ ability.migrationStatus }}</span></header><span v-if="ability.costText" class="l12-effect-body">{{ ability.costText }}</span><span class="l12-effect-body">{{ ability.resolutionText }}</span><div class="atom-flow"><code v-for="atom in ability.atoms" :key="atom.atomId">{{ atom.kind }}</code></div><p v-if="ability.hasLegacyFallback">旧实现兜底仍保留；新旧实现不会同时结算。</p><details><summary>原子定义 JSON</summary><pre>{{ JSON.stringify(ability.atoms, null, 2) }}</pre></details></article></section><AdminEffectWorkbenchPanel :effect="selected" @notice="notice = $event"/></template><div v-else class="empty">选择一张卡牌进入详情工作台。</div></section></div>
  </section>
</template>

<style scoped>
.effects-page{display:grid;gap:14px}.effects-page>header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}.effects-page h2,.effects-page h3{margin:0 0 6px}.effects-page p,.effects-page small{color:#89979d}.effects-page button,.effects-page input,.effects-page select{min-height:38px;padding:7px 10px;border:1px solid #4b5961;background:#080e13;color:#fff}.coverage{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}.coverage article{display:grid;gap:4px;padding:12px;border:1px solid #35424a;background:#101821}.coverage b{font-size:22px}.layout{display:grid;grid-template-columns:minmax(300px,.65fr) minmax(0,1.35fr);gap:14px}.panel{min-width:0;padding:15px;border:1px solid #35424a;background:#0e161d}.filters{display:grid;grid-template-columns:1fr 1fr;gap:7px;margin-bottom:10px}.filters input{grid-column:1/-1}.effect-scroll{max-height:70vh;overflow-y:auto}.card-row{display:grid!important;grid-template-columns:48px minmax(0,1fr) auto;align-items:center;gap:10px;width:100%;border-width:1px 0 0!important;text-align:left}.card-row[aria-current="true"]{background:#211b0e}.card-row :deep(.l12-card-image){width:48px;height:64px}.card-row span{display:grid;gap:3px}.card-row em{color:#d7bd70;font-style:normal}.list footer{display:flex;align-items:center;justify-content:space-between;gap:8px;margin-top:10px}.atomic-source{display:grid;gap:10px}.atomic-source>header,.atomic-source article>header{display:flex;justify-content:space-between;gap:10px}.atomic-source article{display:grid;gap:7px;padding:11px;border:1px solid #34434b;background:#091218}.atom-flow{display:flex;flex-wrap:wrap;gap:6px}.atom-flow code{padding:4px 7px;border:1px solid #5d522f;color:#e3c76e}.atomic-source pre{overflow:auto;padding:8px;background:#071015;color:#aeb8bc}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}.empty{padding:24px;color:#849198;text-align:center}
@media(max-width:1000px){.layout{grid-template-columns:1fr}.detail{min-height:240px}}@media(max-width:650px){.effects-page>header{flex-direction:column}.effects-page>header button{width:100%}.coverage{grid-template-columns:1fr 1fr}.filters{grid-template-columns:1fr}.filters input{grid-column:auto}.list footer{flex-wrap:wrap}}
</style>
