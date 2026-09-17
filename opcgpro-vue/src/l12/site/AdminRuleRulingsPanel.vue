<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { adminApi, hasPermission } from '@/l12/platform'
import { LEGACY_FAQ_SOURCES, createRuleCenterDraft, createRulingsDraft, mergedRulings, parseRulingDocument, type RuleRuling } from '@/l12/data/ruleCenterData'

const emit = defineEmits<{ notice: [value: string] }>()
const entries = ref<RuleRuling[]>([])
const ruleCenterDraft = ref('')
const busy = ref(false)
const selectedSource = ref('')
const sourceQuery = ref('')
const sourceStates = { 'pending-review': '待梳理', replaced: '已替代', 'conflict-flagged': '发现冲突' } as const
const visibleSources = computed(() => LEGACY_FAQ_SOURCES.filter(source => !sourceQuery.value.trim() || [source.id, source.question, source.answer].join(' ').includes(sourceQuery.value.trim())))
const reviewedSources = computed(() => new Set(mergedRulings(entries.value).flatMap(item => [...item.sourceIds, ...item.supersedes])))
const reviewCount = computed(() => LEGACY_FAQ_SOURCES.filter(source => source.reviewState === 'replaced' || reviewedSources.value.has(source.id)).length)
function notice(value: string) { emit('notice', value) }
function split(value: string) { return value.split(',').map(item => item.trim()).filter(Boolean) }
function join(value: string[]) { return value.join(', ') }
function addRuling(sourceId = '') {
  const id = `RULING-${new Date().toISOString().slice(0, 10).replaceAll('-', '')}-${entries.value.length + 1}`
  entries.value.unshift({ id, scope: 'card', question: '', answer: '', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '待补充确认来源', recordedAt: new Date().toISOString().slice(0, 10), status: 'pending', cardIds: [], productIds: [], tags: [], sourceIds: sourceId ? [sourceId] : [], supersedes: sourceId ? [sourceId] : [] })
}
function startFromSource(sourceId: string) { selectedSource.value = sourceId; addRuling(sourceId) }
function removeRuling(index: number) { entries.value.splice(index, 1) }
function normalizeList(item: RuleRuling, key: 'cardIds' | 'productIds' | 'tags' | 'sourceIds' | 'supersedes', value: string) { item[key] = split(value) }
async function load() {
  busy.value = true
  try {
    const [rulings, center] = await Promise.all([adminApi.getContent('rules.rulings'), adminApi.getContent('rules.center')])
    entries.value = parseRulingDocument(rulings.draftValue)
    if (!entries.value.length && !rulings.draftValue.trim()) entries.value = createRulingsDraft()
    ruleCenterDraft.value = center.draftValue.trim() || JSON.stringify(createRuleCenterDraft(), null, 2)
  }
  catch (error) { notice(error instanceof Error ? error.message : '裁定草稿读取失败') }
  finally { busy.value = false }
}
async function save(show = true) {
  try {
    JSON.parse(ruleCenterDraft.value)
    await Promise.all([
      adminApi.saveContentDraft('rules.rulings', JSON.stringify({ entries: entries.value }, null, 2)),
      adminApi.saveContentDraft('rules.center', ruleCenterDraft.value),
    ])
    if (show) notice('规则中心与裁定草稿已保存；玩家只会看到审核后正式发布的快照')
    return true
  } catch (error) { notice(error instanceof Error ? error.message : '规则草稿保存失败；请检查规则中心资料是否为有效 JSON'); return false }
}
async function preview() { if (await save(false)) try { const result = await adminApi.previewContent(['rules.center', 'rules.rulings']); notice(`发布预览完成：${result.items.filter(item => item.wouldChange).length} 项将更新；未写入玩家端`) } catch (error) { notice(error instanceof Error ? error.message : '发布预览失败') } }
async function publish() { if (await save(false)) try { await adminApi.publishContent(['rules.center', 'rules.rulings']); notice('规则中心和已审核裁定已正式发布到玩家端') } catch (error) { notice(error instanceof Error ? error.message : '裁定发布失败') } }
onMounted(load)
</script>

