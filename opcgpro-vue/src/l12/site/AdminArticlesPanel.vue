<script setup lang="ts">
import PagedCollection from './PagedCollection.vue'
import { computed, onMounted, ref, watch } from 'vue'
import { adminApi, hasPermission, type Article, type ArticleDraft, type ArticleRevision, type ModianImportBatchResult, type ModianImportPreview, type SiteCategory, type SiteContentKind, type SiteMedia } from '@/l12/platform'
import ArticleDocumentEditor from './ArticleDocumentEditor.vue'
import ArticleContentRenderer from './ArticleContentRenderer.vue'
import MediaUploadField from './MediaUploadField.vue'

type EditableArticle = Partial<Article> & ArticleDraft

const props = withDefaults(defineProps<{ kind?: SiteContentKind }>(), { kind: 'news' })
const emit = defineEmits<{ notice: [value: string] }>()
const kindCopy = {
  news: { en: 'NEWSROOM', title: '资讯中心', singular: '资讯', summary: '摘要', body: '正文', link: '相关链接' },
  video: { en: 'VIDEO', title: '最新视频', singular: '视频', summary: '', body: '', link: '整卡跳转链接' },
  product: { en: 'PRODUCTS', title: '产品上新', singular: '商品', summary: '商品摘要', body: '商品说明', link: '商品详情链接' },
} as const
const copy = computed(() => kindCopy[props.kind])
const statuses = [
  { id: '', name: '全部状态' }, { id: 'draft', name: '草稿' }, { id: 'published', name: '已发布' },
  { id: 'scheduled', name: '定时发布' }, { id: 'withdrawn', name: '已停用' }, { id: 'archived', name: '已归档' },
]
const articles = ref<Article[]>([])
const categories = ref<SiteCategory[]>([])
const media = ref<SiteMedia[]>([])
const selected = ref<EditableArticle | null>(null)
const revisions = ref<ArticleRevision[]>([])
const search = ref('')
const status = ref('')
const category = ref('')
const busy = ref(false)
const notice = ref('')
const preview = ref(false)
const modianBusy = ref(false)
const modianPreview = ref<ModianImportPreview | null>(null)
const modianResult = ref<ModianImportBatchResult | null>(null)
const modianCategoryId = ref('')
const modianSelectedIds = ref<string[]>([])
const modianReimportIds = ref<string[]>([])
const modianOverwriteIds = ref<string[]>([])

const activeCategories = computed(() => categories.value.filter(item => item.active))
const coverMedia = computed(() => media.value.filter(item => item.kind === props.kind))
const selectedIsSaved = computed(() => Boolean(selected.value?.id))
const selectedPreview = computed(() => {
  const uploaded = media.value.find(item => item.id === selected.value?.mediaAssetId)
  return uploaded?.thumbnailUrl || selected.value?.coverUrl || ''
})
const statusLabel = (value?: string) => statuses.find(item => item.id === value)?.name || '未保存'

function emptyArticle(): EditableArticle {
  const first = activeCategories.value[0]
  return {
    title: '', summary: '', body: '', category: first?.name || '', categoryId: first?.id,
    coverUrl: '', mediaAssetId: '', link: '', slug: '', pinned: false, publishAt: '', videoAuthorName: '',
    kind: props.kind, sortOrder: 0,
  }
}
function dateTimeLocal(value?: string) {
  if (!value) return ''
  const date = new Date(value)
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}
function selectArticle(article: Article) {
  selected.value = { ...article, publishAt: dateTimeLocal(article.publishAt) }
  preview.value = false
  void loadRevisions(article.id)
}
function createArticle() { selected.value = emptyArticle(); revisions.value = []; preview.value = false }
function showNotice(value: string) { notice.value = value; emit('notice', value) }
function categoryChanged() {
  if (!selected.value) return
  const match = categories.value.find(item => item.id === selected.value?.categoryId)
  if (match) selected.value.category = match.name
}
function mediaUploaded(value: SiteMedia) {
  media.value = [value, ...media.value.filter(item => item.id !== value.id)]
  if (selected.value) selected.value.mediaAssetId = value.id
}
function inlineMediaUploaded(value: SiteMedia) {
  media.value = [value, ...media.value.filter(item => item.id !== value.id)]
}

