<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  adminApi,
  hasPermission,
  type AtomicCardEffect,
  type EffectPresentationStyle,
  type EffectWorkbenchDraft,
  type EffectWorkbenchErrata,
  type EffectWorkbenchView,
} from '@/l12/platform'

const props = defineProps<{ effect: AtomicCardEffect }>()
const emit = defineEmits<{ notice: [message: string] }>()

const workbench = ref<EffectWorkbenchView | null>(null)
const draft = ref<EffectWorkbenchDraft | null>(null)
const styles = ref<EffectPresentationStyle[]>([])
const includedProducts = ref('')
const errataProducts = ref<Record<string, string>>({})
const reason = ref('')
const busy = ref('')
const showHistory = ref(false)

const canWrite = computed(() => hasPermission('admin.effects.review'))
const livePreviews = computed(() => {
  if (!workbench.value || !draft.value) return []
  const scene = draft.value.scenes[0]
  return workbench.value.previews.map(item => item.channel === 'archive'
    ? { ...item, text: draft.value!.effectText, styleId: draft.value!.styleId, publicLevel: draft.value!.publicLevel }
    : item.channel === 'button' || !scene ? item
      : { ...item, text: scene.text, styleId: scene.styleId, publicLevel: scene.publicLevel })
})
const statusLabel = computed(() => ({
  source: '权威源定义', draft: '草稿', validated: '结构校验通过', reviewed: '人工复核完成',
  published: '已发布', 'needs-development': '待开发',
}[workbench.value?.status ?? 'source'] ?? workbench.value?.status))

function cloneDraft(source: EffectWorkbenchDraft) {
  draft.value = JSON.parse(JSON.stringify(source)) as EffectWorkbenchDraft
  includedProducts.value = source.includedProducts.join('\n')
  errataProducts.value = Object.fromEntries(source.errata.map(item => [item.id, item.products.join('\n')]))
}

function accept(result: EffectWorkbenchView) {
  workbench.value = result
  cloneDraft(result.draft)
}

async function load() {
  busy.value = 'load'
  try {
    const [result, styleOptions] = await Promise.all([
      adminApi.effectWorkbench(props.effect.cardId),
      styles.value.length ? Promise.resolve(styles.value) : adminApi.effectWorkbenchStyles(),
    ])
    styles.value = styleOptions
    accept(result)
  } catch (error) {
    emit('notice', error instanceof Error ? error.message : '卡效工作台读取失败')
  } finally { busy.value = '' }
}

function normalizeDraft(): EffectWorkbenchDraft {
  if (!draft.value) throw new Error('卡效草稿尚未载入')
  return {
    ...draft.value,
    includedProducts: includedProducts.value.split(/[\n,，]/).map(item => item.trim()).filter(Boolean),
    errata: draft.value.errata.map(item => ({
      ...item,
      products: (errataProducts.value[item.id] ?? '').split(/[\n,，]/).map(value => value.trim()).filter(Boolean),
    })),
  }
}

async function save() {
  if (!workbench.value) return
  busy.value = 'save'
  try {
    accept(await adminApi.saveEffectWorkbench(props.effect.cardId, normalizeDraft(), workbench.value.version, reason.value))
    emit('notice', `${props.effect.name} 的卡效草稿已保存`)
  } catch (error) { emit('notice', error instanceof Error ? error.message : '卡效草稿保存失败') }
  finally { busy.value = '' }
}

async function action(kind: 'validate' | 'review' | 'publish') {
  if (!workbench.value) return
  busy.value = kind
  try {
    const result = kind === 'validate'
      ? await adminApi.validateEffectWorkbench(props.effect.cardId, workbench.value.version)
      : kind === 'review'
        ? await adminApi.reviewEffectWorkbench(props.effect.cardId, workbench.value.version, reason.value)
        : await adminApi.publishEffectWorkbench(props.effect.cardId, workbench.value.version, reason.value)
    accept(result)
    emit('notice', kind === 'validate' ? '结构校验完成' : kind === 'review' ? '人工复核已记录' : '卡效版本已发布；新建对局将使用本次呈现文本')
  } catch (error) { emit('notice', error instanceof Error ? error.message : '卡效工作流操作失败') }
  finally { busy.value = '' }
}

async function rollback(versionId: string) {
  if (!workbench.value) return
  busy.value = `rollback:${versionId}`
  try {
    accept(await adminApi.rollbackEffectWorkbench(props.effect.cardId, workbench.value.version, versionId, reason.value))
    emit('notice', '已生成并发布一个新的回退版本，历史版本未被覆盖')
  } catch (error) { emit('notice', error instanceof Error ? error.message : '卡效版本回退失败') }
  finally { busy.value = '' }
}