<template>
  <section class="ruling-admin">
    <header><div><small>RULE CENTER REVIEW</small><h3>规则中心与 FAQ 裁定库</h3><p>全部资料先作为后台草稿维护。公开页只读取正式发布快照；原始 FAQ、待确认项和被替代项绝不会随草稿出现在玩家端。</p></div><div class="actions"><button @click="load">{{ busy ? '读取中…' : '刷新' }}</button><button v-if="hasPermission('admin.content.draft')" @click="addRuling()">＋ 新建裁定</button><button v-if="hasPermission('admin.content.draft')" @click="save()">保存草稿</button><button @click="preview">预览</button><button v-if="hasPermission('admin.content.publish')" class="publish" @click="publish">正式发布</button></div></header>
    <details class="rule-center-document"><summary>规则中心资料草稿（核心规则、快速入门、术语、赛事与版本记录）</summary><p>首次打开会载入维护种子，仍需管理员核对、保存草稿并正式发布。此处内容在发布前不会被玩家读取。</p><textarea v-model="ruleCenterDraft" rows="18" spellcheck="false" aria-label="规则中心资料 JSON 草稿"></textarea></details>
    <div class="review-summary"><b>原始来源复核：{{ reviewCount }} / {{ LEGACY_FAQ_SOURCES.length }}</b><span>已确认的用户／设计者裁定优先于旧表。发现冲突时先标记、再改写；不能从卡效实现或其他游戏术语反推答案。</span></div>
    <details class="source-inbox"><summary>原始 FAQ 复核收件箱（{{ LEGACY_FAQ_SOURCES.length }} 条）</summary><input v-model="sourceQuery" placeholder="搜索原始问题或关键词"><article v-for="source in visibleSources" :key="source.id" :data-state="source.reviewState"><div><small>{{ source.id }} · {{ source.type }} · {{ sourceStates[source.reviewState] }}</small><b>{{ source.question }}</b><p>{{ source.answer }}</p><em v-if="source.note">{{ source.note }}</em></div><button v-if="source.reviewState === 'pending-review' || source.reviewState === 'conflict-flagged'" @click="startFromSource(source.id)">据此新建草稿</button></article></details>
    <p v-if="selectedSource" class="selected-source">最近选用来源：{{ selectedSource }}</p>
    <article v-for="(item, index) in entries" :key="`${item.id}-${index}`" class="ruling-editor"><header><b>裁定草稿 {{ index + 1 }}</b><button class="danger" @click="removeRuling(index)">移除</button></header><div class="form-grid"><label>稳定 ID<input v-model.trim="item.id" maxlength="100"></label><label>状态<select v-model="item.status"><option value="pending">待复核</option><option value="published">已发布</option><option value="superseded">已替代</option></select></label><label>范围<select v-model="item.scope"><option value="general">通用规则</option><option value="card">单卡裁定</option><option value="errata">勘误</option><option value="construction">构筑</option><option value="tournament">赛事</option></select></label><label>来源类型<select v-model="item.sourceKind"><option value="rulebook">规则书</option><option value="official-faq">官方 FAQ</option><option value="user-ruling">用户确认裁定</option><option value="designer-ruling">设计者确认裁定</option></select></label><label>分类<input v-model.trim="item.category" maxlength="100"></label><label>记录日期<input v-model="item.recordedAt" type="date"></label><label>生效日期（可留空）<input v-model="item.effectiveAt" type="date"></label><label>来源说明<input v-model.trim="item.sourceRef" maxlength="300"></label><label class="wide">问题<input v-model.trim="item.question" maxlength="500" placeholder="以完整、可检索的问题描述填写"></label><label class="wide">规范裁定<textarea v-model.trim="item.answer" rows="5" maxlength="8000" placeholder="使用十二军团术语，先给出明确结论，再说明必要条件与结果。"></textarea></label><label>卡号（逗号分隔，可留空）<input :value="join(item.cardIds)" @change="normalizeList(item, 'cardIds', ($event.target as HTMLInputElement).value)"></label><label>产品（逗号分隔，可留空）<input :value="join(item.productIds)" @change="normalizeList(item, 'productIds', ($event.target as HTMLInputElement).value)"></label><label>标签（逗号分隔）<input :value="join(item.tags)" @change="normalizeList(item, 'tags', ($event.target as HTMLInputElement).value)"></label><label>原始来源 ID（逗号分隔）<input :value="join(item.sourceIds)" @change="normalizeList(item, 'sourceIds', ($event.target as HTMLInputElement).value)"></label><label class="wide">替代的旧条目 ID（逗号分隔；如 LEGACY-FAQ-47）<input :value="join(item.supersedes)" @change="normalizeList(item, 'supersedes', ($event.target as HTMLInputElement).value)"></label></div></article>
    <p v-if="!entries.length" class="empty">尚无后台裁定草稿。请从原始 FAQ 收件箱开始逐条改写，或新建一条已有确认来源的裁定。</p>
  </section>