async function load() {
  busy.value = true; notice.value = ''
  try {
    const [items, nextCategories, nextMedia] = await Promise.all([
      adminApi.articles({ kind: props.kind, status: status.value, category: category.value, search: search.value.trim() }),
      adminApi.siteCategories(props.kind), adminApi.siteMedia(),
    ])
    articles.value = items; categories.value = nextCategories; media.value = nextMedia
    if (!nextCategories.some(item => item.id === modianCategoryId.value && item.active))
      modianCategoryId.value = nextCategories.find(item => item.active)?.id || ''
    if (selected.value?.id) {
      const fresh = items.find(item => item.id === selected.value?.id)
      if (fresh) selectArticle(fresh)
    }
  } catch (error) { showNotice(error instanceof Error ? error.message : `${copy.value.title}加载失败`) }
  finally { busy.value = false }
}
async function save() {
  if (!selected.value) return
  const categoryRow = categories.value.find(item => item.id === selected.value?.categoryId)
  if (!categoryRow) { showNotice('请选择后台分类管理中存在的分类'); return }
  busy.value = true; notice.value = ''
  try {
    const saved = await adminApi.saveArticle({
      ...selected.value, kind: props.kind, category: categoryRow.name, categoryId: categoryRow.id,
      summary: props.kind === 'video' ? '' : selected.value.summary,
      body: props.kind === 'video' ? '' : selected.value.body,
      coverUrl: '', publishAt: selected.value.publishAt ? new Date(selected.value.publishAt).toISOString() : undefined,
    })
    selected.value = { ...saved, publishAt: dateTimeLocal(saved.publishAt) }
    showNotice(`${copy.value.singular}草稿已保存，线上内容未改变`)
    await load(); await loadRevisions(saved.id)
  } catch (error) { showNotice(error instanceof Error ? error.message : '稿件保存失败') }
  finally { busy.value = false }
}
async function mutate(action: 'publish' | 'withdraw' | 'archive' | 'restore') {
  if (!selected.value?.id) { showNotice('请先保存稿件'); return }
  if (action === 'archive' && !window.confirm('归档后内容不再公开展示，确认继续？')) return
  busy.value = true; notice.value = ''
  try {
    const id = selected.value.id
    const updated = action === 'publish' ? await adminApi.publishArticle(id)
      : action === 'withdraw' ? await adminApi.withdrawArticle(id)
        : action === 'archive' ? await adminApi.archiveArticle(id) : await adminApi.restoreArticle(id)
    selected.value = { ...updated, publishAt: dateTimeLocal(updated.publishAt) }
    showNotice(action === 'publish' ? (updated.status === 'scheduled' ? '已安排定时发布' : '已正式发布')
      : action === 'withdraw' ? '内容已停用' : action === 'archive' ? '稿件已归档' : '稿件已恢复为草稿')
    await load(); await loadRevisions(id)
  } catch (error) { showNotice(error instanceof Error ? error.message : '稿件状态更新失败') }
  finally { busy.value = false }
}
async function loadRevisions(id: string) {
  try { revisions.value = await adminApi.articleRevisions(id) } catch { revisions.value = [] }
}
async function restoreRevision(revision: number) {
  if (!selected.value?.id || !window.confirm(`将版本 ${revision} 恢复为新的草稿？`)) return
  busy.value = true
  try {
    const restored = await adminApi.restoreArticleRevision(selected.value.id, revision)
    selected.value = { ...restored, publishAt: dateTimeLocal(restored.publishAt) }
    showNotice(`版本 ${revision} 已恢复为草稿，尚未影响线上内容`)
    await load(); await loadRevisions(restored.id)
  } catch (error) { showNotice(error instanceof Error ? error.message : '历史版本恢复失败') }
  finally { busy.value = false }
}

const modianStateLabel = (state: string) => ({
  new: '待导入', imported: '已导入', 'local-edited': '草稿有本地编辑', 'remote-changed': '来源有更新',
  'remote-changed-local-edited': '来源及本地草稿均有变化', 'check-failed': '变更检查失败',
}[state] || state)