function addErrata() {
  if (!draft.value) return
  const id = `errata-${Date.now().toString(36)}`
  const entry: EffectWorkbenchErrata = { id, previousText: '', correctedText: '', reason: '', effectiveVersion: '', products: [] }
  draft.value.errata.push(entry)
  errataProducts.value[id] = includedProducts.value
}

function removeErrata(id: string) {
  if (!draft.value) return
  draft.value.errata = draft.value.errata.filter(item => item.id !== id)
  delete errataProducts.value[id]
}

function sceneLabel(sceneId: string) {
  return props.effect.abilities.flatMap(item => item.presentations ?? []).find(item => item.sceneId === sceneId)?.label ?? sceneId
}

function styleName(styleId: string) { return styles.value.find(item => item.id === styleId)?.name ?? styleId }
function publicLevelName(level: string) {
  return ({ public: '双方公开', controller: '操作者可见', owner: '持有者可见', hidden: '不公开' } as Record<string, string>)[level] ?? level
}

watch(() => props.effect.cardId, load, { immediate: true })
</script>

<template>
  <section class="effect-maintenance" data-ui-contract="effect-workbench">
    <header>
      <div><small>STRUCTURED EFFECT WORKBENCH</small><h3>卡效统一工作台</h3><p>卡文、勘误、产品、Cost/响应边界与全部呈现场景在同一版本中维护；原子身份和生命周期保持只读。</p></div>
      <span v-if="workbench" class="workbench-state" :data-status="workbench.status">v{{ workbench.version }} · {{ statusLabel }}</span>
    </header>
    <div v-if="busy === 'load'" class="workbench-empty">正在载入工作台…</div>
    <template v-else-if="workbench && draft">
      <nav class="workflow-rail" aria-label="卡效发布流程">
        <span :class="{ done: workbench.version > 0 }">1 草稿</span><i>→</i>
        <span :class="{ done: ['validated','reviewed','published'].includes(workbench.status) }">2 结构校验</span><i>→</i>
        <span class="done">3 场景预演</span><i>→</i>
        <span :class="{ done: ['reviewed','published'].includes(workbench.status) }">4 人工复核</span><i>→</i>
        <span :class="{ done: workbench.status === 'published' }">5 发布 / 回退</span>
      </nav>

      <section class="workbench-section metadata-editor">
        <header><div><b>卡牌资料与生效范围</b><small>换行会作为卡牌正文的一部分保存；收录产品每行一个。</small></div></header>
        <div class="metadata-grid">
          <label>所属产品<input v-model="draft.baseProduct" :disabled="!canWrite"/></label>
          <label>生效范围<select v-model="draft.effectiveScope" :disabled="!canWrite"><option value="new-matches">仅新建对局</option><option value="display-only">仅展示资料</option></select></label>
          <label>默认公开等级<select v-model="draft.publicLevel" :disabled="!canWrite"><option value="public">双方公开</option><option value="controller">操作者可见</option><option value="owner">持有者可见</option><option value="hidden">不公开</option></select></label>
          <label>收录产品<textarea v-model="includedProducts" rows="4" :disabled="!canWrite"/></label>
          <label class="effect-text-field">卡牌正文<textarea v-model="draft.effectText" rows="8" maxlength="8000" :disabled="!canWrite"/></label>
        </div>
      </section>

      <section class="workbench-section">
        <header><div><b>能力与效果段</b><small>按卡牌 → 能力 → 效果段显示；冒号前 Cost、目标、响应与分支来自现有原子结构，不在后台复制第二套规则。</small></div></header>
        <article v-for="(ability, index) in draft.abilities" :key="ability.abilityId" class="structured-ability">
          <header><b>ABILITY {{ index + 1 }} · {{ ability.trigger }}</b><span>{{ ability.optional ? '选发' : '按原文执行' }}</span></header>
          <dl><dt>Cost 边界</dt><dd>{{ ability.costText || '无独立 Cost' }}</dd><dt>结算段</dt><dd>{{ ability.resolutionText }}</dd><dt>目标</dt><dd>{{ ability.targetSummary }}</dd><dt>响应</dt><dd>{{ ability.responseBoundary }}</dd><dt>公开分支</dt><dd>{{ ability.branchSummary }}</dd></dl>
          <code>{{ ability.abilityId }} · {{ ability.structureHash.slice(0, 12) }}</code>
        </article>
      </section>

      <section class="workbench-section">
        <header><div><b>勘误记录</b><small>旧文、新文、原因、生效版本和影响产品一起进入不可覆盖的发布版本。</small></div><button :disabled="!canWrite" @click="addErrata">新增勘误</button></header>
        <div v-if="!draft.errata.length" class="workbench-empty">当前没有勘误记录。</div>
        <article v-for="entry in draft.errata" :key="entry.id" class="errata-editor">
          <label>旧文<textarea v-model="entry.previousText" rows="4" :disabled="!canWrite"/></label>
          <label>新文<textarea v-model="entry.correctedText" rows="4" :disabled="!canWrite"/></label>
          <label>原因<input v-model="entry.reason" :disabled="!canWrite"/></label>
          <label>生效版本<input v-model="entry.effectiveVersion" placeholder="例如 2026.09" :disabled="!canWrite"/></label>
          <label>影响产品<textarea v-model="errataProducts[entry.id]" rows="3" :disabled="!canWrite"/></label>
          <button class="danger" :disabled="!canWrite" @click="removeErrata(entry.id)">删除本条</button>
        </article>
      </section>

      <section class="workbench-section style-section">
        <header><div><b>呈现样式</b><small>每个选项都附带桌面/移动图例；样式只改变呈现，不改变原子执行。</small></div></header>
        <div class="style-options">
          <label v-for="option in styles" :key="option.id" class="style-option" :class="{ selected: draft.styleId === option.id }" :data-tone="option.legendTone">
            <input v-model="draft.styleId" type="radio" :value="option.id" :disabled="!canWrite"/>
            <span><b>{{ option.name }}</b><small>{{ option.applicableScenes }}</small></span>
            <p>{{ option.description }}</p>
            <figure><strong>{{ option.legendTitle }}</strong><span>{{ option.legendBody }}</span></figure>
            <dl><dt>桌面</dt><dd>{{ option.desktopPreview }}</dd><dt>移动</dt><dd>{{ option.mobilePreview }}</dd></dl>
          </label>
        </div>
      </section>

      <section v-if="draft.scenes.length" class="workbench-section">
        <header><div><b>按钮 / 弹框 / 动画 / 日志 / 回放文本</b><small>同一场景文本投影到全部渠道，避免界面各写一份；占位符仍受服务端白名单校验。</small></div></header>
        <article v-for="scene in draft.scenes" :key="scene.sceneId" class="scene-draft">
          <header><b>{{ sceneLabel(scene.sceneId) }}</b><code>{{ scene.sceneId }}</code></header>
          <textarea v-model="scene.text" rows="4" maxlength="2000" :disabled="!canWrite"/>
          <div><label>样式<select v-model="scene.styleId" :disabled="!canWrite"><option v-for="option in styles" :key="option.id" :value="option.id">{{ option.name }}</option></select></label><label>公开等级<select v-model="scene.publicLevel" :disabled="!canWrite"><option value="public">双方公开</option><option value="controller">操作者可见</option><option value="owner">持有者可见</option><option value="hidden">不公开</option></select></label></div>
        </article>
      </section>

      <section class="workbench-section preview-section" data-ui-contract="effect-workbench-preview">
        <header><div><b>场景预演</b><small>同时核对图鉴、按钮、弹框、动效、日志与回放，不把内部枚举直接交给管理员猜测。</small></div></header>
        <div class="preview-grid">
          <article v-for="preview in livePreviews" :key="preview.channel" :data-channel="preview.channel">
            <small>{{ preview.label }}</small><strong>{{ preview.text || '无文本' }}</strong><span>{{ styleName(preview.styleId) }} · {{ publicLevelName(preview.publicLevel) }}</span>
          </article>
        </div>
      </section>

      <section class="workbench-validation" :data-valid="workbench.validation.valid">
        <header><b>{{ workbench.validation.valid ? '当前结构可进入发布流程' : workbench.validation.requiresDevelopment ? '需要效果一致性开发' : '结构校验未通过' }}</b><span>源结构 {{ workbench.sourceStructureHash.slice(0, 12) }}</span></header>
        <ul v-if="workbench.validation.errors.length"><li v-for="message in workbench.validation.errors" :key="message">{{ message }}</li></ul>
        <ul v-if="workbench.validation.warnings.length" class="warnings"><li v-for="message in workbench.validation.warnings" :key="message">{{ message }}</li></ul>
      </section>

      <footer class="workbench-actions">
        <label>本次变更原因<input v-model="reason" placeholder="用于审计与回退记录" :disabled="!canWrite"/></label>
        <button :disabled="!canWrite || !!busy" @click="save">{{ busy === 'save' ? '保存中…' : '保存草稿' }}</button>
        <button :disabled="!canWrite || workbench.version === 0 || !!busy" @click="action('validate')">结构校验</button>
        <button :disabled="!canWrite || workbench.status !== 'validated' || !!busy" @click="action('review')">确认人工复核</button>
        <button class="publish" :disabled="!canWrite || workbench.status !== 'reviewed' || !reason.trim() || !!busy" @click="action('publish')">差异确认并发布</button>
      </footer>

      <section class="workbench-history">
        <button class="history-toggle" @click="showHistory = !showHistory">{{ showHistory ? '收起' : '查看' }}发布与回退记录（{{ workbench.history.length }}）</button>
        <div v-if="showHistory">
          <article v-for="version in workbench.history" :key="version.id">
            <span><b>v{{ version.version }}</b><code>{{ version.id }}</code><small>{{ new Date(version.publishedAt).toLocaleString() }} · {{ version.publishedBy }}</small></span>
            <p>{{ version.reason || '未填写原因' }}</p>
            <button :disabled="!canWrite || !reason.trim() || version.id === workbench.publishedVersionId || !!busy" @click="rollback(version.id)">{{ busy === `rollback:${version.id}` ? '回退中…' : version.id === workbench.publishedVersionId ? '当前版本' : '生成回退版本' }}</button>
          </article>
        </div>
      </section>
    </template>
  </section>