</template>

<style scoped>
.ruling-admin{padding:22px;border:1px solid #35424a;background:#0e161d}.ruling-admin>header,.ruling-editor>header{display:flex;align-items:center;justify-content:space-between;gap:20px}.ruling-admin>header{margin:-22px -22px 20px;padding:22px;border-bottom:1px solid #35424a;background:#101821}.ruling-admin small{color:#55c6cd;font:900 13px monospace;letter-spacing:.15em}.ruling-admin h3{margin:6px 0}.ruling-admin p{color:#98a4a9;font-size:14px;line-height:1.65}.actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:8px}.ruling-admin button,.ruling-admin input,.ruling-admin textarea,.ruling-admin select{box-sizing:border-box;min-height:40px;padding:9px 11px;border:1px solid #4a5860;background:#070d12;color:#fff;font-size:14px}.actions .publish{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.review-summary{display:flex;gap:14px;padding:14px;border-left:3px solid #d1ad54;background:#171811;color:#c9c4b6;font-size:14px;line-height:1.7}.review-summary b{flex:0 0 auto;color:#efd170}.source-inbox{margin-top:16px}.source-inbox summary{cursor:pointer;color:#d8bc69;font-weight:900}.source-inbox>input{width:100%;margin:12px 0}.source-inbox article{display:flex;justify-content:space-between;gap:14px;margin-top:8px;padding:13px;border:1px solid #35424a;background:#091016}.source-inbox article[data-state="conflict-flagged"]{border-color:#a46145;background:#241713}.source-inbox article[data-state="replaced"]{opacity:.65}.source-inbox article div{display:grid;gap:6px}.source-inbox article small{color:#d7bc69;letter-spacing:0}.source-inbox article b{color:#f1f3ef}.source-inbox article p{margin:0;white-space:pre-line}.source-inbox article em{color:#e2b37e;font-size:13px;font-style:normal}.source-inbox article button{height:max-content;flex:0 0 auto}.selected-source{color:#d8bc69!important}.ruling-editor{margin-top:14px;padding:16px;border:1px solid #35424a;background:#091016}.ruling-editor>header{padding-bottom:12px;border-bottom:1px solid #303c43}.danger{border-color:#81434c!important;background:#281117!important;color:#eea6ae!important}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;margin-top:14px}.form-grid label{display:grid;gap:6px;color:#c0c8ca;font-size:13px;font-weight:800}.form-grid input,.form-grid textarea,.form-grid select{width:100%}.form-grid .wide{grid-column:1/-1}.empty{padding:32px;text-align:center}@media(max-width:760px){.ruling-admin>header,.ruling-editor>header,.source-inbox article{align-items:flex-start;flex-direction:column}.actions{justify-content:flex-start}.form-grid{grid-template-columns:1fr}}
.rule-center-document{margin:16px 0;padding:14px;border:1px solid #3d4a52;background:#091016}.rule-center-document summary{cursor:pointer;color:#d8bc69;font-weight:900}.rule-center-document p{margin:10px 0}.rule-center-document textarea{width:100%;resize:vertical;font:12px/1.55 Consolas,'Courier New',monospace}
</style>