async function previewModianUpdates(preserveResult = false) {
  modianBusy.value = true
  if (!preserveResult) modianResult.value = null
  try {
    const result = await adminApi.previewModianUpdates()
    modianPreview.value = result
    modianSelectedIds.value = result.items.filter(item => item.state === 'new').map(item => item.updateId)
    modianReimportIds.value = []
    modianOverwriteIds.value = []
    if (!preserveResult) showNotice(`已检查 ${result.total} 条固定项目更新；默认仅选择尚未导入的条目`)
  } catch (error) { showNotice(error instanceof Error ? error.message : '项目更新检查失败') }
  finally { modianBusy.value = false }
}

function selectAllNewModianUpdates() {
  modianSelectedIds.value = modianPreview.value?.items.filter(item => item.state === 'new').map(item => item.updateId) || []
  modianReimportIds.value = []
  modianOverwriteIds.value = []
}

async function importModianUpdates() {
  if (!modianCategoryId.value) { showNotice('请先选择导入草稿使用的资讯分类'); return }
  if (!modianSelectedIds.value.length) { showNotice('请至少选择一条项目更新'); return }
  const reimport = modianReimportIds.value.filter(id => modianSelectedIds.value.includes(id))
  const overwrite = modianOverwriteIds.value.filter(id => reimport.includes(id))
  if (reimport.length && !window.confirm(`其中 ${reimport.length} 条允许在来源变化时重新导入；操作只更新草稿，不会自动发布。确认继续？`)) return
  if (overwrite.length && !window.confirm(`其中 ${overwrite.length} 条将覆盖本地草稿编辑；已发布快照仍保持不变。确认覆盖？`)) return
  modianBusy.value = true; modianResult.value = null
  try {
    const key = globalThis.crypto?.randomUUID?.() ?? `modian-${Date.now()}-${Math.random().toString(16).slice(2)}`
    const result = await adminApi.importModianUpdates({
      updateIds: modianSelectedIds.value, categoryId: modianCategoryId.value,
      reimportUpdateIds: reimport, overwriteLocalChangesIds: overwrite,
    }, key)
    modianResult.value = result
    showNotice(`草稿同步完成：新建 ${result.created}，更新 ${result.updated}，未变化 ${result.unchanged}，需显式重导 ${result.needsReimport}，冲突 ${result.conflicted}，失败 ${result.failed}；没有自动发布`)
    await load()
    await previewModianUpdates(true)
  } catch (error) { showNotice(error instanceof Error ? error.message : '项目更新同步失败') }
  finally { modianBusy.value = false }
}

watch(() => props.kind, () => { selected.value = null; void load() })
onMounted(load)
</script>