</template>

<style scoped>
.effect-maintenance{display:grid;gap:12px;margin:14px 0;padding:14px;border:1px solid #75622f;background:#0b1217}.effect-maintenance>header,.workbench-section>header,.structured-ability>header,.scene-draft>header,.workbench-validation>header{display:flex;align-items:flex-start;justify-content:space-between;gap:12px}.effect-maintenance h3{margin:3px 0;font-size:20px}.effect-maintenance p{margin:4px 0}.workbench-state{flex:none;padding:6px 8px;border:1px solid #50616a;color:#b8c4c7;font-size:13px;font-weight:900}.workbench-state[data-status="published"]{border-color:#28775b;background:#0b241a;color:#7ee2b8}.workbench-state[data-status="needs-development"]{border-color:#934957;background:#2c1117;color:#f39aa6}.workflow-rail{display:flex;align-items:center;justify-content:center;gap:7px;overflow-x:auto;padding:10px;border:1px solid #34434b;background:#080e12;color:#748289;font-size:12px;white-space:nowrap}.workflow-rail span{padding:5px 7px;border:1px solid #34434b}.workflow-rail span.done{border-color:#2f725c;color:#83ddb9}.workflow-rail i{color:#8c7437;font-style:normal}.workbench-section{display:grid;gap:10px;padding:12px;border:1px solid #34434b;background:#0c151b}.workbench-section>header{padding-bottom:9px;border-bottom:1px solid #2f3c43}.workbench-section>header small{display:block;margin-top:4px;color:#87959a!important;letter-spacing:0!important}.workbench-section button,.workbench-actions button,.workbench-history button{border:1px solid #53616a;background:#0a1116;color:#dbe1df;padding:8px 10px;font-weight:900}.metadata-grid{display:grid;grid-template-columns:1fr 1fr 1fr;gap:9px}.metadata-grid label,.errata-editor label,.scene-draft label,.workbench-actions label{display:grid;gap:5px;color:#aeb9ba;font-size:13px;font-weight:900}.metadata-grid input,.metadata-grid select,.metadata-grid textarea,.errata-editor input,.errata-editor textarea,.scene-draft textarea,.scene-draft select,.workbench-actions input{box-sizing:border-box;width:100%;border:1px solid #506069;background:#071015;color:#fff;padding:8px;font:700 13px 'Microsoft YaHei';resize:vertical}.metadata-grid label:nth-child(4){grid-row:2/span 2}.metadata-grid .effect-text-field{grid-column:2/-1;grid-row:2/span 2}.structured-ability,.scene-draft,.errata-editor{display:grid;gap:8px;padding:11px;border:1px solid #31434c;background:#091218}.structured-ability>header span{padding:3px 6px;border:1px solid #6a5930;color:#e8cb72;font-size:12px}.structured-ability dl{display:grid;grid-template-columns:90px minmax(0,1fr);gap:6px 10px;margin:0;font-size:13px}.structured-ability dt{color:#77878e}.structured-ability dd{margin:0;color:#c8d0cf;white-space:pre-wrap}.structured-ability code,.scene-draft code{color:#71858d;font-size:12px;overflow-wrap:anywhere}.errata-editor{grid-template-columns:1fr 1fr}.errata-editor label:nth-child(n+3){grid-column:auto}.errata-editor .danger{align-self:end;border-color:#85414b;background:#291016;color:#ef929e}.style-options{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:9px}.style-option{display:grid;grid-template-columns:auto 1fr;gap:6px 9px;padding:10px;border:1px solid #394951;background:#091218;cursor:pointer}.style-option.selected{border-color:#d4b557;background:#1b180e}.style-option>span{display:flex;flex-direction:column}.style-option>span small{color:#839299!important;letter-spacing:0!important}.style-option>p,.style-option>figure,.style-option>dl{grid-column:1/-1}.style-option figure{display:flex;min-height:66px;flex-direction:column;justify-content:center;gap:5px;margin:0;padding:9px;border-left:3px solid #4cbed1;background:#0d2027}.style-option[data-tone="gold"] figure{border-color:#d6b74f;background:#211b0d}.style-option[data-tone="violet"] figure{border-color:#9a70ce;background:#1c1328}.style-option[data-tone="green"] figure{border-color:#55bd8f;background:#0c2119}.style-option figure span{white-space:pre-wrap;color:#cdd6d5;font-size:13px}.style-option dl{display:grid;grid-template-columns:46px 1fr;gap:4px 8px;margin:0;font-size:12px}.style-option dt{color:#73828a}.style-option dd{margin:0}.scene-draft>div{display:grid;grid-template-columns:1fr 1fr;gap:8px}.preview-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px}.preview-grid article{display:flex;min-height:90px;flex-direction:column;justify-content:space-between;gap:8px;padding:10px;border:1px solid #3b4a51;background:#081116}.preview-grid small{color:#d4b85d!important}.preview-grid strong{white-space:pre-wrap;font-size:13px;line-height:1.55}.preview-grid span{color:#718087;font-size:12px}.preview-grid article[data-channel="dialog"],.preview-grid article[data-channel="animation"]{border-color:#3d7682;background:#0b1e24}.workbench-validation{padding:11px;border:1px solid #86424c;background:#291116;color:#f0a1aa}.workbench-validation[data-valid="true"]{border-color:#2d7359;background:#0c231a;color:#8be1bb}.workbench-validation header span{font:700 12px monospace}.workbench-validation ul{margin:8px 0 0;padding-left:20px;font-size:13px}.workbench-validation .warnings{color:#e0c878}.workbench-actions{display:grid;grid-template-columns:minmax(220px,1fr) repeat(4,auto);align-items:end;gap:7px}.workbench-actions .publish{border-color:#347b60;background:#0d251c;color:#7fe0b9}.workbench-actions button:disabled,.workbench-section button:disabled,.workbench-history button:disabled{opacity:.4}.workbench-history{display:grid;gap:8px}.history-toggle{justify-self:start}.workbench-history article{display:grid;grid-template-columns:minmax(220px,1fr) minmax(180px,1fr) auto;align-items:center;gap:10px;padding:9px;border-top:1px solid #34434b}.workbench-history article span{display:flex;min-width:0;flex-direction:column}.workbench-history article code{overflow:hidden;color:#d6b95e;text-overflow:ellipsis}.workbench-history article small{color:#738188!important;letter-spacing:0!important}.workbench-history article p{overflow-wrap:anywhere}.workbench-empty{padding:12px;color:#77868c;text-align:center}
@media(max-width:900px){.metadata-grid,.style-options,.preview-grid{grid-template-columns:1fr 1fr}.metadata-grid label:nth-child(4),.metadata-grid .effect-text-field{grid-column:auto;grid-row:auto}.workbench-actions{grid-template-columns:1fr 1fr}.workbench-actions label{grid-column:1/-1}.workbench-history article{grid-template-columns:1fr auto}.workbench-history article p{grid-column:1/-1}}@media(max-width:600px){.metadata-grid,.style-options,.preview-grid,.errata-editor,.scene-draft>div,.workbench-actions{grid-template-columns:1fr}.workflow-rail{justify-content:flex-start}.workbench-history article{grid-template-columns:1fr}.workbench-history article button{justify-self:start}}
</style>