<template>
  <section class="article-workbench">
    <header class="article-page-head">
      <div><small>{{ copy.en }}</small><h2>{{ copy.title }}</h2><p>复用统一稿件、草稿、发布、停用、审计和历史恢复链路；封面只能从后台素材库上传或选择。</p></div>
      <button v-if="hasPermission('admin.content.draft')" class="new-button" @click="createArticle">＋ 新建{{ copy.singular }}</button>
    </header>
    <section v-if="props.kind === 'news' && hasPermission('admin.content.draft')" class="modian-import-panel" data-ui-contract="modian-draft-import">
      <header>
        <div><b>固定项目更新导入</b><small>先检查并选择条目，再生成本站资讯草稿。正文图片会复制进素材库并自动生成 16:9 封面；绝不自动发布，也不附加来源链接。</small></div>
        <button :disabled="modianBusy" @click="previewModianUpdates()">{{ modianBusy ? '处理中…' : '检查项目更新' }}</button>
      </header>
      <div class="modian-import-controls">
        <label>草稿分类<select v-model="modianCategoryId"><option v-for="item in activeCategories" :key="item.id" :value="item.id">{{ item.name }}</option></select></label>
        <button v-if="modianPreview" :disabled="modianBusy" @click="selectAllNewModianUpdates">仅选择全部新增</button>
        <button v-if="modianPreview" class="modian-import-run" :disabled="modianBusy || !modianSelectedIds.length" @click="importModianUpdates">同步所选为草稿</button>
      </div>
      <div v-if="modianPreview" class="modian-update-list">
        <PagedCollection :items="modianPreview.items" v-slot="{ items: paged11264 }"><article v-for="item in paged11264" :key="item.updateId" class="modian-update-row" :data-state="item.state">
          <input v-model="modianSelectedIds" type="checkbox" :value="item.updateId" :disabled="item.state === 'check-failed'" :aria-labelledby="`modian-title-${item.updateId}`">
          <span :id="`modian-title-${item.updateId}`"><b>{{ item.title }}</b><small>第 {{ item.sequence || '—' }} 次更新 · {{ new Date(item.originalPublishedAt).toLocaleString() }} · {{ modianStateLabel(item.state) }}</small><em v-if="item.checkMessage">{{ item.checkMessage }}</em></span>
          <label v-if="item.state === 'remote-changed' || item.state === 'remote-changed-local-edited'" class="modian-explicit"><input v-model="modianReimportIds" type="checkbox" :value="item.updateId">允许重导</label>
          <label v-if="item.state === 'remote-changed-local-edited'" class="modian-explicit danger"><input v-model="modianOverwriteIds" type="checkbox" :value="item.updateId" :disabled="!modianReimportIds.includes(item.updateId)">覆盖本地草稿</label>
        </article></PagedCollection>
      </div>
      <div v-if="modianResult" class="modian-result-list"><PagedCollection :items="modianResult.items.filter(row => row.status !== 'unchanged')" :page-size="5" v-slot="{ items: nestedPage }"><p v-for="item in nestedPage" :key="item.updateId"><b>{{ item.status }}</b> {{ item.message }}<small v-if="item.warnings?.length">{{ item.warnings.join('；') }}</small></p></PagedCollection></div>
    </section>
    <div class="article-layout">
      <aside class="article-list">
        <div class="article-filters">
          <input v-model="search" :placeholder="props.kind === 'video' ? '搜索标题或作者名' : '搜索标题、摘要或正文'" @keyup.enter="load">
          <select v-model="status" @change="load"><option v-for="item in statuses" :key="item.id" :value="item.id">{{ item.name }}</option></select>
          <select v-model="category" @change="load"><option value="">全部分类</option><option v-for="item in categories" :key="item.id" :value="item.id">{{ item.name }}{{ item.active ? '' : '（停用）' }}</option></select>
          <button @click="load">刷新</button>
        </div>
        <PagedCollection :items="articles" v-slot="{ items: paged13295 }"><button v-for="article in paged13295" :key="article.id" class="article-list-row" :class="{ active: selected?.id === article.id }" @click="selectArticle(article)">
          <img v-if="article.coverUrl" :src="article.coverUrl" :alt="article.title">
          <span><small>{{ article.category }} · {{ statusLabel(article.status) }}</small><b>{{ article.title || '未命名稿件' }}</b><em v-if="article.hasUnpublishedChanges">有未发布修改</em><time>{{ article.publishAt ? new Date(article.publishAt).toLocaleString() : '尚未设置发布时间' }}</time></span>
        </button></PagedCollection>
        <div v-if="!articles.length" class="article-empty">{{ busy ? '正在加载…' : `暂无${copy.singular}稿件` }}</div>
      </aside>

      <main class="article-editor">
        <div v-if="!selected" class="article-empty editor-empty">选择一篇稿件，或新建{{ copy.singular }}。</div>
        <template v-else>
          <header class="editor-head"><div><small>{{ selected.id ? selected.id : 'NEW DRAFT' }}</small><h3>{{ selected.title || `未命名${copy.singular}` }}</h3></div><span :data-status="selected.status || 'draft'">{{ statusLabel(selected.status) }}</span></header>
          <div class="editor-grid">
            <label class="wide">标题<input v-model="selected.title" maxlength="180"></label>
            <label>动态分类<select v-model="selected.categoryId" @change="categoryChanged"><option v-for="item in categories" :key="item.id" :value="item.id" :disabled="!item.active">{{ item.name }}{{ item.active ? '' : '（已停用）' }}</option></select></label>
            <label v-if="props.kind !== 'video'">链接标识<input v-model="selected.slug" maxlength="100" placeholder="留空自动生成"></label>
            <label>发布时间<input v-model="selected.publishAt" type="datetime-local"><small>留空则发布时立即公开；未来时间到点自动公开。公开排序只使用置顶与此时间。</small></label>
            <label v-if="props.kind === 'video'" class="wide">作者名（新视频发布必填）<input v-model="selected.videoAuthorName" maxlength="80" placeholder="视频作者或频道名"><small>最多 80 字符，不接受换行或控制字符；旧视频可保留为空。</small></label>
            <label v-if="props.kind !== 'video'" class="wide">{{ copy.summary }}<textarea v-model="selected.summary" rows="3" maxlength="600"></textarea></label>
            <ArticleDocumentEditor v-if="props.kind === 'news'" v-model="selected.body" :media="media" @media-uploaded="inlineMediaUploaded" @notice="showNotice"/>
            <label v-else-if="props.kind === 'product'" class="wide">{{ copy.body }}<textarea v-model="selected.body" rows="12" maxlength="100000"></textarea></label>
            <label class="wide">{{ copy.link }}<input v-model="selected.link" maxlength="2000" :placeholder="props.kind === 'video' ? '必填：站内 /path 或 https://；前台整卡直接跳转' : '站内 /path 或 https://' "></label>
            <label v-if="props.kind !== 'video'" class="check"><input v-model="selected.pinned" type="checkbox"> 首页置顶</label>
            <label v-else class="check"><input v-model="selected.pinned" type="checkbox"> 置顶视频</label>
          </div>

          <section class="cover-manager">
            <header><div><b>{{ props.kind === 'product' ? '商品图' : '封面素材' }}</b><small>禁止粘贴图片地址；选择已上传素材或上传新原图。视频/商品发布时封面必填。</small></div><select v-model="selected.mediaAssetId"><option value="">不使用封面（仅草稿）</option><option v-for="item in coverMedia" :key="item.id" :value="item.id">{{ item.altText || item.contentHash.slice(0, 12) }} · 引用 {{ item.referenceCount }}</option></select></header>
            <MediaUploadField v-model="selected.mediaAssetId" :kind="props.kind" :preview-url="selectedPreview" :initial-alt="selected.title" @uploaded="mediaUploaded" @notice="showNotice"/>
          </section>

          <div class="editor-actions">
            <button v-if="hasPermission('admin.content.draft')" :disabled="busy" @click="save">保存草稿</button>
            <button :class="{ active: preview }" @click="preview = !preview">{{ preview ? '关闭预览' : '预览' }}</button>
            <button v-if="selectedIsSaved && hasPermission('admin.content.publish')" class="publish" :disabled="busy" @click="mutate('publish')">发布 / 安排发布</button>
            <button v-if="selectedIsSaved && (selected.status === 'published' || selected.status === 'scheduled')" class="withdraw" :disabled="busy" @click="mutate('withdraw')">停用</button>
            <button v-if="selectedIsSaved && selected.status === 'archived'" @click="mutate('restore')">恢复草稿</button>
            <button v-else-if="selectedIsSaved" class="archive" @click="mutate('archive')">归档</button>
          </div>
          <article v-if="preview" class="article-preview"><img v-if="selectedPreview" :src="selectedPreview" :alt="selected.title"><small>{{ selected.category }} · {{ selected.publishAt ? new Date(selected.publishAt).toLocaleString() : '发布时立即公开' }}</small><h2>{{ selected.title || '未填写标题' }}</h2><b v-if="props.kind === 'video' && selected.videoAuthorName" class="video-author">作者：{{ selected.videoAuthorName }}</b><p v-if="props.kind !== 'video'">{{ selected.summary }}</p><ArticleContentRenderer v-if="props.kind === 'news'" :body="selected.body" :media="media"/><div v-else-if="props.kind === 'product'">{{ selected.body }}</div><a v-if="selected.link" :href="selected.link">{{ props.kind === 'video' ? '点击视频卡片将直接跳转到此链接' : '相关链接' }}</a></article>
          <p v-if="notice" class="article-notice">{{ notice }}</p>
          <details v-if="selectedIsSaved" class="revision-list"><summary>历史版本（{{ revisions.length }}）</summary><PagedCollection :items="revisions" v-slot="{ items: paged18709 }"><article v-for="revision in paged18709" :key="revision.revision"><span><b>v{{ revision.revision }} · {{ revision.action }}</b><small>{{ revision.actor }} · {{ new Date(revision.createdAt).toLocaleString() }}</small></span><button @click="restoreRevision(revision.revision)">恢复为草稿</button></article></PagedCollection></details>
        </template>
      </main>
    </div>
  </section>
</template>

<style scoped>
.article-workbench{min-width:0;font-size:14px;line-height:1.55}.article-page-head{display:flex;align-items:flex-end;justify-content:space-between;gap:24px;margin-bottom:18px;padding:26px 28px;border:1px solid #35424a;background:#101821}.article-page-head small{color:#55c6cd;font:900 14px monospace;letter-spacing:.18em}.article-page-head h2{margin:7px 0;font-size:28px}.article-page-head p{margin:0;color:#9aa5a9;font-size:14px;line-height:1.65}.new-button,.article-filters button,.editor-actions button,.revision-list button,.modian-import-panel button{min-height:42px;padding:10px 15px;border:1px solid #4d5b63;background:#0a1117;color:#fff;font-size:14px;font-weight:900}.new-button{border-color:#d5b65e!important;color:#efd37b!important}.modian-import-panel{margin-bottom:18px;padding:20px;border:1px solid #3b5960;background:#0c171d}.modian-import-panel>header{display:flex;align-items:center;justify-content:space-between;gap:18px}.modian-import-panel>header div{display:grid;gap:5px}.modian-import-panel>header small{color:#9aa8ad}.modian-import-controls{display:flex;align-items:end;gap:10px;margin-top:16px}.modian-import-controls label{display:grid;min-width:240px;gap:6px;font-weight:900}.modian-import-controls select{min-height:42px;padding:8px;border:1px solid #48565e;background:#070d12;color:#fff}.modian-import-run{border-color:#2f785e!important;color:#7fe0b9!important}.modian-update-list{max-height:420px;margin-top:14px;overflow:auto;border:1px solid #344149}.modian-update-row{display:grid;grid-template-columns:auto minmax(0,1fr) auto auto;align-items:center;gap:12px;padding:12px;border-bottom:1px solid #2d3940}.modian-update-row>span{display:grid;min-width:0;gap:3px}.modian-update-row>span>b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.modian-update-row small{color:#8e9ba0}.modian-update-row em{color:#ef8994;font-style:normal}.modian-update-row[data-state^="remote-changed"]{background:#221d0d}.modian-explicit{display:flex;align-items:center;gap:5px;color:#e2c36c;font-weight:900}.modian-explicit.danger{color:#ef8994}.modian-result-list{margin-top:12px}.modian-result-list p{display:flex;gap:8px;margin:5px 0;color:#b9c3c6}.modian-result-list small{color:#d8bd70}.article-layout{display:grid;grid-template-columns:minmax(320px,.72fr) minmax(620px,1.6fr);min-height:700px;border:1px solid #35424a;background:#0d151c}.article-list{border-right:1px solid #35424a}.article-filters{display:grid;grid-template-columns:1fr 1fr;gap:11px;padding:18px;border-bottom:1px solid #35424a}.article-filters input{grid-column:1/-1}.article-filters input,.article-filters select,.cover-manager select,.editor-grid input,.editor-grid select,.editor-grid textarea{box-sizing:border-box;width:100%;min-height:42px;padding:10px 12px;border:1px solid #48565e;background:#070d12;color:#fff;font-size:14px}.article-list-row{display:grid;width:100%;grid-template-columns:92px 1fr;gap:14px;padding:16px;border:0;border-bottom:1px solid #2d3940;background:transparent;color:#fff;font-size:14px;text-align:left}.article-list-row:hover,.article-list-row.active{background:#172229}.article-list-row.active{box-shadow:inset 3px 0 #d4b55d}.article-list-row>img{width:92px;height:69px;object-fit:cover}.article-list-row>span{display:flex;min-width:0;flex-direction:column;gap:6px}.article-list-row small,.article-list-row time{color:#8e9ba0;font-size:14px}.article-list-row b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.article-list-row em{color:#e2c36c;font-size:14px;font-style:normal}.article-editor{min-width:0;padding:28px}.editor-head,.cover-manager>header{display:flex;align-items:center;justify-content:space-between;gap:18px;padding-bottom:16px;border-bottom:1px solid #344149}.editor-head small{color:#54c5cc;font:900 14px monospace}.editor-head h3{margin:7px 0;font-size:21px}.editor-head>span{padding:7px 11px;border:1px solid #59656c;color:#aeb6b9;font-size:14px;font-weight:900}.editor-head>span[data-status="published"]{border-color:#2f745a;color:#7edfb4}.editor-head>span[data-status="scheduled"]{border-color:#8a6b30;color:#e8cd7a}.editor-grid{display:grid;grid-template-columns:1fr 1fr;gap:18px;margin-top:22px}.editor-grid label{display:grid;align-content:start;gap:8px;color:#c2c9cb;font-size:14px;font-weight:900}.editor-grid label>small{color:#8f9ba0;font-size:14px;font-weight:500;line-height:1.55}.editor-grid .wide{grid-column:1/-1}.editor-grid textarea{resize:vertical;line-height:1.7}.editor-grid .check{display:flex;align-items:center;gap:8px}.editor-grid .check input{width:auto;min-height:auto}.cover-manager{margin-top:22px;padding:18px;border:1px solid #3a4850;background:#0a1117}.cover-manager>header{margin-bottom:16px}.cover-manager>header div{display:flex;flex-direction:column;gap:5px}.cover-manager>header b{font-size:15px}.cover-manager>header small{color:#939fa4;font-size:14px;line-height:1.5}.cover-manager>header select{width:min(430px,52%)}.editor-actions{display:flex;flex-wrap:wrap;gap:10px;margin-top:22px}.editor-actions .publish{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.editor-actions .withdraw,.editor-actions .archive{border-color:#84424b;background:#291116;color:#ef8994}.article-preview{margin-top:22px;padding:clamp(24px,4vw,48px);border:1px solid #6d5b2c;background:#f1ecdc;color:#1d2426}.article-preview>img{width:100%;max-height:420px;object-fit:cover}.article-preview small{display:block;margin-top:15px;color:#9b2632;font-size:14px}.article-preview p{color:#62686a}.article-preview>div{white-space:pre-wrap;line-height:1.8}.article-preview>a{display:inline-block;margin-top:16px;color:#217c85}.video-author{display:block;margin:8px 0;color:#667174}.article-notice{padding:12px 14px;border-left:3px solid #d2b35f;background:#261e0c;color:#edd37b!important;font-size:14px}.revision-list{margin-top:22px}.revision-list summary{cursor:pointer;color:#d9bd69;font-size:14px;font-weight:900}.revision-list article{display:flex;align-items:center;justify-content:space-between;padding:12px;border-bottom:1px solid #303c43}.revision-list span{display:flex;flex-direction:column;gap:4px}.revision-list small{color:#8d999e;font-size:14px}.article-empty{padding:42px;color:#8c999f;font-size:14px;text-align:center}.editor-empty{display:grid;min-height:500px;place-items:center}@media(max-width:1180px){.article-layout{grid-template-columns:1fr}.article-list{max-height:420px;overflow:auto;border-right:0;border-bottom:1px solid #35424a}.modian-update-row{grid-template-columns:auto minmax(0,1fr)}}@media(max-width:700px){.article-page-head,.modian-import-panel>header,.modian-import-controls{align-items:stretch;flex-direction:column}.editor-grid{grid-template-columns:1fr}.editor-grid .wide{grid-column:auto}.cover-manager>header{align-items:stretch;flex-direction:column}.cover-manager>header select{width:100%}.article-editor{padding:16px}}
</style>
